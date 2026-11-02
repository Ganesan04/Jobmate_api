using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Profiles.Queries;

/// <summary>
/// The caller's own profile. GET /api/v1/profile/me.
///
/// Everything on the profile screen is loaded here in one go — the user row, the role
/// profile, the skills, the weekly grid and the uploaded documents — because the screen
/// shows all of it at once and five separate calls would make it flicker.
/// </summary>
public sealed record GetMyProfileQuery : IRequest<MyProfileDto>;

public sealed class GetMyProfileQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetMyProfileQuery, MyProfileDto>
{
    public async Task<MyProfileDto> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        // Tracked rather than no-tracking, and the reason is the role profile: it is a
        // one-to-one dependent whose navigation is fixed up by the fixup query, and the
        // projection below reads the skills and slots off it.
        var user = await db.Users
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.Skills)
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.AvailabilitySlots)
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.Documents)
            .Include(u => u.GiverProfile)
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Profile not found.");

        var worker = user.WorkerProfile;
        var giver = user.GiverProfile;

        return new MyProfileDto
        {
            User = user.ToDto(),
            Worker = worker?.ToDto(),
            Giver = giver?.ToDto(user),
            Documents =
            [
                .. (worker?.Documents ?? [])
                    .OrderBy(d => d.CreatedAt)
                    .Select(d => new WorkerDocumentDto
                    {
                        Id = d.Id,
                        Type = d.Type,
                        FileName = d.FileName,
                        Url = d.Url,
                        Status = d.Status,
                        ReviewedAt = d.ReviewedAt
                    })
            ]
        };
    }
}

/// <summary>
/// Another user's profile. GET /api/v1/profile/{userId}.
///
/// Any signed-in user may read any other profile: the app shows a worker's page from a
/// chat thread and an employer's from an application. The response is the reduced public
/// shape, so no contact details are exposed.
/// </summary>
public sealed record GetPublicProfileQuery(Guid UserId) : IRequest<PublicProfileDto>;

public sealed class GetPublicProfileQueryHandler(
    IAppDbContext db) : IRequestHandler<GetPublicProfileQuery, PublicProfileDto>
{
    public async Task<PublicProfileDto> Handle(GetPublicProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.Skills)
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.AvailabilitySlots)
            .Include(u => u.GiverProfile)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("That user does not exist.");

        // A deactivated account's profile is not readable, even by id. Suspended users
        // keeping a live profile is how a ban gets worked around.
        if (!user.IsActive)
        {
            throw new NotFoundException("That user does not exist.");
        }

        return user.ToPublicDto();
    }
}

/// <summary>
/// The caller's bookmarked jobs. GET /api/v1/worker/saved-jobs.
///
/// Filtered to live jobs by default: a saved list is a shortlist of work still worth
/// applying to, and a job that was cancelled last week is noise on that screen. The
/// bookmark row survives either way, so a cancelled job reopens on the list if it is ever
/// reinstated.
/// </summary>
public sealed record GetSavedJobsQuery(GetSavedJobsRequest Request)
    : IRequest<PagedResult<SavedJobDto>>;

public sealed class GetSavedJobsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetSavedJobsQuery, PagedResult<SavedJobDto>>
{
    public async Task<PagedResult<SavedJobDto>> Handle(
        GetSavedJobsQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var filter = request.Request;

        var source = db.SavedJobs
            .AsNoTracking()
            .Where(s => s.UserId == callerId);

        if (!filter.IncludeClosed)
        {
            source = source.Where(s => s.Job!.Status == JobStatus.Active);
        }

        var total = await source.CountAsync(cancellationToken);

        // Most recently bookmarked first: that is the order the user added them, which is
        // the only ordering that means anything to them.
        var rows = await source
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Include(s => s.Job)!
                .ThenInclude(j => j!.Giver)
            .ToListAsync(cancellationToken);

        // Projected after the read rather than inside the Select: the summary's start and
        // end times are formatted with .NET, which no database provider can translate, so
        // pushing it into the query would fail at runtime.
        var items = rows
            .Where(s => s.Job is not null)
            .Select(s => new SavedJobDto
            {
                Job = s.Job!.ToSummaryDto(),
                SavedAt = s.CreatedAt
            })
            .ToList();

        return PagedResult<SavedJobDto>.Create(items, filter.Page, filter.PageSize, total);
    }
}
