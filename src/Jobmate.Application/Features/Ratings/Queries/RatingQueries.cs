using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Ratings;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Ratings.Queries;

/// <summary>
/// Feedback a person has received. GET /api/v1/ratings.
///
/// Ratings are public — they are what makes a profile trustworthy — so this can be asked
/// for another user by passing <c>toUserId</c>. With no filter it answers "what has been
/// said about me", which is what the caller's own profile screen shows.
/// </summary>
public sealed record GetRatingsQuery(GetRatingsRequest Request) : IRequest<PagedResult<WorkerRatingDto>>;

public sealed class GetRatingsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetRatingsQuery, PagedResult<WorkerRatingDto>>
{
    public async Task<PagedResult<WorkerRatingDto>> Handle(
        GetRatingsQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var subjectId = request.Request.ToUserId ?? callerId;

        if (subjectId == Guid.Empty)
        {
            throw new NotFoundException("That account no longer exists.");
        }

        var source = db.Ratings.AsNoTracking().Where(r => r.ToUserId == subjectId);

        if (request.Request.JobId is { } jobId)
        {
            source = source.Where(r => r.JobId == jobId);
        }

        var total = await source.CountAsync(cancellationToken);

        var page = await source
            .Include(r => r.Job)
            .Include(r => r.FromUser)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(request.Request.Skip)
            .Take(request.Request.Take)
            .ToListAsync(cancellationToken);

        var items = page.Select(r => r.ToDto(r.Job?.Title ?? string.Empty, r.FromUser?.FullName ?? string.Empty)).ToList();

        return PagedResult<WorkerRatingDto>.Create(items, request.Request.Page, request.Request.PageSize, total);
    }
}

/// <summary>
/// Finished work for the signed-in worker. GET /api/v1/worker/work-history.
///
/// A booking appears as soon as it is made, so the history reflects work agreed rather
/// than only work paid, and the payment status on each line is what tells the two apart.
/// </summary>
public sealed record GetWorkHistoryQuery(WorkHistoryRequest Request) : IRequest<PagedResult<WorkHistoryEntryDto>>;

public sealed class GetWorkHistoryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetWorkHistoryQuery, PagedResult<WorkHistoryEntryDto>>
{
    public async Task<PagedResult<WorkHistoryEntryDto>> Handle(
        GetWorkHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var source = db.WorkerAssignments
            .AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.Status != AssignmentStatus.Cancelled);

        var entries = await WorkHistoryBuilder.BuildAsync(
            db, source, request.Request, workerId, WorkHistorySide.Worker, cancellationToken);

        return PagedResult<WorkHistoryEntryDto>.Create(
            entries,
            request.Request.Page,
            request.Request.PageSize,
            await WorkHistoryBuilder.CountAsync(source, request.Request, cancellationToken));
    }
}

/// <summary>
/// Finished work for the signed-in employer. GET /api/v1/giver/work-history.
///
/// The same shape as the worker's view, with the roles swapped: the counterpart on each
/// line is the worker, and <c>earnings</c> is what that worker was paid for the shift.
/// </summary>
public sealed record GetGiverWorkHistoryQuery(WorkHistoryRequest Request) : IRequest<PagedResult<WorkHistoryEntryDto>>;

public sealed class GetGiverWorkHistoryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetGiverWorkHistoryQuery, PagedResult<WorkHistoryEntryDto>>
{
    public async Task<PagedResult<WorkHistoryEntryDto>> Handle(
        GetGiverWorkHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));

        var source = db.WorkerAssignments
            .AsNoTracking()
            .Where(a => a.GiverId == giverId && a.Status != AssignmentStatus.Cancelled);

        var entries = await WorkHistoryBuilder.BuildAsync(
            db, source, request.Request, giverId, WorkHistorySide.Giver, cancellationToken);

        return PagedResult<WorkHistoryEntryDto>.Create(
            entries,
            request.Request.Page,
            request.Request.PageSize,
            await WorkHistoryBuilder.CountAsync(source, request.Request, cancellationToken));
    }
}

/// <summary>
/// Which side of a booking a work-history line is being read from. It decides who the
/// counterpart on the line is: the employer sees the worker, the worker sees the employer.
/// </summary>
internal enum WorkHistorySide
{
    Worker,
    Giver
}

/// <summary>
/// Shared paging and projection for both work-history views.
///
/// The filters, the counterparty and the "did they rate me" lookup are identical for the
/// two roles, so they live here rather than being written twice and drifting apart.
/// </summary>
internal static class WorkHistoryBuilder
{
    /// <param name="subjectId">
    /// Whose ratings are wanted on each line: the worker in their own history, the
    /// employer in theirs. In both cases it is the person the line is about, so the
    /// lookup is the same query.
    /// </param>
    public static async Task<List<WorkHistoryEntryDto>> BuildAsync(
        IAppDbContext db,
        IQueryable<WorkerAssignment> source,
        WorkHistoryRequest request,
        Guid subjectId,
        WorkHistorySide side,
        CancellationToken cancellationToken)
    {
        var page = await source
            .ApplyFilters(request)
            .Include(a => a.Job)
            .ThenInclude(j => j!.Giver)
            .Include(a => a.Worker)
            .Include(a => a.Payment)
            .OrderByDescending(a => a.ConfirmedAt)
            .ThenByDescending(a => a.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return [];
        }

        var jobIds = page.Select(a => a.JobId).Distinct().ToList();

        // One query for every rating on this page rather than one per line: a history of
        // twenty shifts should not become twenty round trips.
        var ratings = await db.Ratings
            .AsNoTracking()
            .Where(r => r.ToUserId == subjectId && jobIds.Contains(r.JobId))
            .ToListAsync(cancellationToken);

        return page
            .Select(a =>
            {
                var rating = ratings.FirstOrDefault(r => r.JobId == a.JobId);

                // In the employer's history the counterpart is the worker; in the worker's
                // it is the employer. The flag is only true when the employer left the
                // rating, which in the employer's own history it never is.
                var counterpartyIsWorker = side == WorkHistorySide.Giver;

                return a.ToWorkHistoryDto(
                    counterpartyIsWorker ? a.WorkerId : a.GiverId,
                    counterpartyIsWorker
                        ? a.Worker?.FullName ?? string.Empty
                        : a.Job?.Giver?.FullName ?? string.Empty,
                    rating?.Stars,
                    ratedByGiver: !counterpartyIsWorker && rating is not null);
            })
            .ToList();
    }

    public static Task<int> CountAsync(
        IQueryable<WorkerAssignment> source,
        WorkHistoryRequest request,
        CancellationToken cancellationToken) =>
        source.ApplyFilters(request).CountAsync(cancellationToken);

    private static IQueryable<WorkerAssignment> ApplyFilters(
        this IQueryable<WorkerAssignment> source,
        WorkHistoryRequest request)
    {
        if (request.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            source = source.Where(a => a.ConfirmedAt >= start);
        }

        if (request.To is { } to)
        {
            var end = to.ToDateTime(TimeOnly.MaxValue);
            source = source.Where(a => a.ConfirmedAt <= end);
        }

        if (request.JobId is { } jobId)
        {
            source = source.Where(a => a.JobId == jobId);
        }

        if (request.PaymentStatus is { } status)
        {
            // The payment status is a projection over the linked payment, so the filter
            // has to be expressed against the payment row itself.
            source = source.Where(a => a.Payment != null && a.Payment.Status == status);
        }

        return source;
    }
}
