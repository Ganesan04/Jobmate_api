using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Domain.Entities;

/// <summary>
/// A job posting. The field set is a one-to-one mapping of the Flutter
/// <c>Job</c> model, which in turn mirrors the six-step Create Job wizard
/// (<c>JobDraft</c>), so no wizard input is lost on the way to the database.
///
/// Times are stored as <see cref="TimeOnly"/> (date comes from <see cref="JobDate"/>),
/// which keeps the database free of arbitrary ISO strings and lets PostgreSQL
/// compare shifts correctly.
/// </summary>
public sealed class Job : AuditableEntity
{
    public Guid GiverId { get; set; }

    public User? Giver { get; set; }

    /// <summary>Display name of the hiring party. Denormalised from the giver profile.</summary>
    public string Company { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public JobCategory Category { get; set; } = JobCategory.Other;

    public string Description { get; set; } = string.Empty;

    // ------------------------------------------------------------- requirements
    public int WorkersNeeded { get; set; } = 1;

    /// <summary>Maintained from the WorkerAssignment table; never incremented by the client.</summary>
    public int WorkersFilled { get; set; }

    public string? Experience { get; set; }

    // ----------------------------------------------------------------- schedule
    public DateOnly JobDate { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public TimeOnly ReportTime { get; set; }

    public int BreakMinutes { get; set; } = 30;

    public JobType JobType { get; set; } = JobType.OneTime;

    /// <summary>True when <see cref="JobType"/> is <see cref="JobType.Recurring"/>.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// ISO day numbers (1 = Monday ... 7 = Sunday) for a recurring job. Normalised
    /// into the JobWeekday table rather than stored as an array.
    /// </summary>
    public IReadOnlyList<int> Weekdays { get; set; } = [];

    // ----------------------------------------------------------------- payment
    public int Pay { get; set; }

    public PayType PayType { get; set; } = PayType.Daily;

    public int? OvertimeRate { get; set; }

    public int TravelAllowance { get; set; }

    public int FoodAllowance { get; set; }

    /// <summary>Date the giver intends to settle wages on.</summary>
    public DateOnly? PaymentDate { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Upi;

    // ----------------------------------------------------------------- location
    public string Area { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public WorkplaceType WorkplaceType { get; set; } = WorkplaceType.OnSite;

    /// <summary>
    /// When false the exact <see cref="Address"/> is withheld from workers until
    /// they are selected; the Flutter job card shows the <see cref="Area"/> instead.
    /// </summary>
    public bool LocationVisible { get; set; } = true;

    // ---------------------------------------------------------- contact & safety
    public string? ContactPerson { get; set; }

    public string? SafetyInformation { get; set; }

    public string? PhysicalRequirements { get; set; }

    public string? DressCode { get; set; }

    public bool VehicleRequired { get; set; }

    public string? EmergencyContact { get; set; }

    public bool IsUrgent { get; set; }

    // ---------------------------------------------------------------- lifecycle
    public JobStatus Status { get; set; } = JobStatus.Draft;

    /// <summary>Optional application cutoff. Null means "until the job date".</summary>
    public DateOnly? ApplicationDeadline { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    // -------------------------------------------------------------- relationships
    public ICollection<JobSkill> Skills { get; set; } = new List<JobSkill>();

    public ICollection<JobWeekday> WeekdayRows { get; set; } = new List<JobWeekday>();

    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();

    public ICollection<WorkerAssignment> Assignments { get; set; } = new List<WorkerAssignment>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();

    /// <summary>Set when the giver deletes a draft. The row stays, and queries skip it.</summary>
    public DateTime? DeletedAt { get; set; }

    public int RemainingWorkers => Math.Max(WorkersNeeded - WorkersFilled, 0);

    /// <summary>Total advertised payout for one worker, matching Flutter <c>Job.totalPayout</c>.</summary>
    public int TotalPayout => Pay + TravelAllowance + FoodAllowance;

    public bool IsAcceptingApplications => Status == JobStatus.Active &&
                                           (ApplicationDeadline is null || ApplicationDeadline.Value >= DateOnly.FromDateTime(DateTime.UtcNow));

    public bool IsDiscoverable => Status is JobStatus.Active or JobStatus.InProgress;
}

/// <summary>A skill a job requires. Unique per job by name.</summary>
public sealed class JobSkill : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>One weekday a recurring job runs on (1 = Monday ... 7 = Sunday).</summary>
public sealed class JobWeekday : AuditableEntity
{
    public Guid JobId { get; set; }

    public Job? Job { get; set; }

    public int DayOfWeek { get; set; }
}
