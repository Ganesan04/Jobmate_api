using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Application.DTOs.Trust;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Trust.Commands;

/// <summary>
/// Raises a dispute against the other participant of a booking.
/// POST /api/v1/disputes.
///
/// Only participants may raise one, and the counterpart is worked out from the booking
/// rather than read from the request, so a complaint cannot be aimed at a third party.
/// </summary>
public sealed record CreateDisputeCommand(CreateDisputeRequest Request) : IRequest<DisputeDto>;

public sealed class CreateDisputeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<CreateDisputeCommand, DisputeDto>
{
    public async Task<DisputeDto> Handle(CreateDisputeCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        var job = await db.Jobs
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == input.JobId, cancellationToken)
            ?? throw new NotFoundException("That job does not exist.");

        var raiser = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Your account no longer exists.");

        // One live dispute per person per job. A second complaint while the first is open is
        // almost always the same problem sent again, and admitting both would give an admin
        // two rows to reconcile and the other party two things to answer.
        var alreadyOpen = await db.Disputes.AnyAsync(
            d => d.JobId == job.Id
                 && d.RaisedByUserId == callerId
                 && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview),
            cancellationToken);

        if (alreadyOpen)
        {
            throw new ConflictException(
                "You already have an open dispute about this job.",
                "DisputeAlreadyOpen");
        }

        var dispute = DisputeRules.Create(job, raiser, input.Reason, input.Description, input.Amount, now);

        db.Disputes.Add(dispute);
        await db.SaveChangesAsync(cancellationToken);

        // Money is held only when the complaint actually names an amount, and only for a
        // payment that has already settled. Holding every disputed job's money would let
        // anyone freeze a worker's payout by opening an unsafe-workplace complaint that has
        // nothing to do with pay.
        if (input.Amount > 0)
        {
            var settled = await db.Payments
                .FirstOrDefaultAsync(
                    p => p.JobId == job.Id
                         && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Disputed),
                    cancellationToken);

            if (settled is not null)
            {
                PaymentRules.MarkDisputed(settled);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            dispute.AgainstUserId,
            NotificationType.System,
            "A dispute has been raised",
            "A dispute about your booking has been raised. Our team will look into it.",
            NotificationReferenceType.Dispute,
            dispute.Id,
            "/disputes",
            cancellationToken);

        return await DisputeLoader.ProjectAsync(db, dispute, cancellationToken);
    }
}

/// <summary>
/// Marks a dispute as being looked at. POST /api/v1/admin/disputes/{id}/review.
///
/// Separate from resolving so the queue can show what has been picked up, and so a second
/// admin does not start duplicating work that is already in progress.
/// </summary>
public sealed record StartDisputeReviewCommand(Guid DisputeId) : IRequest<DisputeDto>;

public sealed class StartDisputeReviewCommandHandler(
    IAppDbContext db,
    IClock clock) : IRequestHandler<StartDisputeReviewCommand, DisputeDto>
{
    public async Task<DisputeDto> Handle(StartDisputeReviewCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var dispute = await DisputeLoader.RequireAsync(db, request.DisputeId, cancellationToken);

        if (dispute.Status != DisputeStatus.Open)
        {
            throw new BusinessRuleException(
                $"A {dispute.Status.ToString().ToLowerInvariant()} dispute cannot be taken for review.",
                "InvalidDisputeTransition");
        }

        dispute.Status = DisputeStatus.UnderReview;
        dispute.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return await DisputeLoader.ProjectAsync(db, dispute, cancellationToken);
    }
}

/// <summary>
/// Closes a dispute. POST /api/v1/admin/disputes/{id}/resolve.
///
/// Resolved and rejected share the endpoint because both need the same things: a status
/// and written notes. What they differ on is only who was right, which the notes say.
/// </summary>
public sealed record ResolveDisputeCommand(Guid DisputeId, ResolveDisputeRequest Request)
    : IRequest<DisputeDto>;

