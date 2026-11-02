using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// Worker payout for one booking, including overtime and the platform fee.
/// Defaults match the Flutter payment screen's 5% platform fee and its
/// "Total to worker = base + overtime - fee" summary.
/// </summary>
public sealed record PayoutBreakdown(int BaseAmount, int OvertimeAmount, int PlatformFee)
{
    /// <summary>What the giver is charged.</summary>
    public int GiverTotal => BaseAmount + OvertimeAmount + PlatformFee;

    /// <summary>What the worker receives.</summary>
    public int WorkerNet => BaseAmount + OvertimeAmount - PlatformFee;
}

/// <summary>Payment rules: pricing, who may pay, and settlement transitions.</summary>
public static class PaymentRules
{
    /// <summary>Platform fee percentage, mirrored from the Flutter payment screen.</summary>
    public const decimal PlatformFeePercent = 5m;

    public const int MaxPaymentAmount = 10_000_000;

    public static PayoutBreakdown Calculate(int baseAmount, int? overtimeAmount)
    {
        if (baseAmount <= 0)
        {
            throw new BusinessRuleException("Enter the base pay for this worker.", "PayRequired");
        }

        if (baseAmount > MaxPaymentAmount)
        {
            throw new BusinessRuleException("Base pay is unrealistically high.", "PayInvalid");
        }

        var overtime = overtimeAmount ?? 0;

        if (overtime < 0)
        {
            throw new BusinessRuleException("Overtime cannot be negative.", "OvertimeInvalid");
        }

        if (baseAmount + overtime > MaxPaymentAmount)
        {
            throw new BusinessRuleException("Payment amount is unrealistically high.", "PayInvalid");
        }

        // Round half-away-from-zero, matching Dart's .round() on a positive double.
        var fee = (int)Math.Round((baseAmount + overtime) * (double)PlatformFeePercent / 100d, MidpointRounding.AwayFromZero);

        return new PayoutBreakdown(baseAmount, overtime, fee);
    }

    public static Payment Create(
        WorkerAssignment assignment,
        User giver,
        int baseAmount,
        int? overtimeAmount,
        PaymentMethod method,
        string idempotencyKey,
        DateTime now)
    {
        if (assignment.GiverId != giver.Id)
        {
            throw new ForbiddenException("Only the work giver who owns this job can pay for it.");
        }

        if (assignment.Status is AssignmentStatus.Cancelled)
        {
            throw new BusinessRuleException("A cancelled booking cannot be paid.", "AssignmentClosed");
        }

        if (assignment.Status == AssignmentStatus.Completed && assignment.Payment?.Status == PaymentStatus.Paid)
        {
            throw new BusinessRuleException("This booking has already been paid.", "AlreadyPaid");
        }

        var breakdown = Calculate(baseAmount, overtimeAmount);

        if (breakdown.WorkerNet <= 0)
        {
            throw new BusinessRuleException(
                "The platform fee exceeds the payout. Increase the pay amount.",
                "PayoutTooSmall");
        }

        return new Payment
        {
            JobId = assignment.JobId,
            WorkerId = assignment.WorkerId,
            GiverId = assignment.GiverId,
            AssignmentId = assignment.Id,
            Amount = breakdown.BaseAmount,
            OvertimeAmount = breakdown.OvertimeAmount,
            PlatformFee = breakdown.PlatformFee,
            TotalAmount = breakdown.GiverTotal,
            PaymentMethod = method,
            Status = PaymentStatus.Processing,
            IdempotencyKey = idempotencyKey,
            AgreedPaySnapshot = assignment.TotalPay,
            CreatedAt = now
        };
    }

    /// <summary>Records a confirmed provider result. Never called by a client endpoint directly.</summary>
    public static void Confirm(Payment payment, string transactionReference, DateTime now)
    {
        if (payment.Status == PaymentStatus.Paid)
        {
            return;
        }

        if (!PaymentStatusRules.CanTransition(payment.Status, PaymentStatus.Paid))
        {
            throw new BusinessRuleException(
                $"A {payment.Status.ToString().ToLowerInvariant()} payment cannot be confirmed.",
                "InvalidPaymentTransition");
        }

        payment.Status = PaymentStatus.Paid;
        payment.TransactionReference = transactionReference;
        payment.PaidAt = now;
    }

    public static void Fail(Payment payment, string reason)
    {
        if (!PaymentStatusRules.CanTransition(payment.Status, PaymentStatus.Failed))
        {
            return;
        }

        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = reason;
    }

    /// <summary>
    /// Re-arms a payment whose charge attempt failed so the booking can be paid.
    ///
    /// A retry reuses the existing row rather than creating another one, because the
    /// database allows only one payment per booking. That is safe precisely because a
    /// failed charge moved no money: re-charging it cannot double-pay, whereas a
    /// settled payment is left alone and simply replayed to the caller.
    /// </summary>
    public static void Retry(Payment payment, int baseAmount, int? overtimeAmount, PaymentMethod method)
    {
        if (payment.Status != PaymentStatus.Failed)
        {
            throw new BusinessRuleException(
                "Only a failed payment can be charged again.",
                "InvalidPaymentTransition");
        }

        var breakdown = Calculate(baseAmount, overtimeAmount);

        if (breakdown.WorkerNet <= 0)
        {
            throw new BusinessRuleException(
                "The platform fee exceeds the payout. Increase the pay amount.",
                "PayoutTooSmall");
        }

        payment.Amount = breakdown.BaseAmount;
        payment.OvertimeAmount = breakdown.OvertimeAmount;
        payment.PlatformFee = breakdown.PlatformFee;
        payment.TotalAmount = breakdown.GiverTotal;
        payment.PaymentMethod = method;
        payment.Status = PaymentStatus.Processing;
        payment.FailureReason = null;
    }
}
