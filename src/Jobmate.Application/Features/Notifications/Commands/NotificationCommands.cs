using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Notifications;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Notifications.Commands;

/// <summary>
/// Marks one of the caller's notifications as read. POST /api/v1/notifications/{id}/read.
///
/// The app calls this as soon as a row is tapped, before navigating, so the badge drops
/// even if the target screen fails to open. Marking an already-read notification is
/// accepted and left alone, which makes a retried tap harmless.
/// </summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest<int>;

public sealed class MarkNotificationReadCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<MarkNotificationReadCommand, int>
{
    /// <summary>Returns how many notifications the call still has unread.</summary>
    public async Task<int> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        // Scoped to the caller: one user marking a row read must not reach another's.
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, cancellationToken);

        if (notification is null || notification.UserId != callerId)
        {
            throw new NotFoundException("Notification not found.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = now;

            await db.SaveChangesAsync(cancellationToken);
        }

        return await db.Notifications.CountAsync(n => n.UserId == callerId && !n.IsRead, cancellationToken);
    }
}

/// <summary>
/// Marks every one of the caller's notifications as read.
/// POST /api/v1/notifications/read-all.
///
/// Bulk update rather than a loop, so the number of rows a user can be made to update is
/// not the reason the request is slow.
/// </summary>
public sealed record MarkAllNotificationsReadCommand : IRequest<int>;

public sealed class MarkAllNotificationsReadCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<MarkAllNotificationsReadCommand, int>
{
    /// <summary>Returns the remaining unread count, which is always zero here.</summary>
    public async Task<int> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        // The unread filter in the WHERE clause is what keeps this to the rows that
        // actually change; an already-read notification keeps its original ReadAt.
        await db.Notifications
            .Where(n => n.UserId == callerId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, now),
                cancellationToken);

        return 0;
    }
}

/// <summary>
/// Registers (or refreshes) a push token for one of the caller's devices.
/// POST /api/v1/devices/register.
///
/// Registration is an upsert keyed on the provider token rather than on the user, because
/// the token identifies the install, not the person: a phone that is handed over, or a
/// sign-out followed by someone else signing in, arrives here with a token that is already
/// in the table. Treating that as a new row would fail on the unique index and leave the
/// second user with no notifications at all.
/// </summary>
public sealed record RegisterDeviceCommand(RegisterDeviceRequest Request) : IRequest<DeviceDto>;

public sealed class RegisterDeviceCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<RegisterDeviceCommand, DeviceDto>
{
    public async Task<DeviceDto> Handle(RegisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var token = request.Request.Token.Trim();

        var device = await db.UserDevices.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

        if (device is null)
        {
            device = new UserDevice
            {
                UserId = callerId,
                Token = token,
                CreatedAt = now
            };

            db.UserDevices.Add(device);
        }
        else if (device.UserId != callerId)
        {
            // The token is moving to a different account, which is what happens when a
            // shared device signs out and someone else signs in.
            device.UserId = callerId;
        }

        // Re-activating on every call is the point: the app sends this on launch, so a
        // device that was switched off in settings comes back simply by opening the app.
        device.Platform = request.Request.Platform;
        device.DeviceName = request.Request.DeviceName?.Trim();
        device.AppVersion = request.Request.AppVersion?.Trim();
        device.IsActive = true;
        device.LastSeenAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return device.ToDto();
    }
}

/// <summary>
/// Stops push reaching one of the caller's devices. DELETE /api/v1/devices/{id}.
///
/// The row is deactivated rather than deleted. A provider token that has been invalidated
/// may be handed out again, and keeping the history lets a later registration be
/// recognised as the same install.
/// </summary>
public sealed record UnregisterDeviceCommand(Guid DeviceId) : IRequest<int>;

public sealed class UnregisterDeviceCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<UnregisterDeviceCommand, int>
{
    /// <summary>Returns how many of the caller's devices remain active.</summary>
    public async Task<int> Handle(UnregisterDeviceCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var device = await db.UserDevices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

        if (device is null || device.UserId != callerId)
        {
            throw new NotFoundException("Device not found.");
        }

        if (device.IsActive)
        {
            device.IsActive = false;

            await db.SaveChangesAsync(cancellationToken);
        }

        return await db.UserDevices
            .CountAsync(d => d.UserId == callerId && d.IsActive, cancellationToken);
    }
}

/// <summary>
/// Stops push reaching every one of the caller's devices. DELETE /api/v1/devices.
///
/// Used when the user signs out: the next person to sign in on that phone should not
/// inherit the previous user's notifications.
/// </summary>
public sealed record UnregisterAllDevicesCommand : IRequest<int>;

public sealed class UnregisterAllDevicesCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<UnregisterAllDevicesCommand, int>
{
    public async Task<int> Handle(UnregisterAllDevicesCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        await db.UserDevices
            .Where(d => d.UserId == callerId && d.IsActive)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(d => d.IsActive, false),
                cancellationToken);

        return 0;
    }
}

/// <summary>
/// The employer nags the workers on a job who have not checked in yet.
/// POST /api/v1/jobs/{jobId}/remind.
///
/// A reminder is worth sending once the shift has started, so it is refused before then.
/// Workers who have already checked in, and bookings already closed, are skipped rather
/// than reported as failures — the employer asked for the workers who have not arrived,
/// not for a report on everyone.
/// </summary>
public sealed record RemindWorkersCommand(Guid JobId) : IRequest<ReminderResultDto>;

public sealed class RemindWorkersCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications) : IRequestHandler<RemindWorkersCommand, ReminderResultDto>
{
    public async Task<ReminderResultDto> Handle(RemindWorkersCommand request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));

        var job = await db.Jobs
            .Include(j => j.Assignments)
            .ThenInclude(a => a!.Worker)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        if (job.GiverId != giverId)
        {
            throw new ForbiddenException("Only the person who posted this job can remind its workers.");
        }

        if (job.Status is not JobStatus.InProgress)
        {
            throw new BusinessRuleException(
                "Reminders only make sense once the job has started.",
                "JobNotStarted");
        }

        var waiting = job.Assignments
            .Where(a => a is { Status: AssignmentStatus.Active, CheckedInAt: null })
            .Select(a => a!.WorkerId)
            .Distinct()
            .ToList();

        if (waiting.Count > 0)
        {
            await notifications.SendManyAsync(
                waiting,
                NotificationType.JobReminder,
                "Your job has started",
                $"\"{job.Title}\" is under way. Please check in with the code to show you are on site.",
                NotificationReferenceType.Assignment,
                job.Id,
                NotificationRoutes.AcceptedJob,
                cancellationToken);
        }

        return new ReminderResultDto(waiting.Count, job.Id);
    }
}
