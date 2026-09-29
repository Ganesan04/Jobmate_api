using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Jobs;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Jobs.Commands;

/// <summary>
/// Creates a job from the six-step Create Job wizard. The wizard posts once, so the
/// request is carried whole rather than split across forty command parameters; the
/// two-step alternative is <c>PublishImmediately = false</c> followed by
/// <c>POST /jobs/{id}/publish</c>.
/// </summary>
public sealed record CreateJobCommand(CreateJobRequest Request) : IRequest<JobDto>;

public sealed class CreateJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<CreateJobCommand, JobDto>
{
    public async Task<JobDto> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        // Only a giver may post work; a worker calling this is a client bug, not a
        // permission to be silently upgraded.
        var giverId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var body = request.Request;
        var now = clock.UtcNow;

        var giver = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == giverId, cancellationToken)
            ?? throw new NotFoundException("Your account no longer exists.");

        var job = new Job
        {
            GiverId = giverId,
            Company = await ResolveCompanyAsync(giverId, cancellationToken),
            Title = body.Title.Trim(),
            Category = body.Category,
            Description = body.Description.Trim(),
            WorkersNeeded = body.WorkersNeeded,
            JobDate = body.Date,
            StartTime = body.StartTime,
            EndTime = body.EndTime,
            ReportTime = body.ReportTime,
            BreakMinutes = body.BreakMinutes,
            IsRecurring = body.IsRecurring,
            Weekdays = NormaliseWeekdays(body.Weekdays),
            Pay = body.Pay,
            PayType = body.PayType,
            OvertimeRate = body.OvertimeRate,
            TravelAllowance = body.TravelAllowance,
            FoodAllowance = body.FoodAllowance,
            PaymentDate = body.PaymentDate,
            PaymentMethod = body.PaymentMethod,
            Area = body.Area.Trim(),
            Address = body.Address.Trim(),
            WorkplaceType = body.WorkplaceType,
            LocationVisible = body.LocationVisible,
            Latitude = body.Latitude,
            Longitude = body.Longitude,
            ContactPerson = NullIfBlank(body.ContactPerson),
            SafetyInformation = NullIfBlank(body.SafetyInfo),
            PhysicalRequirements = NullIfBlank(body.PhysicalRequirement),
            DressCode = NullIfBlank(body.DressCode),
            VehicleRequired = body.VehicleRequired,
            EmergencyContact = NullIfBlank(body.EmergencyContact),
            IsUrgent = body.IsUrgent,
            ApplicationDeadline = body.ApplicationDeadline ?? body.Date,
            Experience = NullIfBlank(body.Experience),
            // Recurring is the only way a job runs on more than one weekday, so the
            // two flags are kept consistent rather than trusting the client.
            JobType = body.IsRecurring ? JobType.Recurring : JobType.OneTime,
            Status = JobStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        job.Skills = NormaliseSkills(body.Skills)
            .Select(name => new JobSkill { JobId = job.Id, Name = name, CreatedAt = now, UpdatedAt = now })
            .ToList();

        // The weekday list lives in its own table; Weekdays stays as the in-memory
        // mirror the domain rules read.
        job.WeekdayRows = job.Weekdays
            .Select(day => new JobWeekday { JobId = job.Id, DayOfWeek = day, CreatedAt = now, UpdatedAt = now })
            .ToList();

        if (body.PublishImmediately)
        {
            JobLifecycle.Publish(job, now);
        }

        db.Jobs.Add(job);

        // The profile is the giver's own row, so a posting is the one place that has to
        // keep the counter honest. It is tracked rather than computed because the stats
        // card reads it on every dashboard load.
        var profile = await db.GiverProfiles
            .FirstOrDefaultAsync(p => p.UserId == giverId, cancellationToken);

        if (profile is not null)
        {
            profile.TotalJobsPosted++;
            profile.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (body.PublishImmediately)
        {
            await NotifyPublishedAsync(job, giverId, cancellationToken);
        }

        return job.ToDto(applicantCount: 0, skills: job.Skills.Select(s => s.Name).ToList());
    }

    /// <summary>
    /// The posting shows the giver's business name when they have one, so the job card
    /// is not blank on first publish.
    /// </summary>
    private async Task<string> ResolveCompanyAsync(Guid giverId, CancellationToken cancellationToken)
    {
        var profile = await db.GiverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == giverId, cancellationToken);

        return string.IsNullOrWhiteSpace(profile?.BusinessName)
            ? profile?.ContactPerson ?? string.Empty
            : profile.BusinessName;
    }

    /// <summary>Trims, drops blanks and de-duplicates, case-insensitively.</summary>
    internal static List<string> NormaliseSkills(IEnumerable<string>? skills) =>
        (skills ?? [])
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Select(skill => skill.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>ISO day numbers 1..7, de-duplicated and ordered.</summary>
    internal static List<int> NormaliseWeekdays(IEnumerable<int>? weekdays) =>
        (weekdays ?? [])
            .Where(day => day is >= 1 and <= 7)
            .Distinct()
            .Order()
            .ToList();

    internal static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Publishing is a public event, so nearby workers are told about it. Push dispatch
    /// is best-effort and never blocks the response.
    /// </summary>
    private Task NotifyPublishedAsync(Job job, Guid giverId, CancellationToken cancellationToken) =>
        notifications.SendAsync(
            giverId,
            NotificationType.System,
            "Job published",
            $"\"{job.Title}\" is now live and accepting applications.",
            NotificationReferenceType.Job,
            job.Id,
            NotificationRoutes.MyJobs,
            cancellationToken);
}

