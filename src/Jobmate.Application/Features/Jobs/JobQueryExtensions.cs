using Jobmate.Application.Common;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Jobs;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Jobs;

/// <summary>
/// The filtering, sorting and paging rules shared by every job list endpoint, so the
/// discovery feed, the search screen and the giver's own list cannot drift apart.
///
/// Everything is applied to <see cref="IQueryable{T}"/>, so filters, the applicant
/// count and the sort all run in PostgreSQL: one page costs one round trip no matter
/// how many jobs match.
/// </summary>
public static class JobQueryExtensions
{
    /// <summary>Sort keys the app's <c>JobSort</c> enum and the query string both accept.</summary>
    public static class SortKeys
    {
        /// <summary>Most recently published first. The default.</summary>
        public const string Newest = "newest";

        public const string PayHigh = "payHigh";
        public const string PayLow = "payLow";
        public const string Nearest = "nearest";
        public const string Applicants = "applicants";
        public const string Soonest = "soonest";
    }

    /// <summary>Every accepted sort key, used to validate the query string.</summary>
    public static IReadOnlyList<string> AllSortKeys =>
    [
        SortKeys.Newest, SortKeys.PayHigh, SortKeys.PayLow,
        SortKeys.Nearest, SortKeys.Applicants, SortKeys.Soonest
    ];

    /// <summary>
    /// The escape character for the generated <c>LIKE</c> patterns. It has to be stated
    /// explicitly: PostgreSQL defaults to <c>ESCAPE ''</c>, which leaves "%" and "_" as
    /// live wildcards, so a search for "50%" would otherwise match every row.
    /// </summary>
    private const string LikeEscape = "\\";

    /// <summary>
    /// Free-text search across the fields the Flutter search box matches on: title,
    /// company, area and description.
    /// </summary>
    public static IQueryable<Job> ApplySearch(this IQueryable<Job> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return source;
        }

        var pattern = ToLikePattern(search);

