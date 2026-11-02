using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Applications.Commands;

/// <summary>
/// Books one applicant. POST /api/v1/applications/{id}/select.
///
/// Two givers can be confirming the last slot at the same moment, and a double tap
/// sends the same request twice, so the work runs as one retriable transaction and the
/// slot is taken by a single conditional database update rather than by a count that was
/// read earlier and written back.
/// </summary>
public sealed record SelectWorkerCommand(Guid ApplicationId, SelectWorkerRequest Request)
    : IRequest<WorkerAssignmentDto>;

public sealed class SelectWorkerCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IWorkerSlotStore slots,
    INotificationService notifications) : IRequestHandler<SelectWorkerCommand, WorkerAssignmentDto>
{
    public async Task<WorkerAssignmentDto> Handle(SelectWorkerCommand request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        // The notification is sent once the transaction has committed, and the values it
        // needs are carried out of the unit of work, because a retried attempt must not
        // push twice.
        var booked = await db.ExecuteInTransactionAsync(async token =>
        {
            var (application, job) = await ApplicationOwnership.LoadAsync(
                db,
                request.ApplicationId,
                giverId,
                token);

            if (request.Request.ProposedPay is { } proposedPay)
            {
                application.ProposedPay = proposedPay;
            }

            // The agreed pay falls back through the same order the confirm screen shows:
            // what the giver types, then what they proposed while shortlisting, then the
            // pay on the posting.
            var agreedPay = request.Request.AgreedPay
                            ?? request.Request.ProposedPay
                            ?? application.ProposedPay
                            ?? job.Pay;

            // An applicant that is already selected means a concurrent request booked them
            // first. It is reported as a conflict rather than a rule failure, because
            // nothing about this request was malformed and the client's response is the
            // same either way: refresh and stop.
            if (application.Status == ApplicationStatus.Accepted)
            {
                throw new ConflictException(
                    "This applicant has already been booked for this job.",
                    "AlreadySelected");
            }

            // Order matters: the application is only marked selected once the rules have
            // passed and the slot is held, because every one of those checks requires an
            // application that is still open.
            var assignment = SelectionRules.CreateAssignment(job, application, agreedPay, now);

            // RowVersion has no database default, so it is set explicitly on insert.
            assignment.RowVersion = 0;

            if (!await slots.TryReserveSlotAsync(job.Id, token))
            {
                // Another request took the slot first. Re-reading the job and re-running
                // the rule turns that into the message this caller would have seen had
                // they arrived second in the read, instead of a generic failure.
                var current = await db.Jobs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(j => j.Id == job.Id, token)
                    ?? throw new NotFoundException("Job not found.");

                SelectionRules.EnsureCanSelect(current, application, agreedPay);

                throw new WorkerLimitReachedException(current.WorkersNeeded, current.WorkersFilled);
            }

            ApplicationRules.MarkSelected(application, now);

            db.WorkerAssignments.Add(assignment);

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex)
            {
                // The unique index on job_application_id is the backstop for two identical
                // requests that both read the application as still selectable.
                throw new ConflictException(
                    "This applicant has already been booked for this job.",
                    ex,
                    "AlreadySelected");
            }

            return new SelectedWorker(
                assignment.ToDto(job),
                assignment.Id,
                application.WorkerId,
                job.Title);
        }, cancellationToken);

        await notifications.SendAsync(
            booked.WorkerId,
            NotificationType.JobAccepted,
            "You have been selected",
            $"You were selected for \"{booked.JobTitle}\".",
            NotificationReferenceType.Assignment,
            booked.AssignmentId,
            NotificationRoutes.AcceptedJob,
            cancellationToken);

        return booked.Assignment;
    }

    /// <summary>What a committed selection needs for the response and the notification.</summary>
    private sealed record SelectedWorker(
        WorkerAssignmentDto Assignment,
        Guid AssignmentId,
        Guid WorkerId,
        string JobTitle);
}

/// <summary>
/// Books several applicants in one call. POST /api/v1/jobs/{id}/select-workers, which is
/// the Flutter confirm-selection step: the giver ticks the applicants, agrees a pay for
/// each and confirms once.
///
/// Either every worker in the request is booked or none is. The whole batch is one
/// transaction, so a request that cannot fit rolls back the slots it had already taken
/// rather than leaving the job half staffed.
/// </summary>
public sealed record SelectWorkersCommand(Guid JobId, SelectWorkersRequest Request)
    : IRequest<SelectWorkersResultDto>;

