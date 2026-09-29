using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// The job lifecycle, expressed as pure rules over <see cref="Job"/>. Handlers call
/// these methods rather than assigning <c>job.Status</c> directly, so an illegal
/// transition can never reach the database and clients cannot invent a status.
/// </summary>
public static class JobLifecycle
{
    public static void EnsureCanEdit(Job job)
    {
        if (!JobStatusTransitions.Editable.Contains(job.Status))
        {
            throw new BusinessRuleException(
                $"A {job.Status.ToString().ToLowerInvariant()} job can no longer be edited.",
                "JobNotEditable");
        }

        if (job.Status == JobStatus.Active && job.PublishedAt is not null && job.Applications.Count > 0)
        {
            throw new BusinessRuleException(
                "This job already has applicants, so it can no longer be edited.",
                "JobHasApplicants");
        }
    }

    /// <summary>Draft -> Active. Flips the job on and stamps <c>PublishedAt</c>.</summary>
    public static void Publish(Job job, DateTime now)
    {
        if (job.Status is not (JobStatus.Draft or JobStatus.Pending))
        {
            throw new BusinessRuleException(
                $"Only a draft or pending job can be published (current status: {Label(job.Status)}).",
                "JobNotPublishable");
        }

        if (job.WorkersNeeded < 1)
        {
            throw new BusinessRuleException("At least one worker is required.", "WorkerCountInvalid");
        }

        if (job.Pay <= 0)
        {
            throw new BusinessRuleException("Pay must be greater than zero.", "PayInvalid");
        }

        if (job.IsRecurring && job.Weekdays.Count == 0)
        {
            throw new BusinessRuleException("Select at least one weekday for a recurring job.", "WeekdaysRequired");
        }

        job.Status = JobStatus.Active;
        job.PublishedAt = now;
    }

    /// <summary>Active -> InProgress. Requires at least one confirmed booking.</summary>
    public static void Start(Job job, DateTime now)
    {
        EnsureTransition(job, JobStatus.InProgress);

        if (job.WorkersFilled < 1)
        {
            throw new BusinessRuleException("Select at least one worker before starting this job.", "NoWorkersSelected");
        }

        job.Status = JobStatus.InProgress;
        job.StartedAt ??= now;
    }

    /// <summary>InProgress (or Active) -> Completed.</summary>
    public static void Complete(Job job, DateTime now)
    {
        EnsureTransition(job, JobStatus.Completed);

        if (job.WorkersFilled < 1)
        {
            throw new BusinessRuleException("This job has no confirmed workers.", "NoWorkersSelected");
        }

        job.Status = JobStatus.Completed;
        job.CompletedAt = now;
    }

    /// <summary>
    /// Any non-terminal -> Cancelled. Cancelling notifies every applicant and cancels
    /// outstanding bookings so the rules stay consistent for all three job states.
    /// </summary>
    public static void Cancel(Job job, string? reason, DateTime now)
    {
        if (JobStatusTransitions.IsTerminal(job.Status))
        {
            throw new BusinessRuleException(
                $"A {Label(job.Status)} job cannot be cancelled.",
                "JobAlreadyClosed");
        }

        job.Status = JobStatus.Cancelled;
        job.CancelledAt = now;
        job.CancellationReason = reason;
    }

    /// <summary>Called by a background sweep once the job date has passed without completion.</summary>
    public static bool TryExpire(Job job, DateOnly today, DateTime now)
    {
        if (job.Status is not (JobStatus.Active or JobStatus.InProgress))
        {
            return false;
        }

        if (job.JobDate >= today)
        {
            return false;
        }

        job.Status = JobStatus.Expired;
        return true;
    }

    public static void EnsureTransition(Job job, JobStatus target)
    {
        if (!JobStatusTransitions.CanTransition(job.Status, target))
        {
            throw new BusinessRuleException(
                $"Cannot change a {Label(job.Status)} job to {Label(target)}.",
                "InvalidStatusTransition");
        }
    }

    public static string Label(JobStatus status) =>
        status switch
        {
            JobStatus.Draft => "draft",
            JobStatus.Pending => "pending",
            JobStatus.Active => "active",
            JobStatus.InProgress => "in progress",
            JobStatus.Completed => "completed",
            JobStatus.Cancelled => "cancelled",
            JobStatus.Expired => "expired",
            _ => status.ToString().ToLowerInvariant()
        };
}
