using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Dtos;
using TransportationService.Api.Modules.Accounting.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Accounting;

/// <summary>
/// ACC-M9 (accounting remediation 2026-10-17): the export carries the invoice's own money —
/// line totals via <c>MoneyRounding.LineTotal</c> and VAT per (category, rate) group exactly as
/// the invoice, PDF and UBL compute it — and offers a preflight report instead of silently
/// skipping or recomputing anything.
/// </summary>
public class AccountingExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 28, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, InvoiceService Invoices, AccountingService Accounting,
        AccountingExportService Export, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumberPrefix = "FAC-", InvoiceNumberNextValue = 1,
            PaymentTermDays = 30, DefaultVatRatePercent = 21m, DefaultCurrency = "EUR",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var accounting = new AccountingService(db.Context, tenant, audit);
        var invoices = new InvoiceService(db.Context, tenant, audit, new TestClock(Now),
            new InvoiceNumberService(db.Context, tenant),
            new CustomerBillingConfigService(db.Context, tenant, audit, new TestClock(Now)),
            accounting);
        return new Harness(db, invoices, accounting, new AccountingExportService(db.Context, tenant), tenantId, customerId);
    }

    private static async Task<Guid> MapCategoryAsync(Harness h, string code, string accountNumber, string accountName)
    {
        var account = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest(accountNumber, accountName), CancellationToken.None);
        var category = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Code == code);
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, account.Id, true, category.SortOrder), CancellationToken.None);
        return category.Id;
    }

    private static async Task<InvoiceDetailDto> IssueAsync(Harness h, Guid categoryId, params ManualInvoiceLineInput[] lines)
    {
        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(h.CustomerId, null, [], lines, null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var sent = await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);
        return (await h.Invoices.GetByIdAsync(created.Invoice.Id, CancellationToken.None))!;
    }

    private static (decimal Net, decimal Vat, decimal Gross) Sums(byte[] bytes)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Boekhoudexport");
        decimal net = 0, vat = 0, gross = 0;
        for (var row = 2; !sheet.Cell(row, 1).IsEmpty(); row++)
        {
            net += (decimal)sheet.Cell(row, 9).GetDouble();
            vat += (decimal)sheet.Cell(row, 11).GetDouble();
            gross += (decimal)sheet.Cell(row, 12).GetDouble();
        }

        return (net, vat, gross);
    }

    [Fact]
    public async Task Export_CarriesTheInvoicesOwnVat_NotAPerLineRecomputation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");

        // Three lines of € 0,03 at 21 %: per line 0,0063 → 0,01 each (0,03 in total), but the
        // invoice's VAT is computed on the group: 0,09 × 21 % = 0,0189 → 0,02. The export must
        // reconcile with the invoice (0,02), never add its own cent. Also a half-cent line:
        // 0,5 × 0,05 = 0,025 → 0,03 (half away from zero, like the invoice line).
        var invoice = await IssueAsync(h, categoryId,
            new ManualInvoiceLineInput("Lijn A", 1m, 0.03m, 21m, categoryId),
            new ManualInvoiceLineInput("Lijn B", 1m, 0.03m, 21m, categoryId),
            new ManualInvoiceLineInput("Lijn C", 1m, 0.03m, 21m, categoryId),
            new ManualInvoiceLineInput("Halve cent", 0.5m, 0.05m, 21m, categoryId));

        Assert.Equal(0.12m, invoice.Subtotal); // 0,03 × 3 + 0,03
        Assert.Equal(0.03m, invoice.VatAmount); // 0,12 × 21 % = 0,0252 → 0,03
        var bytes = await h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        var (net, vat, gross) = Sums(bytes);
        Assert.Equal(invoice.Subtotal, net);
        Assert.Equal(invoice.VatAmount, vat);
        Assert.Equal(invoice.Total, gross);
    }

    [Fact]
    public async Task Export_CreditNoteRows_CarryTheNegativeSign_AndReconcile()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");
        var invoice = await IssueAsync(h, categoryId, new ManualInvoiceLineInput("Transport", 2.5m, 125.33m, 21m, categoryId));
        var credit = await h.Invoices.CreateCreditNoteAsync(invoice.Id, new CreateCreditNoteRequest("Test creditering"), CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(credit.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var bytes = await h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        var (net, vat, gross) = Sums(bytes);
        // Invoice 313,33 + 65,80 and its full credit cancel out exactly.
        Assert.Equal(0m, net);
        Assert.Equal(0m, vat);
        Assert.Equal(0m, gross);
    }

    [Fact]
    public async Task Preflight_ListsEveryBlockingInvoiceWithItsLines_AndTheExportStillRefuses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");
        var complete = await IssueAsync(h, categoryId, new ManualInvoiceLineInput("Compleet", 1m, 100m, 21m, categoryId));
        // Pre-ACC-H6 shape (issue refuses this today; legacy rows still have it): no category at all.
        var withoutCategory = await IssueAsync(h, categoryId, new ManualInvoiceLineInput("Zonder categorie", 1m, 10m, 21m, categoryId));
        await AccountingTestSeed.StripLedgerSnapshotAsync(h.Db.Context, withoutCategory.Id, alsoCategory: true);

        var preflight = await h.Export.PreflightAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        Assert.False(preflight.CanExport);
        var blocking = Assert.Single(preflight.Invoices);
        Assert.Equal(withoutCategory.InvoiceNumber, blocking.InvoiceNumber);
        Assert.Contains(blocking.Reasons, r => r.Contains("Zonder categorie") && r.Contains("geen verkoopcategorie"));
        Assert.DoesNotContain(preflight.Invoices, i => i.InvoiceNumber == complete.InvoiceNumber);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None));
        Assert.Contains("Boekhoudexport geblokkeerd", ex.Message);
        Assert.Contains(withoutCategory.InvoiceNumber, ex.Message);
        Assert.Contains("preflight", ex.Message);

        // A window without the bad invoice exports cleanly, and the preflight says so.
        var later = await h.Export.PreflightAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), CancellationToken.None);
        Assert.True(later.CanExport);
        Assert.Empty(later.Invoices);
    }

    [Fact]
    public async Task Preflight_FlagsALineWithACategoryButNoFrozenAccount_AsFixable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Pre-ACC-H6 shape: category known and mapped today, but never frozen on the issued line.
        var mapped = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");
        var issued = await IssueAsync(h, mapped, new ManualInvoiceLineInput("Categorie zonder rekening", 1m, 10m, 21m, mapped));
        await AccountingTestSeed.StripLedgerSnapshotAsync(h.Db.Context, issued.Id);

        var preflight = await h.Export.PreflightAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        var blocking = Assert.Single(preflight.Invoices);
        Assert.Equal(issued.InvoiceNumber, blocking.InvoiceNumber);
        Assert.Contains(blocking.Reasons, r => r.Contains("Grootboekgegevens aanvullen"));
    }
}
