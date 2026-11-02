using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Verifications;
using Jobmate.Application.Features.Verifications.Commands;
using Jobmate.Application.Features.Verifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// The trust badge, and the requests behind it. The badge belongs to the person, so it
/// is the same in worker and work giver mode.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/verifications")]
[ApiVersion("1.0")]
[Produces("application/json")]
[Authorize]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class VerificationController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/verifications — the badge plus the history behind it.</summary>
    /// <remarks>
    /// Returns the overall badge, the state of each individual check, and every attempt
    /// newest first. Pass <c>type</c> to list only one kind of check — that is what a
    /// checklist row's "try again" link uses.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MyVerificationsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MyVerificationsDto>>> Mine(
        [FromQuery] GetMyVerificationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetMyVerificationsQuery(request), cancellationToken);

        return Ok(new ApiResponse<MyVerificationsDto>(result));
    }

    /// <summary>GET /api/v1/verifications/summary — the badge on its own.</summary>
    /// <remarks>
    /// Cheap enough to call from the app shell on any screen. <c>overall</c> is
    /// <c>verified</c> only when identity, phone, email and skills are all approved; a
    /// rejection anywhere outranks that.
    /// </remarks>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<VerificationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<VerificationSummaryDto>>> Summary(
        CancellationToken cancellationToken)
    {
        var summary = await mediator.Send(new GetVerificationSummaryQuery(), cancellationToken);

        return Ok(new ApiResponse<VerificationSummaryDto>(summary));
    }

    /// <summary>POST /api/v1/verifications — submits one kind of check; it is approved immediately.</summary>
    /// <remarks>
    /// Send the <c>documentUrl</c> returned by <c>POST /profile/documents</c> to attach the
    /// uploaded file. The response is the approved request, and the user's badge is
    /// recomputed. A type that still has a pending request from before auto-approval is a
    /// 409 until that request is withdrawn.
    ///
    /// Phone and email checks are refused up front if the account has no number or address
    /// to check.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Verification)]
    [ProducesResponseType(typeof(ApiResponse<VerificationRequestDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<VerificationRequestDto>>> Submit(
        [FromBody] SubmitVerificationRequest? request,
        CancellationToken cancellationToken)
    {
        var submitted = await mediator.Send(
            new SubmitVerificationCommand(request ?? new SubmitVerificationRequest()),
            cancellationToken);

        return Created(
            $"/api/v1/verifications",
            new ApiResponse<VerificationRequestDto>(submitted, "Verification approved."));
    }

    /// <summary>POST /api/v1/verifications/{id}/withdraw — takes a pending request back.</summary>
    /// <remarks>
    /// Not a delete: the attempt stays in the history, recorded as withdrawn, so it is
    /// clear the applicant chose to stop rather than being refused. Only a pending request
    /// can be withdrawn; new requests are approved on submission, so an approved one is a 400.
    /// </remarks>
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(ApiResponse<VerificationRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<VerificationRequestDto>>> Withdraw(
        Guid id,
        [FromBody] WithdrawVerificationRequest? request,
        CancellationToken cancellationToken)
    {
        var withdrawn = await mediator.Send(
            new WithdrawVerificationCommand(id, request ?? new WithdrawVerificationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<VerificationRequestDto>(withdrawn, "Verification request withdrawn."));
    }
}
