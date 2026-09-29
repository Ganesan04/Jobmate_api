using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Notifications;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Notifications.Queries;

/// <summary>
/// The caller's own notifications, newest first. GET /api/v1/notifications.
///
/// There is no way to ask for another user's notifications: the filter is the caller's id
/// and nothing else. Optional filters: <c>unreadOnly</c>, <c>types</c>.
/// </summary>
public sealed record GetNotificationsQuery(GetNotificationsRequest Request)
    : IRequest<PagedResult<NotificationDto>>;

public sealed class GetNotificationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<PagedResult<NotificationDto>> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var filter = request.Request;

        var source = db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == callerId);

        if (filter.UnreadOnly is true)
        {
            source = source.Where(n => !n.IsRead);
        }

        if (filter.Types is { Count: > 0 } types)
        {
            source = source.Where(n => types.Contains(n.Type));
        }

        var total = await source.CountAsync(cancellationToken);

        // Newest first, with the id as a tie-break so a burst written in the same
        // millisecond still pages in a stable order.
        var items = await source
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(n => n.ToDto())
            .ToListAsync(cancellationToken);

        return PagedResult<NotificationDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>
/// The caller's unread badge counts. GET /api/v1/notifications/unread-count.
///
/// Returned as one object rather than two endpoints because the app shell shows both
/// badges together and would otherwise have to make two calls to draw one screen.
/// </summary>
public sealed record GetUnreadCountQuery : IRequest<UnreadCountDto>;

public sealed class GetUnreadCountQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetUnreadCountQuery, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var notifications = await db.Notifications
            .CountAsync(n => n.UserId == callerId && !n.IsRead, cancellationToken);

        // Message unread is denormalised onto the participant row, so this is a sum
        // rather than a count over messages.
        var messages = await db.ConversationParticipants
            .Where(p => p.UserId == callerId && !p.IsArchived)
            .SumAsync(p => p.UnreadCount, cancellationToken);

        return new UnreadCountDto { Notifications = notifications, Messages = messages };
    }
}

/// <summary>
/// The caller's registered devices. GET /api/v1/devices.
///
/// Listed newest first, because the device a user is trying to identify is almost always
/// the one they just added.
/// </summary>
public sealed record GetDevicesQuery : IRequest<IReadOnlyList<DeviceDto>>;

public sealed class GetDevicesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDevicesQuery, IReadOnlyList<DeviceDto>>
{
    public async Task<IReadOnlyList<DeviceDto>> Handle(
        GetDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        return await db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == callerId)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => d.ToDto())
            .ToListAsync(cancellationToken);
    }
}
