using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>Customer unit configuration: labels, EDI/Excel codes, favourites, sort order.</summary>
public class CustomerUnitConfigTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, PricingAdminService Admin,
        Guid TenantId, Guid CustomerAId, Guid CustomerBId, Guid PalletUnitId, Guid ColliUnitId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        var colliUnitId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerAId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerBId, TenantId = tenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = colliUnitId, TenantId = tenantId, Code = "COLLI", Name = "Colli", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, customerAId, customerBId, palletUnitId, colliUnitId);
    }

    private static SaveCustomerUnitRequest Unit(
        Guid unitTypeId, int sortOrder = 0, string? label = null, string? edi = null, string? excel = null, bool favourite = true) =>
        new(unitTypeId, sortOrder, label, edi, excel, favourite);

    [Fact]
    public async Task Save_RoundTripsLabelsCodesFavouritesAndOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var saved = await h.Admin.SaveCustomerConfigAsync(h.CustomerAId, new SaveCustomerPricingConfigRequest(
            [
                Unit(h.PalletUnitId, 1, "EURO PAL", "EPAL", "EURO", favourite: true),
                Unit(h.ColliUnitId, 2, null, "COL", null, favourite: false),
            ], []), CancellationToken.None);

        Assert.NotNull(saved);
        var pallet = saved!.PreferredUnits.Single(u => u.UnitTypeId == h.PalletUnitId);
        Assert.Equal("EURO PAL", pallet.CustomerLabel);
        Assert.Equal("EPAL", pallet.EdiCode);
        Assert.Equal("EURO", pallet.ExcelCode);
        Assert.True(pallet.IsFavourite);
        var colli = saved.PreferredUnits.Single(u => u.UnitTypeId == h.ColliUnitId);
        Assert.Null(colli.CustomerLabel);
        Assert.False(colli.IsFavourite);
        // Favourites come first, then the customer's own sort order.
        Assert.Equal(h.PalletUnitId, saved.PreferredUnits[0].UnitTypeId);
    }

    [Fact]
    public async Task SameGlobalUnit_SharedByTwoCustomers_WithDifferentConfig()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Admin.SaveCustomerConfigAsync(h.CustomerAId, new SaveCustomerPricingConfigRequest(
            [Unit(h.PalletUnitId, 1, "EURO PAL", "EPAL")], []), CancellationToken.None);
        await h.Admin.SaveCustomerConfigAsync(h.CustomerBId, new SaveCustomerPricingConfigRequest(
            [Unit(h.PalletUnitId, 7, "PAL-B", "PALLET-B", favourite: false)], []), CancellationToken.None);

        var configA = await h.Admin.GetCustomerConfigAsync(h.CustomerAId, CancellationToken.None);
        var configB = await h.Admin.GetCustomerConfigAsync(h.CustomerBId, CancellationToken.None);

        // One global unit, two independent customer configurations — never a copy.
        Assert.Equal(h.PalletUnitId, configA!.PreferredUnits.Single().UnitTypeId);
        Assert.Equal(h.PalletUnitId, configB!.PreferredUnits.Single().UnitTypeId);
        Assert.Equal("EURO PAL", configA.PreferredUnits[0].CustomerLabel);
        Assert.Equal("PAL-B", configB.PreferredUnits[0].CustomerLabel);
        Assert.Equal(1, await h.Db.Context.UnitTypes.CountAsync(u => u.TenantId == h.TenantId && u.Code == "EUROPALLET"));
    }

    [Fact]
    public async Task Save_UpdatesExistingRow_InsteadOfDuplicating()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Admin.SaveCustomerConfigAsync(h.CustomerAId, new SaveCustomerPricingConfigRequest(
            [Unit(h.PalletUnitId, 1, "OUD")], []), CancellationToken.None);
        await h.Admin.SaveCustomerConfigAsync(h.CustomerAId, new SaveCustomerPricingConfigRequest(
            [Unit(h.PalletUnitId, 3, "NIEUW", "EPAL")], []), CancellationToken.None);

        var rows = await h.Db.Context.CustomerPreferredUnits
            .Where(u => u.TenantId == h.TenantId && u.CustomerId == h.CustomerAId)
            .ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("NIEUW", row.CustomerLabel);
        Assert.Equal("EPAL", row.EdiCode);
        Assert.Equal(3, row.SortOrder);
    }

    [Fact]
    public async Task Save_RejectsDuplicateUnits_AndUnknownUnits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveCustomerConfigAsync(
            h.CustomerAId, new SaveCustomerPricingConfigRequest(
                [Unit(h.PalletUnitId), Unit(h.PalletUnitId, 2)], []), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveCustomerConfigAsync(
            h.CustomerAId, new SaveCustomerPricingConfigRequest(
                [Unit(Guid.NewGuid())], []), CancellationToken.None));
    }

    [Fact]
    public async Task Config_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // A unit belonging to another tenant is rejected outright.
        var foreignTenantId = Guid.NewGuid();
        AddForeignTenantUnit(h, foreignTenantId, out var foreignUnitId);
        await h.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.SaveCustomerConfigAsync(
            h.CustomerAId, new SaveCustomerPricingConfigRequest([Unit(foreignUnitId)], []), CancellationToken.None));
    }

    private static void AddForeignTenantUnit(Harness h, Guid foreignTenantId, out Guid foreignUnitId)
    {
        foreignUnitId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        h.Db.Context.UnitTypes.Add(new UnitType { Id = foreignUnitId, TenantId = foreignTenantId, Code = "IBC", Name = "IBC", IsActive = true });
    }
}
