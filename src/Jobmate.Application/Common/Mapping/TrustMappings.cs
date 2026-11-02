using Jobmate.Application.DTOs.Verifications;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Common.Mapping;

/// <summary>Projections for the verification and trust screens.</summary>
public static class TrustMappings
{
    /// <summary>
    /// One request from its owner's point of view.
    ///
    /// <c>badgeStatus</c> is filled in by the caller from the rollup rather than derived
    /// here, because the badge reflects the <em>latest</em> request per type while this is
    /// one row of the history. A row that is two attempts old would otherwise claim to be
    /// the user's current state.
    /// </summary>
    public static VerificationRequestDto ToDto(
        this VerificationRequest request,
        VerificationStatus badgeStatus = VerificationStatus.NotVerified) => new()
    {
        Id = request.Id,
        Type = request.Type,
        Status = request.Status,
        SubmittedAt = request.CreatedAt,
        Note = request.Note,
        DocumentName = request.DocumentName,
        DocumentUrl = request.DocumentUrl,
        RejectionReason = request.RejectionReason,
        ReviewedAt = request.ReviewedAt,
        BadgeStatus = badgeStatus
    };

    /// <summary>The trust badge, from the domain's own rollup.</summary>
    public static VerificationSummaryDto ToDto(this VerificationRollup rollup) => new()
    {
        Identity = rollup.Identity,
        Phone = rollup.Phone,
        Email = rollup.Email,
        Skills = rollup.Skills,
        Address = rollup.Address,
        Overall = rollup.Overall,
        VerifiedCount = rollup.VerifiedCount
    };

    /// <summary>
    /// The per-type record the Flutter <c>VerificationRecord</c> list shows: one line per
    /// attempt, flattened so the app does not have to interpret request status itself.
    /// </summary>
    public static VerificationRecordDto ToRecordDto(this VerificationRequest request) => new()
    {
        Type = request.Type,
        Status = ToBadgeStatus(request.Status),
        SubmittedOn = request.CreatedAt,
        Note = request.Note ?? string.Empty,
        DocumentName = request.DocumentName ?? string.Empty,
        RejectionReason = request.RejectionReason
    };

    /// <summary>
    /// A request's status as the coarse badge value the app filters on.
    ///
    /// A withdrawn request reads as not verified rather than rejected: nobody refused it.
    /// </summary>
    public static VerificationStatus ToBadgeStatus(this VerificationRequestStatus status) => status switch
    {
        VerificationRequestStatus.Approved => VerificationStatus.Verified,
        VerificationRequestStatus.Rejected => VerificationStatus.Rejected,

        // A withdrawn request is not under review, so it reads as not verified. Naming the
        // status here is the point: this is what a caller reads when it is handed the
        // request it just withdrew rather than one built from the history.
        VerificationRequestStatus.Withdrawn => VerificationStatus.NotVerified,
        VerificationRequestStatus.Pending => VerificationStatus.Pending,

        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "Verification request status is not mapped to a badge state.")
    };
}
