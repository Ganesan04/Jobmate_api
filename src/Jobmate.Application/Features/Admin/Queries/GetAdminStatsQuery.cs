using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Admin;
using Jobmate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Admin.Queries;

/// <summary>
/// The figures behind the admin home screen. Every value is a database aggregate
/// rather than a count over loaded rows, so the endpoint stays fast as the tables grow.
/// </summary>
public sealed record GetAdminStatsQuery : IRequest<AdminStatsDto>;

public sealed class GetAdminStatsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAdminStatsQuery, AdminStatsDto>
{
    public async Task<AdminStatsDto> Handle(GetAdminStatsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalUsers = await db.Users.CountAsync(cancellationToken);
        var workers = await db.Users.CountAsync(u => u.Role == UserRole.Worker, cancellationToken);
        var givers = await db.Users.CountAsync(u => u.Role == UserRole.WorkGiver, cancellationToken);
        var newUsersThisWeek = await db.Users.CountAsync(u => u.CreatedAt >= weekAgo, cancellationToken);

        var totalJobs = await db.Jobs.CountAsync(cancellationToken);
        var activeJobs = await db.Jobs.CountAsync(j => j.Status == JobStatus.Active, cancellationToken);
        var completedJobs = await db.Jobs.CountAsync(j => j.Status == JobStatus.Completed, cancellationToken);
        var applications = await db.JobApplications.CountAsync(cancellationToken);

        // Settled volume and the current month's payout total come from the same
        // filter in the same round trip.
        var settled = await db.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                ThisMonth = g.Where(p => p.PaidAt >= monthStart).Sum(p => (decimal?)p.TotalAmount) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        var verificationRequests = await db.VerificationRequests
            .CountAsync(v => v.Status == VerificationRequestStatus.Pending, cancellationToken);

        var openDisputes = await db.Disputes.CountAsync(d => d.Status == DisputeStatus.Open, cancellationToken);
        var underReview = await db.Disputes.CountAsync(d => d.Status == DisputeStatus.UnderReview, cancellationToken);

        return new AdminStatsDto
        {
            TotalUsers = totalUsers,
            Workers = workers,
            WorkGivers = givers,
            NewUsersThisWeek = newUsersThisWeek,
            TotalJobs = totalJobs,
            ActiveJobs = activeJobs,
            CompletedJobs = completedJobs,
            Applications = applications,
            Payments = settled?.Count ?? 0,
            TotalPayoutThisMonth = settled?.ThisMonth ?? 0m,
            VerificationRequests = verificationRequests,
            OpenDisputes = openDisputes,
            OpenDisputesUnderReview = underReview
        };
    }
}
