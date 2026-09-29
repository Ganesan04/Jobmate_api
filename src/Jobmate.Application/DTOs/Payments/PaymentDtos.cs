using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Payments;

/// <summary>
/// Flutter <c>PaymentReceipt</c> — the giver's payment success screen:
/// paymentId, amount, method, paidAt, workerName, fee, overtime, base.
/// </summary>
public sealed record PaymentReceiptDto
{
    /// <summary>Human-facing reference, formatted <c>PAY-000123</c> as the app shows it.</summary>
    public string PaymentId { get; init; } = string.Empty;

    public Guid Id { get; init; }

    /// <summary>
    /// What the giver was charged, i.e. base + overtime + fee. This is the Dart
    /// <c>PaymentReceipt.amount</c>, which the app derives <c>base</c> from.
    /// </summary>
    public int Amount { get; init; }

    public PaymentMethod Method { get; init; }

    public DateTime? PaidAt { get; init; }

    public string WorkerName { get; init; } = string.Empty;

    public int Fee { get; init; }

    public int Overtime { get; init; }

    /// <summary>The agreed base pay, matching the Dart <c>PaymentReceipt.base</c> getter.</summary>
    public int Base { get; init; }

    public PaymentStatus Status { get; init; }

    public string Currency { get; init; } = "INR";

    public string? TransactionReference { get; init; }

    public Guid AssignmentId { get; init; }

    public Guid JobId { get; init; }

    public Guid WorkerId { get; init; }

    public Guid GiverId { get; init; }

    /// <summary>What the giver was charged: base + overtime + fee.</summary>
    public int TotalAmount { get; init; }

    /// <summary>What the worker nets: base + overtime - fee.</summary>
    public int WorkerNetAmount { get; init; }

    public string JobTitle { get; init; } = string.Empty;
}

/// <summary>Flutter <c>Earning</c> — one worker wallet line.</summary>
public sealed record EarningDto
{
    public Guid Id { get; init; }

    public Guid WorkerId { get; init; }

    public Guid JobId { get; init; }

    public string JobTitle { get; init; } = string.Empty;

    public string Company { get; init; } = string.Empty;

    public DateTime Date { get; init; }

    /// <summary>What the worker nets: base + overtime - fee.</summary>
    public int Amount { get; init; }

    public PaymentStatus Status { get; init; }

    /// <summary>
    /// The booking this earning came from. The app uses it to keep the record
    /// idempotent, so one shift can only ever be paid once.
    /// </summary>
    public Guid? AssignmentId { get; init; }

    public string? PaymentId { get; init; }

    public PaymentMethod Method { get; init; }

    public double? Hours { get; init; }
}

/// <summary>Flutter <c>EarningSummary</c> — month-grouped wallet totals.</summary>
public sealed record EarningSummaryDto
{
    /// <summary>ISO <c>yyyy-MM</c> key, used for ordering.</summary>
    public string Month { get; init; } = string.Empty;

    /// <summary>Display label, e.g. "September 2026", matching the app's month grouping.</summary>
    public string Label { get; init; } = string.Empty;

    public int Earned { get; init; }

    public int Pending { get; init; }

    public int Total => Earned + Pending;
}

/// <summary>Worker wallet totals shown on the earnings screen.</summary>
public sealed record WalletSummaryDto
{
    public int TotalEarned { get; init; }

    public int PendingAmount { get; init; }

    public int DisputedAmount { get; init; }

    public int EntryCount { get; init; }
}
