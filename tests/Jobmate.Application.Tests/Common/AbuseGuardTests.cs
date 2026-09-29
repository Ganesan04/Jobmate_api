using FluentAssertions;
using Jobmate.Application.Common;

namespace Jobmate.Application.Tests.Common;

/// <summary>
/// The abuse guards, asserted as configuration rather than through HTTP, because the
/// integration tests can only observe one bucket at a time: the framework's test server
/// leaves <c>RemoteIpAddress</c> null, so every request in a run shares a partition. What
/// needs pinning down here is which limit each policy carries and what a partition key is
/// made of — a limiter keyed on something the caller can set is not a limiter.
/// </summary>
public sealed class AbuseGuardTests
{
    [Fact]
    public void Every_named_policy_has_a_default()
    {
        RateLimitOptions.Default.Policies.Select(p => p.Name).Should().BeEquivalentTo(
        [
            RateLimitPolicies.Auth,
            RateLimitPolicies.Messaging,
            RateLimitPolicies.Media,
            RateLimitPolicies.Reports,
            RateLimitPolicies.Verification
        ]);
    }

    [Fact]
    public void The_policy_names_are_distinct()
    {
        // Two policies sharing a name would silently share a limit, and the second one
        // would be a policy that does not exist.
        RateLimitOptions.Default.Policies.Select(p => p.Name)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_defaults_are_themselves_valid()
    {
        var options = RateLimitOptions.Default;

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void The_default_limits_are_the_documented_ones()
    {
        var policies = RateLimitOptions.Default.Policies.ToDictionary(p => p.Name);

        policies[RateLimitPolicies.Auth].PermitLimit.Should().Be(10);
        policies[RateLimitPolicies.Auth].Window.Should().Be(TimeSpan.FromMinutes(1));
        policies[RateLimitPolicies.Messaging].PermitLimit.Should().Be(30);
        policies[RateLimitPolicies.Messaging].Window.Should().Be(TimeSpan.FromMinutes(1));
        policies[RateLimitPolicies.Media].PermitLimit.Should().Be(10);
        policies[RateLimitPolicies.Media].Window.Should().Be(TimeSpan.FromMinutes(1));
        policies[RateLimitPolicies.Reports].PermitLimit.Should().Be(5);
        policies[RateLimitPolicies.Reports].Window.Should().Be(TimeSpan.FromHours(1));
        policies[RateLimitPolicies.Verification].PermitLimit.Should().Be(10);
        policies[RateLimitPolicies.Verification].Window.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void The_public_list_mirrors_the_defaults()
    {
        RateLimitPolicies.All.Should().BeEquivalentTo(RateLimitOptions.Default.Policies);
    }

    [Fact]
    public void Configured_limits_replace_the_defaults()
    {
        var options = new RateLimitOptions
        {
            AuthPermitLimit = 3,
            AuthWindow = TimeSpan.FromSeconds(30),
            ReportsPermitLimit = 1
        };

        var policies = options.Policies.ToDictionary(p => p.Name);

        policies[RateLimitPolicies.Auth].Should().Be(new RateLimitPolicy("auth", 3, TimeSpan.FromSeconds(30)));
        policies[RateLimitPolicies.Reports].PermitLimit.Should().Be(1);

        // Untouched values keep their defaults, so a deployment only has to write down the
        // number it wants to change.
        policies[RateLimitPolicies.Media].PermitLimit.Should().Be(10);
    }

    [Fact]
    public void A_zero_allowance_is_refused_rather_than_disabling_the_guard()
    {
        var options = new RateLimitOptions { AuthPermitLimit = 0 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*auth*")
            .WithMessage("*at least one*");
    }

    [Fact]
    public void A_negative_allowance_is_refused()
    {
        var options = new RateLimitOptions { ReportsPermitLimit = -1 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*reports*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void A_window_that_is_not_an_interval_is_refused(int seconds)
    {
        var options = new RateLimitOptions { MessagingWindow = TimeSpan.FromSeconds(seconds) };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*messaging*");
    }

    [Fact]
    public void An_allowance_of_exactly_one_is_accepted()
    {
        // The tightest limit the app uses. If this were refused, the abuse-guard tests
        // could not be written at all.
        var options = new RateLimitOptions
        {
            AuthPermitLimit = 1,
            MessagingPermitLimit = 1,
            MediaPermitLimit = 1,
            ReportsPermitLimit = 1,
            VerificationPermitLimit = 1
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }
}

/// <summary>
/// What a request is counted against. The account is preferred over the address so one
/// person on a shared connection cannot spend everybody else's allowance, and the address
/// is the fallback for the calls that arrive before anybody has signed in.
/// </summary>
public sealed class AbuseGuardPartitionerTests
{
    [Fact]
    public void A_signed_in_caller_is_counted_by_account()
    {
        AbuseGuardPartitioner.PartitionKey("7f3a...", "203.0.113.9")
            .Should().Be("user:7f3a...");
    }

    [Fact]
    public void An_anonymous_caller_is_counted_by_address()
    {
        AbuseGuardPartitioner.PartitionKey(null, "203.0.113.9")
            .Should().Be("ip:203.0.113.9");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_account_counts_as_anonymous(string? userId)
    {
        // A blank claim should not produce the key "user:", which would collect every
        // caller with a blank claim into one shared allowance.
        AbuseGuardPartitioner.PartitionKey(userId, "203.0.113.9")
            .Should().Be("ip:203.0.113.9");
    }

    [Fact]
    public void A_caller_with_no_address_still_gets_a_key()
    {
        // Behind a proxy with no forwarded address the framework can hand over null. The
        // request still has to land in some bucket rather than throw mid-pipeline.
        AbuseGuardPartitioner.PartitionKey(null, null).Should().Be("ip:unknown");
    }

    [Fact]
    public void One_account_behind_two_addresses_shares_one_allowance()
    {
        // Signing in on a phone and a laptop is one person, not two.
        AbuseGuardPartitioner.PartitionKey("same-user", "203.0.113.9")
            .Should().Be(AbuseGuardPartitioner.PartitionKey("same-user", "198.51.100.4"));
    }

    [Fact]
    public void Two_accounts_behind_one_address_keep_separate_allowances()
    {
        // The case that makes the account key worth preferring: an office, a campus, or a
        // mobile carrier CGNAT where everybody shares one address.
        AbuseGuardPartitioner.PartitionKey("first-user", "203.0.113.9")
            .Should().NotBe(AbuseGuardPartitioner.PartitionKey("second-user", "203.0.113.9"));
    }

    [Fact]
    public void An_account_and_an_address_can_never_collide()
    {
        // A caller whose user id happens to read like an address must not land in the
        // bucket of whoever is connecting from that address.
        AbuseGuardPartitioner.PartitionKey("203.0.113.9", "203.0.113.9")
            .Should().NotBe(AbuseGuardPartitioner.PartitionKey(null, "203.0.113.9"));
    }
}