public sealed class ResolveDisputeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    INotificationService notifications,
    IClock clock) : IRequestHandler<ResolveDisputeCommand, DisputeDto>
{
    public async Task<DisputeDto> Handle(ResolveDisputeCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        var dispute = await DisputeLoader.RequireAsync(db, request.DisputeId, cancellationToken);

        DisputeRules.Resolve(dispute, input.Status, input.Resolution, adminId, now);

        // Closing a dispute stops the money being questioned, so anything held for it goes
        // back to the worker. Moving it on to the giver is a separate, deliberate step: the
        // admin refund endpoint does that and leaves its own audit trail.
        var held = await db.Payments
            .Where(p => p.JobId == dispute.JobId && p.Status == PaymentStatus.Disputed)
            .ToListAsync(cancellationToken);

        foreach (var payment in held)
        {
            PaymentRules.ReleaseFromDispute(payment, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        var outcome = input.Status == DisputeStatus.Resolved ? "resolved" : "rejected";

        await notifications.SendManyAsync(
            [dispute.RaisedByUserId, dispute.AgainstUserId],
            NotificationType.System,
            $"Dispute {outcome}",
            input.Resolution.Trim(),
            NotificationReferenceType.Dispute,
            dispute.Id,
            "/disputes",
            cancellationToken);

        return await DisputeLoader.ProjectAsync(db, dispute, cancellationToken);
    }
}

/// <summary>
/// Raises a support or safety report. POST /api/v1/support-reports.
///
/// Separate from a dispute because a report does not need a booking: somebody can report a
/// fake job listing they only ever saw, and that has no counterpart to argue with.
/// </summary>
public sealed record CreateSupportReportCommand(CreateSupportReportRequest Request)
    : IRequest<SupportReportDto>;

public sealed class CreateSupportReportCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<CreateSupportReportCommand, SupportReportDto>
{
    public async Task<SupportReportDto> Handle(
        CreateSupportReportCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        if (string.IsNullOrWhiteSpace(input.Details) || input.Details.Trim().Length < 10)
        {
            throw new BusinessRuleException(
                "Describe the problem in at least 10 characters.",
                "DetailsTooShort");
        }

        var report = new SupportReport
        {
            UserId = callerId,
            Reason = input.Reason,
            Details = input.Details.Trim(),
            RelatedJobId = string.IsNullOrWhiteSpace(input.RelatedJobId) ? null : input.RelatedJobId.Trim(),
            IsResolved = false,
            CreatedAt = now
        };

        db.SupportReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        return await DisputeLoader.ProjectReportAsync(db, report, cancellationToken);
    }
}

/// <summary>
/// Ticks a support report off. POST /api/v1/admin/support-reports/{id}/resolve.
///
/// Reports have no comment thread and no status enum, only a flag, so this is a single
/// decision that can be undone by calling it again with isResolved false.
/// </summary>
public sealed record ResolveSupportReportCommand(Guid ReportId, bool IsResolved) : IRequest<SupportReportAdminDto>;

public sealed class ResolveSupportReportCommandHandler(
    IAppDbContext db,
    IClock clock) : IRequestHandler<ResolveSupportReportCommand, SupportReportAdminDto>
{
    public async Task<SupportReportAdminDto> Handle(
        ResolveSupportReportCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var report = await db.SupportReports
            .FirstOrDefaultAsync(r => r.Id == request.ReportId, cancellationToken)
            ?? throw new NotFoundException("That report does not exist.");

        report.IsResolved = request.IsResolved;
        report.ResolvedAt = request.IsResolved ? now : null;
        report.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        var reporter = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == report.UserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new SupportReportAdminDto
        {
            Id = report.Id,
            Reason = report.Reason,
            Details = report.Details,
            SubmittedAt = report.CreatedAt,
            ReportedBy = reporter,
            ReportedByUserId = report.UserId,
            RelatedJobId = report.RelatedJobId,
            IsResolved = report.IsResolved,
            ResolvedAt = report.ResolvedAt
        };
    }
}

/// <summary>
/// The dispute reads both sides need: the people involved, the job, and the admin's notes.
///
/// Assembled from flat queries rather than includes. The navigation graph from a dispute to
/// a job and both users is wider than any screen needs, and populating it by hand would
/// assign untracked entities onto a tracked dispute — which EF reads as new rows to insert.
/// </summary>
internal static class DisputeLoader
{
    /// <summary>Loads a dispute for writing, or throws if it does not exist.</summary>
    public static async Task<Dispute> RequireAsync(
        IAppDbContext db,
        Guid disputeId,
        CancellationToken cancellationToken) =>
        await db.Disputes
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken)
            ?? throw new NotFoundException("That dispute does not exist.");

    /// <summary>Builds the DTO for a dispute that is already tracked.</summary>
    public static async Task<DisputeDto> ProjectAsync(
        IAppDbContext db,
        Dispute dispute,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == dispute.JobId, cancellationToken);

        var people = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == dispute.RaisedByUserId || u.Id == dispute.AgainstUserId)
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var resolver = dispute.ResolvedByUserId is null
            ? null
            : await db.Users
                .AsNoTracking()
                .Where(u => u.Id == dispute.ResolvedByUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(cancellationToken);

        return new DisputeDto
        {
            Id = dispute.Id,
            Reason = dispute.Reason,
            RaisedBy = people.GetValueOrDefault(dispute.RaisedByUserId) ?? string.Empty,
            RaisedByUserId = dispute.RaisedByUserId,
            AgainstUser = people.GetValueOrDefault(dispute.AgainstUserId) ?? string.Empty,
            AgainstUserId = dispute.AgainstUserId,
            JobId = dispute.JobId,
            JobTitle = job?.Title ?? string.Empty,
            Company = job?.Company ?? string.Empty,
            RaisedAt = dispute.CreatedAt,
            Status = dispute.Status,
            Amount = dispute.Amount,
            Details = dispute.Description,
            Notes = dispute.Resolution ?? string.Empty,
            ResolvedBy = resolver,
            ResolvedAt = dispute.ResolvedAt
        };
    }

    /// <summary>Builds the DTO for a support report, naming the job it was filed against.</summary>
    public static async Task<SupportReportDto> ProjectReportAsync(
        IAppDbContext db,
        SupportReport report,
        CancellationToken cancellationToken)
    {
        // The column holds the job id as text, so it is parsed here rather than compared in
        // SQL: Guid.ToString() is not translatable, and an unparseable value simply means
        // the report is not tied to a job we can still name.
        var relatedId = Guid.TryParse(report.RelatedJobId, out var parsed) ? parsed : (Guid?)null;

        var related = relatedId is null
            ? null
            : await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == relatedId.Value)
                .Select(j => j.Title)
                .FirstOrDefaultAsync(cancellationToken);

        return new SupportReportDto
        {
            Id = report.Id,
            Reason = report.Reason,
            Details = report.Details,
            SubmittedAt = report.CreatedAt,
            RelatedJob = related ?? string.Empty,
            IsResolved = report.IsResolved
        };
    }
}
