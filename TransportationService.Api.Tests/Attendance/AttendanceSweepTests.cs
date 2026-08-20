using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Attendance-sweep: vergeten-uitpunt-detectie met one-shot-stempel en dedupe (geen
/// notificatiespam), drempel uit tenant-instellingen, en auto-close die UIT staat by
/// default en — indien ingeschakeld — altijd status AutoClosed + event + audit
/// achterlaat en de open pauze sluit.
/// </summary>
public class AttendanceSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid UserId, TestClock Clock)
    {
        public AttendanceSweepWorker Worker() =>
            new(Db.Context, Clock, NullLogger<AttendanceSweepWorker>.Instance);

        public AttendanceService Attendance() => new(Db.Context, new DevTenantContext(TenantId), Clock);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-1",
            FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.test", FirstName = "Jan", LastName = "Peeters",
            EmployeeId = employeeId, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, userId, new TestClock(Now));
    }

    private static async Task ClockInHoursAgoAsync(Harness h, double hoursAgo, bool withOpenBreak = false)
    {
        var attendance = h.Attendance();
        h.Clock.Advance(TimeSpan.FromHours(-hoursAgo));
        await attendance.ClockInAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        if (withOpenBreak)
        {
            h.Clock.Advance(TimeSpan.FromHours(1));
            await attendance.StartBreakAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromHours(hoursAgo - 1));
        }
        else
        {
            h.Clock.Advance(TimeSpan.FromHours(hoursAgo));
        }
    }

    [Fact]
    public async Task Sweep_WarnsExactlyOnce_ForForgottenClockOut()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await ClockInHoursAgoAsync(h, 20); // > default 16 u

        await h.Worker().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.Worker().SweepTenantAsync(h.TenantId, CancellationToken.None);

        var notifications = h.Db.Context.Notifications
            .Where(n => n.Type == "attendance_forgotten_clockout" && n.UserId == h.UserId)
            .ToList();
        Assert.Single(notifications);

        var session = h.Db.Context.AttendanceSessions.Single();
        Assert.NotNull(session.ForgottenClockOutNotifiedAt);
        Assert.Null(session.ClockOutAt); // default: waarschuwen, niet afsluiten
        Assert.Equal(AttendanceSessionStatus.Working, session.Status);
    }

    [Fact]
    public async Task Sweep_RespectsConfiguredThreshold()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, ForgottenClockOutAfterHours = 10,
        });
        await h.Db.Context.SaveChangesAsync();
        await ClockInHoursAgoAsync(h, 8); // onder de grens van 10 u

        await h.Worker().SweepTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(h.Db.Context.Notifications.Where(n => n.Type == "attendance_forgotten_clockout"));
        Assert.Null(h.Db.Context.AttendanceSessions.Single().ForgottenClockOutNotifiedAt);
    }

    [Fact]
    public async Task AutoClose_IsOffByDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await ClockInHoursAgoAsync(h, 30);

        await h.Worker().SweepTenantAsync(h.TenantId, CancellationToken.None);

        var session = h.Db.Context.AttendanceSessions.Single();
        Assert.Null(session.ClockOutAt);
        Assert.NotEqual(AttendanceSessionStatus.AutoClosed, session.Status);
    }

    [Fact]
    public async Task AutoClose_WhenEnabled_ClosesWithEventAuditAndBreakEnd()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, AutoCloseEnabled = true, AutoCloseAfterHours = 18,
        });
        await h.Db.Context.SaveChangesAsync();
        await ClockInHoursAgoAsync(h, 20, withOpenBreak: true);

        await h.Worker().SweepTenantAsync(h.TenantId, CancellationToken.None);

        var session = h.Db.Context.AttendanceSessions.Single();
        Assert.Equal(AttendanceSessionStatus.AutoClosed, session.Status);
        Assert.Equal(Now.UtcDateTime, session.ClockOutAt);
        Assert.Equal(Now.UtcDateTime, h.Db.Context.AttendanceBreaks.Single().EndedAt);
        Assert.Contains(h.Db.Context.AttendanceEvents, e => e.EventType == AttendanceEventType.AutoClosed);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "AttendanceSession" && a.Action == "AutoClosed");
    }
}
