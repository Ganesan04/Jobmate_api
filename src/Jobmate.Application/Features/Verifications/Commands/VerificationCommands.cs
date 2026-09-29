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
/// Starts a verification request. POST /api/v1/verifications.
///
/// One live request per type per user. A user who already has an identity check pending
/// cannot open a second one: two live requests for the same check would leave an admin
/// deciding between them with no way to tell which document is the current one.
/// </summary>
public sealed record SubmitVerificationCommand(SubmitVerificationRequest Request)
    : IRequest<VerificationRequestDto>;

public sealed class SubmitVerificationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
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
        // check with no address, is a request no admin could ever approve.
        var submission = VerificationRules.Create(user, input.Type, input.DocumentName, input.Note, now);

        // The uploaded document is a WorkerDocument row; linking it here is what lets an
        // admin open the file from the queue without a second lookup.
        if (!string.IsNullOrWhiteSpace(input.DocumentUrl))
        {
            submission.DocumentUrl = input.DocumentUrl;
        }

        db.VerificationRequests.Add(submission);

        // A request in flight moves the user to pending, but never back down: a user who
        // is already rejected shows rejected until the new attempt is decided, because
        // flipping them to pending would hide the rejection they need to act on.
        if (user.VerificationStatus != VerificationStatus.Rejected)
        {
            user.VerificationStatus = VerificationStatus.Pending;
        }

        user.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return submission.ToDto(VerificationStatus.Pending);
    }
}

/// <summary>
/// Withdraws a pending request. POST /api/v1/verifications/{id}/withdraw.
///
/// A separate action rather than a delete, because the history of what was submitted and
/// rejected is part of the record an admin may need to look back at, and because a
/// withdrawn row still tells the user "you tried this and took it back".
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
