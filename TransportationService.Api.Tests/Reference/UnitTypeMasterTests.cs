using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Reference.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reference;

public class UnitTypeMasterTests
{
    private static (SqliteTestDbContext Db, UnitTypeMasterService Service, Guid TenantId) CreateService()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.SaveChanges();
        var tenant = new DevTenantContext(tenantId);
        var service = new UnitTypeMasterService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return (db, service, tenantId);
    }

    private static SaveUnitTypeMasterRequest Request(
        string code = "BLOKPAL-A", string name = "Blokpallet A",
        decimal? lengthCm = 120m, decimal? widthCm = 100m,
        UnitDimensionBehavior behavior = UnitDimensionBehavior.DefaultButOverridable,
        UnitCategory category = UnitCategory.Packaging, int decimals = 0) => new(
        code, name, null, true, 5, true, true, category, decimals, null, behavior,
        lengthCm, widthCm, null, null, null, null, null, null);

    [Fact]
    public async Task Create_CustomUnit_WithConfigurableDimensions()
    {
        var (db, service, tenantId) = CreateService();
        using var _ = db;

        // A company defines its own 120×100 pallet — dimensions are data, never code.
        var a = await service.CreateAsync(Request("BLOKPAL-A", "Blokpallet A", 120m, 100m), CancellationToken.None);
        // And a second variant with different dimensions for the same tenant.
        var b = await service.CreateAsync(Request("BLOKPAL-B", "Blokpallet B", 100m, 100m), CancellationToken.None);

        Assert.Equal(120m, a.DefaultLengthCm);
        Assert.Equal(100m, a.DefaultWidthCm);
        Assert.Equal(100m, b.DefaultLengthCm);
        Assert.Equal(UnitDimensionBehavior.DefaultButOverridable, a.DimensionBehavior);

        var stored = await db.Context.UnitTypes.Where(u => u.TenantId == tenantId).ToListAsync();
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Create_NormalizesCode_AndKeepsItEditableOnRename()
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        var created = await service.CreateAsync(Request("eurpal", "Europallet"), CancellationToken.None);
        Assert.Equal("EURPAL", created.Code);

        // Renaming the unit must never regenerate the code…
        var renamed = await service.UpdateAsync(created.Id,
            Request("EURPAL", "Euro pallet hernoemd"), CancellationToken.None);
        Assert.Equal("EURPAL", renamed!.Code);
        Assert.Equal("Euro pallet hernoemd", renamed.Name);

        // …but the user can explicitly change it (legacy/accounting/EDI conventions).
        var recoded = await service.UpdateAsync(created.Id,
            Request("EP-OLD", "Euro pallet hernoemd"), CancellationToken.None);
        Assert.Equal("EP-OLD", recoded!.Code);
    }

    [Theory]
    [InlineData("A")]           // too short
    [InlineData("SPACES IN")]   // invalid char
    [InlineData("WAY-TOO-LONG-FOR-A-UNIT-CODE")]
    public async Task Create_RejectsInvalidCodeFormats(string code)
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.CreateAsync(Request(code), CancellationToken.None));
    }

    [Fact]
    public async Task Create_RejectsDuplicateCode_WithinTenant()
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        await service.CreateAsync(Request("IBC", "IBC"), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.CreateAsync(Request("ibc", "IBC container"), CancellationToken.None));
    }

    [Fact]
    public async Task Codes_AreTenantIsolated()
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        var otherTenant = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var otherTenantContext = new DevTenantContext(otherTenant);
        var otherService = new UnitTypeMasterService(db.Context, otherTenantContext,
            new AuditService(db.Context, otherTenantContext, new DevCurrentUserContext(null)));

        await service.CreateAsync(Request("IBC", "IBC"), CancellationToken.None);
        // Same code in another tenant is fine, and the other tenant's list stays isolated.
        var other = await otherService.CreateAsync(Request("IBC", "IBC elders"), CancellationToken.None);
        Assert.Equal("IBC", other.Code);

        var mine = await service.ListAsync(CancellationToken.None);
        var onlyUnit = Assert.Single(mine);
        Assert.Equal("IBC", onlyUnit.Name);
    }

    [Fact]
    public async Task DimensionBehaviors_ArePersisted()
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        var fixedUnit = await service.CreateAsync(
            Request("IBC", "IBC", 120m, 100m, UnitDimensionBehavior.Fixed), CancellationToken.None);
        var variableUnit = await service.CreateAsync(
            Request("COL", "Colli", null, null, UnitDimensionBehavior.Variable), CancellationToken.None);

        Assert.Equal(UnitDimensionBehavior.Fixed, fixedUnit.DimensionBehavior);
        Assert.Equal(UnitDimensionBehavior.Variable, variableUnit.DimensionBehavior);
        Assert.Null(variableUnit.DefaultLengthCm);
    }

    [Fact]
    public async Task Validation_RejectsNegativeDefaults_AndBadDecimals()
    {
        var (db, service, _) = CreateService();
        using var _1 = db;

        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.CreateAsync(Request("NEG", "Negatief", -1m, 80m), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(
            () => service.CreateAsync(Request("DEC", "Decimalen", 120m, 80m, decimals: 9), CancellationToken.None));
    }

    [Fact]
    public async Task Create_RecordsAuditTrail()
    {
        var (db, service, tenantId) = CreateService();
        using var _ = db;

        var created = await service.CreateAsync(Request(), CancellationToken.None);

        var log = await db.Context.AuditLogs
            .Where(l => l.TenantId == tenantId && l.EntityType == "UnitType" && l.EntityId == created.Id.ToString())
            .ToListAsync();
        Assert.Contains(log, l => l.Action == "Created");
    }

    [Fact]
    public async Task Seeder_BackfillsPhysicalDefaults_WithoutOverwritingUserEdits()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        await ReferenceDataSeeder.SeedAsync(db.Context);

        var euro = await db.Context.UnitTypes.SingleAsync(u => u.TenantId == tenantId && u.Code == "EUROPALLET");
        Assert.Equal(120m, euro.DefaultLengthCm);
        Assert.Equal(80m, euro.DefaultWidthCm);
        Assert.Equal(UnitCategory.Packaging, euro.Category);
        Assert.Equal(UnitDimensionBehavior.DefaultButOverridable, euro.DimensionBehavior);
        var block = await db.Context.UnitTypes.SingleAsync(u => u.TenantId == tenantId && u.Code == "BLOCKPALLET");
        Assert.Equal(100m, block.DefaultWidthCm);

        // A tenant that customised its Europallet keeps that customisation on re-seed.
        euro.DefaultLengthCm = 110m;
        euro.DefaultWidthCm = 90m;
        await db.Context.SaveChangesAsync();
        await ReferenceDataSeeder.SeedAsync(db.Context);
        var edited = await db.Context.UnitTypes.SingleAsync(u => u.TenantId == tenantId && u.Code == "EUROPALLET");
        Assert.Equal(110m, edited.DefaultLengthCm);
        Assert.Equal(90m, edited.DefaultWidthCm);
    }
}