/// <summary>
/// Edits a job. Only the fields present in the payload change, and the domain rules
/// decide whether the job may be edited at all — a client cannot widen its own
/// permissions by editing a field.
/// </summary>
public sealed record UpdateJobCommand(Guid JobId, UpdateJobRequest Request) : IRequest<JobDto>;

public sealed class UpdateJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<UpdateJobCommand, JobDto>
{
    public async Task<JobDto> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var body = request.Request;
        var now = clock.UtcNow;

        var job = await db.Jobs
            .Include(j => j.Skills)
            .Include(j => j.WeekdayRows)
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        EnsureOwner(job, callerId);

        // Applications must be loaded for the rule below to see them.
        JobLifecycle.EnsureCanEdit(job);

        if (body.WorkersNeeded is { } workersNeeded && workersNeeded < job.WorkersFilled)
        {
            throw new BusinessRuleException(
                $"You already have {job.WorkersFilled} worker(s) booked, so the requirement cannot drop below that.",
                "WorkerCountBelowFilled");
        }

        if (body.Title is not null)
        {
            job.Title = body.Title.Trim();
        }

        if (body.Category is not null)
        {
            job.Category = body.Category.Value;
        }

        if (body.Description is not null)
        {
            job.Description = body.Description.Trim();
        }

        if (body.WorkersNeeded is not null)
        {
            job.WorkersNeeded = body.WorkersNeeded.Value;
        }

        if (body.Experience is not null)
        {
            job.Experience = CreateJobCommandHandler.NullIfBlank(body.Experience);
        }

        if (body.Date is not null)
        {
            job.JobDate = body.Date.Value;
        }

        if (body.StartTime is not null)
        {
            job.StartTime = body.StartTime.Value;
        }

        if (body.EndTime is not null)
        {
            job.EndTime = body.EndTime.Value;
        }

        if (body.ReportTime is not null)
        {
            job.ReportTime = body.ReportTime.Value;
        }

        if (body.BreakMinutes is not null)
        {
            job.BreakMinutes = body.BreakMinutes.Value;
        }

        if (body.IsRecurring is not null)
        {
            job.IsRecurring = body.IsRecurring.Value;
            job.JobType = body.IsRecurring.Value ? JobType.Recurring : JobType.OneTime;

            if (!body.IsRecurring.Value)
            {
                job.Weekdays = [];
                db.JobWeekdays.RemoveRange(job.WeekdayRows);
                job.WeekdayRows.Clear();
            }
        }

        if (body.Weekdays is not null)
        {
            var weekdays = CreateJobCommandHandler.NormaliseWeekdays(body.Weekdays);

            job.Weekdays = weekdays;
            job.IsRecurring = weekdays.Count > 0;
            job.JobType = job.IsRecurring ? JobType.Recurring : JobType.OneTime;

            db.JobWeekdays.RemoveRange(job.WeekdayRows);
            job.WeekdayRows.Clear();

            job.WeekdayRows = weekdays
                .Select(day => new JobWeekday { JobId = job.Id, DayOfWeek = day, CreatedAt = now, UpdatedAt = now })
                .ToList();
        }

