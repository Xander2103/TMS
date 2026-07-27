using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
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
/// Spec ch. 24-26: line-level manual price corrections/removals/free additions with preserved
/// originals, the Draft → Reviewed → Locked → Invoiced status lifecycle and the merge-on-recalc
/// that replaces the old delete-all-rewrite (Manual/AutoAdjusted lines survive a recalculation).
/// </summary>
public class OrderPricingLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 27, 12, 0, 0, TimeSpan.Zero);

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

    private static CreateTransportOrderRequest Request(Guid customerId, decimal quantity) => new(
        customerId, "REF-1", new DateOnly(2026, 7, 27), "Pallets", quantity, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        QuantityUnitCode: "EUROPALLET");

    private static UpdateTransportOrderRequest UpdateRequest(TransportOrderDetailDto order, string? notes = null) => new(
        order.CustomerId, order.CustomerReference, order.OrderDate, order.GoodsDescription, order.Quantity,
        order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount, order.AdrRequired, order.CraneRequired,
        order.AgreedPrice, notes ?? order.Notes,
        order.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
            .ToList(),
        QuantityUnitCode: order.QuantityUnitCode,
        PriceIsManual: order.PriceIsManual, PriceOverrideReason: order.PriceOverrideReason,
        PricingSource: order.PricingSource, OneOffFixedAmount: order.OneOffFixedAmount,
        OneOffIncludedLoadingMinutes: order.OneOffIncludedLoadingMinutes,
        OneOffIncludedUnloadingMinutes: order.OneOffIncludedUnloadingMinutes,
        OneOffIncludedCombinedMinutes: order.OneOffIncludedCombinedMinutes,
        OneOffExtraHourlyRate: order.OneOffExtraHourlyRate, OneOffNotes: order.OneOffNotes);

    /// <summary>€1.25/pallet auto-applied "Picking" service (Phase 5 auto-apply) + a per-pallet base rule.</summary>
    private static async Task SeedRulesAsync(Harness h)
    {
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets klant X", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "PICK", "Picking", SurchargeKind.PerUnit, 1.25m, true, 0,
            UnitTypeId: h.PalletUnitId, AutoApply: true), CancellationToken.None);
    }

    private static OrderPricingLineDto ServiceLine(TransportOrderDetailDto order) =>
        order.PricingLines!.Single(l => l.LineKey != null && l.LineKey.StartsWith("service:"));

    private static OrderPricingLineDto RuleLine(TransportOrderDetailDto order) =>
        order.PricingLines!.Single(l => l.LineKey != null && l.LineKey.StartsWith("rule:"));

    // --- S16: adjust an auto (service) line, originals preserved ----------------------------

    [Fact]
    public async Task S16_AdjustAutoServiceLine_PreservesOriginals_AndReflectsInAgreedPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);

        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 8), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var serviceLine = ServiceLine(created.Order!);
        Assert.Equal(10.00m, serviceLine.Amount); // 8 × 1.25
        Assert.Equal(8m, serviceLine.Quantity);
        Assert.Equal(1.25m, serviceLine.UnitPrice);
        var baseTotal = created.Order!.AgreedPrice! - serviceLine.Amount;

        var saved = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order.Id,
            [new SaveOrderPriceLineRequest(serviceLine.LineKey, serviceLine.Label, 6m, null, null, "Hertelling door magazijn")],
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, saved.Outcome);
        var adjusted = ServiceLine(saved.Order!);
        Assert.Equal(OrderPriceLineKind.AutoAdjusted, adjusted.Kind);
        Assert.Equal(7.50m, adjusted.Amount);
        Assert.Equal(6m, adjusted.Quantity);
        Assert.Equal(1.25m, adjusted.UnitPrice);
        Assert.Equal(10.00m, adjusted.OriginalAmount);
        Assert.Equal(8m, adjusted.OriginalQuantity);
        Assert.Equal(1.25m, adjusted.OriginalUnitPrice);
        Assert.Equal("Hertelling door magazijn", adjusted.AdjustReason);
        Assert.Equal(baseTotal + 7.50m, saved.Order!.AgreedPrice);

        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "OrderPricing" && a.Action == "lines_adjusted");
        Assert.NotNull(audit.NewValuesJson);
    }

    // --- S17: free manual line survives a recalculation --------------------------------------

    [Fact]
    public async Task S17_FreeManualLine_PersistsAndSurvivesRecalculation_CountsInLinesTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var baseline = created.Order!.AgreedPrice!.Value;

        var saved = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order.Id,
            [new SaveOrderPriceLineRequest(null, "Extra behandeling beschadigde pallet", null, null, 35m, null)],
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, saved.Outcome);
        Assert.Equal(baseline + 35m, saved.Order!.AgreedPrice);
        var manualLine = saved.Order.PricingLines!.Single(l => l.Kind == OrderPriceLineKind.Manual);
        Assert.Equal(35m, manualLine.Amount);

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, recalculated.Outcome);
        var survived = Assert.Single(recalculated.Order!.PricingLines!, l => l.Kind == OrderPriceLineKind.Manual);
        Assert.Equal(35m, survived.Amount);
        Assert.Equal(baseline + 35m, recalculated.Order.AgreedPrice);
    }

    // --- Remove semantics ----------------------------------------------------------------------

    [Fact]
    public async Task RemoveAutoLine_KeepsRowZerosAmount_RequiresReason_OriginalKept()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var serviceLine = ServiceLine(created.Order!);

        var withoutReason = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order!.Id, [new SaveOrderPriceLineRequest(serviceLine.LineKey, serviceLine.Label, null, null, null, null, Remove: true)],
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, withoutReason.Outcome);

        var removed = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order.Id,
            [new SaveOrderPriceLineRequest(serviceLine.LineKey, serviceLine.Label, null, null, null, "Niet geleverd", Remove: true)],
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, removed.Outcome);
        var zeroed = ServiceLine(removed.Order!);
        Assert.Equal(OrderPriceLineKind.AutoAdjusted, zeroed.Kind);
        Assert.Equal(0m, zeroed.Amount);
        Assert.Equal(3.75m, zeroed.OriginalAmount); // 3 × 1.25 preserved
        Assert.Equal("Niet geleverd", zeroed.AdjustReason);
    }

    [Fact]
    public async Task RemoveManualLine_HardDeletes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var added = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order!.Id, [new SaveOrderPriceLineRequest(null, "Vrije regel", null, null, 20m, null)], CancellationToken.None);
        var manualKey = added.Order!.PricingLines!.Single(l => l.Kind == OrderPriceLineKind.Manual).LineKey;

        var removed = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order.Id, [new SaveOrderPriceLineRequest(manualKey, "Vrije regel", null, null, null, null, Remove: true)],
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, removed.Outcome);
        Assert.DoesNotContain(removed.Order!.PricingLines!, l => l.LineKey == manualKey);
    }

    // --- Recalculation refreshes the engine baseline, never the user's own value --------------

    [Fact]
    public async Task Recalculate_RefreshesOriginalAmount_KeepsUsersAdjustedAmount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var ruleLine = RuleLine(created.Order!);
        Assert.Equal(90m, ruleLine.Amount); // 3 × 30

        var adjusted = await h.Sut.SaveOrderPriceLinesAsync(
            created.Order!.Id,
            [new SaveOrderPriceLineRequest(ruleLine.LineKey, ruleLine.Label, null, null, 200m, "Onderhandelde prijs")],
            CancellationToken.None);
        Assert.Equal(200m, RuleLine(adjusted.Order!).Amount);

        var rule = await h.Db.Context.PriceRules.SingleAsync();
        rule.UnitPrice = 50m; // 3 × 50 = 150 on the next calculation
        await h.Db.Context.SaveChangesAsync();

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);
        var refreshed = RuleLine(recalculated.Order!);
        Assert.Equal(200m, refreshed.Amount); // user's value untouched
        Assert.Equal(150m, refreshed.OriginalAmount); // baseline refreshed to the new engine amount
    }

    // --- Orphaned adjustment: the rule disappears -----------------------------------------------

    [Fact]
    public async Task OrphanedAdjustedLine_BecomesManual_WhenItsRuleIsDeactivated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var ruleLine = RuleLine(created.Order!);
        await h.Sut.SaveOrderPriceLinesAsync(
            created.Order!.Id,
            [new SaveOrderPriceLineRequest(ruleLine.LineKey, ruleLine.Label, null, null, 200m, "Onderhandelde prijs")],
            CancellationToken.None);

        var rule = await h.Db.Context.PriceRules.SingleAsync();
        rule.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);
        var orphan = Assert.Single(recalculated.Order!.PricingLines!, l => l.LineKey == ruleLine.LineKey);
        Assert.Equal(OrderPriceLineKind.Manual, orphan.Kind);
        Assert.Equal(200m, orphan.Amount);
    }

    // --- Status lifecycle ------------------------------------------------------------------------

    [Fact]
    public async Task StatusFlow_DraftReviewedLockedReviewed_RequiresLockPermissionForLockedTransitions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var orderId = created.Order!.Id;
        Assert.Equal(OrderPricingStatus.Draft, created.Order.PricingSnapshot!.Status);

        h.Permissions.Codes.Add("orders.edit");
        var reviewed = await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Reviewed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, reviewed.Outcome);
        Assert.Equal(OrderPricingStatus.Reviewed, reviewed.Order!.PricingSnapshot!.Status);

        // Locking requires orders.lock_price — orders.edit alone is refused.
        var denied = await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, denied.Outcome);

        h.Permissions.Codes.Add("orders.lock_price");
        var locked = await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, locked.Outcome);
        Assert.Equal(OrderPricingStatus.Locked, locked.Order!.PricingSnapshot!.Status);

        var unlocked = await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Reviewed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, unlocked.Outcome);
        Assert.Equal(OrderPricingStatus.Reviewed, unlocked.Order!.PricingSnapshot!.Status);

        // Invoiced is never reachable via this endpoint.
        var invoicedAttempt = await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Invoiced, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, invoicedAttempt.Outcome);
    }

    [Fact]
    public async Task Reviewed_AllowsRecalculate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        await h.Sut.SetOrderPricingStatusAsync(created.Order!.Id, OrderPricingStatus.Reviewed, CancellationToken.None);

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, recalculated.Outcome);
        Assert.Equal(OrderPricingStatus.Reviewed, recalculated.Order!.PricingSnapshot!.Status);
    }

    // --- Locked blocks every pricing mutation, including the order save itself -----------------

    [Fact]
    public async Task Locked_Blocks_Lines_Recalculate_Confirm_AndPricingRelevantOrderSave()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var orderId = created.Order!.Id;
        await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);
        var ruleLine = RuleLine(created.Order);

        var lineEdit = await h.Sut.SaveOrderPriceLinesAsync(
            orderId, [new SaveOrderPriceLineRequest(ruleLine.LineKey, ruleLine.Label, null, null, 1m, "reden")], CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, lineEdit.Outcome);

        var recalc = await h.Sut.RecalculateOrderPricingAsync(orderId, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, recalc.Outcome);

        var confirm = await h.Sut.ConfirmOrderPriceLineAsync(orderId, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, confirm.Outcome);

        // A non-pricing edit (notes only) still succeeds; pricing rows stay untouched.
        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        var notesOnly = await h.Sut.UpdateAsync(orderId, UpdateRequest(reloaded!, notes: "Bijkomende opmerking"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, notesOnly.Outcome);
        Assert.Equal(reloaded!.AgreedPrice, notesOnly.Order!.AgreedPrice);
        Assert.Equal("Bijkomende opmerking", notesOnly.Order.Notes);

        // A pricing-relevant edit (switching to a manual override) is refused with a validation error.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.UpdateAsync(
            orderId, UpdateRequest(reloaded) with { PriceIsManual = true, AgreedPrice = 999m, PriceOverrideReason = "Test" },
            CancellationToken.None));
    }

    // --- S18: locked snapshot survives a tariff change; explicit recalculate is rejected -------

    [Fact]
    public async Task S18_Locked_TariffChanged_RecalculateRejected_TotalsUnchanged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var orderId = created.Order!.Id;
        var priceBefore = created.Order.AgreedPrice;
        await h.Sut.SetOrderPricingStatusAsync(orderId, OrderPricingStatus.Locked, CancellationToken.None);

        var rule = await h.Db.Context.PriceRules.SingleAsync();
        rule.UnitPrice = 999m;
        await h.Db.Context.SaveChangesAsync();

        var recalc = await h.Sut.RecalculateOrderPricingAsync(orderId, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, recalc.Outcome);
        var reloaded = await h.Sut.GetByIdAsync(orderId, CancellationToken.None);
        Assert.Equal(priceBefore, reloaded!.AgreedPrice);
        Assert.Equal(RuleLine(created.Order).Amount, RuleLine(reloaded).Amount);
    }

    // --- Whole-order manual override still wins over LinesTotal (back-compat) ------------------

    [Fact]
    public async Task WholeOrderOverride_StillWinsOverLinesTotal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        h.Permissions.Codes.Add("orders.override_price");
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);

        var overridden = await h.Sut.UpdateAsync(
            created.Order!.Id,
            UpdateRequest(created.Order) with { PriceIsManual = true, AgreedPrice = 500m, PriceOverrideReason = "Speciale afspraak" },
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, overridden.Outcome);
        Assert.Equal(500m, overridden.Order!.AgreedPrice);
        Assert.True(overridden.Order.PriceIsManual);
        // LinesTotal keeps reflecting the calculated lines, but never wins while the override is on.
        Assert.NotEqual(500m, overridden.Order.PricingSnapshot!.LinesTotal);
    }

    // --- Tenant isolation on every new endpoint --------------------------------------------------

    [Fact]
    public async Task NewEndpoints_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedRulesAsync(h);
        h.Permissions.Codes.Add("orders.edit");
        h.Permissions.Codes.Add("orders.lock_price");
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, quantity: 3), CancellationToken.None);
        var ruleLine = RuleLine(created.Order!);

        // A second tenant's service instance must never see the first tenant's order.
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        var otherTenant = new DevTenantContext(otherTenantId);
        var otherCurrentUser = new DevCurrentUserContext(Guid.NewGuid());
        var otherAudit = new AuditService(h.Db.Context, otherTenant, otherCurrentUser);
        var otherPermissions = new PermissionSet();
        otherPermissions.Codes.Add("orders.edit");
        otherPermissions.Codes.Add("orders.lock_price");
        var otherSut = new TransportOrderService(
            h.Db.Context, otherTenant, otherAudit, new TestClock(Now), new PricingEngine(h.Db.Context, otherTenant), otherCurrentUser, otherPermissions);

        Assert.Equal(TransportOrderOperationOutcome.NotFound, (await otherSut.SaveOrderPriceLinesAsync(
            created.Order!.Id, [new SaveOrderPriceLineRequest(ruleLine.LineKey, ruleLine.Label, null, null, 1m, "reden")], CancellationToken.None)).Outcome);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, (await otherSut.RecalculateOrderPricingAsync(created.Order.Id, CancellationToken.None)).Outcome);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, (await otherSut.SetOrderPricingStatusAsync(
            created.Order.Id, OrderPricingStatus.Reviewed, CancellationToken.None)).Outcome);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, (await otherSut.ConfirmOrderPriceLineAsync(
            created.Order.Id, Guid.NewGuid(), CancellationToken.None)).Outcome);
    }
}
