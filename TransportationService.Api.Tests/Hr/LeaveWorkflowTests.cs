using System.Text;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

/// <summary>
/// Wave 7: the complete leave workflow — review states with a change-request loop, partial
/// days, sick-note attachments, HR review context and the calendar-sync seam on approval.
/// </summary>
public class LeaveWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingCalendarSync : ICalendarSyncService
    {
        public List<CalendarSyncEvent> Events { get; } = [];
        public List<(string EventType, Guid EntityId)> Cancellations { get; } = [];

        public Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken)
        {
            Events.Add(syncEvent);
            return Task.CompletedTask;
        }

        public Task CancelAsync(string eventType, Guid entityId, CancellationToken cancellationToken)
        {
            Cancellations.Add((eventType, entityId));
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(
        SqliteTestDbContext Db, AbsenceService Sut, RecordingCalendarSync CalendarSync, string StorageRoot,
        Guid TenantId, Guid EmployeeId, Guid ColleagueId, Guid EmployeeUserId, Guid DriverId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "TR", Name = "Transport", IsActive = true });
        db.Context.Employees.AddRange(
            new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
                FirstName = "Jan", LastName = "Jansen", DepartmentId = departmentId,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new Employee
            {
                Id = colleagueId, TenantId = tenantId, EmployeeNumber = "MED-2",
                FirstName = "Piet", LastName = "Peters", DepartmentId = departmentId,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Users.Add(new User
        {
            Id = employeeUserId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        await db.Context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-leave-tests", Guid.NewGuid().ToString("N"));
        var calendarSync = new RecordingCalendarSync();
        return new Harness(db, CreateSut(db, tenantId, calendarSync, storageRoot), calendarSync, storageRoot,
            tenantId, employeeId, colleagueId, employeeUserId, driverId);
    }

    /// <summary>HR-style caller: holds every permission, including absences.view_medical.</summary>
    private sealed class AllowAllPermissions : Api.Modules.Identity.Services.IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private static AbsenceService CreateSut(
        SqliteTestDbContext db, Guid tenantId, ICalendarSyncService calendarSync, string storageRoot)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        return new AbsenceService(
            db.Context, tenant, user,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, new TestClock(Now)),
            new LocalFileStorageService(storageRoot),
            calendarSync,
            new TestClock(Now),
            authorization: new AllowAllPermissions());
    }

    private static CreateAbsenceRequest Request(
        DateOnly? start = null, DateOnly? end = null, AbsenceType type = AbsenceType.Vacation,
        AbsencePartDay partDay = AbsencePartDay.FullDay) =>
        new(type, start ?? new(2026, 8, 3), end ?? new(2026, 8, 7), "Vakantie", partDay);

    [Fact]
    public async Task PartialDay_OnlyForSingleDayRequests()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var invalid = await h.Sut.CreateForEmployeeAsync(h.EmployeeId,
            Request(new(2026, 8, 3), new(2026, 8, 4), partDay: AbsencePartDay.Morning), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.ValidationFailed, invalid.Outcome);

        var valid = await h.Sut.CreateForEmployeeAsync(h.EmployeeId,
            Request(new(2026, 8, 3), new(2026, 8, 3), partDay: AbsencePartDay.Afternoon), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, valid.Outcome);
        Assert.Equal(AbsencePartDay.Afternoon, valid.Absence!.PartDay);
    }

    [Fact]
    public async Task ReviewFlow_UnderReview_Then_Decide()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var absence = (await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None)).Absence!;

        var review = await h.Sut.StartReviewAsync(absence.Id, CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, review.Outcome);
        Assert.Equal(AbsenceStatus.UnderReview, review.Absence!.Status);

        // Double review start is refused; deciding from UnderReview works.
        Assert.Equal(AbsenceOperationOutcome.InvalidState,
            (await h.Sut.StartReviewAsync(absence.Id, CancellationToken.None)).Outcome);

        var approved = await h.Sut.DecideAsync(absence.Id, new DecideAbsenceRequest(true, "Prettige vakantie"), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, approved.Outcome);
        Assert.Equal(AbsenceStatus.Approved, approved.Absence!.Status);

        // Approval queues a calendar-sync event through the integration seam.
        var syncEvent = Assert.Single(h.CalendarSync.Events);
        Assert.Equal("leave_approved", syncEvent.EventType);
        Assert.Equal(absence.Id, syncEvent.EntityId);
    }

    [Fact]
    public async Task RequestChanges_ReturnsToRequested_WithEmployeeNotification()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var absence = (await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None)).Absence!;
        await h.Sut.StartReviewAsync(absence.Id, CancellationToken.None);

        var noNote = await h.Sut.RequestChangesAsync(absence.Id,
            new RequestAbsenceChangesRequest(" ", null, null), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.ValidationFailed, noNote.Outcome);

        var changes = await h.Sut.RequestChangesAsync(absence.Id,
            new RequestAbsenceChangesRequest("Die week zit vol", new(2026, 8, 10), new(2026, 8, 14)), CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.Success, changes.Outcome);
        Assert.Equal(AbsenceStatus.Requested, changes.Absence!.Status);
        Assert.Contains("Die week zit vol", changes.Absence.DecisionNote);
        Assert.Contains("10-08-2026", changes.Absence.DecisionNote);

        Assert.Contains(h.Db.Context.Notifications,
            n => n.UserId == h.EmployeeUserId && n.Type == "absence_changes_requested");
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "Absence" && a.Action == "ChangesRequested");
    }

    [Fact]
    public async Task InternalNote_IsStored_AndAudited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var absence = (await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None)).Absence!;

        var result = await h.Sut.SetInternalNoteAsync(absence.Id, "Saldo bijna op — checken met payroll", CancellationToken.None);

        Assert.Equal(AbsenceOperationOutcome.Success, result.Outcome);
        Assert.Equal("Saldo bijna op — checken met payroll", result.Absence!.InternalNote);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "Absence" && a.Action == "InternalNoteUpdated");
    }

    [Fact]
    public async Task Attachment_RoundTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var absence = (await h.Sut.CreateForEmployeeAsync(h.EmployeeId,
            Request(new(2026, 8, 3), new(2026, 8, 3), AbsenceType.Sick), CancellationToken.None)).Absence!;

        try
        {
            // Content must carry the real PDF signature since the magic-byte gate (H6).
            using var upload = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 ziektebriefje"));
            var attached = await h.Sut.AttachDocumentAsync(absence.Id, "attest.pdf", upload, CancellationToken.None);
            Assert.Equal(AbsenceOperationOutcome.Success, attached.Outcome);
            Assert.True(attached.Absence!.HasAttachment);
            Assert.Equal("attest.pdf", attached.Absence.AttachmentFileName);

            var open = await h.Sut.OpenDocumentAsync(absence.Id, CancellationToken.None);
            Assert.NotNull(open);
            Assert.False(open!.MedicalRestricted);
            using var reader = new StreamReader(open.Content!);
            Assert.Equal("%PDF-1.7 ziektebriefje", await reader.ReadToEndAsync());

            var exeRefused = await h.Sut.AttachDocumentAsync(absence.Id, "virus.exe", new MemoryStream([1]), CancellationToken.None);
            Assert.Equal(AbsenceOperationOutcome.ValidationFailed, exeRefused.Outcome);

            // A renamed non-PDF is refused on its bytes, not its name (H6).
            var fakePdf = await h.Sut.AttachDocumentAsync(
                absence.Id, "vermomd.pdf", new MemoryStream(Encoding.UTF8.GetBytes("MZ...")), CancellationToken.None);
            Assert.Equal(AbsenceOperationOutcome.ValidationFailed, fakePdf.Outcome);
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ReviewContext_ListsConflictsColleaguesAndBalance()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Earlier approved vacation this year: 2 days used.
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Type = AbsenceType.Vacation, StartDate = new(2026, 2, 2), EndDate = new(2026, 2, 3),
            Status = AbsenceStatus.Approved,
        });
        // Colleague in the same department overlaps the requested window.
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.ColleagueId,
            Type = AbsenceType.Vacation, StartDate = new(2026, 8, 5), EndDate = new(2026, 8, 10),
            Status = AbsenceStatus.Approved,
        });
        // A confirmed shift and a planned trip inside the window are planning conflicts.
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Date = new(2026, 8, 4), StartTime = new(8, 0), EndTime = new(16, 0),
            Status = ShiftStatus.Confirmed, Type = ShiftType.Work,
        });
        h.Db.Context.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0009",
            TripDate = new(2026, 8, 6), DriverId = h.DriverId, Status = TripStatus.Planned,
        });
        await h.Db.Context.SaveChangesAsync();

        var absence = (await h.Sut.CreateForEmployeeAsync(h.EmployeeId, Request(), CancellationToken.None)).Absence!;
        var context = await h.Sut.GetReviewContextAsync(absence.Id, CancellationToken.None);

        Assert.NotNull(context);
        Assert.Single(context!.OverlappingShifts);
        Assert.Equal(new DateOnly(2026, 8, 4), context.OverlappingShifts[0].Date);
        Assert.Single(context.OverlappingTrips);
        Assert.Equal("RIT-0009", context.OverlappingTrips[0].TripNumber);
        var colleague = Assert.Single(context.OverlappingColleagues);
        Assert.Equal("Piet Peters", colleague.EmployeeName);
        Assert.Equal(2, context.UsedVacationDaysThisYear);
    }
}