public sealed class SelectWorkersCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IWorkerSlotStore slots,
    INotificationService notifications) : IRequestHandler<SelectWorkersCommand, SelectWorkersResultDto>
{
    public async Task<SelectWorkersResultDto> Handle(SelectWorkersCommand request, CancellationToken cancellationToken)
    {
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        var batch = await db.ExecuteInTransactionAsync(async token =>
        {
            var job = await db.Jobs
                .Include(j => j.Applications)
                .FirstOrDefaultAsync(j => j.Id == request.JobId, token)
                ?? throw new NotFoundException("Job not found.");

            if (job.GiverId != giverId)
            {
                throw new ForbiddenException("Only the person who posted this job can select workers for it.");
            }

            // Everything is validated before a single row is written, so one bad entry
            // cannot leave the job partly booked.
            var assignments = new List<Domain.Entities.WorkerAssignment>(request.Request.Selections.Count);

            foreach (var (applicationId, agreedPay) in request.Request.Selections)
            {
                var application = job.Applications.FirstOrDefault(a => a.Id == applicationId)
                                  ?? throw new NotFoundException(
                                      $"Application {applicationId} does not belong to this job.");

                if (application.Status == ApplicationStatus.Accepted)
                {
                    throw new ConflictException(
                        "One of these applicants has already been booked for this job.",
                        "AlreadySelected");
                }

                // Validated and reserved while the application is still open; the status
                // only becomes selected once the whole batch has its slots.
                var assignment = SelectionRules.CreateAssignment(job, application, agreedPay, now);
                assignment.RowVersion = 0;

                if (!await slots.TryReserveSlotAsync(job.Id, token))
                {
                    throw new WorkerLimitReachedException(job.WorkersNeeded, job.WorkersFilled);
                }

                db.WorkerAssignments.Add(assignment);
                assignments.Add(assignment);
            }

            foreach (var assignment in assignments)
            {
                ApplicationRules.MarkSelected(
                    job.Applications.First(a => a.Id == assignment.ApplicationId),
                    now);
            }

            var released = new List<Domain.Entities.JobApplication>();

            if (request.Request.ReleaseUnselectedShortlists)
            {
                var selectedIds = assignments.Select(a => a.ApplicationId).ToHashSet();

                released.AddRange(job.Applications.Where(a =>
                    a.Status == ApplicationStatus.Shortlisted && !selectedIds.Contains(a.Id)));

                foreach (var application in released)
                {
                    ApplicationRules.Release(application, now);
                }
            }

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflictException(
                    "One of these applicants has already been booked for this job.",
                    ex,
                    "AlreadySelected");
            }

            // The increments were done with raw SQL, so the counter is read back from the
            // database rather than from the tracked entity.
            var filled = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == job.Id)
                .Select(j => j.WorkersFilled)
                .FirstOrDefaultAsync(token);

            return new SelectedBatch(
                new SelectWorkersResultDto
                {
                    Selected = assignments.Select(a => a.ToDto(job)).ToList(),
                    ReleasedApplicationIds = released.Select(a => a.Id).ToList(),
                    WorkersFilled = filled,
                    WorkersNeeded = job.WorkersNeeded,
                    RemainingSlots = Math.Max(job.WorkersNeeded - filled, 0)
                },
                assignments.Select(a => (a.Id, a.WorkerId)).ToList(),
                released.Select(a => (a.Id, a.WorkerId)).ToList(),
                job.Title);
        }, cancellationToken);

        foreach (var (assignmentId, workerId) in batch.Booked)
        {
            await notifications.SendAsync(
                workerId,
                NotificationType.JobAccepted,
                "You have been selected",
                $"You were selected for \"{batch.JobTitle}\".",
                NotificationReferenceType.Assignment,
                assignmentId,
                NotificationRoutes.AcceptedJob,
                cancellationToken);
        }

        foreach (var (applicationId, workerId) in batch.Released)
        {
            await notifications.SendAsync(
                workerId,
                NotificationType.ApplicationUpdate,
                "Shortlist updated",
                $"The shortlist for \"{batch.JobTitle}\" was updated and you are still in the running.",
                NotificationReferenceType.Application,
                applicationId,
                NotificationRoutes.ApplicationDetails,
                cancellationToken);
        }

        return batch.Result;
    }

    /// <summary>What a committed batch needs for the response and the notifications.</summary>
    private sealed record SelectedBatch(
        SelectWorkersResultDto Result,
        IReadOnlyList<(Guid AssignmentId, Guid WorkerId)> Booked,
        IReadOnlyList<(Guid ApplicationId, Guid WorkerId)> Released,
        string JobTitle);
}
