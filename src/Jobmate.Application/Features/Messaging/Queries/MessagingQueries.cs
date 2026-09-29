using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Messaging;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Messaging.Queries;

/// <summary>
/// The caller's conversation list. GET /api/v1/conversations.
///
/// Driven from the participant row rather than from the conversation, because the list is
/// always "threads I am in" and that row is also where the unread counter lives — so one
/// read answers both the contents of the list and the badge beside it.
///
/// Ordered by the thread's last message so a new reply floats to the top, which is what a
/// chat list is for. Threads with nothing in them sort last rather than being hidden: a
/// freshly opened thread has to appear immediately, before anything is said in it.
/// </summary>
public sealed record GetConversationsQuery(GetConversationsRequest Request)
    : IRequest<IReadOnlyList<ConversationDto>>;

public sealed class GetConversationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetConversationsQuery, IReadOnlyList<ConversationDto>>
{
    public async Task<IReadOnlyList<ConversationDto>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var filter = request.Request;

        var mine = await db.ConversationParticipants
            .AsNoTracking()
            .Where(p => p.UserId == callerId && (filter.IncludeArchived || !p.IsArchived))
            .Include(p => p.Conversation)
            .ThenInclude(c => c!.Job)
            .OrderByDescending(p => p.Conversation!.LastMessageAt ?? DateTime.MinValue)
            .ThenByDescending(p => p.Conversation!.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        var counterparts = await ConversationCounterparts.LoadAsync(
            db,
            callerId,
            mine.Where(p => p.Conversation is not null).Select(p => p.ConversationId).ToList(),
            cancellationToken);

        return mine
            .Select(p => Project(p, counterparts))
            .ToList();
    }

    private static ConversationDto Project(
        ConversationParticipant participant,
        IReadOnlyDictionary<Guid, Counterpart> counterparts)
    {
        // The conversation is a required foreign key, so a row without one cannot exist;
        // the check keeps that a skipped row rather than a null reference.
        if (participant.Conversation is not { } conversation)
        {
            return ConversationDto.Missing;
        }

        var counterpart = counterparts.GetValueOrDefault(conversation.Id);

        return conversation.ToDto(participant, counterpart?.User, counterpart?.IsOnline ?? false);
    }
}

/// <summary>
/// One thread's messages. GET /api/v1/conversations/{id}/messages.
///
/// Returned oldest-first because a chat view reads top to bottom, and it pages from the
/// newest end so that "the last page" is the screen the user actually wants. The ordering
/// is applied after the skip rather than before it, so a thread that gained messages while
/// the user was reading cannot make an already-seen message disappear off the end.
/// </summary>
public sealed record GetMessagesQuery(Guid ConversationId, GetMessagesRequest Request)
    : IRequest<PagedResult<MessageDto>>;

public sealed class GetMessagesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetMessagesQuery, PagedResult<MessageDto>>
{
    public async Task<PagedResult<MessageDto>> Handle(
        GetMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var filter = request.Request;

        await ConversationAccess.RequireMemberAsync(db, request.ConversationId, callerId, cancellationToken);

        var total = await db.Messages.CountAsync(
            m => m.ConversationId == request.ConversationId,
            cancellationToken);

        var page = await db.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ConversationId == request.ConversationId)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        // The newest page is the one a chat screen wants, and it wants it oldest-first, so
        // the page is reversed after the fact rather than by asking the database for a
        // descending read.
        if (filter.NewestFirst)
        {
            page.Reverse();
        }

        var items = page
            .Select(m => m.ToDto(callerId, m.Sender?.FullName))
            .ToList();

        return PagedResult<MessageDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>
/// One thread's header. GET /api/v1/conversations/{id}.
///
/// Separate from the message list because the chat screen's title bar needs the other
/// person's name and the job context, and it needs them before — and independently of —
/// any page of messages.
/// </summary>
public sealed record GetConversationQuery(Guid ConversationId) : IRequest<ConversationDto>;

public sealed class GetConversationQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetConversationQuery, ConversationDto>
{
    public async Task<ConversationDto> Handle(GetConversationQuery request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        await ConversationAccess.RequireMemberAsync(db, request.ConversationId, callerId, cancellationToken);

        var mine = await db.ConversationParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == request.ConversationId && p.UserId == callerId)
            .Include(p => p.Conversation)
            .ThenInclude(c => c!.Job)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Conversation not found.");

        if (mine.Conversation is not { } conversation)
        {
            throw new NotFoundException("Conversation not found.");
        }

        var counterparts = await ConversationCounterparts.LoadAsync(
            db,
            callerId,
            [request.ConversationId],
            cancellationToken);

        return conversation.ToDto(
            mine,
            counterparts.GetValueOrDefault(conversation.Id)?.User,
            counterparts.GetValueOrDefault(conversation.Id)?.IsOnline ?? false);
    }
}

/// <summary>Who the other person in a thread is, and whether they are online right now.</summary>
/// <param name="User">The other member, as a loaded entity so the projection can use their name.</param>
/// <param name="IsOnline">From their worker profile. False for anyone without one, which is every non-worker.</param>
internal sealed record Counterpart(User User, bool IsOnline);

/// <summary>
/// Resolves "who is the other person in each of these threads".
///
/// Loaded with flat queries instead of by walking <c>Conversation.Participants.User</c>
/// as an <c>Include</c>. Each participant row points back at its conversation, so that
/// include path closes into a cycle, and EF Core rejects a cycle on a read-only query.
/// Two flat reads cost the same number of round trips for any page size, which is the
/// property that matters here — a chat list is small and read constantly.
/// </summary>
internal static class ConversationCounterparts
{
    /// <summary>
    /// The other participant of each thread, keyed by conversation id. A thread with only
    /// the caller in it — which the join rules prevent — simply has no entry, and the
    /// caller of this helper projects that as an unknown participant rather than failing.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, Counterpart>> LoadAsync(
        IAppDbContext db,
        Guid callerId,
        IReadOnlyCollection<Guid> conversationIds,
        CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return new Dictionary<Guid, Counterpart>();
        }

        var rows = await db.ConversationParticipants
            .AsNoTracking()
            .Where(p => conversationIds.Contains(p.ConversationId) && p.UserId != callerId)
            .Include(p => p.User)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new Dictionary<Guid, Counterpart>();
        }

        // Online status lives on the worker profile, which only workers have. One extra
        // read for the whole page rather than one per row.
        var userIds = rows.Select(p => p.UserId).Distinct().ToList();

        var online = await db.WorkerProfiles
            .AsNoTracking()
            .Where(w => userIds.Contains(w.UserId))
            .Select(w => new { w.UserId, w.IsOnline })
            .ToDictionaryAsync(w => w.UserId, w => w.IsOnline, cancellationToken);

        return rows
            .Where(p => p.User is not null)
            .GroupBy(p => p.ConversationId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var user = group.First().User!;

                    return new Counterpart(user, online.GetValueOrDefault(user.Id));
                });
    }
}
