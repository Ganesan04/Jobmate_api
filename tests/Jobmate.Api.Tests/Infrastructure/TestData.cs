using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.ValueObjects;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobmate.Api.Tests.Infrastructure;

/// <summary>
/// Writes the applicant and booking state a test needs directly, for the cross-cutting
/// Phase 1 cases (privacy, stats, cancellation side effects) whose subject is the job
/// endpoints rather than the application flow itself.
///
/// Phases 2 and 3 test <c>POST /jobs/{id}/apply</c> and worker selection through the
/// API; this stays as fixture setup so those tests do not have to re-post a wizard to
/// get to the state they are about.
/// </summary>
internal static class TestData
{
    public static async Task<Guid> SeedApplicationAsync(
        JobmateApiFactory factory,
        Guid jobId,
        string workerEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workerId = await UserIdAsync(db, workerEmail);

        var existing = await db.JobApplications
            .FirstOrDefaultAsync(a => a.JobId == jobId && a.WorkerId == workerId);

        if (existing is not null)
        {
            return existing.Id;
        }

        var application = new JobApplication
        {
            JobId = jobId,
            WorkerId = workerId,
            Message = "Available from tomorrow and experienced with loading work.",
            Status = ApplicationStatus.Applied,
            AppliedAt = DateTime.UtcNow
        };

        db.JobApplications.Add(application);

        await db.SaveChangesAsync();

        return application.Id;
    }

    /// <summary>
    /// Marks the worker's application as selected, creates the booking and increments
    /// the job's filled count, which is the state selection produces. Returns the
    /// booking so a test can attach a payment to it.
    /// </summary>
    public static async Task<Guid> SeedBookingAsync(
        JobmateApiFactory factory,
        Guid jobId,
        string workerEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workerId = await UserIdAsync(db, workerEmail);

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId)
                  ?? throw new InvalidOperationException($"No job {jobId} to book against.");

        var application = await db.JobApplications
            .FirstOrDefaultAsync(a => a.JobId == jobId && a.WorkerId == workerId);

        if (application is null)
        {
            application = new JobApplication
            {
                JobId = jobId,
                WorkerId = workerId,
                Message = "Available and experienced.",
                Status = ApplicationStatus.Applied,
                AppliedAt = DateTime.UtcNow
            };

            db.JobApplications.Add(application);
        }

        if (application.Status is not (ApplicationStatus.Accepted or ApplicationStatus.Shortlisted or ApplicationStatus.Viewed))
        {
            application.Status = ApplicationStatus.Accepted;
            application.AcceptedAt = DateTime.UtcNow;
        }

        var alreadyBooked = await db.WorkerAssignments
            .FirstOrDefaultAsync(a => a.JobId == jobId && a.WorkerId == workerId);

        if (alreadyBooked is null)
        {
            db.WorkerAssignments.Add(new WorkerAssignment
            {
                JobId = jobId,
                WorkerId = workerId,
                GiverId = job.GiverId,
                ApplicationId = application.Id,
                AgreedPay = job.Pay,
                Status = AssignmentStatus.Pending,
                ConfirmedAt = DateTime.UtcNow,
                // The column is NOT NULL with no database default, so a new assignment
                // has to start its optimistic-concurrency version at zero.
                RowVersion = 0
            });

            job.WorkersFilled = Math.Min(job.WorkersFilled + 1, job.WorkersNeeded);

            await db.SaveChangesAsync();
        }

        return alreadyBooked?.Id
               ?? await db.WorkerAssignments
                   .Where(a => a.JobId == jobId && a.WorkerId == workerId)
                   .Select(a => a.Id)
                   .FirstAsync();
    }

    /// <summary>
    /// Books the worker and records a payment for that booking in the given state.
    /// Phase 4 creates payments through the API; the cancellation refund has to be
    /// tested against money that is already settled, which no Phase 1 endpoint can make.
    /// </summary>
    public static async Task<Guid> SeedPaymentAsync(
        JobmateApiFactory factory,
        Guid jobId,
        string workerEmail,
        PaymentStatus status)
    {
        var assignmentId = await SeedBookingAsync(factory, jobId, workerEmail);

        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignment = await db.WorkerAssignments.FirstAsync(a => a.Id == assignmentId);

        var payment = new Payment
        {
            JobId = jobId,
            WorkerId = assignment.WorkerId,
            GiverId = assignment.GiverId,
            AssignmentId = assignmentId,
            Amount = assignment.AgreedPay,
            PlatformFee = 0,
            TotalAmount = assignment.AgreedPay,
            AgreedPaySnapshot = assignment.AgreedPay,
            Status = status,
            PaidAt = status == PaymentStatus.Paid ? DateTime.UtcNow : null,
            // Unique per giver, so two payments on one job cannot collide.
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        db.Payments.Add(payment);

        await db.SaveChangesAsync();

        return payment.Id;
    }

    private static async Task<Guid> UserIdAsync(AppDbContext db, string email) =>
        await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .FirstAsync();
}
