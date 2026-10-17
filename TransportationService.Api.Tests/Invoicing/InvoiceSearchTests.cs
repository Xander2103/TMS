using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// ACC-M10 (accounting remediation 2026-10-17): the invoice list finds an invoice by what
/// bookkeeping actually has in hand — customer number, dossier number, order number, customer
/// reference or PO — and filters on invoice date, paid/open and the newest Peppol state.
/// </summary>
public class InvoiceSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, InvoiceService Sut, Guid TenantId, Guid InvoiceA, Guid InvoiceB, Guid DraftC);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var (haven, acme) = (Guid.NewGuid(), Guid.NewGuid());
        var (orderId, dossierId, activityId, activityDossierId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (invoiceA, invoiceB, draftC) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = haven, TenantId = tenantId, CustomerNumber = "KL-0005", Name = "Haven BV", IsActive = true },
            new Customer { Id = acme, TenantId = tenantId, CustomerNumber = "KL-0006", Name = "TEST ACCOUNTING Klant", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = haven, OrderNumber = "ORD-0054", CustomerReference = "PO-REF-77",
            OrderDate = new(2026, 10, 1), Status = TransportOrderStatus.Invoiced,
        });
        db.Context.TransportDossiers.AddRange(
            new TransportDossier { Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0051", Title = "Standaard", CustomerId = haven },
            new TransportDossier { Id = activityDossierId, TenantId = tenantId, DossierNumber = "DOS-0056", Title = "Kraan", CustomerId = acme });
        db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId });
        var typeId = Guid.NewGuid();
        db.Context.ActivityTypes.Add(new ActivityType { Id = typeId, TenantId = tenantId, Code = "KRAANWERK", Name = "Kraanwerk ter plaatse", IsActive = true });
        db.Context.DossierActivities.Add(new DossierActivity { Id = activityId, TenantId = tenantId, DossierId = activityDossierId, ActivityTypeId = typeId, Sequence = 1, Label = "Kraan 400" });

        void AddInvoice(Guid id, Guid customer, string number, InvoiceStatus status, DateOnly date, string? po, Guid? order, Guid? activity)
        {
            db.Context.Invoices.Add(new Invoice
            {
                Id = id, TenantId = tenantId, CustomerId = customer, InvoiceNumber = number, InvoicePeriodYear = date.Year, InvoicePeriodMonth = date.Month,
                InvoiceDate = date, DueDate = date.AddDays(30), Status = status, Currency = "EUR", PurchaseOrderNumber = po,
            });
            db.Context.InvoiceLines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = id, Sequence = 1, Description = "Werk",
                Quantity = 1, UnitPrice = 100m, VatRatePercent = 21m, TransportOrderId = order, DossierActivityId = activity,
            });
        }

        AddInvoice(invoiceA, haven, "FAC-2026100001", InvoiceStatus.Paid, new(2026, 10, 3), null, orderId, null);
        AddInvoice(invoiceB, acme, "FAC-2026100002", InvoiceStatus.Sent, new(2026, 10, 17), "TEST-PO-GOLDEN", null, activityId);
        AddInvoice(draftC, acme, "FAC-2026100003", InvoiceStatus.Draft, new(2026, 9, 30), null, null, null);
        db.Context.PeppolTransmissions.AddRange(
            new PeppolTransmission { Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceB, Status = PeppolTransmissionStatus.Failed, PayloadVersion = 1, CreatedAt = Now.UtcDateTime.AddMinutes(-10) },
            new PeppolTransmission { Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceB, Status = PeppolTransmissionStatus.Delivered, PayloadVersion = 2, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new InvoiceService(db.Context, tenant, audit, new TestClock(Now), new InvoiceNumberService(db.Context, tenant),
            new TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            new TransportationService.Api.Modules.Accounting.Services.AccountingService(db.Context, tenant, audit));
        return new Harness(db, sut, tenantId, invoiceA, invoiceB, draftC);
    }

    private static async Task<string[]> Numbers(Harness h, string? search = null, InvoiceStatus? status = null,
        DateOnly? from = null, DateOnly? to = null, string? paid = null, string? peppol = null)
    {
        var page = await h.Sut.SearchAsync(search, status, null, PageRequest.Of(1, 25), CancellationToken.None, null, null, from, to, paid, peppol);
        return page.Items.Select(i => i.InvoiceNumber).OrderBy(n => n).ToArray();
    }

    [Theory]
    [InlineData("kl-0005", "FAC-2026100001")]      // customer number, case-insensitive
    [InlineData("DOS-0051", "FAC-2026100001")]     // dossier of the invoiced order
    [InlineData("ORD-0054", "FAC-2026100001")]     // order number
    [InlineData("PO-REF-77", "FAC-2026100001")]    // customer reference on the order
    [InlineData("DOS-0056", "FAC-2026100002")]     // dossier of the invoiced standalone activity
    [InlineData("TEST-PO-GOLDEN", "FAC-2026100002")] // PO number on the invoice
    [InlineData("accounting klant", "FAC-2026100002", "FAC-2026100003")] // customer name
    [InlineData("FAC-20261000", "FAC-2026100001", "FAC-2026100002", "FAC-2026100003")]
    public async Task Search_FindsByNumberCustomerDossierOrderReferenceAndPo(string term, params string[] expected)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Equal(expected, await Numbers(h, term));
    }

    [Fact]
    public async Task Filters_InvoiceDate_PaidOrOpen_AndNewestPeppolStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Equal(["FAC-2026100001", "FAC-2026100002"], await Numbers(h, from: new(2026, 10, 1), to: new(2026, 10, 31)));
        // An open-ended "to" also keeps the September draft (30/09 ≤ 10/10).
        Assert.Equal(["FAC-2026100001", "FAC-2026100003"], await Numbers(h, to: new(2026, 10, 10)));
        Assert.Equal(["FAC-2026100001"], await Numbers(h, paid: "paid"));
        Assert.Equal(["FAC-2026100002"], await Numbers(h, paid: "unpaid"));
        // The newest transmission (v2 Delivered) decides, not the older failed attempt.
        Assert.Equal(["FAC-2026100002"], await Numbers(h, peppol: "Delivered"));
        Assert.Empty(await Numbers(h, peppol: "Failed"));
    }

    [Fact]
    public async Task ListItems_CarryCustomerNumber_NewestPeppolStatus_AndDisplayNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var page = await h.Sut.SearchAsync(null, null, null, PageRequest.Of(1, 25), CancellationToken.None);
        var b = page.Items.Single(i => i.Id == h.InvoiceB);
        Assert.Equal(("KL-0006", "Delivered", "FAC-2026100002"), (b.CustomerNumber, b.PeppolStatus, b.DisplayNumber));
        var a = page.Items.Single(i => i.Id == h.InvoiceA);
        Assert.Null(a.PeppolStatus);
        Assert.Null(a.PaidAt);
    }
}
