using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobmate.Infrastructure.Services;

/// <summary>
/// Development push provider. It looks up the user's active devices and reports how many
/// were targeted, so the dispatch path is exercised end to end and a notification row
/// records that a push really was attempted. The payload itself is logged rather than
/// delivered, which keeps the app usable without Firebase credentials.
///
/// Swapping this for a real provider changes nothing the user sees: notifications are
/// always persisted before dispatch, so a push that is lost or never sent still appears
/// in the app's inbox.
/// </summary>
public sealed class MockPushNotificationService(
    IAppDbContext db,
    ILogger<MockPushNotificationService> logger) : IPushNotificationService
{
    public async Task<PushDispatchResult> SendAsync(
        PushNotification notification,
        CancellationToken cancellationToken)
    {
        var devices = await db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == notification.UserId && d.IsActive)
            .Select(d => d.Token)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Mock push: user={UserId} type={Type} title={Title} route={Route} devices={DeviceCount}",
            notification.UserId,
            notification.Type,
            notification.Title,
            notification.Route,
            devices.Count);

        // A user with no registered device is not a failure: the notification is in their
        // inbox and will be there next time they open the app.
        return new PushDispatchResult(
            Success: true,
            DevicesTargeted: devices.Count,
            Message: devices.Count == 0
                ? "Mock push provider: no active devices; notification is in-app only."
                : $"Mock push provider: payload logged for {devices.Count} device(s), not delivered.");
    }

    public async Task<PushDispatchResult> SendManyAsync(
        IReadOnlyCollection<PushNotification> notifications,
        CancellationToken cancellationToken)
    {
        var targeted = 0;

        foreach (var notification in notifications)
        {
            targeted += (await SendAsync(notification, cancellationToken)).DevicesTargeted;
        }

        return new PushDispatchResult(
            Success: true,
            DevicesTargeted: targeted,
            Message: $"Mock push provider: {notifications.Count} payload(s) logged, {targeted} device(s) targeted.");
    }
}
