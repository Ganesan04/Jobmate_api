using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Ratings;
using Jobmate.Application.Features.Ratings.Commands;
using Jobmate.Application.Features.Ratings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// A booking's own actions: proving attendance and closing the work off.
///
/// These hang off the assignment rather than the job because they act on one booking:
/// a job with three workers has three check-ins and three completions.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/assignments")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class AssignmentController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/assignments/{id}/check-in — the worker confirms they are on site.</summary>
    /// <remarks>
    /// The four-digit code is read out by the employer. It is single use, so a second
    /// attempt with the same code is refused and the worker has to ask for a new one.
    /// </remarks>
    [HttpPost("{id:guid}/check-in")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<WorkerAssignmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkerAssignmentDto>>> CheckIn(
        Guid id,
        [FromBody] CheckInRequest? request,
        CancellationToken cancellationToken)
    {
        var assignment = await mediator.Send(
            new CheckInAssignmentCommand(id, request ?? new CheckInRequest()),
            cancellationToken);

        return Ok(new ApiResponse<WorkerAssignmentDto>(assignment, "Checked in."));
    }

    /// <summary>POST /api/v1/assignments/{id}/complete — closes the booking once the work is done.</summary>
    /// <remarks>
    /// Either the worker or the employer may close it, and <c>overtimeAmount</c> may be
    /// recorded here so it is part of the payout. Closing the last live booking closes
    /// the job, which is what allows both sides to leave a rating.
    /// </remarks>
    [HttpPost("{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<WorkerAssignmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<WorkerAssignmentDto>>> Complete(
        Guid id,
        [FromBody] CompleteAssignmentRequest? request,
        CancellationToken cancellationToken)
    {
        var assignment = await mediator.Send(
            new CompleteAssignmentCommand(id, request ?? new CompleteAssignmentRequest()),
            cancellationToken);

        return Ok(new ApiResponse<WorkerAssignmentDto>(assignment, "Booking completed."));
    }
}

/// <summary>
/// The employer-facing half of check-in: the code the worker has to type in.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/jobs")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class CheckInCodeController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/jobs/{jobId}/check-in-code/{assignmentId} — read the code out.</summary>
    /// <remarks>
    /// The employer opens their job, taps a booking and reads the four digits to the
    /// worker on site. The code is per booking, single use, and never sent to the
    /// worker: a worker asking for their own code would be able to check in unsupervised.
    /// </remarks>
    [HttpGet("{jobId:guid}/check-in-code/{assignmentId:guid}")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<string>>> Read(
        Guid jobId,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var code = await mediator.Send(
            new GetCheckInCodeQuery(jobId, assignmentId),
            cancellationToken);

        return Ok(new ApiResponse<string>(code, "Check-in code for this booking."));
    }
}

/// <summary>
/// Feedback between the two sides of a finished job, and the feedback a person has
/// received.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/ratings")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class RatingController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/ratings — rates the other participant in a completed job.</summary>
    /// <remarks>
    /// Send the <c>jobId</c> and who is being rated. An employer rates the worker and a
    /// worker rates the employer; one rating per person per job, and only after the job
    /// is completed. The rating updates that person's average straight away.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = "WorkGiver,Worker")]
    [ProducesResponseType(typeof(ApiResponse<WorkerRatingDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkerRatingDto>>> Create(
        [FromBody] CreateRatingRequest? request,
        CancellationToken cancellationToken)
    {
        var rating = await mediator.Send(
            new CreateRatingCommand(request ?? new CreateRatingRequest()),
            cancellationToken);

        return Created(
            "/api/v1/ratings",
            new ApiResponse<WorkerRatingDto>(rating, "Thanks for the feedback."));
    }

    /// <summary>GET /api/v1/ratings — feedback received, newest first.</summary>
    /// <remarks>
    /// With no filter this is the caller's own feedback. Pass <c>toUserId</c> to read
    /// someone else's, and <c>jobId</c> to see only what was said about one job.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<WorkerRatingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<WorkerRatingDto>>> List(
        [FromQuery] GetRatingsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetRatingsQuery(request), cancellationToken);

        return Ok(new PagedResponse<WorkerRatingDto>(page));
    }
}

/// <summary>
/// Work history for either side. The endpoint is the same shape for both roles; the
/// counterpart on each line is whoever the other party is.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class WorkHistoryController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/worker/work-history — the signed-in worker's bookings.</summary>
    /// <remarks>
    /// A booking is listed as soon as it is made; <c>paymentStatus</c> is what
    /// distinguishes work agreed from work paid. Optional filters: <c>from</c>, <c>to</c>,
    /// <c>paymentStatus</c>, <c>jobId</c>.
    /// </remarks>
    [HttpGet("worker/work-history")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(PagedResponse<WorkHistoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<WorkHistoryEntryDto>>> ForWorker(
        [FromQuery] WorkHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetWorkHistoryQuery(request), cancellationToken);

        return Ok(new PagedResponse<WorkHistoryEntryDto>(page));
    }

    /// <summary>GET /api/v1/giver/work-history — the signed-in employer's bookings.</summary>
    /// <remarks>The counterpart on each line is the worker, and earnings are what they were paid.</remarks>
    [HttpGet("giver/work-history")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(PagedResponse<WorkHistoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<WorkHistoryEntryDto>>> ForGiver(
        [FromQuery] WorkHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetGiverWorkHistoryQuery(request), cancellationToken);

        return Ok(new PagedResponse<WorkHistoryEntryDto>(page));
    }
}
