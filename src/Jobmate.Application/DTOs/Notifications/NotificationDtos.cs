using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Notifications;

/// <summary>
/// Flutter <c>AppNotification</c>: id, type, title, body, createdAt, isRead,
/// targetRoute, targetId.
///
/// <c>body</c> is the notification text; the entity's own <c>Message</c> column is
/// projected onto it rather than being sent under both names.
/// </summary>
public sealed record NotificationDto
{
    public Guid Id { get; init; }

    public NotificationType Type { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Flutter <c>AppNotification.body</c>.</summary>
    public string Body { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public bool IsRead { get; init; }

    /// <summary>
    /// Named route to open when the row is tapped, e.g. <c>/job-details</c>. One of
    /// <c>NotificationRoutes</c>; the app resolves it by name.
    /// </summary>
    public string? TargetRoute { get; init; }

    /// <summary>Id the target screen needs, handed to it as the route argument.</summary>
    public string? TargetId { get; init; }

    public NotificationReferenceType ReferenceType { get; init; }
}

/// <summary>Unread badge count for the app shell.</summary>
public sealed record UnreadCountDto
{
    public int Notifications { get; init; }

    public int Messages { get; init; }
}

/// <summary>
/// Outcome of the employer tapping "remind workers": how many were actually reminded,
/// and the job they were reminded about.
/// </summary>
public sealed record ReminderResultDto(int RemindedCount, Guid JobId);

/// <summary>
/// One registered push device, as the settings screen lists them. The token itself is
/// never returned: it is a credential and the app only needs to know which devices exist.
/// </summary>
public sealed record DeviceDto
{
    public Guid Id { get; init; }

    public DevicePlatform Platform { get; init; }

    public string? DeviceName { get; init; }

    public string? AppVersion { get; init; }

    public bool IsActive { get; init; }

    public DateTime? LastSeenAt { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>Last eight characters of the token, enough for the user to recognise a device.</summary>
    public string TokenSuffix { get; init; } = string.Empty;
}
