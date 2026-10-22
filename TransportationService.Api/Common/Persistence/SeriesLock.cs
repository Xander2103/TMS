using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// PERF-B2 / PERF-H1 (performance/concurrency final remediation 2026-10-21): serializes a legal
/// number SERIES — invoice numbers per (tenant, entity, kind), transport-document numbers per
/// (tenant, kind, year) — on PostgreSQL with a transaction-scoped advisory lock keyed on the series,
/// the same idea as the <c>SELECT … FOR UPDATE</c> <see cref="TenantNumbering"/> takes on the
/// settings row. The lock lives exactly as long as the transaction: the scope opens one when none is
/// active, the caller commits THROUGH the scope once the claim is saved, and disposing an uncommitted
/// scope rolls everything back (the loser leaves no number, no audit row, no side effect behind).
/// Other providers (the SQLite test harness) get a no-op scope: there the concurrency tokens and the
/// unique indexes remain the final arbiter, exercised by the optimistic retries the callers keep.
/// </summary>
public static class SeriesLock
{
    public const string PostgresProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public static bool IsPostgres(DbContext dbContext) => dbContext.Database.ProviderName == PostgresProvider;

    /// <summary>
    /// Takes the series lock for <paramref name="key"/> (blocking until the current holder's
    /// transaction ends). Re-entrant within one transaction, like the advisory lock itself.
    /// </summary>
    public static async Task<Scope> AcquireAsync(DbContext dbContext, string key, CancellationToken cancellationToken)
    {
        if (!IsPostgres(dbContext))
        {
            return Scope.None;
        }

        var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            // hashtext() folds the series key into the bigint the advisory lock takes; the key is a
            // parameter, never concatenated. A hash collision only serializes two unrelated series.
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))", [key], cancellationToken);
        }
        catch
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }

            throw;
        }

        return new Scope(ownTransaction);
    }

    /// <summary>
    /// The lock scope. <see cref="CommitAsync"/> commits the transaction the scope opened (a no-op
    /// when the caller already had one, or on a provider without the lock); disposing without a
    /// commit rolls that transaction back.
    /// </summary>
    public sealed class Scope : IAsyncDisposable
    {
        public static readonly Scope None = new(null);

        private readonly IDbContextTransaction? _ownTransaction;
        private bool _committed;

        internal Scope(IDbContextTransaction? ownTransaction)
        {
            _ownTransaction = ownTransaction;
        }

        /// <summary>True when this scope opened the transaction it holds (and therefore commits it).</summary>
        public bool OwnsTransaction => _ownTransaction is not null;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_ownTransaction is not null && !_committed)
            {
                await _ownTransaction.CommitAsync(cancellationToken);
                _committed = true;
            }
        }

        public ValueTask DisposeAsync() => _ownTransaction?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

/// <summary>
/// PERF-H2 root cause for role replacement: a pessimistic row lock on PostgreSQL
/// (<c>SELECT 1 FROM "table" WHERE "Key" = @key FOR UPDATE</c>) that serializes every
/// read-modify-write of one aggregate for the rest of the current transaction. Identifiers come
/// from code constants (never from input); the key value is a parameter. A no-op on other providers.
/// </summary>
public static class RowLock
{
    public static Task ForUpdateAsync(DbContext dbContext, string table, string keyColumn, object key, CancellationToken cancellationToken) =>
        SeriesLock.IsPostgres(dbContext)
            ? dbContext.Database.ExecuteSqlRawAsync(
                $"SELECT 1 FROM \"{table}\" WHERE \"{keyColumn}\" = {{0}} FOR UPDATE", [key], cancellationToken)
            : Task.CompletedTask;
}
