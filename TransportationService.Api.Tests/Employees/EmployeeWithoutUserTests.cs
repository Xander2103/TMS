using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
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
    internal static EmployeeService CreateSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid()));
        var driverService = new TransportationService.Api.Modules.Drivers.Services.DriverService(db.Context, tenant, audit,
            new TransportationService.Api.Modules.Qualifications.Services.QualificationStatusCalculator(), TimeProvider.System);
        var qualificationService = new TransportationService.Api.Modules.Qualifications.Services.QualificationService(
            db.Context, tenant, new TransportationService.Api.Modules.Qualifications.Services.QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        return new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context), driverService, qualificationService,
            new EmployeeCompletenessService(db.Context, tenant));
    }

    internal static CreateEmployeeRequest NewEmployee(string firstName, string lastName, string email) => new(
        firstName, lastName, new DateOnly(1990, 1, 1),
        "Kerkstraat", "1", "1000", "Brussel",
        "+32000000000", email, new DateOnly(2020, 1, 1),
        EmploymentStatus.Active, CountryCode: "BE");

    [Fact]
    public async Task CreateAsync_CreatesEmployee_WithNoLinkedUser()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Tenant", Slug = "tenant", CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "EMP-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewEmployee("Jan", "Janssen", "jan@example.com"),
            canEditConfidential: true, CancellationToken.None);

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
        await CountrySeeder.SyncAsync(db.Context);

        var sutA = CreateSut(db, tenantA);
        var sutB = CreateSut(db, tenantB);

        await sutA.CreateAsync(NewEmployee("Jan", "Janssen", "jan@a.com"), canEditConfidential: true, CancellationToken.None);

        var resultForTenantB = await sutB.SearchAsync(null, null, null, null, null, false, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Empty(resultForTenantB.Items);
    }
}
