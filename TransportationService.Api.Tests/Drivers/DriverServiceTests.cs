using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Drivers;

public class DriverServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, DriverService Sut, Guid TenantId, Guid EmployeeId);

    private static DriverService BuildService(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new DriverService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new QualificationStatusCalculator(),
            new TestClock(Now));
    }

    private static async Task<Harness> SeedAsync(Guid tenantId, bool withSettings = true)
    {
        var db = new SqliteTestDbContext();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });

        if (withSettings)
        {
            db.Context.TenantSettings.Add(new TenantSettings
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DriverNumberPrefix = "CH-", DriverNumberNextValue = 1,
                QualificationExpiryWarningDays = 30,
            });
        }

        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-0001",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        return new Harness(db, BuildService(db, tenantId), tenantId, employeeId);
    }

    private static CreateDriverRequest CreateRequest(Guid employeeId) =>
        new(employeeId, null, DriverAvailabilityStatus.Available, false, null, null, null, null);

    [Fact]
    public async Task Create_GeneratesDriverNumber_AndLinksEmployee()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.Success, result.Outcome);
        Assert.Equal("CH-0001", result.Driver!.DriverNumber);
        Assert.Equal(employeeId, result.Driver.EmployeeId);
        Assert.Equal("Jan Jansen", result.Driver.FullName);
        Assert.Equal("Ready", result.Driver.Readiness.Status);
    }

    [Fact]
    public async Task Create_WhenEmployeeMissing_ReturnsEmployeeNotFound()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(CreateRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.EmployeeNotFound, result.Outcome);
    }

    [Fact]
    public async Task Create_WhenEmployeeAlreadyDriver_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        var second = await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.EmployeeAlreadyDriver, second.Outcome);
    }

    [Fact]
    public async Task Search_DoesNotReturnOtherTenantsDrivers()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        // A driver in another tenant must never surface.
        var otherTenant = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Employees.Add(new Employee { Id = otherEmployeeId, TenantId = otherTenant, EmployeeNumber = "X", FirstName = "A", LastName = "B", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Set<Driver>().Add(new Driver { Id = Guid.NewGuid(), TenantId = otherTenant, DriverNumber = "CH-9999", EmployeeId = otherEmployeeId, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var page = await h.Sut.SearchAsync(null, null, null, null, null, null, null, false, false, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(tenantId, h.TenantId);
        Assert.DoesNotContain(page.Items, d => d.DriverNumber == "CH-9999");
    }

    [Fact]
    public async Task SetBlocked_MarksReadinessBlockedWithReason()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        var result = await h.Sut.SetBlockedAsync(created.Driver!.Id, new SetDriverBlockedRequest(true, "Rijbewijs ingetrokken"), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.Success, result.Outcome);
        Assert.True(result.Driver!.IsBlocked);
        Assert.Equal("Blocked", result.Driver.Readiness.Status);
        Assert.Contains(result.Driver.Readiness.BlockingReasons, r => r.Contains("Rijbewijs ingetrokken"));
    }

    [Fact]
    public async Task Delete_SoftDeletes_DriverNoLongerListedButRowRemains()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        var deleted = await h.Sut.DeleteAsync(created.Driver!.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await h.Sut.GetByIdAsync(created.Driver.Id, CancellationToken.None));
        // Row still physically present with IsDeleted = true (ignore global filter).
        var raw = h.Db.Context.Set<Driver>().IgnoreQueryFilters().Single(d => d.Id == created.Driver.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task Readiness_ReflectsExpiredQualification()
    {
        var tenantId = Guid.NewGuid();
        var h = await SeedAsync(tenantId);
        var employeeId = h.EmployeeId;
        using var _ = h.Db;

        var typeId = Guid.NewGuid();
        h.Db.Context.QualificationTypes.Add(new QualificationType { Id = typeId, Code = "code95", Name = "Code 95", Category = "Certificaat", RequiresExpiryDate = true, IsActive = true });
        h.Db.Context.EmployeeQualifications.Add(new EmployeeQualification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, QualificationTypeId = typeId,
            ObtainedDate = new DateOnly(2020, 1, 1), ExpiryDate = new DateOnly(2026, 1, 1), // already expired vs Now (Jul 2026)
            Status = QualificationStatus.Valid, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Sut.CreateAsync(CreateRequest(employeeId), CancellationToken.None);

        Assert.Equal("NotReady", created.Driver!.Readiness.Status);
        Assert.Contains(created.Driver.Readiness.BlockingReasons, r => r.Contains("Code 95") && r.Contains("verlopen"));
    }
}
