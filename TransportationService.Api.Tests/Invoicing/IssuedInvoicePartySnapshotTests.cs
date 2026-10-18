using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Organization.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Peppol.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// MD-H3 (master-data acceptance 2026-10-18): fields that were EMPTY at issue were filled later from
/// the LIVE customer in the UBL (EndpointID, address, VAT, BuyerReference) and the issued PDF picked
/// up a logo uploaded after issue. An issued invoice's party snapshot is now final: PDF, UBL and
/// Peppol validation read it only, the logo is the file version current at issue, and only a NEW
/// invoice reflects the new master data.
/// </summary>
public class IssuedInvoicePartySnapshotTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "ts-md-h3", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid EntityId, Guid CategoryId,
        InvoiceService Invoices, InvoicePdfService Pdf, PeppolInvoiceService Peppol, LegalEntityService Entities);

    private async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = $"t-{tenantId:N}", CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await Data.CountrySeeder.SyncAsync(db.Context);
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Trans BV", VatNumber = "BE0123456749", Iban = "BE68539007547034",
            CountryCode = "BE", Street = "Transportlaan", HouseNumber = "5", PostalCode = "2030", City = "Antwerpen",
            PeppolId = "0123456749", PeppolScheme = "0208", InvoicePrefix = "F", IsActive = true, IsDefault = true,
        };
        // A TEST customer issued with intentionally EMPTY VAT, address, Peppol id and buyer reference.
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = "KL-0009", Name = "TEST Later Aangevuld", CountryCode = "BE",
            IsActive = true, DefaultLegalEntityId = entity.Id,
        };
        db.Context.LegalEntities.Add(entity);
        db.Context.Customers.Add(customer);
        db.Context.PeppolSettings.Add(new PeppolSettings { Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = entity.Id, Enabled = true });
        await db.Context.SaveChangesAsync();
        var categoryId = await AccountingTestSeed.TransportCategoryAsync(db.Context, tenantId);

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var storage = new LocalFileStorageService(_storageRoot);
        var invoices = new InvoiceService(db.Context, tenant, audit, clock, new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, clock), new AccountingService(db.Context, tenant, audit));
        return new Harness(db, tenantId, customer.Id, entity.Id, categoryId, invoices,
            new InvoicePdfService(db.Context, tenant, storage), new PeppolInvoiceService(db.Context, tenant),
            new LegalEntityService(db.Context, tenant, user, audit, new CountryCodeValidator(db.Context), storage));
    }

    private static async Task<Guid> IssueAsync(Harness h)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, LegalEntityId = h.EntityId,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 10, InvoiceDate = new DateOnly(2026, 10, 18), DueDate = new DateOnly(2026, 11, 17),
            Status = InvoiceStatus.Draft, CustomerVatTreatment = "DomesticVat",
            Lines =
            [
                new InvoiceLine
                {
                    Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "Transport", Quantity = 1, UnitPrice = 100,
                    VatRatePercent = 21, UnitCode = "C62", SalesCategoryId = h.CategoryId,
                },
            ],
        };
        h.Db.Context.Invoices.Add(invoice);
        await h.Db.Context.SaveChangesAsync();
        var issued = await h.Invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.True(issued.Outcome == Modules.Invoicing.Dtos.InvoiceOperationOutcome.Success, issued.Error);
        return invoice.Id;
    }

    private static int ImageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), "/Subtype\\s*/Image").Count;

    [Fact]
    public async Task IssuedInvoice_IgnoresLaterCustomerData_AndLaterLogo_NewInvoiceUsesThem()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issuedId = await IssueAsync(h);
        var stored = await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == issuedId);
        Assert.NotNull(stored.PartySnapshotFrozenAt);
        Assert.Null(stored.SellerLogoStorageKey);
        var pdfBefore = (await h.Pdf.RenderAsync(issuedId, CancellationToken.None))!.Value.Bytes;
        var previewBefore = (await h.Peppol.PreviewAsync(issuedId, CancellationToken.None))!;
        Assert.Equal(0, ImageCount(pdfBefore));

        // After issue: the customer gets VAT, address, Peppol identity and a buyer reference; the
        // legal entity uploads a logo.
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        customer.VatNumber = "BE0888777148";
        customer.Street = "Laterstraat";
        customer.HouseNumber = "9";
        customer.PostalCode = "9000";
        customer.City = "Gent";
        customer.PeppolScheme = "0208";
        customer.PeppolId = "0888777148";
        customer.PeppolEnabled = true;
        customer.BuyerReference = "LATER-TOEGEVOEGD";
        await h.Db.Context.SaveChangesAsync();
        await h.Entities.AttachLogoAsync(h.EntityId, "logo.png", "image/png", new MemoryStream(Png), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        // PDF: identical content, still no logo.
        var pdfAfter = (await h.Pdf.RenderAsync(issuedId, CancellationToken.None))!.Value.Bytes;
        Assert.Equal(0, ImageCount(pdfAfter));
        Assert.DoesNotContain("Laterstraat", PdfText(pdfAfter));
        Assert.DoesNotContain("BE0888777148", PdfText(pdfAfter));

        // UBL / Peppol: the issued document had no buyer Peppol identity → still not an e-invoice;
        // the later EndpointID, VAT and buyer reference never appear.
        var xml = await h.Peppol.GetXmlAsync(issuedId, CancellationToken.None);
        Assert.Null(xml!.Value.Xml);
        Assert.Contains(xml.Value.Failure!.Issues, i => i.Code.StartsWith("customer."));
        var previewAfter = (await h.Peppol.PreviewAsync(issuedId, CancellationToken.None))!;
        Assert.Equal(previewBefore.Buyer, previewAfter.Buyer);
        Assert.Null(previewAfter.Buyer.VatNumber);
        Assert.NotEqual("LATER-TOEGEVOEGD", previewAfter.BuyerReference);

        // A NEW invoice issued now carries the new customer data and the new logo.
        var newId = await IssueAsync(h);
        var newPdf = (await h.Pdf.RenderAsync(newId, CancellationToken.None))!.Value.Bytes;
        Assert.True(ImageCount(newPdf) > 0);
        var newXml = (await h.Peppol.GetXmlAsync(newId, CancellationToken.None))!.Value.Xml;
        Assert.NotNull(newXml);
        Assert.Contains("0888777148", newXml);
        Assert.Contains("LATER-TOEGEVOEGD", newXml);
        Assert.Contains("Laterstraat", newXml);
    }

    [Fact]
    public async Task LogoVersion_PrintedOnAnIssuedInvoice_SurvivesAReplacementUpload()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Entities.AttachLogoAsync(h.EntityId, "logo-v1.png", "image/png", new MemoryStream(Png), CancellationToken.None);
        var issuedId = await IssueAsync(h);
        var v1 = (await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == issuedId)).SellerLogoStorageKey;
        Assert.NotNull(v1);

        await h.Entities.AttachLogoAsync(h.EntityId, "logo-v2.png", "image/png", new MemoryStream(Png), CancellationToken.None);
        await h.Entities.RemoveLogoAsync(h.EntityId, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        Assert.True(ImageCount((await h.Pdf.RenderAsync(issuedId, CancellationToken.None))!.Value.Bytes) > 0);
        Assert.Equal(v1, (await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == issuedId)).SellerLogoStorageKey);
    }

    [Fact]
    public async Task LegacyIssuedInvoice_IsFrozenOnce_ThenNeverReadsLiveData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issuedId = await IssueAsync(h);
        // Simulate a pre-remediation row: no issue marker, no final snapshot, no customer name frozen.
        await h.Db.Context.Invoices.Where(i => i.Id == issuedId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.PartySnapshotFrozenAt, (DateTime?)null)
            .SetProperty(i => i.IssuedAt, (DateTime?)null)
            .SetProperty(i => i.CustomerNameSnapshot, (string?)null));
        h.Db.Context.ChangeTracker.Clear();

        Assert.Equal(1, await InvoicePartySnapshotBackfill.SyncAsync(h.Db.Context));
        Assert.Equal(0, await InvoicePartySnapshotBackfill.SyncAsync(h.Db.Context));
        var frozen = await h.Db.Context.Invoices.AsNoTracking().SingleAsync(i => i.Id == issuedId);
        Assert.True(frozen.PartySnapshotBackfilled);
        Assert.Equal("TEST Later Aangevuld", frozen.CustomerNameSnapshot);

        await h.Db.Context.Customers.Where(c => c.Id == h.CustomerId).ExecuteUpdateAsync(s => s.SetProperty(c => c.PeppolId, "0888777148"));
        var preview = (await h.Peppol.PreviewAsync(issuedId, CancellationToken.None))!;
        Assert.Null(preview.Buyer.Participant);
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        return string.Concat(document.Pages.Cast<PdfSharp.Pdf.PdfPage>()
            .Select(p => System.Text.Encoding.Latin1.GetString(p.Contents.CreateSingleContent().Stream.UnfilteredValue)));
    }
}
