using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// Chat rules. A thread is always between two people, and — when it is scoped to a job —
/// only between people who were actually involved in that job, so the conversation list
/// can never become a way to reach a stranger.
///
/// The unread badge is kept as a counter on the participant row rather than derived by
/// counting messages, because the chat list is read far more often than messages are
/// sent, and a count over messages per row is exactly the query that gets slow first.
/// <see cref="MarkRead"/> is therefore the single place the counter is maintained.
/// </summary>
public static class MessagingRules
{
    /// <summary>Matches the <c>message_text</c> column width.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>Matches the <c>last_message_preview</c> column width.</summary>
    public const int MaxPreviewLength = 200;

    /// <summary>Shown as the thread's context when it is not attached to a job.</summary>
    public const string GeneralContext = "General enquiry";

    /// <summary>
    /// Opens a thread between the caller and one other person, optionally scoped to a
    /// job both of them were involved in.
    ///
    /// The returned conversation has no participants yet; the caller joins them, because
    /// who is in the thread is decided by the request rather than by this rule.
    /// </summary>
    /// <exception cref="ForbiddenException">
    /// The caller is messaging themselves, or one of them was not involved in the job.
    /// </exception>
    public static Conversation Start(
        User caller,
        User participant,
        Job? job,
        DateTime now)
    {
        if (caller.Id == participant.Id)
        {
            throw new BusinessRuleException("You cannot start a conversation with yourself.", "SelfConversation");
        }

        if (job is not null && !CanMessageAbout(job, caller.Id, participant.Id))
        {
            throw new ForbiddenException("You can only message people involved in the same job.");
        }

        return new Conversation
        {
            JobId = job?.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// True when two people can hold a conversation about a job: the employer, and a
    /// worker who has either applied for it or been booked onto it.
    ///
    /// An application counts as well as a booking because the whole point of a chat is to
    /// talk before the booking exists — that is when a worker asks whether the shift is
    /// still going ahead.
    /// </summary>
    public static bool CanMessageAbout(Job job, Guid callerId, Guid participantId)
    {
        if (job.GiverId == callerId && job.GiverId == participantId)
        {
            // The employer with themselves, already rejected by Start.
            return false;
        }

        return Involves(job, callerId) && Involves(job, participantId);
    }

    /// <summary>True when a user has any connection at all to a job.</summary>
    public static bool Involves(Job job, Guid userId) =>
        job.GiverId == userId ||
        job.Applications.Any(a => a.WorkerId == userId) ||
        job.Assignments.Any(a => a.WorkerId == userId);

    /// <summary>Adds a user to a thread with a zero unread count and no read marker.</summary>
    public static ConversationParticipant Join(Conversation conversation, Guid userId, DateTime now) =>
        new()
        {
            ConversationId = conversation.Id,
            UserId = userId,
            UnreadCount = 0,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Builds one chat bubble. System messages are written by the API, not by a user, so
    /// they are the only kind allowed to be empty — "Booked for Warehouse load" carries
    /// no prose, and a typed message must say something.
    /// </summary>
    public static Message Send(
        Conversation conversation,
        Guid senderId,
        string text,
        MessageType type,
        DateTime now)
    {
        var body = text?.Trim() ?? string.Empty;

        if (type == MessageType.Text)
        {
            if (body.Length == 0)
            {
                throw new BusinessRuleException("Type a message before sending.", "MessageEmpty");
            }

            if (body.Length > MaxMessageLength)
            {
                throw new BusinessRuleException(
                    $"A message cannot be longer than {MaxMessageLength} characters.",
                    "MessageTooLong");
            }
        }

        return new Message
        {
            ConversationId = conversation.Id,
            SenderId = senderId,
            MessageText = body,
            MessageType = type,
            IsRead = false,
            SentAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Moves the thread's summary onto a message that has just been sent. The chat list is
    /// sorted by this, so it has to be updated in the same operation as the message or a
    /// new conversation would not float to the top.
    /// </summary>
    public static void Touch(Conversation conversation, Message message)
    {
        conversation.LastMessageAt = message.SentAt;
        conversation.LastMessagePreview = Preview(message.MessageText, message.MessageType);
        conversation.LastMessageSenderId = message.SenderId;
        conversation.UpdatedAt = message.SentAt;
    }

    /// <summary>
    /// Clears the unread counter for one participant and returns how many messages it had.
    ///
    /// The returned count is what the caller's own row went from, so the chat screen can
    /// show the badge it just cleared without a second query. Marking an already-read
    /// thread read is a no-op that returns zero, which is what makes a retried open of the
    /// same screen harmless.
    /// </summary>
    public static int MarkRead(
        ConversationParticipant participant,
        IReadOnlyCollection<Message> unreadMessages,
        DateTime now)
    {
        if (participant.UnreadCount == 0 && unreadMessages.Count == 0)
        {
            return 0;
        }

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
            message.ReadAt = now;
        }

        var cleared = participant.UnreadCount;

        participant.UnreadCount = 0;
        participant.LastReadAt = now;
        participant.UpdatedAt = now;

        return cleared;
    }

    /// <summary>
    /// The one-line summary shown in the conversation list.
    ///
    /// A system message has no readable text of its own — it is generated by the API — so
    /// it is labelled rather than shown as an empty row.
    /// </summary>
    public static string Preview(string? text, MessageType type)
    {
        if (type == MessageType.System)
        {
            var label = string.IsNullOrWhiteSpace(text) ? "System update" : text.Trim();

            return label.Length <= MaxPreviewLength
                ? label
                : string.Concat(label.AsSpan(0, MaxPreviewLength - 1), "…");
        }

        var body = (text ?? string.Empty).Trim();

        return body.Length <= MaxPreviewLength
            ? body
            : string.Concat(body.AsSpan(0, MaxPreviewLength - 1), "…");
    }

    /// <summary>
    /// The context line for a thread: the job's title when it is scoped to one, and a
    /// fixed label when it is not.
    /// </summary>
    public static string ContextOf(Job? job) =>
        string.IsNullOrWhiteSpace(job?.Title) ? GeneralContext : job!.Title;
}
