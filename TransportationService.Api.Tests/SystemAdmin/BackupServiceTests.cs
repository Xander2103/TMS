using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Storage;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.SystemAdmin.Entities;
using TransportationService.Api.Modules.SystemAdmin.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.SystemAdmin;

/// <summary>
/// Settings/system wave: backup management is HIGH-RISK — these tests pin the guarantees:
/// server-generated ids/files, fail-closed delete/restore, typed confirmation, safety
/// backup before restore, newest-backup protection and retention that never touches
/// manual backups. The process seam keeps pg_dump/pg_restore out of the tests entirely.
/// Production readiness 2026-10-18: a backup is dump + storage archive + manifest, verified
/// before it counts (H4); restore is Development-only and download Administrator-only (H8).
/// </summary>
public class BackupServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 08, 13, 10, 0, 0, TimeSpan.Zero);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"tms-backup-tests-{Guid.NewGuid():N}");
    private readonly string _storageDir = Path.Combine(Path.GetTempPath(), $"tms-backup-storage-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_storageDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeRunner : IBackupProcessRunner
    {
        public int ExitCode { get; set; }
        public int ListExitCode { get; set; }
        public List<(string FileName, IReadOnlyList<string> Args)> Calls { get; } = [];
        public bool SawPassword { get; private set; }

        public Task<(int ExitCode, string StdErrTail)> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            Calls.Add((fileName, arguments));
            SawPassword = SawPassword || environment.ContainsKey("PGPASSWORD");
            if (arguments.Contains("--list"))
            {
                return Task.FromResult((ListExitCode, ListExitCode == 0 ? "" : "pg_restore: error: input file is not a valid archive"));
            }

            // pg_dump writes the file it was asked for; emulate that so sizes resolve.
            var fileFlag = arguments.ToList().IndexOf("-f");
            if (ExitCode == 0 && fileFlag >= 0)
            {
                File.WriteAllText(arguments[fileFlag + 1], "dummy-dump-content");
            }

            return Task.FromResult((ExitCode, ExitCode == 0 ? "" : "boom"));
        }
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, BackupService Sut, FakeRunner Runner, PermissionSet Permissions, TestClock Clock,
        Guid TenantId, Guid UserId);

    private Harness Build(string environment = "Production", bool createStorageRoot = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.SaveChanges();

        if (createStorageRoot)
        {
            // A small durable-storage tree: two tenant files plus what must NEVER be archived.
            Directory.CreateDirectory(Path.Combine(_storageDir, "tenant-a", "pod-signatures"));
            File.WriteAllText(Path.Combine(_storageDir, "tenant-a", "pod-signatures", "sig.png"), "png");
            Directory.CreateDirectory(Path.Combine(_storageDir, "tenant-b", "order-documents"));
            File.WriteAllText(Path.Combine(_storageDir, "tenant-b", "order-documents", "cmr.pdf"), "pdf");
            Directory.CreateDirectory(Path.Combine(_storageDir, "message-sink"));
            File.WriteAllText(Path.Combine(_storageDir, "message-sink", "token.txt"), "raw token");
            Directory.CreateDirectory(Path.Combine(_storageDir, "backups"));
            File.WriteAllText(Path.Combine(_storageDir, "backups", "old.dump"), "old");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=dbhost;Port=5433;Database=tms;Username=tms;Password=verysecret",
            })
            .Build();
        var runner = new FakeRunner();
        var permissions = new PermissionSet();
        var tenant = new DevTenantContext(tenantId);
        var userId = Guid.NewGuid();
        var user = new DevCurrentUserContext(userId);
        var clock = new TestClock(Now);
        var sut = new BackupService(
            db.Context, configuration, new FakeEnvironment { EnvironmentName = environment }, runner,
            new AuditService(db.Context, tenant, user), clock,
            Options.Create(new BackupOptions { Directory = _tempDir, AutomaticRetentionDays = 30 }),
            permissions, user,
            new StorageRoot(_storageDir, StorageRootSource.Configured));
        return new Harness(db, sut, runner, permissions, clock, tenantId, userId);
    }

    private static void MakeSystemAdministrator(Harness h, string roleName = "Administrator", bool systemRole = true)
    {
        var roleId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = h.UserId, TenantId = h.TenantId, Email = $"{Guid.NewGuid():N}@example.test",
            FirstName = "A", LastName = "Dmin", IsActive = true,
        });
        h.Db.Context.Roles.Add(new Role
        {
            Id = roleId, TenantId = h.TenantId, Name = roleName, IsSystemRole = systemRole, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.UserId, RoleId = roleId });
        h.Db.Context.SaveChanges();
    }

    private static List<string> ArchiveEntries(string archivePath)
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        return names;
    }

    [Fact]
    public async Task Create_GeneratesServerSideFileName_NeverExposesSecrets()
    {
        var h = Build();
        using var _ = h.Db;

        var backup = await h.Sut.CreateAsync("Manual", "test", CancellationToken.None);

        Assert.StartsWith("backup-tms-", backup.FileName);
        Assert.True(File.Exists(Path.Combine(_tempDir, backup.FileName)));
        Assert.True(backup.SizeBytes > 0);
        // The password only ever travels via the child-process environment…
        Assert.True(h.Runner.SawPassword);
        // …and never appears in metadata or the pg_dump argument list.
        Assert.DoesNotContain("verysecret", System.Text.Json.JsonSerializer.Serialize(backup));
        Assert.DoesNotContain(h.Runner.Calls[0].Args, a => a.Contains("verysecret"));
    }

    [Fact]
    public async Task Create_ProducesVerifiedSet_DumpArchiveAndSha256Manifest()
    {
        var h = Build();
        using var _ = h.Db;

        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        var archive = Path.Combine(_tempDir, BackupArtifacts.ArchiveFileName(backup.FileName));
        var manifest = Path.Combine(_tempDir, BackupArtifacts.ManifestFileName(backup.FileName));
        Assert.True(File.Exists(archive));
        Assert.True(File.Exists(manifest));

        // The archive holds the tenant files relative to the root — never the backups or the sink.
        var entries = ArchiveEntries(archive);
        Assert.Contains("tenant-a/pod-signatures/sig.png", entries);
        Assert.Contains("tenant-b/order-documents/cmr.pdf", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("message-sink", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("backups", StringComparison.Ordinal));

        // sha256sum format, matching the files on disk.
        var lines = await File.ReadAllLinesAsync(manifest);
        Assert.Equal(2, lines.Length);
        Assert.Equal($"{await BackupArtifacts.Sha256Async(Path.Combine(_tempDir, backup.FileName), default)}  {backup.FileName}", lines[0]);
        Assert.EndsWith($"  {BackupArtifacts.ArchiveFileName(backup.FileName)}", lines[1]);

        // The dump was verified with pg_restore --list before success was reported.
        Assert.Contains(h.Runner.Calls, c => c.FileName == "pg_restore" && c.Args.Contains("--list"));
        Assert.Equal("Completed", h.Db.Context.DatabaseBackups.Single().Status);
    }

    [Fact]
    public async Task Create_PgDumpFailure_RecordsFailedRow_WithReason()
    {
        var h = Build();
        using var _ = h.Db;
        h.Runner.ExitCode = 1;

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync("Automatic", null, CancellationToken.None));

        Assert.Contains("pg_dump", error.Message);
        var row = h.Db.Context.DatabaseBackups.Single();
        Assert.Equal("Failed", row.Status);
        Assert.Contains("pg_dump exitcode 1", row.Note);
        Assert.Empty(Directory.EnumerateFiles(_tempDir));
    }

    [Fact]
    public async Task Create_UnreadableDump_IsFailed_NeverCompleted()
    {
        var h = Build();
        using var _ = h.Db;
        h.Runner.ListExitCode = 1;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync("Manual", "voor upgrade", CancellationToken.None));

        var row = h.Db.Context.DatabaseBackups.Single();
        Assert.Equal("Failed", row.Status);
        Assert.Contains("pg_restore --list", row.Note);
        Assert.StartsWith("voor upgrade", row.Note);
        // The whole set is removed: no dump/archive/manifest that could be mistaken for a backup.
        Assert.Empty(Directory.EnumerateFiles(_tempDir));
    }

    [Fact]
    public async Task Create_FilePartFailure_IsFailed_EvenThoughTheDumpSucceeded()
    {
        var h = Build(createStorageRoot: false); // storage root missing → the archive part fails
        using var _ = h.Db;

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync("Automatic", null, CancellationToken.None));

        Assert.Contains("bestandsarchief", error.Message);
        Assert.Equal("Failed", h.Db.Context.DatabaseBackups.Single().Status);
        Assert.Empty(Directory.EnumerateFiles(_tempDir));
        // A failed run never counts as a fresh backup.
        var overview = await h.Sut.ListAsync(CancellationToken.None);
        Assert.Null(overview.LastSuccessfulBackupAtUtc);
        Assert.True(overview.BackupStale);
    }

    [Fact]
    public async Task Delete_IsFailClosed_AndProtectsTheNewestBackup()
    {
        var h = Build();
        using var _ = h.Db;
        var older = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(1));
        var newest = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        // No permission wired-in = no delete, ever (fail-closed on top of the controller gate).
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.DeleteAsync(older.Id, CancellationToken.None));

        h.Permissions.Codes.Add(PermissionCodes.BackupsDelete);
        // The newest completed backup is protected.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.DeleteAsync(newest.Id, CancellationToken.None));
        // An older one deletes fine: row gone, dump + archive + manifest gone.
        await h.Sut.DeleteAsync(older.Id, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_tempDir, older.FileName)));
        Assert.False(File.Exists(Path.Combine(_tempDir, BackupArtifacts.ArchiveFileName(older.FileName))));
        Assert.False(File.Exists(Path.Combine(_tempDir, BackupArtifacts.ManifestFileName(older.FileName))));
        Assert.Single(h.Db.Context.DatabaseBackups.ToList());
    }

    [Fact]
    public async Task Restore_RequiresTypedConfirmation_TakesSafetyBackupFirst_AndHealthChecks()
    {
        var h = Build(environment: "Development");
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.BackupsRestore);
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        // Wrong confirmation text → refused before anything happens.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.RestoreAsync(backup.Id, "verkeerde-naam", CancellationToken.None));

        var result = await h.Sut.RestoreAsync(backup.Id, backup.FileName, CancellationToken.None);

        // The safety backup exists and predates the pg_restore call.
        var rows = h.Db.Context.DatabaseBackups.OrderBy(b => b.CreatedAtUtc).ToList();
        Assert.Contains(rows, b => b.Source == "PreRestore" && b.Id == result.SafetyBackupId);
        Assert.Equal("Restored", rows.Single(b => b.Id == backup.Id).Status);
        // Call order: pg_dump (original) → pg_dump (safety) → pg_restore.
        Assert.EndsWith("pg_restore", h.Runner.Calls[^1].FileName.Replace(".exe", ""));
        Assert.Contains("--clean", h.Runner.Calls[^1].Args);
        Assert.Contains("--if-exists", h.Runner.Calls[^1].Args);
    }

    [Fact]
    public async Task Restore_WithoutPermission_IsRefused_EvenWithCorrectConfirmation()
    {
        var h = Build(environment: "Development");
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.BackupsCreate); // irrelevant right
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.RestoreAsync(backup.Id, backup.FileName, CancellationToken.None));
        Assert.DoesNotContain(h.Db.Context.DatabaseBackups.ToList(), b => b.Source == "PreRestore");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Restore_OutsideDevelopment_IsDisabled_AndPointsToTheRunbook(string environment)
    {
        var h = Build(environment);
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.BackupsRestore);
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);
        var callsBefore = h.Runner.Calls.Count;

        Assert.False(h.Sut.InAppRestoreEnabled);
        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.RestoreAsync(backup.Id, backup.FileName, CancellationToken.None));

        Assert.Contains("docs/operations/backup-restore.md", error.Message);
        Assert.Equal(callsBefore, h.Runner.Calls.Count); // no safety dump, no pg_restore
        Assert.False((await h.Sut.ListAsync(CancellationToken.None)).RestoreEnabled);
    }

    [Fact]
    public async Task Controller_Restore_OutsideDevelopment_Returns409()
    {
        var h = Build();
        using var _ = h.Db;
        var controller = new TransportationService.Api.Modules.SystemAdmin.Controllers.SystemAdminController(
            null!, h.Sut);

        var response = await controller.Restore(Guid.NewGuid(), new("x"), CancellationToken.None);

        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(response.Result);
        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task Retention_RemovesExpiredAutomatic_ButNeverManual_NorTheNewest()
    {
        var h = Build();
        using var _ = h.Db;
        // Seed rows directly: an expired automatic, an expired manual, and a fresh automatic.
        void Seed(string source, DateTime createdAt, string name)
        {
            File.WriteAllText(Path.Combine(_tempDir, name), "x");
            h.Db.Context.DatabaseBackups.Add(new DatabaseBackup
            {
                Id = Guid.NewGuid(), FileName = name, CreatedAtUtc = createdAt,
                SizeBytes = 1, Source = source, Status = "Completed",
            });
        }

        Directory.CreateDirectory(_tempDir);
        Seed("Automatic", Now.UtcDateTime.AddDays(-45), "old-auto.dump");
        File.WriteAllText(Path.Combine(_tempDir, "old-auto.files.tar.gz"), "x");
        Seed("Manual", Now.UtcDateTime.AddDays(-400), "old-manual.dump");
        Seed("Automatic", Now.UtcDateTime.AddDays(-1), "fresh-auto.dump");
        await h.Db.Context.SaveChangesAsync();

        var removed = await h.Sut.CleanupExpiredAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        var remaining = h.Db.Context.DatabaseBackups.Select(b => b.FileName).ToList();
        Assert.DoesNotContain("old-auto.dump", remaining);
        Assert.Contains("old-manual.dump", remaining);   // manual is never auto-deleted
        Assert.Contains("fresh-auto.dump", remaining);
        Assert.False(File.Exists(Path.Combine(_tempDir, "old-auto.dump")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "old-auto.files.tar.gz")));
    }

    [Fact]
    public async Task Download_IsIdBased_AndListsShowProtectionAndRetention()
    {
        var h = Build();
        using var _ = h.Db;
        MakeSystemAdministrator(h);
        h.Permissions.Codes.Add(PermissionCodes.BackupsDownload);
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        var download = await h.Sut.OpenDownloadAsync(backup.Id, CancellationToken.None);
        Assert.NotNull(download);
        download!.Value.Content.Dispose();
        Assert.Equal(backup.FileName, download.Value.FileName);
        Assert.Null(await h.Sut.OpenDownloadAsync(Guid.NewGuid(), CancellationToken.None));

        var overview = await h.Sut.ListAsync(CancellationToken.None);
        Assert.Equal(30, overview.AutomaticRetentionDays);
        Assert.True(overview.Backups.Single(b => b.Id == backup.Id).Protected); // newest
        Assert.True(overview.DownloadAllowed);
        Assert.Equal(Now.UtcDateTime, overview.LastSuccessfulBackupAtUtc);
        Assert.False(overview.BackupStale);
    }

    [Theory]
    [InlineData("Planner", false)]        // the permission alone (tenant-assignable role) is not enough
    [InlineData("Administrator", false)]  // a tenant-made role that is merely NAMED Administrator
    public async Task Download_RequiresTheSystemAdministratorRole(string roleName, bool systemRole)
    {
        var h = Build();
        using var _ = h.Db;
        MakeSystemAdministrator(h, roleName, systemRole);
        h.Permissions.Codes.Add(PermissionCodes.BackupsDownload);
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        Assert.False(await h.Sut.CanDownloadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.OpenDownloadAsync(backup.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Download_SystemAdministratorWithoutPermission_IsRefused()
    {
        var h = Build();
        using var _ = h.Db;
        MakeSystemAdministrator(h);

        Assert.False(await h.Sut.CanDownloadAsync(CancellationToken.None));
    }

    // --- freshness health check (tag "backup") ---------------------------------------------

    private static async Task<HealthCheckResult> CheckFreshnessAsync(Harness h)
    {
        var check = new BackupFreshnessHealthCheck(h.Db.Context, h.Clock, Options.Create(new BackupOptions { MaxAgeHours = 26 }));
        return await check.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("backup", check, HealthStatus.Unhealthy, ["backup"]),
        });
    }

    [Fact]
    public async Task Freshness_NoBackup_IsUnhealthy_FreshIsHealthy_StaleIsUnhealthy()
    {
        var h = Build();
        using var _ = h.Db;

        Assert.Equal(HealthStatus.Unhealthy, (await CheckFreshnessAsync(h)).Status);

        await h.Sut.CreateAsync("Automatic", null, CancellationToken.None);
        var fresh = await CheckFreshnessAsync(h);
        Assert.Equal(HealthStatus.Healthy, fresh.Status);
        Assert.Equal(Now.UtcDateTime.ToString("O"), fresh.Data["lastSuccessfulBackupUtc"]);

        h.Clock.Advance(TimeSpan.FromHours(27));
        Assert.Equal(HealthStatus.Unhealthy, (await CheckFreshnessAsync(h)).Status);
    }

    [Fact]
    public async Task Freshness_LatestAttemptFailed_IsDegraded()
    {
        var h = Build();
        using var _ = h.Db;
        await h.Sut.CreateAsync("Automatic", null, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(1));
        h.Runner.ExitCode = 1;
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync("Automatic", null, CancellationToken.None));

        Assert.Equal(HealthStatus.Degraded, (await CheckFreshnessAsync(h)).Status);
    }
}
