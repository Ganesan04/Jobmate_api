using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Payments;
using Jobmate.Application.Features.Payments.Commands;
using Jobmate.Application.Features.Payments.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Money: releasing pay for a booking, listing the caller's payments, and the
/// administrator's refund.
///
/// Creating a payment hangs off the job route, because the app releases pay from a
/// job and names the worker: POST /jobs/{jobId}/payments/{workerId}. The remaining
/// actions are keyed by payment id and live here.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/payments")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class PaymentController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/payments — the caller's own payments, newest first.</summary>
    /// <remarks>
    /// Scoped by role: a giver sees what they paid out, a worker what they received, an
    /// administrator everything. Optional filters: <c>statuses</c>, <c>jobId</c>,
    /// <c>workerId</c>, <c>from</c>, <c>to</c>.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponse<PaymentReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<PaymentReceiptDto>>> List(
        [FromQuery] GetPaymentsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetPaymentsQuery(request), cancellationToken);

        return Ok(new PagedResponse<PaymentReceiptDto>(page));
    }

    /// <summary>POST /api/v1/payments/{id}/confirm — settles a payment the gateway left in flight.</summary>
    /// <remarks>
    /// A real gateway will call this from a signed webhook. Until one is wired up the
    /// giver who made the payment may confirm it by hand. Confirming a payment that is
    /// already settled is a no-op, so a duplicated callback is harmless.
    /// </remarks>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<PaymentReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentReceiptDto>>> Confirm(
        Guid id,
        [FromBody] ConfirmPaymentRequest? request,
        CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(
            new ConfirmPaymentCommand(id, request ?? new ConfirmPaymentRequest()),
            cancellationToken);

        return Ok(new ApiResponse<PaymentReceiptDto>(receipt, "Payment confirmed."));
    }

    /// <summary>POST /api/v1/payments/{id}/refund — reverses a settled payment. Administrator only.</summary>
    /// <remarks>
    /// Deliberately not available to the giver or the worker: reversing a charge moves
    /// money back out of the platform, so only an administrator can do it and the
    /// reason is recorded on the payment.
    /// </remarks>
    [HttpPost("{id:guid}/refund")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<PaymentReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentReceiptDto>>> Refund(
        Guid id,
        [FromBody] RefundPaymentRequest? request,
        CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(
            new RefundPaymentCommand(id, request ?? new RefundPaymentRequest()),
            cancellationToken);

        return Ok(new ApiResponse<PaymentReceiptDto>(receipt, "Payment refunded."));
    }
}

/// <summary>
/// The worker wallet: earnings, month-grouped totals and the summary the earnings
/// screen header shows.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/worker")]
[ApiVersion("1.0")]
[Authorize(Roles = "Worker")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class WorkerEarningsController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/worker/earnings — one wallet line per booking.</summary>
    /// <remarks>
    /// A booking appears as soon as it is made, with its money still pending, and is
    /// never listed twice: the line is keyed by the booking, not by the payment.
    /// </remarks>
    [HttpGet("earnings")]
    [ProducesResponseType(typeof(PagedResponse<EarningDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EarningDto>>> List(
        [FromQuery] WorkerEarningsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetEarningsQuery(request), cancellationToken);

        return Ok(new PagedResponse<EarningDto>(page));
    }

    /// <summary>GET /api/v1/worker/earnings/summary — earned and pending money per month.</summary>
    [HttpGet("earnings/summary")]
    [ProducesResponseType(typeof(ApiResponse<List<EarningSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<EarningSummaryDto>>>> Summary(
        CancellationToken cancellationToken)
    {
        var summary = await mediator.Send(new GetEarningSummaryQuery(), cancellationToken);

        return Ok(new ApiResponse<List<EarningSummaryDto>>(summary, "Earning summary."));
    }

    /// <summary>GET /api/v1/worker/wallet — earned, pending and disputed totals.</summary>
    [HttpGet("wallet")]
    [ProducesResponseType(typeof(ApiResponse<WalletSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<WalletSummaryDto>>> Wallet(CancellationToken cancellationToken)
    {
        var wallet = await mediator.Send(new GetWalletSummaryQuery(), cancellationToken);

        return Ok(new ApiResponse<WalletSummaryDto>(wallet, "Wallet summary."));
    }
}

/// <summary>
/// The giver side of releasing pay. Separate from <see cref="PaymentController"/>
/// because the route is nested under a job: the app releases pay from a job and names
/// the worker it is paying.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/jobs/{jobId:guid}/payments")]
[ApiVersion("1.0")]
[Authorize(Roles = "WorkGiver")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class JobPaymentController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/jobs/{jobId}/payments/{workerId} — releases pay for one booking.</summary>
    /// <remarks>
    /// Sends <c>basePay</c>, optional <c>overtime</c> and the <c>method</c>; the 5%
    /// platform fee and the totals are worked out server side so the client cannot
    /// disagree with them. Supply <c>idempotencyKey</c> to make a retry explicit —
    /// without one, a stable key is derived from the booking, so a repeated request
    /// returns the original receipt instead of paying twice.
    /// </remarks>
    [HttpPost("{workerId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PaymentReceiptDto>>> Create(
        Guid jobId,
        Guid workerId,
        [FromBody] CreatePaymentRequest? request,
        CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(
            new CreatePaymentCommand(jobId, workerId, request ?? new CreatePaymentRequest()),
            cancellationToken);

        return Ok(new ApiResponse<PaymentReceiptDto>(receipt, "Payment released."));
    }
}
