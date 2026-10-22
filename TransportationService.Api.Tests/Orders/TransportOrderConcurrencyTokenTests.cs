using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// PERF-B4 (performance/concurrency final remediation 2026-10-21): two edits of one order that both
/// passed the in-code version check used to BOTH update (silent lost update).
/// <c>TransportOrder.Version</c> is an EF concurrency token now: the loser's UPDATE matches no row
/// and answers the same 409 as a stale token — <c>VersionConflict</c> with the CURRENT state — while
/// the winner's change survives. A legacy caller without a version is protected the same way.
/// </summary>
public class TransportOrderConcurrencyTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CompetingWriterInterceptor Race, Guid TenantId, Guid CustomerId, Guid LocationId)
    {
        public TransportOrderService Sut(TransportationDbContext? context = null)
        {
            var db = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            return new TransportOrderService(db, tenant, new AuditService(db, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var race = new CompetingWriterInterceptor();
        var db = new SqliteTestDbContext(null, race);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        TestUnitCatalog.Seed(db.Context, tenantId);
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Terminal, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, race, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput Stop(StopType type, Guid? locationId = null, string? city = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, null, null, null, null);

    private static UpdateTransportOrderRequest UpdateFrom(TransportOrderDetailDto d, string notes, Guid? version) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity, d.QuantityUnit, d.WeightKg, d.VolumeM3,
        d.PalletCount, d.AdrRequired, d.CraneRequired, d.AgreedPrice, notes,
        d.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions, Id: s.Id))
            .ToList(),
        QuantityUnitCode: d.QuantityUnitCode,
        Version: version);

    private static async Task<TransportOrderDetailDto> CreateAsync(Harness h)
    {
        var created = await h.Sut().CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, "PO-777", new DateOnly(2026, 10, 22), "20 paletten bouwmateriaal",
            20, "paletten", 12500, null, 20, false, false, 1450m, null,
            [Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        return created.Order!;
    }

    private static async Task<TransportOrderOperationResult> RaceOnAsync(Harness h, TransportOrderDetailDto created, Guid? loserVersion)
    {
        // Colleague B saves between A's version check and A's UPDATE — with the version A also holds.
        h.Race.Arm(
            context => context.ChangeTracker.Entries<TransportOrder>().Any(e => e.Entity.Id == created.Id && e.State == EntityState.Modified),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                var winner = await h.Sut(other).UpdateAsync(created.Id, UpdateFrom(created, "notitie van collega B", created.Version), cancellationToken);
                Assert.Equal(TransportOrderOperationOutcome.Success, winner.Outcome);
            });

        var result = await h.Sut().UpdateAsync(created.Id, UpdateFrom(created, "notitie van gebruiker A", loserVersion), CancellationToken.None);

        Assert.Equal(1, h.Race.Fired);
        Assert.Equal(TransportOrderOperationOutcome.VersionConflict, result.Outcome);
        Assert.NotNull(result.Error);
        // The 409 body is the CURRENT state (rebase contract), not the loser's stale copy.
        Assert.Equal("notitie van collega B", result.Order!.Notes);
        Assert.NotEqual(created.Version, result.Order.Version);

        var stored = await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == created.Id);
        Assert.Equal("notitie van collega B", stored.Notes);
        Assert.Equal(result.Order.Version, stored.Version);
        // The loser wrote nothing: one "Updated" audit row (the winner's), not two.
        Assert.Single(await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "TransportOrder" && a.EntityId == created.Id.ToString() && a.Action == "Updated")
            .ToListAsync());
        return result;
    }

    [Fact]
    public async Task ConcurrentEdit_WithTheSameVersion_IsRefusedWithVersionConflict_AndTheWinnerSurvives()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await CreateAsync(h);

        var conflict = await RaceOnAsync(h, created, loserVersion: created.Version);

        // The rebase works: with the current version the edit goes through.
        h.Db.Context.ChangeTracker.Clear();
        var rebased = await h.Sut().UpdateAsync(created.Id, UpdateFrom(conflict.Order!, "notitie van gebruiker A", conflict.Order!.Version), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, rebased.Outcome);
        Assert.Equal("notitie van gebruiker A", rebased.Order!.Notes);
    }

    [Fact]
    public async Task LegacyCallerWithoutVersion_IsStillProtectedByTheToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await CreateAsync(h);

        await RaceOnAsync(h, created, loserVersion: null);
    }
}
