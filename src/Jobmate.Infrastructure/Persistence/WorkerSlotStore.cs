using Jobmate.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="IWorkerSlotStore"/>.
///
/// The capacity check and the increment are one statement, so the database decides
/// who wins the last slot. Under PostgreSQL's read-committed isolation a concurrent
/// <c>UPDATE</c> on the same row blocks on the row lock and then re-evaluates its
/// <c>WHERE</c> clause against the committed row, which is exactly the behaviour needed
/// here: the loser matches zero rows instead of over-filling the job.
/// </summary>
public sealed class WorkerSlotStore(AppDbContext db) : IWorkerSlotStore
{
    public async Task<bool> TryReserveSlotAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // The status literals are the strings EF writes for the enum, matching
        // SelectionRules.EnsureCanSelect. Every hole in an interpolated statement is a
        // parameter, so the predicate is written out rather than composed.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE jobmate.jobs
             SET workers_filled = workers_filled + 1,
                 updated_at = {DateTime.UtcNow}
             WHERE job_id = {jobId}
               AND status IN ('Active', 'Pending')
               AND workers_filled < workers_needed
             """,
            cancellationToken);

        return affected == 1;
    }
}
