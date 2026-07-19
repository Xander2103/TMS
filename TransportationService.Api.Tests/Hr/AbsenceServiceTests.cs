using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

public class AbsenceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeciderId = Guid.NewGuid();

    private sealed record Harness(SqliteTestDbContext Db, AbsenceService Sut, Guid TenantId, Guid EmployeeId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new AbsenceService(
            db.Context, tenant, new DevCurrentUserContext(DeciderId),
            new AuditService(db.Context, tenant, new DevCurrentUserContext(DeciderId)),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(DeciderId), new TestClock(Now)),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), "ts-absence-tests", Guid.NewGuid().ToString("N"))),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            new TransportationService.Api.Modules.Messaging.Services.MessageOutboxService(
                db.Context, tenant, new TestClock(Now)),
            new TestClock(Now));
        return new Harness(db, sut, tenantId, employeeId);
    }

    private static CreateAbsenceRequest Request(
        AbsenceType type = AbsenceType.Vacation, string start = "2026-08-03", string end = "2026-08-14") =>
        new(type, DateOnly.Parse(start), DateOnly.Parse(end), "Zomervakantie");

    [Fact]
    public async Task Create_StartsRequested_WithEmployeeInfo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.Success, result.Outcome);
        Assert.Equal(AbsenceStatus.Requested, result.Absence!.Status);
        Assert.Equal("Jan Jansen", result.Absence.EmployeeName);
        Assert.False(result.Absence.IsDriver);
    }

    [Fact]
    public async Task Create_EndBeforeStart_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForEmployeeAsync(h.EmployeeId,
            Request(start: "2026-08-14", end: "2026-08-03"), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Create_ForeignEmployee_ReturnsOwnerNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForEmployeeAsync(Guid.NewGuid(), Request(), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.OwnerNotFound, result.Outcome);
    }

    [Fact]
    public async Task Create_OverlappingPeriod_Conflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);

        var overlapping = await h.Sut.CreateForEmployeeAsync(h.EmployeeId,
            Request(AbsenceType.Training, "2026-08-10", "2026-08-20"), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.Overlap, overlapping.Outcome);
    }

    [Fact]
    public async Task Create_AfterRejection_PeriodIsFreeAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);
        await h.Sut.DecideAsync(first.Absence!.Id, new DecideAbsenceRequest(false, "Te druk"), CancellationToken.None);

        var second = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.Success, second.Outcome);
    }

    [Fact]
    public async Task Decide_Approves_WithDeciderAndTimestamp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);

        var approved = await h.Sut.DecideAsync(created.Absence!.Id,
            new DecideAbsenceRequest(true, "Goedgekeurd"), CancellationToken.None);

        Assert.Equal(AbsenceStatus.Approved, approved.Absence!.Status);
        Assert.Equal(Now.UtcDateTime, approved.Absence.DecidedAt);
        var stored = await h.Db.Context.Absences.FindAsync(created.Absence.Id);
        Assert.Equal(DeciderId, stored!.DecidedByUserId);
    }

    [Fact]
    public async Task Decide_Twice_IsInvalidState()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);
        await h.Sut.DecideAsync(created.Absence!.Id, new DecideAbsenceRequest(true, null), CancellationToken.None);

        var again = await h.Sut.DecideAsync(created.Absence.Id, new DecideAbsenceRequest(false, null), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.InvalidState, again.Outcome);
    }

    [Fact]
    public async Task Update_OnlyWhileRequested()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);
        await h.Sut.DecideAsync(created.Absence!.Id, new DecideAbsenceRequest(true, null), CancellationToken.None);

        var update = await h.Sut.UpdateAsync(created.Absence.Id,
            new UpdateAbsenceRequest(AbsenceType.Vacation, new(2026, 8, 3), new(2026, 8, 21), null), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.InvalidState, update.Outcome);
    }

    [Fact]
    public async Task Cancel_ApprovedAbsence_FreesPeriod()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);
        await h.Sut.DecideAsync(created.Absence!.Id, new DecideAbsenceRequest(true, null), CancellationToken.None);

        var cancelled = await h.Sut.CancelAsync(created.Absence.Id, CancellationToken.None);
        Assert.Equal(AbsenceStatus.Cancelled, cancelled.Absence!.Status);

        var recreate = await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, recreate.Outcome);
    }

    [Fact]
    public async Task List_FiltersRangeAndIsolatesTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(start: "2026-08-03", end: "2026-08-14"), CancellationToken.None);
        await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(AbsenceType.Training, "2026-12-01", "2026-12-02"), CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        var foreignEmployee = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Employees.Add(new Employee
        {
            Id = foreignEmployee, TenantId = foreignTenant, EmployeeNumber = "X",
            FirstName = "F", LastName = "D", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, EmployeeId = foreignEmployee,
            Type = AbsenceType.Vacation, StartDate = new(2026, 8, 5), EndDate = new(2026, 8, 6),
        });
        await h.Db.Context.SaveChangesAsync();

        // Default window: today (2026-07-18) + 60 days — includes August, excludes December.
        var window = await h.Sut.ListAsync(null, null, null, null, CancellationToken.None);

        var single = Assert.Single(window);
        Assert.Equal(new DateOnly(2026, 8, 3), single.StartDate);
        Assert.Equal("MED-1", single.EmployeeNumber);
    }
}
