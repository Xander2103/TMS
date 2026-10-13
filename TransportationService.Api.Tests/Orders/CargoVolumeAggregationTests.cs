using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Wave 1 §12 audit fix: a cargo line's VolumeM3 is per stuk, so the derived header volume
/// must multiply by the expected quantity (previously it summed the per-piece values, making
/// 33 pallets × 2 m³ report 2 m³). Lines without a volume are skipped, never counted as 0-only.
/// </summary>
public class CargoVolumeAggregationTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, new PermissionSet());
        return new Harness(db, sut, admin, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, IReadOnlyList<CargoItemInput> cargoItems) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 11), "Pallets", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        cargoItems);

    [Fact]
    public async Task DerivedHeaderVolume_MultipliesPerPieceVolume_ByExpectedQuantity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // 3 stuks × 2 m³ per stuk → header 6 m³ (was 2 m³ before the fix).
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
        [
            new CargoItemInput("Kisten", null, 3m, null, null,
                VolumeM3: 2m, VolumeIsManual: true, QuantityUnitCode: "EUROPALLET"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(6m, created.Order!.VolumeM3);

        var stored = await h.Db.Context.TransportOrders.SingleAsync();
        Assert.Equal(6m, stored.VolumeM3);
    }

    [Fact]
    public async Task DerivedHeaderVolume_SkipsLinesWithoutVolume()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Line without a volume contributes nothing; the other line still multiplies by quantity.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
        [
            new CargoItemInput("Kisten", null, 3m, null, null,
                VolumeM3: 2m, VolumeIsManual: true, QuantityUnitCode: "EUROPALLET"),
            new CargoItemInput("Zakken", null, 5m, null, null,
                QuantityUnitCode: "EUROPALLET"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(6m, created.Order!.VolumeM3);
    }
}
