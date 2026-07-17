using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Eligibility;

public class EligibilityOverrideServiceTests
{
    [Fact]
    public async Task CreateAsync_Throws_WhenReasonIsBlank()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(Guid.NewGuid(), "TransportOrder", null, "AdrRequired", "   ", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RecordsApprovingUser_AndWritesAuditLog()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "EMP-0001", FirstName = "Jan", LastName = "Janssen",
            Street = "Kerkstraat", HouseNumber = "1", PostalCode = "1000", City = "Brussel", Country = "BE",
            PhoneNumber = "+32", Email = "jan@example.com", DateOfBirth = new DateOnly(1990, 1, 1),
            EmploymentStartDate = new DateOnly(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active,
            PrimaryFunction = EmployeeFunction.DriverC, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        var result = await sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(employeeId, "TransportOrder", null, "AdrRequired", "Klant heeft alternatieve begeleiding geregeld.", null),
            CancellationToken.None);

        Assert.Equal(approverId, result.ApprovedByUserId);
        Assert.Single(db.Context.AuditLogs);
    }
}
