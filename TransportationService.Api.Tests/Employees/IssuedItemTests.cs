using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

public class IssuedItemTests
{
    /// <summary>Grants every permission — these tests exercise business rules, not authorization.</summary>
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, IssuedItemService Sut, Guid TenantId, Guid EmployeeId, Guid IssuerUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var issuerUserId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TradingName = "Acme" });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+321", Street = "S", HouseNumber = "1",
            PostalCode = "2000", City = "Antwerpen", EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        db.Context.Users.Add(new User
        {
            Id = issuerUserId, TenantId = tenantId, Email = "beheer@acme.example", FirstName = "Bea", LastName = "Heerder", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(issuerUserId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, InventoryTestFactory.Guard(currentUser));
        var sut = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions(), InventoryTestFactory.Guard(currentUser));
        return new Harness(db, sut, tenantId, employeeId, issuerUserId);
    }

    private static SaveIssuedItemTemplateRequest Template(string name = "Toegangsbadge") =>
        new(name, "Algemeen", null, 1, false, true, true, true, 0);

    [Fact]
    public async Task Template_Crud_Works()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateTemplateAsync(Template(), CancellationToken.None);
        Assert.Equal("Toegangsbadge", created.Name);

        await h.Sut.UpdateTemplateAsync(created.Id, Template() with { IsActive = false }, CancellationToken.None);
        Assert.Empty(await h.Sut.ListTemplatesAsync(includeInactive: false, CancellationToken.None));
        Assert.Single(await h.Sut.ListTemplatesAsync(includeInactive: true, CancellationToken.None));

        Assert.True(await h.Sut.DeleteTemplateAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task IssueFromTemplate_SnapshotsName_AndSurvivesTemplateEdit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Sut.CreateTemplateAsync(Template("PDA Zebra"), CancellationToken.None);

        var item = await h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            template.Id, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 7, 1), 1, "SN-123", null, null, null),
            CancellationToken.None);
        Assert.Equal("PDA Zebra", item!.Name);

        // Renaming the template must NOT rewrite the historical item.
        await h.Sut.UpdateTemplateAsync(template.Id, Template("PDA Zebra v2"), CancellationToken.None);
        var reloaded = (await h.Sut.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!.Single();
        Assert.Equal("PDA Zebra", reloaded.Name);
    }

    [Fact]
    public async Task Issue_Then_Return_TracksStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var item = await h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            null, "Spanbanden", "Chauffeur", IssuedItemStatus.Issued, new DateOnly(2026, 7, 1), 4, null, null, null, null),
            CancellationToken.None);

        var returned = await h.Sut.UpsertAsync(h.EmployeeId, item!.Id, new SaveEmployeeIssuedItemRequest(
            null, null, null, IssuedItemStatus.Returned, item.IssuedDate, 4, null, null, new DateOnly(2026, 8, 1), "Goede staat"),
            CancellationToken.None);

        Assert.Equal(IssuedItemStatus.Returned, returned!.Status);
        Assert.Equal(new DateOnly(2026, 8, 1), returned.ReturnedDate);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "EmployeeIssuedItem" && a.Action == "Returned"));
    }

    [Fact]
    public async Task Issue_ResolvesIssuedByName_OnUpsertAndList()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            null, "Werkschoenen", "Chauffeur", IssuedItemStatus.Issued, new DateOnly(2026, 7, 1), 1, null, null, null, null),
            CancellationToken.None);
        Assert.Equal("Bea Heerder", created!.IssuedByName);

        var listed = (await h.Sut.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!.Single();
        Assert.Equal("Bea Heerder", listed.IssuedByName);
        Assert.Equal(h.IssuerUserId, listed.IssuedByUserId);
    }

    [Fact]
    public async Task Upsert_WithoutTemplateOrName_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
                null, null, null, IssuedItemStatus.Issued, null, 1, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledgement_ProducesPdfBytes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.UpsertAsync(h.EmployeeId, null, new SaveEmployeeIssuedItemRequest(
            null, "Toegangsbadge", "Algemeen", IssuedItemStatus.Issued, new DateOnly(2026, 7, 1), 1, "A-1", null, null, null),
            CancellationToken.None);

        var pdf = await h.Sut.BuildAcknowledgementAsync(h.EmployeeId, CancellationToken.None);

        Assert.NotNull(pdf);
        Assert.True(pdf!.Length > 100);
        Assert.Equal(0x25, pdf[0]); // '%' — PDF header
    }
}
