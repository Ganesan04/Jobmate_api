using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Notifications;

/// <summary>POST /devices/register.</summary>
public sealed record RegisterDeviceRequest
{
    public string Token { get; init; } = string.Empty;

    public DevicePlatform Platform { get; init; } = DevicePlatform.Android;

    public string? DeviceName { get; init; }

    public string? AppVersion { get; init; }
}

/// <summary>GET /notifications filters.</summary>
public sealed record GetNotificationsRequest : Domain.Common.PageRequest
{
    public bool? UnreadOnly { get; init; }

    public List<NotificationType>? Types { get; init; }
}
