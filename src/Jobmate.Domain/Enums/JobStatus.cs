namespace Jobmate.Domain.Enums;

/// <summary>
/// Lifecycle of a job posting owned by a work giver.
/// Serialised to JSON as camelCase and matches the Flutter <c>JobStatus</c> enum:
/// draft, pending, active, inProgress, completed, cancelled, expired.
/// Allowed transitions are enforced by <c>JobStatusTransitions</c>.
/// </summary>
public enum JobStatus
{
    Draft = 1,
    Pending = 2,
    Active = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6,
    Expired = 7
}
