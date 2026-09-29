using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Application.DTOs.Trust;
using Jobmate.Application.Features.Trust.Commands;
using Jobmate.Application.Features.Trust.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Raising and tracking a dispute about a booking.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/disputes")]
[ApiVersion("1.0")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class DisputeController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/disputes — raises a dispute about a booking.</summary>
    /// <remarks>
    /// Only the two participants can raise one, and the other party is worked out from the
    /// booking, so a complaint cannot be aimed at somebody uninvolved. The other side is
    /// notified immediately.
    ///
    /// Send <c>amount</c> only when the dispute is about pay. Naming an amount holds the
    /// booking's settled payment while the dispute is open; a dispute about the workplace
    /// itself should leave <c>amount</c> at zero and hold nobody's money. One open dispute
    /// per person per job.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = "WorkGiver,Worker")]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DisputeDto>>> Create(
        [FromBody] CreateDisputeRequest? request,
        CancellationToken cancellationToken)
    {
        var dispute = await mediator.Send(
            new CreateDisputeCommand(request ?? new CreateDisputeRequest()),
            cancellationToken);

        return Created($"/api/v1/disputes/{dispute.Id}", new ApiResponse<DisputeDto>(dispute));
    }

    /// <summary>GET /api/v1/disputes — disputes the caller raised or was named in.</summary>
    /// <remarks>
    /// Newest first, and both sides: a worker who has been complained about needs to see
    /// the case and answer it. Pass <c>status=open</c> for a tab of what is still live.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<DisputeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<DisputeDto>>> Mine(
        [FromQuery] GetMyDisputesRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetMyDisputesQuery(request), cancellationToken);

        return Ok(new PagedResponse<DisputeDto>(page));
    }

    /// <summary>GET /api/v1/disputes/{id} — one dispute, for a participant.</summary>
    /// <remarks>
    /// Includes the admin's resolution notes once it has been closed. Somebody who is not a
    /// party to it is told the dispute does not exist rather than that it is off limits.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DisputeDto>>> Detail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var dispute = await mediator.Send(new GetDisputeQuery(id), cancellationToken);

        return Ok(new ApiResponse<DisputeDto>(dispute));
    }
}

/// <summary>
/// Reporting a problem. Separate from <see cref="DisputeController"/> because a report
/// needs no booking: a fake job listing somebody only ever saw has no other party to
/// argue with, and refusing to let anybody report those would lose exactly the abuse
/// cases that matter most.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/support-reports")]
[ApiVersion("1.0")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class SupportReportController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/support-reports — files a report for the team to look at.</summary>
    /// <remarks>
    /// Describe the problem in a sentence or two. <c>relatedJobId</c> is optional; include
    /// it when the report is about one particular booking so the admin can open it.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    [ProducesResponseType(typeof(ApiResponse<SupportReportDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SupportReportDto>>> Create(
        [FromBody] CreateSupportReportRequest? request,
        CancellationToken cancellationToken)
    {
        var report = await mediator.Send(
            new CreateSupportReportCommand(request ?? new CreateSupportReportRequest()),
            cancellationToken);

        return Created($"/api/v1/support-reports/{report.Id}", new ApiResponse<SupportReportDto>(report));
    }
}

/// <summary>
/// The admin side of disputes and support reports. Lives on the admin route so the whole
/// controller inherits the Admin role requirement.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/admin")]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class AdminTrustController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/admin/disputes — the dispute queue, open cases first.</summary>
    /// <remarks>
    /// Defaults to the cases that are still live, oldest first, because those are the ones
    /// somebody is waiting on. Pass <c>status=resolved</c> or <c>status=rejected</c> to
    /// look at closed work, and <c>search</c> to match the description or the notes.
    /// </remarks>
    [HttpGet("disputes")]
    [ProducesResponseType(typeof(PagedResponse<DisputeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<DisputeDto>>> Disputes(
        [FromQuery] GetAdminDisputesRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetAdminDisputesQuery(request), cancellationToken);

        return Ok(new PagedResponse<DisputeDto>(page));
    }

    /// <summary>POST /api/v1/admin/disputes/{id}/review — marks a dispute as being looked at.</summary>
    /// <remarks>
    /// A separate step from resolving so the queue shows what has been picked up, and so a
    /// second admin does not start duplicating work that is already in progress.
    /// </remarks>
    [HttpPost("disputes/{id:guid}/review")]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DisputeDto>>> StartReview(
        Guid id,
        CancellationToken cancellationToken)
    {
        var dispute = await mediator.Send(new StartDisputeReviewCommand(id), cancellationToken);

        return Ok(new ApiResponse<DisputeDto>(dispute, "Dispute is now under review."));
    }

    /// <summary>POST /api/v1/admin/disputes/{id}/resolve — closes a dispute.</summary>
    /// <remarks>
    /// Send <c>status=resolved</c> when the complaint was justified and
    /// <c>status=rejected</c> when it was not; both need <c>resolution</c> saying why,
    /// which is what both parties are shown. Closing a dispute releases any money it was
    /// holding back to the worker. If the outcome goes against the worker, refund the
    /// payment separately afterwards.
    /// </remarks>
    [HttpPost("disputes/{id:guid}/resolve")]
    [ProducesResponseType(typeof(ApiResponse<DisputeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DisputeDto>>> Resolve(
        Guid id,
        [FromBody] ResolveDisputeRequest? request,
        CancellationToken cancellationToken)
    {
        var dispute = await mediator.Send(
            new ResolveDisputeCommand(id, request ?? new ResolveDisputeRequest()),
            cancellationToken);

        return Ok(new ApiResponse<DisputeDto>(dispute, "Dispute closed."));
    }

    /// <summary>GET /api/v1/admin/support-reports — the support report queue.</summary>
    /// <remarks>
    /// Newest first, showing handled reports too. Add <c>isResolved=false</c> for just the
    /// outstanding ones.
    /// </remarks>
    [HttpGet("support-reports")]
    [ProducesResponseType(typeof(PagedResponse<SupportReportAdminDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<SupportReportAdminDto>>> SupportReports(
        [FromQuery] GetAdminSupportReportsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetAdminSupportReportsQuery(request), cancellationToken);

        return Ok(new PagedResponse<SupportReportAdminDto>(page));
    }

    /// <summary>POST /api/v1/admin/support-reports/{id}/resolve — marks a report dealt with.</summary>
    /// <remarks>
    /// Send <c>isResolved=false</c> to put it back, for a report that turned out to need
    /// more work rather than one that was filed in error.
    /// </remarks>
    [HttpPost("support-reports/{id:guid}/resolve")]
    [ProducesResponseType(typeof(ApiResponse<SupportReportAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SupportReportAdminDto>>> ResolveReport(
        Guid id,
        [FromQuery] bool isResolved,
        CancellationToken cancellationToken)
    {
        var report = await mediator.Send(
            new ResolveSupportReportCommand(id, isResolved),
            cancellationToken);

        return Ok(new ApiResponse<SupportReportAdminDto>(report));
    }
}
