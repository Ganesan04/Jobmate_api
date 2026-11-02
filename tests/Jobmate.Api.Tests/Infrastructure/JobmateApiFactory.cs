using Jobmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobmate.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API pipeline — controllers, MediatR, validation, JWT authentication
/// and the exception middleware — against the <c>jobmate_test</c> PostgreSQL database.
///
/// Configuration is supplied by this factory rather than read from the API project's
/// user secrets, so a test run can never point the app at the development database and
/// the JWT key used by tests is not the real one.
/// </summary>
public class JobmateApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A throwaway signing key. 32+ bytes is all HS256 needs, and it exists only for the
    /// lifetime of the test process.
    /// </summary>
    private const string TestSigningKey = "jobmate-integration-test-signing-key-not-a-secret-0123456789";

    private const string TestEnvironmentName = "Testing";

    private readonly SemaphoreSlim _startLock = new(1, 1);

    private bool _started;

    /// <summary>
    /// The defaults applied before the host is built.
    ///
    /// These are set as environment variables rather than only through
    /// <c>ConfigureAppConfiguration</c> because <c>Program.cs</c> reads its
    /// configuration while the host is being composed: with the minimal hosting model,
    /// a configuration callback on the factory runs too late for
    /// <c>AddInfrastructure</c>, which is exactly where the JWT key and connection
    /// string are validated.
    /// </summary>
    /// <summary>
    /// The limits the rest of the suite runs with. Public so the abuse-guard tests can put
    /// exactly this back when they are done with their own small numbers.
    /// </summary>
    public const int SuitePermitLimit = 100_000;

    private static readonly Dictionary<string, string?> Defaults = new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = TestEnvironmentName,
        ["DOTNET_ENVIRONMENT"] = TestEnvironmentName,
        ["ConnectionStrings:WriteConnection"] = null, // filled in from TestDatabase (jobmate_test)
        ["ConnectionStrings:ReadConnection"] = null,
        ["Jwt:Issuer"] = "Jobmate.Api",
        ["Jwt:Audience"] = "Jobmate.App",
        ["Jwt:SigningKey"] = TestSigningKey,
        ["Jwt:AccessTokenMinutes"] = "60",
        ["Jwt:RefreshTokenDays"] = "30",
        ["Jwt:RequireHttpsMetadata"] = "false",

        // Abuse guards are effectively off for the rest of the suite.
        //
        // The framework's test server leaves RemoteIpAddress null, so every request in the
        // run partitions into a single bucket — and a suite this size makes several hundred
        // auth calls. At the real limit of 10 a minute the whole run would be refused at
        // the first class, which would say nothing about the app. <see cref="RateLimits"/>
        // is where a test that is actually about the limits sets small numbers instead.
        ["RateLimit:AuthPermitLimit"] = SuitePermitLimit.ToString(),
        ["RateLimit:MessagingPermitLimit"] = SuitePermitLimit.ToString(),
        ["RateLimit:MediaPermitLimit"] = SuitePermitLimit.ToString(),
        ["RateLimit:ReportsPermitLimit"] = SuitePermitLimit.ToString(),
        ["RateLimit:VerificationPermitLimit"] = SuitePermitLimit.ToString()
    };

    /// <summary>
    /// Abuse-guard limits for this host. Overridden by the one test class that is about
    /// the limits; everyone else gets <see cref="Defaults"/>.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> RateLimits => new Dictionary<string, string?>();

    /// <summary>
    /// Applied before the host is built.
    ///
    /// Environment variables, for the same reason as <see cref="Defaults"/>: with minimal
    /// hosting the host reads its configuration while <c>Program.cs</c> is still being
    /// composed, so a late configuration callback would arrive after the rate limit
    /// policies had already been built from the defaults.
    /// </summary>
    protected void ApplyRateLimitOverrides()
    {
        foreach (var (key, value) in RateLimits)
        {
            Environment.SetEnvironmentVariable(ToEnvironmentKey(key), value);
        }
    }

    static JobmateApiFactory()
    {
        foreach (var (key, value) in Defaults)
        {
            Environment.SetEnvironmentVariable(ToEnvironmentKey(key), value ?? TestDatabase.ConnectionString);
        }
    }

    public JobmateApiFactory() => ApplyRateLimitOverrides();

    /// <summary>
    /// Prepares the test database and starts the host, at most once per run.
    ///
    /// The schema has to be in place before the host starts, because <c>Program.cs</c>
    /// runs the demo seeder while composing the host. The work is guarded by a lock
    /// rather than by <see cref="IAsyncLifetime"/>, which xUnit is not obliged to call
    /// a fixed number of times for a class fixture.
    /// </summary>
    public async Task StartHostAsync()
    {
        if (Volatile.Read(ref _started))
        {
            return;
        }

        await _startLock.WaitAsync();

        try
        {
            if (_started)
            {
                return;
            }

            ApplyRateLimitOverrides();

            await TestDatabase.EnsureReadyAsync();

            // Creating a client builds the host, which migrates nothing; the caller resets
            // afterwards so it starts from nothing.
            using var probe = CreateAnonymousClient();

            _started = true;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public new Task DisposeAsync()
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <summary>Empties every table, so a test class starts from a known state.</summary>
    public Task ResetAsync() => TestDatabase.ResetAsync(Services);

    /// <summary>An anonymous client, for the endpoints that do not need a token.</summary>
    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnvironmentName);

        // Redundant with the environment variables above, but kept so the values are
        // visible in the host's own configuration if the ordering ever changes.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = Defaults.ToDictionary(
                entry => entry.Key,
                entry => entry.Key.StartsWith("ConnectionStrings:", StringComparison.Ordinal)
                    ? TestDatabase.ConnectionString
                    : entry.Value);

            configuration.AddInMemoryCollection(values);
        });
    }

    /// <summary>Configuration keys are <c>Section:Key</c>; the environment spells that <c>Section__Key</c>.</summary>
    private static string ToEnvironmentKey(string key) => key.Replace(":", "__", StringComparison.Ordinal);
}
