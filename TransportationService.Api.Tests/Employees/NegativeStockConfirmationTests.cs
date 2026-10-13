using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Sprint fase 1: a mutation below zero is hard-blocked when the template forbids it, and
/// otherwise only passes through the confirmed flow: override permission + the CURRENT
/// Version token + a reason (when configured). Stale/replayed confirmations fail closed.
/// </summary>
public class NegativeStockConfirmationTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, InventoryService Inventory, IssuedItemService Items,
        Guid TenantId, Guid EmployeeId, Guid UserId);

    private static async Task<Harness> SeedAsync(bool holdsOverridePermission = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var notifications = new NotificationService(db.Context, tenant, currentUser, TimeProvider.System);
        var alerts = new InventoryAlertService(db.Context, tenant, notifications, TimeProvider.System);
        var guard = holdsOverridePermission
            ? InventoryTestFactory.Guard(currentUser)
            : InventoryTestFactory.DenyingGuard(currentUser);
        var inventory = new InventoryService(db.Context, tenant, currentUser, audit, guard, alerts);
        var items = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory,
            new InventoryTestFactory.AllowAllPermissionService(), guard, alerts);
        return new Harness(db, inventory, items, tenantId, employeeId, userId);
    }

    private static SaveIssuedItemTemplateRequest Template(bool allowNegative, int stock = 2) =>
        new("Tape", "Algemeen", null, 1, false, true, true, false, 0,
            StockTrackingEnabled: true, AllowNegativeStock: allowNegative, Stock: stock);

    private static SaveEmployeeIssuedItemRequest Issue(
        Guid templateId, int quantity, bool confirm = false, Guid? version = null, string? reason = null) =>
        new(templateId, null, null, IssuedItemStatus.Issued, new DateOnly(2026, 8, 1), quantity, null, null, null, null,
            ConfirmNegativeStock: confirm, ExpectedVersion: version, OverrideReason: reason);

    private static async Task<Guid> CurrentVersionAsync(Harness h, Guid templateId) =>
        (await h.Db.Context.IssuedItemTemplates.AsNoTracking().FirstAsync(t => t.Id == templateId)).Version;

    [Fact]
    public async Task NegativeNotAllowed_IsHardBlocked_EvenWithConfirmation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: false), CancellationToken.None);
        var version = await CurrentVersionAsync(h, template.Id);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3, confirm: true, version: version, reason: "Nood"), CancellationToken.None));

        // Nothing moved, nothing was written.
        Assert.Empty(await h.Db.Context.StockMovements.Where(m => m.MovementType == StockMovementType.Issue).ToListAsync());
        Assert.Equal(2, (await h.Db.Context.IssuedItemTemplates.AsNoTracking().FirstAsync(t => t.Id == template.Id)).CurrentStock);
    }

    [Fact]
    public async Task Allowed_WithoutConfirmation_RequiresConfirmationWithPayload()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NegativeStockConfirmationRequiredException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3), CancellationToken.None));

        Assert.Equal(2, exception.CurrentStock);
        Assert.Equal(-3, exception.RequestedDelta);
        Assert.Equal(-1, exception.ProjectedStock);
        Assert.True(exception.RequiresReason);
        Assert.False(exception.VersionMismatch);
        Assert.Equal(await CurrentVersionAsync(h, template.Id), exception.Version);
    }

    [Fact]
    public async Task Allowed_ConfirmedWithStaleVersion_FailsClosed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);
        var staleVersion = await CurrentVersionAsync(h, template.Id);

        // Another mutation rotates the token (receipt of 1 → stock 3).
        await h.Inventory.ReceiveStockAsync(template.Id, new StockReceiptRequest(null, 1, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<NegativeStockConfirmationRequiredException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 4, confirm: true, version: staleVersion, reason: "Nood"), CancellationToken.None));
        Assert.True(exception.VersionMismatch);
    }

    [Fact]
    public async Task Allowed_ConfirmedWithoutReason_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);
        var version = await CurrentVersionAsync(h, template.Id);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3, confirm: true, version: version), CancellationToken.None));
        Assert.Contains("reden", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allowed_ValidConfirmation_BooksNegativeStock_WithAuditAndAlert()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);
        var version = await CurrentVersionAsync(h, template.Id);

        var item = await h.Items.UpsertAsync(h.EmployeeId, null,
            Issue(template.Id, 3, confirm: true, version: version, reason: "Spoeduitgifte"), CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal(-1, (await h.Db.Context.IssuedItemTemplates.AsNoTracking().FirstAsync(t => t.Id == template.Id)).CurrentStock);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(),
            l => l.Action == "NegativeStockConfirmed" && l.TenantId == h.TenantId);

        var alert = Assert.Single(await h.Db.Context.InventoryAlerts.Where(a => a.TemplateId == template.Id).ToListAsync());
        Assert.Equal(InventoryStatus.NegativeStock, alert.Kind);
    }

    [Fact]
    public async Task WithoutOverridePermission_ConfirmationIsRefused()
    {
        var h = await SeedAsync(holdsOverridePermission: false);
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);
        var version = await CurrentVersionAsync(h, template.Id);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Items.UpsertAsync(h.EmployeeId, null, Issue(template.Id, 3, confirm: true, version: version, reason: "Nood"), CancellationToken.None));
    }

    [Fact]
    public async Task Correction_BelowZero_UsesTheSameConfirmationFlow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);
        var version = await CurrentVersionAsync(h, template.Id);

        await Assert.ThrowsAsync<NegativeStockConfirmationRequiredException>(() =>
            h.Inventory.CorrectStockAsync(template.Id, new StockCorrectionRequest(null, -2, "Telling"), CancellationToken.None));

        var movement = await h.Inventory.CorrectStockAsync(template.Id,
            new StockCorrectionRequest(null, -2, "Telling", ExpectedVersion: version, ConfirmNegativeStock: true), CancellationToken.None);
        Assert.Equal(-2, movement!.ResultingStock);
    }

    [Fact]
    public async Task Preflight_ReportsProjectionAndConfirmationNeed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var allowed = await h.Items.CreateTemplateAsync(Template(allowNegative: true), CancellationToken.None);

        var preflight = await h.Inventory.PreflightAsync(allowed.Id, new StockPreflightRequest(null, -3), CancellationToken.None);
        Assert.NotNull(preflight);
        Assert.Equal(2, preflight!.CurrentStock);
        Assert.Equal(-1, preflight.ProjectedStock);
        Assert.True(preflight.RequiresConfirmation);
        Assert.False(preflight.Blocked);
        Assert.Equal(InventoryStatus.NegativeStock, preflight.ProjectedStatus);

        var forbidden = await h.Items.CreateTemplateAsync(
            Template(allowNegative: false) with { Name = "Lijm" }, CancellationToken.None);
        var blocked = await h.Inventory.PreflightAsync(forbidden.Id, new StockPreflightRequest(null, -3), CancellationToken.None);
        Assert.True(blocked!.Blocked);
        Assert.False(blocked.RequiresConfirmation);
    }

    [Fact]
    public async Task UpdateThresholds_ValidatesAndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var template = await h.Items.CreateTemplateAsync(Template(allowNegative: false, stock: 10), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Inventory.UpdateThresholdsAsync(template.Id,
                new UpdateThresholdsRequest(3, 5, null, null, false, true), CancellationToken.None));

        var updated = await h.Inventory.UpdateThresholdsAsync(template.Id,
            new UpdateThresholdsRequest(5, 2, 12, 6, true, false), CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(12, updated!.TargetStockLevel);
        Assert.Equal(6, updated.ReorderQuantity);
        Assert.True(updated.AllowNegativeStock);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(), l => l.Action == "ThresholdsChanged");
    }

    [Fact]
    public void StatusCalculator_HandlesNullThresholdsAndBands()
    {
        Assert.Equal(InventoryStatus.NegativeStock, InventoryStatusCalculator.Compute(-1, null, null));
        Assert.Equal(InventoryStatus.OutOfStock, InventoryStatusCalculator.Compute(0, null, null));
        Assert.Equal(InventoryStatus.Normal, InventoryStatusCalculator.Compute(1, null, null));
        Assert.Equal(InventoryStatus.CriticalStock, InventoryStatusCalculator.Compute(2, 5, 2));
        Assert.Equal(InventoryStatus.LowStock, InventoryStatusCalculator.Compute(3, 5, 2));
        Assert.Equal(InventoryStatus.LowStock, InventoryStatusCalculator.Compute(5, 5, null));
        Assert.Equal(InventoryStatus.Normal, InventoryStatusCalculator.Compute(6, 5, 2));
        // Misconfigured minimum above warning: the critical band wins.
        Assert.Equal(InventoryStatus.CriticalStock, InventoryStatusCalculator.Compute(4, 3, 6));
    }
}
