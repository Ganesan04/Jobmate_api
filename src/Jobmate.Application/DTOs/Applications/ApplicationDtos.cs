using Jobmate.Application.DTOs.Jobs;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Applications;

/// <summary>
/// Outcome of a bulk selection. The Flutter screen uses it to refresh the applicant
/// list, show how many slots are left and confirm what happened to the applicants that
/// were shortlisted but not picked.
/// </summary>
public sealed record SelectWorkersResultDto
{
    /// <summary>The bookings that were created, in request order.</summary>
    public IReadOnlyList<WorkerAssignmentDto> Selected { get; init; } = [];

    /// <summary>Applications put back in the open pool because they were not selected.</summary>
    public IReadOnlyList<Guid> ReleasedApplicationIds { get; init; } = [];

    public int WorkersFilled { get; init; }

    public int WorkersNeeded { get; init; }

    public int RemainingSlots { get; init; }
}

/// <summary>
/// Flutter <c>JobApplication</c>: id, jobId, workerId, status, message, expectedPay,
/// availableFrom and the applied/viewed/shortlisted/accepted timestamps.
/// </summary>
public sealed record JobApplicationDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    public Guid WorkerId { get; init; }

    public ApplicationStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public int? ExpectedPay { get; init; }

    public string AvailableFrom { get; init; } = string.Empty;

    public DateTime? AppliedAt { get; init; }

    public DateTime? ViewedAt { get; init; }

    public DateTime? ShortlistedAt { get; init; }

    public DateTime? RejectedAt { get; init; }

    public DateTime? WithdrawnAt { get; init; }

    public DateTime? AcceptedAt { get; init; }

    public string? RejectionReason { get; init; }

    public int? ProposedPay { get; init; }

    /// <summary>Lightweight job context so the applications list needs no second call.</summary>
    public JobSummaryDto? Job { get; init; }
}

/// <summary>
/// Flutter <c>JobApplicant</c> — the giver-side view, where an accepted application
/// is presented as the Dart <c>ApplicantStatus.selected</c> value.
/// </summary>
public sealed record JobApplicantDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    public UserProfileDto Worker { get; init; } = new();

    /// <summary>Matches Dart <c>ApplicantStatus</c>: applied, shortlisted, rejected, selected.</summary>
    public ApplicantStatus Status { get; init; }

    public int? ProposedPay { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime? AppliedAt { get; init; }

    public int? ExpectedPay { get; init; }

    public double DistanceKm { get; init; }

    public string AvailableFrom { get; init; } = string.Empty;

    /// <summary>The underlying status, for the worker-facing screens.</summary>
    public ApplicationStatus ApplicationStatus { get; init; }
}

/// <summary>
/// Giver-side applicant status, matching the Dart <c>ApplicantStatus</c> enum:
/// applied, shortlisted, rejected, selected.
/// </summary>
public enum ApplicantStatus
{
    Applied = 1,
    Shortlisted = 2,
    Rejected = 3,
    Selected = 4
}

/// <summary>
/// Flutter <c>WorkerAssignment</c>: id, jobId, workerId, giverId, agreedPay,
/// confirmedAt, paymentStatus, checkedInAt, checkInCode, completedAt, paymentId, paidAt.
/// </summary>
public sealed record WorkerAssignmentDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    public Guid WorkerId { get; init; }

    public Guid GiverId { get; init; }

    public int AgreedPay { get; init; }

    public DateTime ConfirmedAt { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public DateTime? CheckedInAt { get; init; }

    public string? CheckInCode { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string? PaymentId { get; init; }

    public DateTime? PaidAt { get; init; }

    public AssignmentStatus Status { get; init; }

    public int OvertimeAmount { get; init; }

    public int TotalPay { get; init; }

    public bool IsCheckedIn { get; init; }

    public JobSummaryDto? Job { get; init; }

    public string? WorkerName { get; init; }
}

/// <summary>
/// Flutter <c>WorkHistoryEntry</c>: id, jobTitle, company, date, durationHours,
/// earnings, paymentStatus, rating, ratedByGiver.
/// </summary>
public sealed record WorkHistoryEntryDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    public string JobTitle { get; init; } = string.Empty;

    public string Company { get; init; } = string.Empty;

    public DateTime Date { get; init; }

    public double DurationHours { get; init; }

    public int Earnings { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    /// <summary>The rating the giver left for this worker on this job, if any.</summary>
    public decimal? Rating { get; init; }

    public bool RatedByGiver { get; init; }

    public Guid CounterpartyId { get; init; }

    public string CounterpartyName { get; init; } = string.Empty;

    public string? Category { get; init; }
}
