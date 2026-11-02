using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A user's request to have one aspect of their account verified. Admin approves
/// or rejects it; approval also updates the owning profile or user so the trust
/// badge the Flutter profile screen shows stays consistent.
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

/// <summary>
/// A complaint raised by one participant of a booking against the other. Only
/// participants of the referenced job can raise it, and only an admin can resolve
/// or reject it.
/// </summary>
public sealed class Dispute : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public Guid RaisedByUserId { get; set; }

    public User? RaisedBy { get; set; }

    public Guid AgainstUserId { get; set; }

    public User? AgainstUser { get; set; }

    public ReportReason Reason { get; set; }

    public string Description { get; set; } = string.Empty;

    public DisputeStatus Status { get; set; } = DisputeStatus.Open;

    /// <summary>Money in dispute, used by the admin table's amount column.</summary>
    public int Amount { get; set; }

    public string? Resolution { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public User? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public bool IsOpen => Status is DisputeStatus.Open or DisputeStatus.UnderReview;
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
/// Kept separate from <see cref="Dispute"/> because reports may reference no job.
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
