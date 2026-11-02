using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Verifications;

/// <summary>Flutter <c>VerificationRecord</c>: type, status, submittedOn, note, documentName.</summary>
public sealed record VerificationRecordDto
{
    public VerificationType Type { get; init; }

    public VerificationStatus Status { get; init; }

    public DateTime? SubmittedOn { get; init; }

    public string Note { get; init; } = string.Empty;

    public string DocumentName { get; init; } = string.Empty;

    public string? RejectionReason { get; init; }
}

/// <summary>
/// Flutter <c>VerificationSummary</c>: identity, phone, email, skills, plus the
/// derived <c>verifiedCount</c> / <c>overall</c> the trust badge reads.
/// </summary>
public sealed record VerificationSummaryDto
{
    public VerificationStatus Identity { get; init; } = VerificationStatus.NotVerified;

    public VerificationStatus Phone { get; init; } = VerificationStatus.NotVerified;

    public VerificationStatus Email { get; init; } = VerificationStatus.NotVerified;

    public VerificationStatus Skills { get; init; } = VerificationStatus.NotVerified;

    public VerificationStatus Address { get; init; } = VerificationStatus.NotVerified;

    public VerificationStatus Overall { get; init; } = VerificationStatus.NotVerified;

    public int VerifiedCount { get; init; }
}

/// <summary>
/// One verification request as its owner sees it.
/// </summary>
public sealed record VerificationRequestDto
{
    public Guid Id { get; init; }

    public VerificationType Type { get; init; }

    public VerificationRequestStatus Status { get; init; }

    public DateTime SubmittedAt { get; init; }

    public string? Note { get; init; }

    public string? DocumentName { get; init; }

    public string? DocumentUrl { get; init; }

    public string? RejectionReason { get; init; }

    public DateTime? ReviewedAt { get; init; }

    /// <summary>
    /// Which step this sits at, matching the Flutter verification checklist. A request
    /// that is still pending is the one the user is waiting on, so the screen can say
    /// "under review" without a second call.
    /// </summary>
    public VerificationStatus BadgeStatus { get; init; }
}

/// <summary>
/// The trust badge screen: the overall badge, the five per-type states, and the history
/// behind them.
///
/// One call rather than two because the screen shows the badge and the list of attempts
/// together, and a user comparing "the badge says pending" against "my last request was
/// rejected two weeks ago" should not have to wait for a second round trip to see it.
/// </summary>
public sealed record MyVerificationsDto
{
    public VerificationSummaryDto Summary { get; init; } = new();

    /// <summary>Newest first, so the request a user is tracking is at the top.</summary>
    public List<VerificationRequestDto> Requests { get; init; } = [];
}
