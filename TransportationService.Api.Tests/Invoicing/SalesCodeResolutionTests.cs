using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Services;
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

/// <summary>
/// Wave 2 §1: invoice-line sales-code resolution. The code frozen on the order's price/service
/// lines wins; the three system roles (Transport/Surcharge/Diesel) stay the structural fallback,
/// so an installation without configured codes keeps producing byte-identical invoices. Plus:
/// the code's default unit for manual lines and the frozen UNCL5305 VAT override at Send.
/// </summary>
public class SalesCodeResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Invoices, AccountingService Accounting,
        Guid TenantId, Guid CustomerId, Guid OrderId);

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
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 8, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 500m,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var accounting = new AccountingService(db.Context, tenant, audit);
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            accounting);
        return new Harness(db, invoices, accounting, tenantId, customerId, orderId);
    }

    private static async Task<Guid> CreateCategoryAsync(
        Harness h, string code, string name,
        string? defaultUnitCode = null, string? vatCategoryOverride = null)
    {
        var dto = await h.Accounting.CreateSalesCategoryAsync(new SaveSalesCategoryRequest(
            code, name, DefaultUnitCode: defaultUnitCode, VatCategoryOverride: vatCategoryOverride),
            CancellationToken.None);
        return dto.Id;
    }

    private static void AddStampedPricingLine(Harness h, Guid? salesCategoryId, int sequence = 0,
        bool informational = false, bool proposed = false)
    {
        h.Db.Context.TransportOrderPricingLines.Add(new TransportOrderPricingLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            Sequence = sequence, Label = $"Lijn {sequence}", Amount = 100m, Source = "Regel",
            Informational = informational, Proposed = proposed, SalesCategoryId = salesCategoryId,
        });
    }

    private static async Task<Guid> RoleCategoryIdAsync(Harness h, Modules.Accounting.Entities.SalesCategorySystemRole role)
    {
        var categories = await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None);
        return categories.Single(c => c.SystemRole == role).Id;
    }

    // --- Base transport line ----------------------------------------------------------------

    [Fact]
    public async Task BaseLine_TakesTheUnanimousStampedCode_InsteadOfTheTransportRole()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var natTransId = await CreateCategoryAsync(h, "NAT-TRANS", "Nationaal transport");
        AddStampedPricingLine(h, natTransId, 0);
        AddStampedPricingLine(h, natTransId, 1);
        // Informational/proposed lines never steer the base line's code.
        AddStampedPricingLine(h, Guid.NewGuid(), 2, informational: true);
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var baseLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.TransportOrderId == h.OrderId);
        Assert.Equal(natTransId, baseLine.SalesCategoryId);
    }

    [Fact]
    public async Task BaseLine_FallsBackToTheTransportRole_WhenStampsAreMixedOrAbsent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var catA = await CreateCategoryAsync(h, "CAT-A", "Categorie A");
        var catB = await CreateCategoryAsync(h, "CAT-B", "Categorie B");
        AddStampedPricingLine(h, catA, 0);
        AddStampedPricingLine(h, catB, 1);
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var transportRoleId = await RoleCategoryIdAsync(h, Modules.Accounting.Entities.SalesCategorySystemRole.Transport);
        var baseLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.TransportOrderId == h.OrderId);
        Assert.Equal(transportRoleId, baseLine.SalesCategoryId);
    }

    // --- Service lines ----------------------------------------------------------------------

    [Fact]
    public async Task ServiceLine_TakesItsStampedCode_AndFallsBackToTheSurchargeRole()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var handlingId = await CreateCategoryAsync(h, "HANDLING", "Handling");
        h.Db.Context.TransportOrderServiceLines.Add(new TransportOrderServiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            NameSnapshot = "Picking", Kind = Modules.Tarification.Entities.SurchargeKind.Fixed,
            Value = 25m, Amount = 25m, SalesCategoryId = handlingId,
        });
        h.Db.Context.TransportOrderServiceLines.Add(new TransportOrderServiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderId,
            NameSnapshot = "Wachttijd", Kind = Modules.Tarification.Entities.SurchargeKind.Fixed,
            Value = 45m, Amount = 45m,
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [h.OrderId], [], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var surchargeRoleId = await RoleCategoryIdAsync(h, Modules.Accounting.Entities.SalesCategorySystemRole.Surcharge);
        var lines = await h.Db.Context.InvoiceLines.OrderBy(l => l.Sequence).ToListAsync();
        var picking = lines.Single(l => l.Description.Contains("Picking"));
        var wacht = lines.Single(l => l.Description.Contains("Wachttijd"));
        Assert.Equal(handlingId, picking.SalesCategoryId);
        Assert.Equal(surchargeRoleId, wacht.SalesCategoryId);
    }

    // --- Manual lines: default unit + frozen VAT override ------------------------------------

    [Fact]
    public async Task ManualLine_FillsTheCodesDefaultUnit_ExplicitUnitStillWins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var palletVerkoopId = await CreateCategoryAsync(h, "PAL-VERKOOP", "Verkoop europallets", defaultUnitCode: "XPX");

        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [],
            [
                new ManualInvoiceLineInput("Europallets", 10m, 8m, 21m, palletVerkoopId),
                new ManualInvoiceLineInput("Europallets per uur", 2m, 8m, 21m, palletVerkoopId, UnitCode: "HUR"),
                new ManualInvoiceLineInput("Zonder categorie", 1m, 5m, 21m),
            ], null), CancellationToken.None);

        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var lines = await h.Db.Context.InvoiceLines.OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal("XPX", lines[0].UnitCode);
        Assert.Equal("HUR", lines[1].UnitCode);
        Assert.Equal("C62", lines[2].UnitCode);
    }

    [Fact]
    public async Task VatOverride_IsFrozenOntoTheLineAtSend()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var exportId = await CreateCategoryAsync(h, "EXPORT", "Export buiten EU", vatCategoryOverride: "G");

        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [],
            [
                new ManualInvoiceLineInput("Exportzending", 1m, 100m, 0m, exportId),
                new ManualInvoiceLineInput("Zonder override", 1m, 50m, 21m),
            ], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);

        var sent = await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);

        var lines = await h.Db.Context.InvoiceLines.OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal("G", lines.Single(l => l.Description == "Exportzending").VatCategoryCode);
        // No override configured → the customer's VAT-treatment chain stays authoritative:
        // the existing Send-time freeze stamps the treatment-resolved code (S at 21% domestic).
        Assert.Equal("S", lines.Single(l => l.Description == "Zonder override").VatCategoryCode);
    }

    // --- Category admin: new Wave 2 fields ---------------------------------------------------

    [Fact]
    public async Task CategoryAdmin_RoundTripsWave2Fields_AndValidatesTheVatCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Accounting.CreateSalesCategoryAsync(new SaveSalesCategoryRequest(
            "DIVERS", "Diverse verkoop", InvoiceDescriptionNl: "Diverse verkopen en diensten",
            DefaultUnitCode: "c62", VatCategoryOverride: "z"), CancellationToken.None);

        Assert.Equal("Diverse verkopen en diensten", created.InvoiceDescriptionNl);
        Assert.Equal("C62", created.DefaultUnitCode);
        Assert.Equal("Z", created.VatCategoryOverride);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Accounting.CreateSalesCategoryAsync(
            new SaveSalesCategoryRequest("FOUT", "Foute categorie", VatCategoryOverride: "XX"),
            CancellationToken.None));
    }
}
