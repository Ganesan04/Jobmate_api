namespace Jobmate.Domain.Enums;

/// <summary>
/// Payment lifecycle. The four values the Flutter app renders (pending,
/// processing, paid, disputed) are a subset; failed, refunded and cancelled are
/// additional states the backend uses for real provider outcomes.
/// </summary>
public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Paid = 3,
    Disputed = 4,
    Failed = 5,
    Refunded = 6,
    Cancelled = 7
}

/// <summary>Payment statuses that still count as "not yet settled" in the worker wallet.</summary>
public static class PaymentStatusRules
{
    /// <summary>Flutter <c>PaymentStatus.pending</c> or <c>processing</c>.</summary>
    public static bool IsOutstanding(PaymentStatus status) =>
        status is PaymentStatus.Pending or PaymentStatus.Processing;

    /// <summary>Flutter <c>PaymentStatus.paid</c>.</summary>
    public static bool IsSettled(PaymentStatus status) => status == PaymentStatus.Paid;

    /// <summary>Flutter <c>PaymentStatus.disputed</c>.</summary>
    public static bool IsDisputed(PaymentStatus status) => status == PaymentStatus.Disputed;

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) => (from, to) switch
    {
        (PaymentStatus.Pending, PaymentStatus.Processing) => true,
        (PaymentStatus.Pending, PaymentStatus.Cancelled) => true,
        (PaymentStatus.Processing, PaymentStatus.Paid) => true,
        (PaymentStatus.Processing, PaymentStatus.Failed) => true,
        (PaymentStatus.Failed, PaymentStatus.Processing) => true,
        (PaymentStatus.Paid, PaymentStatus.Disputed) => true,
        (PaymentStatus.Paid, PaymentStatus.Refunded) => true,
        (PaymentStatus.Disputed, PaymentStatus.Paid) => true,
        (PaymentStatus.Disputed, PaymentStatus.Refunded) => true,
        _ => false
    };
}
