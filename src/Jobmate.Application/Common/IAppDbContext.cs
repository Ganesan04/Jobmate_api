using System.Linq.Expressions;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Jobmate.Application.Common;

/// <summary>
/// The persistence surface the Application layer is allowed to use. Implemented by
/// the Infrastructure <c>AppDbContext</c>; it exposes sets and an explicit
/// transaction factory rather than leaking <c>DbContext</c> or concrete repositories.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<WorkerProfile> WorkerProfiles { get; }
    DbSet<WorkerSkill> WorkerSkills { get; }
    DbSet<WorkerDocument> WorkerDocuments { get; }
    DbSet<WorkerAvailabilitySlot> WorkerAvailabilitySlots { get; }
    DbSet<GiverProfile> GiverProfiles { get; }
    DbSet<Job> Jobs { get; }
    DbSet<JobSkill> JobSkills { get; }
    DbSet<JobWeekday> JobWeekdays { get; }
    DbSet<JobApplication> JobApplications { get; }
    DbSet<WorkerAssignment> WorkerAssignments { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Rating> Ratings { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationParticipant> ConversationParticipants { get; }
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<VerificationRequest> VerificationRequests { get; }
    DbSet<SavedJob> SavedJobs { get; }
    DbSet<SupportReport> SupportReports { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an explicit transaction. Operations that touch several rows (worker
    /// selection, payment settlement, job completion) wrap themselves in one so a
    /// partial write can never be observed.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a unit of work as one transaction, retrying it when the provider reports a
    /// transient failure.
    ///
    /// The connection is configured with retry-on-failure, and a retrying execution
    /// strategy refuses a user-initiated transaction: the whole unit has to be handed to
    /// the strategy instead. That is also the right shape for the operations that need
    /// it, because a deadlock or a serialization failure rolls the attempt back and runs
    /// it again from the start rather than surfacing an error to the caller.
    ///
    /// When a transaction is already open the operation simply runs inside it, so
    /// handlers can be composed without nesting.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>True when the context already owns a transaction, so handlers avoid nesting.</summary>
    bool HasActiveTransaction { get; }
}

/// <summary>
/// Reusable query helpers. Only composition helpers live here — the async
/// operators (<c>ToListAsync</c>, <c>AnyAsync</c>, …) are EF Core's own, so
/// nothing in this file shadows them.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>Applies <paramref name="predicate"/> only when it is not null.</summary>
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> source, Expression<Func<T, bool>>? predicate) =>
        predicate is null ? source : source.Where(predicate);

    /// <summary>Orders by a caller-supplied key only when one was supplied.</summary>
    public static IQueryable<T> OrderIf<T, TKey>(
        this IQueryable<T> source,
        Expression<Func<T, TKey>> keySelector,
        bool descending) =>
        descending
            ? source.OrderByDescending(keySelector)
            : source.OrderBy(keySelector);
}

/// <summary>
/// Helpers for the text search boxes.
///
/// <para>
/// Written against <c>EF.Functions.Like</c> rather than a provider-specific helper so
/// the Application layer keeps no reference to Npgsql. Folding the column to lower case
/// is what makes the match case-insensitive, and both sides have to be folded: the pattern
/// is built lower-cased by <see cref="ToLikePattern"/>.
/// </para>
///
/// <para>
/// There is deliberately no <c>Contains</c>-shaped helper. EF Core only translates the
/// handful of methods it recognises, and a friendly extension method in the middle of a
/// <c>Where</c> is not one of them — it fails at translation time with "could not be
/// translated" rather than at compile time. The <c>Like</c> call is therefore inlined at
/// each use site, the same way the job search does it.
/// </para>
/// </summary>
public static class TextSearchExtensions
{
    /// <summary>Escape character passed to <c>EF.Functions.Like</c>.</summary>
    public const string LikeEscape = "\\";

    /// <summary>Builds a lower-cased, escaped contains-pattern for a user-typed term.</summary>
    public static string ToLikePattern(string value) =>
        $"%{value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .ToLowerInvariant()}%";
}
