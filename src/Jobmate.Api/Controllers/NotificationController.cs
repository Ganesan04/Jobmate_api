using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Notifications;
using Jobmate.Application.Features.Notifications.Commands;
using Jobmate.Application.Features.Notifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// The caller's notification inbox: the list, the unread badge, and marking rows read.
///
/// Every route here is scoped to the signed-in caller by the handlers, so there is no way
/// to address another user's notifications.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/notifications")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class NotificationController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/notifications — the caller's notifications, newest first.</summary>
    /// <remarks>
    /// Optional filters: <c>unreadOnly</c> and <c>types</c> (repeatable, e.g.
    /// <c>?types=payment&amp;types=jobAccepted</c>). The app's type filter chips map
    /// straight onto the second one.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<NotificationDto>>> List(
        [FromQuery] GetNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetNotificationsQuery(request), cancellationToken);

        return Ok(new PagedResponse<NotificationDto>(page));
    }

    /// <summary>GET /api/v1/notifications/unread-count — both badge counts in one call.</summary>
    [HttpGet("unread-count")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UnreadCountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> UnreadCount(
        CancellationToken cancellationToken)
    {
        var counts = await mediator.Send(new GetUnreadCountQuery(), cancellationToken);

        return Ok(new ApiResponse<UnreadCountDto>(counts));
    }

    /// <summary>POST /api/v1/notifications/{id}/read — marks one row read.</summary>
    /// <remarks>
    /// The app calls this as soon as a row is tapped, before it navigates, so the badge
    /// drops even if the target screen cannot be opened. Answering with the remaining
    /// unread count saves the app a second round trip just to redraw its badge. Marking
    /// an already-read row is accepted, so a retried tap is harmless.
    /// </remarks>
    [HttpPost("{id:guid}/read")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<int>>> MarkRead(
        Guid id,
        CancellationToken cancellationToken)
    {
        var remaining = await mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);

        return Ok(new ApiResponse<int>(remaining, "Notification marked as read."));
    }

    /// <summary>POST /api/v1/notifications/read-all — marks every unread row read.</summary>
    [HttpPost("read-all")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> MarkAllRead(CancellationToken cancellationToken)
    {
        var remaining = await mediator.Send(new MarkAllNotificationsReadCommand(), cancellationToken);

        return Ok(new ApiResponse<int>(remaining, "All notifications marked as read."));
    }
}

/// <summary>
/// Push device registration — the tokens a notification is delivered to.
///
/// Registration is keyed on the provider token, so a phone that changes hands is
/// re-pointed at the new account instead of silently keeping the old one's notifications.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/devices")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class DeviceController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/devices — the caller's registered devices.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> List(CancellationToken cancellationToken)
    {
        var devices = await mediator.Send(new GetDevicesQuery(), cancellationToken);

        return Ok(new ApiResponse<IReadOnlyList<DeviceDto>>(devices));
    }

    /// <summary>POST /api/v1/devices/register — registers or refreshes one device.</summary>
    /// <remarks>
    /// The app sends this on every launch, which is what makes it the place a device is
    /// reactivated: a user who switched notifications back on in settings gets them
    /// working simply by opening the app. Registering a token that is already known
    /// updates it rather than creating a second row, and a token that belonged to another
    /// account is moved across.
    /// </remarks>
    [HttpPost("register")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<DeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Register(
        [FromBody] RegisterDeviceRequest? request,
        CancellationToken cancellationToken)
    {
        var device = await mediator.Send(
            new RegisterDeviceCommand(request ?? new RegisterDeviceRequest()),
            cancellationToken);

        return Ok(new ApiResponse<DeviceDto>(device, "Device registered."));
    }

    /// <summary>DELETE /api/v1/devices/{id} — stops push reaching one device.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<int>>> Unregister(Guid id, CancellationToken cancellationToken)
    {
        var remaining = await mediator.Send(new UnregisterDeviceCommand(id), cancellationToken);

        return Ok(new ApiResponse<int>(remaining, "Device unregistered."));
    }

    /// <summary>DELETE /api/v1/devices — stops push reaching every device.</summary>
    /// <remarks>Called on sign-out, so the next person to sign in on that phone starts clean.</remarks>
    [HttpDelete]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> UnregisterAll(CancellationToken cancellationToken)
    {
        var remaining = await mediator.Send(new UnregisterAllDevicesCommand(), cancellationToken);

        return Ok(new ApiResponse<int>(remaining, "All devices unregistered."));
    }
}
