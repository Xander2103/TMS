using Microsoft.EntityFrameworkCore;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// Retry-safe persistence for entities whose number is claimed from a TenantSettings counter.
/// The counter columns are optimistic-concurrency tokens, so two requests claiming the same
/// value conflict at SaveChanges. On conflict the settings row is reloaded and the caller's
/// <c>assignNumber</c> delegate re-claims a fresh number before retrying — duplicate numbers
/// are impossible and the loser simply gets the next value.
/// </summary>
public static class TenantNumbering
{
    private const int MaxAttempts = 3;

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
        }
    }
}
