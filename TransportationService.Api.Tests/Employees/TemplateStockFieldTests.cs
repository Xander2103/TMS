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

/// <summary>
/// Spec 1.3: the template form's "Voorraad" value must flow through the stock ledger
/// (InitialStock on first set, Correction on change) — never silently overwrite history.
/// </summary>
public class TemplateStockFieldTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, IssuedItemService Sut, InventoryService Inventory, Guid TenantId);

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
        var sut = new IssuedItemService(db.Context, tenant, currentUser, audit, inventory, new AllowAllPermissions());
        return new Harness(db, sut, inventory, tenantId);
    }

    private static SaveIssuedItemTemplateRequest StockTemplate(
        string name = "Handschoenen", int? stock = null, string? reason = null, bool variants = false, bool allowNegative = false) =>
        new(name, "Algemeen", null, 1, false, true, true, true, 0,
            StockTrackingEnabled: true, VariantsEnabled: variants, AllowNegativeStock: allowNegative,
            Stock: stock, StockCorrectionReason: reason);

    [Fact]
    public async Task Create_WithStock_WritesInitialStockMovement()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateTemplateAsync(StockTemplate(stock: 25), CancellationToken.None);

        Assert.Equal(25, created.CurrentStock);
        Assert.Equal(25, created.TotalAvailable);
        var movement = await h.Db.Context.StockMovements.SingleAsync(m => m.TemplateId == created.Id);
        Assert.Equal(StockMovementType.InitialStock, movement.MovementType);
        Assert.Equal(25, movement.Quantity);
        Assert.Equal(25, movement.ResultingStock);
    }

    [Fact]
    public async Task Update_StockChange_AppendsCorrection_KeepingHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateTemplateAsync(StockTemplate(stock: 25), CancellationToken.None);

        var updated = await h.Sut.UpdateTemplateAsync(created.Id,
            StockTemplate(stock: 30, reason: "Telling magazijn"), CancellationToken.None);

        Assert.Equal(30, updated!.CurrentStock);
        var movements = await h.Db.Context.StockMovements
            .Where(m => m.TemplateId == created.Id).OrderBy(m => m.Timestamp).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(StockMovementType.InitialStock, movements[0].MovementType);
        Assert.Equal(25, movements[0].Quantity); // history intact
        Assert.Equal(StockMovementType.Correction, movements[1].MovementType);
        Assert.Equal(5, movements[1].Quantity);
        Assert.Equal(30, movements[1].ResultingStock);
        Assert.Equal("Telling magazijn", movements[1].Reason);
    }

    [Fact]
    public async Task Update_StockChange_WithoutReason_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateTemplateAsync(StockTemplate(stock: 25), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.UpdateTemplateAsync(created.Id, StockTemplate(stock: 30), CancellationToken.None));
        Assert.Contains("reden", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Unchanged stock needs no reason.
        var same = await h.Sut.UpdateTemplateAsync(created.Id, StockTemplate(stock: 25), CancellationToken.None);
        Assert.Equal(25, same!.CurrentStock);
        Assert.Equal(1, await h.Db.Context.StockMovements.CountAsync(m => m.TemplateId == created.Id));
    }

    [Fact]
    public async Task NegativeStockTarget_RequiresAllowNegative()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateTemplateAsync(StockTemplate(stock: 5), CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.UpdateTemplateAsync(created.Id, StockTemplate(stock: -2, reason: "Correctie"), CancellationToken.None));

        var allowed = await h.Sut.UpdateTemplateAsync(created.Id,
            StockTemplate(stock: -2, reason: "Correctie", allowNegative: true), CancellationToken.None);
        Assert.Equal(-2, allowed!.CurrentStock);
    }

    [Fact]
    public async Task VariantTemplates_IgnoreTemplateLevelStock()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateTemplateAsync(StockTemplate(stock: 25, variants: true), CancellationToken.None);

        Assert.Equal(0, created.CurrentStock);
        Assert.False(await h.Db.Context.StockMovements.AnyAsync(m => m.TemplateId == created.Id));
    }
}
