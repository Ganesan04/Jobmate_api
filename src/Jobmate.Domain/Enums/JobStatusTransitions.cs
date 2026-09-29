namespace Jobmate.Domain.Enums;

/// <summary>
/// Single source of truth for job lifecycle rules. Clients never set a status
/// directly; they invoke a named command and the domain validates the move.
/// </summary>
public static class JobStatusTransitions
{
    private static readonly Dictionary<JobStatus, JobStatus[]> Allowed = new()
    {
        [JobStatus.Draft] = [JobStatus.Pending, JobStatus.Active, JobStatus.Cancelled],
        [JobStatus.Pending] = [JobStatus.Active, JobStatus.Cancelled],
        [JobStatus.Active] = [JobStatus.InProgress, JobStatus.Completed, JobStatus.Cancelled, JobStatus.Expired],
        [JobStatus.InProgress] = [JobStatus.Completed, JobStatus.Cancelled],
        [JobStatus.Completed] = [],
        [JobStatus.Cancelled] = [],
        [JobStatus.Expired] = []
    };

    /// <summary>Statuses that accept new applications and appear in the worker feed.</summary>
    public static readonly JobStatus[] AcceptingApplications =
    [
        JobStatus.Active
    ];

    /// <summary>Statuses visible to workers in the discovery feed.</summary>
    public static readonly JobStatus[] Discoverable =
    [
        JobStatus.Active,
        JobStatus.InProgress
    ];

    /// <summary>Statuses the job is still editable by its owner.</summary>
    public static readonly JobStatus[] Editable =
    [
        JobStatus.Draft,
        JobStatus.Pending,
        JobStatus.Active
    ];

    public static bool CanTransition(JobStatus from, JobStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static IReadOnlyCollection<JobStatus> NextStatuses(JobStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    public static bool IsTerminal(JobStatus status) => Allowed.TryGetValue(status, out var next) && next.Length == 0;

    public static bool IsOpenToApplications(JobStatus status) => AcceptingApplications.Contains(status);
}
