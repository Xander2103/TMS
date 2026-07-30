using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
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

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 6: audit &amp; forensics. M6 — every audit record carries client IP and correlation id;
/// M7 — health data on sick leave (reason, HR note, certificate) requires absences.view_medical,
/// with a self-exemption for the data subject; M14 — the dossier history and sensitive downloads
/// respect the same gates as the live screens, and reads of special-category/bulk data leave a
/// read-audit trace with a data classification.
/// </summary>
public class Phase6AuditForensicsTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 31, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionStub : IPermissionAuthorizationService
    {
        private readonly HashSet<string> _codes;

        public PermissionStub(params string[] codes) => _codes = new HashSet<string>(codes, StringComparer.Ordinal);

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
            => Task.FromResult(_codes.Contains(permissionCode));
    }

    private sealed class NoopCalendarSync : ICalendarSyncService
    {
        public Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelAsync(string eventType, Guid entityId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record Harness(
        SqliteTestDbContext Db, string StorageRoot,
        Guid TenantId, Guid EmployeeId, Guid EmployeeUserId, Guid HrUserId, Guid ViewerUserId)
    {
        public AbsenceService Absences(Guid actingUserId, params string[] permissions) =>
            AbsencesWith(actingUserId, new PermissionStub(permissions));

        public AbsenceService AbsencesWith(Guid actingUserId, IPermissionAuthorizationService? authorization)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(actingUserId);
            return new AbsenceService(
                Db.Context, tenant, user,
                new AuditService(Db.Context, tenant, user),
                new NotificationService(Db.Context, tenant, user, new TestClock(Now)),
                new LocalFileStorageService(StorageRoot),
                new NoopCalendarSync(),
                new TestClock(Now),
                authorization: authorization);
        }

        public EmployeeDocumentService Documents(Guid actingUserId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(actingUserId);
            return new EmployeeDocumentService(
                Db.Context, tenant, new AuditService(Db.Context, tenant, user), new LocalFileStorageService(StorageRoot));
        }

        public Task<List<Modules.Auditing.Entities.AuditLog>> AuditRowsAsync(string action) =>
            Db.Context.AuditLogs.AsNoTracking().Where(l => l.Action == action).ToListAsync();
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();
        var viewerUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "P-1",
            FirstName = "Jan", LastName = "Jansen",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Users.AddRange(
            new User
            {
                Id = employeeUserId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = "x",
                FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = hrUserId, TenantId = tenantId, Email = "hr@acme.be", PasswordHash = "x",
                FirstName = "Hilde", LastName = "Hr", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = viewerUserId, TenantId = tenantId, Email = "planner@acme.be", PasswordHash = "x",
                FirstName = "Piet", LastName = "Planner", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        await db.Context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-phase6-tests", Guid.NewGuid().ToString("N"));
        return new Harness(db, storageRoot, tenantId, employeeId, employeeUserId, hrUserId, viewerUserId);
    }

    private static async Task<AbsenceDto> CreateSickAbsenceAsync(Harness h, string reason = "griep", string? internalNote = "arts gebeld")
    {
        var hr = h.Absences(h.HrUserId, PermissionCodes.AbsencesViewMedical);
        var created = await hr.CreateForEmployeeAsync(h.EmployeeId,
            new CreateAbsenceRequest(AbsenceType.Sick, new(2026, 8, 3), new(2026, 8, 5), reason), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, created.Outcome);

        if (internalNote is not null)
        {
            var noted = await hr.SetInternalNoteAsync(created.Absence!.Id, internalNote, CancellationToken.None);
            Assert.Equal(AbsenceOperationOutcome.Success, noted.Outcome);
        }

        return created.Absence!;
    }

    // ===================== M6 — forensic fields on every audit record =====================

    [Fact]
    public async Task AuditRecord_CarriesClientIpAndCorrelationId_WhenInsideARequest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var httpContext = new DefaultHttpContext { TraceIdentifier = "corr-123" };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(h.HrUserId), accessor);
        await audit.RecordAsync("Thing", "1", "Did", null, new { X = 1 }, CancellationToken.None);

        var row = Assert.Single(await h.AuditRowsAsync("Did"));
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("corr-123", row.CorrelationId);
    }

    [Fact]
    public async Task AuditRecord_StillWrites_WithoutAnHttpContext()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(h.HrUserId));
        await audit.RecordAsync("Thing", "1", "Background", null, null, CancellationToken.None);

        var row = Assert.Single(await h.AuditRowsAsync("Background"));
        Assert.Null(row.IpAddress);
        Assert.Null(row.CorrelationId);
    }

    // ===================== role catalogue / template guards =====================

    [Fact]
    public void RoleUpgrades_CurrentVersion_CoversEveryStep()
    {
        // A step above CurrentVersion silently never runs — exactly the half-applied state this
        // sprint recovered from. The constant must always keep up with the step list.
        var versions = DefaultRoleUpgrades.Steps.Select(s => s.Version).ToList();
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, versions.Max());
    }

    [Fact]
    public void MedicalPermission_IsInCatalogue_AndGrantedToHrTemplate()
    {
        Assert.Contains(PermissionCodes.All, p => p.Code == PermissionCodes.AbsencesViewMedical);

        var step = DefaultRoleUpgrades.Steps.Single(s => s.Version == 22);
        Assert.Contains(PermissionCodes.AbsencesViewMedical, step.GrantsByTemplateCode["hr"]);
    }

    // ===================== M7 — health data behind absences.view_medical =====================

    [Fact]
    public async Task SickAbsence_HidesReasonNoteAndCertificate_FromCallersWithoutTheMedicalPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateSickAbsenceAsync(h);

        var planner = h.Absences(h.ViewerUserId /* absences.view only, no medical */);
        var listed = Assert.Single((await planner.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);

        // Planning still sees WHO is absent and WHEN — only the health fields disappear.
        Assert.Equal(AbsenceType.Sick, listed.Type);
        Assert.Null(listed.Reason);
        Assert.Null(listed.InternalNote);
        Assert.False(listed.HasAttachment);
        Assert.Null(listed.AttachmentFileName);
    }

    [Fact]
    public async Task SickAbsence_ShowsHealthFields_ToHolderOfTheMedicalPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateSickAbsenceAsync(h);

        var hr = h.Absences(h.HrUserId, PermissionCodes.AbsencesViewMedical);
        var listed = Assert.Single((await hr.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);

        Assert.Equal("griep", listed.Reason);
        Assert.Equal("arts gebeld", listed.InternalNote);
    }

    [Fact]
    public async Task SickAbsence_StaysVisibleToTheDataSubjectThemselves()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateSickAbsenceAsync(h);

        // The employee holds no medical permission, but it is their own request.
        var self = h.Absences(h.EmployeeUserId);
        var listed = Assert.Single((await self.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);
        Assert.Equal("griep", listed.Reason);
    }

    [Fact]
    public async Task NonSickAbsence_KeepsItsReason_ForEveryone()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var hr = h.Absences(h.HrUserId, PermissionCodes.AbsencesViewMedical);
        var created = await hr.CreateForEmployeeAsync(h.EmployeeId,
            new CreateAbsenceRequest(AbsenceType.Vacation, new(2026, 9, 7), new(2026, 9, 11), "Trouwfeest"), CancellationToken.None);
        Assert.Equal(AbsenceOperationOutcome.Success, created.Outcome);

        var planner = h.Absences(h.ViewerUserId);
        var listed = Assert.Single((await planner.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);
        Assert.Equal("Trouwfeest", listed.Reason);
    }

    [Fact]
    public async Task SickCertificate_DownloadIsRefusedWithoutTheMedicalPermission_AndAuditedWithIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var absence = await CreateSickAbsenceAsync(h);

        try
        {
            var hr = h.Absences(h.HrUserId, PermissionCodes.AbsencesViewMedical);
            using var upload = new MemoryStream(Encoding.UTF8.GetBytes("attest"));
            var attached = await hr.AttachDocumentAsync(absence.Id, "attest.pdf", upload, CancellationToken.None);
            Assert.Equal(AbsenceOperationOutcome.Success, attached.Outcome);

            // Without the permission (and not the subject): the file exists but stays closed — 403, not 404.
            var planner = h.Absences(h.ViewerUserId);
            var refused = await planner.OpenDocumentAsync(absence.Id, CancellationToken.None);
            Assert.NotNull(refused);
            Assert.True(refused!.MedicalRestricted);
            Assert.Null(refused.Content);

            // With the permission: opens, and the read leaves a HealthDataViewed trace.
            var opened = await hr.OpenDocumentAsync(absence.Id, CancellationToken.None);
            Assert.NotNull(opened);
            Assert.False(opened!.MedicalRestricted);
            using var reader = new StreamReader(opened.Content!);
            Assert.Equal("attest", await reader.ReadToEndAsync());

            var trace = Assert.Single(await h.AuditRowsAsync(SecurityAuditEvents.HealthDataViewed));
            Assert.Equal(absence.Id.ToString(), trace.EntityId);
            Assert.Contains(SecurityAuditEvents.Classification.Health, trace.NewValuesJson);

            // The data subject reading their own certificate is not a foreign read: no extra trace.
            var self = h.Absences(h.EmployeeUserId);
            var own = await self.OpenDocumentAsync(absence.Id, CancellationToken.None);
            Assert.False(own!.MedicalRestricted);
            own.Content!.Dispose();
            Assert.Single(await h.AuditRowsAsync(SecurityAuditEvents.HealthDataViewed));
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task AbsenceService_FailsClosed_WhenNoAuthorizationServiceIsWired()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await CreateSickAbsenceAsync(h);

        var unwired = h.AbsencesWith(h.ViewerUserId, authorization: null);
        var listed = Assert.Single((await unwired.ListForEmployeeAsync(h.EmployeeId, CancellationToken.None))!);
        Assert.Null(listed.Reason);
        Assert.Null(listed.InternalNote);
    }

    // ===================== M14 — sensitive document downloads are gated and read-audited =====================

    [Fact]
    public async Task SensitiveEmployeeDocument_DownloadIsRefusedWithoutPermission_AndAuditedWithIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        try
        {
            var documents = h.Documents(h.HrUserId);
            using var upload = new MemoryStream(Encoding.UTF8.GetBytes("medisch dossier"));
            var uploaded = await documents.UploadAsync(h.EmployeeId,
                new SaveEmployeeDocumentMetadata(EmployeeDocumentCategory.MedicalDocument, null, null, null),
                "dossier.pdf", "application/pdf", 15, upload, CancellationToken.None);
            Assert.NotNull(uploaded);

            var refused = await documents.OpenAsync(h.EmployeeId, uploaded!.Id, includeSensitive: false, CancellationToken.None);
            Assert.NotNull(refused);
            Assert.True(refused!.SensitiveRestricted);
            Assert.Null(refused.Content);

            var opened = await documents.OpenAsync(h.EmployeeId, uploaded.Id, includeSensitive: true, CancellationToken.None);
            Assert.NotNull(opened);
            Assert.False(opened!.SensitiveRestricted);
            opened.Content!.Dispose();

            var trace = Assert.Single(await h.AuditRowsAsync(SecurityAuditEvents.SensitiveDocumentDownloaded));
            Assert.Equal(uploaded.Id.ToString(), trace.EntityId);
            Assert.Contains(SecurityAuditEvents.Classification.Health, trace.NewValuesJson);
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ExportAudit_RecordsWhoExportedWhatWithWhichFilter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(h.HrUserId));
        await audit.RecordExportAsync("kpi:trips", new { from = "2026-01-01", to = "2026-01-31" }, CancellationToken.None);

        var row = Assert.Single(await h.AuditRowsAsync(SecurityAuditEvents.DataExported));
        Assert.Equal(SecurityAuditEvents.EntityType, row.EntityType);
        Assert.Equal(h.HrUserId, row.UserId);
        Assert.Contains("kpi:trips", row.NewValuesJson);
        Assert.Contains(SecurityAuditEvents.Classification.Personal, row.NewValuesJson);
    }

    // ===================== M14 — dossier history mirrors the live gates =====================

    [Fact]
    public async Task History_HidesConfidentialAndMedicalDiffs_WithoutTheMatchingPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var sickAbsence = await CreateSickAbsenceAsync(h, internalNote: null);

        // A profile edit that touched the national register number, and a sick-leave update that
        // changed the (medical) reason — both replayed straight from the audit trail.
        h.Db.Context.AuditLogs.AddRange(
            new Modules.Auditing.Entities.AuditLog
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.HrUserId,
                EntityType = "Employee", EntityId = h.EmployeeId.ToString(), Action = "Updated",
                OldValuesJson = """{"NationalRegisterNumber":"86.01.01-123.45","City":"Gent"}""",
                NewValuesJson = """{"NationalRegisterNumber":"86.01.01-999.99","City":"Brugge"}""",
                Timestamp = Now.UtcDateTime,
            },
            new Modules.Auditing.Entities.AuditLog
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.HrUserId,
                EntityType = "Absence", EntityId = sickAbsence.Id.ToString(), Action = "Updated",
                OldValuesJson = """{"Reason":"griep"}""",
                NewValuesJson = """{"Reason":"burn-out"}""",
                Timestamp = Now.UtcDateTime,
            });
        await h.Db.Context.SaveChangesAsync();

        var history = new EmployeeHistoryService(h.Db.Context, new DevTenantContext(h.TenantId));

        var full = await history.GetHistoryAsync(
            h.EmployeeId, 1, 50, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        Assert.Contains(full!.Items, e => e.Changes.Any(c => c.Field == "Rijksregisternummer"));
        Assert.Contains(full.Items, e => e.Changes.Any(c => c.After == "burn-out"));

        var restricted = await history.GetHistoryAsync(
            h.EmployeeId, 1, 50, null,
            new EmployeeHistoryAccess(ConfidentialFields: false, MedicalData: false, SensitiveDocuments: true),
            CancellationToken.None);
        Assert.DoesNotContain(restricted!.Items, e => e.Changes.Any(c => c.Field == "Rijksregisternummer"));
        Assert.DoesNotContain(restricted.Items, e => e.Changes.Any(c => c.After == "burn-out"));

        // The non-confidential part of the same profile edit stays visible.
        Assert.Contains(restricted.Items, e => e.Changes.Any(c => c.Field == "Gemeente" && c.After == "Brugge"));
    }

    [Fact]
    public async Task History_HidesSensitiveDocumentEntries_WithoutTheSensitivePermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        try
        {
            var documents = h.Documents(h.HrUserId);
            using var upload = new MemoryStream(Encoding.UTF8.GetBytes("contract"));
            var uploaded = await documents.UploadAsync(h.EmployeeId,
                new SaveEmployeeDocumentMetadata(EmployeeDocumentCategory.Contract, null, null, null),
                "contract.pdf", "application/pdf", 8, upload, CancellationToken.None);
            Assert.NotNull(uploaded);

            var history = new EmployeeHistoryService(h.Db.Context, new DevTenantContext(h.TenantId));

            var full = await history.GetHistoryAsync(
                h.EmployeeId, 1, 50, null, EmployeeHistoryAccess.Full, CancellationToken.None);
            Assert.Contains(full!.Items, e => e.Category == "Documenten");

            var restricted = await history.GetHistoryAsync(
                h.EmployeeId, 1, 50, null,
                new EmployeeHistoryAccess(ConfidentialFields: true, MedicalData: true, SensitiveDocuments: false),
                CancellationToken.None);
            Assert.DoesNotContain(restricted!.Items, e => e.Category == "Documenten");
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