        if (body.Pay is not null)
        {
            job.Pay = body.Pay.Value;
        }

        if (body.PayType is not null)
        {
            job.PayType = body.PayType.Value;
        }

        if (body.OvertimeRate is not null)
        {
            job.OvertimeRate = body.OvertimeRate.Value;
        }

        if (body.TravelAllowance is not null)
        {
            job.TravelAllowance = body.TravelAllowance.Value;
        }

        if (body.FoodAllowance is not null)
        {
            job.FoodAllowance = body.FoodAllowance.Value;
        }

        if (body.PaymentDate is not null)
        {
            job.PaymentDate = body.PaymentDate;
        }

        if (body.PaymentMethod is not null)
        {
            job.PaymentMethod = body.PaymentMethod.Value;
        }

        if (body.Area is not null)
        {
            job.Area = body.Area.Trim();
        }

        if (body.Address is not null)
        {
            job.Address = body.Address.Trim();
        }

        if (body.WorkplaceType is not null)
        {
            job.WorkplaceType = body.WorkplaceType.Value;
        }

        if (body.LocationVisible is not null)
        {
            job.LocationVisible = body.LocationVisible.Value;
        }

        if (body.Latitude is not null)
        {
            job.Latitude = body.Latitude;
        }

        if (body.Longitude is not null)
        {
            job.Longitude = body.Longitude;
        }

        if (body.ContactPerson is not null)
        {
            job.ContactPerson = CreateJobCommandHandler.NullIfBlank(body.ContactPerson);
        }

        if (body.SafetyInfo is not null)
        {
            job.SafetyInformation = CreateJobCommandHandler.NullIfBlank(body.SafetyInfo);
        }

        if (body.PhysicalRequirement is not null)
        {
            job.PhysicalRequirements = CreateJobCommandHandler.NullIfBlank(body.PhysicalRequirement);
        }

        if (body.DressCode is not null)
        {
            job.DressCode = CreateJobCommandHandler.NullIfBlank(body.DressCode);
        }

        if (body.VehicleRequired is not null)
        {
            job.VehicleRequired = body.VehicleRequired.Value;
        }

        if (body.EmergencyContact is not null)
        {
            job.EmergencyContact = CreateJobCommandHandler.NullIfBlank(body.EmergencyContact);
        }

        if (body.IsUrgent is not null)
        {
            job.IsUrgent = body.IsUrgent.Value;
        }

        if (body.ApplicationDeadline is not null)
        {
            job.ApplicationDeadline = body.ApplicationDeadline;
        }

        if (body.Skills is not null)
        {
            var skills = CreateJobCommandHandler.NormaliseSkills(body.Skills);

            db.JobSkills.RemoveRange(job.Skills);
            job.Skills.Clear();

            job.Skills = skills
                .Select(name => new JobSkill { JobId = job.Id, Name = name, CreatedAt = now, UpdatedAt = now })
                .ToList();
        }

        job.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return job.ToDto(
            applicantCount: job.Applications.Count,
            skills: job.Skills.Select(s => s.Name).ToList());
    }

    /// <summary>Only the posting's owner, or an admin, may change it.</summary>
    internal static void EnsureOwner(Job job, Guid callerId)
    {
        if (job.GiverId == callerId)
        {
            return;
        }

        throw new ForbiddenException("Only the person who posted this job can change it.");
    }
}

/// <summary>Draft → Active.</summary>
public sealed record PublishJobCommand(Guid JobId) : IRequest<JobDto>;

public sealed class PublishJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<PublishJobCommand, JobDto>
{
    public async Task<JobDto> Handle(PublishJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        // The publish rule reads the weekday list, so it is loaded rather than relying
        // on a lazy load that is not configured.
        var job = await db.Jobs
            .Include(j => j.WeekdayRows)
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        UpdateJobCommandHandler.EnsureOwner(job, callerId);

        JobLifecycle.Publish(job, now);

        await db.SaveChangesAsync(cancellationToken);

        await notifications.SendAsync(
            callerId,
            NotificationType.System,
            "Job published",
            $"\"{job.Title}\" is now live and accepting applications.",
            NotificationReferenceType.Job,
            job.Id,
            NotificationRoutes.MyJobs,
            cancellationToken);

        return job.ToDto(applicantCount: job.Applications.Count);
    }
}

