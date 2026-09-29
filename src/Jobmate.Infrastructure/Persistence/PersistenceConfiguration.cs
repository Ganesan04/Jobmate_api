using Microsoft.EntityFrameworkCore;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// The provider configuration every <see cref="AppDbContext"/> in every process has to
/// agree on.
///
/// It exists as a single method because the schema and the migrations history table are
/// part of the contract with the database: the application, the design-time factory used
/// by <c>dotnet ef</c> and the integration tests all have to look in the same place. When
/// each of them configured Npgsql separately they drifted, and a context that read the
/// history from a different schema than the one that wrote it re-applied migrations over
/// a database that already had the tables.
/// </summary>
public static class PersistenceConfiguration
{
    /// <summary>Applies the schema, history table and transient-failure retries.</summary>
    /// <param name="builder">The options being built for <see cref="AppDbContext"/>.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder.UseNpgsql(
            connectionString,
            npgsql =>
            {
                // Without this EF records applied migrations in the default schema, which
                // is not the schema the application owns.
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema);

                // Retry on a transient failure (failover, network blip) rather than
                // surfacing a 500. The default is 3 attempts over ~10 seconds.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
    }
}
