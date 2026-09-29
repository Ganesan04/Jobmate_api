using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Jobs;

/// <summary>
/// Flutter <c>Job</c>. Every field name matches the Dart model, including the
/// computed <c>totalPayout</c> / <c>remainingWorkers</c> and the
/// <c>HH:mm a</c> time strings the UI renders directly.
/// </summary>
public sealed record JobDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Hiring party name; Flutter's <c>Job.company</c>.</summary>
    public string Company { get; init; } = string.Empty;

    public Guid GiverId { get; init; }

    public JobCategory Category { get; init; }

    public string Description { get; init; } = string.Empty;

    // Times are echoed as formatted strings so Dart does not have to re-format.
    public DateOnly Date { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public string EndTime { get; init; } = string.Empty;

    public string ReportTime { get; init; } = string.Empty;

    public int Pay { get; init; }

    public PayType PayType { get; init; }

    /// <summary>Flutter <c>Job.workersNeeded</c>.</summary>
    public int WorkersNeeded { get; init; }

    /// <summary>Flutter <c>Job.workersFilled</c>.</summary>
    public int WorkersFilled { get; init; }

    public int RemainingWorkers { get; init; }

    public string Area { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public int Applicants { get; init; }

    public List<string> Skills { get; init; } = [];

    public string Experience { get; init; } = string.Empty;

    /// <summary>Distance from the caller's location in km; 0 when the caller sent no coordinates.</summary>
    public double DistanceKm { get; init; }

    public JobStatus Status { get; init; }

    public WorkplaceType WorkplaceType { get; init; }

    public JobType JobType { get; init; }

    public int BreakMinutes { get; init; }

    public int? OvertimeRate { get; init; }

    public int TravelAllowance { get; init; }

    public int FoodAllowance { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public bool IsRecurring { get; init; }

    /// <summary>ISO day numbers (1 = Monday ... 7 = Sunday).</summary>
    public List<int> Weekdays { get; init; } = [];

    public string DressCode { get; init; } = string.Empty;

    /// <summary>Flutter <c>Job.physicalRequirement</c> (singular on the Dart side).</summary>
    public string PhysicalRequirement { get; init; } = string.Empty;

    public bool VehicleRequired { get; init; }

    public string SafetyInfo { get; init; } = string.Empty;

    public string EmergencyContact { get; init; } = string.Empty;

    public string ContactPerson { get; init; } = string.Empty;

    public bool LocationVisible { get; init; }

    public bool IsUrgent { get; init; }

    public DateTime? CreatedAt { get; init; }

    public DateTime? PublishedAt { get; init; }

    public DateTime? CancelledAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateOnly? ApplicationDeadline { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public int TotalPayout { get; init; }

    public string TimeRange { get; init; } = string.Empty;

    public string ExperienceLabel { get; init; } = string.Empty;

    public string CategoryLabel { get; init; } = string.Empty;
}

/// <summary>Minimal job projection for lists and nested contexts.</summary>
public sealed record JobSummaryDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Company { get; init; } = string.Empty;

    public JobCategory Category { get; init; }

    public JobStatus Status { get; init; }

    public int Pay { get; init; }

    public PayType PayType { get; init; }

    public string Area { get; init; } = string.Empty;

    public DateOnly Date { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public string EndTime { get; init; } = string.Empty;
}

/// <summary>Flutter <c>GiverStats</c>.</summary>
public sealed record GiverStatsDto
{
    public int ActiveJobs { get; init; }

    public int Applications { get; init; }

    public int ToConfirm { get; init; }

    public int TotalHired { get; init; }

    public int SpentThisMonth { get; init; }

    public int InProgressJobs { get; init; }

    public int DraftJobs { get; init; }

    public int CompletedJobs { get; init; }

    public int CancelledJobs { get; init; }
}

/// <summary>Flutter <c>WorkerStats</c>.</summary>
public sealed record WorkerStatsDto
{
    public int Applied { get; init; }

    public int Active { get; init; }

    public int Completed { get; init; }

    public int Earnings { get; init; }

    public int PendingAmount { get; init; }

    public int DisputedAmount { get; init; }
}
