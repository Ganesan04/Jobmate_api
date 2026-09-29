using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Jobs;

/// <summary>
/// Create Job wizard payload. Collects all six steps into one request so the app
/// creates exactly one job; the backend stores it as a draft, then
/// <c>POST /jobs/{id}/publish</c> flips it live.
/// </summary>
public sealed record CreateJobRequest
{
    // Step 1 — basics
    public string Title { get; init; } = string.Empty;

    public JobCategory Category { get; init; } = JobCategory.Other;

    public string Description { get; init; } = string.Empty;

    // Step 2 — requirements
    public int WorkersNeeded { get; init; } = 1;

    public List<string> Skills { get; init; } = [];

    public string? Experience { get; init; }

    // Step 3 — schedule
    public DateOnly Date { get; init; }

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }

    public TimeOnly ReportTime { get; init; }

    public bool IsRecurring { get; init; }

    /// <summary>ISO day numbers 1..7, required when <see cref="IsRecurring"/> is true.</summary>
    public List<int> Weekdays { get; init; } = [];

    public int BreakMinutes { get; init; } = 30;

    // Step 4 — payment
    public int Pay { get; init; }

    public PayType PayType { get; init; } = PayType.Daily;

    public int? OvertimeRate { get; init; }

    public int TravelAllowance { get; init; }

    public int FoodAllowance { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Upi;

    // Step 5 — location
    public string Area { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public WorkplaceType WorkplaceType { get; init; } = WorkplaceType.OnSite;

    public bool LocationVisible { get; init; } = true;

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    // Step 6 — contact & safety
    public string? ContactPerson { get; init; }

    public string? SafetyInfo { get; init; }

    public string? PhysicalRequirement { get; init; }

    public string? DressCode { get; init; }

    public bool VehicleRequired { get; init; }

    public string? EmergencyContact { get; init; }

    public bool IsUrgent { get; init; }

    /// <summary>Optional application cutoff; defaults to the job date.</summary>
    public DateOnly? ApplicationDeadline { get; init; }

    /// <summary>
    /// When true the job is published immediately in the same call, for clients that
    /// do not use the two-step draft flow. Defaults to true because the current
    /// Flutter wizard publishes in one submission.
    /// </summary>
    public bool PublishImmediately { get; init; } = true;
}

/// <summary>PUT /jobs/{id}. Only editable fields; status is changed through the action endpoints.</summary>
public sealed record UpdateJobRequest
{
    public string? Title { get; init; }

    public JobCategory? Category { get; init; }

    public string? Description { get; init; }

    public int? WorkersNeeded { get; init; }

    public List<string>? Skills { get; init; }

    public string? Experience { get; init; }

    public DateOnly? Date { get; init; }

    public TimeOnly? StartTime { get; init; }

    public TimeOnly? EndTime { get; init; }

    public TimeOnly? ReportTime { get; init; }

    public bool? IsRecurring { get; init; }

    public List<int>? Weekdays { get; init; }

    public int? BreakMinutes { get; init; }

    public int? Pay { get; init; }

    public PayType? PayType { get; init; }

    public int? OvertimeRate { get; init; }

    public int? TravelAllowance { get; init; }

    public int? FoodAllowance { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public PaymentMethod? PaymentMethod { get; init; }

    public string? Area { get; init; }

    public string? Address { get; init; }

    public WorkplaceType? WorkplaceType { get; init; }

    public bool? LocationVisible { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? ContactPerson { get; init; }

    public string? SafetyInfo { get; init; }

    public string? PhysicalRequirement { get; init; }

    public string? DressCode { get; init; }

    public bool? VehicleRequired { get; init; }

    public string? EmergencyContact { get; init; }

    public bool? IsUrgent { get; init; }

    public DateOnly? ApplicationDeadline { get; init; }
}

/// <summary>POST /jobs/{id}/cancel.</summary>
public sealed record CancelJobRequest
{
    public string? Reason { get; init; }

    /// <summary>Refund any already-settled payments for the cancelled job.</summary>
    public bool RefundPayments { get; init; } = true;
}

/// <summary>
/// Filters for GET /jobs. Each maps to a documented query string parameter and is
/// applied in SQL, never in memory.
/// </summary>
public sealed record JobSearchRequest : Domain.Common.PageRequest
{
    public string? Search { get; init; }

    public List<JobCategory>? Categories { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double? RadiusKm { get; init; }

    public int? MinPay { get; init; }

    public int? MaxPay { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public List<JobStatus>? Statuses { get; init; }

    public string? Area { get; init; }

    public JobType? JobType { get; init; }

    public WorkplaceType? WorkplaceType { get; init; }

    public bool? UrgentOnly { get; init; }

    public bool? RecurringOnly { get; init; }

    /// <summary>Only jobs from verified givers. Maps to the app's <c>JobFilter.verifiedOnly</c>.</summary>
    public bool? VerifiedGiverOnly { get; init; }

    /// <summary>
    /// Sort key. Accepts the Flutter <c>JobSort</c> values (newest, payHigh, payLow,
    /// nearest) plus distance and applicants.
    /// </summary>
    public string? SortBy { get; init; }

    public bool Descending { get; init; } = true;
}

/// <summary>GET /giver/jobs and GET /worker/jobs filters.</summary>
public sealed record GiverJobsRequest : Domain.Common.PageRequest
{
    public List<JobStatus>? Statuses { get; init; }

    public string? Search { get; init; }

    public JobCategory? Category { get; init; }
}

/// <summary>Worker discovery feed filters, which default to active jobs only.</summary>
public sealed record WorkerJobsRequest : Domain.Common.PageRequest
{
    public string? Search { get; init; }

    public List<JobCategory>? Categories { get; init; }

    public DateOnly? Date { get; init; }

    public int? MinPay { get; init; }

    public int? MaxPay { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double? RadiusKm { get; init; }

    public JobType? JobType { get; init; }

    public bool? UrgentOnly { get; init; }

    public bool? RecurringOnly { get; init; }

    public string? Area { get; init; }

    public string? SortBy { get; init; }
}
