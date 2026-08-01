using Microsoft.EntityFrameworkCore;
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
/// Inventory status alerts (sprint fase 2): a transition into a non-normal status notifies
/// the inventory.low_stock_alerts holders exactly once; staying there is silent; recovery
/// resolves alert + notifications; a later new drop notifies again. Backed by the
/// InventoryAlert state row, not a time window.
/// </summary>
public class LowStockNotificationTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, IssuedItemService Items, InventoryService Inventory,
        Guid TenantId, Guid EmployeeId, Guid AlertUserId, Guid OtherUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var alertUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
        });
        db.Context.Users.Add(new User { Id = alertUserId, TenantId = tenantId, Email = "mag@acme.example", FirstName = "Mia", LastName = "Magazijn", IsActive = true });
        db.Context.Users.Add(new User { Id = otherUserId, TenantId = tenantId, Email = "x@acme.example", FirstName = "Otto", LastName = "Overig", IsActive = true });

        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Magazijn", IsActive = true });
        db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = PermissionCodes.InventoryLowStockAlerts, Module = "inventory", Action = "low_stock_alerts",
        });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = alertUserId, RoleId = roleId });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, TimeProvider.System);
        var alerts = new InventoryAlertService(db.Context, tenant, notifications, TimeProvider.System);
        var guard = InventoryTestFactory.Guard(currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, guard, alerts);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions(), guard, alerts);
        return new Harness(db, items, inventory, tenantId, employeeId, alertUserId, otherUserId);
    }

    private static SaveIssuedItemTemplateRequest StockTemplate(int stock, int? warning, int? minimum = null) =>
        new("Handschoenen", "Algemeen", null, 1, false, true, true, true, 0,
            StockTrackingEnabled: true, LowStockThreshold: warning, MinimumStock: minimum, Stock: stock);

    private static SaveEmployeeIssuedItemRequest Issue(Guid templateId, int quantity) =>
        new(templateId, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 8, 1), quantity, null, null, null, null);

    private static Task<List<TransportationService.Api.Modules.Notifications.Entities.Notification>> NotificationsOfType(
        Harness h, string type) =>
        h.Db.Context.Notifications.Where(n => n.TenantId == h.TenantId && n.Type == type).ToListAsync();

    [Fact]
    public async Task TransitionToLow_NotifiesOnlyPermissionHolders_AndOpensAlert()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, warning: 3), CancellationToken.None);

        // 5 -> 2 enters LowStock.
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None);

        var notifications = await NotificationsOfType(h, "inventory_status_low");
        Assert.Single(notifications);
        Assert.Equal(h.AlertUserId, notifications[0].UserId);
        Assert.Contains("Handschoenen", notifications[0].Message);

        var alert = Assert.Single(await h.Db.Context.InventoryAlerts.Where(a => a.TemplateId == template.Id).ToListAsync());
        Assert.Equal(InventoryStatus.LowStock, alert.Kind);
        Assert.Equal(InventoryAlertStatus.Active, alert.Status);
        Assert.Equal(2, alert.StockSnapshot);
    }

    [Fact]
    public async Task StayingLow_DoesNotNotifyAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, warning: 3), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None); // 5 -> 2: fires
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 1), CancellationToken.None); // 2 -> 1: still LowStock

        Assert.Single(await NotificationsOfType(h, "inventory_status_low"));
    }

    [Fact]
    public async Task Recovery_ResolvesAlertAndNotifications_NewDropNotifiesAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, warning: 3), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None); // 5 -> 2: fires
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None); // 2 -> 7: recovered

        var alert = Assert.Single(await h.Db.Context.InventoryAlerts.Where(a => a.TemplateId == template.Id).ToListAsync());
        Assert.Equal(InventoryAlertStatus.Resolved, alert.Status);
        Assert.All(await NotificationsOfType(h, "inventory_status_low"), n => Assert.NotNull(n.ResolvedAt));

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 5), CancellationToken.None); // 7 -> 2: new episode
        var notifications = await NotificationsOfType(h, "inventory_status_low");
        Assert.Equal(2, notifications.Count);
        Assert.Single(notifications, n => n.ResolvedAt == null);
    }

    [Fact]
    public async Task WorseningStatus_ResolvesOldAndNotifiesNewKind()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, warning: 3, minimum: 1), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None); // 5 -> 2: LowStock
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 1), CancellationToken.None); // 2 -> 1: CriticalStock

        var low = await NotificationsOfType(h, "inventory_status_low");
        Assert.All(low, n => Assert.NotNull(n.ResolvedAt));
        Assert.Single(await NotificationsOfType(h, "inventory_status_critical"));

        var alert = Assert.Single(await h.Db.Context.InventoryAlerts.Where(a => a.TemplateId == template.Id).ToListAsync());
        Assert.Equal(InventoryStatus.CriticalStock, alert.Kind);
    }

    [Fact]
    public async Task Correction_BelowThreshold_Notifies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 10, warning: 3), CancellationToken.None);

        await h.Inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(null, 2, "Telling"), CancellationToken.None);

        Assert.Single(await NotificationsOfType(h, "inventory_status_low"));
    }

    [Fact]
    public async Task OutOfStock_WithoutThresholds_OpensAlertButStaysSilent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 2, warning: null), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None); // 2 -> 0

        var alert = Assert.Single(await h.Db.Context.InventoryAlerts.Where(a => a.TemplateId == template.Id).ToListAsync());
        Assert.Equal(InventoryStatus.OutOfStock, alert.Kind);
        Assert.Empty(await NotificationsOfType(h, "inventory_status_out"));
    }

    [Fact]
    public async Task OutOfStock_WithThresholds_Notifies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 2, warning: 5), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 2), CancellationToken.None); // 2 -> 0

        Assert.Single(await NotificationsOfType(h, "inventory_status_out"));
    }
}
