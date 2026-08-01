using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Corrections wave §6 — the variant edit flow: editing an attribute-backed variant merges its
/// value rows in place (the old delete-all + re-add collided with the unique
/// (tenant, variant, attribute) index on PostgreSQL), unchanged variants/values stay untouched
/// and the per-variant low-stock threshold round-trips.
/// </summary>
public class VariantEditFlowTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, InventoryService Inventory, IssuedItemService Templates, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, InventoryTestFactory.Guard(currentUser));
        var templates = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions(), InventoryTestFactory.Guard(currentUser));
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
        await h.Inventory.GenerateVariantsAsync(template.Id, new GenerateVariantsRequest(
        [
            new GenerateVariantsDimension(maat.Id, [m46.Id]),
            new GenerateVariantsDimension(kleur.Id, [zwart.Id, blauw.Id]),
        ]), CancellationToken.None);
        return new Setup(template.Id, maat.Id, kleur.Id, m46.Id, m48.Id, zwart.Id, blauw.Id);
    }

    [Fact]
    public async Task EditLoadsVariants_AndDetailContainsValues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);

        var detail = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Variants.Count);
        Assert.All(detail.Variants, v => Assert.Equal(2, v.Values.Count));
    }

    [Fact]
    public async Task UpdateOneAttributeValue_OfAnAttributeBackedVariant_Persists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);
        var detail = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        var zwartVariant = detail!.Variants.Single(v => v.Label == "46 / Zwart");
        var untouched = detail.Variants.Single(v => v.Label == "46 / Blauw");
        // Physical row ids straight from the store — the merge must reuse them, never replace.
        var untouchedValueIds = await h.Db.Context.IssuedItemVariantValues
            .Where(v => v.VariantId == untouched.Id).Select(v => v.Id).OrderBy(id => id).ToListAsync();
        var keptValueId = (await h.Db.Context.IssuedItemVariantValues
            .SingleAsync(v => v.VariantId == zwartVariant.Id && v.AttributeDefinitionId == s.MaatId)).Id;

        var groen = (await h.Inventory.AddAttributeOptionAsync(s.KleurId, new SaveAttributeOptionRequest("Groen", 2, true), CancellationToken.None))!;

        // Regression: this exact call crashed on PostgreSQL (delete-all + re-add vs the
        // unfiltered unique index) — size 46 stays, colour changes Zwart → Groen.
        var updated = await h.Inventory.UpdateVariantAsync(s.TemplateId, zwartVariant.Id, new SaveVariantRequest(
            [
                new SaveVariantValueRequest(s.MaatId, s.M46, null),
                new SaveVariantValueRequest(s.KleurId, groen.Id, null),
            ],
            IsActive: true, SortOrder: zwartVariant.SortOrder, InitialStock: null,
            LowStockThreshold: 5), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("46 / Groen", updated!.Label);
        Assert.Equal(5, updated.LowStockThreshold);

        var fresh = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        var edited = fresh!.Variants.Single(v => v.Id == zwartVariant.Id);
        Assert.Equal(["46", "Groen"], edited.Values.OrderBy(v => v.Value == "46" ? 0 : 1).Select(v => v.Value).ToArray());

        // The unchanged attribute row was merged IN PLACE (same physical row id), not replaced.
        var editedRowIds = await h.Db.Context.IssuedItemVariantValues
            .Where(v => v.VariantId == zwartVariant.Id).Select(v => v.Id).ToListAsync();
        Assert.Contains(keptValueId, editedRowIds);

        // The sibling variant is byte-for-byte untouched.
        var sibling = fresh.Variants.Single(v => v.Id == untouched.Id);
        Assert.Equal("46 / Blauw", sibling.Label);
        var siblingRowIds = await h.Db.Context.IssuedItemVariantValues
            .Where(v => v.VariantId == untouched.Id).Select(v => v.Id).OrderBy(id => id).ToListAsync();
        Assert.Equal(untouchedValueIds, siblingRowIds);
    }

    [Fact]
    public async Task Threshold_RoundTrips_AndExplicitNullClears()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);
        var detail = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        var variant = detail!.Variants.First();
        var values = variant.Values
            .Select(v => new SaveVariantValueRequest(v.AttributeDefinitionId, v.AttributeOptionId, v.AttributeOptionId is null ? v.Value : null))
            .ToList();

        var withThreshold = await h.Inventory.UpdateVariantAsync(s.TemplateId, variant.Id, new SaveVariantRequest(
            values, true, variant.SortOrder, null, LowStockThreshold: 3), CancellationToken.None);
        Assert.Equal(3, withThreshold!.LowStockThreshold);

        // Sending the same threshold back preserves it (the UI now always round-trips it).
        var unchanged = await h.Inventory.UpdateVariantAsync(s.TemplateId, variant.Id, new SaveVariantRequest(
            values, true, variant.SortOrder, null, LowStockThreshold: 3), CancellationToken.None);
        Assert.Equal(3, unchanged!.LowStockThreshold);

        // Explicit null clears — documented semantics, no hidden magic.
        var cleared = await h.Inventory.UpdateVariantAsync(s.TemplateId, variant.Id, new SaveVariantRequest(
            values, true, variant.SortOrder, null, LowStockThreshold: null), CancellationToken.None);
        Assert.Null(cleared!.LowStockThreshold);
    }

    [Fact]
    public async Task AddAndRemoveVariants_LeaveOthersIntact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);

        var added = await h.Inventory.CreateVariantAsync(s.TemplateId, new SaveVariantRequest(
            [
                new SaveVariantValueRequest(s.MaatId, s.M48, null),
                new SaveVariantValueRequest(s.KleurId, s.Zwart, null),
            ], true, 5, null), CancellationToken.None);
        Assert.NotNull(added);
        Assert.Equal("48 / Zwart", added!.Label);

        var detail = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        Assert.Equal(3, detail!.Variants.Count);

        Assert.True(await h.Inventory.DeleteVariantAsync(s.TemplateId, added.Id, CancellationToken.None));
        var fresh = await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None);
        Assert.Equal(2, fresh!.Variants.Count);
        Assert.Equal(["46 / Blauw", "46 / Zwart"], fresh.Variants.Select(v => v.Label).OrderBy(l => l).ToArray());
    }

    [Fact]
    public async Task UpdateVariant_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var s = await SeedWerkbroekAsync(h);
        var variant = (await h.Inventory.GetTemplateDetailAsync(s.TemplateId, CancellationToken.None))!.Variants.First();

        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreignInventory = new InventoryService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null),
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)),
            InventoryTestFactory.Guard(new DevCurrentUserContext(null)));

        Assert.Null(await foreignInventory.UpdateVariantAsync(s.TemplateId, variant.Id, new SaveVariantRequest(
            [], true, 0, null, Label: "Gekaapt"), CancellationToken.None));
    }
}
