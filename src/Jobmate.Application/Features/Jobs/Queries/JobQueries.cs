using Jobmate.Application.Common;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Jobs;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Jobs.Queries;

/// <summary>
/// One job. <c>Latitude</c>/<c>Longitude</c> are the caller's own coordinates, not
/// filters: they are what the returned <c>distanceKm</c> is measured from.
/// </summary>
public sealed record GetJobByIdQuery(
    Guid JobId,
    double? Latitude = null,
    double? Longitude = null) : IRequest<JobDto>;

public sealed class GetJobByIdQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetJobByIdQuery, JobDto>
{
    public async Task<JobDto> Handle(GetJobByIdQuery request, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId;

        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .Include(j => j.Skills)
            .Include(j => j.WeekdayRows)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        // A draft is private to its owner, so an unlisted posting cannot be probed for
        // by id.
        if (job.Status is JobStatus.Draft or JobStatus.Pending && job.GiverId != viewerId)
        {
            throw new NotFoundException("Job not found.");
        }

        var applicantCount = await db.JobApplications
            .AsNoTracking()
            .CountAsync(a => a.JobId == job.Id, cancellationToken);

        var canSeeAddress = await CanSeeAddressAsync(job, viewerId, cancellationToken);

        return job.ToDto(
            applicantCount,
            request.Latitude,
            request.Longitude,
            job.Skills.Select(s => s.Name).ToList()) with
        {
            // The exact address stays hidden until the worker is booked, so the
            // privacy rule cannot be bypassed by fetching the job directly.
            Address = canSeeAddress ? job.Address : string.Empty,
            Latitude = canSeeAddress ? job.Latitude : null,
            Longitude = canSeeAddress ? job.Longitude : null
        };
    }

    /// <summary>
    /// Whether the caller may see the exact address: the giver always can, anyone can
    /// when the posting made it public, and a worker who is already booked on the job
    /// is told where to report.
    /// </summary>
    private async Task<bool> CanSeeAddressAsync(Job job, Guid? viewerId, CancellationToken cancellationToken)
    {
        if (job.LocationVisible || job.GiverId == viewerId)
        {
            return true;
        }

        if (viewerId is not { } workerId)
        {
            return false;
        }

        return await db.WorkerAssignments
            .AsNoTracking()
            .AnyAsync(a => a.JobId == job.Id && a.WorkerId == workerId, cancellationToken);
    }
}

/// <summary>
/// Public job search. Every documented filter is applied in SQL, and the page is
/// counted, sorted and projected in the database.
/// </summary>
public sealed record SearchJobsQuery(JobSearchRequest Request) : IRequest<PagedResult<JobDto>>;

public sealed class SearchJobsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<SearchJobsQuery, PagedResult<JobDto>>
{
    public async Task<PagedResult<JobDto>> Handle(SearchJobsQuery request, CancellationToken cancellationToken)
    {
        var body = request.Request;
        var viewerId = currentUser.UserId;

        if (body.RadiusKm is > 0 && (body.Latitude is null || body.Longitude is null))
        {
            throw new BusinessRuleException(
                "A radius filter needs the caller's latitude and longitude.", "CoordinatesRequired");
        }

        var query = db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .ApplyDiscoverable();

        query = query
            .ApplySearch(body.Search)
            .ApplyCategories(body.Categories)
            .ApplyStatuses(body.Statuses)
            .ApplyArea(body.Area)
            .ApplyPayRange(body.MinPay, body.MaxPay)
            .ApplyDateRange(body.StartDate, body.EndDate)
            .ApplyJobType(body.JobType)
            .ApplyWorkplaceType(body.WorkplaceType)
            .ApplyUrgent(body.UrgentOnly)
            .ApplyRecurring(body.RecurringOnly)
            .ApplyVerifiedGiver(body.VerifiedGiverOnly)
            .ApplyRadius(body.Latitude, body.Longitude, body.RadiusKm)
            .ApplySort(body.SortBy, body.Descending, body.Latitude, body.Longitude);

        var visibleTo = await db.VisibleHiddenLocationJobIdsAsync(query, viewerId, cancellationToken);

        return await query.ToPagedResultAsync(body, body.Latitude, body.Longitude, visibleTo, cancellationToken);
    }
}

/// <summary>The caller's own postings. Always scoped to the authenticated giver.</summary>
public sealed record GetGiverJobsQuery(GiverJobsRequest Request) : IRequest<PagedResult<JobDto>>;

public sealed class GetGiverJobsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetGiverJobsQuery, PagedResult<JobDto>>
{
    public async Task<PagedResult<JobDto>> Handle(GetGiverJobsQuery request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var body = request.Request;

        var query = db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .Where(j => j.GiverId == giverId);

        query = query
            .ApplyStatuses(body.Statuses)
            .ApplyCategories(body.Category is null ? null : [body.Category.Value])
            .ApplySearch(body.Search)
            .ApplySort(body.Sort ?? JobQueryExtensions.SortKeys.Newest, true, null, null);

        return await query.ToPagedResultAsync(body, null, null, null, cancellationToken);
    }
}

/// <summary>
/// The worker's discovery feed. Restricted to open jobs with a free slot, so a
/// finished or fully booked posting never shows up under "available".
/// </summary>
public sealed record GetWorkerJobsQuery(WorkerJobsRequest Request) : IRequest<PagedResult<JobDto>>;

