using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>
/// Creates, validates and consumes OTP challenges. OTPs are stored as SHA-256 hashes
/// so the database never holds a usable code, and each challenge is single-use with
/// a small attempt budget to blunt brute force.
/// </summary>
public sealed class OtpServiceHelper(IAppDbContext db, IClock clock, ILogger<OtpServiceHelper> logger)
{
    public const int TtlSeconds = 300;
    public const int MaxAttempts = 5;

    public sealed record IssuedCode(Guid ChallengeId, string Code);

    public async Task<IssuedCode> IssueAsync(string phoneE164, OtpPurpose purpose, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Any outstanding challenge for this phone + purpose is superseded.
        var stale = await db.OtpChallenges
            .Where(c => c.PhoneNumber == phoneE164 && c.Purpose == purpose && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var challenge in stale)
        {
            challenge.ConsumedAt = now;
        }

        var code = GenerateCode();
        var hash = HashCode(code, phoneE164, purpose);

        var entity = new OtpChallenge
        {
            PhoneNumber = phoneE164,
            CodeHash = hash,
            Purpose = purpose,
            ExpiresAt = now.AddSeconds(TtlSeconds),
            MaxAttempts = MaxAttempts,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.OtpChallenges.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Issued a {Purpose} OTP for a phone number", purpose);

        return new IssuedCode(entity.Id, code);
    }

    public async Task<OtpChallenge> ValidateAsync(
        string phoneE164,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var challenge = await db.OtpChallenges
            .Where(c => c.PhoneNumber == phoneE164 && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
        {
            throw new BusinessRuleException("Request a new verification code.", "OtpNotRequested");
        }

        if (challenge.ConsumedAt is not null)
        {
            throw new BusinessRuleException("This code has already been used. Request a new one.", "OtpAlreadyUsed");
        }

        if (challenge.ExpiresAt <= now)
        {
            throw new BusinessRuleException("This code has expired. Request a new one.", "OtpExpired");
        }

        if (challenge.AttemptCount >= challenge.MaxAttempts)
        {
            throw new BusinessRuleException("Too many incorrect attempts. Request a new code.", "OtpAttemptsExceeded");
        }

        var expected = HashCode(code, phoneE164, purpose);

        if (!FixedTimeEquals(challenge.CodeHash, expected))
        {
            challenge.AttemptCount++;
            challenge.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("Incorrect verification code.");
        }

        return challenge;
    }

    /// <summary>
    /// The development code. Fixed and documented so the Flutter app can be tested
    /// without an SMS vendor; the production <c>IOtpService</c> still generates and
    /// delivers a random code and only the delivery channel differs.
    /// </summary>
    private static string GenerateCode() =>
        Environment.GetEnvironmentVariable("JOBMATE_DEV_OTP") is { Length: > 0 } configured
            ? configured
            : "123456";

    /// <summary>Codes are salted with the phone and purpose so hashes are not reusable across rows.</summary>
    private static string HashCode(string code, string phoneE164, OtpPurpose purpose) =>
        Hashing.Sha256($"{purpose}:{phoneE164}:{code}");

    private static bool FixedTimeEquals(string a, string b) => Hashing.FixedTimeEquals(a, b);
}
