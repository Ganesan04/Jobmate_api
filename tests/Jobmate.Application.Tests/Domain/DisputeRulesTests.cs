using FluentAssertions;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// Raising a dispute and closing one.
///
/// The two rules that carry weight: only somebody attached to the booking can raise one,
/// and a dispute is always against the other side of it — never against a third party, and
/// never against a booking with nobody else on it.
/// </summary>
public sealed class DisputeRulesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static WorkerAssignment Booking(
        Guid workerId,
        Guid giverId,
        AssignmentStatus status = AssignmentStatus.Completed) =>
        new()
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            WorkerId = workerId,
            GiverId = giverId,
            AgreedPay = 1000,
            Status = status
        };

    private static Job JobWith(Guid giverId, params WorkerAssignment[] assignments)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            GiverId = giverId,
            Status = JobStatus.Completed
        };

        foreach (var assignment in assignments)
        {
            job.Assignments.Add(assignment);
        }

        return job;
    }

    [Fact]
    public void A_worker_raises_against_the_giver_who_owns_the_booking()
    {
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var dispute = DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.NotPaid, "Worked but never paid", 1000, Now);

        dispute.RaisedByUserId.Should().Be(workerId);
        dispute.AgainstUserId.Should().Be(giverId);
        dispute.JobId.Should().Be(job.Id);
        dispute.Status.Should().Be(DisputeStatus.Open);
        dispute.Amount.Should().Be(1000);
    }

    [Fact]
    public void A_giver_raises_against_the_worker_on_the_booking()
    {
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var dispute = DisputeRules.Create(
            job, new User { Id = giverId, Role = UserRole.WorkGiver },
            ReportReason.NoShow, "Never turned up at all", 500, Now);

        dispute.AgainstUserId.Should().Be(workerId);
    }

    [Fact]
    public void A_giver_raises_against_the_first_live_booking_not_a_cancelled_one()
    {
        // A cancelled booking has no counterparty to take the money back from, so a dispute
        // against it would sit in the queue with nothing to do.
        var giverId = Guid.NewGuid();
        var cancelledWorker = Guid.NewGuid();
        var liveWorker = Guid.NewGuid();

        var job = JobWith(
            giverId,
            Booking(cancelledWorker, giverId, AssignmentStatus.Cancelled),
            Booking(liveWorker, giverId, AssignmentStatus.Completed));

        var dispute = DisputeRules.Create(
            job, new User { Id = giverId, Role = UserRole.WorkGiver },
            ReportReason.NoShow, "The second worker never turned up", 0, Now);

        dispute.AgainstUserId.Should().Be(liveWorker);
    }

    [Fact]
    public void Somebody_with_no_part_in_the_booking_cannot_raise_a_dispute()
    {
        var job = JobWith(Guid.NewGuid(), Booking(Guid.NewGuid(), Guid.NewGuid()));

        var act = () => DisputeRules.Create(
            job, new User { Id = Guid.NewGuid() }, ReportReason.Other, "I saw something wrong", 0, Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void A_booking_with_nobody_else_on_it_cannot_be_disputed()
    {
        var giverId = Guid.NewGuid();
        var job = JobWith(giverId);

        var act = () => DisputeRules.Create(
            job, new User { Id = giverId, Role = UserRole.WorkGiver },
            ReportReason.Other, "There is nobody to raise this against", 0, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("NoCounterpart");
    }

    [Fact]
    public void A_booking_where_only_cancelled_workers_were_assigned_cannot_be_disputed()
    {
        var giverId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(Guid.NewGuid(), giverId, AssignmentStatus.Cancelled));

        var act = () => DisputeRules.Create(
            job, new User { Id = giverId, Role = UserRole.WorkGiver },
            ReportReason.Other, "Nobody is left to raise this against", 0, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("NoCounterpart");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Too short")]
    public void A_dispute_needs_enough_description_to_be_actionable(string description)
    {
        // "Too short" is nine characters: a bare complaint gives an admin nothing to work
        // from and no way to ask a useful follow-up question.
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var act = () => DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.NotPaid, description, 1000, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("DescriptionTooShort");
    }

    [Fact]
    public void Ten_characters_is_enough()
    {
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var dispute = DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.NotPaid, "1234567890", 0, Now);

        dispute.Description.Should().Be("1234567890");
    }

    [Fact]
    public void The_description_is_trimmed_before_it_is_stored()
    {
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var dispute = DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.NotPaid, "  Never paid the agreed rate.  ", 0, Now);

        dispute.Description.Should().Be("Never paid the agreed rate.");
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var act = () => DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.NotPaid, "Asked for a refund of nothing", -1, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("AmountInvalid");
    }

    [Fact]
    public void A_dispute_over_nothing_is_allowed()
    {
        // NoShow and a hostile workplace are real complaints with no money attached. Forcing
        // an amount would make the form lie about what it is for.
        var giverId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var job = JobWith(giverId, Booking(workerId, giverId));

        var dispute = DisputeRules.Create(
            job, new User { Id = workerId }, ReportReason.UnsafeWorkplace, "No safety gear on site", 0, Now);

        dispute.Amount.Should().Be(0);
        dispute.Reason.Should().Be(ReportReason.UnsafeWorkplace);
    }

    [Fact]
    public void An_open_dispute_can_be_reviewed_or_closed_straight_away()
    {
        DisputeRules.CanTransition(DisputeStatus.Open, DisputeStatus.UnderReview).Should().BeTrue();
        DisputeRules.CanTransition(DisputeStatus.Open, DisputeStatus.Resolved).Should().BeTrue();
        DisputeRules.CanTransition(DisputeStatus.Open, DisputeStatus.Rejected).Should().BeTrue();
    }

    [Fact]
    public void A_dispute_under_review_can_still_be_closed()
    {
        DisputeRules.CanTransition(DisputeStatus.UnderReview, DisputeStatus.Resolved).Should().BeTrue();
        DisputeRules.CanTransition(DisputeStatus.UnderReview, DisputeStatus.Rejected).Should().BeTrue();
    }

    [Theory]
    [InlineData(DisputeStatus.Resolved, DisputeStatus.Rejected)]
    [InlineData(DisputeStatus.Rejected, DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Resolved, DisputeStatus.Open)]
    [InlineData(DisputeStatus.Rejected, DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Open, DisputeStatus.Open)]
    [InlineData(DisputeStatus.UnderReview, DisputeStatus.Open)]
    public void A_closed_dispute_is_closed(DisputeStatus from, DisputeStatus to)
    {
        // Reopening by a second resolve is the one that would matter: it would move the
        // held money twice.
        DisputeRules.CanTransition(from, to).Should().BeFalse();
    }

    private static Dispute InStatus(DisputeStatus status) => new()
    {
        Id = Guid.NewGuid(),
        JobId = Guid.NewGuid(),
        RaisedByUserId = Guid.NewGuid(),
        AgainstUserId = Guid.NewGuid(),
        Reason = ReportReason.NotPaid,
        Description = "Worked but never paid",
        Status = status,
        CreatedAt = Now.AddDays(-1)
    };

    [Theory]
    [InlineData(DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Rejected)]
    public void Closing_a_dispute_records_who_closed_it_and_why(DisputeStatus target)
    {
        var dispute = InStatus(DisputeStatus.UnderReview);
        var adminId = Guid.NewGuid();

        DisputeRules.Resolve(dispute, target, "  Refunded from the platform balance.  ", adminId, Now);

        dispute.Status.Should().Be(target);
        dispute.Resolution.Should().Be("Refunded from the platform balance.");
        dispute.ResolvedByUserId.Should().Be(adminId);
        dispute.ResolvedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(DisputeStatus.Open)]
    [InlineData(DisputeStatus.UnderReview)]
    public void A_dispute_is_only_closed_by_resolving_or_rejecting_it(DisputeStatus target)
    {
        // "UnderReview" as an outcome would leave the queue neither open nor closed.
        var dispute = InStatus(DisputeStatus.Open);

        var act = () => DisputeRules.Resolve(dispute, target, "Some notes", Guid.NewGuid(), Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("InvalidDisputeStatus");
        dispute.Status.Should().Be(DisputeStatus.Open);
    }

    [Theory]
    [InlineData(DisputeStatus.Resolved)]
    [InlineData(DisputeStatus.Rejected)]
    public void An_already_closed_dispute_cannot_be_closed_again(DisputeStatus from)
    {
        var dispute = InStatus(from);

        var act = () => DisputeRules.Resolve(dispute, DisputeStatus.Resolved, "Some notes", Guid.NewGuid(), Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("InvalidDisputeTransition");
        dispute.Status.Should().Be(from);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_dispute_cannot_be_closed_without_notes(string? resolution)
    {
        // Whoever was complained of is entitled to know what was decided and why.
        var dispute = InStatus(DisputeStatus.UnderReview);

        var act = () => DisputeRules.Resolve(dispute, DisputeStatus.Resolved, resolution, Guid.NewGuid(), Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("ResolutionRequired");
        dispute.Status.Should().Be(DisputeStatus.UnderReview);
    }

    [Fact]
    public void A_rejection_also_needs_notes()
    {
        var dispute = InStatus(DisputeStatus.Open);

        var act = () => DisputeRules.Resolve(dispute, DisputeStatus.Rejected, "  ", Guid.NewGuid(), Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("ResolutionRequired");
    }
}
