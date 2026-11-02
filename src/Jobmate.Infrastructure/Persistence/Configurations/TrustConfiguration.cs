using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class VerificationRequestConfiguration : IEntityTypeConfiguration<VerificationRequest>
{
    public void Configure(EntityTypeBuilder<VerificationRequest> builder)
    {
        builder.ToTable("verification_requests");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.DocumentName).HasColumnName("document_name").HasMaxLength(255);
        builder.Property(r => r.DocumentUrl).HasColumnName("document_url").HasMaxLength(500);
        builder.Property(r => r.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(r => r.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz");
        builder.Property(r => r.ReviewedByUserId).HasColumnName("reviewed_by_user_id");

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReviewedBy)
            .WithMany()
            .HasForeignKey(r => r.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user's own request history.
        builder.HasIndex(r => new { r.UserId, r.Type, r.Status })
            .HasDatabaseName("ix_verification_requests_user_type_status");
    }
}

public sealed class SavedJobConfiguration : IEntityTypeConfiguration<SavedJob>
{
    public void Configure(EntityTypeBuilder<SavedJob> builder)
    {
        builder.ToTable("saved_jobs");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.JobId).HasColumnName("job_id").IsRequired();

        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.HasQueryFilter(s => s.DeletedAt == null);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Job)
            .WithMany()
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Bookmarking twice is a no-op, which is what the heart button expects. Removed
        // bookmarks are ignored, so a job can be saved again after it was unsaved.
        builder.HasIndex(s => new { s.UserId, s.JobId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_saved_jobs_user_job");
    }
}

public sealed class SupportReportConfiguration : IEntityTypeConfiguration<SupportReport>
{
    public void Configure(EntityTypeBuilder<SupportReport> builder)
    {
        builder.ToTable("support_reports");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(r => r.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Details).HasColumnName("details").HasMaxLength(2000).IsRequired();
        builder.Property(r => r.RelatedJobId).HasColumnName("related_job_id");
        builder.Property(r => r.IsResolved).HasColumnName("is_resolved").HasDefaultValue(false);
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.IsResolved, r.CreatedAt }).HasDatabaseName("ix_support_reports_resolved_created");
    }
}
