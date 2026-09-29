using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Applications.Commands;

/// <summary>
/// A worker applies to a job. POST /api/v1/jobs/{jobId}/apply.
///
/// The apply is the one place that has to be forgiving: the same worker tapping Apply
/// twice must not produce two rows, and the database's unique index on
/// (job_id, worker_id) is the backstop for a genuine race.
/// </summary>
public sealed record ApplyToJobCommand(Guid JobId, ApplyToJobRequest Request) : IRequest<JobApplicationDto>;

public sealed class ApplyToJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<ApplyToJobCommand, JobApplicationDto>
{
    public async Task<JobApplicationDto> Handle(ApplyToJobCommand request, CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));
        var now = clock.UtcNow;

        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        var worker = await db.Users
            .Include(u => u.WorkerProfile)
            .FirstOrDefaultAsync(u => u.Id == workerId, cancellationToken)
            ?? throw new NotFoundException("Your account no longer exists.");

        // Read before writing so a second apply is refused with a readable message
        // rather than by the unique index.
        var existing = await db.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.JobId == job.Id && a.WorkerId == workerId, cancellationToken);

        var application = ApplicationRules.Create(
            job,
            worker,
            request.Request.Message ?? string.Empty,
            request.Request.ExpectedPay,
            request.Request.AvailableFrom,
            now,
            existing);

        db.JobApplications.Add(application);

        // The worker dashboard counts applications from the profile, so it is kept in
        // step here rather than recomputed per read.
        if (worker.WorkerProfile is { } profile)
        {
            profile.TotalApplications++;
            profile.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            job.GiverId,
            NotificationType.NewApplicant,
            "New applicant",
            $"{worker.FullName} applied to \"{job.Title}\".",
            NotificationReferenceType.Job,
            job.Id,
            NotificationRoutes.JobApplications,
            cancellationToken);

        return application.ToDto(job);
    }
}

/// <summary>The worker takes their application back. POST /api/v1/applications/{id}/withdraw.</summary>
public sealed record WithdrawApplicationCommand(Guid ApplicationId) : IRequest<JobApplicationDto>;

public sealed class WithdrawApplicationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<WithdrawApplicationCommand, JobApplicationDto>
{
    public async Task<JobApplicationDto> Handle(WithdrawApplicationCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireRole(nameof(UserRole.Worker));
        var now = clock.UtcNow;

        var application = await db.JobApplications
            .Include(a => a.Job)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException("Application not found.");

        var worker = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Your account no longer exists.");

        ApplicationRules.Withdraw(application, worker, now);

        await db.SaveChangesAsync(cancellationToken);

        var job = application.Job!;

        await notifications.SendAsync(
            job.GiverId,
            NotificationType.ApplicationUpdate,
            "Application withdrawn",
            $"{worker.FullName} withdrew their application for \"{job.Title}\".",
            NotificationReferenceType.Job,
            job.Id,
            NotificationRoutes.JobApplications,
            cancellationToken);

        return application.ToDto(job);
    }
}

/// <summary>The giver keeps an applicant on the shortlist. POST /api/v1/applications/{id}/shortlist.</summary>
public sealed record ShortlistApplicationCommand(Guid ApplicationId, ReviewApplicationRequest Request)
    : IRequest<JobApplicationDto>;

public sealed class ShortlistApplicationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<ShortlistApplicationCommand, JobApplicationDto>
{
    public async Task<JobApplicationDto> Handle(ShortlistApplicationCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        var (application, job) = await ApplicationOwnership.LoadAsync(db, request.ApplicationId, callerId, cancellationToken);

        ApplicationRules.Shortlist(application, now);

        if (request.Request.ProposedPay is { } proposedPay)
        {
            application.ProposedPay = proposedPay;
        }

        application.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            application.WorkerId,
            NotificationType.JobShortlisted,
            "You were shortlisted",
            $"You were shortlisted for \"{job.Title}\".",
            NotificationReferenceType.Application,
            application.Id,
            NotificationRoutes.ApplicationDetails,
            cancellationToken);

        return application.ToDto(job);
    }
}

/// <summary>The giver turns an applicant down. POST /api/v1/applications/{id}/reject.</summary>
public sealed record RejectApplicationCommand(Guid ApplicationId, ReviewApplicationRequest Request)
    : IRequest<JobApplicationDto>;

public sealed class RejectApplicationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<RejectApplicationCommand, JobApplicationDto>
{
    public async Task<JobApplicationDto> Handle(RejectApplicationCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        var (application, job) = await ApplicationOwnership.LoadAsync(db, request.ApplicationId, callerId, cancellationToken);

        ApplicationRules.Reject(application, request.Request.Reason, now);
        application.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            application.WorkerId,
            NotificationType.ApplicationUpdate,
            "Application not selected",
            $"Your application for \"{job.Title}\" was not accepted this time.",
            NotificationReferenceType.Application,
            application.Id,
            NotificationRoutes.ApplicationDetails,
            cancellationToken);

        return application.ToDto(job);
    }
}

/// <summary>
/// Loads an application with its job and refuses the request unless the caller posted
/// that job. Shared by every giver-side application action so ownership is checked the
/// same way everywhere.
/// </summary>
internal static class ApplicationOwnership
{
    public static async Task<(JobApplication Application, Job Job)> LoadAsync(
        IAppDbContext db,
        Guid applicationId,
        Guid giverId,
        CancellationToken cancellationToken)
    {
        var application = await db.JobApplications
            .Include(a => a.Job)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new NotFoundException("Application not found.");

        var job = application.Job ?? throw new NotFoundException("The job for this application no longer exists.");

        if (job.GiverId != giverId)
        {
            throw new ForbiddenException("Only the person who posted this job can manage its applicants.");
        }

        return (application, job);
    }
}
