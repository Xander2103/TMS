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
/// Closure sprint 2026-09-23 (P1) — the "niet alle goederen geprijsd" warning must follow the
/// ACTUAL price coverage. The engine freezes per-unit coverage with the calculation; a goods
/// line priced afterwards through its own sales line (D4 link) makes that frozen entry
/// effectively covered. Every reader (frontend strip, confirm gate, invoice/dossier readiness)
/// works from the persisted snapshot, so the reconciliation is persisted, not read-time only.
/// </summary>
public class PricingCoverageReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync(bool withTariff)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1, PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", VatNumber = "BE0123456789", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), new PricingEngine(db.Context, tenant), currentUser, new PermissionSet());
        if (withTariff)
        {
            await new PricingAdminService(db.Context, tenant, audit).CreateRuleAsync(new SavePriceRuleRequest(
                customerId, palletUnitId, PriceRuleBasis.PerUnit, null,
                "Pallets klant X", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        }

        return new Harness(db, sut, tenantId, customerId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    /// <summary>Two goods lines: 8 coded pallets and one crate without a unit.</summary>
    private static async Task<TransportOrderDetailDto> CreateAsync(Harness h, decimal? oneOff = null)
    {
        var created = await h.Sut.CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, "REF-1", new DateOnly(2026, 9, 21), "Pallets + kist", null, null, null, null, null, false, false,
            null, null,
            [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
            CargoItems:
            [
                new CargoItemInput("Pallets", null, 8, "pal", null, QuantityUnitCode: "EUROPALLET"),
                new CargoItemInput("Losse kist", null, 1, null, null),
            ],
            PricingSource: oneOff is null ? OrderPricingSource.Contract : OrderPricingSource.OneOff,
            OneOffFixedAmount: oneOff), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        return created.Order!;
    }

    private static Guid Pallets(TransportOrderDetailDto order) => order.CargoItems.Single(c => c.Description == "Pallets").Id;
    private static Guid Crate(TransportOrderDetailDto order) => order.CargoItems.Single(c => c.Description == "Losse kist").Id;

    private static async Task<TransportOrderDetailDto> SaveLinesAsync(Harness h, Guid orderId, params SaveOrderPriceLineRequest[] lines)
    {
        var saved = await h.Sut.SaveOrderPriceLinesAsync(orderId, lines, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, saved.Outcome);
        return saved.Order!;
    }

    /// <summary>What the frontend strip and the confirm gate look at: any entry that is not Full.</summary>
    private static bool WarnsAboutUnpricedGoods(TransportOrderDetailDto order) =>
        (order.PricingSnapshot?.Coverage ?? []).Any(c => c.Status != "Full");

    private static async Task<(string? Status, string? Readiness)> PersistedAsync(Harness h, Guid orderId)
    {
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots.AsNoTracking().SingleAsync(s => s.TransportOrderId == orderId);
        var order = await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        return (snapshot.CoverageStatus, order.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task NoGoodsPriced_Warns()
    {
        var h = await SeedAsync(withTariff: false);
        using var _ = h.Db;

        var order = await CreateAsync(h);

        Assert.True(WarnsAboutUnpricedGoods(order));
        Assert.NotEqual("Full", (await PersistedAsync(h, order.Id)).Status);
    }

    [Fact]
    public async Task OneOfSeveralGoodsPricedSeparately_StillWarns_ForTheOther()
    {
        var h = await SeedAsync(withTariff: false);
        using var _ = h.Db;
        var order = await CreateAsync(h);

        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order)]));

        Assert.True(WarnsAboutUnpricedGoods(saved));
        Assert.Contains(saved.PricingSnapshot!.Coverage!, c => c.Status == "Full");
        Assert.Contains(saved.PricingSnapshot.Coverage!, c => c.Status != "Full");
        Assert.NotEqual("Full", (await PersistedAsync(h, order.Id)).Status);
    }

    [Fact]
    public async Task AllGoodsPricedSeparately_ClearsTheWarning_Persistently()
    {
        var h = await SeedAsync(withTariff: false);
        using var _ = h.Db;
        var order = await CreateAsync(h);
        Assert.True(WarnsAboutUnpricedGoods(order));

        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Pallets apart", 8m, 25m, null, null, CargoItemIds: [Pallets(order)]),
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order)]));

        Assert.False(WarnsAboutUnpricedGoods(saved));
        Assert.All(saved.PricingSnapshot!.Coverage!, c => Assert.Equal("Full", c.Status));
        var (status, readiness) = await PersistedAsync(h, order.Id);
        Assert.Equal("Full", status);
        Assert.DoesNotContain("pricing.coverage", readiness ?? string.Empty);

        // Reload from scratch: the persisted state is what the page shows after F5.
        var reloaded = (await h.Sut.GetByIdAsync(order.Id, CancellationToken.None))!;
        Assert.False(WarnsAboutUnpricedGoods(reloaded));
        Assert.Equal("Full", reloaded.PricingSnapshot!.CoverageStatus);
    }

    [Fact]
    public async Task GoodsIncludedInAFixedPrice_NeverWarn()
    {
        var h = await SeedAsync(withTariff: false);
        using var _ = h.Db;

        var order = await CreateAsync(h, oneOff: 500m);

        Assert.False(WarnsAboutUnpricedGoods(order));
        Assert.All(order.CargoItems, c => Assert.Equal(CargoCommercialCoverage.Included, c.CommercialCoverage));
    }

    [Fact]
    public async Task MixOfTariffIncludedAndSeparatelyPriced_ClearsTheWarning()
    {
        var h = await SeedAsync(withTariff: true);
        using var _ = h.Db;
        var order = await CreateAsync(h);
        // Pallets are covered by the tariff; the unit-less crate is the ONE open entry.
        Assert.True(WarnsAboutUnpricedGoods(order));
        Assert.Single(order.PricingSnapshot!.Coverage!, c => c.Status != "Full");

        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order)]));

        Assert.False(WarnsAboutUnpricedGoods(saved));
        Assert.Equal("Full", (await PersistedAsync(h, order.Id)).Status);
        Assert.Equal(CargoCommercialCoverage.SeparatelyPriced, saved.CargoItems.Single(c => c.Id == Crate(order)).CommercialCoverage);
    }

    [Fact]
    public async Task RemovingTheSeparateLine_BringsTheWarningBack()
    {
        var h = await SeedAsync(withTariff: true);
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order)]));
        Assert.False(WarnsAboutUnpricedGoods(saved));
        var manual = saved.PricingLines!.Single(l => l.Kind == OrderPriceLineKind.Manual);

        var removed = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(manual.LineKey, manual.Label, null, null, null, null, Remove: true));

        Assert.True(WarnsAboutUnpricedGoods(removed));
        var open = Assert.Single(removed.PricingSnapshot!.Coverage!, c => c.Status != "Full");
        Assert.Equal("None", open.Status);
        Assert.NotEqual("Full", (await PersistedAsync(h, order.Id)).Status);
    }

    [Fact]
    public async Task UnlinkingWithoutRemovingTheLine_BringsTheWarningBack()
    {
        var h = await SeedAsync(withTariff: true);
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order)]));
        var manual = saved.PricingLines!.Single(l => l.Kind == OrderPriceLineKind.Manual);

        var unlinked = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(manual.LineKey, manual.Label, null, null, null, null, CargoItemIds: []));

        Assert.True(WarnsAboutUnpricedGoods(unlinked));
        Assert.NotEqual("Full", (await PersistedAsync(h, order.Id)).Status);
    }
}
