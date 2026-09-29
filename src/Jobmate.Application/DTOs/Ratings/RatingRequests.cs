using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Ratings;

/// <summary>POST /ratings — the Flutter rate-worker screen payload.</summary>
public sealed record CreateRatingRequest
{
    public Guid JobId { get; init; }

    /// <summary>The worker (or giver) being rated.</summary>
    public Guid ToUserId { get; init; }

    public int Stars { get; init; }

    public string? Feedback { get; init; }
}

/// <summary>GET /ratings filters.</summary>
public sealed record GetRatingsRequest : Domain.Common.PageRequest
{
    public Guid? JobId { get; init; }

    public Guid? ToUserId { get; init; }
}