        return source.Where(j =>
            EF.Functions.Like(j.Title.ToLower(), pattern, LikeEscape) ||
            EF.Functions.Like(j.Company.ToLower(), pattern, LikeEscape) ||
            EF.Functions.Like(j.Area.ToLower(), pattern, LikeEscape) ||
            EF.Functions.Like(j.Description.ToLower(), pattern, LikeEscape));
    }

    public static IQueryable<Job> ApplyCategories(this IQueryable<Job> source, IReadOnlyCollection<JobCategory>? categories)
    {
        if (categories is null || categories.Count == 0)
        {
            return source;
        }

        var wanted = categories.Distinct().ToList();

        return source.Where(j => wanted.Contains(j.Category));
    }

    public static IQueryable<Job> ApplyStatuses(this IQueryable<Job> source, IReadOnlyCollection<JobStatus>? statuses)
    {
        if (statuses is null || statuses.Count == 0)
        {
            return source;
        }

        var wanted = statuses.Distinct().ToList();

        return source.Where(j => wanted.Contains(j.Status));
    }

    public static IQueryable<Job> ApplyArea(this IQueryable<Job> source, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return source;
        }

        var pattern = ToLikePattern(area);

        return source.Where(j => EF.Functions.Like(j.Area.ToLower(), pattern, LikeEscape));
    }

    public static IQueryable<Job> ApplyPayRange(this IQueryable<Job> source, int? minPay, int? maxPay)
    {
        if (minPay is not null)
        {
            source = source.Where(j => j.Pay >= minPay.Value);
        }

        return maxPay is not null ? source.Where(j => j.Pay <= maxPay.Value) : source;
    }

    public static IQueryable<Job> ApplyDateRange(this IQueryable<Job> source, DateOnly? from, DateOnly? to)
    {
        if (from is not null)
        {
            source = source.Where(j => j.JobDate >= from.Value);
        }

        return to is not null ? source.Where(j => j.JobDate <= to.Value) : source;
    }

    /// <summary>
    /// Restricts to jobs within <paramref name="radiusKm"/>.
    ///
    /// A bounding box runs first because it is cheap and can use the coordinate indexes;
    /// the exact haversine then removes the corners of that box, which are outside the
    /// circle. Without the second step the result would be a square rather than the
    /// radius the caller asked for.
    /// </summary>
    public static IQueryable<Job> ApplyRadius(
        this IQueryable<Job> source,
        double? latitude,
        double? longitude,
        double? radiusKm)
    {
        if (latitude is null || longitude is null || radiusKm is null or <= 0)
        {
            return source;
        }

        var box = JobDistance.BoundingBox(latitude.Value, longitude.Value, radiusKm.Value);

        var inBox = source.Where(j =>
            j.Latitude != null && j.Longitude != null &&
            j.Latitude >= box.MinLat && j.Latitude <= box.MaxLat &&
            j.Longitude >= box.MinLng && j.Longitude <= box.MaxLng);

        return inBox.Where(JobDistance.WithinRadiusExpression(latitude.Value, longitude.Value, radiusKm.Value));
    }

    public static IQueryable<Job> ApplyJobType(this IQueryable<Job> source, JobType? jobType) =>
        jobType is null ? source : source.Where(j => j.JobType == jobType.Value);

    public static IQueryable<Job> ApplyWorkplaceType(this IQueryable<Job> source, WorkplaceType? workplaceType) =>
        workplaceType is null ? source : source.Where(j => j.WorkplaceType == workplaceType.Value);

    public static IQueryable<Job> ApplyUrgent(this IQueryable<Job> source, bool? urgentOnly) =>
        urgentOnly is true ? source.Where(j => j.IsUrgent) : source;

    public static IQueryable<Job> ApplyRecurring(this IQueryable<Job> source, bool? recurringOnly) =>
        recurringOnly is true ? source.Where(j => j.IsRecurring) : source;

    /// <summary>
    /// Only jobs from verified givers, matching the app's "verified only" filter. The
    /// user flag is the source of truth because it already folds in the per-aspect
    /// verification roll-up.
    /// </summary>
    public static IQueryable<Job> ApplyVerifiedGiver(this IQueryable<Job> source, bool? verifiedOnly) =>
        verifiedOnly is true
            ? source.Where(j =>
                j.Giver != null &&
                j.Giver.IsVerified &&
                j.Giver.VerificationStatus == VerificationStatus.Verified)
            : source;

    /// <summary>
    /// The worker's "available jobs" definition: published, with a free slot, and not
    /// past its application deadline.
    /// </summary>
    public static IQueryable<Job> ApplyStillOpen(this IQueryable<Job> source, DateOnly today) =>
        source.Where(j =>
            j.Status == JobStatus.Active &&
            j.WorkersFilled < j.WorkersNeeded &&
            (j.ApplicationDeadline == null || j.ApplicationDeadline >= today));

    /// <summary>
    /// The canonical form of a sort key, matched case-insensitively because it arrives
    /// from a query string while the app sends its own enum names. An unrecognised key
    /// falls back to the default rather than throwing here, so a stale client degrades to
    /// "newest"; the request validator is what rejects an unknown key outright.
    /// </summary>
    public static string NormalizeSortKey(string? sortBy) =>
        AllSortKeys.FirstOrDefault(key => string.Equals(key, sortBy?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? SortKeys.Newest;

    /// <summary>
    /// Whether a caller-supplied key is one this endpoint understands. An absent key is
    /// valid: the default applies.
    /// </summary>
    public static bool IsKnownSortKey(string? sortBy) =>
        string.IsNullOrWhiteSpace(sortBy) ||
        AllSortKeys.Any(key => string.Equals(key, sortBy.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sorts by one of the documented keys and appends the id as a tie-breaker, so
    /// paging can neither repeat nor skip rows when several jobs share a sort value.
    /// </summary>
    public static IQueryable<Job> ApplySort(
        this IQueryable<Job> source,
        string? sortBy,
        bool descending,
        double? latitude,
        double? longitude) =>
        (NormalizeSortKey(sortBy)) switch
        {
            SortKeys.PayHigh => TieBreak(source.OrderByDescending(j => j.Pay)),
            SortKeys.PayLow => TieBreak(source.OrderBy(j => j.Pay)),

            // Nearest is meaningless without coordinates, so it falls back to newest
            // rather than silently sorting on distance from null.
            SortKeys.Nearest when latitude is not null && longitude is not null =>
                TieBreak(source.OrderBy(JobDistance.ExpressionFrom(latitude.Value, longitude.Value))),

            SortKeys.Applicants => descending
                ? TieBreak(source.OrderByDescending(j => j.Applications.Count()))
                : TieBreak(source.OrderBy(j => j.Applications.Count())),

            SortKeys.Soonest => descending
                ? TieBreak(source.OrderByDescending(j => j.JobDate))
                : TieBreak(source.OrderBy(j => j.JobDate)),

            _ => TieBreak(source.OrderByDescending(j => j.PublishedAt ?? j.CreatedAt))
        };

    /// <summary>
    /// The statuses a job may be discovered by. A draft or a pending posting has not been
    /// published, and a cancelled, expired or completed one is closed: none of them
    /// belongs in a search result, however the caller filters. A giver sees the full
    /// history of their own jobs in the giver feed instead.
    /// </summary>
    public static IQueryable<Job> ApplyDiscoverable(this IQueryable<Job> source) =>
        source.Where(j => JobStatusTransitions.Discoverable.Contains(j.Status));

    /// <summary>
    /// Projects one page of jobs with the applicant count attached, so mapping never
    /// triggers a per-row query, and withholds a hidden address from everyone who is
    /// not allowed to see it.
    /// </summary>
    public static async Task<PagedResult<JobDto>> ToPagedResultAsync(
        this IQueryable<Job> source,
        PageRequest page,
        double? latitude,
        double? longitude,
        IReadOnlyCollection<Guid>? jobsWhoseLocationIsVisibleToViewer,
        CancellationToken cancellationToken)
    {
        var total = await source.CountAsync(cancellationToken);

        var pageItems = await source
            .Skip(page.Skip)
            .Take(page.Take)
            .Include(j => j.Giver)
            .Include(j => j.Skills)
            .Include(j => j.WeekdayRows)
            .ToListAsync(cancellationToken);

        if (pageItems.Count == 0)
        {
            return PagedResult<JobDto>.Create([], page.Page, page.PageSize, total);
        }

        // One grouped count for the whole page, not one query per job.
        var jobIds = pageItems.Select(j => j.Id).ToList();

        var counts = await source
            .Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, Count = j.Applications.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        var locationVisibleTo = jobsWhoseLocationIsVisibleToViewer ?? [];

        var items = pageItems.Select(job =>
        {
            var dto = job.ToDto(
                counts.GetValueOrDefault(job.Id),
                latitude,
                longitude,
                job.Skills.Select(s => s.Name).ToList());

            // A hidden address is withheld from the feed too, not just from the
            // detail screen.
            return job.LocationVisible || locationVisibleTo.Contains(job.Id)
                ? dto
                : dto with { Address = string.Empty, Latitude = null, Longitude = null };
        }).ToList();

        return PagedResult<JobDto>.Create(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Single-job read with the same privacy rule as the list, so a hidden address
    /// cannot be bypassed by asking for the job directly.
    /// </summary>
    public static async Task<JobDto?> ToSingleResultAsync(
        this IQueryable<Job> source,
        double? latitude,
        double? longitude,
        bool hideAddress,
        CancellationToken cancellationToken)
    {
        var job = await source
            .Include(j => j.Giver)
            .Include(j => j.Skills)
            .Include(j => j.WeekdayRows)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return null;
        }

        var applicantCount = await source
            .Where(j => j.Id == job.Id)
            .Select(j => j.Applications.Count())
            .FirstAsync(cancellationToken);

        var dto = job.ToDto(applicantCount, latitude, longitude, job.Skills.Select(s => s.Name).ToList());

        return hideAddress ? dto with { Address = string.Empty, Latitude = null, Longitude = null } : dto;
    }

    private static IQueryable<Job> TieBreak(IOrderedQueryable<Job> ordered) => ordered.ThenBy(j => j.Id);

    /// <summary>
    /// The jobs in <paramref name="source"/> whose address is hidden but which the
    /// caller may still see, because they are already booked on them. Resolved once for
    /// a whole page instead of once per row.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>?> VisibleHiddenLocationJobIdsAsync(
        this IAppDbContext db,
        IQueryable<Job> source,
        Guid? viewerId,
        CancellationToken cancellationToken)
    {
        if (viewerId is not { } workerId)
        {
            return null;
        }

        var jobIds = source.Select(j => j.Id);

        return await db.WorkerAssignments
            .AsNoTracking()
            .Where(a => a.WorkerId == workerId && jobIds.Contains(a.JobId))
            .Select(a => a.JobId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a case-insensitive <c>LIKE</c> pattern. Both sides are lower-cased in SQL
    /// rather than using a PostgreSQL-only function, so the Application layer stays free
    /// of a vendor dependency; LIKE metacharacters in the input are escaped so a search
    /// for "50%" is a literal search.
    /// </summary>
    private static string ToLikePattern(string value)
    {
        var escaped = value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped.ToLowerInvariant()}%";
    }
}

/// <summary>Shared coordinate checks for the job endpoints.</summary>
public static class JobCoordinates
{
    /// <summary>
    /// Validates a coordinate pair. The request validators already cover a malformed
    /// latitude, and this is the second gate that keeps a hand-crafted query string
    /// from reaching the distance maths.
    /// </summary>
    public static (double Latitude, double Longitude) Require(double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null)
        {
            throw new BusinessRuleException(
                "Both latitude and longitude are required for this filter.", "CoordinatesRequired");
        }

        if (!GeoDistance.IsValidLatitude(latitude.Value) || !GeoDistance.IsValidLongitude(longitude.Value))
        {
            throw new BusinessRuleException(
                "Latitude must be between -90 and 90 and longitude between -180 and 180.", "CoordinatesInvalid");
        }

        return (latitude.Value, longitude.Value);
    }
}
