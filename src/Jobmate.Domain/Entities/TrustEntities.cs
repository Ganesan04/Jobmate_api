using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A user's request to have one aspect of their account verified. Approved as soon as
/// it is submitted; the user's rolled-up <see cref="User.VerificationStatus"/> is the
/// badge both worker and work giver mode show.
/// </summary>
public sealed class VerificationRequest : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public VerificationType Type { get; set; }

    public VerificationRequestStatus Status { get; set; } = VerificationRequestStatus.Pending;

    public string? DocumentName { get; set; }

    public string? DocumentUrl { get; set; }

    public string? Note { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedBy { get; set; }

    public bool IsPending => Status == VerificationRequestStatus.Pending;
}

/// <summary>A worker's saved / bookmarked job, mirroring Flutter <c>SavedJob</c>.</summary>
public sealed class SavedJob : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    /// <summary>Set when the bookmark is removed. The row stays, and queries skip it.</summary>
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// A support / safety report a user filed from the Report a problem screen.
/// May reference no job.
/// </summary>
public sealed class SupportReport : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public ReportReason Reason { get; set; }

    public string Details { get; set; } = string.Empty;

    public string? RelatedJobId { get; set; }

    public bool IsResolved { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
