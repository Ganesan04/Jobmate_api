using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Messaging;

/// <summary>POST /conversations — opens (or reuses) the thread with a participant.</summary>
public sealed record StartConversationRequest
{
    public Guid ParticipantId { get; init; }

    public string? ParticipantName { get; init; }

    public Guid? JobId { get; init; }

    public string? JobContext { get; init; }
}

/// <summary>POST /conversations/{id}/messages.</summary>
public sealed record SendMessageRequest
{
    public string Text { get; init; } = string.Empty;

    public MessageType MessageType { get; init; } = MessageType.Text;
}

/// <summary>GET /conversations/{id}/messages.</summary>
public sealed record GetMessagesRequest : Domain.Common.PageRequest
{
    /// <summary>Return the newest page and order ascending, which is what a chat view needs.</summary>
    public bool NewestFirst { get; init; }
}

/// <summary>GET /conversations filters.</summary>
public sealed record GetConversationsRequest : Domain.Common.PageRequest
{
    /// <summary>Include threads the caller has cleared from their list. Default is to hide them.</summary>
    public bool IncludeArchived { get; init; }
}

/// <summary>POST /conversations/{id}/archive — hide a thread, or put an archived one back.</summary>
public sealed record ArchiveConversationRequest
{
    public bool Archived { get; init; } = true;
}
