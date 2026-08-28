using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Accounting.Entities;
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
using AccountingService = TransportationService.Api.Modules.Accounting.Services.AccountingService;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Sprint 5 scenarios B and H end-to-end: a French-language customer receives the approved
/// French description, and editing that description afterwards never touches the finalized
/// invoice.
/// </summary>
public class InvoiceFiscalSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 28, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Invoices, Guid TenantId, Guid CustomerId, Guid OrderId, Guid LegalEntityId);

    private static async Task<Harness> SeedAsync(string invoiceLanguage, VatTreatment treatment = VatTreatment.DomesticVat)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.LegalEntities.Add(new TransportationService.Api.Modules.Organization.Entities.LegalEntity
        {
            Id = legalEntityId, TenantId = tenantId, LegalName = "Acme NV",
            InvoiceNumberFormat = "{PREFIX}{SEQ}", InvoicePrefix = "FAC-", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Client SA", IsActive = true,
            InvoiceLanguageCode = invoiceLanguage, VatTreatment = treatment, VatNumber = "FR12345678901",
            DefaultLegalEntityId = legalEntityId,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 8, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 500m, LegalEntityId = legalEntityId,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new AccountingService(db.Context, tenant, audit));
        return new Harness(db, invoices, tenantId, customerId, orderId, legalEntityId);
    }

    /// <summary>The ADM sales code with its four approved descriptions.</summary>
    private static async Task<Guid> AddAdmAsync(Harness h)
    {
        var code = new SalesCategory
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "ADM", Name = "Administratieve kost",
            IsActive = true,
            InvoiceDescriptionNl = "Administratieve kost",
            InvoiceDescriptionFr = "Frais administratifs",
            InvoiceDescriptionEn = "Administrative fee",
            InvoiceDescriptionDe = "Verwaltungsgebühr",
        };
        h.Db.Context.SalesCategories.Add(code);
        await h.Db.Context.SaveChangesAsync();
        return code.Id;
    }

    private static async Task<Guid> CreateAndSendAsync(Harness h, Guid salesCategoryId)
    {
        var created = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null, h.LegalEntityId),
            CancellationToken.None);
        var invoiceId = created.Invoice!.Id;

        // A line carrying the sales code, still holding the code's own default text.
        // Added through the DbSet with an explicit InvoiceId: adding to a tracked navigation
        // makes EF classify the brand-new line as Modified.
        h.Db.Context.Set<InvoiceLine>().Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId,
            Sequence = 99, Description = "Administratieve kost", Quantity = 1m, UnitPrice = 25m,
            VatRatePercent = 21m, SalesCategoryId = salesCategoryId,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Invoices.ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);
        return invoiceId;
    }

    [Fact]
    public async Task B_FrenchCustomer_GetsTheApprovedFrenchDescriptionOnTheInvoiceLine()
    {
        var h = await SeedAsync("fr");
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);

        var invoiceId = await CreateAndSendAsync(h, admId);

        var line = await h.Db.Context.Set<InvoiceLine>().AsNoTracking()
            .FirstAsync(l => l.InvoiceId == invoiceId && l.SalesCategoryId == admId);
        Assert.Equal("Frais administratifs", line.Description);
        Assert.Equal("fr", line.DescriptionLanguageSnapshot);
        Assert.Equal("ADM", line.SalesCodeSnapshot);
    }

    [Fact]
    public async Task A_DutchCustomer_GetsTheDutchDescription()
    {
        var h = await SeedAsync("nl");
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);

        var invoiceId = await CreateAndSendAsync(h, admId);

        var line = await h.Db.Context.Set<InvoiceLine>().AsNoTracking()
            .FirstAsync(l => l.InvoiceId == invoiceId && l.SalesCategoryId == admId);
        Assert.Equal("Administratieve kost", line.Description);
    }

    [Fact]
    public async Task H_ChangingTheTranslationLater_LeavesTheFinalizedInvoiceUntouched()
    {
        var h = await SeedAsync("fr");
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);
        var invoiceId = await CreateAndSendAsync(h, admId);

        // The administrator rewords the French description months later.
        var code = await h.Db.Context.SalesCategories.FirstAsync(c => c.Id == admId);
        code.InvoiceDescriptionFr = "Frais de dossier";
        code.Name = "Andere interne naam";
        await h.Db.Context.SaveChangesAsync();

        var line = await h.Db.Context.Set<InvoiceLine>().AsNoTracking()
            .FirstAsync(l => l.InvoiceId == invoiceId && l.SalesCategoryId == admId);
        Assert.Equal("Frais administratifs", line.Description);
        Assert.Equal("ADM", line.SalesCodeSnapshot);
    }

    [Fact]
    public async Task C_ReverseChargeCustomer_FreezesTheTreatmentAndItsSourceOnTheLine()
    {
        var h = await SeedAsync("nl", VatTreatment.ReverseCharge);
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);

        var invoiceId = await CreateAndSendAsync(h, admId);

        var line = await h.Db.Context.Set<InvoiceLine>().AsNoTracking()
            .FirstAsync(l => l.InvoiceId == invoiceId && l.SalesCategoryId == admId);
        Assert.Equal("ReverseCharge", line.VatTreatmentSnapshot);
        // The invoice preview can say "overgenomen van klant" because the source is recorded.
        Assert.Equal("Customer", line.VatTreatmentSourceSnapshot);
        Assert.Equal("AE", line.VatCategoryCode);
        Assert.False(string.IsNullOrWhiteSpace(line.VatLegalTextSnapshot));
    }

    /// <summary>
    /// Audit regression: a sales code with a statutory exemption on a DOMESTIC-VAT customer. The
    /// resolver was right, but the pipeline charged 21% and froze category "S" on that line
    /// while declaring it exempt — the customer-level category freeze ran first and the rate was
    /// never written back.
    /// </summary>
    [Fact]
    public async Task D_ExemptSalesCode_OnADomesticCustomer_IsZeroRatedOnThatLineOnly()
    {
        var h = await SeedAsync("nl", VatTreatment.DomesticVat);
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);
        var exempt = new SalesCategory
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "DOORREK", Name = "Doorrekening", IsActive = true,
            VatTreatmentOverride = VatTreatment.VatExempt,
        };
        h.Db.Context.SalesCategories.Add(exempt);
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId],
                [
                    new ManualInvoiceLineInput("Administratieve kost", 1m, 25m, null, admId, null),
                    new ManualInvoiceLineInput("Doorrekening tol", 1m, 40m, null, exempt.Id, null),
                ],
                null, h.LegalEntityId),
            CancellationToken.None);
        var invoiceId = created.Invoice!.Id;

        // Already at draft time the exempt line must not pretend to be 21%.
        var draft = await h.Db.Context.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == invoiceId).ToListAsync();
        Assert.Equal(0m, draft.Single(l => l.SalesCategoryId == exempt.Id).VatRatePercent);
        Assert.Equal(21m, draft.Single(l => l.SalesCategoryId == admId).VatRatePercent);

        await h.Invoices.ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);

        var sent = await h.Db.Context.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == invoiceId).ToListAsync();
        var exemptLine = sent.Single(l => l.SalesCategoryId == exempt.Id);
        Assert.Equal(0m, exemptLine.VatRatePercent);
        Assert.Equal("E", exemptLine.VatCategoryCode);
        Assert.Equal("VatExempt", exemptLine.VatTreatmentSnapshot);
        Assert.Equal("SalesCode", exemptLine.VatTreatmentSourceSnapshot);

        var normalLine = sent.Single(l => l.SalesCategoryId == admId);
        Assert.Equal(21m, normalLine.VatRatePercent);
        Assert.Equal("S", normalLine.VatCategoryCode);
        Assert.Equal("Customer", normalLine.VatTreatmentSourceSnapshot);
    }

    [Fact]
    public async Task ALineWithADeliberateLabel_KeepsIt()
    {
        var h = await SeedAsync("fr");
        using var _ = h.Db;
        var admId = await AddAdmAsync(h);

        var created = await h.Invoices.CreateAsync(
            new CreateInvoiceRequest(h.CustomerId, null, [h.OrderId], [], null, h.LegalEntityId), CancellationToken.None);
        var invoiceId = created.Invoice!.Id;
        // Added through the DbSet with an explicit InvoiceId: adding to a tracked navigation
        // makes EF classify the brand-new line as Modified.
        h.Db.Context.Set<InvoiceLine>().Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, Sequence = 99,
            Description = "Frais de dossier spécifique", Quantity = 1m, UnitPrice = 25m,
            VatRatePercent = 21m, SalesCategoryId = admId,
        });
        await h.Db.Context.SaveChangesAsync();
        await h.Invoices.ChangeStatusAsync(invoiceId, InvoiceStatus.Sent, CancellationToken.None);

        var line = await h.Db.Context.Set<InvoiceLine>().AsNoTracking()
            .FirstAsync(l => l.InvoiceId == invoiceId && l.SalesCategoryId == admId);
        Assert.Equal("Frais de dossier spécifique", line.Description);
    }
}
