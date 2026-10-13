using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Employee-lifecycle × attendance: deactivering trekt de prikklokcode in én sluit een
/// nog openstaande sessie traceerbaar af (met event); historiek blijft bestaan.
/// </summary>
public class AttendanceEmployeeLifecycleTests
{
    [Fact]
    public async Task Deactivate_DisablesCredential_AndClosesOpenSessionWithEvent()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-1",
            FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        db.Context.AttendanceCredentials.Add(new AttendanceCredential
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            SecretHash = "hash", LookupHash = "lookup", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 6, 0, 0, TimeSpan.Zero));
        var attendance = new AttendanceService(db.Context, tenant, clock);
        await attendance.ClockInAsync(employeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(2));
        await attendance.StartBreakAsync(employeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);

        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(db.Context, tenant, new QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context),
            drivers, qualifications, new EmployeeCompletenessService(db.Context, tenant));

        var result = await employees.DeactivateAsync(employeeId, CancellationToken.None);

        Assert.True(result);
        Assert.False(db.Context.AttendanceCredentials.Single().IsActive);
        var session = db.Context.AttendanceSessions.Single();
        Assert.NotNull(session.ClockOutAt);
        Assert.Equal(AttendanceSessionStatus.Completed, session.Status);
        Assert.NotNull(db.Context.AttendanceBreaks.Single().EndedAt);
        Assert.Contains(db.Context.AttendanceEvents,
            e => e.EventType == AttendanceEventType.ClockOut && e.Note != null && e.Note.Contains("deactivering"));
    }
}
