using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobmate.Application.Common.Services;

/// <summary>
/// Single funnel for in-app notifications. Every handler that needs to tell a user
/// something calls this instead of inserting <see cref="Notification"/> rows itself,
/// so push dispatch and unread bookkeeping happen exactly once per event.
///
/// Persisting the row first and pushing second is deliberate: a push failure must
/// never lose the in-app notification the user can still see in the app.
/// </summary>
public sealed class NotificationService(
    IAppDbContext db,
    IPushNotificationService push,
    ILogger<NotificationService> logger) : INotificationService
{
    /// <summary>Persists one notification for a single user and queues a push.</summary>
    public async Task<Notification> SendAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        NotificationReferenceType referenceType = NotificationReferenceType.None,
        Guid? referenceId = null,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Route = route
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        await TryPushAsync(notification, cancellationToken);

        return notification;
    }

    /// <summary>Persists one notification per distinct recipient. Used by fan-out events.</summary>
    public async Task SendManyAsync(
        IEnumerable<Guid> userIds,
        NotificationType type,
        string title,
        string message,
        NotificationReferenceType referenceType = NotificationReferenceType.None,
        Guid? referenceId = null,
        string? route = null,
        CancellationToken cancellationToken = default)
    {
        var distinct = userIds.Distinct().ToList();

        if (distinct.Count == 0)
        {
            return;
        }

        var notifications = distinct.Select(userId => new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Route = route
        }).ToList();

        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await TryPushAsync(notification, cancellationToken);
        }
    }

    private async Task TryPushAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var pushResult = await push.SendAsync(
                new PushNotification(
                    notification.UserId,
                    notification.Title,
                    notification.Message,
                    notification.Type,
                    notification.ReferenceType,
                    notification.ReferenceId,
                    notification.Route),
                cancellationToken);

            if (pushResult.DevicesTargeted > 0)
            {
                notification.PushDispatchedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never let a push provider outage fail the business transaction.
            logger.LogWarning(ex, "Push dispatch failed for notification {NotificationId}", notification.Id);
        }
    }
}
