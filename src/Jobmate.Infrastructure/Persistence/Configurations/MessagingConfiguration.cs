using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobmate.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(c => c.JobId).HasColumnName("job_id");
        builder.Property(c => c.LastMessageAt).HasColumnName("last_message_at").HasColumnType("timestamptz");
        builder.Property(c => c.LastMessagePreview).HasColumnName("last_message_preview").HasMaxLength(200);
        builder.Property(c => c.LastMessageSenderId).HasColumnName("last_message_sender_id").IsRequired();

        builder.HasOne(c => c.Job)
            .WithMany()
            .HasForeignKey(c => c.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.JobId).HasDatabaseName("ix_conversations_job_id");
    }
}

public sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(p => p.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.UnreadCount).HasColumnName("unread_count").HasDefaultValue(0);
        builder.Property(p => p.LastReadAt).HasColumnName("last_read_at").HasColumnType("timestamptz");
        builder.Property(p => p.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);

        builder.HasOne(p => p.Conversation)
            .WithMany(c => c.Participants)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A user appears in a thread once, and the chat list is read per user, so the
        // pair is unique and indexed.
        builder.HasIndex(p => new { p.ConversationId, p.UserId })
            .IsUnique()
            .HasDatabaseName("ix_conversation_participants_conversation_user");

        builder.HasIndex(p => new { p.UserId, p.IsArchived, p.UnreadCount })
            .HasDatabaseName("ix_conversation_participants_user_unread");
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(m => m.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(m => m.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(m => m.MessageText).HasColumnName("message_text").HasMaxLength(4000).IsRequired();
        builder.Property(m => m.MessageType).HasColumnName("message_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(m => m.SentAt).HasColumnName("sent_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(m => m.ReadAt).HasColumnName("read_at").HasColumnType("timestamptz");

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chat history is always read newest-last for one thread, so the composite
        // index matches that access pattern and keeps paging stable.
        builder.HasIndex(m => new { m.ConversationId, m.SentAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_conversation_sent");
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(n => n.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(150).IsRequired();
        builder.Property(n => n.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(n => n.ReferenceType).HasColumnName("reference_type").HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.ReferenceId).HasColumnName("reference_id");
        builder.Property(n => n.Route).HasColumnName("route").HasMaxLength(200);
        builder.Property(n => n.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(n => n.ReadAt).HasColumnName("read_at").HasColumnType("timestamptz");
        builder.Property(n => n.PushDispatchedAt).HasColumnName("push_dispatched_at").HasColumnType("timestamptz");

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The notification list and the unread badge both filter by user, newest first.
        builder.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_notifications_user_read_created");
    }
}

public sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("user_devices");
        builder.HasJobmateKey();
        builder.HasJobmateAuditColumns();

        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(d => d.Token).HasColumnName("token").HasMaxLength(512).IsRequired();
        builder.Property(d => d.Platform).HasColumnName("platform").HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.DeviceName).HasColumnName("device_name").HasMaxLength(120);
        builder.Property(d => d.AppVersion).HasColumnName("app_version").HasMaxLength(30);
        builder.Property(d => d.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(d => d.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamptz");

        builder.HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per push token, so a re-registration updates rather than duplicates.
        builder.HasIndex(d => d.Token).IsUnique().HasDatabaseName("ix_user_devices_token");
        builder.HasIndex(d => new { d.UserId, d.IsActive }).HasDatabaseName("ix_user_devices_user_active");
    }
}
