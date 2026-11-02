using FluentAssertions;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// Money arithmetic and the settlement lifecycle.
///
/// Two properties are worth more than the rest, and both are about what a caller can and
/// cannot move: the fee is the app's 5% and has to agree with the Flutter payment screen
/// to the last rupee, and a settled payment must never be charged again.
/// </summary>
public sealed class PaymentRulesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1000, 0, 50)]
    [InlineData(1200, 0, 60)]
    [InlineData(800, 0, 40)]
    [InlineData(900, 300, 60)]
    public void The_fee_is_five_percent_of_the_total(int baseAmount, int overtime, int expectedFee)
    {
        var breakdown = PaymentRules.Calculate(baseAmount, overtime);

        breakdown.PlatformFee.Should().Be(expectedFee);
        breakdown.BaseAmount.Should().Be(baseAmount);
        breakdown.OvertimeAmount.Should().Be(overtime);
    }

    [Fact]
    public void Omitted_overtime_counts_as_zero()
    {
        PaymentRules.Calculate(1000, null).OvertimeAmount.Should().Be(0);
    }

    [Theory]
    [InlineData(999, 50)]   // 49.95 rounds up
    [InlineData(1010, 51)]  // 50.5 rounds away from zero, matching Dart's .round()
    [InlineData(1030, 52)]  // 51.5 rounds away from zero, not to even
    public void The_fee_rounds_half_away_from_zero(int baseAmount, int expectedFee)
    {
        // Dart's double.round() rounds .5 up, so a half-rupee fee must round away from zero
        // rather than to even. Rounding to even here would leave the worker a rupee out on
        // some bookings and not others.
        PaymentRules.Calculate(baseAmount, null).PlatformFee.Should().Be(expectedFee);
    }

    [Fact]
    public void The_giver_pays_the_fee_and_the_worker_keeps_the_rest()
    {
        // 1000 + 200 = 1200 of work, 60 of fee: 1260 off the giver, 1140 to the worker.
        var breakdown = PaymentRules.Calculate(1000, 200);

        breakdown.GiverTotal.Should().Be(1260);
        breakdown.WorkerNet.Should().Be(1140);
    }

    [Fact]
    public void The_giver_total_is_base_plus_overtime_plus_the_fee()
    {
        var breakdown = PaymentRules.Calculate(1500, 250);

        breakdown.GiverTotal.Should().Be(breakdown.BaseAmount + breakdown.OvertimeAmount + breakdown.PlatformFee);
        breakdown.WorkerNet.Should().Be(breakdown.BaseAmount + breakdown.OvertimeAmount - breakdown.PlatformFee);
    }

    [Theory]
    [InlineData(0, "PayRequired")]
    [InlineData(-100, "PayRequired")]
    public void A_missing_base_pay_is_refused(int baseAmount, string errorCode)
    {
        var act = () => PaymentRules.Calculate(baseAmount, null);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(errorCode);
    }

    [Fact]
    public void An_absurd_base_pay_is_refused()
    {
        var act = () => PaymentRules.Calculate(PaymentRules.MaxPaymentAmount + 1, null);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PayInvalid");
    }

    [Fact]
    public void Negative_overtime_is_refused()
    {
        var act = () => PaymentRules.Calculate(1000, -1);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("OvertimeInvalid");
    }

    [Fact]
    public void A_base_and_overtime_that_together_are_absurd_are_refused()
    {
        // Each part is legal; the sum is not, and it is the sum that is charged.
        var act = () => PaymentRules.Calculate(PaymentRules.MaxPaymentAmount, 1);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PayInvalid");
    }

    [Fact]
    public void The_largest_legal_payment_is_accepted()
    {
        PaymentRules.Calculate(PaymentRules.MaxPaymentAmount, null).BaseAmount
            .Should().Be(PaymentRules.MaxPaymentAmount);
    }
}

