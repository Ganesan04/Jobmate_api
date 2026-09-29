using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Jobs;
using Jobmate.Application.DTOs.Notifications;
using Jobmate.Application.Features.Applications.Commands;
using Jobmate.Application.Features.Applications.Queries;
using Jobmate.Application.Features.Jobs.Commands;
using Jobmate.Application.Features.Jobs.Queries;
using Jobmate.Application.Features.Notifications.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Job postings: the create/edit lifecycle for a work giver, the public search feed
/// and the worker's discovery list. Every action is a MediatR request; this class only
/// binds, authorises and wraps the result in the standard response envelope.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/jobs")]
[ApiVersion("1.0")]
[Produces("application/json")]
public sealed class JobController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// POST /api/v1/jobs — creates a job from the six-step wizard.
    /// </summary>
    /// <remarks>
    /// Requires the <c>WorkGiver</c> role. The wizard posts once, so the default is a
    /// published job; send <c>publishImmediately: false</c> to keep it as a draft and
    /// call <c>POST /jobs/{id}/publish</c> later.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Create(
        [FromBody] CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new CreateJobCommand(request), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job created."));
    }

    /// <summary>PUT /api/v1/jobs/{id} — edits a job. Only the fields you send change.</summary>
    /// <remarks>Only the poster can edit, and only while the job is still editable.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Update(
        Guid id,
        [FromBody] UpdateJobRequest request,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new UpdateJobCommand(id, request), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job updated."));
    }

    /// <summary>GET /api/v1/jobs/{id} — one job, with the applicant count and your distance.</summary>
    /// <remarks>
    /// A job whose address is hidden returns an empty <c>address</c> and null
    /// coordinates until you are booked on it.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> GetById(
        Guid id,
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new GetJobByIdQuery(id, latitude, longitude), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job));
    }

    /// <summary>GET /api/v1/jobs — searches published jobs.</summary>
    /// <remarks>
    /// Supports <c>search</c>, <c>categories</c>, <c>statuses</c>, <c>area</c>,
    /// <c>minPay</c>/<c>maxPay</c>, <c>startDate</c>/<c>endDate</c>, <c>jobType</c>,
    /// <c>workplaceType</c>, <c>urgentOnly</c>, <c>recurringOnly</c>,
    /// <c>verifiedGiverOnly</c> and a <c>latitude</c>/<c>longitude</c>/<c>radiusKm</c>
    /// combination. Drafts are never returned.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<JobDto>>> Search(
        [FromQuery] JobSearchRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new SearchJobsQuery(request), cancellationToken);

        return Ok(new PagedResponse<JobDto>(page));
    }

    /// <summary>
    /// POST /api/v1/jobs/{id}/apply — a worker applies to an open job.
    /// </summary>
    /// <remarks>
    /// Refused for a draft, a closed job, a full job, a passed deadline or an application
    /// the worker already has, with the reason in <c>errorCode</c>. Applying twice is a
    /// 409, not a second row.
    /// </remarks>
    [HttpPost("{id:guid}/apply")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<JobApplicationDto>>> Apply(
        Guid id,
        [FromBody] ApplyToJobRequest request,
        CancellationToken cancellationToken)
    {
        var application = await mediator.Send(new ApplyToJobCommand(id, request), cancellationToken);

        return Ok(new ApiResponse<JobApplicationDto>(application, "Application submitted."));
    }

    /// <summary>
    /// GET /api/v1/jobs/{id}/applications — the applicants for a job the caller posted.
    /// </summary>
    /// <remarks>
    /// Opening the list marks unseen applications as viewed. Filter with
    /// <c>statuses</c>, <c>search</c> (worker name, area or phone), <c>minRating</c> and
    /// <c>sortBy</c> (newest, oldest, ratingHigh, ratingLow, expectedPayHigh,
    /// expectedPayLow, name).
    /// </remarks>
    [HttpGet("{id:guid}/applications")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(PagedResponse<JobApplicantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<JobApplicantDto>>> GetApplications(
        Guid id,
        [FromQuery] GetApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetJobApplicationsQuery(id, request), cancellationToken);

        return Ok(new PagedResponse<JobApplicantDto>(page));
    }

    /// <summary>POST /api/v1/jobs/{id}/select-workers — books several applicants in one call.</summary>
    /// <remarks>
    /// The Flutter confirm-selection step sends every chosen applicant with the pay
    /// agreed for each. The batch is all or nothing: if the job cannot fit all of them,
    /// nothing is booked. Set <c>releaseUnselectedShortlists</c> to false to keep
    /// shortlisted-but-not-selected applicants on the shortlist.
    /// </remarks>
    [HttpPost("{id:guid}/select-workers")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<SelectWorkersResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SelectWorkersResultDto>>> SelectWorkers(
        Guid id,
        [FromBody] SelectWorkersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SelectWorkersCommand(id, request), cancellationToken);

        return Ok(new ApiResponse<SelectWorkersResultDto>(
            result,
            $"{result.Selected.Count} worker(s) selected."));
    }

    /// <summary>POST /api/v1/jobs/{id}/publish — flips a draft live.</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new PublishJobCommand(id), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job published."));
    }

    /// <summary>POST /api/v1/jobs/{id}/cancel — cancels the job and closes everything attached to it.</summary>
    /// <remarks>
    /// Applicants and booked workers are notified, open applications are expired and
    /// outstanding bookings are cancelled, so no party is left with a live-looking job.
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Cancel(
        Guid id,
        [FromBody] CancelJobRequest? request,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(
            new CancelJobCommand(id, request ?? new CancelJobRequest()),
            cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job cancelled."));
    }

    /// <summary>DELETE /api/v1/jobs/{id} — removes a draft.</summary>
    /// <remarks>
    /// A job that has been published must be cancelled instead, so its applications
    /// and payments keep a valid history.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteJobCommand(id), cancellationToken);

        return Ok(new ApiResponse<object?>(null, "Job deleted."));
    }

    /// <summary>POST /api/v1/jobs/{id}/start — marks the job in progress.</summary>
    /// <remarks>Requires at least one booked worker, and only the poster may start it.</remarks>
    [HttpPost("{id:guid}/start")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Start(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new StartJobCommand(id), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job started."));
    }

    /// <summary>POST /api/v1/jobs/{id}/complete — marks the job completed, which unlocks ratings.</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobDto>>> Complete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var job = await mediator.Send(new CompleteJobCommand(id), cancellationToken);

        return Ok(new ApiResponse<JobDto>(job, "Job completed."));
    }

    /// <summary>POST /api/v1/jobs/{id}/remind — nudges the workers who have not checked in.</summary>
    /// <remarks>
    /// The employer's answer to a no-show. Only workers on the job who have not checked in
    /// yet are messaged, and only once the job is actually running, so a reminder can never
    /// reach a booking that is not due. The answer says how many were reminded, which is
    /// what the app shows in the toast.
    /// </remarks>
    [HttpPost("{id:guid}/remind")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<ReminderResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReminderResultDto>>> Remind(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RemindWorkersCommand(id), cancellationToken);

        return Ok(new ApiResponse<ReminderResultDto>(result, "Reminder sent."));
    }
}

