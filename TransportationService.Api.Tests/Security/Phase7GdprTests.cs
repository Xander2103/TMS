using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Gdpr;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 7 (H13): the retention sweep actually deletes what the policy says (and freezes
/// completely under legal hold), the data-subject export is complete and read-audited, and
/// anonymisation erases identifying/special-category data while business structure survives.
/// </summary>
public class Phase7GdprTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 31, 12, 0, 0, TimeSpan.Zero);

    // ===================== retention sweep =====================

    private static OutboxMessage Mail(Guid tenantId, OutboxStatus status, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Channel = MessageChannel.Email,
        Kind = MessageKinds.PodAvailable, OwnerType = MessageOwnerType.Customer, OwnerId = Guid.NewGuid(),
        RecipientAddress = "iemand@haven.be", Body = "inhoud", Status = status,
        IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAt = createdAt, UpdatedAt = createdAt,
    };

    [Fact]
    public async Task Sweep_DeletesOldDeliveredMail_KeepsRecentAndPending()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var old = Now.UtcDateTime.AddDays(-400);
        var oldSent = Mail(tenantId, OutboxStatus.Sent, old);
        var oldFailed = Mail(tenantId, OutboxStatus.Failed, old);
        var oldPending = Mail(tenantId, OutboxStatus.Pending, old);
        db.Context.OutboxMessages.AddRange(
            oldSent, oldFailed, oldPending, Mail(tenantId, OutboxStatus.Sent, Now.UtcDateTime.AddDays(-10)));
        await db.Context.SaveChangesAsync();

        // The audit interceptor stamps CreatedAt on insert — backdate the "old" rows afterwards.
        Guid[] oldIds = [oldSent.Id, oldFailed.Id, oldPending.Id];
        await db.Context.OutboxMessages.IgnoreQueryFilters()
            .Where(m => oldIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, old));

        var result = await GdprRetentionService.SweepAsync(
            db.Context, new TestClock(Now), new RetentionOptions(), sinkDirectory: null, CancellationToken.None);

        Assert.Equal(2, result.OutboxRowsDeleted);
        var remaining = await db.Context.OutboxMessages.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, remaining.Count);
        // A pending mail is never purged, however old: delivery is still owed.
        Assert.Contains(remaining, m => m.Status == OutboxStatus.Pending);
    }

    [Fact]
    public async Task Sweep_DeletesConsumedSecurityTokens_KeepsOpenOnes()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "t@acme.be", PasswordHash = "x",
            FirstName = "T", LastName = "T", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        var old = Now.UtcDateTime.AddDays(-60);
        db.Context.UserSecurityTokens.AddRange(
            new UserSecurityToken
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Kind = UserSecurityTokenKind.PasswordReset,
                TokenHash = "a", CreatedAt = old, ExpiresAt = old.AddHours(2), UsedAt = old.AddHours(1),
            },
            new UserSecurityToken
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Kind = UserSecurityTokenKind.Activation,
                TokenHash = "b", CreatedAt = Now.UtcDateTime.AddHours(-1), ExpiresAt = Now.UtcDateTime.AddHours(71),
            });
        await db.Context.SaveChangesAsync();

        var result = await GdprRetentionService.SweepAsync(
            db.Context, new TestClock(Now), new RetentionOptions(), sinkDirectory: null, CancellationToken.None);

        Assert.Equal(1, result.SecurityTokenRowsDeleted);
        Assert.Equal("b", (await db.Context.UserSecurityTokens.SingleAsync()).TokenHash);
    }

    [Fact]
    public async Task Sweep_UnderLegalHold_DeletesNothing()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.OutboxMessages.Add(Mail(tenantId, OutboxStatus.Sent, Now.UtcDateTime.AddDays(-400)));
        await db.Context.SaveChangesAsync();

        var result = await GdprRetentionService.SweepAsync(
            db.Context, new TestClock(Now), new RetentionOptions { LegalHold = true }, null, CancellationToken.None);

        Assert.Equal(RetentionSweepResult.Skipped, result);
        Assert.Equal(1, await db.Context.OutboxMessages.IgnoreQueryFilters().CountAsync());
    }

    // ===================== data-subject export & anonymisation =====================

    private sealed record Harness(SqliteTestDbContext Db, string StorageRoot, Guid TenantId, Guid EmployeeId, Guid UserId)
        : IDisposable
    {
        public DataSubjectService Service()
        {
            var tenant = new DevTenantContext(TenantId);
            var currentUser = new Modules.Identity.Services.DevCurrentUserContext(Guid.NewGuid());
            return new DataSubjectService(
                Db.Context, tenant,
                new AuditService(Db.Context, tenant, currentUser),
                new LocalFileStorageService(StorageRoot),
                new TestClock(Now));
        }

        public void Dispose()
        {
            try { Directory.Delete(StorageRoot, recursive: true); } catch { /* best effort */ }
            Db.Dispose();
        }
    }

    private static async Task<Harness> SeedAsync(bool active = false)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-phase7-tests", Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorageService(storageRoot);

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "P-7",
            FirstName = "Jan", LastName = "Jansen", DateOfBirth = new DateOnly(1986, 1, 1),
            NationalRegisterNumber = "86.01.01-123.45", Iban = "BE68539007547034", Bic = "BBRUBEBB",
            Email = "jan@prive.be", PhoneNumber = "0470 12 34 56", Street = "Kerkstraat", HouseNumber = "1",
            PostalCode = "9000", City = "Gent", Notes = "Allergie voor latex",
            IsActive = active, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });

        var attachmentKey = await storage.SaveAsync(
            tenantId, "absence-attachments", "attest.pdf",
            new MemoryStream("%PDF-1.7 attest"u8.ToArray()), CancellationToken.None);
        db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            Type = AbsenceType.Sick, StartDate = new(2026, 6, 1), EndDate = new(2026, 6, 5),
            Status = AbsenceStatus.Approved, Reason = "burn-out", InternalNote = "arts gebeld",
            AttachmentPath = attachmentKey, AttachmentFileName = "attest.pdf",
        });

        var documentKey = await storage.SaveAsync(
            tenantId, "employee-documents", "id-kaart.pdf",
            new MemoryStream("%PDF-1.7 id"u8.ToArray()), CancellationToken.None);
        db.Context.EmployeeDocuments.Add(new EmployeeDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            Category = EmployeeDocumentCategory.IdentityCardFront, FileName = "id-kaart.pdf",
            ContentType = "application/pdf", SizeBytes = 11, StorageKey = documentKey,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });

        await db.Context.SaveChangesAsync();
        return new Harness(db, storageRoot, tenantId, employeeId, userId);
    }

    [Fact]
    public async Task Export_ContainsTheFullDossier_AndLeavesADataExportedTrace()
    {
        using var h = await SeedAsync();

        var export = await h.Service().ExportAsync(h.EmployeeId, CancellationToken.None);

        Assert.NotNull(export);
        var json = System.Text.Json.JsonSerializer.Serialize(export);
        Assert.Contains("86.01.01-123.45", json);
        Assert.Contains("burn-out", json);
        Assert.Contains("id-kaart.pdf", json);

        var trace = Assert.Single(await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(l => l.Action == SecurityAuditEvents.DataExported).ToListAsync());
        Assert.Contains(SecurityAuditEvents.Classification.Health, trace.NewValuesJson);
    }

    [Fact]
    public async Task Anonymize_RefusesAnActiveEmployee()
    {
        using var h = await SeedAsync(active: true);
        var error = await h.Service().AnonymizeAsync(h.EmployeeId, CancellationToken.None);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Anonymize_ErasesPersonalData_KeepsBusinessStructure_AndKillsTheAccount()
    {
        using var h = await SeedAsync();

        Assert.Null(await h.Service().AnonymizeAsync(h.EmployeeId, CancellationToken.None));

        var employee = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == h.EmployeeId);
        Assert.Null(employee.NationalRegisterNumber);
        Assert.Null(employee.Iban);
        Assert.Null(employee.Notes);
        Assert.Equal(string.Empty, employee.Email);
        Assert.Equal("Geanonimiseerd", employee.FirstName);
        Assert.Equal(new DateOnly(1900, 1, 1), employee.DateOfBirth);
        // Business identity survives for referential integrity and statutory records.
        Assert.Equal("P-7", employee.EmployeeNumber);

        var absence = await h.Db.Context.Absences.AsNoTracking().SingleAsync(a => a.EmployeeId == h.EmployeeId);
        Assert.Null(absence.Reason);
        Assert.Null(absence.InternalNote);
        Assert.Null(absence.AttachmentPath);

        Assert.Empty(await h.Db.Context.EmployeeDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.EmployeeId == h.EmployeeId).ToListAsync());
        // Uploaded files are physically gone.
        Assert.Empty(Directory.EnumerateFiles(h.StorageRoot, "*", SearchOption.AllDirectories));

        var user = await h.Db.Context.Users.AsNoTracking().SingleAsync(u => u.Id == h.UserId);
        Assert.False(user.IsActive);
        Assert.DoesNotContain("jan@acme.be", user.Email);
        Assert.Equal("Geanonimiseerd", user.FirstName);

        // The audit trail records the FACT, never the erased values.
        var trace = Assert.Single(await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(l => l.Action == "Anonymized").ToListAsync());
        Assert.Null(trace.OldValuesJson);
        Assert.DoesNotContain("Jansen", trace.NewValuesJson ?? string.Empty);
    }
}
