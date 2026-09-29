using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Application.DTOs.Verifications;
using Jobmate.Application.Features.Admin.Commands;
using Jobmate.Application.Features.Admin.Queries;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// The admin back office: the dashboard, the verification review queue, and the user and
/// job tables.
///
/// The <c>Admin</c> role is required on the whole controller, so every endpoint here is
/// unreachable to a worker or a work giver, and each read goes through a MediatR query
/// rather than a direct database call.
///
/// These are the only endpoints that return phone numbers, email addresses and exact
/// areas for other people. That is deliberate and confined to this controller: an admin
/// has to be able to identify and contact the person in front of them, and the privacy
/// rules for the marketplace itself are enforced by the user-facing projections rather
/// than by hiding fields from this one screen.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/admin")]
[ApiVersion("1.0")]
[Authorize(Roles = nameof(UserRole.Admin))]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class AdminController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/admin/dashboard — the counts the admin home screen shows.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<AdminStatsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminStatsDto>>> Dashboard(CancellationToken cancellationToken)
    {
        var stats = await mediator.Send(new GetAdminStatsQuery(), cancellationToken);

        return Ok(new ApiResponse<AdminStatsDto>(stats));
    }

    // ------------------------------------------------------------------ verification

    /// <summary>GET /api/v1/admin/verifications — the review queue.</summary>
    /// <remarks>
    /// Pending requests oldest first, which is the order they should be worked in: each
    /// one is somebody waiting for an answer. Add <c>status=rejected</c> to revisit
    /// decisions, or <c>type=identity</c> to work one kind of check at a time.
    ///
    /// The applicant's phone number is included so a document that looks wrong can be
    /// checked with the person before it is refused.
    /// </remarks>
    [HttpGet("verifications")]
    [ProducesResponseType(typeof(PagedResponse<AdminVerificationRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminVerificationRequestDto>>> Verifications(
        [FromQuery] GetAdminVerificationsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetAdminVerificationsQuery(request), cancellationToken);

        return Ok(new PagedResponse<AdminVerificationRequestDto>(page));
    }

    /// <summary>POST /api/v1/admin/verifications/{id}/approve — accepts the document.</summary>
    /// <remarks>
    /// An empty body approves. Approving updates the applicant's trust badge straight away,
    /// and the badge only turns green once all four consumer checks are approved, so this
    /// is one step towards verified rather than verified itself.
    /// </remarks>
    [HttpPost("verifications/{id:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<AdminVerificationRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminVerificationRequestDto>>> ApproveVerification(
        Guid id,
        [FromBody] ReviewVerificationRequest? request,
        CancellationToken cancellationToken)
    {
        var reviewed = await mediator.Send(
            new ApproveVerificationCommand(id, request ?? new ReviewVerificationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<AdminVerificationRequestDto>(reviewed, "Verification approved."));
    }

    /// <summary>POST /api/v1/admin/verifications/{id}/reject — refuses it, with a reason.</summary>
    /// <remarks>
    /// <c>rejectionReason</c> is required and is shown to the applicant verbatim, so it has
    /// to say what to fix — "photo too dark", not "rejected". The request keeps the reason,
    /// and the applicant can submit again afterwards.
    /// </remarks>
    [HttpPost("verifications/{id:guid}/reject")]
    [ProducesResponseType(typeof(ApiResponse<AdminVerificationRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminVerificationRequestDto>>> RejectVerification(
        Guid id,
        [FromBody] ReviewVerificationRequest? request,
        CancellationToken cancellationToken)
    {
        var reviewed = await mediator.Send(
            new RejectVerificationCommand(id, request ?? new ReviewVerificationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<AdminVerificationRequestDto>(reviewed, "Verification rejected."));
    }

    // ------------------------------------------------------------------------- users

    /// <summary>GET /api/v1/admin/users — the users table.</summary>
    /// <remarks>
    /// The activity column means jobs posted for a work giver and applications made for a
    /// worker. Filter with <c>role</c>, <c>isActive</c> or <c>verification</c>; search
    /// matches name, email and phone number.
    /// </remarks>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminUserDto>>> Users(
        [FromQuery] GetAdminUsersRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetAdminUsersQuery(request), cancellationToken);

        return Ok(new PagedResponse<AdminUserDto>(page));
    }

    /// <summary>POST /api/v1/admin/users/{id}/status — suspends or reinstates an account.</summary>
    /// <remarks>
    /// A suspension, not a deletion: the account keeps its jobs, bookings and messages, so
    /// reinstating it puts the person back where they were. The body is required, because
    /// an absent body would otherwise read as "suspend".
    /// </remarks>
    [HttpPost("users/{id:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> SetUserStatus(
        Guid id,
        [FromBody] SetUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var user = await mediator.Send(new SetUserStatusCommand(id, request), cancellationToken);

        return Ok(new ApiResponse<AdminUserDto>(user));
    }

    // ------------------------------------------------------------------------- jobs

    /// <summary>GET /api/v1/admin/jobs — the jobs table.</summary>
    /// <remarks>
    /// Filter by <c>status</c> or <c>category</c>; search matches the title and the hiring
    /// company. <c>applicants</c> is a count, so the table does not have to load every
    /// application to draw a row.
    /// </remarks>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(PagedResponse<AdminJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminJobDto>>> Jobs(
        [FromQuery] GetAdminJobsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetAdminJobsQuery(request), cancellationToken);

        return Ok(new PagedResponse<AdminJobDto>(page));
    }

    /// <summary>GET /api/v1/admin/jobs/{id} — the job detail panel.</summary>
    /// <remarks>
    /// The row plus the two things an admin opens a job to find out: how much of the
    /// applicant pipeline converted into bookings, and how much was actually paid.
    /// </remarks>
    [HttpGet("jobs/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminJobDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminJobDetailDto>>> Job(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await mediator.Send(new GetAdminJobDetailQuery(id), cancellationToken);

        return Ok(new ApiResponse<AdminJobDetailDto>(detail));
    }
}
