using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("job_applications");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(a => a.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(a => a.WorkerId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(a => a.Message).HasColumnName("message").HasMaxLength(1000);
        builder.Property(a => a.ExpectedPay).HasColumnName("expected_pay");
        builder.Property(a => a.AvailableFrom).HasColumnName("available_from").HasMaxLength(120);
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.AppliedAt).HasColumnName("applied_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.ViewedAt).HasColumnName("viewed_at").HasColumnType("timestamptz");
        builder.Property(a => a.ShortlistedAt).HasColumnName("shortlisted_at").HasColumnType("timestamptz");
        builder.Property(a => a.RejectedAt).HasColumnName("rejected_at").HasColumnType("timestamptz");
        builder.Property(a => a.WithdrawnAt).HasColumnName("withdrawn_at").HasColumnType("timestamptz");
        builder.Property(a => a.AcceptedAt).HasColumnName("accepted_at").HasColumnType("timestamptz");
        builder.Property(a => a.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(a => a.ProposedPay).HasColumnName("proposed_pay");

        builder.HasOne(a => a.Job)
            .WithMany(j => j.Applications)
            .HasForeignKey(a => a.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Worker)
            .WithMany()
            .HasForeignKey(a => a.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        // This is the guarantee that makes double-tapping Apply safe: the second
        // insert is rejected by PostgreSQL, not merely ignored by a handler check.
        builder.HasIndex(a => new { a.JobId, a.WorkerId })
            .IsUnique()
            .HasDatabaseName("ix_job_applications_job_worker");

        // The two applicant lists in the app: "my applications" and "applicants for
        // this job", both filtered by status.
        builder.HasIndex(a => new { a.WorkerId, a.Status }).HasDatabaseName("ix_job_applications_worker_status");
        builder.HasIndex(a => new { a.JobId, a.Status }).HasDatabaseName("ix_job_applications_job_status");
    }
}

public sealed class WorkerAssignmentConfiguration : IEntityTypeConfiguration<WorkerAssignment>
{
    public void Configure(EntityTypeBuilder<WorkerAssignment> builder)
    {
        builder.ToTable("worker_assignments");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(a => a.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(a => a.WorkerId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(a => a.GiverId).HasColumnName("giver_user_id").IsRequired();
        builder.Property(a => a.ApplicationId).HasColumnName("job_application_id").IsRequired();
        builder.Property(a => a.AgreedPay).HasColumnName("agreed_pay").IsRequired();
        builder.Property(a => a.OvertimeAmount).HasColumnName("overtime_amount");
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.CheckInCode).HasColumnName("check_in_code").HasMaxLength(20);
        builder.Property(a => a.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.CheckedInAt).HasColumnName("checked_in_at").HasColumnType("timestamptz");
        builder.Property(a => a.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");
        builder.Property(a => a.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(a => a.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz");
        builder.Property(a => a.PaymentId).HasColumnName("payment_id");

        // uint maps to PostgreSQL bigint, which is what xmin-based concurrency needs.
        builder.Property(a => a.RowVersion)
            .HasColumnName("row_version")
            .HasColumnType("bigint")
            .IsConcurrencyToken();

        builder.HasOne(a => a.Job)
            .WithMany(j => j.Assignments)
            .HasForeignKey(a => a.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Worker)
            .WithMany()
            .HasForeignKey(a => a.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        // WorkerAssignment has no Giver navigation; the giver is reachable through
        // the job, so the foreign key is configured without a navigation.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.GiverId)
            .OnDelete(DeleteBehavior.Restrict);

        // One booking per application, so a worker cannot be double-booked for a job.
        builder.HasOne(a => a.Application)
            .WithMany()
            .HasForeignKey(a => a.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Payment)
            .WithOne(p => p.Assignment)
            .HasForeignKey<WorkerAssignment>(a => a.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.ApplicationId).IsUnique().HasDatabaseName("ix_worker_assignments_application");
        builder.HasIndex(a => new { a.JobId, a.Status }).HasDatabaseName("ix_worker_assignments_job_status");
        builder.HasIndex(a => new { a.WorkerId, a.Status }).HasDatabaseName("ix_worker_assignments_worker_status");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            // The worker net amount is derived, so the database checks the arithmetic.
            t.HasCheckConstraint(
                "chk_payments_total_amount",
                "\"total_amount\" = \"amount\" + \"overtime_amount\" + \"platform_fee\"");
        });
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(p => p.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(p => p.WorkerId).HasColumnName("worker_user_id").IsRequired();
        builder.Property(p => p.GiverId).HasColumnName("giver_user_id").IsRequired();
        builder.Property(p => p.AssignmentId).HasColumnName("worker_assignment_id").IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount").IsRequired();
        builder.Property(p => p.OvertimeAmount).HasColumnName("overtime_amount").HasDefaultValue(0);
        builder.Property(p => p.PlatformFee).HasColumnName("platform_fee").HasDefaultValue(0);
        builder.Property(p => p.TotalAmount).HasColumnName("total_amount").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("INR");
        builder.Property(p => p.PaymentMethod).HasColumnName("payment_method").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.TransactionReference).HasColumnName("transaction_reference").HasMaxLength(120);
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120).IsRequired();
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(p => p.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz");
        builder.Property(p => p.RefundedAt).HasColumnName("refunded_at").HasColumnType("timestamptz");
        builder.Property(p => p.AgreedPaySnapshot).HasColumnName("agreed_pay_snapshot").IsRequired();

        builder.HasOne(p => p.Job)
            .WithMany(j => j.Payments)
            .HasForeignKey(p => p.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Worker)
            .WithMany()
            .HasForeignKey(p => p.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Giver)
            .WithMany()
            .HasForeignKey(p => p.GiverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Assignment)
            .WithOne(a => a.Payment)
            .HasForeignKey<Payment>(p => p.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // One payment per booking, and a retried request from the same giver with the
        // same key returns the original row instead of charging twice.
        builder.HasIndex(p => p.AssignmentId).IsUnique().HasDatabaseName("ix_payments_assignment");
        builder.HasIndex(p => new { p.GiverId, p.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_payments_giver_idempotency");

        // Wallet and earnings-summary screens.
        builder.HasIndex(p => new { p.WorkerId, p.Status, p.CreatedAt })
            .HasDatabaseName("ix_payments_worker_status_created");
        builder.HasIndex(p => new { p.GiverId, p.Status, p.CreatedAt })
            .HasDatabaseName("ix_payments_giver_status_created");
    }
}

public sealed class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.ToTable("ratings", t =>
            t.HasCheckConstraint("chk_ratings_stars", "\"stars\" BETWEEN 1 AND 5"));
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(r => r.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(r => r.FromUserId).HasColumnName("from_user_id").IsRequired();
        builder.Property(r => r.ToUserId).HasColumnName("to_user_id").IsRequired();
        builder.Property(r => r.Stars).HasColumnName("stars").IsRequired();
        builder.Property(r => r.Comment).HasColumnName("comment").HasMaxLength(1000);
        builder.Property(r => r.WorkerAssignmentId).HasColumnName("worker_assignment_id");
        builder.Property(r => r.RatingDirection).HasColumnName("rating_direction").HasConversion<string>().HasMaxLength(20);

        builder.HasOne(r => r.Job)
            .WithMany(j => j.Ratings)
            .HasForeignKey(r => r.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.FromUser)
            .WithMany()
            .HasForeignKey(r => r.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ToUser)
            .WithMany()
            .HasForeignKey(r => r.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A participant can rate a counterpart only once per job.
        builder.HasIndex(r => new { r.JobId, r.FromUserId, r.ToUserId })
            .IsUnique()
            .HasDatabaseName("ix_ratings_job_from_to");

        // The reviews list on a profile.
        builder.HasIndex(r => new { r.ToUserId, r.CreatedAt }).HasDatabaseName("ix_ratings_to_user_created");
    }
}
