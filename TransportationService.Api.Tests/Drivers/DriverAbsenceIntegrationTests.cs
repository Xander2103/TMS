using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Drivers;

/// <summary>An approved absence covering today must drive both effective availability and readiness.</summary>
public class DriverAbsenceIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, DriverService Sut, Guid TenantId, Guid EmployeeId, Guid DriverId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver
        {
            Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId,
            AvailabilityStatus = DriverAvailabilityStatus.Available, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new DriverService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new QualificationStatusCalculator(),
            new TestClock(Now));
        return new Harness(db, sut, tenantId, employeeId, driverId);
    }

    private static Absence Approved(Guid tenantId, Guid employeeId, string start, string end, AbsenceType type = AbsenceType.Vacation) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
        Type = type, StartDate = DateOnly.Parse(start), EndDate = DateOnly.Parse(end),
        Status = AbsenceStatus.Approved,
    };

    [Fact]
    public async Task ApprovedAbsenceToday_OverridesAvailability_AndBlocksReadiness()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Absences.Add(Approved(h.TenantId, h.EmployeeId, "2026-07-13", "2026-07-24"));
        await h.Db.Context.SaveChangesAsync();

        var list = await h.Sut.SearchAsync(null, null, null, null, null, null, null, false, false, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(DriverAvailabilityStatus.OnLeave, list.Items[0].AvailabilityStatus);

        var detail = await h.Sut.GetByIdAsync(h.DriverId, CancellationToken.None);
        Assert.Equal(DriverAvailabilityStatus.OnLeave, detail!.AvailabilityStatus);
        Assert.Equal("NotReady", detail.Readiness.Status);
        Assert.Contains(detail.Readiness.BlockingReasons, r => r.Contains("afwezig", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RequestedOrPastAbsence_DoesNotAffectAvailability()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Requested (not approved) covering today + an approved one entirely in the past.
        var requested = Approved(h.TenantId, h.EmployeeId, "2026-07-13", "2026-07-24");
        requested.Status = AbsenceStatus.Requested;
        h.Db.Context.Absences.AddRange(
            requested,
            Approved(h.TenantId, h.EmployeeId, "2026-06-01", "2026-06-05", AbsenceType.Sick));
        await h.Db.Context.SaveChangesAsync();

        var list = await h.Sut.SearchAsync(null, null, null, null, null, null, null, false, false, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(DriverAvailabilityStatus.Available, list.Items[0].AvailabilityStatus);

        var detail = await h.Sut.GetByIdAsync(h.DriverId, CancellationToken.None);
        Assert.Equal("Ready", detail!.Readiness.Status);
    }
}
