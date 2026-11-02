using System.Globalization;
using System.Security.Cryptography;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// Rules for turning a shortlisted application into a confirmed booking. The
/// capacity check is the domain half of the protection against over-selection; the
/// serialisable transaction plus the unique index in Infrastructure is the other half.
/// </summary>
public static class SelectionRules
{
    public static WorkerAssignment CreateAssignment(
        Job job,
        JobApplication application,
        int agreedPay,
        DateTime now)
    {
        EnsureCanSelect(job, application, agreedPay);

        return new WorkerAssignment
        {
            JobId = job.Id,
            WorkerId = application.WorkerId,
            GiverId = job.GiverId,
            ApplicationId = application.Id,
            AgreedPay = agreedPay,
            Status = AssignmentStatus.Pending,
            ConfirmedAt = now
        };
    }

    public static void EnsureCanSelect(Job job, JobApplication application, int agreedPay)
    {
        if (application.JobId != job.Id)
        {
            throw new BusinessRuleException("This application belongs to a different job.", "ApplicationJobMismatch");
        }

        // One account can work and hire, so a giver could otherwise book themselves onto
        // their own job. Applying is already refused; this covers a row that predates that.
        if (application.WorkerId == job.GiverId)
        {
            throw new BusinessRuleException("You cannot hire yourself for your own job.", "OwnJob");
        }

        if (application.Status is not (ApplicationStatus.Applied or ApplicationStatus.Viewed or ApplicationStatus.Shortlisted))
        {
            throw new BusinessRuleException(
                $"A {ApplicationRules.Label(application.Status)} application cannot be selected.",
                "InvalidApplicationTransition");
        }

        if (job.Status is not (JobStatus.Active or JobStatus.Pending))
        {
            throw new BusinessRuleException(
                $"A {JobLifecycle.Label(job.Status)} job cannot take new workers.",
                "JobNotSelectable");
        }

        if (job.WorkersFilled >= job.WorkersNeeded)
        {
            throw new WorkerLimitReachedException(job.WorkersNeeded, job.WorkersFilled);
        }

        if (agreedPay <= 0)
        {
            throw new BusinessRuleException("Agreed pay must be greater than zero.", "PayInvalid");
        }

        if (agreedPay > MaxReasonablePay)
        {
            throw new BusinessRuleException(
                $"Agreed pay cannot exceed {MaxReasonablePay:N0}.",
                "PayInvalid");
        }
    }

    /// <summary>Upper guard rail against a fat-finger pay entry, in whole rupees.</summary>
    public const int MaxReasonablePay = 1_000_000;

    public static void CheckIn(WorkerAssignment assignment, string? code, DateTime now)
    {
        if (assignment.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"A {assignment.Status.ToString().ToLowerInvariant()} booking cannot be checked in.",
                "AssignmentClosed");
        }

        if (assignment.CheckedInAt is not null)
        {
            throw new BusinessRuleException("You have already checked in for this job.", "AlreadyCheckedIn");
        }

        if (string.IsNullOrWhiteSpace(assignment.CheckInCode))
        {
            // No code has ever been issued for this booking, so "wrong code" would send
            // the worker hunting for a typo instead of telling them the job is not running.
            throw new BusinessRuleException(
                "This job has not started yet, so there is no check-in code.",
                "JobNotStarted");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 4 || !code.Trim().All(char.IsDigit))
        {
            throw new BusinessRuleException("Enter the 4-digit check-in code.", "CheckInCodeInvalid");
        }

        if (!string.Equals(assignment.CheckInCode?.Trim(), code.Trim(), StringComparison.Ordinal))
        {
            throw new BusinessRuleException("Incorrect check-in code.", "CheckInCodeInvalid");
        }

        assignment.CheckedInAt = now;
        assignment.Status = AssignmentStatus.CheckedIn;

        // A single-use code is cleared so the same code cannot be replayed.
        assignment.CheckInCode = null;
    }

    public static void Complete(WorkerAssignment assignment, DateTime now)
    {
        if (assignment.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"A {assignment.Status.ToString().ToLowerInvariant()} booking cannot be completed.",
                "AssignmentClosed");
        }

        assignment.Status = AssignmentStatus.Completed;
        assignment.CompletedAt = now;
    }

    public static void Activate(WorkerAssignment assignment)
    {
        if (assignment.Status == AssignmentStatus.Pending)
        {
            assignment.Status = AssignmentStatus.Active;
        }
    }

    /// <summary>
    /// Issues the 4-digit code a worker enters to prove they turned up, and returns it so
    /// the giver can read it out.
    ///
    /// The code is drawn from a cryptographic source rather than <see cref="Random"/>,
    /// because it is the only thing standing between "a booking exists" and "this worker
    /// really is on site": a predictable code would let one worker check in on another's
    /// behalf. Four digits keeps it typeable on a work site while leaving 10,000
    /// possibilities, which is why it is single use — <see cref="CheckIn"/> clears it.
    /// </summary>
    public static string IssueCheckInCode(WorkerAssignment assignment, DateTime now)
    {
        if (assignment.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled)
        {
            throw new BusinessRuleException(
                $"A {assignment.Status.ToString().ToLowerInvariant()} booking cannot be issued a check-in code.",
                "AssignmentClosed");
        }

        var code = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture);

        assignment.CheckInCode = code;
        assignment.UpdatedAt = now;

        return code;
    }

    public static void Cancel(WorkerAssignment assignment, string? reason)
    {
        if (assignment.Status == AssignmentStatus.Completed)
        {
            throw new BusinessRuleException("A completed booking cannot be cancelled.", "AssignmentClosed");
        }

        assignment.Status = AssignmentStatus.Cancelled;
        assignment.CheckInCode = null;
        _ = reason;
    }

    public static string CheckInCodeFor(Job job, Guid assignmentId)
    {
        var assignment = job.Assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
        {
            throw new NotFoundException("Booking not found for this job.");
        }

        return assignment.CheckInCode ?? throw new BusinessRuleException(
            "No check-in code has been issued for this booking yet.",
            "CheckInCodeMissing");
    }
}
