namespace Jobmate.Domain.Enums;

/// <summary>
/// Verification state for a user, a profile or a single verification request.
/// Serialised as camelCase and matches the Flutter <c>VerificationStatus</c> enum:
/// notVerified, pending, verified, rejected.
/// </summary>
public enum VerificationStatus
{
    NotVerified = 1,
    Pending = 2,
    Verified = 3,
    Rejected = 4
}

/// <summary>What is being verified. Matches Flutter <c>VerificationType</c>.</summary>
public enum VerificationType
{
    Identity = 1,
    Phone = 2,
    Email = 3,
    Skills = 4,
    Address = 5
}

/// <summary>
/// State of a <c>VerificationRequest</c>. New requests are approved on submission;
/// pending, rejected and withdrawn remain for rows created before that.
///
/// <see cref="Withdrawn"/> is the applicant taking a pending request back, kept apart
/// from a rejection: the user chose to stop, nobody refused them.
/// </summary>
public enum VerificationRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4
}

/// <summary>Why a safety report was raised. Matches Flutter <c>ReportReason</c>.</summary>
public enum ReportReason
{
    UnsafeWorkplace = 1,
    NotPaid = 2,
    AbusiveBehaviour = 3,
    FakeJob = 4,
    NoShow = 5,
    Other = 6
}
