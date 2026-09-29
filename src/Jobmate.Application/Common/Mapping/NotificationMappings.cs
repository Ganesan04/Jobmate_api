using Jobmate.Application.DTOs.Notifications;
using Jobmate.Domain.Entities;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Projections for the notification and device-token features.
/// </summary>
public static class NotificationMappings
{
    /// <summary>
    /// Projects a stored notification onto the app's <c>AppNotification</c>.
    ///
    /// The route and the id are kept apart because the app resolves the route by name and
    /// passes the id separately as the route argument. The entity stores the route in
    /// <see cref="Notification.Route"/> and the id in <see cref="Notification.ReferenceId"/>,
    /// so the two can never drift out of step.
    /// </summary>
    public static NotificationDto ToDto(this Notification notification) =>
        new()
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Body = notification.Message,
            CreatedAt = notification.CreatedAt,
            IsRead = notification.IsRead,
            TargetRoute = notification.Route,
            TargetId = notification.ReferenceId?.ToString(),
            ReferenceType = notification.ReferenceType
        };

    /// <summary>
    /// Projects a device for the settings list. The provider token is not projected:
    /// returning it would put a push credential in a response the user can read, and the
    /// app has no use for it beyond the suffix shown next to the device name.
    /// </summary>
    public static DeviceDto ToDto(this UserDevice device) =>
        new()
        {
            Id = device.Id,
            Platform = device.Platform,
            DeviceName = device.DeviceName,
            AppVersion = device.AppVersion,
            IsActive = device.IsActive,
            LastSeenAt = device.LastSeenAt,
            CreatedAt = device.CreatedAt,
            TokenSuffix = TokenSuffix(device.Token)
        };

    /// <summary>Trailing characters of a token, so a user can tell two phones apart.</summary>
    public static string TokenSuffix(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var trimmed = token.Trim();

        return trimmed.Length <= 8 ? trimmed : trimmed[^8..];
    }
}
