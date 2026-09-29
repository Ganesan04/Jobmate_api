using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Application.Features.Profiles.Commands;
using Jobmate.Application.Features.Profiles.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// A worker's bookmarked jobs.
///
/// The bookmark list is per user and per job, and both adding and removing are idempotent:
/// a double tap on the save icon is not an error, and neither is unsaving something that
/// is not saved.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/worker/saved-jobs")]
[ApiVersion("1.0")]
[Produces("application/json")]
[Authorize(Roles = "Worker")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class SavedJobsController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/worker/saved-jobs — the bookmarked jobs, newest bookmark first.</summary>
    /// <remarks>
    /// Only active jobs by default. Pass <c>includeClosed=true</c> to see jobs that have
    /// since been filled, completed or cancelled; the bookmark itself is never removed
    /// automatically.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SavedJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<SavedJobDto>>> List(
        [FromQuery] GetSavedJobsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetSavedJobsQuery(request), cancellationToken);

        return Ok(new PagedResponse<SavedJobDto>(page));
    }

    /// <summary>POST /api/v1/worker/saved-jobs/{jobId} — bookmarks a job.</summary>
    /// <remarks>Saving a job that is already saved returns the existing bookmark.</remarks>
    [HttpPost("{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SavedJobDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SavedJobDto>>> Save(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var saved = await mediator.Send(new SaveJobCommand(jobId), cancellationToken);

        return Created(
            $"/api/v1/worker/saved-jobs/{jobId}",
            new ApiResponse<SavedJobDto>(saved, "Job saved."));
    }

    /// <summary>DELETE /api/v1/worker/saved-jobs/{jobId} — removes a bookmark.</summary>
    /// <remarks>Reports whether a bookmark was actually there, so the app can refresh either way.</remarks>
    [HttpDelete("{jobId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<bool>>> Unsave(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var removed = await mediator.Send(new UnsaveJobCommand(jobId), cancellationToken);

        return Ok(new ApiResponse<bool>(removed, removed ? "Job removed from saved." : "Job was not saved."));
    }
}
