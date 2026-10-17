using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class PortalInvoiceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid OtherCustomerId,
        Guid PortalUserId, PortalInvoiceService Sut, string StorageRoot);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.Add(new User
        {
            Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant",
            CustomerId = customerId, IsActive = true,
        });

        void AddInvoice(Guid id, Guid customer, string number, InvoiceStatus status, InvoiceKind kind = InvoiceKind.Invoice)
        {
            db.Context.Invoices.Add(new Invoice
            {
                Id = id, TenantId = tenantId, CustomerId = customer, InvoiceNumber = number,
                InvoicePeriodYear = 2026, InvoicePeriodMonth = 7,
                InvoiceDate = new DateOnly(2026, 7, 30), DueDate = new DateOnly(2026, 8, 29),
                Status = status, Currency = "EUR", Kind = kind,
                SellerName = "Acme Transport BV", SellerVatNumber = "BE0123456789", SellerIban = "BE68539007547034",
            });
            db.Context.InvoiceLines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = id, Sequence = 1,
                Description = "Transport", Quantity = 1, UnitPrice = 100m, VatRatePercent = 21m,
            });
        }

        var draftId = Guid.NewGuid();
        var sentId = Guid.NewGuid();
        var otherCustomerInvoiceId = Guid.NewGuid();
        AddInvoice(draftId, customerId, "2026070001", InvoiceStatus.Draft);
        AddInvoice(sentId, customerId, "2026070002", InvoiceStatus.Sent);
        AddInvoice(otherCustomerInvoiceId, otherCustomerId, "2026070003", InvoiceStatus.Sent);
        // ACC-H2: a draft cancelled before issue and an issued credit note of the own customer.
        AddInvoice(Guid.NewGuid(), customerId, "2026070004", InvoiceStatus.Cancelled);
        AddInvoice(Guid.NewGuid(), customerId, "CN2026070005", InvoiceStatus.Sent, InvoiceKind.CreditNote);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var invoiceService = new InvoiceService(db.Context, tenant, audit, clock,
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock),
            new AccountingService(db.Context, tenant, audit));
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-portal-invoice-tests", Guid.NewGuid().ToString("N"));
        var fileStorage = new LocalFileStorageService(storageRoot);
        var pdfService = new InvoicePdfService(db.Context, tenant, fileStorage);
        var attachmentService = new InvoiceAttachmentService(db.Context, tenant, audit, fileStorage);

        var sut = new PortalInvoiceService(
            db.Context, tenant, new DevCurrentUserContext(portalUserId), invoiceService, pdfService, attachmentService);

        return new Harness(db, tenantId, customerId, otherCustomerId, portalUserId, sut, storageRoot);
    }

    [Fact]
    public async Task List_ExcludesDraft_AndOtherCustomers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ListMyInvoicesAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        var items = result.Value!;
        // Only ISSUED documents: the Sent invoice and the issued credit note. The draft, the
        // cancelled draft and the other customer's invoice never surface (ACC-H2).
        Assert.Equal(["2026070002", "CN2026070005"], items.Select(i => i.InvoiceNumber).OrderBy(n => n.Length).ToArray());
        Assert.Equal("CreditNote", items.Single(i => i.InvoiceNumber == "CN2026070005").Kind);
        Assert.Null(items[0].PeppolStatus);
    }

    [Fact]
    public async Task CancelledDraft_IsHiddenEverywhere_LikeADraft()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var cancelled = await h.Db.Context.Invoices.FirstAsync(i => i.Status == InvoiceStatus.Cancelled);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Sut.GetMyInvoiceAsync(cancelled.Id, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Sut.GetInvoicePdfAsync(cancelled.Id, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await h.Sut.GetInvoiceAttachmentAsync(cancelled.Id, Guid.NewGuid(), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task IssuedCreditNote_IsVisible_WithItsKind()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var creditNote = await h.Db.Context.Invoices.FirstAsync(i => i.Kind == InvoiceKind.CreditNote);
        var detail = await h.Sut.GetMyInvoiceAsync(creditNote.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, detail.Outcome);
        Assert.Equal("CreditNote", detail.Value!.Kind);
        Assert.Equal(PortalOutcomeKind.Success, (await h.Sut.GetInvoicePdfAsync(creditNote.Id, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task GetInvoice_Draft_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var draft = await h.Db.Context.Invoices.FirstAsync(i => i.Status == InvoiceStatus.Draft);
        var result = await h.Sut.GetMyInvoiceAsync(draft.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task GetInvoice_OtherCustomer_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreignInvoice = await h.Db.Context.Invoices.FirstAsync(i => i.CustomerId == h.OtherCustomerId);
        var result = await h.Sut.GetMyInvoiceAsync(foreignInvoice.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task GetInvoicePdf_NonDraftOwnInvoice_ReturnsNonTrivialBytes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var sent = await h.Db.Context.Invoices.FirstAsync(i => i.Status == InvoiceStatus.Sent && i.CustomerId == h.CustomerId);
        var result = await h.Sut.GetInvoicePdfAsync(sent.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        Assert.True(result.Value!.Content.Length > 500);
        Assert.Equal("application/pdf", result.Value.ContentType);
    }

    [Fact]
    public async Task GetInvoicePdf_Draft_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var draft = await h.Db.Context.Invoices.FirstAsync(i => i.Status == InvoiceStatus.Draft);
        var result = await h.Sut.GetInvoicePdfAsync(draft.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task GetInvoicePdf_OtherCustomer_IsBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreignInvoice = await h.Db.Context.Invoices.FirstAsync(i => i.CustomerId == h.OtherCustomerId);
        var result = await h.Sut.GetInvoicePdfAsync(foreignInvoice.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }
}
