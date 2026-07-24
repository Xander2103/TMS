using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>Spec 1.5: cartesian variant generation from attribute option selections.</summary>
public class VariantGenerationTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, InventoryService Inventory, IssuedItemService Templates, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit);
        var templates = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions());
        return new Harness(db, inventory, templates, tenantId);
    }

    private sealed record Setup(Guid TemplateId, Guid MaatId, Guid KleurId, Guid M46, Guid M48, Guid Zwart, Guid Blauw);

    private static async Task<Setup> SeedWerkbroekAsync(Harness h)
    {
        var template = await h.Templates.CreateTemplateAsync(
            new SaveIssuedItemTemplateRequest("Werkbroek", "Kleding", null, 1, false, true, true, true, 0,
                StockTrackingEnabled: true, VariantsEnabled: true),
            CancellationToken.None);

        var maat = await h.Inventory.CreateAttributeDefinitionAsync(
            new SaveAttributeDefinitionRequest("Maat", false, true, 0, true), CancellationToken.None);
        var m46 = (await h.Inventory.AddAttributeOptionAsync(maat.Id, new SaveAttributeOptionRequest("46", 0, true), CancellationToken.None))!;
        var m48 = (await h.Inventory.AddAttributeOptionAsync(maat.Id, new SaveAttributeOptionRequest("48", 1, true), CancellationToken.None))!;

        var kleur = await h.Inventory.CreateAttributeDefinitionAsync(
            new SaveAttributeDefinitionRequest("Kleur", false, true, 1, true), CancellationToken.None);
        var zwart = (await h.Inventory.AddAttributeOptionAsync(kleur.Id, new SaveAttributeOptionRequest("Zwart", 0, true), CancellationToken.None))!;
        var blauw = (await h.Inventory.AddAttributeOptionAsync(kleur.Id, new SaveAttributeOptionRequest("Blauw", 1, true), CancellationToken.None))!;

        return new Setup(template.Id, maat.Id, kleur.Id, m46.Id, m48.Id, zwart.Id, blauw.Id);
    }

    [Fact]
    public async Task Generate_CreatesCartesianProduct_WithZeroStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);

        var detail = await h.Inventory.GenerateVariantsAsync(s.TemplateId, new GenerateVariantsRequest(
        [
            new GenerateVariantsDimension(s.MaatId, [s.M46, s.M48]),
            new GenerateVariantsDimension(s.KleurId, [s.Zwart, s.Blauw]),
        ]), CancellationToken.None);

        Assert.NotNull(detail);
        var labels = detail!.Variants.Select(v => v.Label).OrderBy(l => l).ToList();
        Assert.Equal(["46 / Blauw", "46 / Zwart", "48 / Blauw", "48 / Zwart"], labels);
        Assert.All(detail.Variants, v => Assert.Equal(0, v.CurrentStock));
    }

    [Fact]
    public async Task Generate_SkipsExistingCombinations()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);

        await h.Inventory.GenerateVariantsAsync(s.TemplateId, new GenerateVariantsRequest(
        [
            new GenerateVariantsDimension(s.MaatId, [s.M46]),
            new GenerateVariantsDimension(s.KleurId, [s.Zwart]),
        ]), CancellationToken.None);

        // Give the existing variant stock, then regenerate with a broader selection.
        var existing = await h.Db.Context.IssuedItemVariants.SingleAsync(v => v.TemplateId == s.TemplateId);
        await h.Inventory.ReceiveStockAsync(s.TemplateId, new StockReceiptRequest(existing.Id, 10, null), CancellationToken.None);

        var detail = await h.Inventory.GenerateVariantsAsync(s.TemplateId, new GenerateVariantsRequest(
        [
            new GenerateVariantsDimension(s.MaatId, [s.M46, s.M48]),
            new GenerateVariantsDimension(s.KleurId, [s.Zwart]),
        ]), CancellationToken.None);

        Assert.Equal(2, detail!.Variants.Count);
        var kept = detail.Variants.Single(v => v.Label == "46 / Zwart");
        Assert.Equal(10, kept.CurrentStock); // existing variant untouched
        Assert.Equal(0, detail.Variants.Single(v => v.Label == "48 / Zwart").CurrentStock);
    }

    [Fact]
    public async Task Generate_RequiresVariantsEnabled_AndTenantScope()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var plain = await h.Templates.CreateTemplateAsync(
            new SaveIssuedItemTemplateRequest("Badge", "Algemeen", null, 1, false, true, true, true, 0,
                StockTrackingEnabled: true),
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.GenerateVariantsAsync(plain.Id, new GenerateVariantsRequest(
                [new GenerateVariantsDimension(Guid.NewGuid(), [Guid.NewGuid()])]), CancellationToken.None));

        Assert.Null(await h.Inventory.GenerateVariantsAsync(Guid.NewGuid(),
            new GenerateVariantsRequest([]), CancellationToken.None));
    }
}
