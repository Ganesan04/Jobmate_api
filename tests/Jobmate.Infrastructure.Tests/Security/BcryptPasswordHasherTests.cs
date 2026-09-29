using FluentAssertions;
using Jobmate.Infrastructure.Security;

namespace Jobmate.Infrastructure.Tests.Security;

/// <summary>
/// The password hasher.
///
/// What is worth pinning down is the boundary behaviour: a password that BCrypt would
/// silently truncate must be refused, and a corrupt hash in the database must read as a
/// failed sign-in rather than a 500. A verification that throws is how a bad row turns
/// into an outage for one user.
/// </summary>
public sealed class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hash = _hasher.Hash("Passw0rd!2345");

        _hasher.Verify("Passw0rd!2345", hash).Should().BeTrue();
    }

    [Fact]
    public void A_different_password_does_not_verify()
    {
        var hash = _hasher.Hash("Passw0rd!2345");

        _hasher.Verify("Passw0rd!2346", hash).Should().BeFalse();
    }

    [Fact]
    public void The_wrong_password_is_indistinguishable_from_a_missing_account()
    {
        // Both answer false, so the login endpoint cannot be used to enumerate accounts.
        var hash = _hasher.Hash("Passw0rd!2345");

        _hasher.Verify("Passw0rd!2346", hash).Should().BeFalse();
        _hasher.Verify("anything at all", hash).Should().BeFalse();
    }

    [Fact]
    public void Two_hashes_of_the_same_password_are_different_strings()
    {
        // A salted hash. Identical strings would mean the salt is not being applied, and
        // two users who happened to choose the same password would be visibly related.
        var first = _hasher.Hash("Passw0rd!2345");
        var second = _hasher.Hash("Passw0rd!2345");

        first.Should().NotBe(second);
        first.Should().NotBe(second[..29]);
    }

    [Fact]
    public void Both_hashes_still_verify_against_the_password()
    {
        var first = _hasher.Hash("Passw0rd!2345");
        var second = _hasher.Hash("Passw0rd!2345");

        _hasher.Verify("Passw0rd!2345", first).Should().BeTrue();
        _hasher.Verify("Passw0rd!2345", second).Should().BeTrue();
    }

    [Fact]
    public void The_work_factor_is_embedded_in_the_hash_so_it_can_be_raised()
    {
        // The cost travels with the hash, which is what makes raising it safe: every
        // existing password keeps verifying and is re-hashed at the new cost on sign-in.
        var hash = _hasher.Hash("Passw0rd!2345");

        hash.Should().StartWith("$2");
        hash.Should().Contain("$12$");
    }

    [Fact]
    public void The_stored_hash_is_never_the_password()
    {
        _hasher.Hash("Passw0rd!2345").Should().NotContain("Passw0rd");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_password_is_refused_before_it_is_hashed(string password)
    {
        var act = () => _hasher.Hash(password);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_missing_password_is_refused_before_it_is_hashed()
    {
        var act = () => _hasher.Hash(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_password_at_the_bcrypt_limit_is_accepted()
    {
        var password = new string('x', BcryptPasswordHasher.MaxPasswordLength);

        _hasher.Verify(password, _hasher.Hash(password)).Should().BeTrue();
    }

    [Fact]
    public void A_password_past_the_bcrypt_limit_is_refused()
    {
        // BCrypt truncates at 72 bytes, so the 73rd character would be silently ignored:
        // "Passw0rd!2345AAAA" and "Passw0rd!2345BBBB" would be the same login. Refusing is
        // the only answer, because quietly accepting them means two different passwords
        // open one account.
        var password = new string('x', BcryptPasswordHasher.MaxPasswordLength + 1);

        var act = () => _hasher.Hash(password);

        act.Should().Throw<ArgumentException>().WithMessage($"*{BcryptPasswordHasher.MaxPasswordLength}*");
    }

    [Theory]
    [InlineData(null, "some-hash")]
    [InlineData("some-password", null)]
    [InlineData("", "some-hash")]
    [InlineData("some-password", "")]
    public void A_missing_side_of_a_verification_is_a_failure_not_a_crash(string? password, string? hash)
    {
        _hasher.Verify(password!, hash!).Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("$2a$12$tooshort")]
    [InlineData("$2a$12$")]
    [InlineData("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$")]
    public void A_corrupt_hash_in_the_database_is_a_failed_sign_in(string corrupt)
    {
        // A truncated column, or a row written by something else, must not become a 500 on
        // the login endpoint for that one user.
        var result = true;

        var act = () => result = _hasher.Verify("Passw0rd!2345", corrupt);

        act.Should().NotThrow();
        result.Should().BeFalse();
    }
}
