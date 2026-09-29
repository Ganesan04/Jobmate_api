using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Application.DTOs.Trust;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Trust.Queries;

/// <summary>
/// The disputes a user has raised or been named in. GET /api/v1/disputes.
///
/// Both sides are returned deliberately: a worker who was complained about needs to see it
/// and answer it, not discover later that somebody opened a case in their name.
/// </summary>
public sealed record GetMyDisputesRequest : PageRequest
{
    /// <summary>Filter to one status, for a tab showing only what is still open.</summary>
    public DisputeStatus? Status { get; init; }
}

public sealed record GetMyDisputesQuery(GetMyDisputesRequest Request) : IRequest<PagedResult<DisputeDto>>;

public sealed class GetMyDisputesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetMyDisputesQuery, PagedResult<DisputeDto>>
{
    public async Task<PagedResult<DisputeDto>> Handle(
        GetMyDisputesQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var page = request.Request;

        var query = db.Disputes
            .AsNoTracking()
            .Where(d => d.RaisedByUserId == callerId || d.AgainstUserId == callerId);

        if (page.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);

        var ids = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        return PagedResult<DisputeDto>.Create(
            await DisputeQueryProjection.ManyAsync(db, ids, cancellationToken), page.Page, page.PageSize, total);
    }
}

/// <summary>One dispute, for a participant. GET /api/v1/disputes/{id}.</summary>
public sealed record GetDisputeQuery(Guid DisputeId) : IRequest<DisputeDto>;

public sealed class GetDisputeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDisputeQuery, DisputeDto>
{
    public async Task<DisputeDto> Handle(GetDisputeQuery request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var dispute = await db.Disputes
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId, cancellationToken)
            ?? throw new NotFoundException("That dispute does not exist.");

        // 404 rather than 403: whether a case exists is not something a bystander should be
        // able to probe, and "not found" is as true for them as it is for a deleted record.
        if (dispute.RaisedByUserId != callerId && dispute.AgainstUserId != callerId)
        {
            throw new NotFoundException("That dispute does not exist.");
        }

        return await DisputeQueryProjection.OneAsync(db, request.DisputeId, cancellationToken);
    }
}

/// <summary>
/// The admin dispute queue. GET /api/v1/admin/disputes.
///
/// Defaults to the open cases, because a queue that opens on rows nobody can act on is
/// noise. Pass status explicitly to look at closed ones.
/// </summary>
public sealed record GetAdminDisputesRequest : PageRequest
{
    public DisputeStatus? Status { get; init; }

    public string? Search { get; init; }
}

public sealed record GetAdminDisputesQuery(GetAdminDisputesRequest Request)
    : IRequest<PagedResult<DisputeDto>>;

public sealed class GetAdminDisputesQueryHandler(
    IAppDbContext db) : IRequestHandler<GetAdminDisputesQuery, PagedResult<DisputeDto>>
{
    public async Task<PagedResult<DisputeDto>> Handle(
        GetAdminDisputesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Request;

        var query = db.Disputes.AsNoTracking();

        query = page.Status is { } status
            ? query.Where(d => d.Status == status)
            : query.Where(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview);

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var pattern = TextSearchExtensions.ToLikePattern(page.Search);
            const string escape = TextSearchExtensions.LikeEscape;

            // Over the text that exists on the dispute row itself. Names live on the users
            // table, so a name search would need a join; the reason and free text are what
            // an admin actually has to hand when triaging.
            query = query.Where(d =>
                EF.Functions.Like(d.Description.ToLower(), pattern, escape)
                || EF.Functions.Like((d.Resolution ?? string.Empty).ToLower(), pattern, escape));
        }

        var total = await query.CountAsync(cancellationToken);

        var ids = await query
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        return PagedResult<DisputeDto>.Create(
            await DisputeQueryProjection.ManyAsync(db, ids, cancellationToken), page.Page, page.PageSize, total);
    }
}

