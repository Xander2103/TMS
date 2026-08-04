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
        Guid TenantId, Guid CustomerId, Guid PalletUnitId, Guid UserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.Users.Add(new TransportationService.Api.Modules.Identity.Entities.User
        {
            Id = userId, TenantId = tenantId, Email = "dev@acme.test", FirstName = "Dev", LastName = "Admin",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var permissions = new PermissionSet();
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissions);
        return new Harness(db, sut, admin, permissions, tenantId, customerId, palletUnitId, userId);
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

    [Fact]
    public async Task ManualService_Note_PersistsAndSurvivesRecalculation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var picking = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0, UnitTypeId: h.PalletUnitId), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3) with
        {
            Services = [new OrderServiceInput(picking.Id, Quantity: 3m, Note: "Afgesproken met klant")],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal("Afgesproken met klant", serviceLine.Note);
        var stored = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal("Afgesproken met klant", stored.Note);

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);
        Assert.NotNull(recalculated);
        var storedAfter = await h.Db.Context.TransportOrderServiceLines.SingleAsync();
        Assert.Equal("Afgesproken met klant", storedAfter.Note);
    }

    /// <summary>Spec 7 closing requirement: a PerUnit auto-apply service bound to a unit type that is
    /// not the order's own primary unit stays informational until a cargo line in that unit shows up
    /// on the order, then becomes a real, quantified service line (Tasks 1-2 make cargo units and ids
    /// survive an update; this proves the pricing engine actually consumes them).</summary>
    [Fact]
    public async Task AddingColliLine_OnUpdate_MakesPerUnitAutoServiceApply()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var colliUnitId = Guid.NewGuid();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = colliUnitId, TenantId = h.TenantId, Code = "COLLI", Name = "colli", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var picking = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: colliUnitId, AutoApply: true), CancellationToken.None);
        var serviceLineKey = $"service:{picking.Id}";

        // 1. Create WITHOUT any Colli cargo — order is priced purely on pallets; the Colli-bound
        // auto-apply service has nothing to quantify itself from and stays informational at €0.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(115m, created.Order!.AgreedPrice);

        // The informational line carries no LineKey (only real service lines do) — locate it by label.
        var informational = Assert.Single(created.Order.PricingLines!,
            l => l.Informational && l.Label.Contains("geen", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, informational.Amount);
        Assert.Contains("colli", informational.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Picking", informational.Label[..informational.Label.IndexOf(':')]);
        Assert.DoesNotContain(created.Order.PricingLines!, l => l.LineKey == serviceLineKey);
        Assert.Empty(created.Order.ServiceLines!);

        // 2. Update: add a Colli cargo line (order's own primary unit stays Europallet).
        h.Db.Context.ChangeTracker.Clear();
        var update = BuildUpdateFrom(created.Order) with
        {
            CargoItems = [new CargoItemInput("Kleine dozen", null, 3, null, null, QuantityUnitCode: "COLLI")],
        };
        var updated = await h.Sut.UpdateAsync(created.Order.Id, update, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);

        // 3. A real, non-informational service line now exists, quantified from the Colli cargo line.
        var realLine = Assert.Single(updated.Order!.PricingLines!, l => l.LineKey == serviceLineKey);
        Assert.False(realLine.Informational);
        Assert.Equal(3m, realLine.BillableQuantity);
        Assert.Equal(3.75m, realLine.Amount); // 3 × €1.25
        var storedLine = await h.Db.Context.TransportOrderPricingLines.SingleAsync(l => l.LineKey == serviceLineKey);
        Assert.False(storedLine.Informational);
        Assert.Equal(3m, storedLine.BillableQuantity);
        Assert.Equal(3.75m, storedLine.Amount);

        // 4. The old informational "geen colli" line is gone (merged/replaced by the real line above).
        Assert.DoesNotContain(updated.Order.PricingLines!,
            l => l.Informational && l.Label.Contains("geen", StringComparison.OrdinalIgnoreCase)
                 && l.Label.Contains("colli", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Regression guard for the fix above: a PerUnit auto-apply service bound to the
    /// order's OWN primary unit (Europallet, driven by order.Quantity) must still bill from that
    /// quantity even when the order also carries cargo detail in a completely different unit
    /// (Colli) and no cargo item at all shares the order's own unit code. BuildPricingGroupsAsync
    /// only ever builds Groups from cargo items that carry a QuantityUnitCode, so Groups here
    /// contains Colli only — a Groups-preferring (wholesale) derivation would incorrectly find zero
    /// pallets and silently drop this service to €0/informational instead of billing 2 × value.</summary>
    [Fact]
    public async Task PerUnitService_BoundToOrdersOwnUnit_StillBills_WhenCargoDetailExistsOnlyInAnotherUnit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var colliUnitId = Guid.NewGuid();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = colliUnitId, TenantId = h.TenantId, Code = "COLLI", Name = "colli", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        // Bound to the order's OWN primary unit (Europallet), not Colli.
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: h.PalletUnitId, AutoApply: true), CancellationToken.None);

        // Wave 2026-08-04 §2: commercial cargo lines are the pricing source of truth as soon as
        // any carries a managed unit code — the pallet quantity must therefore live on its own
        // cargo line next to the Colli detail (a header-only pallet quantity would be the exact
        // stale-summary ambiguity this wave removes). The engine receives one line per unit and
        // the pallet-bound service bills from the Europallet line.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 2, cargoItems:
        [
            new CargoItemInput("Paletten", null, 2, null, null, QuantityUnitCode: "EUROPALLET"),
            new CargoItemInput("Kleine dozen", null, 5, null, null, QuantityUnitCode: "COLLI"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = Assert.Single(created.Order!.ServiceLines!);
        Assert.Equal("Picking", serviceLine.Name);
        Assert.Equal(2m, serviceLine.Quantity);
        Assert.Equal(2.5m, serviceLine.Amount); // 2 pallets × €1.25 — NOT €0/informational
    }

    // --- Wave 2026-08-04 §6: recalculation always uses the CURRENT goods lines ------------------

    private static async Task AddUnitTypeAsync(Harness h, string code, string name)
    {
        h.Db.Context.UnitTypes.Add(new UnitType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = code, Name = name, IsActive = true });
        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task CargoUnitChange_EuropalletToDoos_RemovesStaleAutoLine_AndReportsMissingTariff()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await AddUnitTypeAsync(h, "DOOS", "Doos");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        Assert.Equal(85m, created.Order!.AgreedPrice); // bracket 2 pallets
        var lineId = created.Order.CargoItems.Single().Id;
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Order.Id, BuildUpdateFrom(created.Order) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "DOOS", Id: lineId)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        // The stale "2 × Europallet = €85" automatic line is gone…
        Assert.DoesNotContain(updated.Order!.PricingLines!, l => l.Source == "Pallets klant X");
        // …and the engine reports the missing Doos tariff instead of pricing nothing silently.
        Assert.Contains(updated.Order.PricingLines!, l => l.Label.Contains("Doos", StringComparison.OrdinalIgnoreCase)
            && l.Source == "Ontbrekend");
        Assert.Null(updated.Order.CalculatedPrice);
    }

    [Fact]
    public async Task CargoQuantityChange_Reprices_FromCurrentLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        Assert.Equal(85m, created.Order!.AgreedPrice);
        var lineId = created.Order.CargoItems.Single().Id;
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Order.Id, BuildUpdateFrom(created.Order) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 3, null, null, QuantityUnitCode: "EUROPALLET", Id: lineId)],
        }, CancellationToken.None);

        Assert.Equal(115m, updated.Order!.AgreedPrice); // bracket 3+ pallets
    }

    [Fact]
    public async Task AddSecondGoodsLine_UnpricedUnit_KeepsBaseLine_AndAddsMissingTariffLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await AddUnitTypeAsync(h, "COLLI", "Colli");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var lineId = created.Order!.CargoItems.Single().Id;
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Order.Id, BuildUpdateFrom(created.Order) with
        {
            CargoItems =
            [
                new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET", Id: lineId),
                new CargoItemInput(null, null, 4, null, null, QuantityUnitCode: "COLLI"),
            ],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var pallets = Assert.Single(updated.Order!.PricingLines!, l => l.Source == "Pallets klant X");
        Assert.Equal(85m, pallets.Amount);
        Assert.Contains(updated.Order.PricingLines!, l => l.Source == "Ontbrekend"
            && l.Label.Contains("Colli", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManualPriceLine_SurvivesGoodsChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        var lineId = created.Order.CargoItems.Single().Id;
        var manual = await h.Sut.SaveOrderPriceLinesAsync(orderId,
            [new SaveOrderPriceLineRequest(null, "Extra handling", null, null, 10m, null)], CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, manual.Outcome);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        var updated = await h.Sut.UpdateAsync(orderId, BuildUpdateFrom(reloaded!) with
        {
            CargoItems = [new CargoItemInput(null, null, 3, null, null, QuantityUnitCode: "EUROPALLET", Id: lineId)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var manualLine = Assert.Single(updated.Order!.PricingLines!, l => l.Kind == OrderPriceLineKind.Manual);
        Assert.Equal(10m, manualLine.Amount);
        Assert.Equal(125m, updated.Order.AgreedPrice); // 115 repriced + 10 manual
    }

    [Fact]
    public async Task LockedPricing_RejectsCargoQuantityChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        var lineId = created.Order.CargoItems.Single().Id;
        await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            h.Sut.UpdateAsync(orderId, BuildUpdateFrom(reloaded!) with
            {
                CargoItems = [new CargoItemInput(null, null, 5, null, null, QuantityUnitCode: "EUROPALLET", Id: lineId)],
            }, CancellationToken.None));

        // The frozen price is untouched.
        h.Db.Context.ChangeTracker.Clear();
        var after = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        Assert.Equal(85m, after!.AgreedPrice);
        Assert.Equal(2m, after.CargoItems.Single().ExpectedQuantity);
    }

    [Fact]
    public async Task LockedPricing_AllowsUnrelatedNotesEdit_WithCargoLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        var updated = await h.Sut.UpdateAsync(orderId,
            BuildUpdateFrom(reloaded!) with { Notes = "Chauffeur eerst bellen" }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(85m, updated.Order!.AgreedPrice);
        Assert.Equal("Chauffeur eerst bellen", updated.Order.Notes);
    }

    // --- Wave 2026-08-04 §7: pricing coverage per goods line ------------------------------------

    [Fact]
    public async Task Coverage_MixedUnits_ReportsFullAndNone_OnSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await AddUnitTypeAsync(h, "DOOS", "Doos");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
        [
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET"),
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "DOOS"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var coverage = created.Order!.PricingSnapshot!.Coverage!;
        var pallet = Assert.Single(coverage, c => c.UnitLabel == "Europallet");
        Assert.Equal("Full", pallet.Status);
        Assert.Equal(85m, pallet.BaseAmount);
        Assert.Equal("Pallets klant X", pallet.BaseRuleName);
        var doos = Assert.Single(coverage, c => c.UnitLabel == "Doos");
        Assert.Equal("None", doos.Status);
        Assert.Equal(2m, doos.Quantity);
        Assert.Equal("Geen passend basistarief", doos.Reason);
    }

    [Fact]
    public async Task Coverage_PerUnitServiceOnUnpricedUnit_IsPartial_NeverFull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        var doosUnitId = Guid.NewGuid();
        h.Db.Context.UnitTypes.Add(new UnitType { Id = doosUnitId, TenantId = h.TenantId, Code = "DOOS", Name = "Doos", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: doosUnitId, AutoApply: true), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
        [
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "DOOS"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var doos = Assert.Single(created.Order!.PricingSnapshot!.Coverage!, c => c.UnitLabel == "Doos");
        // Picking bills the Doos, but a service never counts as transport pricing.
        Assert.Equal("Partial", doos.Status);
        Assert.Equal(2.5m, doos.ServicesAmount);
        Assert.Equal(0m, doos.BaseAmount);
        Assert.Equal("Geen passend basistarief", doos.Reason);
    }

    [Fact]
    public async Task Coverage_UncodedCargoLine_ReportsNone_WithMissingUnitReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
        [
            new CargoItemInput("Losse goederen", null, 3, "zakken", null),
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET"),
        ]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var coverage = created.Order!.PricingSnapshot!.Coverage!;
        var uncoded = Assert.Single(coverage, c => c.UnitLabel == "zakken");
        Assert.Equal("None", uncoded.Status);
        Assert.Equal("Geen eenheid gekozen voor deze goederenlijn", uncoded.Reason);
        Assert.Equal("Full", Assert.Single(coverage, c => c.UnitLabel == "Europallet").Status);
    }

    // --- Wave 2026-08-04 §8/§10: Prijs bevestigen / Prijs aanpassen -----------------------------

    [Fact]
    public async Task ConfirmPrice_FullCoverage_Locks_AndStampsConfirmer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var confirmed = await h.Sut.ConfirmOrderPricingAsync(created.Order!.Id, null, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
        var snapshot = confirmed.Order!.PricingSnapshot!;
        Assert.Equal(OrderPricingStatus.Locked, snapshot.Status);
        Assert.Equal(Now.UtcDateTime, snapshot.ConfirmedAtUtc);
        Assert.Equal(h.UserId, snapshot.ConfirmedByUserId);
        Assert.Equal("Dev Admin", snapshot.ConfirmedByName);
        Assert.Null(snapshot.ConfirmedWithUnpricedGoodsReason);
        // Order status stays a separate concept — confirming the price never confirms the order.
        Assert.Equal(TransportOrderStatus.Draft, confirmed.Order.Status);
    }

    [Fact]
    public async Task ConfirmPrice_UnpricedGoods_IsBlocked_WithoutOverridePermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await AddUnitTypeAsync(h, "DOOS", "Doos");
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
        [
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET"),
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "DOOS"),
        ]), CancellationToken.None);
        var confirmed = await h.Sut.ConfirmOrderPricingAsync(created.Order!.Id, null, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, confirmed.Outcome);
        Assert.Contains("kan niet worden bevestigd", confirmed.Error!);
        Assert.Contains("2 Doos", confirmed.Error!);
    }

    [Fact]
    public async Task ConfirmPrice_UnpricedGoods_WithPermission_RequiresReason_AndKeepsWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await AddUnitTypeAsync(h, "DOOS", "Doos");
        h.Permissions.Codes.Add("orders.lock_price");
        h.Permissions.Codes.Add("orders.confirm_incomplete_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
        [
            new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "DOOS"),
        ]), CancellationToken.None);

        var withoutReason = await h.Sut.ConfirmOrderPricingAsync(created.Order!.Id, "  ", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, withoutReason.Outcome);
        Assert.Contains("reden", withoutReason.Error!, StringComparison.OrdinalIgnoreCase);

        var confirmed = await h.Sut.ConfirmOrderPricingAsync(created.Order!.Id, "Prijs volgt via creditnota", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
        Assert.Equal(OrderPricingStatus.Locked, confirmed.Order!.PricingSnapshot!.Status);
        Assert.Equal("Prijs volgt via creditnota", confirmed.Order.PricingSnapshot.ConfirmedWithUnpricedGoodsReason);
    }

    [Fact]
    public async Task ConfirmPrice_RequiresLockPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var confirmed = await h.Sut.ConfirmOrderPricingAsync(created.Order!.Id, null, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, confirmed.Outcome);
        Assert.Contains("geen rechten", confirmed.Error!);
    }

    [Fact]
    public async Task ReopenPrice_RequiresReason_ReturnsToDraft_AndAuditsOldTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        await h.Sut.ConfirmOrderPricingAsync(orderId, null, CancellationToken.None);

        var withoutReason = await h.Sut.ReopenOrderPricingAsync(orderId, " ", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, withoutReason.Outcome);

        var reopened = await h.Sut.ReopenOrderPricingAsync(orderId, "Extra kost vergeten", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, reopened.Outcome);
        var snapshot = reopened.Order!.PricingSnapshot!;
        Assert.Equal(OrderPricingStatus.Draft, snapshot.Status);
        Assert.Null(snapshot.ConfirmedAtUtc);
        Assert.Null(snapshot.ConfirmedByName);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "OrderPricing" && a.Action == "price_reopened")
            .OrderByDescending(a => a.Id)
            .FirstAsync();
        Assert.Contains("85", audit.OldValuesJson);          // old total stays in the trail
        Assert.Contains("Extra kost vergeten", audit.NewValuesJson);

        // Reconfirmation works after editing.
        var reconfirmed = await h.Sut.ConfirmOrderPricingAsync(orderId, null, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, reconfirmed.Outcome);
        Assert.Equal(OrderPricingStatus.Locked, reconfirmed.Order!.PricingSnapshot!.Status);
    }

    [Fact]
    public async Task ConfirmedPrice_BlocksRecalculation_UntilReopened()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.lock_price");
        h.Permissions.Codes.Add("orders.edit");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        await h.Sut.ConfirmOrderPricingAsync(orderId, null, CancellationToken.None);

        var recalc = await h.Sut.RecalculateOrderPricingAsync(orderId, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, recalc.Outcome);

        await h.Sut.ReopenOrderPricingAsync(orderId, "Aanpassing nodig", CancellationToken.None);
        var afterReopen = await h.Sut.RecalculateOrderPricingAsync(orderId, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, afterReopen.Outcome);
    }

    // --- Wave 2026-08-04 §16: stop time requirements drive configured surcharges ---------------

    [Fact]
    public async Task StopTimeRequirement_TriggersConfiguredSurcharge_AndRemovalRemovesIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "VOOR10", "Levering vóór 10u", SurchargeKind.Fixed, 35m, true, 0,
            AutoApply: true,
            TimeConditions: [new ServiceTimeConditionDto(
                ServiceConditionKind.StopTimeBefore,
                ServiceConditionStopScope.Unloading,
                new TimeOnly(10, 0))]), CancellationToken.None);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]) with
        {
            Stops =
            [
                Stop(StopType.Loading, "Antwerpen"),
                Stop(StopType.Unloading, "Hasselt", "3500") with
                {
                    TimeRequirement = StopTimeRequirementKind.Before,
                    TimeRequirementTo = new TimeOnly(9, 30),
                },
            ],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(120m, created.Order!.AgreedPrice); // 85 bracket + 35 surcharge
        Assert.Contains(created.Order.ServiceLines!, l => l.Name == "Levering vóór 10u");
        h.Db.Context.ChangeTracker.Clear();

        // Dropping the requirement must also drop the surcharge (§6: no stale automatic lines).
        var reloaded = await h.Sut.GetByIdAsync(created.Order.Id, CancellationToken.None);
        var withoutRequirement = BuildUpdateFrom(reloaded!) with
        {
            Stops = reloaded!.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions)).ToList(),
        };
        var updated = await h.Sut.UpdateAsync(created.Order.Id, withoutRequirement, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(85m, updated.Order!.AgreedPrice);
        Assert.DoesNotContain(updated.Order.ServiceLines ?? [], l => l.Name == "Levering vóór 10u");
    }

    [Fact]
    public async Task LockedPricing_RejectsStopTimeRequirementChange_ButAllowsIdenticalStops()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPalletBracketsAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, cargoItems:
            [new CargoItemInput(null, null, 2, null, null, QuantityUnitCode: "EUROPALLET")]), CancellationToken.None);
        var orderId = created.Order!.Id;
        await h.Sut.ConfirmOrderPricingAsync(orderId, null, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        // Identical stops (notes-only edit) stay possible on a confirmed price.
        var notesOnly = await h.Sut.UpdateAsync(orderId,
            BuildUpdateFrom(reloaded!) with { Notes = "OK" }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, notesOnly.Outcome);
        h.Db.Context.ChangeTracker.Clear();

        // Changing a stop's time requirement is a pricing input → refused while confirmed.
        var reloaded2 = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        var changed = BuildUpdateFrom(reloaded2!) with
        {
            Stops = reloaded2!.Stops.Select((s, i) => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
                TimeRequirement: i == 1 ? StopTimeRequirementKind.Before : StopTimeRequirementKind.None,
                TimeRequirementTo: i == 1 ? new TimeOnly(10, 0) : null)).ToList(),
        };
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            h.Sut.UpdateAsync(orderId, changed, CancellationToken.None));
    }

    private static UpdateTransportOrderRequest BuildUpdateFrom(TransportOrderDetailDto d)
    {
        var stopIndexById = d.Stops
            .Select((s, i) => (s.Id, Index: i))
            .ToDictionary(x => x.Id, x => x.Index);

        return new UpdateTransportOrderRequest(
            d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
            d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
            d.AgreedPrice, d.Notes,
            d.Stops.Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList(),
            CargoItems: d.CargoItems.Select(c => new CargoItemInput(
                    c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes,
                    c.UnitType, c.UnitTypeLabel, c.TotalWeightKg, c.WeightPerUnitKg,
                    c.LengthMeters, c.WidthMeters, c.HeightMeters, c.VolumeM3, c.VolumeIsManual,
                    c.AdrRequired, c.AdrDetails, c.Stackable, c.Reference,
                    c.LoadingStopId is { } lid && stopIndexById.TryGetValue(lid, out var li) ? li : null,
                    c.UnloadingStopId is { } uid && stopIndexById.TryGetValue(uid, out var ui) ? ui : null,
                    c.QuantityUnitCode, Id: c.Id))
                .ToList(),
            QuantityUnitCode: d.QuantityUnitCode,
            ServiceOptionIds: null,
            Services: d.ServiceLines?.Select(s => new OrderServiceInput(s.ServiceOptionId ?? Guid.Empty, s.Quantity, Note: s.Note)).ToList(),
            PriceIsManual: d.PriceIsManual,
            PriceOverrideReason: d.PriceOverrideReason);
    }
}
