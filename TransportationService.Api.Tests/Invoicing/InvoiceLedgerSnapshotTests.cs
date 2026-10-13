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

namespace TransportationService.Api.Tests.Invoicing;

/// <summary>
/// Corrections wave §7.3/§7.4: invoice lines snapshot the sales category + ledger account at
/// Send; later mapping changes never alter historical invoices; the accounting export reads
/// only the snapshots and blocks when one is missing; drafts warn instead of blocking.
/// </summary>
public class InvoiceLedgerSnapshotTests
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
        var export = new AccountingExportService(db.Context, tenant);
        return new Harness(db, invoices, accounting, export, tenantId, customerId);
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

    [Fact]
    public async Task DraftWarns_SendFreezes_AndMappingChangeNeverRewritesHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");

        // One mapped manual line, one line without any category → draft warning, no block.
        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [],
            [
                new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, categoryId),
                new ManualInvoiceLineInput("Zonder categorie", 1m, 50m, 21m),
            ], null), CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, created.Outcome);
        var draft = created.Invoice!;
        Assert.Null(draft.Lines[0].LedgerWarning);
        Assert.Equal("700400", draft.Lines[0].LedgerAccountNumber);
        Assert.Contains("Geen verkoopcategorie gekozen", draft.Lines[1].LedgerWarning);

        // Sending freezes the snapshot from the then-current mapping.
        var sent = await h.Invoices.ChangeStatusAsync(draft.Id, InvoiceStatus.Sent, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, sent.Outcome);
        var storedLine = await h.Db.Context.InvoiceLines.SingleAsync(l => l.Description == "Verkoop binnenland");
        Assert.Equal("700400", storedLine.LedgerAccountNumberSnapshot);
        Assert.Equal("Diverse verkoop binnenland", storedLine.LedgerAccountNameSnapshot);
        Assert.Equal("Diverse verkoop binnenland", storedLine.SalesCategoryNameSnapshot);

        // Re-mapping the category afterwards must never touch the sent invoice.
        var otherAccount = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest("709999", "Nieuw nummer"), CancellationToken.None);
        var category = (await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None)).Single(c => c.Id == categoryId);
        await h.Accounting.UpdateSalesCategoryAsync(categoryId, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, otherAccount.Id, true, category.SortOrder), CancellationToken.None);

        var detail = await h.Invoices.GetByIdAsync(draft.Id, CancellationToken.None);
        Assert.Equal("700400", detail!.Lines[0].LedgerAccountNumber);
        Assert.Equal("700400", (await h.Db.Context.InvoiceLines.SingleAsync(l => l.Description == "Verkoop binnenland")).LedgerAccountNumberSnapshot);
    }

    [Fact]
    public async Task Export_UsesTheSnapshot_AndBlocksWhenALineMissesOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");

        var complete = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 2m, 100m, 21m, categoryId)], null),
            CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(complete.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        // A complete window exports fine and the workbook carries the snapshotted number.
        var bytes = await h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        Assert.True(bytes.Length > 0);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Boekhoudexport");
            Assert.Equal("Factuur", sheet.Cell(2, 2).GetString());
            Assert.Equal("700400", sheet.Cell(2, 7).GetString());
            Assert.Equal(200d, sheet.Cell(2, 9).GetDouble()); // netto 2 × 100
        }

        // An invoice sent WITHOUT a frozen account blocks the export by name.
        var incomplete = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Zonder mapping", 1m, 10m, 21m)], null),
            CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(incomplete.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None));
        Assert.Contains("Boekhoudexport geblokkeerd", ex.Message);
        Assert.Contains(incomplete.Invoice!.InvoiceNumber, ex.Message);
    }

    [Fact]
    public async Task CompleteLedgerSnapshots_FillsOnlyMissingOnes_AndUnblocksTheExport()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Sent while the category existed but the MAPPING was still missing.
        var categories = await h.Accounting.ListSalesCategoriesAsync(false, CancellationToken.None);
        var category = categories.Single(c => c.Code == "DIVERS-BINNEN");
        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop binnenland", 1m, 100m, 21m, category.Id)], null),
            CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        // Export is blocked and points at the fix.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None));
        Assert.Contains("Boekhoudsnapshot aanvullen", ex.Message);

        // Map the category, run the remediation → snapshots filled, export unblocked.
        var account = await h.Accounting.CreateLedgerAccountAsync(
            new SaveLedgerAccountRequest("700400", "Diverse verkoop binnenland"), CancellationToken.None);
        await h.Accounting.UpdateSalesCategoryAsync(category.Id, new SaveSalesCategoryRequest(
            category.Code, category.Name, category.SystemRole, account.Id, true, category.SortOrder), CancellationToken.None);
        var completed = await h.Invoices.CompleteLedgerSnapshotsAsync(created.Invoice.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.Success, completed.Outcome);
        Assert.Equal("700400", completed.Invoice!.Lines[0].LedgerAccountNumber);

        var bytes = await h.Export.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        Assert.True(bytes.Length > 0);

        // Draft invoices are refused by the remediation endpoint.
        var draft = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Concept", 1m, 10m, 21m, category.Id)], null),
            CancellationToken.None);
        var refused = await h.Invoices.CompleteLedgerSnapshotsAsync(draft.Invoice!.Id, CancellationToken.None);
        Assert.Equal(InvoiceOperationOutcome.InvalidState, refused.Outcome);
    }

    [Fact]
    public async Task Export_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = await MapCategoryAsync(h, "DIVERS-BINNEN", "700400", "Diverse verkoop binnenland");
        var created = await h.Invoices.CreateAsync(new CreateInvoiceRequest(
            h.CustomerId, null, [], [new ManualInvoiceLineInput("Verkoop", 1m, 100m, 21m, categoryId)], null),
            CancellationToken.None);
        await h.Invoices.ChangeStatusAsync(created.Invoice!.Id, InvoiceStatus.Sent, CancellationToken.None);

        // A foreign tenant's export of the same window contains no rows at all.
        var foreignExport = new AccountingExportService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));
        var bytes = await foreignExport.ExportAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        Assert.True(workbook.Worksheet("Boekhoudexport").Cell(2, 1).IsEmpty());
    }
}
