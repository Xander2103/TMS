using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

public class EmployeeWithoutUserTests
{
    [Fact]
    public async Task CreateAsync_CreatesEmployee_WithNoLinkedUser()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Tenant", Slug = "tenant", CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "EMP-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var sut = new EmployeeService(db.Context, new DevTenantContext(tenantId), new AuditService(db.Context, new DevTenantContext(tenantId), new DevCurrentUserContext(Guid.NewGuid())));

        var created = await sut.CreateAsync(new CreateEmployeeRequest(
            "Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE",
            "+32000000000", "jan@example.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1),
            EmploymentStatus.Active, EmployeeFunction.DriverC, null, null, null), CancellationToken.None);

        Assert.Equal("EMP-0001", created.EmployeeNumber);
        var usersLinkedToEmployee = db.Context.Users.Count(u => u.EmployeeId == created.Id);
        Assert.Equal(0, usersLinkedToEmployee);
    }

    [Fact]
    public async Task SearchAsync_DoesNotReturnEmployeesFromOtherTenants()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantA, Name = "Tenant A", Slug = "tenant-a", CreatedAt = DateTime.UtcNow });
        db.Context.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B", Slug = "tenant-b", CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "A-", EmployeeNumberNextValue = 1 });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeNumberPrefix = "B-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var sutA = new EmployeeService(db.Context, new DevTenantContext(tenantA), new AuditService(db.Context, new DevTenantContext(tenantA), new DevCurrentUserContext(Guid.NewGuid())));
        var sutB = new EmployeeService(db.Context, new DevTenantContext(tenantB), new AuditService(db.Context, new DevTenantContext(tenantB), new DevCurrentUserContext(Guid.NewGuid())));

        await sutA.CreateAsync(new CreateEmployeeRequest("Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE", "+32", "jan@a.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);

        var resultForTenantB = await sutB.SearchAsync(null, null, 1, 25, CancellationToken.None);

        Assert.Empty(resultForTenantB.Items);
    }
}
