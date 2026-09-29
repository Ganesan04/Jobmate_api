using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Applications;

/// <summary>
/// Filtering, sorting and paging for the two application lists, kept apart from the
/// handlers so both sides of the flow share one definition of "sorted by rating".
///
/// Every filter and sort runs in SQL. The one exception is the distance shown on an
/// applicant card, which is measured per page row in memory: a page is at most a hundred
/// applicants, and the sort keys the app offers (newest, rating, expected pay) are all
/// indexable columns, so there is nothing to gain from a distance expression here.
/// </summary>
public static class ApplicationQueryExtensions
{
    /// <summary>
    /// The escape character for generated LIKE patterns. Stated explicitly because
    /// PostgreSQL otherwise applies <c>ESCAPE ''</c>, which leaves "%" and "_" as live
    /// wildcards and makes a search for "50%" match every applicant.
    /// </summary>
    private const string LikeEscape = "\\";

    /// <summary>Documented sort keys for the giver's applicant list.</summary>
    public static class ApplicantSortKeys
    {
        public const string Newest = "newest";
        public const string Oldest = "oldest";
        public const string RatingHigh = "ratingHigh";
        public const string RatingLow = "ratingLow";
        public const string ExpectedPayHigh = "expectedPayHigh";
        public const string ExpectedPayLow = "expectedPayLow";
        public const string Name = "name";

        public static readonly string[] All =
        [
            Newest,
            Oldest,
            RatingHigh,
            RatingLow,
            ExpectedPayHigh,
            ExpectedPayLow,
            Name
        ];
    }

    /// <summary>
    /// The canonical form of a sort key. An unrecognised key falls back to the default
    /// here so a stale client degrades to "newest"; the request validator is what
    /// rejects an unknown key outright.
    /// </summary>
    public static string NormalizeApplicantSortKey(string? sortBy) =>
        ApplicantSortKeys.All.FirstOrDefault(key => string.Equals(key, sortBy?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? ApplicantSortKeys.Newest;

    /// <summary>Whether the caller-supplied key is one this endpoint understands.</summary>
    public static bool IsKnownApplicantSortKey(string? sortBy) =>
        string.IsNullOrWhiteSpace(sortBy) ||
        ApplicantSortKeys.All.Any(key => string.Equals(key, sortBy.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The giver's applicant list: status filter, free text over the worker's name,
    /// phone and area, and a minimum average rating. The search text is treated as
    /// literal text, so a name containing "%" cannot widen the query.
    /// </summary>
    public static IQueryable<JobApplication> ApplyApplicantFilters(
        this IQueryable<JobApplication> source,
        GetApplicationsRequest request)
    {
        if (request.Statuses is { Count: > 0 } statuses)
        {
            source = source.Where(a => statuses.Contains(a.Status));
        }

        if (request.MinRating is { } minRating)
        {
            // Registration always creates the profile, so the required navigation does
            // not silently drop a worker who has not filled one in.
            source = source.Where(a => a.Worker!.WorkerProfile!.AverageRating >= minRating);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = ToLikePattern(request.Search);

            source = source.Where(a =>
                EF.Functions.Like(a.Worker!.FirstName.ToLower(), pattern, LikeEscape) ||
                EF.Functions.Like(a.Worker!.LastName.ToLower(), pattern, LikeEscape) ||
                EF.Functions.Like(a.Worker!.Area.ToLower(), pattern, LikeEscape) ||
                EF.Functions.Like(a.Worker!.PhoneNumber, pattern, LikeEscape));
        }

        return source;
    }

    /// <summary>
    /// Sorts applicants and appends the id as a tie-breaker, so paging can neither
    /// repeat nor skip a row when several applicants share a sort value.
    /// </summary>
    public static IQueryable<JobApplication> ApplyApplicantSort(
        this IQueryable<JobApplication> source,
        string? sortBy) =>
        NormalizeApplicantSortKey(sortBy) switch
        {
            ApplicantSortKeys.Oldest => TieBreak(source.OrderBy(a => a.AppliedAt)),
            ApplicantSortKeys.RatingHigh => TieBreak(source.OrderByDescending(a => a.Worker!.WorkerProfile!.AverageRating)),
            ApplicantSortKeys.RatingLow => TieBreak(source.OrderBy(a => a.Worker!.WorkerProfile!.AverageRating)),
            ApplicantSortKeys.ExpectedPayHigh => TieBreak(source.OrderByDescending(a => a.ExpectedPay)),
            ApplicantSortKeys.ExpectedPayLow => TieBreak(source.OrderBy(a => a.ExpectedPay)),
            ApplicantSortKeys.Name => TieBreak(source.OrderBy(a => a.Worker!.FirstName).ThenBy(a => a.Worker!.LastName)),
            _ => TieBreak(source.OrderByDescending(a => a.AppliedAt))
        };

    /// <summary>Counts and pages one list, returning the items with the paging metadata.</summary>
    public static async Task<PagedResult<JobApplication>> ToApplicationPageAsync(
        this IQueryable<JobApplication> source,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var total = await source.CountAsync(cancellationToken);

        var items = await source
            .Skip(page.Skip)
            .Take(page.Take)
            .ToListAsync(cancellationToken);

        return PagedResult<JobApplication>.Create(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Distance from the job being reviewed to the applicant, or 0 km when either side
    /// has no coordinates. Matches what the Flutter applicant card shows.
    /// </summary>
    public static double ApplicantDistanceKm(Job job, WorkerProfile? workerProfile)
    {
        if (job.Latitude is not { } jobLatitude || job.Longitude is not { } jobLongitude ||
            workerProfile?.Latitude is not { } workerLatitude || workerProfile.Longitude is not { } workerLongitude)
        {
            return 0;
        }

        return GeoDistance.Between(jobLatitude, jobLongitude, workerLatitude, workerLongitude);
    }

    private static IOrderedQueryable<JobApplication> TieBreak(IOrderedQueryable<JobApplication> ordered) =>
        ordered.ThenBy(a => a.Id);

    /// <summary>Wraps literal text in wildcards, escaping the LIKE metacharacters.</summary>
    private static string ToLikePattern(string value)
    {
        var escaped = value
            .Trim()
            .ToLower()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
