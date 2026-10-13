using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

public class IssuedItemCategoryTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid OtherTenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Umbrella", Slug = "umbrella", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, otherTenantId);
    }

    private static LookupService<IssuedItemCategory> CategoryService(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new LookupService<IssuedItemCategory>(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
    }

    private static IssuedItemService TemplateService(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, InventoryTestFactory.Guard(currentUser));
        return new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions(), InventoryTestFactory.Guard(currentUser));
    }

    private static SaveIssuedItemTemplateRequest Template(string name = "Werkbroek", Guid? categoryId = null) =>
        new(name, "Algemeen", null, 1, false, true, true, true, 0, CategoryId: categoryId);

    [Fact]
    public async Task Crud_IsTenantIsolated_AndDeactivationHidesOption()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var service = CategoryService(h.Db, h.TenantId);

        var created = await service.CreateAsync(new CreateLookupRequest("KLEDING", "Kleding", null, true, 1), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.Success, created.Status);

        // Other tenant sees nothing.
        var otherService = CategoryService(h.Db, h.OtherTenantId);
        Assert.Empty(await otherService.ListOptionsAsync(CancellationToken.None));

        // Deactivating removes it from dropdown options but keeps it listable.
        var updated = await service.UpdateAsync(created.Item!.Id,
            new UpdateLookupRequest("KLEDING", "Kleding", null, false, 1), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.Success, updated.Status);
        Assert.Empty(await service.ListOptionsAsync(CancellationToken.None));
        var search = await service.SearchAsync(null, null, PageRequest.Of(1, 20), CancellationToken.None);
        Assert.Single(search.Items);
    }

    [Fact]
    public async Task Seeder_AddsDefaultAssetCategories()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await ReferenceDataSeeder.SeedAsync(h.Db.Context);

        var categories = await h.Db.Context.IssuedItemCategories
            .Where(c => c.TenantId == h.TenantId).ToListAsync();
        Assert.Contains(categories, c => c.Code == "KLEDING" && c.Name == "Kleding");
        Assert.Contains(categories, c => c.Code == "IT");
        Assert.Contains(categories, c => c.Code == "OVERIG");
    }

    [Fact]
    public async Task TemplateSave_ResolvesCategory_AndSnapshotsName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categories = CategoryService(h.Db, h.TenantId);
        var kleding = (await categories.CreateAsync(new CreateLookupRequest("KLEDING", "Kleding", null, true, 0), CancellationToken.None)).Item!;

        var templates = TemplateService(h.Db, h.TenantId);
        var created = await templates.CreateTemplateAsync(Template(categoryId: kleding.Id), CancellationToken.None);

        Assert.Equal(kleding.Id, created.CategoryId);
        Assert.Equal("Kleding", created.Category); // snapshot synced from master data

        // Switching category on update re-syncs the snapshot.
        var schoenen = (await categories.CreateAsync(new CreateLookupRequest("SCHOENEN", "Schoenen", null, true, 1), CancellationToken.None)).Item!;
        var updated = await templates.UpdateTemplateAsync(created.Id, Template(categoryId: schoenen.Id), CancellationToken.None);
        Assert.Equal(schoenen.Id, updated!.CategoryId);
        Assert.Equal("Schoenen", updated.Category);
    }

    [Fact]
    public async Task TemplateSave_RejectsForeignOrUnknownCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreign = (await CategoryService(h.Db, h.OtherTenantId)
            .CreateAsync(new CreateLookupRequest("IT", "IT", null, true, 0), CancellationToken.None)).Item!;

        var templates = TemplateService(h.Db, h.TenantId);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            templates.CreateTemplateAsync(Template(categoryId: foreign.Id), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            templates.CreateTemplateAsync(Template(categoryId: Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task ListTemplates_FiltersByCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categories = CategoryService(h.Db, h.TenantId);
        var kleding = (await categories.CreateAsync(new CreateLookupRequest("KLEDING", "Kleding", null, true, 0), CancellationToken.None)).Item!;
        var it = (await categories.CreateAsync(new CreateLookupRequest("IT", "IT", null, true, 1), CancellationToken.None)).Item!;

        var templates = TemplateService(h.Db, h.TenantId);
        await templates.CreateTemplateAsync(Template("Werkbroek", kleding.Id), CancellationToken.None);
        await templates.CreateTemplateAsync(Template("Laptop", it.Id), CancellationToken.None);

        var all = await templates.ListTemplatesAsync(includeInactive: false, CancellationToken.None);
        Assert.Equal(2, all.Count);

        var onlyKleding = await templates.ListTemplatesAsync(includeInactive: false, CancellationToken.None, categoryId: kleding.Id);
        Assert.Single(onlyKleding);
        Assert.Equal("Werkbroek", onlyKleding[0].Name);
    }
}
