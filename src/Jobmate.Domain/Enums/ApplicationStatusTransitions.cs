namespace Jobmate.Domain.Enums;

/// <summary>
/// Application lifecycle rules. Terminal states mirror the Flutter
/// <c>ApplicationStatus.isTerminal</c> getter (rejected, withdrawn, expired).
/// </summary>
public static class ApplicationStatusTransitions
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Allowed = new()
    {
        [ApplicationStatus.Applied] = [ApplicationStatus.Viewed, ApplicationStatus.Shortlisted, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn, ApplicationStatus.Expired],
        [ApplicationStatus.Viewed] = [ApplicationStatus.Shortlisted, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn, ApplicationStatus.Expired],
        [ApplicationStatus.Shortlisted] = [ApplicationStatus.Accepted, ApplicationStatus.Applied, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn, ApplicationStatus.Expired],
        [ApplicationStatus.Accepted] = [ApplicationStatus.Expired],
        [ApplicationStatus.Rejected] = [],
        [ApplicationStatus.Withdrawn] = [],
        [ApplicationStatus.Expired] = []
    };

    /// <summary>Flutter <c>ApplicationStatus.isTerminal</c>.</summary>
    public static bool IsTerminal(ApplicationStatus status) =>
        status is ApplicationStatus.Rejected or ApplicationStatus.Withdrawn or ApplicationStatus.Expired;

    /// <summary>Flutter <c>ApplicationStatus.isOpen</c>.</summary>
    public static bool IsOpen(ApplicationStatus status) => !IsTerminal(status);

    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>Statuses a work giver may still act on.</summary>
    public static bool IsActionableByGiver(ApplicationStatus status) =>
        status is ApplicationStatus.Applied or ApplicationStatus.Viewed or ApplicationStatus.Shortlisted;
}
