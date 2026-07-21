using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

public class InvoiceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid CustomerId, Guid OrderId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", VatNumber = "BE0123456789", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 1450m,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new InvoiceService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now), new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now)));
        return new Harness(db, sut, tenantId, customerId, orderId);
    }

    [Fact]
    public async Task Create_FromCompletedOrder_BuildsLine_AndMarksOrderInvoiced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, result.Outcome);
        Assert.Equal("FAC-0001", result.Invoice!.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 8, 17), result.Invoice.DueDate); // +30 dagen betalingstermijn
        var line = Assert.Single(result.Invoice.Lines);
        Assert.StartsWith("ORD-0001", line.Description);
        Assert.Equal(1450m, line.UnitPrice);
        Assert.Equal(21m, line.VatRatePercent);
        Assert.Equal(1450m, result.Invoice.Subtotal);
        Assert.Equal(304.5m, result.Invoice.VatAmount);
        Assert.Equal(1754.5m, result.Invoice.Total);
        Assert.Equal(TransportOrderStatus.Invoiced, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Create_OrderAlreadyInvoiced_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        var again = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, again.Outcome);
    }

    [Fact]
    public async Task Create_UsesCustomerDefaultVatRate_WhenSet()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.DefaultVatRatePercent = 6m;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(6m, Assert.Single(result.Invoice!.Lines).VatRatePercent);
    }

    [Fact]
    public async Task Create_NonDomesticVatTreatment_DefaultsToZeroPercent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.VatTreatment = VatTreatment.IntraCommunitySupply;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(0m, Assert.Single(result.Invoice!.Lines).VatRatePercent);
        Assert.Equal(0m, result.Invoice!.VatAmount);
    }

    [Fact]
    public async Task Create_ManualLinesOnly_UsesDefaultVat()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [],
            [new ManualInvoiceLineInput("Wachturen laadkade", 2m, 65m, null)], "Nota"), CancellationToken.None);

        var line = Assert.Single(result.Invoice!.Lines);
        Assert.Equal(130m, line.LineTotal);
        Assert.Equal(21m, line.VatRatePercent);
    }

    [Fact]
    public async Task Create_WithoutAnyLine_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Cancel_ReleasesOrders_BackToCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var invoice = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        await h.Sut.ChangeStatusAsync(invoice.Invoice!.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
        // En de opdracht is weer factureerbaar.
        var uninvoiced = await h.Sut.ListUninvoicedOrdersAsync(h.CustomerId, CancellationToken.None);
        Assert.Single(uninvoiced);
    }

    [Fact]
    public async Task StatusFlow_DraftSentPaid_LocksEditing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var invoice = await h.Sut.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);
        var id = invoice.Invoice!.Id;

        await h.Sut.ChangeStatusAsync(id, InvoiceStatus.Sent, CancellationToken.None);
        var locked = await h.Sut.UpdateAsync(id, new UpdateInvoiceRequest(
            new(2026, 7, 18), new(2026, 8, 17), [new UpdateInvoiceLineInput(invoice.Invoice.Lines[0].Id, "x", 1, 1, 21)], null),
            CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.InvalidState, locked.Outcome);

        var paid = await h.Sut.ChangeStatusAsync(id, InvoiceStatus.Paid, CancellationToken.None);
        Assert.Equal(InvoiceStatus.Paid, paid.Invoice!.Status);
        Assert.Empty(paid.Invoice.AllowedTransitions);
    }

    [Fact]
    public async Task Update_DroppingOrderLine_ReleasesThatOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var invoice = await h.Sut.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId],
            [new ManualInvoiceLineInput("Toeslag", 1m, 50m, 21m)], null), CancellationToken.None);
        var manualLine = invoice.Invoice!.Lines.Single(l => l.TransportOrderId is null);

        // Keep only the manual line; the order-backed one disappears.
        var updated = await h.Sut.UpdateAsync(invoice.Invoice.Id, new UpdateInvoiceRequest(
            new(2026, 7, 18), new(2026, 8, 17),
            [new UpdateInvoiceLineInput(manualLine.Id, "Toeslag", 1m, 50m, 21m)], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, updated.Outcome);
        Assert.Single(updated.Invoice!.Lines);
        Assert.Equal(TransportOrderStatus.Completed, (await h.Db.Context.TransportOrders.FindAsync(h.OrderId))!.Status);
    }

    [Fact]
    public async Task Search_ComputesTotals_TenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        var foreignCustomer = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Customers.Add(new Customer { Id = foreignCustomer, TenantId = foreignTenant, CustomerNumber = "X", Name = "Spy", IsActive = true });
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, CustomerId = foreignCustomer,
            InvoiceNumber = "FAC-9999", InvoiceDate = new(2026, 7, 18), DueDate = new(2026, 8, 17),
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.SearchAsync(null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        var single = Assert.Single(result.Items);
        Assert.Equal("FAC-0001", single.InvoiceNumber);
        Assert.Equal(1754.5m, single.Total);
    }
}
