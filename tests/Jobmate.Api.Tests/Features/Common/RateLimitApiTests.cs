using System.Net;
using System.Net.Http.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Application.Common;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobmate.Api.Tests.Features.Common;

/// <summary>
/// A host whose abuse-guard limits are set low enough to reach in a test, and a class that
/// puts the environment variables back afterwards.
///
/// The other 275 tests run with the guards effectively lifted, because the framework's
/// test server has no remote address and every request in the run shares one bucket. The
/// numbers asserted here come from the same <see cref="RateLimitOptions"/> the application
/// registers its policies from, so a test cannot pass against a limit the app does not use.
/// </summary>
public sealed class RateLimitApiTests : IAsyncLifetime
{
    private readonly TightlyLimitedApiFactory _factory = new();

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Login_attempts_stop_once_the_allowance_is_used_up()
    {
        using var client = _factory.CreateAnonymousClient();

        // Two wrong passwords are allowed, and each answer is the same: a client that is
        // guessing must not be able to tell a wrong password from a throttled one.
        for (var attempt = 0; attempt < TightlyLimitedApiFactory.AuthPermitLimit; attempt++)
        {
            var refused = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { phone = "9999999999", password = "WrongPassword!1" });

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        var limited = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { phone = "9999999999", password = "WrongPassword!1" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        var body = await TestApi.ReadJsonAsync(limited, HttpStatusCode.TooManyRequests);

        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("RateLimitExceeded", body.GetProperty("errorCode").GetString());
        Assert.Contains("Too many requests", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task A_refused_caller_is_told_how_long_to_wait()
    {
        using var client = _factory.CreateAnonymousClient();

        for (var attempt = 0; attempt < TightlyLimitedApiFactory.AuthPermitLimit; attempt++)
        {
            await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { phone = "9999999999", password = "WrongPassword!1" });
        }

        var limited = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { phone = "9999999999", password = "WrongPassword!1" });

        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.True(limited.Headers.RetryAfter.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task The_allowance_is_per_account_and_not_shared()
    {
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        // One report each, against a policy that allows a single report. The second user
        // must be unaffected: a shared bucket would let one busy person lock out everybody.
        Assert.Equal(
            HttpStatusCode.Created,
            (await first.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.FakeJob,
                details = "The listing asked for a joining fee up front."
            })).StatusCode);

        Assert.Equal(
            HttpStatusCode.Created,
            (await second.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.UnsafeWorkplace,
                details = "There was no safety equipment on site at all."
            })).StatusCode);

        // The first user is now out, and stays out rather than getting a fresh allowance.
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await first.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.NoShow,
                details = "Trying again while I am still limited."
            })).StatusCode);
    }

    [Fact]
    public async Task Reading_is_not_limited()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // The guards only cover the endpoints that cost money, leak information or can be
        // used to harass somebody. A job search that trips a limit looks like an outage, so
        // reads are left alone and this pins that decision down.
        for (var i = 0; i < TightlyLimitedApiFactory.ReportsPermitLimit * 3; i++)
        {
            var response = await worker.Client.GetAsync("/api/v1/jobs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_limited_registration_does_not_create_an_account()
    {
        using var client = _factory.CreateAnonymousClient();

        for (var attempt = 0; attempt < TightlyLimitedApiFactory.AuthPermitLimit; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/register", Registration(attempt));
        }

        var refusedNumber = "9970000099";

        var limited = await client.PostAsJsonAsync("/api/v1/auth/register", Registration(99, refusedNumber));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // The refused attempt must not have half-created a user. Otherwise the next attempt
        // after the window would be told the number is already taken, and the person would
        // never be able to register at all.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Users.AnyAsync(u => u.PhoneNumber == "+91" + refusedNumber));
    }

    private static object Registration(int attempt, string? phone = null) => new
    {
        name = $"Rate Test {attempt}",
        phone = phone ?? $"9{970000000 + attempt}",
        email = $"rate.{attempt}.{Guid.NewGuid():N}"[..22] + "@jobmate.test",
        password = "Passw0rd!2345",
        area = "Delhi"
    };
}

/// <summary>
/// Small limits, applied before the host starts and put back on dispose so the next test
/// class gets the defaults. Test parallelism is off project-wide, so nothing else is
/// building a host while these values are in place.
/// </summary>
public sealed class TightlyLimitedApiFactory : JobmateApiFactory
{
    public const int AuthPermitLimit = 2;

    public const int MessagingPermitLimit = 2;

    public const int MediaPermitLimit = 1;

    public const int ReportsPermitLimit = 1;

    public const int VerificationPermitLimit = 1;

    private static readonly string[] Keys =
    [
        "RateLimit:AuthPermitLimit",
        "RateLimit:MessagingPermitLimit",
        "RateLimit:MediaPermitLimit",
        "RateLimit:ReportsPermitLimit",
        "RateLimit:VerificationPermitLimit"
    ];

    protected override IReadOnlyDictionary<string, string?> RateLimits => new Dictionary<string, string?>
    {
        ["RateLimit:AuthPermitLimit"] = AuthPermitLimit.ToString(),
        ["RateLimit:MessagingPermitLimit"] = MessagingPermitLimit.ToString(),
        ["RateLimit:MediaPermitLimit"] = MediaPermitLimit.ToString(),
        ["RateLimit:ReportsPermitLimit"] = ReportsPermitLimit.ToString(),
        ["RateLimit:VerificationPermitLimit"] = VerificationPermitLimit.ToString()
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // The defaults have to be restored explicitly: the base class writes them from a
        // static constructor, which has already run by now and will not run again.
        foreach (var key in Keys)
        {
            Environment.SetEnvironmentVariable(
                key.Replace(":", "__", StringComparison.Ordinal),
                JobmateApiFactory.SuitePermitLimit.ToString());
        }
    }
}
