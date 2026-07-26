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

/// <summary>Spec 7: order save prices via the engine, snapshots the breakdown, override is guarded.</summary>
public class OrderPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin, PermissionSet Permissions,
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
        var permissions = new PermissionSet();
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissions);
        return new Harness(db, sut, admin, permissions, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(
        Guid customerId, decimal quantity = 3, decimal? agreedPrice = null,
        IReadOnlyList<Guid>? serviceOptionIds = null, bool priceIsManual = false, string? overrideReason = null,
        IReadOnlyList<CargoItemInput>? cargoItems = null) => new(
        customerId, "REF-1", new DateOnly(2026, 7, 24), "Pallets", quantity, null, null, null, null, false, false,
        agreedPrice, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        cargoItems,
        QuantityUnitCode: "EUROPALLET", ServiceOptionIds: serviceOptionIds,
        PriceIsManual: priceIsManual, PriceOverrideReason: overrideReason);

    private static async Task SeedPalletBracketsAsync(Harness h)
    {
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.QuantityBracket, null,
            "Pallets klant X", new DateOnly(2026, 1, 1), null, true, null, null,
            [
                new SavePriceRuleBracketRequest(1, 1, 50, null),
                new SavePriceRuleBracketRequest(2, 2, 85, null),
                new SavePriceRuleBracketRequest(3, null, 115, 25),
            ]), CancellationToken.None);
    }

    [Fact]
    public async Task Create_CalculatesPrice_AndSnapshotsBreakdown()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(115m, created.Order!.CalculatedPrice);
        Assert.Equal(115m, created.Order.AgreedPrice);
        Assert.False(created.Order.PriceIsManual);
        Assert.NotNull(created.Order.PricingLines);
        Assert.Contains(created.Order.PricingLines!, l => l.Source == "Pallets klant X");
    }

    [Fact]
    public async Task ManualOverride_RequiresPermissionAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var noPermission = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true, overrideReason: "Afspraak telefonisch"),
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noPermission.Outcome);

        h.Permissions.Codes.Add("orders.override_price");
        var noReason = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noReason.Outcome);

        var allowed = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true, overrideReason: "Afspraak telefonisch"),
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, allowed.Outcome);
        Assert.Equal(99m, allowed.Order!.AgreedPrice);
        Assert.Equal(115m, allowed.Order.CalculatedPrice); // engine result stays visible next to the override
        Assert.True(allowed.Order.PriceIsManual);
        Assert.Equal("Afspraak telefonisch", allowed.Order.PriceOverrideReason);
    }

    [Fact]
    public async Task NoPricingConfig_LegacyManualEntry_StillWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, agreedPrice: 1450m), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(1450m, created.Order!.AgreedPrice);
        Assert.Null(created.Order.CalculatedPrice);
        Assert.False(created.Order.PriceIsManual);
    }

    [Fact]
    public async Task Snapshot_SurvivesLaterTariffChanges_UntilResave()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        Assert.Equal(115m, created.Order!.AgreedPrice);

        // Master data changes afterwards; the historical order must not move.
        var rule = await h.Db.Context.PriceRules.Include(r => r.Brackets).SingleAsync();
        foreach (var bracket in rule.Brackets)
        {
            bracket.Price += 1000m;
        }

        await h.Db.Context.SaveChangesAsync();

        var reloaded = await h.Sut.GetByIdAsync(created.Order.Id, CancellationToken.None);
        Assert.Equal(115m, reloaded!.AgreedPrice);
        Assert.Equal(115m, reloaded.CalculatedPrice);
        Assert.Contains(reloaded.PricingLines!, l => l.Amount == 115m);
    }

    [Fact]
    public async Task Create_WritesSnapshotHeader()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        var snapshot = created.Order!.PricingSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal(new DateOnly(2026, 7, 24), snapshot!.TariffDate);
        Assert.Equal("EUR", snapshot.Currency);
        Assert.Equal(115m, snapshot.CalculatedTotal);
        Assert.Null(snapshot.OverrideAmount);
        Assert.Contains("Europallet", snapshot.UnitSummary);
        Assert.Contains("Pallets klant X", snapshot.Explanation);
    }

    [Fact]
    public async Task Override_RecordsUserAndTimestamp_InSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.override_price");

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, agreedPrice: 99, priceIsManual: true, overrideReason: "Afspraak telefonisch"),
            CancellationToken.None);

        var snapshot = created.Order!.PricingSnapshot!;
        Assert.Equal(99m, snapshot.OverrideAmount);
        Assert.Equal("Afspraak telefonisch", snapshot.OverrideReason);
        Assert.NotNull(snapshot.OverriddenByUserId);
        Assert.NotNull(snapshot.OverriddenAtUtc);
        Assert.Equal(115m, snapshot.CalculatedTotal); // the original calculated amount stays preserved
    }

    [Fact]
    public async Task SnapshotHeader_SurvivesLaterTariffChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        var rule = await h.Db.Context.PriceRules.Include(r => r.Brackets).SingleAsync();
        foreach (var bracket in rule.Brackets)
        {
            bracket.Price += 1000m;
        }

        await h.Db.Context.SaveChangesAsync();

        var reloaded = await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None);
        Assert.Equal(115m, reloaded!.PricingSnapshot!.CalculatedTotal);
        Assert.Contains("115", reloaded.PricingSnapshot.Explanation);
    }

    [Fact]
    public async Task CargoDimensions_DriveBillableQuantity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // €45 per pallet; above 125×85 cm a pallet bills as two pallet places.
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Palletplaatsen", new DateOnly(2026, 1, 1), null, true, 45m, null, null,
            OversizeLengthCm: 125m, OversizeWidthCm: 85m, OversizeBillableFactor: 2m), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 1, cargoItems:
        [
            new CargoItemInput("Buitenmaat pallet", null, 1, null, null,
                LengthMeters: 1.6m, WidthMeters: 1.2m, QuantityUnitCode: "EUROPALLET"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        // 1 physical pallet, 2 billable pallet places → 2 × 45.
        Assert.Equal(90m, created.Order!.AgreedPrice);
        var line = created.Order.PricingLines!.Single(l => l.RuleName == "Palletplaatsen");
        Assert.Equal(1m, line.ActualQuantity);
        Assert.Equal(2m, line.BillableQuantity);
        // The physical cargo line still holds ONE pallet.
        Assert.Equal(1m, (await h.Db.Context.CargoItems.SingleAsync()).ExpectedQuantity);
    }

    [Fact]
    public async Task ServiceOptions_AreSnapshotted_AndBecomeSeparateInvoiceLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var voor8 = await h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("VOOR8", "Levering vóór 08:00", SurchargeKind.Fixed, 25, true, 0), CancellationToken.None);

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, quantity: 3, serviceOptionIds: [voor8.Id]), CancellationToken.None);

        Assert.Equal(140m, created.Order!.AgreedPrice); // 115 + 25
        var serviceLine = Assert.Single(created.Order.ServiceLines!);
        Assert.Equal("Levering vóór 08:00", serviceLine.Name);
        Assert.Equal(25m, serviceLine.Amount);
    }

    [Fact]
    public async Task HourlyService_WithQuantity_IsSnapshottedWithQuantityAndInvoiceDescription()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var wachttijd = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "WACHT", "Wachttijd", SurchargeKind.PerHour, 45, true, 0,
            InvoiceDescription: "Wachturen chauffeur"), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3) with
        {
            Services = [new OrderServiceInput(wachttijd.Id, 3m)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(250m, created.Order!.AgreedPrice); // 115 + 3 × 45
        var serviceLine = Assert.Single(created.Order.ServiceLines!);
        Assert.Equal(3m, serviceLine.Quantity);
        Assert.Equal(135m, serviceLine.Amount);

        // The frozen effective invoice description travels with the snapshot.
        var stored = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal("Wachturen chauffeur", stored.InvoiceDescriptionSnapshot);
        Assert.Equal(3m, stored.Quantity);
    }

    [Fact]
    public async Task AutoAppliedService_BecomesATransportOrderServiceLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: h.PalletUnitId, AutoApply: true), CancellationToken.None);

        // Never selected — the engine adds it automatically, quantified from the order's pallet quantity.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal("Picking", serviceLine.Name);
        Assert.Equal(3.75m, serviceLine.Amount); // 3 pallets × €1.25
        Assert.Equal(3m, serviceLine.Quantity);
        Assert.Equal(118.75m, created.Order.AgreedPrice); // 115 base + 3.75 auto-applied service

        var stored = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal(3m, stored.Quantity);
        Assert.Equal("Picking", stored.NameSnapshot);
    }

    [Fact]
    public async Task CustomerDisabledService_IsNeverCharged_OnTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var voor8 = await h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("VOOR8", "Levering vóór 08:00", SurchargeKind.Fixed, 25, true, 0), CancellationToken.None);
        await h.Admin.SaveCustomerConfigAsync(h.CustomerId, new SaveCustomerPricingConfigRequest(
            [], [new SaveCustomerOptionPriceRequest(voor8.Id, null, Disabled: true)]), CancellationToken.None);

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, quantity: 3, serviceOptionIds: [voor8.Id]), CancellationToken.None);

        Assert.Equal(115m, created.Order!.AgreedPrice); // base only; disabled service ignored
        Assert.Empty(created.Order.ServiceLines!);
    }
}
