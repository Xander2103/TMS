using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

public class DieselSurchargeAndPoTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Invoices, CustomerBillingConfigService Billing,
        Guid TenantId, Guid CustomerId, Guid OrderId, Guid SecondOrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, PaymentTermDays = 30, DefaultVatRatePercent = 21m });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.AddRange(
            new TransportOrder
            {
                Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
                OrderDate = new(2026, 7, 10), Status = TransportOrderStatus.Completed,
                GoodsDescription = "Paletten", AgreedPrice = 1000m,
            },
            new TransportOrder
            {
                Id = secondOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0002",
                OrderDate = new(2026, 7, 11), Status = TransportOrderStatus.Completed,
                GoodsDescription = "Rollen", AgreedPrice = 500m,
                DieselSurchargeOverride = true, DieselSurchargePercentOverride = 5m, DieselSurchargeOverrideReason = "vaste afspraak",
            });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var billing = new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now));
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant), billing);
        return new Harness(db, invoices, billing, tenantId, customerId, orderId, secondOrderId);
    }

    private static SaveCustomerDieselSurchargeRequest Surcharge(
        decimal percent = 10m,
        DieselSurchargeBasis basis = DieselSurchargeBasis.OrderAmount,
        DieselSurchargePresentation presentation = DieselSurchargePresentation.PerOrderLine,
        DieselSurchargeRounding rounding = DieselSurchargeRounding.NearestCent) =>
        new(true, percent, basis, presentation, rounding, null, null, null);

    [Fact]
    public async Task Invoice_AppliesCustomerSurcharge_PerOrder_WithOrderOverride()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.SaveSurchargeAsync(h.CustomerId, Surcharge(10m), CancellationToken.None);

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId, h.SecondOrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        var lines = result.Invoice!.Lines;
        // 2 freight lines + 2 surcharge lines (per order; the second uses its 5% override).
        Assert.Equal(4, lines.Count);
        var surcharge1 = lines.Single(l => l.Description.Contains("Dieseltoeslag 10% — ORD-0001"));
        Assert.Equal(100m, surcharge1.UnitPrice);
        var surcharge2 = lines.Single(l => l.Description.Contains("Dieseltoeslag 5% — ORD-0002"));
        Assert.Equal(25m, surcharge2.UnitPrice);
        // The base lines are untouched — no double counting.
        Assert.Equal(1000m, lines.Single(l => l.Description.StartsWith("ORD-0001")).UnitPrice);
        Assert.Equal(1625m, result.Invoice.Subtotal);
    }

    [Fact]
    public async Task Invoice_AggregatedPresentation_ProducesOneLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.SaveSurchargeAsync(h.CustomerId,
            Surcharge(10m, presentation: DieselSurchargePresentation.AggregatedLine), CancellationToken.None);

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId, h.SecondOrderId], [], null), CancellationToken.None);

        var surchargeLines = result.Invoice!.Lines.Where(l => l.Description.StartsWith("Dieseltoeslag")).ToList();
        var line = Assert.Single(surchargeLines);
        Assert.Equal(125m, line.UnitPrice); // 100 (10%) + 25 (override 5%)
    }

    [Fact]
    public async Task Invoice_SubtotalBasis_IgnoresOrderOverrides()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.SaveSurchargeAsync(h.CustomerId,
            Surcharge(8m, basis: DieselSurchargeBasis.InvoiceSubtotal), CancellationToken.None);

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId, h.SecondOrderId], [], null), CancellationToken.None);

        var line = Assert.Single(result.Invoice!.Lines, l => l.Description.StartsWith("Dieseltoeslag"));
        Assert.Equal(120m, line.UnitPrice); // 8% van 1500
    }

    [Fact]
    public async Task Invoice_OutsideEffectiveWindow_AddsNoSurcharge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.SaveSurchargeAsync(h.CustomerId,
            Surcharge(10m) with { EffectiveUntil = new DateOnly(2026, 6, 30) }, CancellationToken.None);

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.DoesNotContain(result.Invoice!.Lines, l => l.Description.StartsWith("Dieseltoeslag"));
    }

    [Fact]
    public void RoundUpCent_RoundsUp()
    {
        var config = new CustomerDieselSurcharge { Percent = 3m, Enabled = true, Rounding = DieselSurchargeRounding.RoundUpCent };
        var lines = DieselSurchargeCalculator.BuildLines(config,
            [new DieselSurchargeCalculator.OrderBase(Guid.NewGuid(), "ORD-1", 333.33m, null)], new DateOnly(2026, 7, 1));

        Assert.Equal(10.00m, Assert.Single(lines).Amount); // 9.9999 → 10.00
    }

    [Fact]
    public async Task Invoice_PoNumber_DefaultsFromEffectiveCustomerPo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.AddPoNumberAsync(h.CustomerId,
            new SaveCustomerPoNumberRequest("PO-OLD", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null), CancellationToken.None);
        await h.Billing.AddPoNumberAsync(h.CustomerId,
            new SaveCustomerPoNumberRequest("PO-2026-Q3", new DateOnly(2026, 7, 1), null, null), CancellationToken.None);

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal("PO-2026-Q3", result.Invoice!.PurchaseOrderNumber);
    }

    [Fact]
    public async Task Invoice_PoNumber_FallsBackToSingleDistinctOrderReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Db.Context.TransportOrders.FindAsync(h.OrderId);
        order!.CustomerReference = "REF-999";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal("REF-999", result.Invoice!.PurchaseOrderNumber);
    }

    [Fact]
    public async Task Send_RequiredPolicy_WithoutPo_IsBlocked_WithExactMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.SetPoPolicyAsync(h.CustomerId,
            new SetPurchaseOrderPolicyRequest(PurchaseOrderPolicy.Required), CancellationToken.None);

        var created = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        Assert.Null(created.Invoice!.PurchaseOrderNumber);

        var send = await h.Invoices.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.InvalidState, send.Outcome);
        Assert.Equal("Deze klant vereist een PO-nummer. Voeg een geldig PO-nummer toe voor verzending.", send.Error);

        // Adding the PO on the draft unblocks sending.
        var updated = await h.Invoices.UpdateAsync(created.Invoice.Id, new UpdateInvoiceRequest(
            created.Invoice.InvoiceDate, created.Invoice.DueDate,
            [.. created.Invoice.Lines.Select(l => new UpdateInvoiceLineInput(l.Id, l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent))],
            null, PurchaseOrderNumber: "PO-XYZ"), CancellationToken.None);
        Assert.Equal("PO-XYZ", updated.Invoice!.PurchaseOrderNumber);
        var sent = await h.Invoices.ChangeStatusAsync(created.Invoice.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);
    }

    [Fact]
    public async Task PoPolicyChange_SyncsLegacyFlag_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Billing.SetPoPolicyAsync(h.CustomerId,
            new SetPurchaseOrderPolicyRequest(PurchaseOrderPolicy.Required), CancellationToken.None);

        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        Assert.True(customer!.PurchaseOrderRequired);
        Assert.Equal(PurchaseOrderPolicy.Required, customer.PurchaseOrderPolicy);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.Action == "PoPolicyChanged"));
    }

    [Fact]
    public async Task PoHistory_EffectiveResolution_LatestValidFromWins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Billing.AddPoNumberAsync(h.CustomerId,
            new SaveCustomerPoNumberRequest("PO-A", new DateOnly(2026, 1, 1), null, null), CancellationToken.None);
        await h.Billing.AddPoNumberAsync(h.CustomerId,
            new SaveCustomerPoNumberRequest("PO-B", new DateOnly(2026, 7, 1), null, null), CancellationToken.None);

        Assert.Equal("PO-B", await h.Billing.ResolveEffectivePoNumberAsync(h.CustomerId, new DateOnly(2026, 7, 18), CancellationToken.None));
        Assert.Equal("PO-A", await h.Billing.ResolveEffectivePoNumberAsync(h.CustomerId, new DateOnly(2026, 5, 1), CancellationToken.None));

        var policy = await h.Billing.GetPoPolicyAsync(h.CustomerId, CancellationToken.None);
        Assert.Equal(2, policy!.History.Count);
        Assert.True(policy.History.Single(p => p.PoNumber == "PO-B").IsEffectiveToday);
    }
}
