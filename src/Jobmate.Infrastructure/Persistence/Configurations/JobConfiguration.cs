using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs", t =>
        {
            // A job cannot be posted for zero or negative workers, and the filled
            // count is maintained server-side and can never be negative.
            t.HasCheckConstraint("ck_jobs_workers_needed", "\"workers_needed\" > 0");
            t.HasCheckConstraint("ck_jobs_workers_filled", "\"workers_filled\" >= 0");
        });
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(j => j.GiverId).HasColumnName("giver_id").IsRequired();
        builder.Property(j => j.Company).HasColumnName("company").HasMaxLength(150).IsRequired();
        builder.Property(j => j.Title).HasColumnName("title").HasMaxLength(150).IsRequired();
        builder.Property(j => j.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(j => j.Description).HasColumnName("description").HasMaxLength(4000).IsRequired();
        builder.Property(j => j.WorkersNeeded).HasColumnName("workers_needed").HasDefaultValue(1);
        builder.Property(j => j.WorkersFilled).HasColumnName("workers_filled").HasDefaultValue(0);
        builder.Property(j => j.Experience).HasColumnName("experience").HasMaxLength(500);

        // The date plus three TimeOnly values form the shift, so PostgreSQL time
        // columns compare and sort correctly instead of being opaque strings.
        builder.Property(j => j.JobDate).HasColumnName("job_date").HasColumnType("date").IsRequired();
        builder.Property(j => j.StartTime).HasColumnName("start_time").HasColumnType("time").IsRequired();
        builder.Property(j => j.EndTime).HasColumnName("end_time").HasColumnType("time").IsRequired();
        builder.Property(j => j.ReportTime).HasColumnName("report_time").HasColumnType("time").IsRequired();
        builder.Property(j => j.BreakMinutes).HasColumnName("break_minutes").HasDefaultValue(30);
        builder.Property(j => j.JobType).HasColumnName("job_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.IsRecurring).HasColumnName("is_recurring").HasDefaultValue(false);

        // The DTO exposes the weekday list, so the property is kept and the same
        // values are mirrored into JobWeekdays for querying. Kept in sync by the
        // job commands, never written directly by a client.
        var weekdays = builder.Property(j => j.Weekdays)
            .HasColumnName("weekdays")
            .HasColumnType("integer[]")
            .HasConversion(
                v => v.ToArray(),
                v => v.ToList());

        // Without an explicit comparer EF compares the converted reference and would
        // report a spurious change on every load; sequence equality is the intent.
        weekdays.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<int>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToList()));

        builder.Property(j => j.Pay).HasColumnName("pay").IsRequired();
        builder.Property(j => j.PayType).HasColumnName("pay_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.OvertimeRate).HasColumnName("overtime_rate");
        builder.Property(j => j.TravelAllowance).HasColumnName("travel_allowance").HasDefaultValue(0);
        builder.Property(j => j.FoodAllowance).HasColumnName("food_allowance").HasDefaultValue(0);
        builder.Property(j => j.PaymentDate).HasColumnName("payment_date").HasColumnType("date");
        builder.Property(j => j.PaymentMethod).HasColumnName("payment_method").HasConversion<string>().HasMaxLength(20);

        builder.Property(j => j.Area).HasColumnName("area").HasMaxLength(120).IsRequired();
        builder.Property(j => j.Address).HasColumnName("address").HasMaxLength(400);
        builder.Property(j => j.Latitude).HasColumnName("latitude");
        builder.Property(j => j.Longitude).HasColumnName("longitude");
        builder.Property(j => j.WorkplaceType).HasColumnName("workplace_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.LocationVisible).HasColumnName("location_visible").HasDefaultValue(true);

        builder.Property(j => j.ContactPerson).HasColumnName("contact_person").HasMaxLength(120);
        builder.Property(j => j.SafetyInformation).HasColumnName("safety_information").HasMaxLength(1000);
        builder.Property(j => j.PhysicalRequirements).HasColumnName("physical_requirements").HasMaxLength(500);
        builder.Property(j => j.DressCode).HasColumnName("dress_code").HasMaxLength(200);
        builder.Property(j => j.VehicleRequired).HasColumnName("vehicle_required").HasDefaultValue(false);
        builder.Property(j => j.EmergencyContact).HasColumnName("emergency_contact").HasMaxLength(200);
        builder.Property(j => j.IsUrgent).HasColumnName("is_urgent").HasDefaultValue(false);

        builder.Property(j => j.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.ApplicationDeadline).HasColumnName("application_deadline").HasColumnType("date");
        builder.Property(j => j.PublishedAt).HasColumnName("published_at").HasColumnType("timestamptz");
        builder.Property(j => j.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        builder.Property(j => j.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(j => j.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz");

        builder.HasOne(j => j.Giver)
            .WithMany()
            .HasForeignKey(j => j.GiverId)
            .OnDelete(DeleteBehavior.Restrict);

        // The job list and the "my jobs" tabs are the hottest queries in the app:
        // giver-scoped lists, and active jobs ordered by shift date.
        builder.HasIndex(j => new { j.GiverId, j.Status }).HasDatabaseName("ix_jobs_giver_status");
        builder.HasIndex(j => new { j.Status, j.JobDate }).HasDatabaseName("ix_jobs_status_date");
        builder.HasIndex(j => new { j.Category, j.Status, j.JobDate }).HasDatabaseName("ix_jobs_category_status_date");
        builder.HasIndex(j => j.Area).HasDatabaseName("ix_jobs_area");
    }
}

public sealed class JobSkillConfiguration : IEntityTypeConfiguration<JobSkill>
{
    public void Configure(EntityTypeBuilder<JobSkill> builder)
    {
        builder.ToTable("job_skills");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(s => s.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(60).IsRequired();

        builder.HasOne(s => s.Job)
            .WithMany(j => j.Skills)
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.JobId, s.Name }).IsUnique().HasDatabaseName("ix_job_skills_job_name");
        builder.HasIndex(s => s.Name).HasDatabaseName("ix_job_skills_name");
    }
}

public sealed class JobWeekdayConfiguration : IEntityTypeConfiguration<JobWeekday>
{
    public void Configure(EntityTypeBuilder<JobWeekday> builder)
    {
        builder.ToTable("job_weekdays", t =>
            // Matches the app's WorkDay numbering: 1 = Monday ... 7 = Sunday.
            t.HasCheckConstraint("ck_job_weekdays_day_of_week", "\"day_of_week\" BETWEEN 1 AND 7"));
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(d => d.JobId).HasColumnName("job_id").IsRequired();
        builder.Property(d => d.DayOfWeek).HasColumnName("day_of_week").IsRequired();

        builder.HasOne(d => d.Job)
            .WithMany(j => j.WeekdayRows)
            .HasForeignKey(d => d.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.JobId, d.DayOfWeek })
            .IsUnique()
            .HasDatabaseName("ix_job_weekdays_job_day");
    }
}
