using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
namespace Jobmate.Domain.Rules;

/// <summary>Rules that govern whether and how a worker may apply to a job.</summary>
public static class ApplicationRules
{
    public static JobApplication Create(
        Job job,
        User worker,
        string message,
        int? expectedPay,
        string? availableFrom,
        DateTime now,
        JobApplication? existing = null)
    {
        EnsureCanApply(job, worker, now, existing);

        if (expectedPay is < 0)
        {
            throw new BusinessRuleException("Expected pay cannot be negative.", "PayInvalid");
        }

        return new JobApplication
        {
            JobId = job.Id,
            WorkerId = worker.Id,
            Message = message?.Trim() ?? string.Empty,
            ExpectedPay = expectedPay,
            AvailableFrom = string.IsNullOrWhiteSpace(availableFrom) ? "Immediately available" : availableFrom.Trim(),
            Status = ApplicationStatus.Applied,
            AppliedAt = now
        };
    }

    public static void EnsureCanApply(Job job, User worker, DateTime now, JobApplication? existing = null)
    {
        if (job.GiverId == worker.Id)
        {
            throw new BusinessRuleException("You cannot apply to your own job.", "OwnJob");
        }

        // One application per worker per job, which the unique index on
        // (job_id, worker_user_id) also enforces. Checking here turns a double tap on Apply
        // into a readable 409 instead of a database error, and a previously closed
        // application stays closed rather than being resurrected.
        if (existing is not null)
        {
            throw new ConflictException(
                existing.IsOpen
                    ? "You have already applied to this job."
                    : $"You already {Label(existing.Status)} this application.",
                "AlreadyApplied");
        }

        if (!worker.IsActive)
        {
            throw new ForbiddenException("Your account has been deactivated.");
        }

        if (job.Status is JobStatus.Cancelled or JobStatus.Expired)
        {
            throw new BusinessRuleException(
                $"This job is {JobLifecycle.Label(job.Status)} and is no longer accepting applications.",
                "JobClosed");
        }

        if (job.Status is JobStatus.Completed or JobStatus.InProgress)
        {
            throw new BusinessRuleException("This job is no longer accepting applications.", "JobClosed");
        }

        if (job.Status is not (JobStatus.Active or JobStatus.Draft or JobStatus.Pending))
        {
            throw new BusinessRuleException("This job is not accepting applications.", "JobClosed");
        }

        if (job.Status != JobStatus.Active)
        {
            throw new BusinessRuleException("This job is not open for applications yet.", "JobNotOpen");
        }

        if (job.WorkersFilled >= job.WorkersNeeded)
        {
            throw new BusinessRuleException("All worker slots for this job are filled.", "WorkerLimitReached");
        }

        if (job.ApplicationDeadline is { } deadline && DateOnly.FromDateTime(now) > deadline)
        {
            throw new BusinessRuleException("The application deadline for this job has passed.", "ApplicationClosed");
        }
    }

    /// <summary>Withdraws an application. Only the worker may do this, and only while it is open.</summary>
    public static void Withdraw(JobApplication application, User worker, DateTime now)
    {
        if (application.WorkerId != worker.Id)
        {
            throw new ForbiddenException("You can only withdraw your own application.");
        }

        if (!ApplicationStatusTransitions.CanTransition(application.Status, ApplicationStatus.Withdrawn))
        {
            throw new BusinessRuleException(
                $"A {Label(application.Status)} application can no longer be withdrawn.",
                "ApplicationClosed");
        }

        application.Status = ApplicationStatus.Withdrawn;
        application.WithdrawnAt = now;
    }

    /// <summary>Marks an application as seen the first time a giver opens the applicant list.</summary>
    public static void MarkViewed(JobApplication application, DateTime now)
    {
        if (application.Status != ApplicationStatus.Applied || application.ViewedAt is not null)
        {
            return;
        }

        application.Status = ApplicationStatus.Viewed;
        application.ViewedAt = now;
    }

    public static void Shortlist(JobApplication application, DateTime now)
    {
        if (!ApplicationStatusTransitions.CanTransition(application.Status, ApplicationStatus.Shortlisted))
        {
            throw new BusinessRuleException(
                $"A {Label(application.Status)} application cannot be shortlisted.",
                "InvalidApplicationTransition");
        }

        application.Status = ApplicationStatus.Shortlisted;
        application.ShortlistedAt = now;
    }

    public static void Reject(JobApplication application, string? reason, DateTime now)
    {
        if (!ApplicationStatusTransitions.CanTransition(application.Status, ApplicationStatus.Rejected))
        {
            throw new BusinessRuleException(
                $"A {Label(application.Status)} application cannot be rejected.",
                "InvalidApplicationTransition");
        }

        application.Status = ApplicationStatus.Rejected;
        application.RejectedAt = now;
        application.RejectionReason = reason;
    }

    public static void MarkSelected(JobApplication application, DateTime now)
    {
        if (application.Status is not (ApplicationStatus.Applied or ApplicationStatus.Viewed or ApplicationStatus.Shortlisted))
        {
            throw new BusinessRuleException(
                $"A {Label(application.Status)} application cannot be selected.",
                "InvalidApplicationTransition");
        }

        application.Status = ApplicationStatus.Accepted;
        application.AcceptedAt = now;
    }

    /// <summary>
    /// Puts a shortlisted application back in the open pool. Used when a giver confirms
    /// a selection in bulk and the applicants they shortlisted but did not pick should
    /// stay in the running. The view is kept, because the giver has seen it, but the
    /// shortlist marker is cleared so the applicant is not counted twice.
    /// </summary>
    public static void Release(JobApplication application, DateTime now)
    {
        if (application.Status != ApplicationStatus.Shortlisted)
        {
            return;
        }

        application.Status = ApplicationStatus.Applied;
        application.ShortlistedAt = null;
        application.UpdatedAt = now;
    }

    /// <summary>
    /// Closes an open application because the job it was for is no longer running —
    /// cancelled by the employer, or expired by the sweep. Only the states the
    /// transition table allows are moved, so a terminal application is left untouched.
    /// </summary>
    public static void Expire(JobApplication application)
    {
        if (!ApplicationStatusTransitions.CanTransition(application.Status, ApplicationStatus.Expired))
        {
            return;
        }

        application.Status = ApplicationStatus.Expired;
    }

    public static string Label(ApplicationStatus status) =>
        status switch
        {
            ApplicationStatus.Applied => "applied",
            ApplicationStatus.Viewed => "viewed",
            ApplicationStatus.Shortlisted => "shortlisted",
            ApplicationStatus.Accepted => "selected",
            ApplicationStatus.Rejected => "rejected",
            ApplicationStatus.Withdrawn => "withdrawn",
            ApplicationStatus.Expired => "expired",
            _ => status.ToString().ToLowerInvariant()
        };
}