/// <summary>Who may pay, and what a new payment row looks like.</summary>
public sealed class PaymentCreationTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static WorkerAssignment Assignment(
        Guid? giverId = null,
        Guid? workerId = null,
        AssignmentStatus status = AssignmentStatus.Completed,
        int agreedPay = 1000) =>
        new()
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            GiverId = giverId ?? Guid.NewGuid(),
            WorkerId = workerId ?? Guid.NewGuid(),
            AgreedPay = agreedPay,
            Status = status
        };

    [Fact]
    public void A_payment_starts_as_processing_and_snapshots_the_agreed_pay()
    {
        var assignment = Assignment();
        var giver = new User { Id = assignment.GiverId };

        var payment = PaymentRules.Create(assignment, giver, 1000, 200, PaymentMethod.Upi, "key-1", Now);

        payment.Status.Should().Be(PaymentStatus.Processing);
        payment.PaymentMethod.Should().Be(PaymentMethod.Upi);
        payment.IdempotencyKey.Should().Be("key-1");
        payment.Amount.Should().Be(1000);
        payment.OvertimeAmount.Should().Be(200);
        payment.PlatformFee.Should().Be(60);
        payment.TotalAmount.Should().Be(1260);
        payment.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void The_snapshot_is_the_pay_they_agreed_not_the_pay_being_charged()
    {
        // The booking was agreed at 1000 and the giver is now charging 1500. Both have to
        // be on the row, because the payment history shows what was agreed next to what was
        // paid and a mismatch between them is the whole question.
        var assignment = Assignment(agreedPay: 1000);
        var giver = new User { Id = assignment.GiverId };

        var payment = PaymentRules.Create(assignment, giver, 1500, null, PaymentMethod.Bank, "key-1", Now);

        payment.Amount.Should().Be(1500);
        payment.AgreedPaySnapshot.Should().Be(assignment.TotalPay);
    }

    [Fact]
    public void The_snapshot_is_the_bookings_own_total_including_its_overtime()
    {
        // The snapshot has to come from the booking, not from the amounts in the request:
        // the payment history shows what was agreed next to what was charged, so a snapshot
        // built from the request would agree with itself by construction and prove nothing.
        var assignment = Assignment(agreedPay: 1000);
        assignment.OvertimeAmount = 250;

        var giver = new User { Id = assignment.GiverId };

        var payment = PaymentRules.Create(assignment, giver, 1200, null, PaymentMethod.Upi, "key-1", Now);

        payment.AgreedPaySnapshot.Should().Be(1250);
        payment.TotalAmount.Should().Be(1260);
    }

    [Fact]
    public void The_parties_are_taken_from_the_booking_not_the_caller()
    {
        // A caller can name a worker in the request; the booking is what decides. Copying
        // the request instead would let a payer settle money into somebody else's account.
        var workerId = Guid.NewGuid();
        var assignment = Assignment(workerId: workerId);
        var giver = new User { Id = assignment.GiverId };

        var payment = PaymentRules.Create(assignment, giver, 1000, null, PaymentMethod.Upi, "key-1", Now);

        payment.WorkerId.Should().Be(workerId);
        payment.GiverId.Should().Be(assignment.GiverId);
        payment.AssignmentId.Should().Be(assignment.Id);
    }

    [Fact]
    public void Only_the_giver_who_owns_the_job_can_pay_for_it()
    {
        var assignment = Assignment();
        var stranger = new User { Id = Guid.NewGuid() };

        var act = () => PaymentRules.Create(assignment, stranger, 1000, null, PaymentMethod.Upi, "key-1", Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void A_cancelled_booking_cannot_be_paid()
    {
        var assignment = Assignment(status: AssignmentStatus.Cancelled);
        var giver = new User { Id = assignment.GiverId };

        var act = () => PaymentRules.Create(assignment, giver, 1000, null, PaymentMethod.Upi, "key-1", Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("AssignmentClosed");
    }

    [Fact]
    public void A_completed_booking_that_is_already_paid_cannot_be_paid_again()
    {
        var assignment = Assignment();
        assignment.Payment = new Payment { Status = PaymentStatus.Paid };
        var giver = new User { Id = assignment.GiverId };

        var act = () => PaymentRules.Create(assignment, giver, 1000, null, PaymentMethod.Upi, "key-1", Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("AlreadyPaid");
    }

    [Fact]
    public void A_completed_booking_whose_payment_failed_can_be_paid_again()
    {
        // The common case: the charge bounced, and the giver tries once more.
        var assignment = Assignment();
        assignment.Payment = new Payment { Status = PaymentStatus.Failed };
        var giver = new User { Id = assignment.GiverId };

        var payment = PaymentRules.Create(assignment, giver, 1000, null, PaymentMethod.Upi, "key-1", Now);

        payment.Status.Should().Be(PaymentStatus.Processing);
    }
}

/// <summary>Confirming, failing and retrying.</summary>
public sealed class PaymentSettlementTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Payment InStatus(PaymentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Amount = 1000,
        PlatformFee = 50,
        TotalAmount = 1050,
        Status = status
    };

    [Fact]
    public void Confirming_records_the_reference_and_the_moment_the_money_arrived()
    {
        var payment = InStatus(PaymentStatus.Processing);

        PaymentRules.Confirm(payment, "txn_123", Now);

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.TransactionReference.Should().Be("txn_123");
        payment.PaidAt.Should().Be(Now);
    }

    [Fact]
    public void Confirming_an_already_paid_payment_changes_nothing()
    {
        // The provider webhook is retried. A second confirmation must not restamp PaidAt,
        // or the payout date the worker sees would move every time the provider wobbled.
        var paid = new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc);
        var payment = InStatus(PaymentStatus.Paid);
        payment.PaidAt = paid;
        payment.TransactionReference = "txn_original";

        PaymentRules.Confirm(payment, "txn_duplicate", Now.AddDays(1));

        payment.PaidAt.Should().Be(paid);
        payment.TransactionReference.Should().Be("txn_original");
    }

    [Fact]
    public void A_failed_payment_cannot_be_confirmed()
    {
        var payment = InStatus(PaymentStatus.Failed);

        var act = () => PaymentRules.Confirm(payment, "txn_123", Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("InvalidPaymentTransition");
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void Failing_records_the_reason()
    {
        var payment = InStatus(PaymentStatus.Processing);

        PaymentRules.Fail(payment, "Insufficient funds");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Insufficient funds");
    }

    [Fact]
    public void A_settled_payment_cannot_be_failed()
    {
        // Silent rather than throwing, because a late failure callback for a payment that
        // already succeeded is the provider's problem to report, not a client error.
        var payment = InStatus(PaymentStatus.Paid);

        var act = () => PaymentRules.Fail(payment, "Something went wrong");

        act.Should().NotThrow();
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void Retrying_reuses_the_row_and_re_arms_it()
    {
        // The database allows one payment per booking, so a retry has to reuse the row.
        // That is safe precisely because a failed charge moved no money.
        var payment = InStatus(PaymentStatus.Failed);
        payment.FailureReason = "Declined";

        PaymentRules.Retry(payment, 1200, 200, PaymentMethod.Bank);

        payment.Status.Should().Be(PaymentStatus.Processing);
        payment.Amount.Should().Be(1200);
        payment.OvertimeAmount.Should().Be(200);
        payment.PlatformFee.Should().Be(70);
        payment.TotalAmount.Should().Be(1470);
        payment.PaymentMethod.Should().Be(PaymentMethod.Bank);
        payment.FailureReason.Should().BeNull();
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Refunded)]
    public void Only_a_failed_payment_can_be_retried(PaymentStatus status)
    {
        // A settled payment is the important one here: retrying it would move money twice.
        var payment = InStatus(status);

        var act = () => PaymentRules.Retry(payment, 1000, null, PaymentMethod.Upi);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("InvalidPaymentTransition");
    }

    [Fact]
    public void Retrying_keeps_the_row_identified_so_it_cannot_be_duplicated()
    {
        var payment = InStatus(PaymentStatus.Failed);
        payment.IdempotencyKey = "key-original";

        PaymentRules.Retry(payment, 1000, null, PaymentMethod.Upi);

        payment.IdempotencyKey.Should().Be("key-original");
    }
}

/// <summary>The transition table itself, so a new status cannot be added without a decision.</summary>
public sealed class PaymentStatusRulesTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Processing, true)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Cancelled, true)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Paid, true)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Processing, true)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Refunded, true)]
    // Everything below is money moving backwards, or a state being re-entered.
    [InlineData(PaymentStatus.Paid, PaymentStatus.Processing, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Cancelled, PaymentStatus.Processing, false)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Paid, false)]
    public void The_transition_table_is_exactly_this(PaymentStatus from, PaymentStatus to, bool allowed)
    {
        PaymentStatusRules.CanTransition(from, to).Should().Be(allowed);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Processing)]
    public void Outstanding_money_is_still_owed(PaymentStatus status)
    {
        PaymentStatusRules.IsOutstanding(status).Should().BeTrue();
        PaymentStatusRules.IsSettled(status).Should().BeFalse();
    }

    [Fact]
    public void Only_paid_is_settled()
    {
        PaymentStatusRules.IsSettled(PaymentStatus.Paid).Should().BeTrue();
        PaymentStatusRules.IsSettled(PaymentStatus.Refunded).Should().BeFalse();
    }
}
