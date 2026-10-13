using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

/// <summary>HR wave 2026-09-12 §7/§10/§11: receipt regression, "Teruggebracht" / "Heractiveren" state machine, no delete of history.</summary>
public class IssuedItemReturnTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, IssuedItemService Sut, Guid TenantId, Guid EmployeeId, Guid UserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TradingName = "Acme Transport" });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-7", FirstName = "Karel", LastName = "Chauffeur",
            EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "hr@acme.example", FirstName = "Hilde", LastName = "Rooms", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, user);
        var inventory = new InventoryService(db.Context, tenant, user, audit, InventoryTestFactory.Guard(user));
        var sut = new IssuedItemService(db.Context, tenant, user, audit, inventory, new AllowAllPermissions(), InventoryTestFactory.Guard(user));
        return new Harness(db, sut, tenantId, employeeId, userId);
    }

    private static Task<EmployeeIssuedItemDto?> IssueAsync(Harness h, string name = "Mobiele telefoon", string? serial = "SN-1") =>
        h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            null, name, "Optioneel", IssuedItemStatus.Issued, new DateOnly(2026, 9, 1), 1, serial, null, null, null), CancellationToken.None);

    // ---- §7 receipt ----

    [Fact]
    public async Task Receipt_RendersValidPdf_WithEmployeeItemSerialAndIssueDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await IssueAsync(h);

        var pdf = await h.Sut.BuildAcknowledgementAsync(h.EmployeeId, CancellationToken.None);

        Assert.NotNull(pdf);
        Assert.Equal(0x25, pdf![0]); // '%' — PDF header
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        Assert.Equal(1, document.PageCount);
        // Content streams are Flate-compressed; the decoded stream carries the literal text
        // operators, so the employee, item, serial number and issue date must all be present.
        var content = System.Text.Encoding.Latin1.GetString(document.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue);
        Assert.Contains("Ontvangstbewijs", content);
        Assert.Contains("Karel Chauffeur", content);
        Assert.Contains("MED-7", content);
        Assert.Contains("Mobiele telefoon", content);
        Assert.Contains("SN-1", content);
        Assert.Contains("01-09-2026", content);
    }

    /// <summary>Regression for the production 500: overflow rows must land on a fresh page (own XGraphics).</summary>
    [Fact]
    public async Task Receipt_WithManyItems_SpansMultiplePages()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        for (var i = 0; i < 60; i++)
        {
            await IssueAsync(h, $"Middel {i:00}", serial: null);
        }

        var pdf = await h.Sut.BuildAcknowledgementAsync(h.EmployeeId, CancellationToken.None);

        using var document = PdfReader.Open(new MemoryStream(pdf!), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 2, $"expected multiple pages, got {document.PageCount}");
    }

    // ---- §10 return / reactivate ----

    [Fact]
    public async Task Return_MarksReturned_RecordsDateAndReceiver_KeepsRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = await IssueAsync(h);

        var returned = await h.Sut.ReturnAsync(h.EmployeeId, issued!.Id,
            new ReturnIssuedItemRequest(new DateOnly(2026, 9, 12), "Krasje op scherm"), CancellationToken.None);

        Assert.Equal(IssuedItemStatus.Returned, returned!.Status);
        Assert.Equal(new DateOnly(2026, 9, 12), returned.ReturnedDate);
        Assert.Equal("good", returned.ReturnDisposition);
        Assert.Equal(h.UserId, returned.ReceivedBackByUserId);
        Assert.Equal("Hilde Rooms", returned.ReceivedBackByName);
        Assert.Equal("Krasje op scherm", returned.ReturnCondition);
        Assert.Equal(new DateOnly(2026, 9, 1), returned.IssuedDate);

        var listed = (await h.Sut.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!.Single();
        Assert.Equal(IssuedItemStatus.Returned, listed.Status);
        Assert.Equal("Hilde Rooms", listed.ReceivedBackByName);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "EmployeeIssuedItem" && a.Action == "Returned"));
    }

    [Fact]
    public async Task Return_DefaultsToToday_AndRejectsNonIssuedRows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = await IssueAsync(h);

        var returned = await h.Sut.ReturnAsync(h.EmployeeId, issued!.Id, new ReturnIssuedItemRequest(), CancellationToken.None);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), returned!.ReturnedDate);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ReturnAsync(h.EmployeeId, issued.Id, new ReturnIssuedItemRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Return_BeforeIssueDate_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = await IssueAsync(h);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ReturnAsync(h.EmployeeId, issued!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 8, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task Reactivate_ReopensReturnedRow_ClearsReturnData_AndIsAudited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = await IssueAsync(h);
        await h.Sut.ReturnAsync(h.EmployeeId, issued!.Id, new ReturnIssuedItemRequest(new DateOnly(2026, 9, 5)), CancellationToken.None);

        var reactivated = await h.Sut.ReactivateAsync(h.EmployeeId, issued.Id,
            new ReactivateIssuedItemRequest(new DateOnly(2026, 9, 10), "Opnieuw meegegeven"), CancellationToken.None);

        Assert.Equal(IssuedItemStatus.Issued, reactivated!.Status);
        Assert.Equal(new DateOnly(2026, 9, 10), reactivated.IssuedDate);
        Assert.Null(reactivated.ReturnedDate);
        Assert.Null(reactivated.ReturnCondition);
        Assert.Null(reactivated.ReturnDisposition);
        Assert.Null(reactivated.ReceivedBackByUserId);
        Assert.Equal(h.UserId, reactivated.IssuedByUserId);

        var stored = await h.Db.Context.EmployeeIssuedItems.SingleAsync();
        Assert.Equal(IssuedItemStatus.Issued, stored.Status);
        Assert.Null(stored.ReceivedBackByUserId);
        var audit = await h.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "EmployeeIssuedItem" && a.Action == "Reactivated");
        Assert.Contains("Opnieuw meegegeven", audit.NewValuesJson);

        // Only returned rows can be reactivated.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.ReactivateAsync(h.EmployeeId, issued.Id, new ReactivateIssuedItemRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ReturnAndReactivate_WithStockTracking_MoveStockBothWays()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Sut.CreateTemplateAsync(new SaveIssuedItemTemplateRequest(
            "Helm", "Veiligheid", null, 1, false, true, true, true, 0,
            StockTrackingEnabled: true, Stock: 5), CancellationToken.None);
        var issued = await h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 9, 1), 2, null, null, null, null), CancellationToken.None);
        Assert.Equal(3, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);

        await h.Sut.ReturnAsync(h.EmployeeId, issued!.Id, new ReturnIssuedItemRequest(), CancellationToken.None);
        Assert.Equal(5, (await h.Db.Context.IssuedItemTemplates.AsNoTracking().SingleAsync()).CurrentStock);

        await h.Sut.ReactivateAsync(h.EmployeeId, issued.Id, new ReactivateIssuedItemRequest(), CancellationToken.None);
        Assert.Equal(3, (await h.Db.Context.IssuedItemTemplates.AsNoTracking().SingleAsync()).CurrentStock);
    }

    // ---- §11 delete semantics ----

    [Fact]
    public async Task Delete_OfReturnedIssuance_IsRefused_HistoryStays()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var issued = await IssueAsync(h);
        await h.Sut.ReturnAsync(h.EmployeeId, issued!.Id, new ReturnIssuedItemRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.DeleteItemAsync(h.EmployeeId, issued.Id, CancellationToken.None));

        Assert.Equal(1, await h.Db.Context.EmployeeIssuedItems.CountAsync());
        // An active issuance can still be deleted (registration error) — unchanged behaviour.
        var other = await IssueAsync(h, "Badge", null);
        Assert.True(await h.Sut.DeleteItemAsync(h.EmployeeId, other!.Id, CancellationToken.None));
    }
}
