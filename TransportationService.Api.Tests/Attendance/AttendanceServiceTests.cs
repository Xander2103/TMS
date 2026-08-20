using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Punch-state-machine server-side afgedwongen: dubbel inpunten, uitpunten zonder
/// sessie, pauzeregels, uitpunten tijdens pauze, meerdere pauzes, de databankinvariant
/// "één actieve sessie", inactieve medewerkers, settings-gates en tenant-isolatie.
/// </summary>
public class AttendanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 6, 0, 0, TimeSpan.Zero); // 08:00 Brussel

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, TestClock Clock)
    {
        public AttendanceService Sut(Guid? tenantOverride = null) =>
            new(Db.Context, new DevTenantContext(tenantOverride ?? TenantId), Clock);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, Timezone = "Europe/Brussels" });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-001",
            FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, new TestClock(Now));
    }

    private static AttendancePunchContext Web => new(AttendanceSource.Web);

    [Fact]
    public async Task ClockIn_CreatesActiveSession_WithImmutableEvent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var result = await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.Success, result.Outcome);
        Assert.Equal(AttendanceLiveStatus.Working, result.Status!.Status);
        Assert.True(result.Status.CanClockOut);
        Assert.True(result.Status.CanStartBreak);
        Assert.False(result.Status.CanClockIn);

        var session = Assert.Single(h.Db.Context.AttendanceSessions);
        Assert.Equal(AttendanceSessionStatus.Working, session.Status);
        Assert.Equal(Now.UtcDateTime, session.ClockInAt);
        var evt = Assert.Single(h.Db.Context.AttendanceEvents);
        Assert.Equal(AttendanceEventType.ClockIn, evt.EventType);
        Assert.Equal(session.Id, evt.SessionId);
    }

    [Fact]
    public async Task ClockIn_Twice_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        var second = await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.AlreadyClockedIn, second.Outcome);
        Assert.Single(h.Db.Context.AttendanceSessions);
    }

    [Fact]
    public async Task ClockOut_WithoutActiveSession_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut().ClockOutAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.NotClockedIn, result.Outcome);
    }

    [Fact]
    public async Task BreakRules_NoBreakWithoutClockIn_NoDoubleBreak_NoEndWithoutBreak()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        Assert.Equal(AttendancePunchOutcome.NotClockedIn,
            (await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None)).Outcome);

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        Assert.Equal(AttendancePunchOutcome.NoActiveBreak,
            (await sut.EndBreakAsync(h.EmployeeId, Web, CancellationToken.None)).Outcome);

        Assert.Equal(AttendancePunchOutcome.Success,
            (await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None)).Outcome);
        Assert.Equal(AttendancePunchOutcome.BreakAlreadyActive,
            (await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task MultipleBreaks_AccumulateInStatusTotals()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        h.Clock.Advance(TimeSpan.FromMinutes(120));
        await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        await sut.EndBreakAsync(h.EmployeeId, Web, CancellationToken.None);

        h.Clock.Advance(TimeSpan.FromMinutes(60));
        await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(28));
        await sut.EndBreakAsync(h.EmployeeId, Web, CancellationToken.None);

        h.Clock.Advance(TimeSpan.FromMinutes(30));
        var status = await sut.GetStatusAsync(h.EmployeeId, CancellationToken.None);

        Assert.Equal(38, status.BreakMinutesToday);
        Assert.Equal(120 + 60 + 30, status.WorkedMinutesToday);
        Assert.Equal(2, h.Db.Context.AttendanceBreaks.Count());
    }

    [Fact]
    public async Task ClockOut_WhileOnBreak_ClosesBreakAtSameInstant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await sut.StartBreakAsync(h.EmployeeId, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(15));

        var result = await sut.ClockOutAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.Success, result.Outcome);
        var session = Assert.Single(h.Db.Context.AttendanceSessions);
        Assert.Equal(AttendanceSessionStatus.Completed, session.Status);
        var brk = Assert.Single(h.Db.Context.AttendanceBreaks);
        Assert.Equal(session.ClockOutAt, brk.EndedAt);
        Assert.Contains(h.Db.Context.AttendanceEvents, e => e.EventType == AttendanceEventType.BreakEnded);
        Assert.Contains(h.Db.Context.AttendanceEvents, e => e.EventType == AttendanceEventType.ClockOut);
    }

    [Fact]
    public async Task Database_RejectsSecondActiveSessionForSameEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut().ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        h.Db.Context.AttendanceSessions.Add(new AttendanceSession
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            ClockInAt = Now.UtcDateTime.AddMinutes(1), Status = AttendanceSessionStatus.Working,
            ClockInSource = AttendanceSource.Web,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task InactiveEmployee_CannotPunch_ButHistoryRemains()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        await sut.ClockOutAsync(h.EmployeeId, Web, CancellationToken.None);

        var employee = h.Db.Context.Employees.Single(e => e.Id == h.EmployeeId);
        employee.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var result = await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.EmployeeInactive, result.Outcome);
        Assert.Single(h.Db.Context.AttendanceSessions); // historie blijft bestaan
    }

    [Fact]
    public async Task SelfPunchDisabled_BlocksWeb_ButKioskStillWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.AttendanceSettings.Add(new AttendanceSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, SelfPunchEnabled = false, KioskEnabled = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var sut = h.Sut();

        var web = await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        Assert.Equal(AttendancePunchOutcome.SelfPunchDisabled, web.Outcome);

        var kiosk = await sut.ClockInAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Kiosk), CancellationToken.None);
        Assert.Equal(AttendancePunchOutcome.Success, kiosk.Outcome);
    }

    [Fact]
    public async Task CrossTenant_EmployeeIsInvisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut(otherTenant).ClockInAsync(h.EmployeeId, Web, CancellationToken.None);

        Assert.Equal(AttendancePunchOutcome.EmployeeNotFound, result.Outcome);
        Assert.Empty(h.Db.Context.AttendanceSessions);
    }

    [Fact]
    public async Task GetStatus_AfterClockOut_ShowsClockedOutWithTotals()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(8));
        await sut.ClockOutAsync(h.EmployeeId, Web, CancellationToken.None);

        var status = await sut.GetStatusAsync(h.EmployeeId, CancellationToken.None);

        Assert.Equal(AttendanceLiveStatus.ClockedOut, status.Status);
        Assert.True(status.CanClockIn);
        Assert.False(status.CanClockOut);
        Assert.Equal(480, status.WorkedMinutesToday);
        Assert.NotNull(status.LastClockOutAt);
    }

    [Fact]
    public async Task GetHistory_SplitsOvernightShift_AndComparesWithPlanning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        // Nachtshift: in om 22:00 lokaal (20:00 UTC) op 18/08, uit om 06:00 lokaal 19/08.
        var clock = h.Clock;
        var start = new DateTimeOffset(2026, 8, 18, 20, 0, 0, TimeSpan.Zero);
        clock.Advance(start - Now);
        await sut.ClockInAsync(h.EmployeeId, Web, CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(8));
        await sut.ClockOutAsync(h.EmployeeId, Web, CancellationToken.None);

        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Date = new DateOnly(2026, 8, 18), StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(23, 59),
            BreakMinutes = 0, Type = ShiftType.Work, Status = ShiftStatus.Planned,
        });
        await h.Db.Context.SaveChangesAsync();

        var history = await sut.GetHistoryAsync(
            h.EmployeeId, new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 20), CancellationToken.None);

        Assert.Equal(2, history.Days.Count);
        var day1 = history.Days.Single(d => d.Date == new DateOnly(2026, 8, 18));
        var day2 = history.Days.Single(d => d.Date == new DateOnly(2026, 8, 19));
        Assert.Equal(120, day1.NetMinutes);
        Assert.Equal(360, day2.NetMinutes);
        Assert.Equal(119, day1.PlannedMinutes);
        Assert.Equal(120 - 119, day1.DeviationMinutes);
        Assert.Equal(480, history.TotalNetMinutes);
        // Beide dagen tonen dezelfde sessie: één registratie, gesplitst voor rapportage.
        Assert.Equal(day1.Sessions.Single().Id, day2.Sessions.Single().Id);
    }
}
