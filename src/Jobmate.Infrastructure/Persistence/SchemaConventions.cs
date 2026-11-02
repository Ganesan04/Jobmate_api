using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// The schema naming rules, applied to the whole model after every entity configuration
/// has run, so no single configuration can forget one. Database/README.md lists the
/// rules; <c>SchemaConventionTests</c> fails the build if the model breaks any of them.
///
/// <list type="bullet">
/// <item>Primary key column: <c>{singular table}_id</c>, e.g. <c>job_application_id</c>.</item>
/// <item>Constraints and indexes: <c>pk_</c>, <c>fk_</c>, <c>ix_</c>, <c>ux_</c> (unique), <c>chk_</c>.</item>
/// <item>Every enum column gets a <c>chk_{table}_{column}</c> listing its allowed values.</item>
/// </list>
/// </summary>
public static class SchemaConventions
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();

            if (table is null)
            {
                continue;
            }

            var key = entity.FindPrimaryKey();

            if (key is not null)
            {
                key.SetName($"pk_{table}");

                if (key.Properties is [{ Name: "Id" } id])
                {
                    id.SetColumnName(PrimaryKeyColumn(table));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var columns = string.Join("_", foreignKey.Properties.Select(p => p.GetColumnName()));
                foreignKey.SetConstraintName($"fk_{table}_{columns}");
            }

            foreach (var index in entity.GetIndexes())
            {
                // Keeps a hand-picked name's meaning ("ix_users_area") and only normalises
                // the prefix, so EF's "IX_payments_giver_user_id" and a unique index
                // misnamed "ix_" both come out right.
                var name = index.GetDatabaseName() ?? $"ix_{table}";
                index.SetDatabaseName((index.IsUnique ? "ux_" : "ix_") + name[3..].ToLowerInvariant());
            }

            foreach (var property in entity.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (!enumType.IsEnum || property.GetProviderClrType() != typeof(string))
                {
                    continue;
                }

                var column = property.GetColumnName();
                var allowed = string.Join(", ", Enum.GetNames(enumType).Select(n => $"'{n}'"));

                entity.AddCheckConstraint($"chk_{table}_{column}", $"\"{column}\" IN ({allowed})");
            }
        }
    }

    /// <summary><c>job_applications</c> becomes <c>job_application_id</c>. Every table name is a regular plural.</summary>
    public static string PrimaryKeyColumn(string table) => $"{table[..^1]}_id";
}
