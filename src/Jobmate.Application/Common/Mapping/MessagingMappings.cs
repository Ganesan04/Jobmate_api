using Jobmate.Application.DTOs.Messaging;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Projections for the chat feature.
///
/// Both shapes are read from the caller's point of view, which is why they take the
/// caller id and the other participant rather than just the entity: a row in the
/// conversation list says "Maria, 2 unread" rather than listing both members, and a chat
/// bubble says <c>isMine</c> rather than carrying a sender id the app has to compare
/// against the signed-in user itself.
/// </summary>
public static class MessagingMappings
{
    /// <summary>
    /// Projects one thread for the chat list.
    /// </summary>
    /// <param name="participant">
    /// The caller's own membership row, which carries the unread counter.
    /// </param>
    /// <param name="counterpart">
    /// The other member of the thread. Null only for a thread whose counterpart row has
    /// gone, which the cascade rules should make impossible; it is tolerated so a single
    /// bad row cannot fail the whole list.
    /// </param>
    /// <param name="counterpartIsOnline">
    /// Passed separately rather than read off <c>counterpart.WorkerProfile</c>, because the
    /// counterpart is loaded with flat queries and its worker profile is not along for the
    /// ride. Only workers have one, so a counterpart without it is simply offline.
    /// </param>
    public static ConversationDto ToDto(
        this Conversation conversation,
        ConversationParticipant participant,
        User? counterpart,
        bool counterpartIsOnline = false) =>
        new()
        {
            Id = conversation.Id,
            JobId = conversation.JobId ?? Guid.Empty,
            ParticipantId = counterpart?.Id ?? Guid.Empty,
            ParticipantName = counterpart?.FullName ?? "Unknown",
            JobContext = MessagingRules.ContextOf(conversation.Job),
            Unread = participant.UnreadCount,
            IsOnline = counterpartIsOnline,
            LastMessage = conversation.LastMessagePreview ?? string.Empty,
            LastMessageAt = conversation.LastMessageAt
        };

    /// <summary>
    /// Projects one chat bubble. <paramref name="callerId"/> is what turns a sender id
    /// into the <c>isMine</c> flag the Flutter chat view aligns on.
    /// </summary>
    public static MessageDto ToDto(this Message message, Guid callerId, string? senderName) =>
        new()
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Text = message.MessageText,
            SentAt = message.SentAt,
            SenderId = message.SenderId,
            SenderName = senderName ?? string.Empty,
            IsMine = message.SenderId == callerId,
            IsRead = message.IsRead,
            MessageType = message.MessageType
        };
}
