using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Messaging;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Messaging.Commands;

/// <summary>
/// Opens the chat between the caller and one other person. POST /api/v1/conversations.
///
/// Opening the same thread twice returns the existing one rather than a second copy: the
/// app's "Message" button on a job card and the chat list's "new message" both lead here,
/// and a worker who taps the job card twice should not end up with two threads to the
/// same employer.
/// </summary>
public sealed record StartConversationCommand(StartConversationRequest Request) : IRequest<ConversationDto>;

public sealed class StartConversationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<StartConversationCommand, ConversationDto>
{
    public async Task<ConversationDto> Handle(
        StartConversationCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var participant = await db.Users
            .AsNoTracking()
            .Include(u => u.WorkerProfile)
            .FirstOrDefaultAsync(u => u.Id == request.Request.ParticipantId, cancellationToken)
            ?? throw new NotFoundException("That account no longer exists.");

        var job = request.Request.JobId is { } jobId
            ? await LoadJobAsync(jobId, cancellationToken)
            : null;

        var caller = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Account not found.");

        // The rule knows who may talk to whom; the reuse check only runs once it has
        // agreed the pair is allowed, so a refused pair is reported as a rule failure
        // rather than as "you already have a conversation".
        var existing = await FindExistingAsync(callerId, participant.Id, job?.Id, cancellationToken);

        Conversation conversation;
        ConversationParticipant callerParticipant;

        if (existing is not null)
        {
            conversation = existing;
            conversation.Job = job;

            // Archived means "cleared from my list", not "closed": reusing the thread has
            // to put it back, otherwise a user who archived a chat could never message
            // that person again about the same job.
            var existingRow = existing.Participants.First(p => p.UserId == callerId);

            existingRow.IsArchived = false;

            callerParticipant = existingRow;
        }
        else
        {
            conversation = MessagingRules.Start(caller, participant, job, now);
            conversation.Job = job;

            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);

            callerParticipant = MessagingRules.Join(conversation, callerId, now);

            db.ConversationParticipants.AddRange(
                callerParticipant,
                MessagingRules.Join(conversation, participant.Id, now));
        }

        await db.SaveChangesAsync(cancellationToken);

        return conversation.ToDto(callerParticipant, participant);
    }

    /// <summary>
    /// The job a thread is being opened about, with the two collections the "was this
    /// person involved" rule reads.
    ///
    /// Tracked rather than projected, because the conversation takes it as its navigation
    /// property: handing EF Core an untracked instance would make it insert the job a
    /// second time.
    /// </summary>
    private Task<Job?> LoadJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.Jobs
            .Include(j => j.Applications)
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

    /// <summary>
    /// The thread between exactly these two people about exactly this job, or null.
    ///
    /// Scoped by job id rather than by "any thread with the same two people" so a worker
    /// keeps a separate thread per job: the questions about Tuesday's shift and about
    /// Friday's are not the same conversation.
    ///
    /// Tracked, not projected, because the caller may have to un-archive the row it finds.
    /// </summary>
    private async Task<Conversation?> FindExistingAsync(
        Guid callerId,
        Guid participantId,
        Guid? jobId,
        CancellationToken cancellationToken) =>
        await db.Conversations
            .Include(c => c.Participants)
            .Where(c => c.JobId == jobId)
            .FirstOrDefaultAsync(
                c => c.Participants.Count == 2
                     && c.Participants.Any(p => p.UserId == callerId)
                     && c.Participants.Any(p => p.UserId == participantId),
                cancellationToken);
}

/// <summary>
/// Posts one message into a thread. POST /api/v1/conversations/{id}/messages.
///
/// The unread counter is maintained here rather than being derived on read, so
/// <c>GET /notifications/unread-count</c> — which already sums
/// <see cref="ConversationParticipant.UnreadCount"/> — is correct without another query.
/// </summary>
public sealed record SendMessageCommand(Guid ConversationId, SendMessageRequest Request) : IRequest<MessageDto>;

