using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Verifications;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Verifications.Commands;

/// <summary>
/// Submits a verification request. POST /api/v1/verifications.
///
/// Approved immediately: there is no review queue. The request belongs to the person, so
/// the recomputed badge counts in both worker and work giver mode. A type that still has
/// a pending request from before auto-approval cannot be submitted again until that one
/// is withdrawn, so the history never holds two live requests for the same check.
/// </summary>
public sealed record SubmitVerificationCommand(SubmitVerificationRequest Request)
    : IRequest<VerificationRequestDto>;

public sealed class SubmitVerificationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<SubmitVerificationCommand, VerificationRequestDto>
{
    public async Task<VerificationRequestDto> Handle(
        SubmitVerificationCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Profile not found.");

        var alreadyPending = await db.VerificationRequests
            .AnyAsync(
                r => r.UserId == callerId && r.Type == input.Type && r.Status == VerificationRequestStatus.Pending,
                cancellationToken);

        if (alreadyPending)
        {
            throw new ConflictException(
                $"You already have a {input.Type.ToString().ToLowerInvariant()} check under review.",
                "VerificationAlreadyPending");
        }

        // Checks the request is answerable: a phone check with no number, or an email
        // check with no address, is not something that can be verified.
        var submission = VerificationRules.Create(user, input.Type, input.DocumentName, input.Note, now);

        if (!string.IsNullOrWhiteSpace(input.DocumentUrl))
        {
            submission.DocumentUrl = input.DocumentUrl;
        }

        db.VerificationRequests.Add(submission);

        // Recomputed from the whole history rather than folded forward, so the stored
        // status can never drift from the badge GET /verifications/summary shows.
        var history = await db.VerificationRequests
            .AsNoTracking()
            .Where(v => v.UserId == user.Id)
            .ToListAsync(cancellationToken);

        var rollup = VerificationSummary.Build(history.Append(submission));

        user.VerificationStatus = rollup.Overall;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            user.Id,
            NotificationType.ApplicationUpdate,
            "Verification approved",
            $"Your {input.Type.ToString().ToLowerInvariant()} check was approved.",
            NotificationReferenceType.Verification,
            submission.Id,
            "/verifications",
            cancellationToken);

        return submission.ToDto(rollup.StatusOf(submission.Type));
    }
}

/// <summary>
/// Withdraws a pending request. POST /api/v1/verifications/{id}/withdraw.
///
/// New requests are approved on submission, so this only applies to pending rows created
/// before that; an approved request is refused with a 400. A separate action rather than
/// a delete, because a withdrawn row still tells the user "you tried this and took it back".
/// </summary>
public sealed record WithdrawVerificationCommand(Guid RequestId, WithdrawVerificationRequest Request)
    : IRequest<VerificationRequestDto>;

public sealed class WithdrawVerificationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<WithdrawVerificationCommand, VerificationRequestDto>
{
    public async Task<VerificationRequestDto> Handle(
        WithdrawVerificationCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var verification = await db.VerificationRequests
            .FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken)
            ?? throw new NotFoundException("That verification request does not exist.");

        // Scoped to the caller rather than filtered out with a 404: a user withdrawing
        // their own request should not be able to touch anyone else's by guessing an id.
        if (verification.UserId != callerId)
        {
            throw new NotFoundException("That verification request does not exist.");
        }

        if (!verification.IsPending)
        {
            throw new BusinessRuleException(
                "This request has already been reviewed.",
                "VerificationAlreadyReviewed");
        }

        verification.Status = VerificationRequestStatus.Withdrawn;
        verification.RejectionReason = null;
        verification.ReviewedAt = null;
        verification.UpdatedAt = now;

        var user = await db.Users.FirstAsync(u => u.Id == callerId, cancellationToken);
        var stillPending = await db.VerificationRequests
            .AnyAsync(
                r => r.UserId == callerId
                     && r.Status == VerificationRequestStatus.Pending
                     && r.Id != verification.Id,
                cancellationToken);

        // Withdrawing the only outstanding request drops the user back to not-verified,
        // but never past a rejection they still have to act on.
        if (!stillPending && user.VerificationStatus == VerificationStatus.Pending)
        {
            user.VerificationStatus = VerificationStatus.NotVerified;
            user.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return verification.ToDto(VerificationStatus.NotVerified);
    }
}