/// <summary>
/// The work giver's own job list and home-screen counters. Kept off
/// <c>/jobs</c> because the route is always scoped to the caller and never accepts a
/// giver id, which is what stops one employer reading another's postings.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/giver")]
[ApiVersion("1.0")]
[Produces("application/json")]
[Authorize(Roles = "WorkGiver")]
public sealed class GiverJobsController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/giver/jobs — the caller's own postings, newest first.</summary>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(PagedResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<JobDto>>> GetJobs(
        [FromQuery] GiverJobsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetGiverJobsQuery(request), cancellationToken);

        return Ok(new PagedResponse<JobDto>(page));
    }

    /// <summary>GET /api/v1/giver/stats — the counters on the giver home screen.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<GiverStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<GiverStatsDto>>> GetStats(CancellationToken cancellationToken)
    {
        var stats = await mediator.Send(new GetGiverStatsQuery(), cancellationToken);

        return Ok(new ApiResponse<GiverStatsDto>(stats));
    }
}

/// <summary>
/// The worker's discovery feed and home-screen counters. <c>GET /worker/jobs</c> only
/// ever returns open jobs with a free worker slot.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/worker")]
[ApiVersion("1.0")]
[Produces("application/json")]
[Authorize(Roles = "Worker")]
public sealed class WorkerJobsController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/worker/jobs — jobs a worker can still apply to.</summary>
    /// <remarks>
    /// Supports the same filters as <c>GET /jobs</c>, plus a radius search around
    /// <c>latitude</c>/<c>longitude</c> that returns a <c>distanceKm</c> per job.
    /// </remarks>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(PagedResponse<JobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<JobDto>>> GetJobs(
        [FromQuery] WorkerJobsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetWorkerJobsQuery(request), cancellationToken);

        return Ok(new PagedResponse<JobDto>(page));
    }

    /// <summary>GET /api/v1/worker/stats — application, booking and earnings counters.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<WorkerStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<WorkerStatsDto>>> GetStats(CancellationToken cancellationToken)
    {
        var stats = await mediator.Send(new GetWorkerStatsQuery(), cancellationToken);

        return Ok(new ApiResponse<WorkerStatsDto>(stats));
    }

    /// <summary>GET /api/v1/worker/applications — the caller's own applications.</summary>
    /// <remarks>
    /// Newest first. Filter with <c>statuses</c> (for example <c>shortlisted</c>) and with
    /// <c>jobStatus</c> to see only applications for jobs that are still running.
    /// </remarks>
    [HttpGet("applications")]
    [ProducesResponseType(typeof(PagedResponse<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<JobApplicationDto>>> GetApplications(
        [FromQuery] GetWorkerApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetWorkerApplicationsQuery(request), cancellationToken);

        return Ok(new PagedResponse<JobApplicationDto>(page));
    }
}