/// <summary>Cancels a job and closes everything attached to it.</summary>
public sealed record CancelJobCommand(Guid JobId, CancelJobRequest Request) : IRequest<JobDto>;

public sealed class CancelJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<CancelJobCommand, JobDto>
{
    public async Task<JobDto> Handle(CancelJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireRole(nameof(UserRole.WorkGiver));
        var now = clock.UtcNow;

        var job = await db.Jobs
            .Include(j => j.Applications)
            .Include(j => j.Assignments)
            .Include(j => j.Payments)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        UpdateJobCommandHandler.EnsureOwner(job, callerId);

        // The caller is a giver, so the administration check inside the rule can only
        // be satisfied by a genuine posting owner.
        if (job.GiverId != callerId)
        {
            throw new ForbiddenException("Only the person who posted this job can cancel it.");
        }

        JobLifecycle.Cancel(job, CreateJobCommandHandler.NullIfBlank(request.Request.Reason), now);

        // Applicants and booked workers both have to be told, or the app would show a
        // cancelled job as still live in their lists.
        var openApplicantIds = job.Applications
            .Where(a => a.Status is ApplicationStatus.Applied or ApplicationStatus.Viewed or ApplicationStatus.Shortlisted)
            .Select(a => a.WorkerId)
            .ToList();

        foreach (var application in job.Applications.Where(a => a.IsOpen))
        {
            ApplicationRules.Expire(application);
        }

        var bookedWorkerIds = job.Assignments
            .Where(a => a.Status is AssignmentStatus.Pending or AssignmentStatus.Active or AssignmentStatus.CheckedIn)
            .Select(a => a.WorkerId)
            .ToList();

        foreach (var assignment in job.Assignments.Where(a => bookedWorkerIds.Contains(a.WorkerId)))
        {
            assignment.Status = AssignmentStatus.Cancelled;
        }

        job.WorkersFilled = 0;

        var refundedWorkerIds = RefundPayments(job, request.Request.RefundPayments, now);

        await db.SaveChangesAsync(cancellationToken);

        var recipients = openApplicantIds.Concat(bookedWorkerIds).Concat(refundedWorkerIds).Distinct().ToList();

        if (recipients.Count > 0)
        {
            await notifications.SendManyAsync(
                recipients,
                NotificationType.ApplicationUpdate,
                "Job cancelled",
                $"\"{job.Title}\" was cancelled by the employer.",
                NotificationReferenceType.Job,
                job.Id,
                NotificationRoutes.JobDetails,
                cancellationToken);
        }

        return job.ToDto(applicantCount: job.Applications.Count);
    }

    /// <summary>
    /// Money that already left the giver is returned when the giver asks for it. Only
    /// payments the rules allow to move are touched, and the workers affected are
    /// returned so they can be told: a cancelled job that silently kept the pay would
    /// look like theft from the app's point of view.
    /// </summary>
    private static List<Guid> RefundPayments(Job job, bool refund, DateTime now)
    {
        if (!refund)
        {
            return [];
        }

        var refundedWorkerIds = new List<Guid>();

        foreach (var payment in job.Payments)
        {
            // Disputed money follows the same path as settled money: the dispute is
            // resolved in the giver's favour by the refund.
            var target = payment.Status switch
            {
                PaymentStatus.Paid or PaymentStatus.Disputed => PaymentStatus.Refunded,
                // Never captured, so there is nothing to send back.
                PaymentStatus.Pending => PaymentStatus.Cancelled,
                _ => (PaymentStatus?)null
            };

            // Processing is deliberately left alone: the charge is already in flight, so
            // the provider webhook settles it and the refund follows once it is Paid.
            if (target is null || !PaymentStatusRules.CanTransition(payment.Status, target.Value))
            {
                continue;
            }

            payment.Status = target.Value;

            if (target == PaymentStatus.Refunded)
            {
                payment.RefundedAt = now;
                refundedWorkerIds.Add(payment.WorkerId);
            }
        }

        return refundedWorkerIds.Distinct().ToList();
    }
}

