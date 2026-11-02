using System.Text.RegularExpressions;
using FluentAssertions;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Jobmate.Infrastructure.Tests.Persistence;

/// <summary>
/// The schema rules in Database/README.md, checked against the real EF model. No database
/// is needed: the model is built in memory, so this runs in every test run and a new table
/// that breaks a rule fails here rather than in review.
/// </summary>
public sealed class SchemaConventionTests : IDisposable
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$");

    private readonly AppDbContext _db;
    private readonly IModel _model;

    public SchemaConventionTests()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        // Never opened: building the model does not touch the server.
        PersistenceConfiguration.Configure(builder, "Host=localhost;Database=unused");

        _db = new AppDbContext(builder.Options);
        _model = _db.GetService<IDesignTimeModel>().Model;
    }

    public void Dispose() => _db.Dispose();

    private IEnumerable<IEntityType> Tables => _model.GetEntityTypes().Where(e => e.GetTableName() is not null);

    [Fact]
    public void The_migrations_match_the_model()
    {
        // Fails when an entity or configuration changed without `dotnet ef migrations add`.
        _db.Database.HasPendingModelChanges().Should().BeFalse(
            "every model change needs a migration (see Database/README.md)");
    }

    [Fact]
    public void Tables_and_columns_are_snake_case()
    {
        var bad = Tables
            .SelectMany(e => e.GetProperties().Select(p => $"{e.GetTableName()}.{p.GetColumnName()}")
                .Prepend(e.GetTableName()!))
            .Where(name => !name.Split('.').All(SnakeCase.IsMatch));

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Primary_keys_are_named_after_the_singular_table()
    {
        var bad = Tables
            .Select(e => (Table: e.GetTableName()!, Key: e.FindPrimaryKey()!))
            .Where(t => t.Key.GetName() != $"pk_{t.Table}" ||
                        t.Key.Properties.Single().GetColumnName() != SchemaConventions.PrimaryKeyColumn(t.Table))
            .Select(t => $"{t.Table}: {t.Key.GetName()} on {t.Key.Properties.Single().GetColumnName()}");

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Foreign_key_columns_end_with_the_key_they_reference()
    {
        var bad = Tables
            .SelectMany(e => e.GetForeignKeys().Select(fk => (Table: e.GetTableName(), Fk: fk)))
            .Where(t => !t.Fk.Properties.Single().GetColumnName()
                .EndsWith(t.Fk.PrincipalKey.Properties.Single().GetColumnName(), StringComparison.Ordinal))
            .Select(t => $"{t.Table}.{t.Fk.Properties.Single().GetColumnName()} -> " +
                         $"{t.Fk.PrincipalEntityType.GetTableName()}.{t.Fk.PrincipalKey.Properties.Single().GetColumnName()}");

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Every_foreign_key_column_is_indexed()
    {
        var bad = Tables
            .SelectMany(e => e.GetForeignKeys().Select(fk => (Entity: e, Fk: fk)))
            .Where(t => !t.Entity.GetIndexes().Any(i => i.Properties[0] == t.Fk.Properties[0]))
            .Select(t => $"{t.Entity.GetTableName()}.{t.Fk.Properties[0].GetColumnName()}");

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Constraints_and_indexes_use_the_standard_prefixes()
    {
        var bad = Tables.SelectMany(e =>
            e.GetForeignKeys().Select(fk => fk.GetConstraintName()!).Where(n => !n.StartsWith("fk_"))
                .Concat(e.GetIndexes().Where(i => !i.GetDatabaseName()!.StartsWith(i.IsUnique ? "ux_" : "ix_"))
                    .Select(i => i.GetDatabaseName()!))
                .Concat(e.GetCheckConstraints().Select(c => c.Name!).Where(n => !n.StartsWith("chk_"))));

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Every_table_has_audit_columns()
    {
        var bad = Tables
            .Where(e => new[] { "created_at", "updated_at" }.Any(c =>
                e.GetProperties().SingleOrDefault(p => p.GetColumnName() == c)?.GetColumnType() != "timestamptz"))
            .Select(e => e.GetTableName());

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Every_enum_column_has_a_check_listing_its_values()
    {
        var bad = Tables.SelectMany(e => e.GetProperties()
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .Where(p => e.GetCheckConstraints().All(c => c.Name != $"chk_{e.GetTableName()}_{p.GetColumnName()}"))
            .Select(p => $"{e.GetTableName()}.{p.GetColumnName()}"));

        bad.Should().BeEmpty();
    }
}