public sealed class GetWorkerJobsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<GetWorkerJobsQuery, PagedResult<JobDto>>
{
    public async Task<PagedResult<JobDto>> Handle(GetWorkerJobsQuery request, CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));
        var body = request.Request;

        var query = db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .ApplyStillOpen(clock.Today);

        query = query
            .ApplySearch(body.Search)
            .ApplyCategories(body.Categories)
            .ApplyArea(body.Area)
            .ApplyPayRange(body.MinPay, body.MaxPay)
            .ApplyDateRange(body.Date, body.Date)
            .ApplyJobType(body.JobType)
            .ApplyUrgent(body.UrgentOnly)
            .ApplyRecurring(body.RecurringOnly)
            .ApplyRadius(body.Latitude, body.Longitude, body.RadiusKm)
            .ApplySort(body.SortBy, true, body.Latitude, body.Longitude);

        var visibleTo = await db.VisibleHiddenLocationJobIdsAsync(query, workerId, cancellationToken);
        return await query.ToPagedResultAsync(body, body.Latitude, body.Longitude, visibleTo, cancellationToken);
    }
}

/// <summary>
/// Counts behind the giver home screen. Every figure is a database aggregate, so the
/// screen stays fast as the jobs table grows.
/// </summary>
public sealed record GetGiverStatsQuery : IRequest<GiverStatsDto>;

public sealed class GetGiverStatsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<GetGiverStatsQuery, GiverStatsDto>
{
    public async Task<GiverStatsDto> Handle(GetGiverStatsQuery request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));

        var jobs = db.Jobs.AsNoTracking().Where(j => j.GiverId == giverId);

        var byStatus = await jobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        int Count(JobStatus status) => byStatus.GetValueOrDefault(status);

        var jobIds = jobs.Select(j => j.Id);

        var applications = await db.JobApplications
            .AsNoTracking()
            .CountAsync(a => jobIds.Contains(a.JobId), cancellationToken);

        // "To confirm" is what the giver still has to act on: applications that are
        // neither shortlisted nor rejected.
        var toConfirm = await db.JobApplications
            .AsNoTracking()
            .CountAsync(a => jobIds.Contains(a.JobId) &&
                             (a.Status == ApplicationStatus.Applied || a.Status == ApplicationStatus.Viewed),
                cancellationToken);

        var totalHired = await db.WorkerAssignments
            .AsNoTracking()
            .CountAsync(a => a.GiverId == giverId, cancellationToken);

        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // The net figure is spelled out rather than using the computed
        // WorkerNetAmount property, which EF cannot translate.
        var spentThisMonth = await db.Payments
            .AsNoTracking()
            .Where(p => p.GiverId == giverId && p.PaidAt >= monthStart)
            .SumAsync(p => p.Amount + p.OvertimeAmount + p.PlatformFee, cancellationToken);

        return new GiverStatsDto
        {
            ActiveJobs = Count(JobStatus.Active),
            InProgressJobs = Count(JobStatus.InProgress),
            DraftJobs = Count(JobStatus.Draft) + Count(JobStatus.Pending),
            CompletedJobs = Count(JobStatus.Completed),
            CancelledJobs = Count(JobStatus.Cancelled) + Count(JobStatus.Expired),
            Applications = applications,
            ToConfirm = toConfirm,
            TotalHired = totalHired,
            SpentThisMonth = spentThisMonth
        };
    }
}

/// <summary>
/// Counts behind the worker home screen: how many applications are open, how many jobs
/// are booked, and what has been earned or is pending.
/// </summary>
public sealed record GetWorkerStatsQuery : IRequest<WorkerStatsDto>;

public sealed class GetWorkerStatsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetWorkerStatsQuery, WorkerStatsDto>
{
    public async Task<WorkerStatsDto> Handle(GetWorkerStatsQuery request, CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var applied = await db.JobApplications
            .AsNoTracking()
            .CountAsync(a => a.WorkerId == workerId &&
                             (a.Status == ApplicationStatus.Applied || a.Status == ApplicationStatus.Viewed ||
                              a.Status == ApplicationStatus.Shortlisted), cancellationToken);

        var active = await db.WorkerAssignments
            .AsNoTracking()
            .CountAsync(a => a.WorkerId == workerId &&
                             (a.Status == AssignmentStatus.Pending || a.Status == AssignmentStatus.Active ||
                              a.Status == AssignmentStatus.CheckedIn), cancellationToken);

        var completed = await db.WorkerAssignments
            .AsNoTracking()
            .CountAsync(a => a.WorkerId == workerId && a.Status == AssignmentStatus.Completed, cancellationToken);

        var totals = await db.Payments
            .AsNoTracking()
            .Where(p => p.WorkerId == workerId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Earned = g.Where(p => p.Status == PaymentStatus.Paid)
                    .Sum(p => (int?)(p.Amount + p.OvertimeAmount - p.PlatformFee)) ?? 0,
                Pending = g.Where(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing)
                    .Sum(p => (int?)(p.Amount + p.OvertimeAmount - p.PlatformFee)) ?? 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new WorkerStatsDto
        {
            Applied = applied,
            Active = active,
            Completed = completed,
            Earnings = totals?.Earned ?? 0,
            PendingAmount = totals?.Pending ?? 0
        };
    }
}