public sealed class SendMessageCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<SendMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        await ConversationAccess.RequireMemberAsync(db, request.ConversationId, callerId, cancellationToken);

        var conversation = await db.Conversations
            .Include(c => c.Job)
            .Include(c => c.Participants)
            .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken)
            ?? throw new NotFoundException("Conversation not found.");

        // Membership was already established above; this picks the row out of the loaded
        // participants rather than re-querying for it.
        var callerRow = conversation.Participants.First(p => p.UserId == callerId);

        var message = MessagingRules.Send(
            conversation,
            callerId,
            request.Request.Text,
            request.Request.MessageType,
            now);

        MessagingRules.Touch(conversation, message);

        // Sending into a thread the user had cleared puts it back in their list.
        callerRow.IsArchived = false;

        // The sender has by definition read their own message, and a conversation that
        // starts with a message must not show as unread to the person who sent it.
        callerRow.UnreadCount = 0;
        callerRow.LastReadAt = now;

        foreach (var participant in conversation.Participants.Where(p => p.UserId != callerId))
        {
            participant.UnreadCount++;
            participant.IsArchived = false;
        }

        db.Messages.Add(message);

        await db.SaveChangesAsync(cancellationToken);

        // A new message is a notification like any other, so the recipient's bell works
        // even when they never open the chat list.
        var recipients = conversation.Participants
            .Where(p => p.UserId != callerId)
            .Select(p => p.UserId)
            .ToList();

        var sender = conversation.Participants
            .First(p => p.UserId == callerId)
            .User?.FullName;

        await notifications.SendManyAsync(
            recipients,
            NotificationType.ApplicationUpdate,
            sender is null ? "New message" : $"Message from {sender}",
            MessagingRules.Preview(message.MessageText, message.MessageType),
            NotificationReferenceType.Conversation,
            conversation.Id,
            NotificationRoutes.Chat,
            cancellationToken);

        return message.ToDto(callerId, sender);
    }
}

/// <summary>
/// Clears the unread badge for one thread. POST /api/v1/conversations/{id}/read.
///
/// Done as its own call rather than as a side effect of listing messages, because the app
/// clears the badge the moment the chat screen opens — which is before it has drawn the
/// messages, and whether it drew them or not should not decide the badge.
/// </summary>
public sealed record MarkConversationReadCommand(Guid ConversationId) : IRequest<int>;

public sealed class MarkConversationReadCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<MarkConversationReadCommand, int>
{
    public async Task<int> Handle(MarkConversationReadCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        await ConversationAccess.RequireMemberAsync(db, request.ConversationId, callerId, cancellationToken);

        var participant = await ConversationAccess.RequireRowAsync(
            db, request.ConversationId, callerId, cancellationToken);

        // Only the messages the badge is actually counting: everything sent since the last
        // read marker, and only from other people.
        var unread = await db.Messages
            .Where(m => m.ConversationId == request.ConversationId
                        && m.SenderId != callerId
                        && !m.IsRead)
            .ToListAsync(cancellationToken);

        var cleared = MessagingRules.MarkRead(participant, unread, now);

        await db.SaveChangesAsync(cancellationToken);

        return cleared;
    }
}

/// <summary>
/// Hides a thread from the caller's conversation list without deleting it.
/// POST /api/v1/conversations/{id}/archive.
///
/// Archive rather than delete: deleting a thread would delete the other participant's copy
/// of the conversation too, and would throw away the record a later support report might need.
/// </summary>
public sealed record ArchiveConversationCommand(Guid ConversationId, bool Archived) : IRequest<bool>;

public sealed class ArchiveConversationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<ArchiveConversationCommand, bool>
{
    public async Task<bool> Handle(ArchiveConversationCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        await ConversationAccess.RequireMemberAsync(db, request.ConversationId, callerId, cancellationToken);

        var participant = await ConversationAccess.RequireRowAsync(
            db, request.ConversationId, callerId, cancellationToken);

        participant.IsArchived = request.Archived;

        // An archived thread must also stop contributing to the badge, or it would be
        // invisible in the list while still holding a count the shell displays.
        if (request.Archived)
        {
            participant.UnreadCount = 0;
        }

        await db.SaveChangesAsync(cancellationToken);

        return participant.IsArchived;
    }
}
