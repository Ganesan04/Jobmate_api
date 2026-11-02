using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// Worker-mode data. Every user has one, alongside a <see cref="GiverProfile"/>.
/// Normalised rather than stored as a JSON blob: skills, documents and availability
/// are their own tables. Verification lives on <see cref="User"/>, because it belongs
/// to the person and counts in both modes.
/// </summary>
public sealed class WorkerProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string? Bio { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Location { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Free-text experience summary, mirrors the Flutter job "experience" field.</summary>
    public string? Experience { get; set; }

    public string? Availability { get; set; }

    public decimal? ExpectedPay { get; set; }

    public PayType? ExpectedPayType { get; set; }

    /// <summary>Denormalised average of the ratings this worker received. Recomputed on rating write.</summary>
    public decimal AverageRating { get; set; }

    public int RatingCount { get; set; }

    public int TotalCompletedJobs { get; set; }

    public int TotalApplications { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsOnline { get; set; }

    public DateTime? LastActiveAt { get; set; }

    public ICollection<WorkerSkill> Skills { get; set; } = new List<WorkerSkill>();

    public ICollection<WorkerDocument> Documents { get; set; } = new List<WorkerDocument>();

    public ICollection<WorkerAvailabilitySlot> AvailabilitySlots { get; set; } = new List<WorkerAvailabilitySlot>();
}

/// <summary>A single skill a worker has declared. Unique per worker by name.</summary>
public sealed class WorkerSkill : AuditableEntity
{
    public Guid WorkerProfileId { get; set; }

    public WorkerProfile? WorkerProfile { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>An uploaded identity / skill document backing a verification request.</summary>
public sealed class WorkerDocument : AuditableEntity
{
    public Guid WorkerProfileId { get; set; }

    public WorkerProfile? WorkerProfile { get; set; }

    public VerificationType Type { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? Url { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

    public DateTime? ReviewedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }
}

/// <summary>A recurring availability window, e.g. "Mon-Fri 09:00-18:00".</summary>
public sealed class WorkerAvailabilitySlot : AuditableEntity
{
    public Guid WorkerProfileId { get; set; }

    public WorkerProfile? WorkerProfile { get; set; }

    /// <summary>ISO day of week: 1 = Monday ... 7 = Sunday (matches Flutter WorkDay.weekday).</summary>
    public int DayOfWeek { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public bool IsAvailable { get; set; } = true;
}
