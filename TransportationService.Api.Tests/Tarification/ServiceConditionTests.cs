using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
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
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>
/// Corrections wave 2026-07-27 §2.4: service options can be restricted by conditions beyond
/// ADR. Currently supported: warehouse (the order stops at the warehouse's location). Rows of
/// the same kind OR together; different kinds — including OnlyForAdr — AND together. No
/// condition = the service applies to all orders.
/// </summary>
public class ServiceConditionTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(
        SqliteTestDbContext Db, PricingEngine Engine, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid WarehouseId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        var locationId = Guid.NewGuid();
        db.Context.Locations.Add(new Location { Id = locationId, TenantId = tenantId, Name = "Magazijn Gent", IsActive = true });
        db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Magazijn Gent", LocationId = locationId, IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        return new Harness(db, engine, admin, tenantId, customerId, palletUnitId, warehouseId);
    }

    private static PriceCalculationRequest Request(Harness h, IReadOnlyList<Guid>? warehouseIds = null, bool? adr = null) =>
        new(h.CustomerId, Today, [new PriceCalculationLineInput(h.PalletUnitId, 3)], "BE", null, null, null, null,
            [], AdrRequired: adr, WarehouseIds: warehouseIds);

    private static async Task SeedBaseRuleAsync(Harness h) =>
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", Today.AddMonths(-1), null, true, 10m, null, null), CancellationToken.None);

    private static Task<ServiceOptionDto> CreateColdStoreHandlingAsync(Harness h, bool autoApply = true, bool onlyForAdr = false) =>
        h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "KOEL", "Koelbehandeling", SurchargeKind.Fixed, 15m, true, 0,
            AutoApply: autoApply, OnlyForAdr: onlyForAdr, WarehouseIds: [h.WarehouseId]), CancellationToken.None)!;

    [Fact]
    public async Task WarehouseCondition_AutoAppliesOnlyWhenTheOrderTouchesTheWarehouse()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateColdStoreHandlingAsync(h);

        var match = await h.Engine.CalculateAsync(Request(h, [h.WarehouseId]), CancellationToken.None);
        Assert.Equal(45m, match.Total); // 3 × €10 + €15 auto-applied
        Assert.Contains(match.ServiceLines, l => l.Name == "Koelbehandeling" && l.AutoApplied);

        var noMatch = await h.Engine.CalculateAsync(Request(h), CancellationToken.None);
        Assert.Equal(30m, noMatch.Total);
        Assert.Empty(noMatch.ServiceLines);

        var otherWarehouse = await h.Engine.CalculateAsync(Request(h, [Guid.NewGuid()]), CancellationToken.None);
        Assert.Equal(30m, otherWarehouse.Total);
    }

    [Fact]
    public async Task ExplicitSelection_OutsideTheWarehouse_IsInformationalNeverCharged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        var option = await CreateColdStoreHandlingAsync(h, autoApply: false);

        var result = await h.Engine.CalculateAsync(Request(h) with { ServiceOptionIds = [option.Id] }, CancellationToken.None);

        Assert.Equal(30m, result.Total);
        Assert.Empty(result.ServiceLines);
        Assert.Contains(result.Lines, l => l.Informational && l.Label.Contains("alleen van toepassing voor het gekoppelde magazijn"));
    }

    [Fact]
    public async Task AdrAndWarehouseConditions_AndTogether()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedBaseRuleAsync(h);
        await CreateColdStoreHandlingAsync(h, onlyForAdr: true);

        // Warehouse matches but not ADR → not auto-applied.
        Assert.Equal(30m, (await h.Engine.CalculateAsync(Request(h, [h.WarehouseId], adr: false), CancellationToken.None)).Total);
        // ADR but wrong warehouse → not auto-applied.
        Assert.Equal(30m, (await h.Engine.CalculateAsync(Request(h, adr: true), CancellationToken.None)).Total);
        // Both hold → charged.
        Assert.Equal(45m, (await h.Engine.CalculateAsync(Request(h, [h.WarehouseId], adr: true), CancellationToken.None)).Total);
    }

    [Fact]
    public async Task OrderSave_ResolvesWarehousesFromStopLocations()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        await h.Db.Context.SaveChangesAsync();
        await SeedBaseRuleAsync(h);
        await CreateColdStoreHandlingAsync(h);

        var tenant = new DevTenantContext(h.TenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var orderService = new TransportOrderService(
            h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, currentUser),
            new TestClock(new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)), h.Engine, currentUser,
            new StubPermissions());

        var warehouseLocationId = (await h.Db.Context.Warehouses.SingleAsync()).LocationId;
        var created = await orderService.CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, "REF-1", Today, "Pallets", 3, null, null, null, null, false, false, null, null,
            [
                new TransportOrderStopInput(StopType.Loading, null, null, null, null, "Antwerpen", "BE", null, null, null, null),
                new TransportOrderStopInput(StopType.Unloading, warehouseLocationId, null, null, "9000", "Gent", "BE", null, null, null, null),
            ],
            null, QuantityUnitCode: "EUROPALLET"), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal("Koelbehandeling", serviceLine.Name);
        Assert.Equal(45m, created.Order.AgreedPrice);
    }

    [Fact]
    public async Task Admin_ValidatesWarehouseReferences_AndRoundTripsConditions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Foreign warehouse refused.
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() => h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("X", "X", SurchargeKind.Fixed, 1m, true, 0, WarehouseIds: [Guid.NewGuid()]),
            CancellationToken.None));

        // Round-trip: names come back; clearing the list removes the condition.
        var created = await CreateColdStoreHandlingAsync(h);
        Assert.Equal(["Magazijn Gent"], created.WarehouseNames);

        var updated = await h.Admin.UpdateServiceOptionAsync(created.Id, new SaveServiceOptionRequest(
            "KOEL", "Koelbehandeling", SurchargeKind.Fixed, 15m, true, 0, AutoApply: true, WarehouseIds: []),
            CancellationToken.None);
        Assert.Empty(updated!.WarehouseIds!);
        Assert.Empty(await h.Db.Context.ServiceOptionConditions.Where(c => c.TenantId == h.TenantId).ToListAsync());
    }

    private sealed class StubPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
