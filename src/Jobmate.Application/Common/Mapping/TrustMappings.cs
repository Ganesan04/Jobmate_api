using Jobmate.Application.DTOs.Admin;
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
    /// The admin view of a request, including the applicant and the reviewer.
    ///
    /// The applicant's phone number is here because an admin verifying an identity
    /// document has to be able to call them, and this is the one screen where that is
    /// appropriate. It is deliberately absent from every user-facing projection.
    /// </summary>
    public static AdminVerificationRequestDto ToAdminDto(this VerificationRequest request) => new()
    {
        Id = request.Id,
        UserId = request.UserId,
        UserName = request.User?.FullName ?? string.Empty,
        UserRole = request.User?.Role ?? UserRole.Worker,
        Type = request.Type,
        SubmittedAt = request.CreatedAt,
        Status = ToBadgeStatus(request.Status),
        Document = request.DocumentName ?? string.Empty,
        Note = request.Note,
        RejectionReason = request.RejectionReason,
        ReviewedAt = request.ReviewedAt,
        ReviewedBy = request.ReviewedBy?.FullName,
        UserPhone = request.User?.PhoneNumber ?? string.Empty
    };

    /// <summary>
    /// A request's admin-side status as the coarse badge value the app filters on.
    ///
    /// A withdrawn request reads as not verified rather than rejected: nobody refused it,
    /// and showing it in the rejection queue would send an admin looking for a reason that
    /// does not exist.
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
