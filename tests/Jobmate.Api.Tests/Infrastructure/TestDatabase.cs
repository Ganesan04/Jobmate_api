using System.Text.Json;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Jobmate.Api.Tests.Infrastructure;

/// <summary>
/// Resolves the PostgreSQL database the integration tests run against.
///
/// The tests exercise real SQL — the distance expression, the grouped counts and the
/// <c>LIKE</c> search are all provider-specific — so an in-memory provider would prove
/// nothing. To keep the development database safe, tests only ever run against
/// <c>jobmate_test</c>, and every destructive step refuses to run against any other
/// name.
/// </summary>
public static class TestDatabase
{
    public const string DatabaseName = "jobmate_test";

    /// <summary>Overrides every default; the only supported way to point the tests elsewhere.</summary>
    public const string ConnectionStringVariable = "JOBMATE_TEST_CONNECTION";

    private const string ApiUserSecretsId = "jobmate-api-b7f1c2d4-3e6a-4a19-9c05-2f8d1e7b4a63";

    private const string DevelopmentConnectionKey = "ConnectionStrings:WriteConnection";

    private static string? _resolved;

    private static readonly SemaphoreSlim ReadyLock = new(1, 1);

    private static bool _ready;

    /// <summary>The test connection string, derived once per test run.</summary>
    public static string ConnectionString => _resolved ??= BuildConnectionString();

    public static NpgsqlConnectionStringBuilder Builder => new(ConnectionString);

    /// <summary>
    /// Creates the test database if it is missing and brings it up to the latest
    /// migration, at most once per test run.
    ///
    /// The schema has to be in place before the host is built, because <c>Program.cs</c>
    /// runs the demo seeder while the host starts. That makes this step a
    /// prerequisite of host construction rather than a fixture hook, which xUnit is not
    /// obliged to call a fixed number of times, hence the lock and the flag.
    /// </summary>
    public static async Task EnsureReadyAsync()
    {
        if (_ready)
        {
            return;
        }

        await ReadyLock.WaitAsync();

        try
        {
            if (_ready)
            {
                return;
            }

            await CreateDatabaseIfMissingAsync();

            // Its own context rather than the host's, because the host does not exist yet.
            var builder = new DbContextOptionsBuilder<AppDbContext>();

            PersistenceConfiguration.Configure(builder, ConnectionString);

            await using var db = new AppDbContext(builder.Options);

            await db.Database.MigrateAsync();

            _ready = true;
        }
        finally
        {
            ReadyLock.Release();
        }
    }

    /// <summary>
    /// Empties every table in the schema so each test class starts from a known state.
    /// The migration history table is left alone, and the guard below means this can
    /// only ever run against the test database.
    /// </summary>
    public static async Task ResetAsync(IServiceProvider services)
    {
        var builder = Builder;

        if (!string.Equals(builder.Database, DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to truncate '{builder.Database}'. Integration tests may only reset '{DatabaseName}'.");
        }

        var db = services.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE
                statements text;
            BEGIN
                SELECT string_agg(format('TRUNCATE TABLE %I.%I RESTART IDENTITY CASCADE', schemaname, tablename), '; ')
                INTO statements
                FROM pg_tables
                WHERE schemaname = 'jobmate' AND tablename <> '__ef_migrations_history';

                IF statements IS NOT NULL THEN
                    EXECUTE statements;
                END IF;
            END $$;
            """);
    }

    /// <summary>Drops the test database. Only ever called when a run is explicitly asked to clean up.</summary>
    public static async Task DropAsync()
    {
        var builder = Builder;

        if (!string.Equals(builder.Database, DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to drop '{builder.Database}'. Integration tests may only drop '{DatabaseName}'.");
        }

        await using var connection = new Npgsql.NpgsqlConnection(ToPostgresDatabase(builder));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}' AND pid <> pg_backend_pid();
            DROP DATABASE IF EXISTS "{DatabaseName}";
            """;

        await command.ExecuteNonQueryAsync();

        // The next EnsureReadyAsync has to rebuild the schema.
        _ready = false;
    }

    private static async Task CreateDatabaseIfMissingAsync()
    {
        var builder = Builder;

        await using var connection = new Npgsql.NpgsqlConnection(ToPostgresDatabase(builder));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{DatabaseName}'";

        if (await command.ExecuteScalarAsync() is not null)
        {
            return;
        }

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{DatabaseName}\"";

        await create.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The maintenance connection, pointed at the always-present <c>postgres</c>
    /// database so the test database itself can be created.
    /// </summary>
    private static string ToPostgresDatabase(NpgsqlConnectionStringBuilder builder)
    {
        builder.Database = "postgres";

        return builder.ConnectionString;
    }

    private static string BuildConnectionString()
    {
        if (Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } fromEnvironment)
        {
            return Guard(fromEnvironment);
        }

        var developmentConnection = ReadDevelopmentConnectionString()
                                    ?? throw new InvalidOperationException(
                                        $"No connection string for the tests. Set {ConnectionStringVariable} or " +
                                        "configure the API project's user secrets.");

        var builder = new NpgsqlConnectionStringBuilder(developmentConnection)
        {
            Database = DatabaseName
        };

        return Guard(builder.ConnectionString);
    }

    private static string Guard(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (!string.Equals(builder.Database, DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The test connection string must target '{DatabaseName}', not '{builder.Database}'.");
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Reads the API project's development connection string out of user secrets. The
    /// value never leaves this process; only the database name is changed.
    /// </summary>
    private static string? ReadDevelopmentConnectionString()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            return null;
        }

        var path = Path.Combine(appData, "Microsoft", "UserSecrets", ApiUserSecretsId, "secrets.json");

        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.TryGetProperty(DevelopmentConnectionKey, out var value)
            ? value.GetString()
            : null;
    }
}
