using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Services;
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
/// Master sprint 2026-09-21 D4 — sales line ↔ goods line links (keyed on LineKey) and the derived
/// <c>CommercialCoverage</c> per goods line. Links are a CONTROL relation: they survive a
/// recalculation, disappear with their line or goods line, and never change a price or an invoice.
/// </summary>
public class OrderPriceLineCargoLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin, Guid TenantId, Guid CustomerId, Guid PalletUnitId);

    private static async Task<Harness> SeedAsync(bool withTariff = true)
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
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), new PricingEngine(db.Context, tenant), currentUser, new PermissionSet());
        if (withTariff)
        {
            await admin.CreateRuleAsync(new SavePriceRuleRequest(
                customerId, palletUnitId, PriceRuleBasis.PerUnit, null,
                "Pallets klant X", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        }

        return new Harness(db, sut, admin, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null) =>
        new(type, null, null, null, postalCode, city, "BE", null, null, null, null);

    /// <summary>Two goods lines: 8 coded pallets (priced by the tariff) and one crate without a unit (nothing prices it).</summary>
    private static CreateTransportOrderRequest Request(Guid customerId, decimal? oneOff = null) => new(
        customerId, "REF-1", new DateOnly(2026, 9, 21), "Pallets + kist", null, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
        CargoItems:
        [
            new CargoItemInput("Pallets", null, 8, "pal", null, QuantityUnitCode: "EUROPALLET"),
            new CargoItemInput("Losse kist", null, 1, null, null),
        ],
        PricingSource: oneOff is null ? OrderPricingSource.Contract : OrderPricingSource.OneOff,
        OneOffFixedAmount: oneOff);

    private static UpdateTransportOrderRequest UpdateRequest(TransportOrderDetailDto order, IEnumerable<CargoItemDto> cargo) => new(
        order.CustomerId, order.CustomerReference, order.OrderDate, order.GoodsDescription, order.Quantity,
        order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount, order.AdrRequired, order.CraneRequired,
        order.AgreedPrice, order.Notes,
        order.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions, Id: s.Id))
            .ToList(),
        CargoItems: cargo.Select(c => new CargoItemInput(
            c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes, QuantityUnitCode: c.QuantityUnitCode, Id: c.Id)).ToList(),
        QuantityUnitCode: order.QuantityUnitCode,
        PricingSource: order.PricingSource, OneOffFixedAmount: order.OneOffFixedAmount);

    private static CargoItemDto Pallets(TransportOrderDetailDto order) => order.CargoItems.Single(c => c.Description == "Pallets");

    private static CargoItemDto Crate(TransportOrderDetailDto order) => order.CargoItems.Single(c => c.Description == "Losse kist");

    private static OrderPricingLineDto ManualLine(TransportOrderDetailDto order) =>
        order.PricingLines!.Single(l => l.Kind == OrderPriceLineKind.Manual);

    private static async Task<TransportOrderDetailDto> CreateAsync(Harness h, decimal? oneOff = null)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId, oneOff), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        return created.Order!;
    }

    private static async Task<TransportOrderDetailDto> SaveLinesAsync(Harness h, Guid orderId, params SaveOrderPriceLineRequest[] lines)
    {
        var saved = await h.Sut.SaveOrderPriceLinesAsync(orderId, lines, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, saved.Outcome);
        return saved.Order!;
    }

    // ------------------------------------------------------------ coverage: the three outcomes

    [Fact]
    public async Task Coverage_Included_ToReview_AndSeparatelyPriced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateAsync(h);
        Assert.Equal(240m, order.AgreedPrice); // 8 × 30

        // Unit coverage "Full" for the pallets; nothing prices the unit-less crate.
        Assert.Equal(CargoCommercialCoverage.Included, Pallets(order).CommercialCoverage);
        Assert.Equal(CargoCommercialCoverage.ToReview, Crate(order).CommercialCoverage);

        var saved = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order).Id]));

        Assert.Equal(CargoCommercialCoverage.SeparatelyPriced, Crate(saved).CommercialCoverage);
        Assert.Equal(CargoCommercialCoverage.Included, Pallets(saved).CommercialCoverage);
        Assert.Equal(new[] { Crate(order).Id }, ManualLine(saved).CargoItemIds);
        Assert.Equal(285m, saved.AgreedPrice);
        var link = await h.Db.Context.OrderPriceLineCargoLinks.AsNoTracking().SingleAsync();
        Assert.Equal((h.TenantId, order.Id, ManualLine(saved).LineKey, Crate(order).Id),
            (link.TenantId, link.TransportOrderId, link.LineKey, link.CargoItemId));
    }

    [Fact]
    public async Task Coverage_FixedPriceIncludesEverything_AndAnUnpricedOrderIncludesNothing()
    {
        var h = await SeedAsync(withTariff: false);
        using var _ = h.Db;

        var unpriced = await CreateAsync(h);
        Assert.False(OrderPricingState.IsPriced(unpriced.PriceIsManual, unpriced.PricingSource, unpriced.OneOffFixedAmount, unpriced.AgreedPrice));
        Assert.All(unpriced.CargoItems, c => Assert.Equal(CargoCommercialCoverage.ToReview, c.CommercialCoverage));

        var oneOff = await CreateAsync(h, oneOff: 500m);
        Assert.All(oneOff.CargoItems, c => Assert.Equal(CargoCommercialCoverage.Included, c.CommercialCoverage));
    }

    // ------------------------------------------------------------ save / replace / reject

    [Fact]
    public async Task LinkOnlySave_ReplacesTheLinks_WithoutTouchingThePriceOrTheLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var withLine = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order).Id]));
        var manualKey = ManualLine(withLine).LineKey;

        // Replace: the manual line now prices the pallets instead of the crate (label echoed by the client).
        var replaced = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(manualKey, "Kist apart", null, null, null, null, CargoItemIds: [Pallets(order).Id]));

        Assert.Equal(new[] { Pallets(order).Id }, ManualLine(replaced).CargoItemIds);
        Assert.Equal(CargoCommercialCoverage.SeparatelyPriced, Pallets(replaced).CommercialCoverage);
        Assert.Equal(CargoCommercialCoverage.ToReview, Crate(replaced).CommercialCoverage);
        Assert.Equal(withLine.AgreedPrice, replaced.AgreedPrice);
        Assert.Equal(45m, ManualLine(replaced).Amount);

        // An ENGINE line can be linked without an adjust reason and stays an untouched Auto line.
        var ruleLine = replaced.PricingLines!.Single(l => l.LineKey != null && l.LineKey.StartsWith("rule:"));
        var ruleLinked = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(ruleLine.LineKey, "", null, null, null, null, CargoItemIds: [Pallets(order).Id]));
        var ruleAfter = ruleLinked.PricingLines!.Single(l => l.LineKey == ruleLine.LineKey);
        Assert.Equal((OrderPriceLineKind.Auto, ruleLine.Amount, (string?)null), (ruleAfter.Kind, ruleAfter.Amount, ruleAfter.AdjustReason));
        Assert.Equal(new[] { Pallets(order).Id }, ruleAfter.CargoItemIds);

        // An empty list clears; null leaves untouched.
        var cleared = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(manualKey, "Kist apart", null, null, null, null, CargoItemIds: []));
        Assert.Null(ManualLine(cleared).CargoItemIds);
        Assert.Equal(1, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync()); // the rule link stays
    }

    [Fact]
    public async Task ACargoItemOfAnotherOrder_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var other = await CreateAsync(h);

        var result = await h.Sut.SaveOrderPriceLinesAsync(order.Id,
            [new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(other).Id])],
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(0, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync());
    }

    // ------------------------------------------------------------ lifecycle

    [Fact]
    public async Task Links_SurviveARecalculation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var ruleKey = order.PricingLines!.Single(l => l.LineKey != null && l.LineKey.StartsWith("rule:")).LineKey;
        var linked = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order).Id]),
            new SaveOrderPriceLineRequest(ruleKey, "", null, null, null, null, CargoItemIds: [Pallets(order).Id]));
        var ruleRowId = linked.PricingLines!.Single(l => l.LineKey == ruleKey).Id;

        var recalculated = await h.Sut.RecalculateOrderPricingAsync(order.Id, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, recalculated.Outcome);
        // The Auto row was rewritten (new id) — its link followed the LineKey.
        var ruleAfter = recalculated.Order!.PricingLines!.Single(l => l.LineKey == ruleKey);
        Assert.NotEqual(ruleRowId, ruleAfter.Id);
        Assert.Equal(new[] { Pallets(order).Id }, ruleAfter.CargoItemIds);
        Assert.Equal(new[] { Crate(order).Id }, ManualLine(recalculated.Order).CargoItemIds);
        Assert.Equal(CargoCommercialCoverage.SeparatelyPriced, Crate(recalculated.Order).CommercialCoverage);
        Assert.Equal(2, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync());
    }

    [Fact]
    public async Task Links_GoWithTheirGoodsLine_AndWithTheirSalesLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateAsync(h);
        var linked = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null, CargoItemIds: [Crate(order).Id, Pallets(order).Id]));
        Assert.Equal(2, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync());

        // The crate is removed from the order (soft delete — no FK cascade helps here).
        var updated = await h.Sut.UpdateAsync(order.Id, UpdateRequest(linked, [Pallets(linked)]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(new[] { Pallets(order).Id }, ManualLine(updated.Order!).CargoItemIds);
        Assert.Equal(Pallets(order).Id, (await h.Db.Context.OrderPriceLineCargoLinks.AsNoTracking().SingleAsync()).CargoItemId);

        // Removing the manual line takes its remaining link along.
        var removed = await SaveLinesAsync(h, order.Id,
            new SaveOrderPriceLineRequest(ManualLine(updated.Order!).LineKey, "Kist apart", null, null, null, null, Remove: true));
        Assert.DoesNotContain(removed.PricingLines!, l => l.Kind == OrderPriceLineKind.Manual);
        Assert.Equal(0, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync());
        Assert.Equal(CargoCommercialCoverage.Included, Pallets(removed).CommercialCoverage);
    }

    // ------------------------------------------------------------ invoicing is untouched

    [Fact]
    public async Task ALinkedLine_NeverChangesTheInvoice()
    {
        async Task<InvoiceDetailDto> InvoiceAsync(bool withLink)
        {
            var h = await SeedAsync();
            using var _ = h.Db;
            var order = await CreateAsync(h);
            var saved = await SaveLinesAsync(h, order.Id,
                new SaveOrderPriceLineRequest(null, "Kist apart", 1m, 45m, null, null,
                    CargoItemIds: withLink ? [Crate(order).Id] : null));
            Assert.Equal(withLink ? 1 : 0, await h.Db.Context.OrderPriceLineCargoLinks.CountAsync());
            var entity = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == order.Id);
            entity.Status = TransportOrderStatus.Completed;
            await h.Db.Context.SaveChangesAsync();

            var tenant = new DevTenantContext(h.TenantId);
            AuditService Audit() => new(h.Db.Context, tenant, new DevCurrentUserContext(null));
            var invoices = new InvoiceService(h.Db.Context, tenant, Audit(), new TestClock(Now), new InvoiceNumberService(h.Db.Context, tenant),
                new Api.Modules.Partners.Services.CustomerBillingConfigService(h.Db.Context, tenant, Audit(), new TestClock(Now)),
                new Api.Modules.Accounting.Services.AccountingService(h.Db.Context, tenant, Audit()));
            var result = await invoices.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [order.Id], [], null), CancellationToken.None);
            Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
            Assert.Equal(285m, saved.AgreedPrice);
            return result.Invoice!;
        }

        var plain = await InvoiceAsync(withLink: false);
        var linked = await InvoiceAsync(withLink: true);

        // One aggregated base line = AgreedPrice − service lines; a link adds no line and no euro.
        Assert.Equal(285m, plain.Subtotal);
        Assert.Equal((plain.Subtotal, plain.VatAmount, plain.Total, plain.Lines.Count),
            (linked.Subtotal, linked.VatAmount, linked.Total, linked.Lines.Count));
        Assert.Equal(plain.Lines.Select(l => (l.Quantity, l.UnitPrice)), linked.Lines.Select(l => (l.Quantity, l.UnitPrice)));
    }
}
