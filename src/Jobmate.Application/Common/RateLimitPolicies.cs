namespace Jobmate.Application.Common;

/// <summary>
/// Named rate limit policies, bound to endpoints with
/// <c>[EnableRateLimiting(RateLimitPolicies.X)]</c>.
///
/// Only the endpoints that cost money, leak information or can be used to harass somebody
/// are limited. Reads stay unlimited: a job search that trips a limit looks like an outage,
/// and the addresses it would be protecting are already public to signed-in users.
///
/// Limits live here rather than inside the <c>Program.cs</c> wiring so they can be
/// asserted on directly, and so adding an endpoint to a policy is a one-word change.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Registration, login, OTP sending and OTP checking.
    ///
    /// The tightest policy in the app, and the only one partitioned by address rather than
    /// by account: these calls arrive before there is a user to key on. An SMS code costs
    /// money and a login attempt that keeps failing is somebody guessing, so both are
    /// worth counting.
    /// </summary>
    public const string Auth = "auth";

    /// <summary>Starting conversations and posting messages.</summary>
    public const string Messaging = "messaging";

    /// <summary>Uploading a profile image or a verification document.</summary>
    public const string Media = "media";

    /// <summary>Raising a dispute or filing a support report.</summary>
    public const string Reports = "reports";

    /// <summary>Submitting and withdrawing a verification request.</summary>
    public const string Verification = "verification";

    /// <summary>Every policy above, with the number of requests allowed per window.</summary>
    public static IReadOnlyList<RateLimitPolicy> All => RateLimitOptions.Default.Policies;
}

/// <summary>One policy's name, how many requests a partition may make per window, and how long the window is.</summary>
/// <param name="Name">The value passed to <c>EnableRateLimiting</c>.</param>
/// <param name="PermitLimit">Requests allowed per partition per window.</param>
/// <param name="Window">How long the count is held for.</param>
public sealed record RateLimitPolicy(string Name, int PermitLimit, TimeSpan Window);

/// <summary>
/// The abuse guards as configured. Every value can be overridden from
/// <c>appsettings</c> under a <c>RateLimit</c> section, because a limit that is wrong for
/// one deployment is wrong for the next: a college campus during registration week and a
/// single-marketplace town are not the same traffic.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Section name these options bind from.</summary>
    public const string SectionName = "RateLimit";

    /// <summary>Registration, login, OTP and password reset attempts per address per minute.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    /// <summary>Messages and conversation starts per user per minute.</summary>
    public int MessagingPermitLimit { get; set; } = 30;

    /// <summary>Uploads per user per minute.</summary>
    public int MediaPermitLimit { get; set; } = 10;

    /// <summary>Disputes and reports per user per hour.</summary>
    public int ReportsPermitLimit { get; set; } = 5;

    /// <summary>Verification submissions and withdrawals per user per hour.</summary>
    public int VerificationPermitLimit { get; set; } = 10;

    /// <summary>
    /// Reports are limited per hour rather than per minute on purpose. A burst of five in a
    /// minute is somebody who genuinely has a problem; five an hour sustained is somebody
    /// burying an admin's queue, and the limit has to catch that without punishing the
    /// first case.
    /// </summary>
    public TimeSpan ReportsWindow { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan VerificationWindow { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MessagingWindow { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MediaWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The defaults, used when nothing is configured.</summary>
    public static RateLimitOptions Default => new();

    /// <summary>Every policy, in the order they are registered.</summary>
    public IReadOnlyList<RateLimitPolicy> Policies =>
    [
        new(RateLimitPolicies.Auth, AuthPermitLimit, AuthWindow),
        new(RateLimitPolicies.Messaging, MessagingPermitLimit, MessagingWindow),
        new(RateLimitPolicies.Media, MediaPermitLimit, MediaWindow),
        new(RateLimitPolicies.Reports, ReportsPermitLimit, ReportsWindow),
        new(RateLimitPolicies.Verification, VerificationPermitLimit, VerificationWindow)
    ];

    /// <summary>
    /// Refuses a configuration that would silently disable a guard, so a bad
    /// <c>appsettings</c> value fails at startup instead of quietly opening the app to
    /// abuse.
    /// </summary>
    public void Validate()
    {
        foreach (var policy in Policies)
        {
            if (policy.PermitLimit < 1)
            {
                throw new InvalidOperationException(
                    $"Rate limit '{policy.Name}' must allow at least one request per window, but was configured as {policy.PermitLimit}.");
            }

            if (policy.Window <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"Rate limit '{policy.Name}' has a window of {policy.Window}, which is not a usable interval.");
            }
        }
    }
}

/// <summary>
/// Chooses what a request is counted against.
///
/// The account is preferred because it is the honest answer to "who is doing this", and it
/// means one abusive user on a shared office connection cannot spend everybody else's
/// allowance. The address is the fallback for the calls that arrive before anybody has
/// signed in.
///
/// Deliberately not derived from a header: <c>X-Forwarded-For</c> is client-supplied, so a
/// limiter keyed on it would be keyed on something the caller controls. The framework's
/// own remote address is the only value here that cannot be forged, and the reverse proxy
/// in front of the app is what should be trusting the header.
/// </summary>
public static class AbuseGuardPartitioner
{
    /// <summary>
    /// The key a request counts against. Returns the account id when the caller is signed
    /// in, otherwise their address.
    /// </summary>
    public static string PartitionKey(string? userId, string? remoteAddress) =>
        string.IsNullOrEmpty(userId)
            ? $"ip:{remoteAddress ?? "unknown"}"
            : $"user:{userId}";
}
