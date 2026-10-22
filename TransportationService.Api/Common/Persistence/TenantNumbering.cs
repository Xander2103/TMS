using Microsoft.EntityFrameworkCore;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// Retry-safe persistence for entities whose number is claimed from a TenantSettings counter.
/// The counter columns are optimistic-concurrency tokens, so two requests claiming the same
/// value conflict at SaveChanges. On conflict the settings row is reloaded and the caller's
/// <c>assignNumber</c> delegate re-claims a fresh number before retrying — duplicate numbers
/// are impossible and the loser simply gets the next value.
/// <para>
/// MD-H5 (master-data acceptance 2026-10-18): five parallel order creates gave one 201 and four
/// 500s. In PostgreSQL the INSERT of the new row reaches the unique number index BEFORE the
/// counter UPDATE is judged, so the loser saw a unique violation (23505), not a concurrency
/// conflict, and nothing retried it. A unique-number collision is now retried too: the delegate
/// claims the NEXT value from the in-memory counter (skipping a number that is taken — also when
/// existing data is ahead of the counter), and the counter's concurrency token then either
/// accepts that value or reloads the winner's counter. Enough attempts for a burst of parallel
/// creates, with a small jitter so the losers do not collide again in lock-step. Inside an
/// explicit transaction EF rolls each failed SaveChanges back to its own savepoint.
/// </para>
/// </summary>
public static class TenantNumbering
{
    private const int MaxAttempts = 30;
    private const string PostgresProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public static async Task SaveWithClaimedNumberAsync(
        DbContext dbContext,
        object? settings,
        Action assignNumber,
        CancellationToken cancellationToken)
    {
        // MD-H5 final (parallel PostgreSQL proof 2026-10-18): every counter of the settings row is a
        // concurrency token, so an order create (order + dossier number) racing standalone dossier
        // creates kept invalidating each other's optimistic UPDATE — 40 mixed parallel creates gave
        // 409s and 500s once the retries ran out. On PostgreSQL the claim is now SERIALIZED per
        // tenant: the settings row is locked (SELECT … FOR UPDATE) inside a transaction and the
        // counters are refreshed from the locked row before the number is assigned. Holding the
        // lock, nobody else can move a counter until this save commits; the unique-violation
        // retry below only has to skip numbers that existing data already uses.
        var lockRow = settings is not null && dbContext.Database.ProviderName == PostgresProvider;
        await using var ownTransaction = lockRow && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (lockRow)
        {
            await LockAndRefreshCountersAsync(dbContext, settings!, cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            assignNumber();

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (ownTransaction is not null)
                {
                    await ownTransaction.CommitAsync(cancellationToken);
                }

                return;
            }
            catch (DbUpdateConcurrencyException exception)
                when (settings is not null && attempt < MaxAttempts && exception.Entries.Any(e => ReferenceEquals(e.Entity, settings)))
            {
                // Another request claimed the counter value first (only possible without the row
                // lock, e.g. SQLite). Take the current counters and let assignNumber() claim the next.
                // PERF-B4: only a conflict ON THE COUNTER ROW is retried here; a conflict on another
                // row in the same save (an order or dossier whose Version token was overtaken) is a
                // genuine race that must surface as 409, not be retried thirty times.
                await RefreshCountersAsync(dbContext, settings, cancellationToken);
            }
            catch (DbUpdateException exception) when (settings is not null && attempt < MaxAttempts && IsUniqueViolation(exception))
            {
                // The number is taken (data ahead of the counter, or — without the lock — a parallel
                // winner): keep the in-memory counter, so the next assignNumber() moves past it.
            }

            if (!lockRow)
            {
                await Task.Delay(Random.Shared.Next(2, 15 + attempt * 5), cancellationToken);
            }
        }
    }

    private static async Task LockAndRefreshCountersAsync(DbContext dbContext, object settings, CancellationToken cancellationToken)
    {
        var entry = dbContext.Entry(settings);
        var key = entry.Metadata.FindPrimaryKey()!.Properties.Single();
        var table = entry.Metadata.GetTableName();
        var keyColumn = key.GetColumnName();
        // Identifiers come from the EF model (never from input); the key value is a parameter.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"SELECT 1 FROM \"{table}\" WHERE \"{keyColumn}\" = {{0}} FOR UPDATE",
            [entry.Property(key.Name).CurrentValue!], cancellationToken);
        await RefreshCountersAsync(dbContext, settings, cancellationToken);
    }

    /// <summary>
    /// Takes the stored counters as the concurrency baseline and never moves an in-memory counter
    /// BACK (it may already be past numbers that existing data uses). Other pending changes of the
    /// caller on the settings entity are kept (unlike a reload).
    /// </summary>
    private static async Task RefreshCountersAsync(DbContext dbContext, object settings, CancellationToken cancellationToken)
    {
        var entry = dbContext.Entry(settings);
        var stored = await entry.GetDatabaseValuesAsync(cancellationToken);
        if (stored is null)
        {
            return;
        }

        foreach (var property in entry.Properties.Where(p => p.Metadata.IsConcurrencyToken && p.Metadata.ClrType == typeof(int)))
        {
            var storedValue = (int)stored[property.Metadata]!;
            var current = (int)property.CurrentValue!;
            property.OriginalValue = storedValue;
            property.CurrentValue = Math.Max(current, storedValue);
        }
    }

    /// <summary>PostgreSQL 23505 / SQLite constraint 19 (UNIQUE) — provider-neutral, no provider reference.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (sqlState == "23505")
            {
                return true;
            }

            if (inner.GetType().Name == "SqliteException" && inner.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
