using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A worker's application to a job. Uniqueness on (JobId, WorkerId) is enforced by
/// a database unique index, which is what makes double-tapping Apply safe.
/// </summary>
public sealed class JobApplication : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public Guid WorkerId { get; set; }

    public User? Worker { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ExpectedPay { get; set; }

    /// <summary>Mirrors the Flutter <c>JobApplication.availableFrom</c> free-text field.</summary>
    public string AvailableFrom { get; set; } = "Immediately available";

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;

    public DateTime AppliedAt { get; set; }

    public DateTime? ViewedAt { get; set; }

    public DateTime? ShortlistedAt { get; set; }

    public DateTime? RejectedAt { get; set; }

    public DateTime? WithdrawnAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Pay the giver proposed to this applicant before confirming.</summary>
    public int? ProposedPay { get; set; }

    public bool IsOpen => ApplicationStatusTransitions.IsOpen(Status);
}

/// <summary>
/// A confirmed booking between one job and one worker. Created only by
/// SelectWorkerCommand, inside a serialisable transaction, and never more times
/// than the job's <see cref="Job.WorkersNeeded"/>.
/// </summary>
public sealed class WorkerAssignment : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public Guid WorkerId { get; set; }

    public User? Worker { get; set; }

    public Guid GiverId { get; set; }

    /// <summary>Application this booking came from; unique so a worker cannot be booked twice for one job.</summary>
    public Guid ApplicationId { get; set; }

    public JobApplication? Application { get; set; }

    public int AgreedPay { get; set; }

    public int? OvertimeAmount { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Pending;

    public string? CheckInCode { get; set; }

    public DateTime ConfirmedAt { get; set; }

    public DateTime? CheckedInAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public Guid? PaymentId { get; set; }

    public Payment? Payment { get; set; }

    /// <summary>Optimistic concurrency token; guards against two givers booking the last slot.</summary>
    public uint RowVersion { get; set; }

    public int TotalPay => AgreedPay + (OvertimeAmount ?? 0);

    /// <summary>Projects the linked payment status onto the Flutter <c>PaymentStatus</c> values.</summary>
    public PaymentStatus PaymentStatus => Payment?.Status switch
    {
        null => PaymentStatus.Pending,
        PaymentStatus.Pending => PaymentStatus.Pending,
        PaymentStatus.Processing => PaymentStatus.Processing,
        PaymentStatus.Paid => PaymentStatus.Paid,
        _ => PaymentStatus.Pending
    };
}