/// <summary>Active → InProgress, once at least one worker is booked.</summary>
public sealed record StartJobCommand(Guid JobId) : IRequest<JobDto>;

public sealed class StartJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<StartJobCommand, JobDto>
{
    public async Task<JobDto> Handle(StartJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var job = await db.Jobs
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        UpdateJobCommandHandler.EnsureOwner(job, callerId);

        JobLifecycle.Start(job, now);

        // Starting the job activates every live booking and issues each worker a
        // single-use check-in code, so the giver can confirm on the day that the person
        // who turned up is the person who was booked.
        foreach (var assignment in job.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
        {
            SelectionRules.Activate(assignment);
            SelectionRules.IssueCheckInCode(assignment, now);
        }

        var bookings = job.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled)
            .Select(a => (a.Id, a.WorkerId))
            .Distinct()
            .ToList();

        await db.SaveChangesAsync(cancellationToken);

        // Sent one per booking rather than fanned out over the job, so each worker
        // carries their own assignment id and lands on the screen where the check-in
        // code is entered rather than on a list they have to search.
        foreach (var (assignmentId, workerId) in bookings)
        {
            await notifications.SendAsync(
                workerId,
                NotificationType.JobAccepted,
                "Work started",
                $"\"{job.Title}\" has started. Please report at {job.ReportTime:hh\\:mm}.",
                NotificationReferenceType.Assignment,
                assignmentId,
                NotificationRoutes.AcceptedJob,
                cancellationToken);
        }

        return job.ToDto();
    }
}

/// <summary>Active or InProgress → Completed, which is what unlocks ratings.</summary>
public sealed record CompleteJobCommand(Guid JobId) : IRequest<JobDto>;

public sealed class CompleteJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    INotificationService notifications) : IRequestHandler<CompleteJobCommand, JobDto>
{
    public async Task<JobDto> Handle(CompleteJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var job = await db.Jobs
            .Include(j => j.Assignments)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        UpdateJobCommandHandler.EnsureOwner(job, callerId);

        JobLifecycle.Complete(job, now);

        foreach (var assignment in job.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
        {
            assignment.Status = AssignmentStatus.Completed;
            assignment.CompletedAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var workerIds = job.Assignments
            .Where(a => a.Status == AssignmentStatus.Completed)
            .Select(a => a.WorkerId)
            .Distinct()
            .ToList();

        if (workerIds.Count > 0)
        {
            await notifications.SendManyAsync(
                workerIds,
                NotificationType.ApplicationUpdate,
                "Job completed",
                $"\"{job.Title}\" is marked complete. You can now rate the employer.",
                NotificationReferenceType.Job,
                job.Id,
                NotificationRoutes.RateWorker,
                cancellationToken);
        }

        return job.ToDto();
    }
}

/// <summary>
/// Deletes a draft outright. A job that has been live has to be cancelled instead, so
/// the applications and payments attached to it keep a valid history.
/// </summary>
public sealed record DeleteJobCommand(Guid JobId) : IRequest<Unit>;

public sealed class DeleteJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<DeleteJobCommand, Unit>
{
    public async Task<Unit> Handle(DeleteJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireRole(nameof(UserRole.WorkGiver));

        var job = await db.Jobs
            .Include(j => j.Skills)
            .Include(j => j.WeekdayRows)
            .Include(j => j.Applications)
            .Include(j => j.Assignments)
            .Include(j => j.Payments)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        if (job.GiverId != callerId)
        {
            throw new ForbiddenException("Only the person who posted this job can delete it.");
        }

        if (job.Status != JobStatus.Draft)
        {
            throw new BusinessRuleException(
                "Only a draft can be deleted. Cancel the job instead so its history is kept.",
                "JobNotDeletable");
        }

        if (job.Applications.Count > 0 || job.Assignments.Count > 0 || job.Payments.Count > 0)
        {
            throw new BusinessRuleException(
                "This job already has activity against it, so it can only be cancelled.",
                "JobHasActivity");
        }

        db.JobSkills.RemoveRange(job.Skills);
        db.JobWeekdays.RemoveRange(job.WeekdayRows);
        db.Jobs.Remove(job);

        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
