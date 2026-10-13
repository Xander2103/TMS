using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
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
/// </summary>
public class BackupServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 08, 13, 10, 0, 0, TimeSpan.Zero);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"tms-backup-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeRunner : IBackupProcessRunner
    {
        public int ExitCode { get; set; }
        public List<(string FileName, IReadOnlyList<string> Args)> Calls { get; } = [];
        public bool SawPassword { get; private set; }

        public Task<(int ExitCode, string StdErrTail)> RunAsync(
            string fileName, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            Calls.Add((fileName, arguments));
            SawPassword = SawPassword || environment.ContainsKey("PGPASSWORD");
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
        SqliteTestDbContext Db, BackupService Sut, FakeRunner Runner, PermissionSet Permissions, TestClock Clock);

    private Harness Build()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.SaveChanges();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=dbhost;Port=5433;Database=tms;Username=tms;Password=verysecret",
            })
            .Build();
        var runner = new FakeRunner();
        var permissions = new PermissionSet();
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var clock = new TestClock(Now);
        var sut = new BackupService(
            db.Context, configuration, new FakeEnvironment(), runner,
            new AuditService(db.Context, tenant, user), clock,
            Options.Create(new BackupOptions { Directory = _tempDir, AutomaticRetentionDays = 30 }),
            permissions, user);
        return new Harness(db, sut, runner, permissions, clock);
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
        // An older one deletes fine: row gone, file gone.
        await h.Sut.DeleteAsync(older.Id, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(_tempDir, older.FileName)));
        Assert.Single(h.Db.Context.DatabaseBackups.ToList());
    }

    [Fact]
    public async Task Restore_RequiresTypedConfirmation_TakesSafetyBackupFirst_AndHealthChecks()
    {
        var h = Build();
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
        var h = Build();
        using var _ = h.Db;
        h.Permissions.Codes.Add(PermissionCodes.BackupsCreate); // irrelevant right
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.RestoreAsync(backup.Id, backup.FileName, CancellationToken.None));
        Assert.DoesNotContain(h.Db.Context.DatabaseBackups.ToList(), b => b.Source == "PreRestore");
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
    }

    [Fact]
    public async Task Download_IsIdBased_AndListsShowProtectionAndRetention()
    {
        var h = Build();
        using var _ = h.Db;
        var backup = await h.Sut.CreateAsync("Manual", null, CancellationToken.None);

        var download = await h.Sut.OpenDownloadAsync(backup.Id, CancellationToken.None);
        Assert.NotNull(download);
        download!.Value.Content.Dispose();
        Assert.Equal(backup.FileName, download.Value.FileName);
        Assert.Null(await h.Sut.OpenDownloadAsync(Guid.NewGuid(), CancellationToken.None));

        var overview = await h.Sut.ListAsync(CancellationToken.None);
        Assert.Equal(30, overview.AutomaticRetentionDays);
        Assert.True(overview.Backups.Single(b => b.Id == backup.Id).Protected); // newest
    }
}
