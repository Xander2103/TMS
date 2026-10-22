using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// PERF-H2 (performance/concurrency final remediation 2026-10-21): names the unique constraint a
/// <see cref="DbUpdateException"/> tripped over, so a caller can tell an EXPECTED race (a known
/// index: "the other request won") from a defect that must stay a 500.
/// <para>
/// PostgreSQL reports the constraint itself (<c>PostgresException.ConstraintName</c>, read by
/// reflection like <see cref="TenantNumbering.IsUniqueViolation"/> so Common keeps no provider
/// reference). SQLite only says <c>UNIQUE constraint failed: table.ColA, table.ColB</c>; that
/// column set is resolved against the EF model of the failed entries to the primary key or unique
/// index that owns it (so a PK violation on <c>user_roles</c> reads <c>PK_user_roles</c> and an
/// explicitly named index keeps its name), falling back to EF's conventional
/// <c>IX_{table}_{col}_{col}</c>.
/// </para>
/// </summary>
public static partial class UniqueViolations
{
    /// <summary>PostgreSQL 23505 / SQLite UNIQUE — see <see cref="TenantNumbering.IsUniqueViolation"/>.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) => TenantNumbering.IsUniqueViolation(exception);

    /// <summary>The violated unique constraint's database name, or null when the exception is no unique violation.</summary>
    public static string? ConstraintName(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            var type = inner.GetType();
            if (type.GetProperty("SqlState")?.GetValue(inner) as string == "23505")
            {
                return type.GetProperty("ConstraintName")?.GetValue(inner) as string;
            }

            if (type.Name == "SqliteException")
            {
                var match = SqliteUniqueMessage().Match(inner.Message);
                if (match.Success)
                {
                    return ResolveSqlite(match.Groups["columns"].Value, exception.Entries);
                }
            }
        }

        return null;
    }

    private static string? ResolveSqlite(string columnList, IReadOnlyList<EntityEntry> entries)
    {
        var parts = columnList
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('.', 2))
            .Where(part => part.Length == 2)
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var table = parts[0][0];
        var columns = parts.Select(part => part[1].Trim()).ToList();

        foreach (var entityType in entries.Select(e => e.Metadata).Distinct())
        {
            if (entityType.GetTableName() != table)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var key in entityType.GetKeys())
            {
                if (ColumnsMatch(key.Properties, store, columns))
                {
                    return key.GetName(store);
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (index.IsUnique && ColumnsMatch(index.Properties, store, columns))
                {
                    return index.GetDatabaseName(store);
                }
            }
        }

        return $"IX_{table}_{string.Join("_", columns)}";
    }

    private static bool ColumnsMatch(IReadOnlyList<IProperty> properties, StoreObjectIdentifier store, IReadOnlyList<string> columns) =>
        properties.Count == columns.Count
        && properties.Select(p => p.GetColumnName(store)).SequenceEqual(columns, StringComparer.Ordinal);

    [GeneratedRegex(@"UNIQUE constraint failed: (?<columns>[^'\r\n]+)")]
    private static partial Regex SqliteUniqueMessage();
}
