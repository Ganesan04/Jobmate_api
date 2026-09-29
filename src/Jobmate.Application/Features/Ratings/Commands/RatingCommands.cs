using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Ratings;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Ratings.Commands;

/// <summary>
/// The worker proves they turned up by entering the code the giver reads out.
/// POST /api/v1/assignments/{id}/check-in.
///
/// The code is the attendance record for the whole shift, so it is checked against the
/// booking and then cleared: the same code cannot be replayed to claim a second
/// check-in, and the giver's copy stops working the moment it is used.
/// </summary>
public sealed record CheckInAssignmentCommand(Guid AssignmentId, CheckInRequest Request)
    : IRequest<WorkerAssignmentDto>;

public sealed class CheckInAssignmentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications)
    : IRequestHandler<CheckInAssignmentCommand, WorkerAssignmentDto>
{
    public async Task<WorkerAssignmentDto> Handle(
        CheckInAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var workerId = currentUser.RequireRole(nameof(UserRole.Worker));
        var now = clock.UtcNow;

        var assignment = await db.WorkerAssignments
            .Include(a => a.Job)
            .Include(a => a.Worker)
            .Include(a => a.Payment)
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (assignment.WorkerId != workerId)
        {
            throw new ForbiddenException("You can only check in to your own booking.");
        }

        // Checked before the rule so "your booking is not running yet" is the answer,
        // rather than "wrong code" for a code that has not been issued. Only InProgress
        // counts as started: Active just means the job is open for applications.
        if (assignment.Job?.Status is not JobStatus.InProgress)
        {
            throw new BusinessRuleException(
                "This job has not started yet.",
                "JobNotStarted");
        }

        SelectionRules.CheckIn(assignment, request.Request.Code, now);

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            assignment.GiverId,
            NotificationType.ApplicationUpdate,
            "Worker checked in",
            $"{assignment.Worker?.FullName ?? "Your worker"} has checked in for \"{assignment.Job?.Title}\".",
            NotificationReferenceType.Assignment,
            assignment.Id,
            NotificationRoutes.MyJobs,
            cancellationToken);

        return assignment.ToDto(assignment.Job);
    }
}

/// <summary>
/// Closes a booking once the work is done.
/// POST /api/v1/assignments/{id}/complete.
///
/// Either side may close it — the worker when they finish, the giver when they accept
/// that the work is done — because a booking left open forever would keep the job out
/// of the completed state and neither side could rate or be paid. Completing the last
/// live booking completes the job, which is what unlocks ratings.
/// </summary>
public sealed record CompleteAssignmentCommand(Guid AssignmentId, CompleteAssignmentRequest Request)
    : IRequest<WorkerAssignmentDto>;

public sealed class CompleteAssignmentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications)
    : IRequestHandler<CompleteAssignmentCommand, WorkerAssignmentDto>
{
    public async Task<WorkerAssignmentDto> Handle(
        CompleteAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var assignment = await db.WorkerAssignments
            .Include(a => a.Job)
            .ThenInclude(j => j!.Assignments)
            .Include(a => a.Payment)
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        if (assignment.WorkerId != callerId && assignment.GiverId != callerId)
        {
            throw new ForbiddenException("Only the worker or the employer can close this booking.");
        }

        if (request.Request.OvertimeAmount is { } overtime)
        {
            assignment.OvertimeAmount = overtime;
        }

        SelectionRules.Complete(assignment, now);

        // The job is finished once nothing is still outstanding, so the app can show it
        // as completed and both sides can leave a rating without a separate step.
        var job = assignment.Job;

        if (job is not null && job.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled).All(a => a.Status == AssignmentStatus.Completed))
        {
            JobLifecycle.Complete(job, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            assignment.WorkerId == callerId ? assignment.GiverId : assignment.WorkerId,
            NotificationType.JobAccepted,
            "Work completed",
            $"\"{job?.Title}\" has been marked complete.",
            NotificationReferenceType.Assignment,
            assignment.Id,
            assignment.WorkerId == callerId ? NotificationRoutes.AcceptedJob : NotificationRoutes.MyJobs,
            cancellationToken);

        return assignment.ToDto(job);
    }
}

/// <summary>
/// The giver reads out the check-in code for one booking.
/// GET /api/v1/jobs/{jobId}/check-in-code/{assignmentId}.
///
/// Only the employer sees it. A worker asking for their own code would defeat the point
/// of the code, which is that the employer confirms the person on site.
/// </summary>
public sealed record GetCheckInCodeQuery(Guid JobId, Guid AssignmentId) : IRequest<string>;

public sealed class GetCheckInCodeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetCheckInCodeQuery, string>
{
    public async Task<string> Handle(GetCheckInCodeQuery request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));

        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        if (job.GiverId != giverId)
        {
            throw new ForbiddenException("Only the person who posted this job can see its check-in codes.");
        }

        return SelectionRules.CheckInCodeFor(job, request.AssignmentId);
    }
}

/// <summary>
/// One participant rates the other after a completed job.
/// POST /api/v1/ratings — the Flutter rate-worker screen.
///
/// Both directions are accepted: a giver rates the worker, a worker rates the giver.
/// The recipient's stored average is recalculated in the same transaction so a profile
/// never shows a rating that is not counted.
/// </summary>
public sealed record CreateRatingCommand(CreateRatingRequest Request) : IRequest<WorkerRatingDto>;

public sealed class CreateRatingCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<CreateRatingCommand, WorkerRatingDto>
{
    public async Task<WorkerRatingDto> Handle(CreateRatingCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var job = await db.Jobs
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == request.Request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        var from = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Account not found.");

        var to = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Request.ToUserId, cancellationToken)
            ?? throw new NotFoundException("That account no longer exists.");

        // The unique index on (job, from, to) is what actually stops a second rating;
        // catching it here turns the violation into an explanation. Checked before the
        // rule so a repeat attempt is reported as a conflict rather than a rule error.
        var alreadyRated = await db.Ratings.AnyAsync(
            r => r.JobId == job.Id && r.FromUserId == from.Id && r.ToUserId == to.Id,
            cancellationToken);

        if (alreadyRated)
        {
            throw new ConflictException("You have already rated this person for this job.", "AlreadyRated");
        }

        var rating = RatingRules.Create(job, from, to, request.Request.Stars, request.Request.Feedback, now);

        db.Ratings.Add(rating);

        await RecalculateAsync(to.Id, rating, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return rating.ToDto(job.Title, from.FullName);
    }

    /// <summary>
    /// Recomputes the recipient's denormalised average. Both profile kinds are updated
    /// because either side of a job can be rated, and the rule that produces the average
    /// is the same for each.
    /// </summary>
    /// <param name="justAdded">
    /// The rating being created. It is still only in the change tracker when this runs, so
    /// without it the average would be one behind the rating that is about to be saved.
    /// </param>
    private async Task RecalculateAsync(Guid userId, Rating justAdded, CancellationToken cancellationToken)
    {
        var received = await db.Ratings
            .AsNoTracking()
            .Where(r => r.ToUserId == userId)
            .ToListAsync(cancellationToken);

        received.Add(justAdded);

        var (average, count) = RatingRules.Recalculate(received);

        var worker = await db.WorkerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (worker is not null)
        {
            worker.AverageRating = average;
            worker.RatingCount = count;

            return;
        }

        var giver = await db.GiverProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (giver is not null)
        {
            giver.AverageRating = average;
            giver.RatingCount = count;
        }
    }
}
