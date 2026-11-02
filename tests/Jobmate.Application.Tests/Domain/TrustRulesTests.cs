using FluentAssertions;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// The badge roll-up: five request types folded into one state the profile screen shows.
///
/// The behaviour worth protecting is that a rejection is escapable. Reading the earliest
/// request instead of the latest leaves somebody permanently badged red with no way to fix
/// it, which is the exact state this projection exists to resolve.
/// </summary>
public sealed class VerificationSummaryTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static VerificationRequest Request(
        VerificationType type,
        VerificationRequestStatus status,
        int daysAgo = 1,
        int idSeed = 0)
    {
        var request = new VerificationRequest
        {
            Id = new Guid(idSeed + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            UserId = Guid.NewGuid(),
            Type = type,
            Status = status,
            CreatedAt = Now.AddDays(-daysAgo)
        };

        return request;
    }

    private static VerificationRequest[] AllBadgeTypes(VerificationRequestStatus status) =>
    [
        Request(VerificationType.Identity, status, idSeed: 1),
        Request(VerificationType.Phone, status, idSeed: 2),
        Request(VerificationType.Email, status, idSeed: 3),
        Request(VerificationType.Skills, status, idSeed: 4)
    ];

    [Fact]
    public void Someone_who_has_asked_for_nothing_is_not_verified_rather_than_failed()
    {
        // An empty badge is an honest answer; a red one would be a lie about a user who has
        // simply not got round to it.
        var rollup = VerificationSummary.Build([]);

        rollup.Overall.Should().Be(VerificationStatus.NotVerified);
        rollup.VerifiedCount.Should().Be(0);
        rollup.Identity.Should().Be(VerificationStatus.NotVerified);
        rollup.Phone.Should().Be(VerificationStatus.NotVerified);
        rollup.Email.Should().Be(VerificationStatus.NotVerified);
        rollup.Skills.Should().Be(VerificationStatus.NotVerified);
        rollup.Address.Should().Be(VerificationStatus.NotVerified);
    }

    [Fact]
    public void All_four_badge_types_approved_gives_a_green_badge()
    {
        var rollup = VerificationSummary.Build(AllBadgeTypes(VerificationRequestStatus.Approved));

        rollup.Overall.Should().Be(VerificationStatus.Verified);
        rollup.VerifiedCount.Should().Be(4);
    }

    [Fact]
    public void Address_does_not_gate_the_badge()
    {
        // Confirming where somebody lives is a different question from who they are, and
        // gating on it would fail every user who has not moved house yet.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests.Add(Request(VerificationType.Address, VerificationRequestStatus.Pending));

        var rollup = VerificationSummary.Build(requests);

        rollup.Overall.Should().Be(VerificationStatus.Verified);
        rollup.VerifiedCount.Should().Be(4);
        rollup.Address.Should().Be(VerificationStatus.Pending);
    }

    [Fact]
    public void An_approved_address_is_reported_but_does_not_raise_the_count()
    {
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests.Add(Request(VerificationType.Address, VerificationRequestStatus.Approved));

        var rollup = VerificationSummary.Build(requests);

        rollup.Address.Should().Be(VerificationStatus.Verified);
        rollup.VerifiedCount.Should().Be(4);
    }

    [Fact]
    public void A_rejection_outranks_the_types_that_passed()
    {
        // Somebody who has been told no needs to see that, not a green tick for the other
        // three.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests[1] = Request(VerificationType.Phone, VerificationRequestStatus.Rejected);

        var rollup = VerificationSummary.Build(requests);

        rollup.Overall.Should().Be(VerificationStatus.Rejected);
        rollup.VerifiedCount.Should().Be(3);
        rollup.Phone.Should().Be(VerificationStatus.Rejected);
        rollup.Identity.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void A_rejection_outranks_a_pending_request_too()
    {
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests[0] = Request(VerificationType.Identity, VerificationRequestStatus.Rejected);
        requests[1] = Request(VerificationType.Phone, VerificationRequestStatus.Pending);

        var rollup = VerificationSummary.Build(requests);

        rollup.Overall.Should().Be(VerificationStatus.Rejected);
    }

    [Fact]
    public void A_pending_request_holds_the_badge_amber()
    {
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests[3] = Request(VerificationType.Skills, VerificationRequestStatus.Pending);

        var rollup = VerificationSummary.Build(requests);

        rollup.Overall.Should().Be(VerificationStatus.Pending);
        rollup.VerifiedCount.Should().Be(3);
    }

    [Fact]
    public void A_withdrawn_request_is_not_a_failure_and_not_a_review()
    {
        // Withdrawing is the applicant taking the request back, so nothing is wrong and
        // nobody is looking at it. Collapsing it into "pending" would leave the badge amber
        // after they took it back, which reads as a decision that was never made.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests[2] = Request(VerificationType.Email, VerificationRequestStatus.Withdrawn);

        var rollup = VerificationSummary.Build(requests);

        rollup.Email.Should().Be(VerificationStatus.NotVerified);
        rollup.Overall.Should().Be(VerificationStatus.NotVerified);
        rollup.VerifiedCount.Should().Be(3);
    }

    [Fact]
    public void A_later_approval_replaces_an_earlier_rejection()
    {
        // The whole point: a user rejected for a bad ID photo who re-uploads a good one is
        // verified. Judging them on the first attempt leaves no way out.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests.Add(Request(VerificationType.Identity, VerificationRequestStatus.Rejected, daysAgo: 10));

        var rollup = VerificationSummary.Build(requests);

        rollup.Identity.Should().Be(VerificationStatus.Verified);
        rollup.Overall.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void A_later_rejection_replaces_an_earlier_approval()
    {
        // Three identity requests: approved, then approved again, then rejected. Only the
        // last one counts, so the badge goes red even though two of the three said yes.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests.Add(Request(VerificationType.Identity, VerificationRequestStatus.Approved, daysAgo: 10, idSeed: 5));
        requests.Add(Request(VerificationType.Identity, VerificationRequestStatus.Rejected, daysAgo: 1, idSeed: 6));

        var rollup = VerificationSummary.Build(requests);

        rollup.Identity.Should().Be(VerificationStatus.Rejected);
        rollup.Overall.Should().Be(VerificationStatus.Rejected);
    }

    [Fact]
    public void Only_the_latest_request_of_a_type_is_kept()
    {
        // A user who has uploaded three times is judged on the third, so the number of
        // attempts cannot change the answer.
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests.Add(Request(VerificationType.Skills, VerificationRequestStatus.Rejected, daysAgo: 20, idSeed: 5));

        var rollup = VerificationSummary.Build(requests);

        rollup.Skills.Should().Be(VerificationStatus.Verified);
        rollup.Overall.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void The_newest_wins_even_when_two_requests_share_a_timestamp()
    {
        // A user who re-uploads within the same clock tick still gets judged on the new one.
        // The id breaks the tie, and a higher id is the later insert.
        var older = Request(VerificationType.Identity, VerificationRequestStatus.Rejected, daysAgo: 0, idSeed: 1);
        var newer = Request(VerificationType.Identity, VerificationRequestStatus.Approved, daysAgo: 0, idSeed: 2);

        VerificationSummary.Build([older, newer]).Identity.Should().Be(VerificationStatus.Verified);
        VerificationSummary.Build([newer, older]).Identity.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void Each_type_can_be_read_back_individually()
    {
        var requests = AllBadgeTypes(VerificationRequestStatus.Approved).ToList();
        requests[0] = Request(VerificationType.Identity, VerificationRequestStatus.Rejected);
        requests[1] = Request(VerificationType.Phone, VerificationRequestStatus.Pending);
        requests.Add(Request(VerificationType.Address, VerificationRequestStatus.Approved));

        var rollup = VerificationSummary.Build(requests);

        rollup.StatusOf(VerificationType.Identity).Should().Be(VerificationStatus.Rejected);
        rollup.StatusOf(VerificationType.Phone).Should().Be(VerificationStatus.Pending);
        rollup.StatusOf(VerificationType.Email).Should().Be(VerificationStatus.Verified);
        rollup.StatusOf(VerificationType.Skills).Should().Be(VerificationStatus.Verified);
        rollup.StatusOf(VerificationType.Address).Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void An_unknown_type_reads_as_not_verified()
    {
        var rollup = VerificationSummary.Build(AllBadgeTypes(VerificationRequestStatus.Approved));

        rollup.StatusOf((VerificationType)99).Should().Be(VerificationStatus.NotVerified);
    }
}

/// <summary>Raising a verification request, which is approved on the spot.</summary>
public sealed class VerificationRulesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static User UserWith(string phone = "+919812300001", string? email = "someone@jobmate.test") =>
        new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Email = email
        };

    [Fact]
    public void A_new_request_is_approved_on_the_spot_and_trims_what_was_sent()
    {
        var request = VerificationRules.Create(
            UserWith(), VerificationType.Identity, "  id_front.jpg  ", "  a note  ", Now);

        // There is no reviewer: the request is approved as it is raised, by nobody.
        request.Status.Should().Be(VerificationRequestStatus.Approved);
        request.ReviewedAt.Should().Be(Now);
        request.ReviewedByUserId.Should().BeNull();
        request.DocumentName.Should().Be("id_front.jpg");
        request.Note.Should().Be("a note");
        request.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void A_phone_cannot_be_verified_before_one_is_on_the_account()
    {
        var act = () => VerificationRules.Create(
            UserWith(phone: string.Empty), VerificationType.Phone, null, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PhoneMissing");
    }

    [Fact]
    public void A_phone_outside_the_supported_country_cannot_be_verified()
    {
        var act = () => VerificationRules.Create(
            UserWith(phone: "+14155550100"), VerificationType.Phone, null, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PhoneMissing");
    }

    [Fact]
    public void An_email_cannot_be_verified_before_one_is_on_the_account()
    {
        var act = () => VerificationRules.Create(
            UserWith(email: null), VerificationType.Email, null, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("EmailMissing");
    }

    [Fact]
    public void A_request_belongs_to_the_user_it_was_raised_for()
    {
        var user = UserWith();

        var request = VerificationRules.Create(user, VerificationType.Skills, null, null, Now);

        request.UserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData(VerificationRequestStatus.Approved, VerificationStatus.Verified)]
    [InlineData(VerificationRequestStatus.Rejected, VerificationStatus.Rejected)]
    [InlineData(VerificationRequestStatus.Pending, VerificationStatus.Pending)]
    [InlineData(VerificationRequestStatus.Withdrawn, VerificationStatus.NotVerified)]
    public void A_decision_reads_as_one_badge_state(
        VerificationRequestStatus decision,
        VerificationStatus expected)
    {
        // The same mapping the summary uses, so a queue row and a profile badge cannot be
        // told two different things about the same request.
        VerificationRules.StatusFor(decision).Should().Be(expected);
    }
}
