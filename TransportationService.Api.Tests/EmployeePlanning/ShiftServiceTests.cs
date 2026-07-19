using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.EmployeePlanning;

/// <summary>
/// Wave 6: personnel shifts (separate from trip planning) with a controlled status flow,
/// overlap guards, copy-week and a schedule grid that merges shifts with absences into
/// explainable day states.
/// </summary>
public class ShiftServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 7, 20);

    private sealed record Harness(
        SqliteTestDbContext Db, ShiftService Sut, Guid TenantId, Guid EmployeeId, Guid OtherEmployeeId,
        Guid DepartmentId, Guid EmployeeUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "MAG", Name = "Magazijn", IsActive = true });
        db.Context.Employees.AddRange(
            new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
                FirstName = "Jan", LastName = "Jansen", DepartmentId = departmentId,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new Employee
            {
                Id = otherEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2",
                FirstName = "Piet", LastName = "Peters",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Users.Add(new User
        {
            Id = employeeUserId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        return new Harness(db, CreateSut(db, tenantId), tenantId, employeeId, otherEmployeeId, departmentId, employeeUserId);
    }

    private static ShiftService CreateSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        return new ShiftService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, new TestClock(Now)),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            new TestClock(Now));
    }

    private static CreateShiftRequest Request(
        Guid employeeId, DateOnly? date = null, string start = "08:00", string end = "16:30",
        int breakMinutes = 30, ShiftType type = ShiftType.Work) =>
        new(employeeId, date ?? Monday, TimeOnly.Parse(start), TimeOnly.Parse(end), breakMinutes,
            type, "Magazijn Antwerpen", "Magazijnier", null);

    [Fact]
    public async Task Create_ComputesPlannedMinutes_AndNotifiesEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.EmployeeId), false, CancellationToken.None);

        Assert.Equal(ShiftOutcome.Success, result.Outcome);
        Assert.Equal(ShiftStatus.Draft, result.Shift!.Status);
        // 08:00-16:30 minus 30 min break = 480 minutes.
        Assert.Equal(480, result.Shift.PlannedMinutes);
        Assert.Contains(h.Db.Context.Notifications, n => n.UserId == h.EmployeeUserId && n.Type == "shift_assigned");
    }

    [Fact]
    public async Task Create_Validates_Times_And_Overlap()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var endBeforeStart = await h.Sut.CreateAsync(
            Request(h.EmployeeId, start: "16:00", end: "08:00"), false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.ValidationFailed, endBeforeStart.Outcome);

        var breakTooLong = await h.Sut.CreateAsync(
            Request(h.EmployeeId, start: "08:00", end: "09:00", breakMinutes: 90), false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.ValidationFailed, breakTooLong.Outcome);

        await h.Sut.CreateAsync(Request(h.EmployeeId), false, CancellationToken.None);
        var overlapping = await h.Sut.CreateAsync(
            Request(h.EmployeeId, start: "12:00", end: "20:00"), false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Overlap, overlapping.Outcome);

        // A different employee on the same slot is fine.
        var colleague = await h.Sut.CreateAsync(Request(h.OtherEmployeeId), false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Success, colleague.Outcome);

        var unknownEmployee = await h.Sut.CreateAsync(Request(Guid.NewGuid()), false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.NotFound, unknownEmployee.Outcome);
    }

    [Fact]
    public async Task StatusFlow_DraftPlannedConfirmed_GuardsJumps()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shift = (await h.Sut.CreateAsync(Request(h.EmployeeId), false, CancellationToken.None)).Shift!;

        // Draft -> Confirmed directly is not allowed.
        var jump = await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Confirmed, CancellationToken.None);
        Assert.Equal(ShiftOutcome.InvalidState, jump.Outcome);

        Assert.Equal(ShiftOutcome.Success, (await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Planned, CancellationToken.None)).Outcome);
        var confirmed = await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Confirmed, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Success, confirmed.Outcome);
        Assert.Equal(ShiftStatus.Confirmed, confirmed.Shift!.Status);

        // Confirmed can fall back to Planned (retraction), never straight to Draft.
        Assert.Equal(ShiftOutcome.InvalidState, (await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Draft, CancellationToken.None)).Outcome);
        Assert.Equal(ShiftOutcome.Success, (await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Planned, CancellationToken.None)).Outcome);
        Assert.Equal(ShiftOutcome.Success, (await h.Sut.ChangeStatusAsync(shift.Id, ShiftStatus.Draft, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task CopyWeek_CopiesAsDraft_SkipsCollisions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday), false, CancellationToken.None);
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday.AddDays(1)), false, CancellationToken.None);
        // Target week already has a shift on Tuesday: that day is skipped.
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday.AddDays(8), start: "10:00", end: "14:00", breakMinutes: 0), false, CancellationToken.None);

        var result = await h.Sut.CopyWeekAsync(
            new CopyWeekRequest(Monday, Monday.AddDays(7), h.EmployeeId, null), CancellationToken.None);

        Assert.Equal(ShiftOutcome.Success, result.Outcome);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(1, result.SkippedCount);

        var grid = await h.Sut.GetScheduleAsync(Monday.AddDays(7), Monday.AddDays(13), null, h.EmployeeId, CancellationToken.None);
        var monday2 = grid.Rows.Single().Days.Single(d => d.Date == Monday.AddDays(7));
        Assert.Contains(monday2.Entries, e => e.State == ScheduleEntryState.Draft);
    }

    [Fact]
    public async Task Schedule_MergesShiftsAndAbsences_IntoExplainableStates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday), false, CancellationToken.None);
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday.AddDays(1), type: ShiftType.Training), false, CancellationToken.None);
        await h.Sut.CreateAsync(Request(h.EmployeeId, Monday.AddDays(2), type: ShiftType.Standby), false, CancellationToken.None);
        h.Db.Context.Absences.AddRange(
            new Absence
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
                Type = AbsenceType.Vacation, StartDate = Monday.AddDays(3), EndDate = Monday.AddDays(3),
                Status = AbsenceStatus.Approved,
            },
            new Absence
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
                Type = AbsenceType.Sick, StartDate = Monday.AddDays(4), EndDate = Monday.AddDays(4),
                Status = AbsenceStatus.Approved,
            },
            new Absence
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.OtherEmployeeId,
                Type = AbsenceType.Vacation, StartDate = Monday, EndDate = Monday,
                Status = AbsenceStatus.Requested,
            });
        await h.Db.Context.SaveChangesAsync();

        var grid = await h.Sut.GetScheduleAsync(Monday, Monday.AddDays(6), null, null, CancellationToken.None);

        Assert.Equal(2, grid.Rows.Count);
        var jan = grid.Rows.Single(r => r.EmployeeId == h.EmployeeId);
        Assert.Equal(ScheduleEntryState.Draft, jan.Days.Single(d => d.Date == Monday).Entries.Single().State);
        Assert.Equal(ScheduleEntryState.Training, jan.Days.Single(d => d.Date == Monday.AddDays(1)).Entries.Single().State);
        Assert.Equal(ScheduleEntryState.Standby, jan.Days.Single(d => d.Date == Monday.AddDays(2)).Entries.Single().State);
        Assert.Equal(ScheduleEntryState.LeaveApproved, jan.Days.Single(d => d.Date == Monday.AddDays(3)).Entries.Single().State);
        Assert.Equal(ScheduleEntryState.Sick, jan.Days.Single(d => d.Date == Monday.AddDays(4)).Entries.Single().State);

        var piet = grid.Rows.Single(r => r.EmployeeId == h.OtherEmployeeId);
        Assert.Equal(ScheduleEntryState.LeaveRequested, piet.Days.Single(d => d.Date == Monday).Entries.Single().State);

        // Weekly planned minutes only count real work shifts (work + standby + training all planned).
        Assert.Equal(480 * 3, jan.PlannedMinutes);
    }

    [Fact]
    public async Task Schedule_FiltersByDepartment_AndIsolatesTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h.EmployeeId), false, CancellationToken.None);
        await h.Sut.CreateAsync(Request(h.OtherEmployeeId), false, CancellationToken.None);

        var department = await h.Sut.GetScheduleAsync(Monday, Monday.AddDays(6), h.DepartmentId, null, CancellationToken.None);
        Assert.Single(department.Rows);
        Assert.Equal(h.EmployeeId, department.Rows[0].EmployeeId);

        var foreign = CreateSut(h.Db, Guid.NewGuid());
        var foreignGrid = await foreign.GetScheduleAsync(Monday, Monday.AddDays(6), null, null, CancellationToken.None);
        Assert.Empty(foreignGrid.Rows);
    }

    [Fact]
    public async Task UpdateAndDelete_Work_WithAudit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shift = (await h.Sut.CreateAsync(Request(h.EmployeeId), false, CancellationToken.None)).Shift!;

        var updated = await h.Sut.UpdateAsync(shift.Id,
            new UpdateShiftRequest(Monday, new(9, 0), new(17, 0), 45, ShiftType.Work, "Magazijn Gent", "Heftruck", "Aangepast"),
            false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Success, updated.Outcome);
        Assert.Equal(435, updated.Shift!.PlannedMinutes);
        Assert.Equal("Magazijn Gent", updated.Shift.WorkLocation);

        Assert.True(await h.Sut.DeleteAsync(shift.Id, CancellationToken.None));
        Assert.False(await h.Sut.DeleteAsync(shift.Id, CancellationToken.None));
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "Shift" && a.Action == "Deleted");
    }
}
