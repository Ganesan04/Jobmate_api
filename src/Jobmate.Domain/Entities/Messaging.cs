using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A chat thread, optionally scoped to a job. Uniqueness on (JobId, low participant
/// pair) prevents duplicate threads between the same two people for the same job.
/// </summary>
public sealed class Conversation : AuditableEntity
{
    public Guid? JobId { get; set; }

    public Job? Job { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public string? LastMessagePreview { get; set; }

    public Guid LastMessageSenderId { get; set; }

    public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public string JobContext => Job?.Title ?? "General enquiry";
}

/// <summary>Join row making a user a member of a conversation.</summary>
public sealed class ConversationParticipant : AuditableEntity
{
    public Guid ConversationId { get; set; }

    public Conversation? Conversation { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int UnreadCount { get; set; }

    public DateTime? LastReadAt { get; set; }

    public bool IsArchived { get; set; }
}

/// <summary>
/// A single chat bubble. <see cref="IsRead"/> is per participant via
/// <see cref="ConversationParticipant.LastReadAt"/>; a message newer than that
/// timestamp counts towards the unread badge.
/// </summary>
public sealed class Message : AuditableEntity
{
    public Guid ConversationId { get; set; }

    public Conversation? Conversation { get; set; }

    public Guid SenderId { get; set; }

    public User? Sender { get; set; }

    public string MessageText { get; set; } = string.Empty;

    public MessageType MessageType { get; set; } = MessageType.Text;

    public bool IsRead { get; set; }

    public DateTime SentAt { get; set; }

    public DateTime? ReadAt { get; set; }
}
