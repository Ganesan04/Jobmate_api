using Jobmate.Application.Common;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Admin.Queries;

/// <summary>
/// The verification review queue. GET /api/v1/admin/verifications.
///
/// Ordered oldest first on purpose: a pending request is a person waiting on an answer,
/// and the queue an admin works through should be the one where waiting has cost the most
/// time, not the one they happened to refresh last.
/// </summary>
public sealed record GetAdminVerificationsQuery(GetAdminVerificationsRequest Request)
    : IRequest<PagedResult<AdminVerificationRequestDto>>;

public sealed class GetAdminVerificationsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdminVerificationsQuery, PagedResult<AdminVerificationRequestDto>>
{
    public async Task<PagedResult<AdminVerificationRequestDto>> Handle(
        GetAdminVerificationsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Request;

        var query = db.VerificationRequests
            .AsNoTracking()
            .Where(v => v.Status == filter.Status)
            .WhereIf(filter.Type is null ? null : v => v.Type == filter.Type);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = TextSearchExtensions.ToLikePattern(filter.Search);
            var escape = TextSearchExtensions.LikeEscape;

            // The applicant and the document are two different columns, so the filter has
            // to reach both: an admin searching a phone number fragment and one searching
            // a file name are looking at the same queue.
            query = query.Where(v =>
                (v.User != null &&
                 (EF.Functions.Like(v.User.FirstName.ToLower(), pattern, escape) ||
                  EF.Functions.Like(v.User.LastName.ToLower(), pattern, escape) ||
                  EF.Functions.Like(v.User.PhoneNumber.ToLower(), pattern, escape))) ||
                EF.Functions.Like((v.DocumentName ?? string.Empty).ToLower(), pattern, escape));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(v => new { Request = v, v.User, v.ReviewedBy })
            .ToListAsync(cancellationToken);

        // Assembled here rather than projected in the query because the mapping reads
        // three navigations, and projecting them separately would produce one row per
        // navigation instead of one per request.
        var items = rows
            .Select(row =>
            {
                row.Request.User = row.User;
                row.Request.ReviewedBy = row.ReviewedBy;

                return row.Request.ToAdminDto();
            })
            .ToList();

        return PagedResult<AdminVerificationRequestDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>
/// The users table. GET /api/v1/admin/users.
///
/// The applicant and rating columns are counts and averages gathered in two extra
/// grouped queries rather than per user, so listing a thousand accounts is three round
/// trips rather than two thousand.
/// </summary>
public sealed record GetAdminUsersQuery(GetAdminUsersRequest Request)
    : IRequest<PagedResult<AdminUserDto>>;

public sealed class GetAdminUsersQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{
    public async Task<PagedResult<AdminUserDto>> Handle(
        GetAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Request;

        var query = db.Users
            .AsNoTracking()
            .WhereIf(filter.Role is null ? null : u => u.Role == filter.Role)
            .WhereIf(filter.IsActive is null ? null : u => u.IsActive == filter.IsActive)
            .WhereIf(filter.Verification is null ? null : u => u.VerificationStatus == filter.Verification);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = TextSearchExtensions.ToLikePattern(filter.Search);
            var escape = TextSearchExtensions.LikeEscape;

            query = query.Where(u =>
                EF.Functions.Like(u.FirstName.ToLower(), pattern, escape) ||
                EF.Functions.Like(u.LastName.ToLower(), pattern, escape) ||
                EF.Functions.Like((u.Email ?? string.Empty).ToLower(), pattern, escape) ||
                EF.Functions.Like(u.PhoneNumber.ToLower(), pattern, escape));
        }

        var total = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        var ids = users.Select(u => u.Id).ToList();

        // A giver's count is jobs posted, a worker's is applications made: the column
        // means "how active is this person", and what counts as activity depends on which
        // side of the marketplace they are on.
        var jobCounts = await db.Jobs
            .AsNoTracking()
            .Where(j => ids.Contains(j.GiverId))
            .GroupBy(j => j.GiverId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var applicationCounts = await db.JobApplications
            .AsNoTracking()
            .Where(a => ids.Contains(a.WorkerId))
            .GroupBy(a => a.WorkerId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var ratings = await db.Ratings
            .AsNoTracking()
            .Where(r => ids.Contains(r.ToUserId))
            .GroupBy(r => r.ToUserId)
            .Select(g => new { UserId = g.Key, Average = g.Average(r => r.Stars) })
            .ToDictionaryAsync(x => x.UserId, x => (decimal?)x.Average, cancellationToken);

        var items = users
            .Select(user => user.ToAdminDto(
                user.Role == UserRole.WorkGiver
                    ? jobCounts.GetValueOrDefault(user.Id)
                    : applicationCounts.GetValueOrDefault(user.Id),
                ratings.GetValueOrDefault(user.Id)))
            .ToList();

        return PagedResult<AdminUserDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>The jobs table. GET /api/v1/admin/jobs.</summary>
public sealed record GetAdminJobsQuery(GetAdminJobsRequest Request) : IRequest<PagedResult<AdminJobDto>>;

public sealed class GetAdminJobsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdminJobsQuery, PagedResult<AdminJobDto>>
{
    public async Task<PagedResult<AdminJobDto>> Handle(
        GetAdminJobsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = request.Request;

        var query = db.Jobs
            .AsNoTracking()
            .WhereIf(filter.Status is null ? null : j => j.Status == filter.Status)
            .WhereIf(filter.Category is null ? null : j => j.Category == filter.Category);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = TextSearchExtensions.ToLikePattern(filter.Search);
            var escape = TextSearchExtensions.LikeEscape;

            query = query.Where(j =>
                EF.Functions.Like(j.Title.ToLower(), pattern, escape) ||
                EF.Functions.Like(j.Company.ToLower(), pattern, escape));
        }

        var total = await query.CountAsync(cancellationToken);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(j => new { Job = j, j.Giver })
            .ToListAsync(cancellationToken);

        var ids = jobs.Select(x => x.Job.Id).ToList();

        var applicants = await db.JobApplications
            .AsNoTracking()
            .Where(a => ids.Contains(a.JobId))
            .GroupBy(a => a.JobId)
            .Select(g => new { JobId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobId, x => x.Count, cancellationToken);

        var items = jobs
            .Select(x =>
            {
                x.Job.Giver = x.Giver;

                return x.Job.ToAdminDto(applicants.GetValueOrDefault(x.Job.Id));
            })
            .ToList();

        return PagedResult<AdminJobDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>
/// The job detail panel. GET /api/v1/admin/jobs/{id}.
///
/// Answers the two questions an admin opens a job to ask: did the hiring actually happen,
/// and was anyone paid.
/// </summary>
public sealed record GetAdminJobDetailQuery(Guid JobId) : IRequest<AdminJobDetailDto>;

public sealed class GetAdminJobDetailQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdminJobDetailQuery, AdminJobDetailDto>
{
    public async Task<AdminJobDetailDto> Handle(
        GetAdminJobDetailQuery request,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("That job does not exist.");

        var applicants = await db.JobApplications.CountAsync(a => a.JobId == job.Id, cancellationToken);

        // Shortlisted and accepted are counted separately rather than as "one way or the
        // other": an admin comparing them is asking how much of the pipeline converted.
        var shortlisted = await db.JobApplications
            .CountAsync(
                a => a.JobId == job.Id
                      && (a.Status == ApplicationStatus.Shortlisted || a.Status == ApplicationStatus.Accepted),
                cancellationToken);

        var selected = await db.WorkerAssignments
            .CountAsync(a => a.JobId == job.Id && a.Status != AssignmentStatus.Cancelled, cancellationToken);

        var paidPayments = await db.Payments
            .Where(p => p.JobId == job.Id && p.Status == PaymentStatus.Paid)
            .ToListAsync(cancellationToken);

        var openDisputes = await db.Disputes
            .CountAsync(
                d => d.JobId == job.Id && (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview),
                cancellationToken);

        return job.ToAdminDetailDto(
            applicants,
            shortlisted,
            selected,
            paidPayments.Count,
            paidPayments.Sum(p => p.TotalAmount),
            openDisputes);
    }
}
