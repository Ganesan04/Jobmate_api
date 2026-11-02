using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Payments;

/// <summary>
/// POST /jobs/{jobId}/payments/{workerId} — the Flutter "Release payment" screen:
/// base pay, optional overtime, method, plus the 5% platform fee the app previews.
/// </summary>
public sealed record CreatePaymentRequest
{
    public int BasePay { get; init; }

    public int Overtime { get; init; }

    public PaymentMethod Method { get; init; } = PaymentMethod.Upi;

    /// <summary>
    /// Client-generated key that makes the request idempotent. When omitted the
    /// server derives a stable key from the assignment, so a retry is still safe.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>POST /payments/{id}/confirm — a provider callback or a manual confirm.</summary>
public sealed record ConfirmPaymentRequest
{
    public string? TransactionReference { get; init; }
}

/// <summary>GET /payments filters.</summary>
public sealed record GetPaymentsRequest : Domain.Common.PageRequest
{
    public List<PaymentStatus>? Statuses { get; init; }

    public Guid? JobId { get; init; }

    public Guid? WorkerId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <summary>GET /worker/earnings filters.</summary>
public sealed record WorkerEarningsRequest : Domain.Common.PageRequest
{
    public List<PaymentStatus>? Statuses { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
