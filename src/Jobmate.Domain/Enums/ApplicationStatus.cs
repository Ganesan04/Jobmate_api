namespace Jobmate.Domain.Enums;

/// <summary>
/// Status of a worker's application to a job. Serialised as camelCase and matches
/// the Flutter <c>ApplicationStatus</c> enum exactly:
/// applied, viewed, shortlisted, accepted, rejected, withdrawn, expired.
/// The giver-facing DTO projects <see cref="Accepted"/> onto the Flutter
/// <c>ApplicantStatus.selected</c> value.
/// </summary>
public enum ApplicationStatus
{
    Applied = 1,
    Viewed = 2,
    Shortlisted = 3,
    Accepted = 4,
    Rejected = 5,
    Withdrawn = 6,
    Expired = 7
}
