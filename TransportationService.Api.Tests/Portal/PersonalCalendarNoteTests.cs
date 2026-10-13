using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Portal;

/// <summary>Spec 8: personal calendar notes are strictly self-scoped, palette-validated.</summary>
public class PersonalCalendarNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PortalService MeService, PortalService ColleagueService,
        Guid TenantId, Guid MeEmployeeId, Guid ColleagueEmployeeId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var meEmployeeId = Guid.NewGuid();
        var colleagueEmployeeId = Guid.NewGuid();
        var meUserId = Guid.NewGuid();
        var colleagueUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.Employees.AddRange(
            new Employee { Id = meEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", IsActive = true },
            new Employee { Id = colleagueEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2", FirstName = "Piet", LastName = "Peters", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = meUserId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Jansen", EmployeeId = meEmployeeId, IsActive = true },
            new User { Id = colleagueUserId, TenantId = tenantId, Email = "piet@acme.be", FirstName = "Piet", LastName = "Peters", EmployeeId = colleagueEmployeeId, IsActive = true });
        await db.Context.SaveChangesAsync();

        return new Harness(db,
            CreateSut(db, tenantId, meUserId),
            CreateSut(db, tenantId, colleagueUserId),
            tenantId, meEmployeeId, colleagueEmployeeId);
    }

    private static PortalService CreateSut(SqliteTestDbContext db, Guid tenantId, Guid userId)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var notifications = new NotificationService(db.Context, tenant, user, clock);
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-note-tests", Guid.NewGuid().ToString("N"));
        var absences = new AbsenceService(db.Context, tenant, user, audit, notifications,
            new LocalFileStorageService(storageRoot),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            clock);
        var qualifications = new QualificationService(
            db.Context, tenant, new QualificationStatusCalculator(), clock, audit,
            new TransportationService.Api.Common.Reference.CountryCodeValidator(db.Context),
            new LocalFileStorageService(storageRoot));
        return new PortalService(
            db.Context, tenant, user, absences, qualifications, notifications,
            new QualificationStatusCalculator(), new PasswordHasher(), audit,
            new TransportationService.Api.Modules.EmployeePlanning.Services.ShiftService(
                db.Context, tenant, audit, notifications,
                new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(), clock),
            new TransportationService.Api.Modules.Hr.Services.LeaveBalanceService(db.Context, tenant, audit),
            clock);
    }

    private static SavePersonalCalendarNoteRequest Note(
        string title = "Tandarts", string colour = "#16a34a", DateOnly? date = null, bool allDay = false) =>
        new(title, "Afspraak om 10u", date ?? new DateOnly(2026, 7, 27),
            allDay ? null : new TimeOnly(10, 0), allDay ? null : new TimeOnly(11, 0), allDay, colour);

    [Fact]
    public async Task Crud_IsSelfScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.MeService.CreateMyCalendarNoteAsync(Note(), CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal("Tandarts", created!.Title);
        Assert.Equal("#16a34a", created.Colour);

        // The colleague cannot see, edit or delete it.
        var colleagueList = await h.ColleagueService.ListMyCalendarNotesAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        Assert.Empty(colleagueList!);
        Assert.Null(await h.ColleagueService.UpdateMyCalendarNoteAsync(created.Id, Note("Gekaapt"), CancellationToken.None));
        Assert.False(await h.ColleagueService.DeleteMyCalendarNoteAsync(created.Id, CancellationToken.None));

        // The owner can.
        var updated = await h.MeService.UpdateMyCalendarNoteAsync(created.Id, Note("Auto garage", "#ea580c"), CancellationToken.None);
        Assert.Equal("Auto garage", updated!.Title);
        Assert.Equal("#ea580c", updated.Colour);
        Assert.True(await h.MeService.DeleteMyCalendarNoteAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task InvalidColour_OrMissingTitle_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.MeService.CreateMyCalendarNoteAsync(Note(colour: "red; background:url(x)"), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.MeService.CreateMyCalendarNoteAsync(Note(title: "  "), CancellationToken.None));
    }

    [Fact]
    public async Task Notes_AppearInOwnPlanningFeed_WithColour()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.MeService.CreateMyCalendarNoteAsync(Note(date: new DateOnly(2026, 7, 27)), CancellationToken.None);

        var days = await h.MeService.GetMyPlanningAsync(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 27), CancellationToken.None);
        var entry = Assert.Single(days![0].Entries);
        Assert.Equal(TransportationService.Api.Modules.EmployeePlanning.Dtos.ScheduleEntryState.Note, entry.State);
        Assert.Equal("Tandarts", entry.Label);
        Assert.Equal("#16a34a", entry.Colour);
        Assert.NotNull(entry.NoteId);

        // ...and never in someone else's feed.
        var colleagueDays = await h.ColleagueService.GetMyPlanningAsync(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 27), CancellationToken.None);
        Assert.Empty(colleagueDays![0].Entries);
    }

    [Fact]
    public async Task LeaveEntries_CarryLeaveTypeColour()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var leaveTypeId = Guid.NewGuid();
        h.Db.Context.LeaveTypes.Add(new LeaveType
        {
            Id = leaveTypeId, TenantId = h.TenantId, Code = "VAK", Name = "Vakantie",
            Colour = "#9333ea", IsActive = true,
        });
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.MeEmployeeId,
            Type = AbsenceType.Vacation, LeaveTypeId = leaveTypeId,
            StartDate = new DateOnly(2026, 7, 28), EndDate = new DateOnly(2026, 7, 28),
            Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();

        var days = await h.MeService.GetMyPlanningAsync(new DateOnly(2026, 7, 28), new DateOnly(2026, 7, 28), CancellationToken.None);
        var entry = Assert.Single(days![0].Entries);
        Assert.Equal("#9333ea", entry.Colour);
    }
}
