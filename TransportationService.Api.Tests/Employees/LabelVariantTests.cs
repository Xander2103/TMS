using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Direct, label-only variants (Small / maat 43 / zwart): no attribute machinery required,
/// stock stays movement-audited, template total stays a computed sum.
/// </summary>
public class LabelVariantTests
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

    private static async Task<Guid> SeedShirtTemplateAsync(Harness h)
    {
        var template = await h.Templates.CreateTemplateAsync(
            new SaveIssuedItemTemplateRequest("Werkshirt", "Kleding", null, 1, false, true, true, true, 0,
                StockTrackingEnabled: true, VariantsEnabled: true),
            CancellationToken.None);
        return template.Id;
    }

    private static SaveVariantRequest LabelVariant(string label, int? initialStock = null, int? threshold = null) =>
        new([], true, 0, initialStock, Label: label, LowStockThreshold: threshold);

    [Fact]
    public async Task Create_LabelOnlyVariant_WithInitialStockMovement()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var templateId = await SeedShirtTemplateAsync(h);

        var small = await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Small", 10), CancellationToken.None);
        var medium = await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Medium", 15), CancellationToken.None);
        var large = await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Large", 8), CancellationToken.None);

        Assert.Equal("Small", small!.Label);
        Assert.Equal(10, small.CurrentStock);
        Assert.Equal(15, medium!.CurrentStock);

        // Initial stock is a real ledger movement, not a silently written number.
        var movements = await h.Db.Context.StockMovements
            .Where(m => m.TenantId == h.TenantId && m.TemplateId == templateId)
            .ToListAsync();
        Assert.Equal(3, movements.Count);
        Assert.All(movements, m => Assert.Equal(StockMovementType.InitialStock, m.MovementType));

        Assert.Equal(8, large!.CurrentStock);

        // The template-level total is the computed sum of the variants (10 + 15 + 8).
        var detail = await h.Inventory.GetTemplateDetailAsync(templateId, CancellationToken.None);
        Assert.Equal(33, detail!.Template.TotalAvailable);
    }

    [Fact]
    public async Task AdjustingVariantStock_CreatesAuditableCorrection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var templateId = await SeedShirtTemplateAsync(h);
        var small = await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Small", 10), CancellationToken.None);

        // 10 → 14 must be a +4 correction in the ledger, never a replaced number.
        var movement = await h.Inventory.CorrectStockAsync(templateId,
            new StockCorrectionRequest(small!.Id, 14, "Telverschil"), CancellationToken.None);

        Assert.Equal(StockMovementType.Correction, movement!.MovementType);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal(14, movement.ResultingStock);
        var reloaded = await h.Db.Context.IssuedItemVariants.SingleAsync(v => v.Id == small.Id);
        Assert.Equal(14, reloaded.CurrentStock);
    }

    [Fact]
    public async Task Create_RequiresLabel_AndRejectsDuplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var templateId = await SeedShirtTemplateAsync(h);
        await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Small"), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Inventory.CreateVariantAsync(templateId, LabelVariant("  "), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Inventory.CreateVariantAsync(templateId, LabelVariant("Small"), CancellationToken.None));
    }

    [Fact]
    public async Task Update_RenamesLabelOnlyVariant_AndSetsThreshold()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var templateId = await SeedShirtTemplateAsync(h);
        var variant = await h.Inventory.CreateVariantAsync(templateId, LabelVariant("Smal", 5), CancellationToken.None);

        var updated = await h.Inventory.UpdateVariantAsync(templateId, variant!.Id,
            LabelVariant("Small", threshold: 3), CancellationToken.None);

        Assert.Equal("Small", updated!.Label);
        Assert.Equal(3, updated.LowStockThreshold);
        Assert.Equal(5, updated.CurrentStock); // renaming never touches the stock ledger
    }

    [Fact]
    public async Task VariantThreshold_OverridesTemplateThreshold_ForLowStockAlert()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Real notifier + a permission holder, so the crossing rule under test is the real one.
        var alertUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = alertUserId, TenantId = h.TenantId, Email = "mag@acme.example",
            FirstName = "Mia", LastName = "Magazijn", IsActive = true,
        });
        h.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = h.TenantId, Name = "Magazijn", IsActive = true });
        h.Db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = PermissionCodes.InventoryLowStockAlerts,
            Module = "inventory", Action = "low_stock_alerts",
        });
        h.Db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = alertUserId, RoleId = roleId });
        await h.Db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(h.TenantId);
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(h.Db.Context, tenant, currentUser);
        var notifications = new NotificationService(h.Db.Context, tenant, currentUser, TimeProvider.System);
        var inventory = new InventoryService(h.Db.Context, tenant, currentUser, audit,
            InventoryTestFactory.Guard(currentUser),
            new InventoryAlertService(h.Db.Context, tenant, notifications, TimeProvider.System));
        var templates = new IssuedItemService(h.Db.Context, tenant, currentUser, audit, inventory,
            new AllowAllPermissions(), InventoryTestFactory.Guard(currentUser));
        var template = await templates.CreateTemplateAsync(
            new SaveIssuedItemTemplateRequest("Werkshirt", "Kleding", null, 1, false, true, true, true, 0,
                StockTrackingEnabled: true, VariantsEnabled: true, LowStockThreshold: 1),
            CancellationToken.None);
        var variant = await inventory.CreateVariantAsync(template.Id, LabelVariant("Small", 10, threshold: 5), CancellationToken.None);

        // 10 → 4 crosses the VARIANT threshold (5) even though the template threshold is 1.
        await inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(variant!.Id, 4, "Test"), CancellationToken.None);

        var notification = await h.Db.Context.Notifications
            .Where(n => n.TenantId == h.TenantId && n.Type == "inventory_status_low")
            .ToListAsync();
        Assert.Contains(notification, n => n.Message.Contains("Small") && n.Message.Contains("waarschuwingsgrens: 5"));
    }
}
