using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A payment from a work giver to a worker for one booking. Created by
/// PayForWorkerCommand with an idempotency key so a retried request returns the
/// original payment instead of charging twice. Only a confirmed provider webhook
/// (or the mock provider's confirmation) moves this to <see cref="PaymentStatus.Paid"/>.
/// </summary>
public sealed class Payment : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public Guid WorkerId { get; set; }

    public User? Worker { get; set; }

    public Guid GiverId { get; set; }

    public User? Giver { get; set; }

    /// <summary>One payment per assignment, enforced by a unique index.</summary>
    public Guid AssignmentId { get; set; }

    public WorkerAssignment? Assignment { get; set; }

    /// <summary>Base agreed pay for the booking.</summary>
    public int Amount { get; set; }

    public int OvertimeAmount { get; set; }

    public int PlatformFee { get; set; }

    /// <summary>What the giver pays: base + overtime + fee. Matches the Flutter payment summary.</summary>
    public int TotalAmount { get; set; }

    /// <summary>What the worker receives: base + overtime - fee.</summary>
    public int WorkerNetAmount => Amount + OvertimeAmount - PlatformFee;

    public string Currency { get; set; } = "INR";

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Upi;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Provider reference (mock gateway reference until a real PSP is wired in).</summary>
    public string? TransactionReference { get; set; }

    /// <summary>
    /// Client-supplied idempotency key. Unique per giver so a repeated
    /// POST /payments cannot create a second charge.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? RefundedAt { get; set; }

    /// <summary>Money is stored in whole rupees to match the Flutter int money fields.</summary>
    public int AgreedPaySnapshot { get; set; }
}

/// <summary>
/// A 1-5 rating between two participants of a completed job. Uniqueness on
/// (JobId, FromUserId, ToUserId) is a database constraint, so a participant can
/// only ever rate a counterpart once per job.
/// </summary>
public sealed class Rating : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public Guid FromUserId { get; set; }

    public User? FromUser { get; set; }

    public Guid ToUserId { get; set; }

    public User? ToUser { get; set; }

    public int Stars { get; set; }

    public string Comment { get; set; } = string.Empty;

    /// <summary>For giver -> worker ratings, which is the direction the Flutter app sends.</summary>
    public Guid? WorkerAssignmentId { get; set; }

    public bool IsGiverRatingWorker => FromUserId != ToUserId && RatingDirection == RatingDirection.GiverToWorker;

    public RatingDirection RatingDirection { get; set; } = RatingDirection.GiverToWorker;
}

/// <summary>Which side of a completed job left the rating.</summary>
public enum RatingDirection
{
    /// <summary>Work giver rates the worker. Matches Flutter <c>WorkerRating</c>.</summary>
    GiverToWorker = 1,

    /// <summary>Worker rates the work giver.</summary>
    WorkerToGiver = 2
}
