using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Ratings;

/// <summary>
/// Flutter <c>WorkerRating</c>: id, jobId, workerId, giverId, stars, feedback, createdAt.
/// </summary>
public sealed record WorkerRatingDto
{
    public Guid Id { get; init; }

    public Guid JobId { get; init; }

    public Guid WorkerId { get; init; }

    public Guid GiverId { get; init; }

    public int Stars { get; init; }

    public string Feedback { get; init; } = string.Empty;

    public DateTime? CreatedAt { get; init; }

    public string JobTitle { get; init; } = string.Empty;

    public string FromUserName { get; init; } = string.Empty;
}
