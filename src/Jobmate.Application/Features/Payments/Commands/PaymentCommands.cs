using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Payments;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Payments.Commands;

/// <summary>
/// Releases payment for one booking. POST /api/v1/jobs/{jobId}/payments/{workerId} —
/// the Flutter "Release payment" screen, which previews base, overtime and the 5% fee.
///
/// The charge is attempted inline so the app gets a receipt (or a failure) in one round
/// trip instead of waiting for a webhook it has no endpoint for yet. The request is
/// idempotent: a replay returns the original payment rather than charging twice.
/// </summary>
public sealed record CreatePaymentCommand(Guid JobId, Guid WorkerId, CreatePaymentRequest Request)
    : IRequest<PaymentReceiptDto>;

public sealed class CreatePaymentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IPaymentService gateway,
    INotificationService notifications) : IRequestHandler<CreatePaymentCommand, PaymentReceiptDto>
{
    public async Task<PaymentReceiptDto> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        // Tracked, because the booking is updated with the payment link below.
        var assignment = await db.WorkerAssignments
            .Include(a => a.Job)
            .FirstOrDefaultAsync(
                a => a.JobId == request.JobId && a.WorkerId == request.WorkerId,
                cancellationToken)
            ?? throw new NotFoundException("That worker is not booked for this job.");

        var giver = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == giverId, cancellationToken)
            ?? throw new NotFoundException("Account not found.");

        // A booking carries at most one payment, so an absent client key falls back to
        // one derived from the booking itself. That makes the common case — the app
        // retrying its own request — resolve to the same payment without the client
        // having to remember to send a key.
        var key = NullIfBlank(request.Request.IdempotencyKey) ?? DerivedKey(assignment.Id);

        var existing = await db.Payments
            .Include(p => p.Job)
            .Include(p => p.Worker)
            .FirstOrDefaultAsync(
                p => p.GiverId == giverId && p.IdempotencyKey == key,
                cancellationToken);

        // A settled or in-flight payment is replayed, never re-charged. Only a payment
        // the gateway actually rejected is safe to try again, because then no money moved.
        if (existing is not null && existing.Status != PaymentStatus.Failed)
        {
            return existing.ToReceiptDto();
        }

        if (existing is null)
        {
            // A different key can still name a booking that was already paid for, and the
            // one-payment-per-booking index would reject the insert. Detecting it here
            // turns a database error into the original receipt.
            var paid = await db.Payments
                .AsNoTracking()
                .Include(p => p.Job)
                .Include(p => p.Worker)
                .FirstOrDefaultAsync(p => p.AssignmentId == assignment.Id, cancellationToken);

            if (paid is not null)
            {
                return paid.ToReceiptDto();
            }
        }

        var isRetry = existing is not null;

        var payment = isRetry
            ? Retry(existing!, request.Request)
            : PaymentRules.Create(
                assignment,
                giver,
                request.Request.BasePay,
                request.Request.Overtime,
                request.Request.Method,
                key,
                now);

        if (!isRetry)
        {
            db.Payments.Add(payment);
        }

        var charge = await gateway.ChargeAsync(
            new PaymentChargeRequest(
                payment.Id,
                payment.Amount,
                payment.OvertimeAmount,
                payment.PlatformFee,
                payment.TotalAmount,
                payment.Currency,
                payment.PaymentMethod,
                payment.IdempotencyKey,
                $"Jobmate payout for \"{assignment.Job?.Title}\""),
            cancellationToken);

        if (!charge.Success)
        {
            PaymentRules.Fail(payment, charge.FailureReason ?? "The payment could not be completed.");

            // The failed attempt is kept so the giver sees why in their history and can
            // retry the same booking; the error is still surfaced to the caller.
            await db.SaveChangesAsync(cancellationToken);

            throw new BusinessRuleException(
                charge.FailureReason ?? "The payment could not be completed.",
                "PaymentFailed");
        }

        PaymentRules.Confirm(payment, charge.TransactionReference ?? payment.Id.ToString(), now);

        // The booking is the app's record that this shift has been paid, and its payment
        // status is what the worker's job card and work history show.
        assignment.PaymentId = payment.Id;
        assignment.PaidAt = now;
        assignment.Payment = payment;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            assignment.WorkerId,
            NotificationType.Payment,
            "Payment released",
            $"You earned {payment.WorkerNetAmount} for \"{assignment.Job?.Title}\".",
            NotificationReferenceType.Payment,
            payment.Id,
            NotificationRoutes.Earnings,
            cancellationToken);

        return payment.ToReceiptDto();
    }

    /// <summary>
    /// Re-arms a previously failed charge. The row keeps its id and booking, so the
    /// one-payment-per-booking index still holds and the app keeps one history line.
    /// </summary>
    private static Jobmate.Domain.Entities.Payment Retry(
        Jobmate.Domain.Entities.Payment payment,
        CreatePaymentRequest request)
    {
        PaymentRules.Retry(payment, request.BasePay, request.Overtime, request.Method);

        return payment;
    }

    /// <summary>Stable key for a booking when the client did not supply one.</summary>
    private static string DerivedKey(Guid assignmentId) => $"assignment:{assignmentId}";

    internal static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Confirms a payment the gateway left in flight.
