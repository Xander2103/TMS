using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reference;

public class UnitTypeTests
{
    [Fact]
    public async Task Seeder_AddsUnitTypes_WithStableCodes()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        await ReferenceDataSeeder.SeedAsync(db.Context);

        var units = await db.Context.UnitTypes.Where(u => u.TenantId == tenantId).ToListAsync();
        Assert.Contains(units, u => u.Code == "COLLI" && u.Name == "Colli");
        Assert.Contains(units, u => u.Code == "LOADINGMETER" && u.Name == "Laadmeter");
        Assert.Contains(units, u => u.Code == "KG");
        Assert.True(units.Count >= 16);
    }

    [Fact]
    public async Task LookupService_CreatesAndListsUnitTypes()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var service = new LookupService<UnitType>(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));

        var created = await service.CreateAsync(new CreateLookupRequest("SPECIAL", "Speciaal", null, true, 0), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.Success, created.Status);
        Assert.Equal("SPECIAL", created.Item!.Code);

        var options = await service.ListOptionsAsync(CancellationToken.None);
        Assert.Contains(options, o => o.Code == "SPECIAL");
    }
}
