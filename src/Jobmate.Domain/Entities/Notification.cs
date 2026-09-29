using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// An in-app notification. The app renders <c>title</c> / <c>body</c> and uses
/// <see cref="ReferenceType"/> + <see cref="ReferenceId"/> as the deep link
/// (Flutter <c>targetRoute</c> / <c>targetId</c>).
/// </summary>
public sealed class Notification : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public NotificationType Type { get; set; } = NotificationType.System;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public NotificationReferenceType ReferenceType { get; set; } = NotificationReferenceType.None;

    public Guid? ReferenceId { get; set; }

    /// <summary>
    /// Flutter route to open when the row is tapped, e.g. <c>/job-details</c> or
    /// <c>/application-details</c>. Mirrors the app's <c>AppRoutes</c> names.
    /// </summary>
    public string? Route { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    /// <summary>Set once a push notification for this row has been dispatched.</summary>
    public DateTime? PushDispatchedAt { get; set; }
}

/// <summary>
/// A push token for one of a user's devices. Tokens are stored so
/// <c>IPushNotificationService</c> can target a device later; the business layer
/// only depends on the abstraction, never on Firebase.
/// </summary>
public sealed class UserDevice : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Provider token (FCM registration token today).</summary>
    public string Token { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public string? DeviceName { get; set; }

    public string? AppVersion { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeenAt { get; set; }
}
