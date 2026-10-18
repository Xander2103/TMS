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

    public static async Task SaveWithClaimedNumberAsync(
        DbContext dbContext,
        object? settings,
        Action assignNumber,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            assignNumber();

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (settings is not null && attempt < MaxAttempts)
            {
                // Another request claimed the counter value first. Discard our stale increment,
                // reload the current counter, and let assignNumber() claim the next value.
                await dbContext.Entry(settings).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (settings is not null && attempt < MaxAttempts && IsUniqueViolation(exception))
            {
                // The number is taken (a parallel winner, or data ahead of the counter): keep the
                // in-memory counter, so the next assignNumber() moves past it.
            }

            await Task.Delay(Random.Shared.Next(2, 15 + attempt * 5), cancellationToken);
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
