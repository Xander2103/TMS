using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.Employees;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.TenantIsolation;

public class TenantIsolationTests
{
    private static EmployeeService CreateSut(SqliteTestDbContext db, Guid tenantId) =>
        EmployeeWithoutUserTests.CreateSut(db, tenantId);

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_ForEmployeeBelongingToAnotherTenant()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantA, Name = "Tenant A", Slug = "tenant-a", CreatedAt = DateTime.UtcNow });
        db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B", Slug = "tenant-b", CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "A-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var serviceForTenantA = CreateSut(db, tenantA);
        var created = await serviceForTenantA.CreateAsync(
            EmployeeWithoutUserTests.NewEmployee("Jan", "Janssen", "jan@a.com"),
            canEditConfidential: true, CancellationToken.None);

        var serviceForTenantB = CreateSut(db, tenantB);
        var resultFromWrongTenant = await serviceForTenantB.GetByIdAsync(created.Id, includeConfidential: true, CancellationToken.None);

        Assert.Null(resultFromWrongTenant);
    }

    [Fact]
    public async Task DifferentTenants_CanUseTheSameEmployeeNumber_BecauseUniquenessIsTenantScoped()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantA, Name = "Tenant A", Slug = "tenant-a-same", CreatedAt = DateTime.UtcNow });
        db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B", Slug = "tenant-b-same", CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "SAME-", EmployeeNumberNextValue = 1 });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeNumberPrefix = "SAME-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var serviceForTenantA = CreateSut(db, tenantA);
        var serviceForTenantB = CreateSut(db, tenantB);

        var createdA = await serviceForTenantA.CreateAsync(
            EmployeeWithoutUserTests.NewEmployee("Jan", "A", "a@a.com"), canEditConfidential: true, CancellationToken.None);
        var createdB = await serviceForTenantB.CreateAsync(
            EmployeeWithoutUserTests.NewEmployee("Piet", "B", "b@b.com"), canEditConfidential: true, CancellationToken.None);

        Assert.Equal(createdA.EmployeeNumber, createdB.EmployeeNumber);
    }
}
