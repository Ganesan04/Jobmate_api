using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(u => u.FirstName).HasColumnName("first_name").HasMaxLength(60).IsRequired();
        builder.Property(u => u.LastName).HasColumnName("last_name").HasMaxLength(60).IsRequired();

        // Stored lower-cased by the EmailAddress value object, so the unique index
        // below is effectively case-insensitive and login lookups cannot miss a row.
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(254);

        builder.Property(u => u.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.ProfileImageUrl).HasColumnName("profile_image_url").HasMaxLength(500);
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(u => u.IsVerified).HasColumnName("is_verified").HasDefaultValue(false);
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at").HasColumnType("timestamptz");
        builder.Property(u => u.VerificationStatus).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Area).HasColumnName("area").HasMaxLength(120);

        // Kept as text rather than a geometry column: the app only ever does a
        // bounding-box / haversine comparison, and this keeps the schema portable.
        builder.Property(u => u.Latitude).HasColumnName("latitude").HasMaxLength(32);
        builder.Property(u => u.Longitude).HasColumnName("longitude").HasMaxLength(32);

        // Login looks the user up by either identifier, and registration must reject
        // duplicates, so both columns carry a unique index.
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("email IS NOT NULL").HasDatabaseName("ix_users_email");
        builder.HasIndex(u => u.PhoneNumber).IsUnique().HasDatabaseName("ix_users_phone_number");

        // Supports the admin user list (role + active) and the search-by-name screen.
        builder.HasIndex(u => new { u.Role, u.IsActive }).HasDatabaseName("ix_users_role_active");
        builder.HasIndex(u => u.Area).HasDatabaseName("ix_users_area");

        // A user has at most one profile; enforced here so a hand-rolled insert cannot
        // leave a user with two worker profiles.
        builder.HasOne(u => u.WorkerProfile)
            .WithOne(p => p.User)
            .HasForeignKey<WorkerProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.GiverProfile)
            .WithOne(p => p.User)
            .HasForeignKey<GiverProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
        builder.Property(t => t.RevokedReason).HasColumnName("revoked_reason").HasMaxLength(120);
        builder.Property(t => t.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(t => t.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        builder.HasIndex(t => new { t.UserId, t.RevokedAt }).HasDatabaseName("ix_refresh_tokens_user_revoked");
    }
}

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(c => c.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20).IsRequired();
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
        builder.Property(c => c.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamptz");
        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(c => c.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);

        // Validation loads the newest live challenge for a phone + purpose, and the
        // expiry sweep scans by expiry, so both are indexed.
        builder.HasIndex(c => new { c.PhoneNumber, c.Purpose, c.CreatedAt })
            .HasDatabaseName("ix_otp_challenges_phone_purpose_created");
        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName("ix_otp_challenges_expires");
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamptz");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_password_reset_tokens_token_hash");
    }
}
