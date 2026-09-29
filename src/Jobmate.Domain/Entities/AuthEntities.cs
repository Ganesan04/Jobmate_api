using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A rotating, revocable refresh token. Only the SHA-256 hash of the token is
/// stored, so a database leak cannot be replayed against the auth endpoints.
/// Each rotation marks the previous token <see cref="ReplacedByTokenId"/> and
/// reusing a rotated token revokes the whole family (reuse detection).
/// </summary>
public sealed class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Hex SHA-256 of the opaque token value.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// A one-time phone OTP. The code is stored hashed and expires quickly; the
/// plaintext is only ever handed to <c>IOtpService</c> for delivery.
/// </summary>
public sealed class OtpChallenge : AuditableEntity
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the numeric code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public OtpPurpose Purpose { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public bool IsActive(DateTime now) => ConsumedAt is null && ExpiresAt > now && AttemptCount < MaxAttempts;
}

/// <summary>What an OTP was issued for.</summary>
public enum OtpPurpose
{
    PhoneVerification = 1,
    PasswordReset = 2,
    EmailVerification = 3
}

/// <summary>
/// A single-use password reset token. Reusing or resetting twice is prevented by
/// <see cref="ConsumedAt"/> and by the user-level lockout on the phone number.
/// </summary>
public sealed class PasswordResetToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public bool IsActive(DateTime now) => ConsumedAt is null && ExpiresAt > now;
}