/// POST /api/v1/payments/{id}/confirm.
///
/// A real PSP will call this from a signed webhook; until one is wired up the giver who
/// owns the payment may confirm it by hand, which is what lets the app's "I have paid"
/// step work end to end against the mock gateway.
/// </summary>
public sealed record ConfirmPaymentCommand(Guid PaymentId, ConfirmPaymentRequest Request)
    : IRequest<PaymentReceiptDto>;

public sealed class ConfirmPaymentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<ConfirmPaymentCommand, PaymentReceiptDto>
{
    public async Task<PaymentReceiptDto> Handle(ConfirmPaymentCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var payment = await db.Payments
            .Include(p => p.Job)
            .Include(p => p.Worker)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.GiverId != callerId)
        {
            throw new ForbiddenException("Only the person who made this payment can confirm it.");
        }

        // Confirming is a no-op once the payment is settled, so a duplicated webhook or
        // a second tap on "I have paid" cannot fail or double-count.
        var wasSettled = payment.Status == PaymentStatus.Paid;

        PaymentRules.Confirm(
            payment,
            CreatePaymentCommandHandler.NullIfBlank(request.Request.TransactionReference)
                ?? payment.TransactionReference
                ?? payment.Id.ToString(),
            now);

        var assignment = await db.WorkerAssignments
            .FirstOrDefaultAsync(a => a.Id == payment.AssignmentId, cancellationToken);

        if (assignment is not null)
        {
            assignment.PaidAt ??= payment.PaidAt ?? now;
            assignment.Payment = payment;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (!wasSettled && payment.Status == PaymentStatus.Paid)
        {
            await notifications.SendAsync(
                payment.WorkerId,
                NotificationType.Payment,
                "Payment released",
                $"You earned {payment.WorkerNetAmount} for \"{payment.Job?.Title}\".",
                NotificationReferenceType.Payment,
                payment.Id,
                NotificationRoutes.Earnings,
                cancellationToken);
        }

        return payment.ToReceiptDto();
    }
}

/// <summary>
/// Refunds a settled payment. POST /api/v1/payments/{id}/refund — admin only.
///
/// A refund is money leaving the platform, so it is deliberately not available to the
/// giver or the worker: only an administrator can reverse a charge, and the reason is
/// kept on the payment for the audit trail.
/// </summary>
public sealed record RefundPaymentCommand(Guid PaymentId, RefundPaymentRequest Request)
    : IRequest<PaymentReceiptDto>;

public sealed class RefundPaymentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IPaymentService gateway,
    INotificationService notifications) : IRequestHandler<RefundPaymentCommand, PaymentReceiptDto>
{
    public async Task<PaymentReceiptDto> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireRole(nameof(UserRole.Admin));
        var now = clock.UtcNow;

        var payment = await db.Payments
            .Include(p => p.Job)
            .Include(p => p.Worker)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        var reason = CreatePaymentCommandHandler.NullIfBlank(request.Request.Reason)
            ?? throw new BusinessRuleException("Give a reason for the refund.", "ReasonRequired");

        // The rule rejects anything that is not settled or disputed, so an in-flight or
        // already-refunded payment cannot be reversed twice.
        PaymentRules.Refund(payment, adminId, now);

        var result = await gateway.RefundAsync(
            new PaymentRefundRequest(payment.Id, payment.TotalAmount, reason, $"refund:{payment.Id}"),
            cancellationToken);

        if (!result.Success)
        {
            throw new BusinessRuleException(
                result.FailureReason ?? "The refund could not be completed.",
                "RefundFailed");
        }

        // The provider's reversal reference replaces the charge reference, and the reason
        // is kept so the audit trail says why the money went back.
        payment.TransactionReference = result.Reference ?? payment.TransactionReference;
        payment.FailureReason = reason;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            payment.GiverId,
            NotificationType.Payment,
            "Payment refunded",
            $"The payment for \"{payment.Job?.Title}\" was refunded.",
            NotificationReferenceType.Payment,
            payment.Id,
            NotificationRoutes.MyJobs,
            cancellationToken);

        return payment.ToReceiptDto();
    }
}
