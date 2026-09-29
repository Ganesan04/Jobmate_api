using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// Role-specific data for a <see cref="UserRole.WorkGiver"/>. Covers both
/// individuals and registered businesses: <see cref="GiverKind"/> selects which
/// fields the Flutter profile screen shows.
/// </summary>
public sealed class GiverProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Individual hiring for themselves, or a registered business / workplace.</summary>
    public GiverKind Kind { get; set; } = GiverKind.Individual;

    /// <summary>Maps to the Flutter <c>WorkGiverProfile.organisation</c> and <c>Job.company</c>.</summary>
    public string BusinessName { get; set; } = string.Empty;

    public string? BusinessType { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? ContactPerson { get; set; }

    public string? ContactPhone { get; set; }

    public string? GstNumber { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotVerified;

    public decimal AverageRating { get; set; }

    public int RatingCount { get; set; }

    public int TotalJobsPosted { get; set; }

    public int TotalHired { get; set; }
}
