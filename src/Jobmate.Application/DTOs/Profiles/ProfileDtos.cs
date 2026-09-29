using Jobmate.Application.DTOs.Jobs;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Profiles;

/// <summary>
/// Flutter <c>UserProfile</c>. Field names and types match the Dart model exactly:
/// id, name, phone, email, role, area, skills, rating, jobsCompleted, isOnline.
/// </summary>
public sealed record UserProfileDto
{
    public Guid Id { get; init; }

    /// <summary>First + last name joined, matching the Dart <c>UserProfile.name</c> field.</summary>
    public string Name { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserRole Role { get; init; }

    public string Area { get; init; } = string.Empty;

    public List<string> Skills { get; init; } = [];

    /// <summary>Average of ratings received. 0 when the user has not been rated.</summary>
    public decimal Rating { get; init; }

    public int JobsCompleted { get; init; }

    public bool IsOnline { get; init; }

    public string? ProfileImageUrl { get; init; }

    public bool IsActive { get; init; }

    public bool IsVerified { get; init; }

    public VerificationStatus Verification { get; init; }

    public DateTime JoinedOn { get; init; }

    /// <summary>Two-letter avatar initials, matching Dart <c>UserProfile.displayInitials</c>.</summary>
    public string? Initials { get; init; }
}

/// <summary>
/// Flutter <c>WorkGiverProfile</c>: organisation, contactPerson, totalHired, rating.
/// </summary>
public sealed record GiverProfileDto
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Organisation { get; init; } = string.Empty;

    public string ContactPerson { get; init; } = string.Empty;

    public int TotalHired { get; init; }

    public decimal Rating { get; init; }

    public GiverKind Kind { get; init; }

    public string? BusinessType { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? ContactPhone { get; init; }

    public VerificationStatus VerificationStatus { get; init; }

    public int TotalJobsPosted { get; init; }
}

/// <summary>Full worker profile behind the Flutter profile and edit-profile screens.</summary>
public sealed record WorkerProfileDto
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public UserProfileDto User { get; init; } = new();

    public string? Bio { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public string? Gender { get; init; }

    public string? Location { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public List<string> Skills { get; init; } = [];

    public string? Experience { get; init; }

    public string? Availability { get; init; }

    public List<AvailabilitySlotDto> AvailabilitySlots { get; init; } = [];

    public int? ExpectedPay { get; init; }

    public PayType? ExpectedPayType { get; init; }

    public VerificationStatus VerificationStatus { get; init; }

    public decimal AverageRating { get; init; }

    public int RatingCount { get; init; }

    public int TotalCompletedJobs { get; init; }

    public int TotalApplications { get; init; }

    public bool IsAvailable { get; init; }

    public bool IsOnline { get; init; }
}

/// <summary>A recurring availability window on a worker profile.</summary>
public sealed record AvailabilitySlotDto
{
    public Guid Id { get; init; }

    /// <summary>ISO day number: 1 = Monday ... 7 = Sunday (Flutter <c>WorkDay.weekday</c>).</summary>
    public int DayOfWeek { get; init; }

    public string ShortLabel { get; init; } = string.Empty;

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }

    public bool IsAvailable { get; init; }
}

/// <summary>Worker document backing a verification request.</summary>
public sealed record WorkerDocumentDto
{
    public Guid Id { get; init; }

    public VerificationType Type { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string? Url { get; init; }

    public VerificationStatus Status { get; init; }

    public DateTime? ReviewedAt { get; init; }
}

/// <summary>
/// The signed-in user's own profile screen. Carries the role-specific half alongside the
/// user row because the app renders name, phone and photo from one screen and the
/// editable fields from the other, and it would take two round trips to assemble that.
/// </summary>
public sealed record MyProfileDto
{
    public UserProfileDto User { get; init; } = new();

    /// <summary>Null for a work giver. The role is a property of the user, not a request.</summary>
    public WorkerProfileDto? Worker { get; init; }

    public GiverProfileDto? Giver { get; init; }

    /// <summary>
    /// Identity and skill documents the caller uploaded. Only ever their own: the list is
    /// what the verification screen lists, and it is loaded per owner rather than by id.
    /// </summary>
    public List<WorkerDocumentDto> Documents { get; init; } = [];
}

/// <summary>
/// Someone else's profile, as seen from a chat thread or a worker's page.
///
/// Deliberately a different shape from <see cref="MyProfileDto"/> rather than the same DTO
/// with fields omitted at the call site: a public profile has no phone number, no email
/// and no coordinates, and giving the endpoint its own type means those fields cannot be
/// added back to it by accident. Contact details are exchanged inside a conversation
/// instead, which is also where the app's own chat screen expects to find them.
/// </summary>
public sealed record PublicProfileDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public UserRole Role { get; init; }

    public string Area { get; init; } = string.Empty;

    public List<string> Skills { get; init; } = [];

    public decimal Rating { get; init; }

    public int RatingCount { get; init; }

    public bool IsOnline { get; init; }

    public string? ProfileImageUrl { get; init; }

    public VerificationStatus Verification { get; init; }

    /// <summary>One line the app shows under the name: a trade, or the business name.</summary>
    public string? Headline { get; init; }

    public string? Bio { get; init; }

    public string? Experience { get; init; }

    public List<AvailabilitySlotDto> AvailabilitySlots { get; init; } = [];

    /// <summary>Work giver only — the organisation or business this person hires for.</summary>
    public string? Organisation { get; init; }

    public GiverKind? GiverKind { get; init; }

    /// <summary>Worker only: whether they are currently taking work.</summary>
    public bool? IsAvailable { get; init; }

    public int CompletedJobs { get; init; }

    public DateTime JoinedOn { get; init; }

    public string? Initials { get; init; }
}

/// <summary>One row of the saved-jobs list.</summary>
public sealed record SavedJobDto
{
    public JobSummaryDto Job { get; init; } = new();

    public DateTime SavedAt { get; init; }
}

/// <summary>The result of an upload. <c>url</c> is what the app stores and renders.</summary>
public sealed record UploadedMediaDto
{
    public string FileName { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string ContentType { get; init; } = string.Empty;
}
