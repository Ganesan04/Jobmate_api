using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// The single authentication and identity table for every account. Role specific
/// data lives in <see cref="WorkerProfile"/> or <see cref="GiverProfile"/> rather
/// than in parallel user tables, so login, refresh and revocation are handled once.
/// </summary>
public sealed class User : AuditableEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Lower-cased, unique. Optional for phone-first sign-up but never duplicated.</summary>
    public string? Email { get; set; }

    /// <summary>E.164-ish, unique. The Flutter app signs in with either email or phone.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>BCrypt hash. Never leaves the Infrastructure boundary.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public string? ProfileImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsVerified { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Overall trust level rolled up from the user's verification requests.</summary>
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotVerified;

    /// <summary>Free-text area / locality shown on profiles and used as the default search area.</summary>
    public string Area { get; set; } = string.Empty;

    public WorkerProfile? WorkerProfile { get; set; }

    public GiverProfile? GiverProfile { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public ICollection<UserDevice> Devices { get; set; } = new List<UserDevice>();

    public string FullName
    {
        get
        {
            var name = $"{FirstName} {LastName}".Trim();
            return name.Length == 0 ? (Email ?? PhoneNumber) : name;
        }
    }
}
