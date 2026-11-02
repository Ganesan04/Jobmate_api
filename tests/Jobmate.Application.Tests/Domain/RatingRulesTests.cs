using FluentAssertions;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Tests.Domain;

/// <summary>
/// Ratings, and the average shown on a profile.
///
/// Ratings only exist between people who actually did a job together, and the average has
/// to agree with the rows behind it — the two are stored separately, so the recalculation
/// is the only thing keeping them honest.
/// </summary>
public sealed class RatingRulesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private static User Worker(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ActiveRole = UserRole.Worker,
        FirstName = "Deepak",
        LastName = "Kumar"
    };

    private static User Giver(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ActiveRole = UserRole.WorkGiver,
        FirstName = "Anita",
        LastName = "Sharma"
    };

    private static Job CompletedJob(Guid giverId, Guid? workerId = null, WorkerAssignment? assignment = null)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            GiverId = giverId,
            Status = JobStatus.Completed
        };

        if (assignment is not null || workerId is not null)
        {
            job.Assignments.Add(assignment ?? new WorkerAssignment
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                WorkerId = workerId!.Value,
                GiverId = giverId,
                Status = AssignmentStatus.Completed
            });
        }

        return job;
    }

    private static Rating Stars(int value) => new()
    {
        Id = Guid.NewGuid(),
        JobId = Guid.NewGuid(),
        FromUserId = Guid.NewGuid(),
        ToUserId = Guid.NewGuid(),
        Stars = value,
        CreatedAt = Now
    };

    [Fact]
    public void A_giver_rating_a_worker_is_recorded_in_that_direction()
    {
        var giver = Giver();
        var worker = Worker();
        var assignment = new WorkerAssignment
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            WorkerId = worker.Id,
            GiverId = giver.Id,
            Status = AssignmentStatus.Completed
        };

        var job = CompletedJob(giver.Id, assignment: assignment);

        var rating = RatingRules.Create(job, giver, worker, 5, "Excellent work", Now);

        rating.RatingDirection.Should().Be(RatingDirection.GiverToWorker);
        rating.FromUserId.Should().Be(giver.Id);
        rating.ToUserId.Should().Be(worker.Id);
        rating.WorkerAssignmentId.Should().Be(assignment.Id);
        rating.Stars.Should().Be(5);
        rating.Comment.Should().Be("Excellent work");
    }

    [Fact]
    public void A_worker_rating_a_giver_is_recorded_in_that_direction_and_not_against_a_booking()
    {
        // A worker-to-giver rating is about the person, not about one shift, so it carries
        // no assignment.
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        var rating = RatingRules.Create(job, worker, giver, 4, "Paid on time", Now);

        rating.RatingDirection.Should().Be(RatingDirection.WorkerToGiver);
        rating.WorkerAssignmentId.Should().BeNull();
    }

    [Fact]
    public void The_direction_comes_from_the_job_not_the_mode_the_rater_is_in()
    {
        // One account works and hires. The giver here happens to be in worker mode and the
        // worker in giver mode; the job still says who posted it and who was booked.
        var giver = Worker();
        var worker = Giver();
        var job = CompletedJob(giver.Id, worker.Id);

        RatingRules.Create(job, giver, worker, 5, null, Now)
            .RatingDirection.Should().Be(RatingDirection.GiverToWorker);

        RatingRules.Create(job, worker, giver, 5, null, Now)
            .RatingDirection.Should().Be(RatingDirection.WorkerToGiver);
    }

    [Fact]
    public void A_job_has_to_be_finished_before_it_can_be_rated()
    {
        var giver = Giver();
        var worker = Worker();

        var running = CompletedJob(giver.Id, worker.Id);
        running.Status = JobStatus.Active;

        var act = () => RatingRules.Create(running, giver, worker, 5, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("JobNotCompleted");
    }

    [Fact]
    public void Nobody_rates_themselves()
    {
        var person = Giver();
        var job = CompletedJob(person.Id, person.Id);

        var act = () => RatingRules.Create(job, person, person, 5, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SelfRating");
    }

    [Fact]
    public void Somebody_with_no_part_in_the_job_cannot_rate_the_worker()
    {
        var worker = Worker();
        var job = CompletedJob(Guid.NewGuid(), worker.Id);
        var stranger = Worker();

        var act = () => RatingRules.Create(job, stranger, worker, 1, null, Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Somebody_with_no_part_in_the_job_cannot_be_rated()
    {
        // Otherwise anybody could inflate a stranger's average by rating them a five.
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);
        var stranger = Worker();

        var act = () => RatingRules.Create(job, giver, stranger, 5, null, Now);

        act.Should().Throw<ForbiddenException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void Stars_outside_one_to_five_are_refused(int stars)
    {
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        var act = () => RatingRules.Create(job, giver, worker, stars, null, Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("RatingOutOfRange");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Any_star_count_in_range_is_accepted(int stars)
    {
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        RatingRules.Create(job, giver, worker, stars, null, Now).Stars.Should().Be(stars);
    }

    [Fact]
    public void A_comment_at_the_limit_is_accepted_and_one_over_is_not()
    {
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        RatingRules.Create(job, giver, worker, 5, new string('x', RatingRules.MaxCommentLength), Now)
            .Should().NotBeNull();

        var act = () => RatingRules.Create(
            job, giver, worker, 5, new string('x', RatingRules.MaxCommentLength + 1), Now);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("CommentTooLong");
    }

    [Fact]
    public void An_absent_comment_is_stored_as_an_empty_one()
    {
        // The column is not nullable, and storing null would need a decision on every read.
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        RatingRules.Create(job, giver, worker, 4, null, Now).Comment.Should().BeEmpty();
    }

    [Fact]
    public void A_comment_is_trimmed()
    {
        var giver = Giver();
        var worker = Worker();
        var job = CompletedJob(giver.Id, worker.Id);

        RatingRules.Create(job, giver, worker, 4, "  Good work  ", Now).Comment.Should().Be("Good work");
    }

    [Fact]
    public void Owning_the_job_counts_as_participating()
    {
        var job = CompletedJob(Guid.NewGuid());

        RatingRules.IsParticipant(job, job.GiverId).Should().BeTrue();
    }

    [Fact]
    public void Having_a_booking_counts_as_participating()
    {
        var workerId = Guid.NewGuid();
        var job = CompletedJob(Guid.NewGuid(), workerId);

        RatingRules.IsParticipant(job, workerId).Should().BeTrue();
    }

    [Fact]
    public void Everybody_else_is_an_outsider()
    {
        var job = CompletedJob(Guid.NewGuid(), Guid.NewGuid());

        RatingRules.IsParticipant(job, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void A_profile_with_no_ratings_shows_no_average_and_no_count()
    {
        var (average, count) = RatingRules.Recalculate([]);

        average.Should().Be(0m);
        count.Should().Be(0);
    }

    [Fact]
    public void A_single_rating_is_its_own_average()
    {
        var (average, count) = RatingRules.Recalculate([Stars(5)]);

        average.Should().Be(5.00m);
        count.Should().Be(1);
    }

    [Theory]
    [InlineData(4, 4.00)]
    [InlineData(5, 5.00)]
    [InlineData(1, 1.00)]
    public void A_whole_average_keeps_two_decimal_places(int stars, decimal expected)
    {
        var (average, _) = RatingRules.Recalculate([Stars(stars)]);

        average.Should().Be(expected);
    }

    [Fact]
    public void A_repeating_average_is_rounded_to_two_places()
    {
        var (average, count) = RatingRules.Recalculate(
            [Stars(5), Stars(4), Stars(4), Stars(5), Stars(3)]);

        average.Should().Be(4.20m);
        count.Should().Be(5);
    }

    [Fact]
    public void A_half_rounds_away_from_zero()
    {
        // 4.125 has to become 4.13 rather than 4.12: the alternative is a worker whose
        // displayed average is below the true one of the ratings they were given.
        var (average, count) = RatingRules.Recalculate(
            [Stars(4), Stars(4), Stars(4), Stars(4), Stars(4), Stars(4), Stars(4), Stars(5)]);

        average.Should().Be(4.13m);
        count.Should().Be(8);
    }

    [Fact]
    public void An_average_that_repeats_exactly_is_not_given_a_dust_tail()
    {
        // 4.5 must read as 4.5, not 4.499999: trailing noise on a profile is the kind of
        // thing that makes a number look broken.
        var (average, _) = RatingRules.Recalculate([Stars(4), Stars(5)]);

        average.Should().Be(4.50m);
    }

    [Fact]
    public void The_count_is_the_number_of_ratings_not_the_number_of_stars()
    {
        var (_, count) = RatingRules.Recalculate(
            [Stars(5), Stars(5), Stars(5), Stars(5), Stars(5)]);

        count.Should().Be(5);
    }

    [Fact]
    public void The_average_is_calculated_from_what_is_passed_in_each_time()
    {
        // A single generator call over a live query would be fine, but the callers pass a
        // list; enumerating it twice is only safe because it is materialised.
        var ratings = new List<Rating> { Stars(2), Stars(3) };

        var first = RatingRules.Recalculate(ratings);
        var second = RatingRules.Recalculate(ratings);

        first.Should().Be(second);
        first.Average.Should().Be(2.50m);
    }
}
