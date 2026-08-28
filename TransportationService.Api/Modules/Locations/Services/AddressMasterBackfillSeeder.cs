using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Services;

/// <summary>
/// Sprint 2 (central address master): one-time, idempotent data move for the additive
/// <c>customer_location_links</c> table and the derived address keys.
///
/// Two independent steps, both safe to re-run and both batched:
/// <list type="number">
/// <item>Derive <see cref="Location.AddressExactKey"/>/<see cref="Location.AddressStreetKey"/>
/// for rows that have none yet or whose keys came from an older normaliser. The keys need diacritic folding, which is not expressible in
/// plain SQL, so they are computed here with the same <see cref="AddressNormalizer"/> the
/// service uses — a SQL approximation would silently disagree with newly written rows and miss
/// duplicates on accented addresses.</item>
/// <item>Turn every legacy <c>Location.CustomerId</c> ownership into a relationship row,
/// carrying the per-customer defaults and the customer's own reference across. Nothing is
/// deleted or overwritten: the legacy columns stay exactly as they are, so a rollback to the
/// previous build keeps working.</item>
/// </list>
/// Runs without a tenant context (like the other startup seeders), which the global filter
/// treats as "all tenants"; TenantId is copied from the address so rows never cross tenants.
/// </summary>
public static class AddressMasterBackfillSeeder
{
    private const int BatchSize = 500;

    public static async Task<(int KeysWritten, int LinksCreated)> SyncAsync(
        TransportationDbContext db, CancellationToken cancellationToken = default)
    {
        var keys = await BackfillAddressKeysAsync(db, cancellationToken);
        var links = await BackfillCustomerLinksAsync(db, cancellationToken);
        return (keys, links);
    }

    /// <summary>
    /// Derives the keys for every address whose stored keys differ from a fresh computation:
    /// rows that never had keys AND rows written by an older normaliser (the house-number and
    /// street-key rules changed after the first backfill). Recomputation converges, so the pass
    /// is idempotent without a separate marker — a second run finds nothing to write.
    /// </summary>
    private static async Task<int> BackfillAddressKeysAsync(
        TransportationDbContext db, CancellationToken cancellationToken)
    {
        var written = 0;
        Guid lastId = Guid.Empty;

        while (true)
        {
            var batch = await db.Locations
                .Where(l => l.Id > lastId)
                .OrderBy(l => l.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) return written;

            foreach (var location in batch)
            {
                // Empty string (not null) marks "computed, nothing to match on", so an address
                // without street is not re-examined on every start-up.
                var exactKey = AddressNormalizer.ExactKey(
                    location.CountryCode, location.PostalCode, location.City, location.Street, location.HouseNumber);
                var streetKey = AddressNormalizer.StreetKey(
                    location.CountryCode, location.PostalCode, location.City, location.Street);
                if (location.AddressExactKey == exactKey && location.AddressStreetKey == streetKey) continue;

                location.AddressExactKey = exactKey;
                location.AddressStreetKey = streetKey;
                written++;
            }

            await db.SaveChangesAsync(cancellationToken);
            lastId = batch[^1].Id;
        }
    }

    private static async Task<int> BackfillCustomerLinksAsync(
        TransportationDbContext db, CancellationToken cancellationToken)
    {
        var created = 0;
        Guid lastId = Guid.Empty;

        while (true)
        {
            var batch = await db.Locations
                .Where(l => l.Id > lastId && l.CustomerId != null)
                .Where(l => !db.CustomerLocationLinks.Any(link =>
                    link.LocationId == l.Id && link.CustomerId == l.CustomerId))
                .OrderBy(l => l.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) return created;

            foreach (var location in batch)
            {
                db.CustomerLocationLinks.Add(new CustomerLocationLink
                {
                    TenantId = location.TenantId,
                    CustomerId = location.CustomerId!.Value,
                    LocationId = location.Id,
                    // The legacy model had no role; every existing address served both kinds.
                    Role = CustomerLocationRole.Both,
                    IsDefaultLoading = location.IsDefaultLoadingLocation,
                    IsDefaultUnloading = location.IsDefaultUnloadingLocation,
                    IsDefaultBilling = location.IsDefaultBillingLocation,
                    CustomerReference = location.ExternalReference,
                    // A deactivated address keeps its relationship, but it should not surface
                    // in the selectors either.
                    IsActive = location.IsActive,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            created += batch.Count;
            lastId = batch[^1].Id;
        }
    }
}
