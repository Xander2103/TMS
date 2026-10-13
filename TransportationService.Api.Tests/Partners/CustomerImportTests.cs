using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

public class CustomerImportTests
{
    private sealed record Harness(SqliteTestDbContext Db, CustomerImportService Sut, CustomerService Customers, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumberPrefix = "KL-", CustomerNumberNextValue = 1,
        });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new CustomerImportService(db.Context, tenant, audit);
        var customers = new CustomerService(db.Context, tenant, audit, new CountryCodeValidator(db.Context));
        return new Harness(db, sut, customers, tenantId);
    }

    /// <summary>Builds an .xlsx in memory with the template's column order.</summary>
    private static MemoryStream Workbook(params (string Number, string Name, string? Vat, string? Country)[] rows)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Klanten");
        sheet.Cell(1, 1).SetValue("Klantnummer*");
        sheet.Cell(1, 2).SetValue("Naam*");
        sheet.Cell(1, 4).SetValue("BTW-nummer");
        sheet.Cell(1, 12).SetValue("Landcode");
        for (var i = 0; i < rows.Length; i += 1)
        {
            sheet.Cell(i + 2, 1).SetValue(rows[i].Number);
            sheet.Cell(i + 2, 2).SetValue(rows[i].Name);
            if (rows[i].Vat is { } vat) sheet.Cell(i + 2, 4).SetValue(vat);
            if (rows[i].Country is { } country) sheet.Cell(i + 2, 12).SetValue(country);
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        workbook.Dispose();
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Preview_ReportsExactRowAndValueErrors()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        using var file = Workbook(
            ("KL-0001", "Goede Klant", "BE0417497106", "BE"),
            ("KL-0001", "Dubbel nummer", null, null),
            ("", "Zonder nummer", null, null),
            ("KL-0002", "Fout BTW", "BE0123456750", null),
            ("KL-0003", "Fout land", null, "XX"));

        var (preview, error) = await h.Sut.PreviewAsync(file, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(5, preview!.TotalRows);
        Assert.Equal(1, preview.Creates);
        Assert.Equal(4, preview.Errors);

        var duplicate = preview.Rows.Single(r => r.RowNumber == 3);
        Assert.Contains(duplicate.Messages, m => m.Contains("KL-0001") && m.Contains("meermaals"));
        var noNumber = preview.Rows.Single(r => r.RowNumber == 4);
        Assert.Contains(noNumber.Messages, m => m.Contains("Klantnummer is verplicht"));
        var badVat = preview.Rows.Single(r => r.RowNumber == 5);
        Assert.Contains(badVat.Messages, m => m.Contains("controlegetal"));
        var badCountry = preview.Rows.Single(r => r.RowNumber == 6);
        Assert.Contains(badCountry.Messages, m => m.Contains("'XX'"));
    }

    [Fact]
    public async Task Commit_AllOrNothing_RefusesWholeFile_OnAnyError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        using var file = Workbook(
            ("KL-0001", "Goede Klant", null, null),
            ("", "Zonder nummer", null, null));

        var (result, error) = await h.Sut.CommitAsync(file, allOrNothing: true, allowUpdates: false, CancellationToken.None);

        Assert.Null(error);
        Assert.False(result!.Committed);
        Assert.Equal(0, result.Created);
        Assert.NotNull(result.ErrorWorkbookBase64);
        Assert.Equal(0, await h.Db.Context.Customers.CountAsync());
    }

    [Fact]
    public async Task Commit_HappyPath_CreatesCustomers_WithImportedNumbers_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        using var file = Workbook(
            ("KL-0001", "Eerste", "BE0417497106", "BE"),
            ("ACC-77", "Tweede", null, "NL"));

        var (result, error) = await h.Sut.CommitAsync(file, allOrNothing: true, allowUpdates: false, CancellationToken.None);

        Assert.Null(error);
        Assert.True(result!.Committed);
        Assert.Equal(2, result.Created);
        var customers = await h.Db.Context.Customers.OrderBy(c => c.CustomerNumber).ToListAsync();
        Assert.Equal(["ACC-77", "KL-0001"], customers.Select(c => c.CustomerNumber).ToArray());
        Assert.Equal("BE0417497106", customers[1].VatNumber);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "Customer" && a.Action == "Imported"));
    }

    [Fact]
    public async Task Commit_ExistingNumber_WithoutAllowUpdates_IsError_WithAllowUpdates_Updates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Customers.CreateAsync(Request("Bestaande", customerNumber: "KL-0001"), CancellationToken.None);

        using var refused = Workbook(("KL-0001", "Nieuwe naam", null, null));
        var (refusedResult, _) = await h.Sut.CommitAsync(refused, allOrNothing: true, allowUpdates: false, CancellationToken.None);
        Assert.False(refusedResult!.Committed);
        Assert.Contains(refusedResult.Rows[0].Messages, m => m.Contains("al in gebruik"));

        using var updates = Workbook(("KL-0001", "Nieuwe naam", null, null));
        var (updateResult, _) = await h.Sut.CommitAsync(updates, allOrNothing: true, allowUpdates: true, CancellationToken.None);
        Assert.True(updateResult!.Committed);
        Assert.Equal(1, updateResult.Updated);
        Assert.Equal("Nieuwe naam", (await h.Db.Context.Customers.SingleAsync()).Name);
    }

    [Fact]
    public async Task ManualCreate_WithDuplicateExplicitNumber_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Customers.CreateAsync(Request("Eerste", customerNumber: "KL-0009"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Customers.CreateAsync(Request("Tweede", customerNumber: "KL-0009"), CancellationToken.None));
    }

    [Fact]
    public async Task ManualCreate_WithoutNumber_StillAutoGenerates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Customers.CreateAsync(Request("Auto"), CancellationToken.None);

        Assert.Equal("KL-0001", created.CustomerNumber);
    }

    [Fact]
    public async Task ChangeNumber_RequiresReason_BlocksDuplicates_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Customers.CreateAsync(Request("Eerste", customerNumber: "KL-0001"), CancellationToken.None);
        await h.Customers.CreateAsync(Request("Tweede", customerNumber: "KL-0002"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Customers.ChangeNumberAsync(first.Id, new ChangeCustomerNumberRequest("KL-0099", " "), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Customers.ChangeNumberAsync(first.Id, new ChangeCustomerNumberRequest("KL-0002", "boekhouding"), CancellationToken.None));

        var changed = await h.Customers.ChangeNumberAsync(first.Id,
            new ChangeCustomerNumberRequest("KL-0099", "afstemming boekhoudpakket"), CancellationToken.None);

        Assert.Equal("KL-0099", changed!.CustomerNumber);
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.Action == "NumberChanged");
        Assert.Contains("afstemming boekhoudpakket", audit.NewValuesJson);
    }

    private static CreateCustomerRequest Request(string name, string? customerNumber = null) => new(
        name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null,
        CustomerNumber: customerNumber);
}
