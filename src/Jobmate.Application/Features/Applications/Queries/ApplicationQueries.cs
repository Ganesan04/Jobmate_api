using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Applications.Queries;

/// <summary>
/// The giver's applicant list for one job. GET /api/v1/jobs/{jobId}/applications.
///
/// Opening the list is what marks an application as seen, so the "applied" badge in the
/// app clears without a separate read endpoint.
/// </summary>
public sealed record GetJobApplicationsQuery(Guid JobId, GetApplicationsRequest Request)
    : IRequest<PagedResult<JobApplicantDto>>;

public sealed class GetJobApplicationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<GetJobApplicationsQuery, PagedResult<JobApplicantDto>>
{
    public async Task<PagedResult<JobApplicantDto>> Handle(
        GetJobApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        var job = await db.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        if (job.GiverId != giverId)
        {
            throw new ForbiddenException("Only the person who posted this job can see its applicants.");
        }

        var source = db.JobApplications.Where(a => a.JobId == job.Id);

        // Seen first, then filtered: a status filter of "applied" would otherwise hide
        // the very applications this request is what marks as seen.
        var unseen = await source
            .Where(a => a.Status == ApplicationStatus.Applied && a.ViewedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var application in unseen)
        {
            ApplicationRules.MarkViewed(application, now);
        }

        if (unseen.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var filtered = source.ApplyApplicantFilters(request.Request);

        var total = await filtered.CountAsync(cancellationToken);

        var page = await filtered
            .Include(a => a.Worker!)
            .ThenInclude(w => w!.WorkerProfile!)
            .ThenInclude(p => p!.Skills)
            .ApplyApplicantSort(request.Request.SortBy)
            .Skip(request.Request.Skip)
            .Take(request.Request.Take)
            .ToListAsync(cancellationToken);

        var items = page
            .Select(a => a.ToApplicantDto(
                a.Worker!,
                ApplicationQueryExtensions.ApplicantDistanceKm(job, a.Worker!.WorkerProfile)))
            .ToList();

        return PagedResult<JobApplicantDto>.Create(items, request.Request.Page, request.Request.PageSize, total);
    }
}

/// <summary>
/// The worker's own applications, newest first, with the job summary attached.
/// GET /api/v1/worker/applications.
/// </summary>
public sealed record GetWorkerApplicationsQuery(GetWorkerApplicationsRequest Request)
    : IRequest<PagedResult<JobApplicationDto>>;

public sealed class GetWorkerApplicationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetWorkerApplicationsQuery, PagedResult<JobApplicationDto>>
{
    public async Task<PagedResult<JobApplicationDto>> Handle(
        GetWorkerApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var source = db.JobApplications.Where(a => a.WorkerId == workerId);

        if (request.Request.Statuses is { Count: > 0 } statuses)
        {
            source = source.Where(a => statuses.Contains(a.Status));
        }

        if (request.Request.JobStatus is { } jobStatus)
        {
            // Lets the app show "my applications for jobs that are still running"
            // without fetching every job first.
            source = source.Where(a => a.Job!.Status == jobStatus);
        }

        var total = await source.CountAsync(cancellationToken);

        var page = await source
            .Include(a => a.Job)
            .OrderByDescending(a => a.AppliedAt)
            .ThenBy(a => a.Id)
            .Skip(request.Request.Skip)
            .Take(request.Request.Take)
            .ToListAsync(cancellationToken);

        var items = page.Select(a => a.ToDto(a.Job)).ToList();

        return PagedResult<JobApplicationDto>.Create(items, request.Request.Page, request.Request.PageSize, total);
    }
}
