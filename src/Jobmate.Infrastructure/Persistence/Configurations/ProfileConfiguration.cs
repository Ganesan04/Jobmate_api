using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class WorkerProfileConfiguration : IEntityTypeConfiguration<WorkerProfile>
{
    public void Configure(EntityTypeBuilder<WorkerProfile> builder)
    {
        builder.ToTable("worker_profiles");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.Bio).HasColumnName("bio").HasMaxLength(2000);
        builder.Property(p => p.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
        builder.Property(p => p.Gender).HasColumnName("gender").HasMaxLength(40);
        builder.Property(p => p.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(p => p.Latitude).HasColumnName("latitude");
        builder.Property(p => p.Longitude).HasColumnName("longitude");
        builder.Property(p => p.Experience).HasColumnName("experience").HasMaxLength(1000);
        builder.Property(p => p.Availability).HasColumnName("availability").HasMaxLength(200);
        builder.Property(p => p.ExpectedPay).HasColumnName("expected_pay").HasPrecision(12, 2);
        builder.Property(p => p.ExpectedPayType).HasColumnName("expected_pay_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.VerificationStatus).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.AverageRating).HasColumnName("average_rating").HasPrecision(3, 2).HasDefaultValue(0m);
        builder.Property(p => p.RatingCount).HasColumnName("rating_count").HasDefaultValue(0);
        builder.Property(p => p.TotalCompletedJobs).HasColumnName("total_completed_jobs").HasDefaultValue(0);
        builder.Property(p => p.TotalApplications).HasColumnName("total_applications").HasDefaultValue(0);
        builder.Property(p => p.IsAvailable).HasColumnName("is_available").HasDefaultValue(true);
        builder.Property(p => p.IsOnline).HasColumnName("is_online").HasDefaultValue(false);
        builder.Property(p => p.LastActiveAt).HasColumnName("last_active_at").HasColumnType("timestamptz");

        builder.HasOne(p => p.User)
            .WithOne(u => u.WorkerProfile)
            .HasForeignKey<WorkerProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_worker_profiles_user_id");

        // The worker discovery screen filters on these, so they are indexed together.
        builder.HasIndex(p => new { p.IsAvailable, p.IsOnline }).HasDatabaseName("ix_worker_profiles_availability");
        builder.HasIndex(p => p.VerificationStatus).HasDatabaseName("ix_worker_profiles_verification");
    }
}

public sealed class WorkerSkillConfiguration : IEntityTypeConfiguration<WorkerSkill>
{
    public void Configure(EntityTypeBuilder<WorkerSkill> builder)
    {
        builder.ToTable("worker_skills");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(s => s.WorkerProfileId).HasColumnName("worker_profile_id").IsRequired();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(60).IsRequired();

        builder.HasOne(s => s.WorkerProfile)
            .WithMany(p => p.Skills)
            .HasForeignKey(s => s.WorkerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per skill name per worker, so adding a skill twice is a no-op.
        builder.HasIndex(s => new { s.WorkerProfileId, s.Name })
            .IsUnique()
            .HasDatabaseName("ix_worker_skills_profile_name");

        // Skill search on the worker list.
        builder.HasIndex(s => s.Name).HasDatabaseName("ix_worker_skills_name");
    }
}

public sealed class WorkerDocumentConfiguration : IEntityTypeConfiguration<WorkerDocument>
{
    public void Configure(EntityTypeBuilder<WorkerDocument> builder)
    {
        builder.ToTable("worker_documents");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(d => d.WorkerProfileId).HasColumnName("worker_profile_id").IsRequired();
        builder.Property(d => d.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(d => d.Url).HasColumnName("url").HasMaxLength(500);
        builder.Property(d => d.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz");
        builder.Property(d => d.ReviewedByUserId).HasColumnName("reviewed_by_user_id");

        builder.HasOne(d => d.WorkerProfile)
            .WithMany(p => p.Documents)
            .HasForeignKey(d => d.WorkerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.Status).HasDatabaseName("ix_worker_documents_status");
    }
}

public sealed class WorkerAvailabilitySlotConfiguration : IEntityTypeConfiguration<WorkerAvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<WorkerAvailabilitySlot> builder)
    {
        builder.ToTable("worker_availability_slots", t =>
            // Matches the app's WorkDay numbering: 1 = Monday ... 7 = Sunday.
            t.HasCheckConstraint(
                "chk_worker_availability_slots_day_of_week",
                "\"day_of_week\" BETWEEN 1 AND 7"));
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(s => s.WorkerProfileId).HasColumnName("worker_profile_id").IsRequired();
        builder.Property(s => s.DayOfWeek).HasColumnName("day_of_week").IsRequired();
        builder.Property(s => s.StartTime).HasColumnName("start_time").HasColumnType("time");
        builder.Property(s => s.EndTime).HasColumnName("end_time").HasColumnType("time");
        builder.Property(s => s.IsAvailable).HasColumnName("is_available").HasDefaultValue(true);

        builder.HasOne(s => s.WorkerProfile)
            .WithMany(p => p.AvailabilitySlots)
            .HasForeignKey(s => s.WorkerProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.WorkerProfileId, s.DayOfWeek })
            .HasDatabaseName("ix_worker_availability_slots_profile_day");
    }
}

public sealed class GiverProfileConfiguration : IEntityTypeConfiguration<GiverProfile>
{
    public void Configure(EntityTypeBuilder<GiverProfile> builder)
    {
        builder.ToTable("giver_profiles");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.BusinessName).HasColumnName("business_name").HasMaxLength(150).IsRequired();
        builder.Property(p => p.BusinessType).HasColumnName("business_type").HasMaxLength(100);
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(p => p.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(p => p.Latitude).HasColumnName("latitude");
        builder.Property(p => p.Longitude).HasColumnName("longitude");
        builder.Property(p => p.ContactPerson).HasColumnName("contact_person").HasMaxLength(120);
        builder.Property(p => p.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20);
        builder.Property(p => p.GstNumber).HasColumnName("gst_number").HasMaxLength(20);
        builder.Property(p => p.VerificationStatus).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.AverageRating).HasColumnName("average_rating").HasPrecision(3, 2).HasDefaultValue(0m);
        builder.Property(p => p.RatingCount).HasColumnName("rating_count").HasDefaultValue(0);
        builder.Property(p => p.TotalJobsPosted).HasColumnName("total_jobs_posted").HasDefaultValue(0);
        builder.Property(p => p.TotalHired).HasColumnName("total_hired").HasDefaultValue(0);

        builder.HasOne(p => p.User)
            .WithOne(u => u.GiverProfile)
            .HasForeignKey<GiverProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ix_giver_profiles_user_id");
        builder.HasIndex(p => p.VerificationStatus).HasDatabaseName("ix_giver_profiles_verification");
    }
}
