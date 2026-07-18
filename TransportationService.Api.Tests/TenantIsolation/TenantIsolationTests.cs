using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.TenantIsolation;

public class TenantIsolationTests
{
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

        var serviceForTenantA = new EmployeeService(db.Context, new DevTenantContext(tenantA), new AuditService(db.Context, new DevTenantContext(tenantA), new DevCurrentUserContext(Guid.NewGuid())));
        var created = await serviceForTenantA.CreateAsync(new CreateEmployeeRequest(
            "Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE", "+32", "jan@a.com",
            new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null),
            CancellationToken.None);

        var serviceForTenantB = new EmployeeService(db.Context, new DevTenantContext(tenantB), new AuditService(db.Context, new DevTenantContext(tenantB), new DevCurrentUserContext(Guid.NewGuid())));
        var resultFromWrongTenant = await serviceForTenantB.GetByIdAsync(created.Id, CancellationToken.None);

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

        var serviceForTenantA = new EmployeeService(db.Context, new DevTenantContext(tenantA), new AuditService(db.Context, new DevTenantContext(tenantA), new DevCurrentUserContext(Guid.NewGuid())));
        var serviceForTenantB = new EmployeeService(db.Context, new DevTenantContext(tenantB), new AuditService(db.Context, new DevTenantContext(tenantB), new DevCurrentUserContext(Guid.NewGuid())));

        var createdA = await serviceForTenantA.CreateAsync(new CreateEmployeeRequest("Jan", "A", "S", "1", "1000", "C", "BE", "+32", "a@a.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);
        var createdB = await serviceForTenantB.CreateAsync(new CreateEmployeeRequest("Piet", "B", "S", "1", "1000", "C", "BE", "+32", "b@b.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);

        Assert.Equal(createdA.EmployeeNumber, createdB.EmployeeNumber);
    }
}