/// <summary>The support report queue. GET /api/v1/admin/support-reports.</summary>
public sealed record GetAdminSupportReportsQuery(GetAdminSupportReportsRequest Request)
    : IRequest<PagedResult<SupportReportAdminDto>>;

public sealed class GetAdminSupportReportsQueryHandler(
    IAppDbContext db) : IRequestHandler<GetAdminSupportReportsQuery, PagedResult<SupportReportAdminDto>>
{
    public async Task<PagedResult<SupportReportAdminDto>> Handle(
        GetAdminSupportReportsQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Request;

        var query = db.SupportReports.AsNoTracking();

        if (page.IsResolved is { } isResolved)
        {
            query = query.Where(r => r.IsResolved == isResolved);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        var reporters = await db.Users
            .AsNoTracking()
            .Where(u => rows.Select(r => r.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var data = rows
            .Select(r => new SupportReportAdminDto
            {
                Id = r.Id,
                Reason = r.Reason,
                Details = r.Details,
                SubmittedAt = r.CreatedAt,
                ReportedBy = reporters.GetValueOrDefault(r.UserId) ?? string.Empty,
                ReportedByUserId = r.UserId,
                RelatedJobId = r.RelatedJobId,
                IsResolved = r.IsResolved,
                ResolvedAt = r.ResolvedAt
            })
            .ToList();

        return PagedResult<SupportReportAdminDto>.Create(data, page.Page, page.PageSize, total);
    }
}

/// <summary>
/// Fills in the two sides of a dispute for a list of rows.
///
/// Kept in one place so the queue, a participant's list and a single read cannot drift
/// apart. Takes ids rather than entities so the callers' ordering and paging stay theirs.
/// </summary>
internal static class DisputeQueryProjection
{
    public static async Task<DisputeDto> OneAsync(
        IAppDbContext db,
        Guid disputeId,
        CancellationToken cancellationToken)
    {
        var rows = await ManyAsync(db, [disputeId], cancellationToken);

        return rows.Count == 0
            ? throw new NotFoundException("That dispute does not exist.")
            : rows[0];
    }

    public static async Task<List<DisputeDto>> ManyAsync(
        IAppDbContext db,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var byId = await db.Disputes
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var jobIds = byId.Values.Select(d => d.JobId).Distinct().ToList();

        // Gathered in memory rather than as a SelectMany inside the query: a projection
        // that builds a new array of a row's columns is not translatable, and the rows are
        // already loaded here anyway.
        var peopleIds = byId.Values
            .SelectMany(d => new[] { d.RaisedByUserId, d.AgainstUserId, d.ResolvedByUserId ?? Guid.Empty })
            .Distinct()
            .ToList();

        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => (j.Title, j.Company), cancellationToken);

        var people = await db.Users
            .AsNoTracking()
            .Where(u => peopleIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        // Walked in the caller's id order so the DTO list lines up with the page that was
        // asked for, rather than with whatever order the database happened to return.
        return ids
            .Where(byId.ContainsKey)
            .Select(id =>
            {
                var d = byId[id];
                jobs.TryGetValue(d.JobId, out var job);

                return new DisputeDto
                {
                    Id = d.Id,
                    Reason = d.Reason,
                    RaisedBy = people.GetValueOrDefault(d.RaisedByUserId) ?? string.Empty,
                    RaisedByUserId = d.RaisedByUserId,
                    AgainstUser = people.GetValueOrDefault(d.AgainstUserId) ?? string.Empty,
                    AgainstUserId = d.AgainstUserId,
                    JobId = d.JobId,
                    JobTitle = job.Title,
                    Company = job.Company,
                    RaisedAt = d.CreatedAt,
                    Status = d.Status,
                    Amount = d.Amount,
                    Details = d.Description,
                    Notes = d.Resolution ?? string.Empty,
                    ResolvedBy = d.ResolvedByUserId is null
                        ? null
                        : people.GetValueOrDefault(d.ResolvedByUserId.Value),
                    ResolvedAt = d.ResolvedAt
                };
            })
            .ToList();
    }
}
