using System.Reflection;
using Jobmate.Application.Common;
using Jobmate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// The application's <see cref="DbContext"/>. Entity shape lives in
/// <c>Configurations</c> (one file per aggregate area) and cross-cutting concerns
/// (audit stamping, soft deletes, concurrency) are interceptors, so neither leaks
/// into the Domain or Application layers.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public const string Schema = "jobmate";

    public DbSet<Domain.Entities.User> Users => Set<Domain.Entities.User>();
    public DbSet<Domain.Entities.RefreshToken> RefreshTokens => Set<Domain.Entities.RefreshToken>();
    public DbSet<Domain.Entities.OtpChallenge> OtpChallenges => Set<Domain.Entities.OtpChallenge>();
    public DbSet<Domain.Entities.PasswordResetToken> PasswordResetTokens => Set<Domain.Entities.PasswordResetToken>();
    public DbSet<Domain.Entities.WorkerProfile> WorkerProfiles => Set<Domain.Entities.WorkerProfile>();
    public DbSet<Domain.Entities.WorkerSkill> WorkerSkills => Set<Domain.Entities.WorkerSkill>();
    public DbSet<Domain.Entities.WorkerDocument> WorkerDocuments => Set<Domain.Entities.WorkerDocument>();
    public DbSet<Domain.Entities.WorkerAvailabilitySlot> WorkerAvailabilitySlots => Set<Domain.Entities.WorkerAvailabilitySlot>();
    public DbSet<Domain.Entities.GiverProfile> GiverProfiles => Set<Domain.Entities.GiverProfile>();
    public DbSet<Domain.Entities.Job> Jobs => Set<Domain.Entities.Job>();
    public DbSet<Domain.Entities.JobSkill> JobSkills => Set<Domain.Entities.JobSkill>();
    public DbSet<Domain.Entities.JobWeekday> JobWeekdays => Set<Domain.Entities.JobWeekday>();
    public DbSet<Domain.Entities.JobApplication> JobApplications => Set<Domain.Entities.JobApplication>();
    public DbSet<Domain.Entities.WorkerAssignment> WorkerAssignments => Set<Domain.Entities.WorkerAssignment>();
    public DbSet<Domain.Entities.Payment> Payments => Set<Domain.Entities.Payment>();
    public DbSet<Domain.Entities.Rating> Ratings => Set<Domain.Entities.Rating>();
    public DbSet<Domain.Entities.Conversation> Conversations => Set<Domain.Entities.Conversation>();
    public DbSet<Domain.Entities.ConversationParticipant> ConversationParticipants => Set<Domain.Entities.ConversationParticipant>();
    public DbSet<Domain.Entities.Message> Messages => Set<Domain.Entities.Message>();
    public DbSet<Domain.Entities.Notification> Notifications => Set<Domain.Entities.Notification>();
    public DbSet<Domain.Entities.UserDevice> UserDevices => Set<Domain.Entities.UserDevice>();
    public DbSet<Domain.Entities.VerificationRequest> VerificationRequests => Set<Domain.Entities.VerificationRequest>();
    public DbSet<Domain.Entities.Dispute> Disputes => Set<Domain.Entities.Dispute>();
    public DbSet<Domain.Entities.SavedJob> SavedJobs => Set<Domain.Entities.SavedJob>();
    public DbSet<Domain.Entities.SupportReport> SupportReports => Set<Domain.Entities.SupportReport>();

    public bool HasActiveTransaction => Database.CurrentTransaction is not null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        SchemaConventions.Apply(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (HasActiveTransaction)
        {
            return await operation(cancellationToken);
        }

        // The configured retrying strategy owns the transaction, so a transient failure
        // re-runs the whole unit of work instead of leaving a half-applied one behind.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            state: (Context: this, Operation: operation),
            operation: async (context, state, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);

                var result = await state.Operation(token);

                await transaction.CommitAsync(token);

                return result;
            },
            verifySucceeded: null,
            cancellationToken);
    }

    /// <summary>
    /// Stamps audit columns on every save. Registered as an interceptor rather than
    /// overridden in <c>SaveChangesAsync</c> so the same logic applies to
    /// <c>SaveChanges</c> and to bulk paths, and so it is covered by the same tests.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();

        return base.SaveChanges();
    }

    /// <summary>
    /// Sets <c>CreatedAt</c> on insert and <c>UpdatedAt</c> on every insert or update,
    /// so no handler has to remember to. A single clock read is used for the whole
    /// unit of work, which keeps a batch of rows internally consistent.
    /// </summary>
    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }
    }
}
