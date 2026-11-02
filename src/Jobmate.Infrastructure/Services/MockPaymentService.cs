using Jobmate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Jobmate.Infrastructure.Services;

/// <summary>
/// Development payment gateway. It approves charges without contacting
/// a bank, which keeps the booking and wallet flows testable end to end.
///
/// It deliberately refuses to fabricate success for the failure cases the app must
/// handle: an amount of zero, a mismatched total, and any amount above a ceiling are
/// rejected, so error handling is exercised rather than hidden.
/// </summary>
public sealed class MockPaymentService(ILogger<MockPaymentService> logger) : IPaymentService
{
    /// <summary>Upper bound for a single charge, to catch an arithmetic bug early.</summary>
    public const int MaxChargeAmount = 10_000_000;

    public string ProviderName => "mock";

    public Task<PaymentChargeResult> ChargeAsync(
        PaymentChargeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            logger.LogWarning("Mock gateway refused payment {PaymentId}: non-positive amount", request.PaymentId);
            return Task.FromResult(PaymentChargeResult.Failed("Amount must be greater than zero."));
        }

        if (request.TotalAmount != request.Amount + request.OvertimeAmount + request.PlatformFee)
        {
            logger.LogWarning(
                "Mock gateway refused payment {PaymentId}: total {Total} does not equal the components",
                request.PaymentId,
                request.TotalAmount);

            return Task.FromResult(PaymentChargeResult.Failed("The payment total is incorrect."));
        }

        if (request.TotalAmount > MaxChargeAmount)
        {
            return Task.FromResult(PaymentChargeResult.Failed("The payment amount is above the permitted limit."));
        }

        var reference = $"MOCK-{request.PaymentId.ToString()[..8].ToUpperInvariant()}";

        logger.LogInformation(
            "Mock gateway approved {Amount} {Currency} for payment {PaymentId} as {Reference}",
            request.TotalAmount,
            request.Currency,
            request.PaymentId,
            reference);

        return Task.FromResult(PaymentChargeResult.Succeeded(
            reference,
            $"{{\"provider\":\"mock\",\"idempotencyKey\":\"{request.IdempotencyKey}\"}}"));
    }

    /// <summary>
    /// Always false: the mock gateway issues no webhooks, so any webhook arriving for
    /// it is unsolicited and must not be trusted.
    /// </summary>
    public Task<bool> VerifyWebhookSignatureAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
