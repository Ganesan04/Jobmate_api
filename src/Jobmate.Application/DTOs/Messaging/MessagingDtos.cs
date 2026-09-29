using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Messaging;

/// <summary>
/// Flutter <c>Conversation</c>: id, participantId, participantName, jobContext,
/// unread, isOnline, lastMessage, lastMessageAt.
/// </summary>
public sealed record ConversationDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    /// <summary>The other participant, from the caller's point of view.</summary>
    public Guid ParticipantId { get; init; }

    public string ParticipantName { get; init; } = string.Empty;

    public string JobContext { get; init; } = string.Empty;

    public int Unread { get; init; }

    public bool IsOnline { get; init; }

    public string LastMessage { get; init; } = string.Empty;

    public DateTime? LastMessageAt { get; init; }

    /// <summary>
    /// Stand-in for a participant row whose conversation could not be loaded. Present so a
    /// single unreadable row costs one line in the chat list rather than failing the whole
    /// screen; the caller sees a row with no id rather than a request error.
    /// </summary>
    public static ConversationDto Missing => new() { JobContext = "Conversation unavailable" };
}

/// <summary>Flutter <c>Message</c>: id, conversationId, text, sentAt, isMine, isRead.</summary>
public sealed record MessageDto
{
    public Guid Id { get; init; }

    public Guid ConversationId { get; init; }

    public string Text { get; init; } = string.Empty;

    public DateTime SentAt { get; init; }

    public Guid SenderId { get; init; }

    public string SenderName { get; init; } = string.Empty;

    /// <summary>True when the authenticated caller sent this message.</summary>
    public bool IsMine { get; init; }

    public bool IsRead { get; init; }

    public MessageType MessageType { get; init; }
}
