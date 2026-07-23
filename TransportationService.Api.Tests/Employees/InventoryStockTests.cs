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

public class InventoryStockTests
{
    private sealed class PermissionStub : IPermissionAuthorizationService
    {
        public bool Allow { get; set; }

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Allow);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, IssuedItemService Items, InventoryService Inventory,
        PermissionStub Permissions, Guid TenantId, Guid EmployeeId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+321", Street = "S", HouseNumber = "1",
            PostalCode = "2000", City = "Antwerpen", EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var permissions = new PermissionStub();
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, permissions);
        return new Harness(db, items, inventory, permissions, tenantId, employeeId);
    }

    private static SaveIssuedItemTemplateRequest Template(
        string name = "Veiligheidsschoenen", bool stock = false, bool variants = false,
        bool allowNegative = false, int? lowStockThreshold = null) =>
        new(name, "PBM", null, 1, false, true, true, true, 0,
            StockTrackingEnabled: stock, VariantsEnabled: variants, AllowNegativeStock: allowNegative,
            LowStockThreshold: lowStockThreshold);

    private static SaveEmployeeIssuedItemRequest Issue(
        Guid templateId, int quantity = 1, Guid? variantId = null,
        bool overrideStock = false, string? overrideReason = null) =>
        new(templateId, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 7, 1), quantity, null, null, null, null,
            VariantId: variantId, OverrideInsufficientStock: overrideStock, OverrideReason: overrideReason);

    private static SaveEmployeeIssuedItemRequest Return(
        Guid templateId, int quantity, string disposition, Guid? variantId = null, bool? restoreStock = null) =>
        new(templateId, null, null, IssuedItemStatus.Returned, new DateOnly(2026, 7, 1), quantity, null, null,
            new DateOnly(2026, 8, 1), null, VariantId: variantId, ReturnDisposition: disposition, RestoreStock: restoreStock);

    // --- Stock disabled ---

    [Fact]
    public async Task StockDisabled_IssuanceDoesNotTouchInventory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: false), CancellationToken.None);

        var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None);

        Assert.NotNull(item);
        Assert.Empty(await h.Db.Context.StockMovements.ToListAsync());
        var stored = await h.Db.Context.IssuedItemTemplates.SingleAsync();
        Assert.Equal(0, stored.CurrentStock);
    }

    // --- Stock enabled ---

    [Fact]
    public async Task StockEnabled_IssueCreatesMovement_AndReducesStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 10, null), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);

        var stored = await h.Db.Context.IssuedItemTemplates.SingleAsync();
        Assert.Equal(8, stored.CurrentStock);
        var movements = await h.Db.Context.StockMovements.OrderBy(m => m.CreatedAt).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(StockMovementType.InitialStock, movements[0].MovementType);
        var issue = movements[1];
        Assert.Equal(StockMovementType.Issue, issue.MovementType);
        Assert.Equal(-2, issue.Quantity);
        Assert.Equal(8, issue.ResultingStock);
        Assert.Equal(h.EmployeeId, issue.EmployeeId);
    }

    [Fact]
    public async Task InsufficientStock_BlocksIssuance_AndConsumesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 1, null), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None));

        // The rejected issuance left no item and no stock change behind.
        Assert.Empty(await h.Db.Context.EmployeeIssuedItems.ToListAsync());
        Assert.Equal(1, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
        Assert.Single(await h.Db.Context.StockMovements.ToListAsync()); // only the receipt
    }

    [Fact]
    public async Task InsufficientStock_OverrideRequiresPermissionAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);

        // Without the permission the override flag is refused.
        h.Permissions.Allow = false;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 1, overrideStock: true, overrideReason: "Spoed"), CancellationToken.None));

        // With permission but without a reason it is still refused.
        h.Permissions.Allow = true;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 1, overrideStock: true), CancellationToken.None));

        // Permission + reason → allowed, stock goes negative.
        var item = await h.Items.UpsertAsync(h.EmployeeId, null,
            Issue(template.Id, 1, overrideStock: true, overrideReason: "Spoedlevering nieuwe collega"), CancellationToken.None);
        Assert.NotNull(item);
        Assert.Equal(-1, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
    }

    [Fact]
    public async Task AllowNegativeStock_SkipsTheGuard()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true, allowNegative: true), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);

        Assert.Equal(-2, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
    }

    // --- Variants ---

    private static async Task<(IssuedItemTemplateDto Template, IssuedItemVariantDto VariantM, IssuedItemVariantDto VariantL)>
        SeedVariantTemplateAsync(Harness h)
    {
        var template = await h.Items.CreateTemplateAsync(Template("T-shirt", stock: true, variants: true), CancellationToken.None);
        var maat = await h.Inventory.CreateAttributeDefinitionAsync(
            new SaveAttributeDefinitionRequest("Maat", false, true, 0, true), CancellationToken.None);
        var m = await h.Inventory.AddAttributeOptionAsync(maat.Id, new SaveAttributeOptionRequest("M", 0, true), CancellationToken.None);
        var l = await h.Inventory.AddAttributeOptionAsync(maat.Id, new SaveAttributeOptionRequest("L", 1, true), CancellationToken.None);
        await h.Inventory.SetTemplateAttributesAsync(template.Id, [maat.Id], CancellationToken.None);
        var variantM = await h.Inventory.CreateVariantAsync(template.Id,
            new SaveVariantRequest([new SaveVariantValueRequest(maat.Id, m!.Id, null)], true, 0, null), CancellationToken.None);
        var variantL = await h.Inventory.CreateVariantAsync(template.Id,
            new SaveVariantRequest([new SaveVariantValueRequest(maat.Id, l!.Id, null)], true, 1, null), CancellationToken.None);
        return (template, variantM!, variantL!);
    }

    [Fact]
    public async Task VariantRequired_OnlyWhenTemplateUsesVariants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (variantTemplate, variantM, _) = await SeedVariantTemplateAsync(h);
        var plainTemplate = await h.Items.CreateTemplateAsync(Template("Badge", stock: true), CancellationToken.None);

        // Variant-enabled template without a variant → refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(variantTemplate.Id, 1), CancellationToken.None));

        // Plain template with a variant → refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(plainTemplate.Id, 1, variantId: variantM.Id), CancellationToken.None));
    }

    [Fact]
    public async Task VariantStock_IsTrackedPerVariant_AndSnapshotsLabel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (template, variantM, variantL) = await SeedVariantTemplateAsync(h);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(variantM.Id, 5, null), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(variantL.Id, 3, null), CancellationToken.None);

        var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2, variantId: variantM.Id), CancellationToken.None);

        Assert.Equal("M", item!.VariantLabel);
        var variants = await h.Db.Context.IssuedItemVariants.OrderBy(v => v.SortOrder).ToListAsync();
        Assert.Equal(3, variants.Single(v => v.Id == variantM.Id).CurrentStock);
        Assert.Equal(3, variants.Single(v => v.Id == variantL.Id).CurrentStock);
        Assert.Equal(0, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock); // template cache untouched
    }

    [Fact]
    public async Task CustomAttributeValues_HonourAllowCustomValues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template("Scanner", stock: true, variants: true), CancellationToken.None);
        var model = await h.Inventory.CreateAttributeDefinitionAsync(
            new SaveAttributeDefinitionRequest("Model", AllowCustomValues: false, true, 0, true), CancellationToken.None);
        await h.Inventory.SetTemplateAttributesAsync(template.Id, [model.Id], CancellationToken.None);

        // Free value while the definition forbids it → refused.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.CreateVariantAsync(template.Id,
                new SaveVariantRequest([new SaveVariantValueRequest(model.Id, null, "MC3300")], true, 0, null), CancellationToken.None));

        // After enabling custom values the same variant is accepted.
        await h.Inventory.UpdateAttributeDefinitionAsync(model.Id,
            new SaveAttributeDefinitionRequest("Model", AllowCustomValues: true, true, 0, true), CancellationToken.None);
        var variant = await h.Inventory.CreateVariantAsync(template.Id,
            new SaveVariantRequest([new SaveVariantValueRequest(model.Id, null, "MC3300")], true, 0, null), CancellationToken.None);
        Assert.Equal("MC3300", variant!.Label);
    }

    [Fact]
    public async Task DuplicateVariantCombination_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (template, variantM, _) = await SeedVariantTemplateAsync(h);
        var maatId = (await h.Inventory.ListAttributeDefinitionsAsync(false, CancellationToken.None)).Single().Id;
        var optionM = (await h.Inventory.ListAttributeDefinitionsAsync(false, CancellationToken.None))
            .Single().Options.Single(o => o.Value == "M");

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.CreateVariantAsync(template.Id,
                new SaveVariantRequest([new SaveVariantValueRequest(maatId, optionM.Id, null)], true, 5, null), CancellationToken.None));
    }

    // --- Returns ---

    [Fact]
    public async Task GoodReturn_RestoresUsableStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None);
        var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);
        Assert.Equal(3, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);

        await h.Items.UpsertAsync(h.EmployeeId, item!.Id, Return(template.Id, 2, "good"), CancellationToken.None);

        Assert.Equal(5, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
        Assert.Contains(await h.Db.Context.StockMovements.ToListAsync(),
            m => m.MovementType == StockMovementType.Return && m.Quantity == 2);
    }

    [Fact]
    public async Task DamagedLostDisposedReturn_NeverRestoresStock()
    {
        foreach (var (disposition, movementType) in new[]
                 {
                     ("damaged", StockMovementType.Damaged),
                     ("lost", StockMovementType.Lost),
                     ("disposed", StockMovementType.Disposed),
                 })
        {
            var h = await SeedAsync();
            using var _ = h.Db;
            var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
            await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None);
            var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);

            await h.Items.UpsertAsync(h.EmployeeId, item!.Id, Return(template.Id, 2, disposition), CancellationToken.None);

            Assert.Equal(3, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
            var recorded = await h.Db.Context.StockMovements.SingleAsync(m => m.MovementType == movementType);
            Assert.Equal(0, recorded.Quantity);
        }
    }

    [Fact]
    public async Task GoodReturn_WithRestoreDeclined_KeepsStockOut()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None);
        var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, item!.Id, Return(template.Id, 2, "good", restoreStock: false), CancellationToken.None);

        Assert.Equal(3, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
    }

    // --- Receipt / correction rules ---

    [Fact]
    public async Task Correction_RequiresReason_AndRecordsDelta()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 10, null), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(null, 7, " "), CancellationToken.None));

        var movement = await h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, 7, "Telverschil na inventaris"), CancellationToken.None);

        Assert.Equal(-3, movement!.Quantity);
        Assert.Equal(7, movement.ResultingStock);
        Assert.Equal(7, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
    }

    [Fact]
    public async Task Receipt_OnStockDisabledTemplate_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: false), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None));
    }

    [Fact]
    public async Task TemplateList_ReportsTotalsAndLowStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var plain = await h.Items.CreateTemplateAsync(Template("Badge", stock: true, lowStockThreshold: 3), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(plain.Id, new StockReceiptRequest(null, 2, null), CancellationToken.None);
        var (variantTemplate, variantM, variantL) = await SeedVariantTemplateAsync(h);
        await h.Inventory.ReceiveStockAsync(variantTemplate.Id, new StockReceiptRequest(variantM.Id, 4, null), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(variantTemplate.Id, new StockReceiptRequest(variantL.Id, 6, null), CancellationToken.None);

        var list = await h.Items.ListTemplatesAsync(false, CancellationToken.None);

        var badge = list.Single(t => t.Name == "Badge");
        Assert.Equal(2, badge.TotalAvailable);
        Assert.True(badge.LowStock);
        var shirt = list.Single(t => t.Name == "T-shirt");
        Assert.Equal(10, shirt.TotalAvailable);
        Assert.Equal(2, shirt.VariantCount);
        Assert.False(shirt.LowStock);
    }

    // --- Deleting an issued registration gives consumed stock back ---

    [Fact]
    public async Task DeleteIssuedRegistration_ReturnsConsumedStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(stock: true), CancellationToken.None);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None);
        var item = await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None);

        await h.Items.DeleteItemAsync(h.EmployeeId, item!.Id, CancellationToken.None);

        Assert.Equal(5, (await h.Db.Context.IssuedItemTemplates.SingleAsync()).CurrentStock);
    }

    // --- Tenant isolation ---

    [Fact]
    public async Task TenantIsolation_OtherTenantTemplateIsInvisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var foreignTemplate = new IssuedItemTemplate
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, Name = "Vreemd", Category = "Algemeen",
            StockTrackingEnabled = true, IsActive = true,
        };
        h.Db.Context.IssuedItemTemplates.Add(foreignTemplate);
        await h.Db.Context.SaveChangesAsync();

        Assert.Null(await h.Inventory.GetTemplateDetailAsync(foreignTemplate.Id, CancellationToken.None));
        Assert.Null(await h.Inventory.ReceiveStockAsync(foreignTemplate.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None));
        Assert.Null(await h.Inventory.ListMovementsAsync(foreignTemplate.Id, CancellationToken.None));
        Assert.Empty(await h.Items.ListTemplatesAsync(true, CancellationToken.None)
            .ContinueWith(t => t.Result.Where(x => x.Name == "Vreemd")));
    }

    // --- Concurrency ---

    [Fact]
    public async Task VariantVersion_GuardsConcurrentStockMutation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (template, variantM, _) = await SeedVariantTemplateAsync(h);
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(variantM.Id, 5, null), CancellationToken.None);

        // Start a mutation, then simulate a competing writer bumping the Version directly.
        var trackedTemplate = await h.Db.Context.IssuedItemTemplates.SingleAsync(t => t.Id == template.Id);
        var trackedVariant = await h.Db.Context.IssuedItemVariants.SingleAsync(v => v.Id == variantM.Id);
        h.Inventory.ApplyMovement(trackedTemplate, trackedVariant, StockMovementType.Purchase, 1, null, null, null);
        await h.Db.Context.Database.ExecuteSqlRawAsync(
            "UPDATE issued_item_variants SET \"Version\" = '11111111-1111-1111-1111-111111111111'");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Db.Context.SaveChangesAsync());
    }
}
