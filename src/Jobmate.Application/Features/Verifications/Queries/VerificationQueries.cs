using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Verifications;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Verifications.Queries;

/// <summary>
/// The caller's trust badge and verification history. GET /api/v1/verifications.
///
/// The badge and the list of attempts are answered together on purpose. They are two
/// projections of the same rows, and a user comparing "the badge says pending" against
/// "my last attempt was rejected" is exactly the user who needs to see both at once.
/// </summary>
public sealed record GetMyVerificationsQuery(GetMyVerificationsRequest Request)
    : IRequest<MyVerificationsDto>;

public sealed class GetMyVerificationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetMyVerificationsQuery, MyVerificationsDto>
{
    public async Task<MyVerificationsDto> Handle(
        GetMyVerificationsQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var filter = request.Request;

        // Every request, not just the visible page: the badge is computed from the latest
        // request per type, and paging its input would make the badge depend on which page
        // happened to be asked for.
        var all = await db.VerificationRequests
            .AsNoTracking()
            .Where(r => r.UserId == callerId)
            .ToListAsync(cancellationToken);

        var rollup = VerificationSummary.Build(all);

        var paged = all
            .Where(r => filter.Type is null || r.Type == filter.Type)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToList();

        return new MyVerificationsDto
        {
            Summary = rollup.ToDto(),

            // Each row carries the badge state for its own type, so a checklist row and
            // the badge above it can never disagree on screen.
            Requests =
            [
                .. paged.Select(r => r.ToDto(rollup.StatusOf(r.Type)))
            ]
        };
    }
}

/// <summary>
/// The trust badge on its own. GET /api/v1/verifications/summary.
///
/// Separate from the history because the app shell shows the badge on nearly every screen
/// while the history is only opened on purpose. It is one object, so it is also the
/// endpoint worth caching in a header.
/// </summary>
public sealed record GetVerificationSummaryQuery : IRequest<VerificationSummaryDto>;

public sealed class GetVerificationSummaryQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetVerificationSummaryQuery, VerificationSummaryDto>
{
    public async Task<VerificationSummaryDto> Handle(
        GetVerificationSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var requests = await db.VerificationRequests
            .AsNoTracking()
            .Where(r => r.UserId == callerId)
            .ToListAsync(cancellationToken);

        return VerificationSummary.Build(requests).ToDto();
    }
}
