using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Application.DTOs.Verifications;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Admin.Commands;

/// <summary>
/// Records an admin's approval. POST /api/v1/admin/verifications/{id}/approve.
///
/// Deliberately a separate command from the rejection rather than one command with a
/// nullable reason: inferring the decision from whether a field was filled in meant a
/// rejection sent with a blank reason was recorded as an approval, which is the one
/// mistake on this screen that cannot be undone by the applicant.
/// </summary>
public sealed record ApproveVerificationCommand(Guid RequestId, ReviewVerificationRequest Request)
    : IRequest<AdminVerificationRequestDto>;

public sealed class ApproveVerificationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<ApproveVerificationCommand, AdminVerificationRequestDto>
{
    public async Task<AdminVerificationRequestDto> Handle(
        ApproveVerificationCommand request,
        CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var verification = await AdminVerificationLoader.LoadAsync(db, request.RequestId, cancellationToken);

        // Refuses a request that has already been decided, which is what stops two admins
        // racing on the same queue row and the second one silently overwriting the first.
        VerificationRules.Approve(verification, adminId, now);

        await AdminVerificationLoader.ApplyDecisionAsync(
            db, notifications, clock, verification, adminId, now, null, cancellationToken);

        return verification.ToAdminDto();
    }
}

/// <summary>
/// Records an admin's refusal. POST /api/v1/admin/verifications/{id}/reject.
///
/// The reason is mandatory and is shown to the applicant, so it has to say what to fix.
/// </summary>
public sealed record RejectVerificationCommand(Guid RequestId, ReviewVerificationRequest Request)
    : IRequest<AdminVerificationRequestDto>;

public sealed class RejectVerificationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<RejectVerificationCommand, AdminVerificationRequestDto>
{
    public async Task<AdminVerificationRequestDto> Handle(
        RejectVerificationCommand request,
        CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        // Read and trimmed before the rule sees it, so a reason of "   " is caught as the
        // blank it is rather than being stored as three spaces.
        var reason = input.RejectionReason?.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            throw new BusinessRuleException(
                "Add a reason so the applicant can fix the issue.",
                "RejectionReasonRequired");
        }

        var verification = await AdminVerificationLoader.LoadAsync(db, request.RequestId, cancellationToken);

        VerificationRules.Reject(verification, adminId, reason, now);

        await AdminVerificationLoader.ApplyDecisionAsync(
            db, notifications, clock, verification, adminId, now, reason, cancellationToken);

        return verification.ToAdminDto();
    }
}

/// <summary>
/// The parts of reviewing a verification request that approval and rejection share.
///
/// Both decisions load the same rows, fold the same badge and send the same kind of
/// notification, so those live here rather than being copied into two handlers where they
/// would drift apart on the next change.
/// </summary>
internal static class AdminVerificationLoader
{
    /// <summary>Loads the request with its applicant, or throws if it is gone.</summary>
    public static async Task<VerificationRequest> LoadAsync(
        IAppDbContext db,
        Guid requestId,
        CancellationToken cancellationToken) =>
        await db.VerificationRequests
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == requestId, cancellationToken)
            ?? throw new NotFoundException("That verification request does not exist.");

    /// <summary>
    /// Writes a decided request: refreshes the applicant's coarse status, attaches the
    /// reviewer so the response can name them, saves, and tells the applicant.
    /// </summary>
    public static async Task ApplyDecisionAsync(
        IAppDbContext db,
        INotificationService notifications,
        IClock clock,
        VerificationRequest verification,
        Guid adminId,
        DateTime now,
        string? rejectionReason,
        CancellationToken cancellationToken)
    {
        var user = verification.User
                   ?? await db.Users.FirstOrDefaultAsync(u => u.Id == verification.UserId, cancellationToken)
                   ?? throw new NotFoundException("The applicant for this request no longer exists.");

        // The stored status is recomputed from the applicant's whole request history rather
        // than folded forward one decision at a time, so the coarse status an admin sees in
        // the users table can never drift from the badge the applicant sees. Folding would
        // let a later approval quietly clear an earlier rejection, and neither screen would
        // be able to explain the disagreement.
        var history = await db.VerificationRequests
            .AsNoTracking()
            .Where(v => v.UserId == user.Id)
            .ToListAsync(cancellationToken);

        user.VerificationStatus = VerificationSummary.Build(history).Overall;
        user.UpdatedAt = now;

        // IsVerified is deliberately left alone: it means "this person proved they own the
        // phone number", and flipping it here would quietly disable the sign-in OTP gate
        // for whoever happened to be verified.
        await db.SaveChangesAsync(cancellationToken);

        // The reviewer's own row, so the response and the queue can name who decided
        // rather than showing a bare id. Tracked on purpose: this instance is assigned to
        // the navigation, and an untracked one with a populated key would be treated as a
        // new entity and inserted, colliding with the row that is already there.
        var reviewer = await db.Users
            .FirstOrDefaultAsync(u => u.Id == adminId, cancellationToken);

        verification.ReviewedBy = reviewer;

        var approved = verification.Status == VerificationRequestStatus.Approved;
        var type = verification.Type.ToString().ToLowerInvariant();

        await notifications.SendAsync(
            user.Id,
            NotificationType.ApplicationUpdate,
            approved ? "Verification approved" : "Verification needs another look",
            approved
                ? $"Your {type} check was approved."
                : $"Your {type} check was not approved. {rejectionReason}",
            NotificationReferenceType.Verification,
            verification.Id,
            "/verifications",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Suspends or reinstates an account. POST /api/v1/admin/users/{id}/status.
///
/// Suspension is a flag rather than a delete: the account keeps its jobs, bookings and
/// message history, so reinstating it puts the person back where they were instead of
/// leaving a trail of half-deleted work behind.
/// </summary>
public sealed record SetUserStatusCommand(Guid UserId, SetUserStatusRequest Request)
    : IRequest<AdminUserDto>;

public sealed class SetUserStatusCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<SetUserStatusCommand, AdminUserDto>
{
    public async Task<AdminUserDto> Handle(
        SetUserStatusCommand request,
        CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("That account does not exist.");

        // An admin locking themselves out is unrecoverable without database access, so it
        // is refused here rather than discovered at the next sign-in.
        if (user.Id == adminId)
        {
            throw new BusinessRuleException(
                "You cannot suspend your own account.",
                "CannotSuspendSelf");
        }

        if (user.IsActive == request.Request.IsActive)
        {
            return user.ToAdminDto();
        }

        user.IsActive = request.Request.IsActive;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            user.Id,
            NotificationType.System,
            user.IsActive ? "Your account is active again" : "Your account has been suspended",
            user.IsActive
                ? "You can use Jobmate as normal again."
                : $"Contact support if you think this is a mistake. {request.Request.Reason}",
            NotificationReferenceType.None,
            null,
            null,
            cancellationToken);

        return user.ToAdminDto();
    }
}
