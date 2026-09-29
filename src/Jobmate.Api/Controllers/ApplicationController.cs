using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.Features.Applications.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Actions on a single application: the worker's withdraw and the giver's shortlist or
/// reject. Applying lives on the job route (POST /jobs/{id}/apply) and the two lists
/// live there too, so this controller only holds the actions keyed by application id.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/applications")]
[ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ApplicationController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/applications/{id}/withdraw — the worker takes back their application.</summary>
    /// <remarks>
    /// Only the worker who applied may do this, and only while the application is still
    /// open: a rejected, withdrawn or expired one is terminal.
    /// </remarks>
    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobApplicationDto>>> Withdraw(
        Guid id,
        CancellationToken cancellationToken)
    {
        var application = await mediator.Send(new WithdrawApplicationCommand(id), cancellationToken);

        return Ok(new ApiResponse<JobApplicationDto>(application, "Application withdrawn."));
    }

    /// <summary>POST /api/v1/applications/{id}/shortlist — the giver keeps this applicant.</summary>
    /// <remarks>Send <c>proposedPay</c> to attach an offer the app shows on the applicant card.</remarks>
    [HttpPost("{id:guid}/shortlist")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobApplicationDto>>> Shortlist(
        Guid id,
        [FromBody] ReviewApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        var application = await mediator.Send(
            new ShortlistApplicationCommand(id, request ?? new ReviewApplicationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<JobApplicationDto>(application, "Applicant shortlisted."));
    }

    /// <summary>POST /api/v1/applications/{id}/reject — the giver turns this applicant down.</summary>
    /// <remarks>Send <c>reason</c> to record why; it is stored on the application and shown to the worker.</remarks>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobApplicationDto>>> Reject(
        Guid id,
        [FromBody] ReviewApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        var application = await mediator.Send(
            new RejectApplicationCommand(id, request ?? new ReviewApplicationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<JobApplicationDto>(application, "Applicant rejected."));
    }

    /// <summary>POST /api/v1/applications/{id}/select — books this applicant and takes one of the job's slots.</summary>
    /// <remarks>
    /// The agreed pay falls back to the proposal made while shortlisting and then to the
    /// pay on the posting, so the common case needs no body at all. The slot is taken
    /// with a conditional database update, so two givers confirming the last worker at
    /// once produce one booking and one 409 rather than an over-filled job.
    /// </remarks>
    [HttpPost("{id:guid}/select")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<WorkerAssignmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<WorkerAssignmentDto>>> Select(
        Guid id,
        [FromBody] SelectWorkerRequest? request,
        CancellationToken cancellationToken)
    {
        var assignment = await mediator.Send(
            new SelectWorkerCommand(id, request ?? new SelectWorkerRequest()),
            cancellationToken);

        return Ok(new ApiResponse<WorkerAssignmentDto>(assignment, "Worker selected."));
    }
}
