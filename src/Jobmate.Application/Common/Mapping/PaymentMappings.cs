using System.Globalization;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Payments;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Entity to DTO projections for payments, earnings and work history.
///
/// The money split is easy to get backwards, so it is written out once here:
/// the giver is charged <c>base + overtime + fee</c> and the worker nets
/// <c>base + overtime - fee</c>. Giver-facing DTOs therefore report the charge,
/// while the worker-facing wallet reports the net.
/// </summary>
public static class PaymentMappings
{
    /// <summary>
    /// Projects a payment as the giver's receipt.
    ///
    /// <see cref="PaymentReceiptDto.Amount"/> is the amount charged and
    /// <see cref="PaymentReceiptDto.Base"/> is the agreed base pay, which is what the
    /// Dart <c>PaymentReceipt.base</c> getter computes as <c>amount - fee - overtime</c>.
    /// </summary>
    public static PaymentReceiptDto ToReceiptDto(this Payment payment)
    {
        return new PaymentReceiptDto
        {
            PaymentId = ReceiptNumber(payment.Id),
            Id = payment.Id,
            Amount = payment.TotalAmount,
            Base = payment.Amount,
            Overtime = payment.OvertimeAmount,
            Fee = payment.PlatformFee,
            Method = payment.PaymentMethod,
            Status = payment.Status,
            Currency = payment.Currency,
            TransactionReference = payment.TransactionReference,
            PaidAt = payment.PaidAt,
            WorkerName = payment.Worker?.FullName ?? string.Empty,
            AssignmentId = payment.AssignmentId,
            JobId = payment.JobId,
            WorkerId = payment.WorkerId,
            GiverId = payment.GiverId,
            TotalAmount = payment.TotalAmount,
            WorkerNetAmount = payment.WorkerNetAmount,
            JobTitle = payment.Job?.Title ?? string.Empty
        };
    }

    /// <summary>
    /// Projects a booking as one worker wallet line, whether or not it has been paid:
    /// the app lists a finished shift straight away with its money still pending.
    ///
    /// One line per booking, never per payment, which is what makes a shift
    /// impossible to pay — or count — twice. An unpaid line quotes the same net the
    /// domain would settle, so the pending figure is the amount that will actually
    /// arrive rather than the gross pay.
    /// </summary>
    public static EarningDto ToEarningDto(this WorkerAssignment assignment)
    {
        var payment = assignment.Payment;

        return new EarningDto
        {
            Id = assignment.Id,
            WorkerId = assignment.WorkerId,
            JobId = assignment.JobId,
            JobTitle = assignment.Job?.Title ?? string.Empty,
            Company = Company(assignment.Job),
            Date = payment?.PaidAt ?? assignment.CompletedAt ?? assignment.StartedAt ?? assignment.ConfirmedAt,
            Amount = NetOf(assignment, payment),
            Status = assignment.PaymentStatus,
            AssignmentId = assignment.Id,
            PaymentId = payment is null ? null : ReceiptNumber(payment.Id),
            Method = payment?.PaymentMethod ?? assignment.Job?.PaymentMethod ?? PaymentMethod.Upi,
            Hours = DurationHours(assignment)
        };
    }

    /// <summary>
    /// The worker's net for a booking: the settled figure when paid, otherwise the
    /// amount the domain would settle, so a pending line is not overstated by the fee.
    /// </summary>
    private static int NetOf(WorkerAssignment assignment, Payment? payment)
    {
        if (payment is not null)
        {
            return payment.WorkerNetAmount;
        }

        if (assignment.TotalPay <= 0)
        {
            return 0;
        }

        return PaymentRules.Calculate(assignment.TotalPay, null).WorkerNet;
    }

    /// <summary>
    /// Projects a booking as a work-history line, whether or not it has been paid:
    /// the app lists finished work immediately, with the payment status still pending.
    /// </summary>
    /// <param name="counterpartyId">The other participant: giver for a worker, worker for a giver.</param>
    /// <param name="rating">The counterparty's rating of this user on this job, when one exists.</param>
    /// <param name="ratedByGiver">
    /// Whether the employer was the one who left <paramref name="rating"/>. In an employer's
    /// own history the rating came from the worker, so it is passed in rather than inferred.
    /// </param>
    public static WorkHistoryEntryDto ToWorkHistoryDto(
        this WorkerAssignment assignment,
        Guid counterpartyId,
        string counterpartyName,
        decimal? rating,
        bool ratedByGiver)
    {
        return new WorkHistoryEntryDto
        {
            Id = assignment.Id,
            JobId = assignment.JobId,
            JobTitle = assignment.Job?.Title ?? string.Empty,
            Company = Company(assignment.Job),
            Date = assignment.CompletedAt ?? assignment.StartedAt ?? assignment.ConfirmedAt,
            DurationHours = DurationHours(assignment) ?? 0d,
            Earnings = assignment.Payment?.WorkerNetAmount ?? assignment.TotalPay,
            PaymentStatus = assignment.PaymentStatus,
            Rating = rating,
            RatedByGiver = ratedByGiver,
            CounterpartyId = counterpartyId,
            CounterpartyName = counterpartyName,
            Category = assignment.Job?.Category.ToString()
        };
    }

    /// <summary>The reference the app shows, e.g. <c>PAY-000123</c>.</summary>
    public static string ReceiptNumber(Guid paymentId)
    {
        // The tail of a UUID is 32 hex digits in "N" form; the last eight are read in
        // base 16 and reduced into six digits, so the reference reads like a receipt
        // rather than like a GUID while staying unique in practice.
        var tail = paymentId.ToString("N")[^8..];
        var digits = Convert.ToInt64(tail, 16) % 1_000_000;

        return $"PAY-{digits:D6}";
    }

    /// <summary>Month key used for ordering wallet groupings, e.g. <c>2026-09</c>.</summary>
    public static string MonthKey(DateTime moment) => moment.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Display label for a month key, matching the app's wallet grouping.</summary>
    public static string MonthLabel(string monthKey)
    {
        return DateOnly.TryParseExact(
                $"{monthKey}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var month)
            ? string.Format(CultureInfo.InvariantCulture, "{0:MMMM yyyy}", month)
            : monthKey;
    }

    /// <summary>
    /// Hours worked, from the check-in (or start) to completion. Returns null while the
    /// shift is still running, so the app can show "in progress" instead of "0h".
    /// </summary>
    public static double? DurationHours(WorkerAssignment assignment)
    {
        var start = assignment.CheckedInAt ?? assignment.StartedAt;
        var end = assignment.CompletedAt;

        if (start is null || end is null || end < start)
        {
            return null;
        }

        return Math.Round((end.Value - start.Value).TotalHours, 2);
    }

    private static string Company(Job? job)
    {
        if (job is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(job.Company) ? (job.Giver?.FullName ?? string.Empty) : job.Company;
    }
}
