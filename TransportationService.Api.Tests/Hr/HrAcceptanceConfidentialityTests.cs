using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Controllers;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Hr;

/// <summary>
/// HR acceptance 2026-10-16, B2: planning roles (employees.view + absences.view, no HR rights)
/// receive operational employee history and absence overviews, but never HR note text, the
/// personal absence text (request reason, rejection/decision note, internal note) or leave
/// balance entries — asserted on the serialized API response, not just on the UI.
/// </summary>
public class HrAcceptanceConfidentialityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private const string NoteText = "TEST HR vertrouwelijke HR-notitie";
    private const string RequestReason = "Huwelijk van mijn zus";
    private const string RejectionNote = "Drukke periode, privé-afspraak met teamleider";

    private static readonly string[] PlannerCodes =
    [
        PermissionCodes.EmployeesView, PermissionCodes.AbsencesView, PermissionCodes.EmployeeDocumentsView,
    ];

    private static readonly string[] HrCodes =
    [
        PermissionCodes.EmployeesView, PermissionCodes.AbsencesView, PermissionCodes.AbsencesApprove,
        PermissionCodes.AbsencesViewMedical, PermissionCodes.EmployeeNotesView, PermissionCodes.LeaveBalancesView,
        PermissionCodes.EmployeeDocumentsView, PermissionCodes.EmployeeDocumentsViewSensitive,
    ];

    private sealed class PermissionStub(params string[] codes) : IPermissionAuthorizationService
    {
        private readonly HashSet<string> _codes = new(codes, StringComparer.Ordinal);

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
            => Task.FromResult(_codes.Contains(permissionCode));
    }

    private sealed class NoopCalendarSync : ICalendarSyncService
    {
        public Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelAsync(string eventType, Guid entityId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid EmployeeUserId, Guid HrUserId, Guid PlannerUserId)
    {
        public AbsenceService Absences(Guid actingUserId, params string[] codes)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(actingUserId);
            return new AbsenceService(
                Db.Context, tenant, user,
                new AuditService(Db.Context, tenant, user),
                new NotificationService(Db.Context, tenant, user, new TestClock(Now)),
                new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-hr-acc", Guid.NewGuid().ToString("N"))),
                new NoopCalendarSync(),
                new TestClock(Now),
                authorization: new PermissionStub(codes));
        }

        public EmployeesController Controller(Guid actingUserId, params string[] codes)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(actingUserId);
            var audit = new AuditService(Db.Context, tenant, user);
            var drivers = new Modules.Drivers.Services.DriverService(Db.Context, tenant, audit,
                new QualificationStatusCalculator(), TimeProvider.System);
            var qualifications = new QualificationService(
                Db.Context, tenant, new QualificationStatusCalculator(), TimeProvider.System, audit,
                new CountryCodeValidator(Db.Context),
                new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-hr-acc", Guid.NewGuid().ToString("N"))));
            var employees = new EmployeeService(Db.Context, tenant, audit, new CountryCodeValidator(Db.Context),
                drivers, qualifications, new EmployeeCompletenessService(Db.Context, tenant));
            return new EmployeesController(employees, new EmployeeHistoryService(Db.Context, tenant), user, new PermissionStub(codes));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();
        var plannerUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen",
            Notes = "Legacy HR-notitie: loonbeslag", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        foreach (var (id, email, employee) in new[]
                 {
                     (employeeUserId, "jan@acme.be", (Guid?)employeeId),
                     (hrUserId, "hr@acme.be", null),
                     (plannerUserId, "planner@acme.be", null),
                 })
        {
            db.Context.Users.Add(new User
            {
                Id = id, TenantId = tenantId, Email = email, PasswordHash = "x", FirstName = "X", LastName = "Y",
                EmployeeId = employee, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        }

        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        return new Harness(db, tenantId, employeeId, employeeUserId, hrUserId, plannerUserId);
    }

    /// <summary>A vacation request with a personal reason, rejected by HR with a note, plus an HR note.</summary>
    private static async Task<AbsenceDto> SeedHrTextAsync(Harness h)
    {
        var hr = h.Absences(h.HrUserId, HrCodes);
        var created = await hr.CreateForEmployeeAsync(h.EmployeeId,
            new CreateAbsenceRequest(AbsenceType.Vacation, new(2026, 11, 2), new(2026, 11, 3), RequestReason), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, created.Outcome);
        var decided = await hr.DecideAsync(created.Absence!.Id, new DecideAbsenceRequest(false, RejectionNote), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, decided.Outcome);
        Assert.Equal(AbsenceOperationOutcome.Success,
            (await hr.SetInternalNoteAsync(created.Absence.Id, "intern: opvolgen", CancellationToken.None)).Outcome);

        var tenant = new DevTenantContext(h.TenantId);
        var user = new DevCurrentUserContext(h.HrUserId);
        var notes = new EmployeeNoteService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, user), user, TimeProvider.System);
        Assert.NotNull(await notes.CreateAsync(h.EmployeeId, NoteText, CancellationToken.None));
        return created.Absence;
    }

    // Relaxed escaping: the assertions compare raw (accented) text, which the default encoder would \u-escape.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    private static object? Body<T>(ActionResult<T> result) => result.Result is ObjectResult o ? o.Value : result.Value;

    [Fact]
    public async Task Planner_EmployeeHistoryResponse_ContainsNoHrNoteNoAbsenceTextAndNoLeaveBalance()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHrTextAsync(h);

        var planner = h.Controller(h.PlannerUserId, PlannerCodes);
        var response = Json(Body(await planner.GetHistory(h.EmployeeId, 1, 100, null, CancellationToken.None)));

        Assert.DoesNotContain(NoteText, response);
        Assert.DoesNotContain(RequestReason, response);
        Assert.DoesNotContain(RejectionNote, response);
        Assert.DoesNotContain("intern: opvolgen", response);
        Assert.DoesNotContain("Notities", response);

        // Still useful: the absence itself (who, status change) remains in the operational history.
        var page = (EmployeeHistoryPageDto)Body(await planner.GetHistory(h.EmployeeId, 1, 100, null, CancellationToken.None))!;
        Assert.Contains(page.Items, e => e.Category == "Afwezigheden");
    }

    [Fact]
    public async Task Hr_EmployeeHistoryResponse_KeepsTheFullHrText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHrTextAsync(h);

        var hr = h.Controller(h.HrUserId, HrCodes);
        var response = Json(Body(await hr.GetHistory(h.EmployeeId, 1, 100, null, CancellationToken.None)));

        Assert.Contains(NoteText, response);
        Assert.Contains(RejectionNote, response);
    }

    [Fact]
    public async Task Planner_EmployeeDetail_DoesNotCarryTheLegacyHrNote()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var planner = Body(await h.Controller(h.PlannerUserId, PlannerCodes).GetById(h.EmployeeId, CancellationToken.None));
        Assert.Null(((EmployeeDetailDto)planner!).Notes);

        var hr = Body(await h.Controller(h.HrUserId, HrCodes).GetById(h.EmployeeId, CancellationToken.None));
        Assert.Equal("Legacy HR-notitie: loonbeslag", ((EmployeeDetailDto)hr!).Notes);
    }

    [Fact]
    public async Task Planner_AbsenceListAndSearch_CarryNoReasonRejectionOrInternalNote()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHrTextAsync(h);

        var planner = h.Absences(h.PlannerUserId, PlannerCodes);
        var list = await planner.ListAsync(new(2026, 10, 1), new(2026, 12, 31), null, null, CancellationToken.None);
        var search = await planner.SearchAsync(new(2026, 10, 1), new(2026, 12, 31), null, null, null, null, null,
            Api.Common.Models.PageRequest.Of(1, 50), CancellationToken.None);
        var perEmployee = await planner.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        foreach (var payload in new[] { Json(list), Json(search), Json(perEmployee) })
        {
            Assert.DoesNotContain(RequestReason, payload);
            Assert.DoesNotContain(RejectionNote, payload);
            Assert.DoesNotContain("intern: opvolgen", payload);
        }

        // Operational facts stay: who, when, type and status.
        var row = Assert.Single(list);
        Assert.Equal(AbsenceStatus.Rejected, row.Status);
        Assert.Equal(AbsenceType.Vacation, row.Type);
    }

    [Fact]
    public async Task DataSubject_SeesOwnReasonAndDecision_ButNotTheInternalNote()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedHrTextAsync(h);

        var self = Assert.Single((await h.Absences(h.EmployeeUserId).ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);
        Assert.Equal(RequestReason, self.Reason);
        Assert.Equal(RejectionNote, self.DecisionNote);
        Assert.Null(self.InternalNote);

        var hr = Assert.Single((await h.Absences(h.HrUserId, HrCodes).ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);
        Assert.Equal("intern: opvolgen", hr.InternalNote);
    }
}
