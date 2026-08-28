using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Locations;

/// <summary>
/// Sprint 2 migration safety: the additive backfill must carry every existing customer/address
/// association across without losing defaults, references or tenant ownership — and must be
/// safe to run again on every start-up.
/// </summary>
public class AddressMasterBackfillSeederTests
{
    private static readonly DateTime Now = new(2026, 08, 27, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Seeded(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Seeded> ArrangeLegacyDataAsync(bool defaults = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });

        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-1" };
        db.Context.Customers.Add(customer);

        // A pre-sprint address: owned through the legacy column, no link, no derived keys.
        var location = new Location
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "ADR-1",
            Name = "Magazijn Noord",
            Type = LocationType.CustomerLocation,
            Street = "Noorderlaan",
            HouseNumber = "10",
            PostalCode = "2030",
            City = "Antwerpen",
            CountryCode = "BE",
            ExternalReference = "KLANT-REF-7",
            CustomerId = customer.Id,
            IsDefaultLoadingLocation = defaults,
            IsDefaultBillingLocation = defaults,
            IsActive = true,
        };
        db.Context.Locations.Add(location);
        await db.Context.SaveChangesAsync();

        return new Seeded(db, tenantId, customer.Id, location.Id);
    }

    [Fact]
    public async Task Backfill_TurnsLegacyOwnershipIntoARelationship_PreservingDefaultsAndReference()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;

        var (keys, links) = await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        Assert.Equal(1, keys);
        Assert.Equal(1, links);

        var link = await s.Db.Context.CustomerLocationLinks.SingleAsync();
        Assert.Equal(s.TenantId, link.TenantId);
        Assert.Equal(s.CustomerId, link.CustomerId);
        Assert.Equal(s.LocationId, link.LocationId);
        Assert.True(link.IsDefaultLoading);
        Assert.True(link.IsDefaultBilling);
        Assert.False(link.IsDefaultUnloading);
        Assert.Equal("KLANT-REF-7", link.CustomerReference);
        // The legacy model had no role; every existing address served both kinds.
        Assert.Equal(CustomerLocationRole.Both, link.Role);
    }

    [Fact]
    public async Task Backfill_LeavesTheLegacyColumnsUntouched()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;

        await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        // Rolling back to the previous build must keep working.
        var location = await s.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == s.LocationId);
        Assert.Equal(s.CustomerId, location.CustomerId);
        Assert.True(location.IsDefaultLoadingLocation);
        Assert.True(location.IsDefaultBillingLocation);
    }

    [Fact]
    public async Task Backfill_DerivesTheDuplicateDetectionKeys()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;

        await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        var location = await s.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == s.LocationId);
        Assert.Equal(
            AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", "10"),
            location.AddressExactKey);
        Assert.Equal(
            AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", "Noorderlaan"),
            location.AddressStreetKey);
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;

        await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);
        var second = await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        Assert.Equal((0, 0), second);
        Assert.Equal(1, await s.Db.Context.CustomerLocationLinks.CountAsync());
    }

    [Fact]
    public async Task Backfill_RecomputesKeysWrittenByAnOlderNormaliser_Once()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;
        // Stale keys as the pre-audit normaliser wrote them ("1/1" collapsed to "11").
        var stale = await s.Db.Context.Locations.SingleAsync(l => l.Id == s.LocationId);
        stale.HouseNumber = "1/1";
        stale.AddressExactKey = "BE|2030|antwerpen|noorderlaan|11";
        stale.AddressStreetKey = "BE|2030|antwerpen|noorderlaan";
        await s.Db.Context.SaveChangesAsync();

        var (keys, _) = await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);
        var (keysAgain, _) = await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        Assert.Equal(1, keys);
        Assert.Equal(0, keysAgain);
        var location = await s.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == s.LocationId);
        Assert.Equal(AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", "1/1"), location.AddressExactKey);
        Assert.EndsWith("|1/1", location.AddressExactKey);
    }

    [Fact]
    public async Task Backfill_SkipsAddressesWithoutACustomer()
    {
        var s = await ArrangeLegacyDataAsync();
        using var _ = s.Db;
        s.Db.Context.Locations.Add(new Location
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, Code = "ADR-2", Name = "Bedrijfsdepot",
            Type = LocationType.Depot, City = "Gent", CountryCode = "BE", IsActive = true,
        });
        await s.Db.Context.SaveChangesAsync();

        var (_, links) = await AddressMasterBackfillSeeder.SyncAsync(s.Db.Context);

        Assert.Equal(1, links);
    }
}
