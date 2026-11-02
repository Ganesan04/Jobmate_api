using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// Rating rules. Ratings are only possible between participants of a completed
/// job, once per participant per job, and never of yourself.
/// </summary>
public static class RatingRules
{
    public const int MinStars = 1;
    public const int MaxStars = 5;

    public const int MaxCommentLength = 1000;

    public static Rating Create(
        Job job,
        User from,
        User to,
        int stars,
        string? comment,
        DateTime now)
    {
        if (job.Status != JobStatus.Completed)
        {
            throw new BusinessRuleException(
                "You can rate only after the job is completed.",
                "JobNotCompleted");
        }

        if (from.Id == to.Id)
        {
            throw new BusinessRuleException("You cannot rate yourself.", "SelfRating");
        }

        if (!IsParticipant(job, from.Id) || !IsParticipant(job, to.Id))
        {
            throw new ForbiddenException("Only participants of this job can rate each other.");
        }

        if (stars is < MinStars or > MaxStars)
        {
            throw new BusinessRuleException(
                $"Rating must be between {MinStars} and {MaxStars}.",
                "RatingOutOfRange");
        }

        if (comment is { Length: > MaxCommentLength })
        {
            throw new BusinessRuleException(
                $"Feedback cannot exceed {MaxCommentLength} characters.",
                "CommentTooLong");
        }

        // From the job, not the account: one person can be a giver on one job and a
        // worker on the next, so only the job says which side of it they were on.
        var direction = from.Id == job.GiverId
            ? RatingDirection.GiverToWorker
            : RatingDirection.WorkerToGiver;

        return new Rating
        {
            JobId = job.Id,
            FromUserId = from.Id,
            ToUserId = to.Id,
            Stars = stars,
            Comment = comment?.Trim() ?? string.Empty,
            RatingDirection = direction,
            WorkerAssignmentId = direction == RatingDirection.GiverToWorker
                ? job.Assignments.FirstOrDefault(a => a.WorkerId == to.Id)?.Id
                : null,
            CreatedAt = now
        };
    }

    /// <summary>A user counts as a participant if they own the job or have a booking on it.</summary>
    public static bool IsParticipant(Job job, Guid userId) =>
        job.GiverId == userId || job.Assignments.Any(a => a.WorkerId == userId);

    /// <summary>Recomputes the denormalised average shown on the Flutter profile.</summary>
    public static (decimal Average, int Count) Recalculate(IEnumerable<Rating> ratingsReceived)
    {
        var list = ratingsReceived as IReadOnlyCollection<Rating> ?? ratingsReceived.ToList();

        if (list.Count == 0)
        {
            return (0m, 0);
        }

        var average = list.Average(r => r.Stars);
        return (Math.Round((decimal)average, 2, MidpointRounding.AwayFromZero), list.Count);
    }
}
