using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Payments;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Payments.Queries;

/// <summary>
/// The caller's own payments. GET /api/v1/payments.
///
/// One route serves both sides because the app shows the same list shape to each: in
/// giver mode the caller sees what they paid out, in worker mode what they received.
/// The scope is taken from the caller's active role, never from the request, so no
/// filter can widen it.
/// </summary>
public sealed record GetPaymentsQuery(GetPaymentsRequest Request) : IRequest<PagedResult<PaymentReceiptDto>>;

public sealed class GetPaymentsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetPaymentsQuery, PagedResult<PaymentReceiptDto>>
{
    public async Task<PagedResult<PaymentReceiptDto>> Handle(
        GetPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var caller = currentUser.Role ?? throw new UnauthorizedException("Sign in to see payments.");

        var source = db.Payments.AsNoTracking().AsQueryable();

        switch (caller)
        {
            case UserRole.WorkGiver:
                source = source.Where(p => p.GiverId == currentUser.UserId);
                break;
            case UserRole.Worker:
                source = source.Where(p => p.WorkerId == currentUser.UserId);
                break;
            default:
                throw new ForbiddenException("Payments are not available to this account.");
        }

        var filter = request.Request;

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            source = source.Where(p => statuses.Contains(p.Status));
        }

        if (filter.JobId is { } jobId)
        {
            source = source.Where(p => p.JobId == jobId);
        }

        if (filter.WorkerId is { } workerId)
        {
            source = source.Where(p => p.WorkerId == workerId);
        }

        if (filter.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            source = source.Where(p => (p.PaidAt ?? p.CreatedAt) >= start);
        }

        if (filter.To is { } to)
        {
            // Inclusive of the whole day the caller asked for, so a single-day range
            // does not silently drop payments made later that evening.
            var end = to.ToDateTime(TimeOnly.MaxValue);
            source = source.Where(p => (p.PaidAt ?? p.CreatedAt) <= end);
        }

        var total = await source.CountAsync(cancellationToken);

        var page = await source
            .Include(p => p.Job)
            .Include(p => p.Worker)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        return PagedResult<PaymentReceiptDto>.Create(
            page.Select(p => p.ToReceiptDto()).ToList(),
            filter.Page,
            filter.PageSize,
            total);
    }
}

/// <summary>
/// One wallet line per booking for the signed-in worker.
/// GET /api/v1/worker/earnings.
///
/// Cancelled bookings are excluded: no work happened, so there is nothing to show the
/// worker and nothing to pay.
/// </summary>
public sealed record GetEarningsQuery(WorkerEarningsRequest Request) : IRequest<PagedResult<EarningDto>>;

public sealed class GetEarningsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetEarningsQuery, PagedResult<EarningDto>>
{
    public async Task<PagedResult<EarningDto>> Handle(GetEarningsQuery request, CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var source = db.WorkerAssignments
            .AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.Status != AssignmentStatus.Cancelled);

        var filter = request.Request;

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            source = source.Where(a => a.Payment != null && statuses.Contains(a.Payment.Status));
        }

        if (filter.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            source = source.Where(a => a.ConfirmedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = to.ToDateTime(TimeOnly.MaxValue);
            source = source.Where(a => a.ConfirmedAt <= end);
        }

        var total = await source.CountAsync(cancellationToken);

        var page = await source
            .Include(a => a.Job)
            .Include(a => a.Payment)
            .OrderByDescending(a => a.ConfirmedAt)
            .ThenByDescending(a => a.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        return PagedResult<EarningDto>.Create(
            page.Select(a => a.ToEarningDto()).ToList(),
            filter.Page,
            filter.PageSize,
            total);
    }
}

/// <summary>
/// Month-grouped wallet totals. GET /api/v1/worker/earnings/summary.
///
/// Settled and pending money are kept apart because they are not the same promise:
/// only a paid booking is money the worker can spend.
/// </summary>
public sealed record GetEarningSummaryQuery : IRequest<List<EarningSummaryDto>>;

public sealed class GetEarningSummaryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetEarningSummaryQuery, List<EarningSummaryDto>>
{
    public async Task<List<EarningSummaryDto>> Handle(
        GetEarningSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var rows = await db.WorkerAssignments
            .AsNoTracking()
            .Include(a => a.Payment)
            .Where(a => a.WorkerId == workerId && a.Status != AssignmentStatus.Cancelled)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(a => PaymentMappings.MonthKey(a.Payment?.PaidAt ?? a.ConfirmedAt))
            .OrderByDescending(g => g.Key)
            .Select(group =>
            {
                var items = group.ToList();

                return new EarningSummaryDto
                {
                    Month = group.Key,
                    Label = PaymentMappings.MonthLabel(group.Key),
                    Earned = items.Where(a => a.PaymentStatus == PaymentStatus.Paid).Sum(a => a.ToEarningDto().Amount),
                    Pending = items
                        .Where(a => a.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Processing)
                        .Sum(a => a.ToEarningDto().Amount)
                };
            })
            .ToList();
    }
}

/// <summary>Wallet totals for the earnings screen header. GET /api/v1/worker/wallet.</summary>
public sealed record GetWalletSummaryQuery : IRequest<WalletSummaryDto>;

public sealed class GetWalletSummaryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetWalletSummaryQuery, WalletSummaryDto>
{
    public async Task<WalletSummaryDto> Handle(
        GetWalletSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));

        var rows = await db.WorkerAssignments
            .AsNoTracking()
            .Include(a => a.Payment)
            .Where(a => a.WorkerId == workerId && a.Status != AssignmentStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var lines = rows.Select(a => a.ToEarningDto()).ToList();

        return new WalletSummaryDto
        {
            TotalEarned = lines.Where(l => l.Status == PaymentStatus.Paid).Sum(l => l.Amount),
            PendingAmount = lines
                .Where(l => l.Status is PaymentStatus.Pending or PaymentStatus.Processing)
                .Sum(l => l.Amount),
            EntryCount = lines.Count
        };
    }
}
