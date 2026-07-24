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
/// Spec 1.4: low-stock notifications fire on a threshold crossing (not on every read or
/// mutation), reach only holders of inventory.low_stock_alerts, and dedupe within a window.
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
        var currentUser = new DevCurrentUserContext(null);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, TimeProvider.System);
        var lowStock = new LowStockNotifier(db.Context, tenant, notifications);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, lowStock);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions(), lowStock);
        return new Harness(db, items, inventory, tenantId, employeeId, alertUserId, otherUserId);
    }

    private static SaveIssuedItemTemplateRequest StockTemplate(int stock, int threshold) =>
        new("Handschoenen", "Algemeen", null, 1, false, true, true, true, 0,
            StockTrackingEnabled: true, LowStockThreshold: threshold, Stock: stock);

    private static SaveEmployeeIssuedItemRequest Issue(Guid templateId, int quantity) =>
        new(templateId, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 7, 24), quantity, null, null, null, null);

    [Fact]
    public async Task Crossing_NotifiesOnlyPermissionHolders()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, threshold: 3), CancellationToken.None);

        // 5 -> 2 crosses the threshold of 3.
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None);

        var notifications = await h.Db.Context.Notifications.Where(n => n.Type == "inventory_low_stock").ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(h.AlertUserId, notifications[0].UserId);
        Assert.Contains("Handschoenen", notifications[0].Message);
        Assert.Contains($"#{template.Id}", notifications[0].LinkPath);
    }

    [Fact]
    public async Task NoCrossing_NoNotification()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 10, threshold: 3), CancellationToken.None);

        // 10 -> 5 stays above the threshold; already-below mutations don't re-fire either.
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 5), CancellationToken.None);
        Assert.Empty(await h.Db.Context.Notifications.Where(n => n.Type == "inventory_low_stock").ToListAsync());
    }

    [Fact]
    public async Task RepeatedCrossings_WithinWindow_AreDeduplicated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 5, threshold: 3), CancellationToken.None);

        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None); // 5 -> 2: fires
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 5, null), CancellationToken.None); // 2 -> 7
        await h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 5), CancellationToken.None); // 7 -> 2: crossing again, deduped

        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "inventory_low_stock").ToListAsync());
    }

    [Fact]
    public async Task Correction_BelowThreshold_Notifies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(StockTemplate(stock: 10, threshold: 3), CancellationToken.None);

        await h.Inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(null, 2, "Telling"), CancellationToken.None);

        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "inventory_low_stock").ToListAsync());
    }
}
