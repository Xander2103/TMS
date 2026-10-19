using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Storage;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.SystemAdmin.Entities;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>Configuration of the in-app backup store. The directory MUST live outside any
/// web root; downloads stream by server-generated id only, never by path.</summary>
public class BackupOptions
{
    public const string SectionName = "Backups";

    /// <summary>Backup directory; default <c>&lt;Storage:RootPath&gt;/backups</c>. Must be absolute and
    /// outside the release directory (StorageRootValidator).</summary>
    public string? Directory { get; set; }

    public string PgDumpPath { get; set; } = "pg_dump";
    public string PgRestorePath { get; set; } = "pg_restore";

    /// <summary>Read-only extra directories whose .dump files are listed as PreDeployment
    /// backups (the deploy script's own dumps) — visible, never deletable from the app.</summary>
    public string[] ExternalDirectories { get; set; } = [];

    public int AutomaticRetentionDays { get; set; } = 30;
    public int PreRestoreRetentionDays { get; set; } = 30;

    public bool AutomaticEnabled { get; set; } = true;
    public int AutomaticHourUtc { get; set; } = 2;

    /// <summary>Freshness limit for monitoring: the "backup" health check and the overview flag a
    /// newest successful backup older than this (default 26 h = daily run plus slack).</summary>
    public int MaxAgeHours { get; set; } = 26;
}

/// <summary>Process seam so tests never spawn real pg_dump/pg_restore.</summary>
public interface IBackupProcessRunner
{
    Task<(int ExitCode, string StdErrTail)> RunAsync(
        string fileName, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken);
}

public class ProcessBackupRunner : IBackupProcessRunner
{
    public async Task<(int ExitCode, string StdErrTail)> RunAsync(
        string fileName, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        // ArgumentList (never a shell string): no injection surface, whatever the values.
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var process = Process.Start(info)
            ?? throw new DomainValidationException($"Kon {Path.GetFileName(fileName)} niet starten.");
        // BG-02: a cancelled request/shutdown must stop the child too, never leave it running.
        await using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already exited
            }
        });

        // Drain both pipes concurrently: reading them one after the other deadlocks as soon as the
        // child fills the other pipe's buffer (pg_restore --list writes the whole TOC to stdout).
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        await Task.WhenAll(stderrTask, stdoutTask);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = stderrTask.Result;
        var tail = stderr.Length > 4000 ? stderr[^4000..] : stderr;
        return (process.ExitCode, tail);
    }
}

public record BackupDto(
    Guid Id, string FileName, DateTime CreatedAtUtc, long SizeBytes, string Source,
    string Status, string? SchemaVersion, string? Note, DateTime? RestoredAtUtc,
    /// <summary>Protected backups can never be deleted (newest completed, restore in progress).</summary>
    bool Protected,
    /// <summary>Read-only entries (deploy-script dumps) support no actions from the app.</summary>
    bool ReadOnly = false);

public record BackupOverviewDto(
    IReadOnlyList<BackupDto> Backups,
    int AutomaticRetentionDays,
    int PreRestoreRetentionDays,
    bool AutomaticEnabled,
    /// <summary>LEGACY Nederlandse weergavetekst; logica hoort op StorageAvailable (i18n-wave).</summary>
    string StorageStatus,
    /// <summary>Stabiele machineleesbare status van de back-upopslagmap.</summary>
    bool StorageAvailable = true,
    /// <summary>H4: newest successful (Completed/Restored) in-app backup — the freshness signal.</summary>
    DateTime? LastSuccessfulBackupAtUtc = null,
    /// <summary>True when there is no successful backup within Backups:MaxAgeHours.</summary>
    bool BackupStale = false,
    int MaxBackupAgeHours = 26,
    /// <summary>H8: in-app restore only exists in Development; elsewhere the offline runbook.</summary>
    bool RestoreEnabled = false,
    /// <summary>H8: whole-database downloads are limited to the system Administrator role.</summary>
    bool DownloadAllowed = false);

public record RestoreResultDto(
    Guid SafetyBackupId, string SafetyBackupFileName, string DatabaseStatus, string Advice);

public interface IBackupService
{
    /// <summary>H8: false outside Development — restore is an offline operator procedure there
    /// (docs/operations/backup-restore.md).</summary>
    bool InAppRestoreEnabled { get; }
    Task<bool> CanDownloadAsync(CancellationToken cancellationToken);
    Task<BackupOverviewDto> ListAsync(CancellationToken cancellationToken);
    Task<BackupDto> CreateAsync(string source, string? note, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName)?> OpenDownloadAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<RestoreResultDto> RestoreAsync(Guid id, string typedConfirmation, CancellationToken cancellationToken);
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Settings/system wave 2026-08: database backups managed from the ERP without shell
/// knowledge. Every rule the security review demanded lives HERE, server-side:
///  * ids are server-generated and the only client handle (no paths from the frontend);
///  * pg_dump/pg_restore run via ArgumentList with credentials only in the child process
///    environment — nothing secret is stored or returned;
///  * H4 (2026-10-18): a backup is a SET — DB dump + durable-storage archive + sha256 manifest —
///    and is only "Completed" after both parts are verified (pg_restore --list, archive read back,
///    checksums); any failing part records a Failed row with the reason and logs tool stderr;
///  * H8: restore is Development-only (offline runbook elsewhere) and whole-database downloads
///    are limited to the system Administrator role;
///  * delete/restore are fail-closed double-checked in the service (L7 house rule), on top
///    of the controller permission gates;
///  * restore demands a typed confirmation (the exact file name), takes a fresh safety
///    backup FIRST, terminates other connections, restores, then health-checks;
///  * the newest completed backup and any restore-in-progress are protected from deletion;
///  * retention only ever removes UNPROTECTED Automatic/PreRestore backups past their
///    configured age — manual backups are never auto-deleted.
/// </summary>
public class BackupService : IBackupService
{
    private const string AdministratorRoleName = "Administrator";

    public const string RestoreDisabledMessage =
        "Terugzetten vanuit de applicatie is uitgeschakeld buiten Development. Een herstel is een offline "
        + "beheerdersprocedure: zie docs/operations/backup-restore.md.";

    private readonly TransportationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IBackupProcessRunner _runner;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly BackupOptions _options;
    private readonly IPermissionAuthorizationService? _permissionService;
    private readonly ICurrentUserContext? _currentUser;
    private readonly StorageRoot _storageRoot;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        TransportationDbContext dbContext, IConfiguration configuration, IHostEnvironment environment,
        IBackupProcessRunner runner, IAuditService auditService, TimeProvider timeProvider,
        IOptions<BackupOptions>? options = null,
        IPermissionAuthorizationService? permissionService = null,
        ICurrentUserContext? currentUser = null,
        StorageRoot? storageRoot = null,
        ILogger<BackupService>? logger = null)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
        _runner = runner;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _options = options?.Value ?? new BackupOptions();
        _permissionService = permissionService;
        _currentUser = currentUser;
        _storageRoot = storageRoot ?? StorageRoot.DevelopmentDefault(environment);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupService>.Instance;
    }

    private string BackupDirectory => _options.Directory ?? _storageRoot.BackupsDirectory;

    public bool InAppRestoreEnabled => _environment.IsDevelopment();

    public async Task<BackupOverviewDto> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.DatabaseBackups.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var newestCompleted = rows.Where(b => b.Status is "Completed" or "Restored")
            .OrderByDescending(b => b.CreatedAtUtc)
            .FirstOrDefault();
        var newestCompletedId = newestCompleted?.Id;

        var backups = rows.Select(b => new BackupDto(
                b.Id, b.FileName, b.CreatedAtUtc, b.SizeBytes, b.Source, b.Status,
                b.SchemaVersion, b.Note, b.RestoredAtUtc,
                Protected: b.Id == newestCompletedId || b.Status == "Restoring"))
            .ToList();

        // Deploy-script dumps: visible so the admin sees the WHOLE backup picture, but
        // read-only — their lifecycle belongs to the server, not the app.
        foreach (var directory in _options.ExternalDirectories.Where(System.IO.Directory.Exists))
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.dump"))
            {
                var info = new FileInfo(file);
                backups.Add(new BackupDto(
                    Guid.Empty, info.Name, info.LastWriteTimeUtc, info.Length,
                    "PreDeployment", "Completed", null,
                    "Aangemaakt door het deployscript (alleen-lezen).", null,
                    Protected: true, ReadOnly: true));
            }
        }

        bool storageAvailable;
        try
        {
            System.IO.Directory.CreateDirectory(BackupDirectory);
            storageAvailable = true;
        }
        catch
        {
            storageAvailable = false;
        }

        var lastSuccess = newestCompleted?.CreatedAtUtc;
        return new BackupOverviewDto(
            backups.OrderByDescending(b => b.CreatedAtUtc).ToList(),
            _options.AutomaticRetentionDays, _options.PreRestoreRetentionDays,
            _options.AutomaticEnabled,
            storageAvailable ? "Beschikbaar" : "Opslagmap niet beschikbaar",
            storageAvailable,
            LastSuccessfulBackupAtUtc: lastSuccess,
            BackupStale: BackupFreshnessHealthCheck.IsStale(lastSuccess, _timeProvider.GetUtcNow().UtcDateTime, _options),
            MaxBackupAgeHours: _options.MaxAgeHours,
            RestoreEnabled: InAppRestoreEnabled,
            DownloadAllowed: await CanDownloadAsync(cancellationToken));
    }

    public async Task<BackupDto> CreateAsync(string source, string? note, CancellationToken cancellationToken)
    {
        if (source is not ("Manual" or "Automatic" or "PreRestore"))
        {
            throw new DomainValidationException("source", "Ongeldige back-upbron.");
        }

        var conn = ResolveConnection();
        System.IO.Directory.CreateDirectory(BackupDirectory);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        // Server-generated, immutable, and free of user input — the id is in the name so a
        // file on disk can always be traced back to its metadata row.
        var fileName = $"backup-{conn.Database}-{now:yyyyMMdd-HHmmss}-{source.ToLowerInvariant()}-{id.ToString("N")[..8]}.dump";
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        long sizeBytes;
        try
        {
            sizeBytes = await ProduceVerifiedBackupSetAsync(conn, fileName, cancellationToken);
        }
        catch (BackupStepException failure)
        {
            await RecordFailureAsync(id, fileName, source, trimmedNote, now, failure.Reason, failure.InnerException);
            throw new DomainValidationException($"Back-up mislukt: {failure.Reason}. Controleer de serverlogs.");
        }
        catch (OperationCanceledException)
        {
            // Never leave a half-written set behind, and never let it look like a success.
            await RecordFailureAsync(id, fileName, source, trimmedNote, now, "afgebroken (annulering of afsluiten)", null);
            throw;
        }

        var backup = new DatabaseBackup
        {
            Id = id,
            FileName = fileName,
            CreatedAtUtc = now,
            SizeBytes = sizeBytes,
            Source = source,
            Status = "Completed",
            SchemaVersion = await LastAppliedMigrationAsync(cancellationToken),
            Note = trimmedNote,
            CreatedByUserId = _currentUser?.CurrentUserId,
        };
        _dbContext.DatabaseBackups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "BackupCreated",
            null, new { backup.FileName, backup.Source, backup.SizeBytes }, cancellationToken);
        _logger.LogInformation(
            "Back-up {FileName} ({Source}) voltooid en geverifieerd: {SizeBytes} bytes (dump + bestandsarchief).",
            backup.FileName, backup.Source, backup.SizeBytes);

        await CleanupExpiredAsync(cancellationToken);
        return new BackupDto(backup.Id, backup.FileName, backup.CreatedAtUtc, backup.SizeBytes,
            backup.Source, backup.Status, backup.SchemaVersion, backup.Note, null, Protected: true);
    }

    /// <summary>
    /// H4: dump → storage archive → manifest → verify both. Each step that fails throws a
    /// <see cref="BackupStepException"/> naming the step; the partial set is removed first.
    /// Returns the total size of dump + archive.
    /// </summary>
    private async Task<long> ProduceVerifiedBackupSetAsync(
        ConnectionParts conn, string fileName, CancellationToken cancellationToken)
    {
        var dumpPath = Path.Combine(BackupDirectory, fileName);
        var archiveFileName = BackupArtifacts.ArchiveFileName(fileName);
        var archivePath = Path.Combine(BackupDirectory, archiveFileName);
        var manifestPath = Path.Combine(BackupDirectory, BackupArtifacts.ManifestFileName(fileName));
        var step = "databankdump (pg_dump)";
        try
        {
            // 1. Database dump.
            var (exitCode, stderr) = await _runner.RunAsync(
                _options.PgDumpPath,
                ["-h", conn.Host, "-p", conn.Port, "-U", conn.Username, "-d", conn.Database, "-Fc", "-f", dumpPath],
                PasswordEnvironment(conn), cancellationToken);
            LogToolOutput("pg_dump", fileName, exitCode, stderr);
            if (exitCode != 0)
            {
                throw new BackupStepException($"pg_dump exitcode {exitCode}");
            }

            if (!File.Exists(dumpPath) || new FileInfo(dumpPath).Length == 0)
            {
                throw new BackupStepException("pg_dump leverde een leeg of ontbrekend bestand op");
            }

            // 2. Durable-storage archive. The backup directory and the dev sink are never part of
            // it (no dumps-inside-dumps, no raw tokens).
            step = "bestandsarchief (opslagmap)";
            var entries = await BackupArtifacts.CreateArchiveAsync(
                _storageRoot.RootPath, archivePath,
                [BackupDirectory, _storageRoot.BackupsDirectory, _storageRoot.MessageSinkDirectory],
                cancellationToken);

            // 3. Manifest (sha256sum format).
            step = "checksummanifest";
            var dumpHash = await BackupArtifacts.Sha256Async(dumpPath, cancellationToken);
            var archiveHash = await BackupArtifacts.Sha256Async(archivePath, cancellationToken);
            await BackupArtifacts.WriteManifestAsync(
                manifestPath, [(fileName, dumpHash), (archiveFileName, archiveHash)], cancellationToken);

            // 4. Verification BEFORE anything is reported as a success.
            step = "verificatie databankdump (pg_restore --list)";
            var (listExit, listStderr) = await _runner.RunAsync(
                _options.PgRestorePath, ["--list", dumpPath], new Dictionary<string, string>(), cancellationToken);
            LogToolOutput("pg_restore --list", fileName, listExit, listStderr);
            if (listExit != 0)
            {
                throw new BackupStepException($"de dump is niet leesbaar (pg_restore --list exitcode {listExit})");
            }

            step = "verificatie bestandsarchief";
            var readBack = await BackupArtifacts.CountArchiveEntriesAsync(archivePath, cancellationToken);
            if (readBack != entries)
            {
                throw new BackupStepException(
                    $"het bestandsarchief bevat {readBack} van de {entries} verwachte bestanden");
            }

            step = "verificatie checksums";
            var manifest = await BackupArtifacts.ReadManifestAsync(manifestPath, cancellationToken);
            if (!manifest.TryGetValue(fileName, out var expectedDump)
                || expectedDump != await BackupArtifacts.Sha256Async(dumpPath, cancellationToken)
                || !manifest.TryGetValue(archiveFileName, out var expectedArchive)
                || expectedArchive != await BackupArtifacts.Sha256Async(archivePath, cancellationToken))
            {
                throw new BackupStepException("de checksums komen niet overeen met het manifest");
            }

            return new FileInfo(dumpPath).Length + new FileInfo(archivePath).Length;
        }
        catch (BackupStepException)
        {
            DeleteBackupSet(fileName);
            throw;
        }
        catch (OperationCanceledException)
        {
            DeleteBackupSet(fileName);
            throw;
        }
        catch (Exception ex)
        {
            DeleteBackupSet(fileName);
            throw new BackupStepException($"{step} mislukt ({ex.GetType().Name})", ex);
        }
    }

    /// <summary>A failed run leaves a Failed row (visible in the listing, ignored by the
    /// freshness signal), an audit entry and an error log with the reason — never a success.</summary>
    private async Task RecordFailureAsync(
        Guid id, string fileName, string source, string? note, DateTime createdAtUtc, string reason, Exception? cause)
    {
        _logger.LogError(cause, "Back-up {FileName} ({Source}) MISLUKT: {Reason}.", fileName, source, reason);
        try
        {
            var text = $"{(note is null ? string.Empty : note + " · ")}Mislukt: {reason}";
            _dbContext.DatabaseBackups.Add(new DatabaseBackup
            {
                Id = id,
                FileName = fileName,
                CreatedAtUtc = createdAtUtc,
                SizeBytes = 0,
                Source = source,
                Status = "Failed",
                Note = text.Length > 500 ? text[..500] : text,
                CreatedByUserId = _currentUser?.CurrentUserId,
            });
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            await _auditService.RecordAsync("DatabaseBackup", id.ToString(), "BackupFailed",
                null, new { Source = source, Reason = reason }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The DB may be the very thing that is down; the error log above already carries the reason.
            _logger.LogError(ex, "Kon de mislukte back-up {FileName} niet registreren.", fileName);
        }
    }

    private void LogToolOutput(string tool, string fileName, int exitCode, string stderr)
    {
        // LOG-04: tool stderr reaches the server log (it never contains the password, which only
        // travels in the child environment).
        if (exitCode != 0)
        {
            _logger.LogError("{Tool} voor {FileName} eindigde met exitcode {ExitCode}. stderr: {StdErr}",
                tool, fileName, exitCode, string.IsNullOrWhiteSpace(stderr) ? "(leeg)" : stderr.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogWarning("{Tool} voor {FileName} meldde op stderr: {StdErr}", tool, fileName, stderr.Trim());
        }
    }

    public async Task<bool> CanDownloadAsync(CancellationToken cancellationToken)
    {
        if (_currentUser?.CurrentUserId is not { } userId)
        {
            return false;
        }

        if (_permissionService is null
            || !await _permissionService.UserHasPermissionAsync(
                userId, Modules.Identity.PermissionCodes.BackupsDownload, cancellationToken))
        {
            return false;
        }

        // H8/SEC-03: a dump holds EVERY tenant's data, so a tenant-assignable permission alone is not
        // enough — only members of the protected system Administrator role may take it off the server.
        return await (
                from ur in _dbContext.UserRoles.AsNoTracking()
                join u in _dbContext.Users.AsNoTracking() on ur.UserId equals u.Id
                join r in _dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where ur.UserId == userId
                      && u.IsActive && !u.IsBlocked && u.CustomerId == null
                      && r.IsSystemRole && r.IsActive && r.Name == AdministratorRoleName
                      && r.TenantId == u.TenantId
                select 1)
            .AnyAsync(cancellationToken);
    }

    public async Task<(Stream Content, string FileName)?> OpenDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanDownloadAsync(cancellationToken))
        {
            throw new DomainValidationException(
                "Alleen de systeemrol Administrator kan een volledige databaseback-up downloaden.");
        }

        var backup = await _dbContext.DatabaseBackups.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (backup is null)
        {
            return null;
        }

        // The stored FileName is server-generated; joining it to the configured directory can
        // never traverse — but verify anyway (defense in depth against a tampered row).
        var fullPath = Path.GetFullPath(Path.Combine(BackupDirectory, backup.FileName));
        if (!fullPath.StartsWith(Path.GetFullPath(BackupDirectory), StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            return null;
        }

        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "BackupDownloaded",
            null, new { backup.FileName }, cancellationToken);
        return (File.OpenRead(fullPath), backup.FileName);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(Modules.Identity.PermissionCodes.BackupsDelete, cancellationToken);
        var backup = await _dbContext.DatabaseBackups
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new DomainValidationException("Back-up niet gevonden.");
        if (backup.Status == "Restoring")
        {
            throw new DomainValidationException("Deze back-up wordt momenteel teruggezet en kan niet worden verwijderd.");
        }

        var newestCompletedId = await _dbContext.DatabaseBackups.AsNoTracking()
            .Where(b => b.Status == "Completed" || b.Status == "Restored")
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (backup.Id == newestCompletedId)
        {
            throw new DomainValidationException(
                "De nieuwste back-up is beschermd en kan niet worden verwijderd. Maak eerst een nieuwe back-up.");
        }

        DeleteBackupSet(backup.FileName);
        _dbContext.DatabaseBackups.Remove(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "BackupDeleted",
            new { backup.FileName, backup.Source, backup.SizeBytes }, null, cancellationToken);
    }

    public async Task<RestoreResultDto> RestoreAsync(
        Guid id, string typedConfirmation, CancellationToken cancellationToken)
    {
        // H8: pg_restore --clean on the live database from an HTTP request is a Development
        // convenience only. Fail-closed here as well as in the controller.
        if (!InAppRestoreEnabled)
        {
            throw new DomainValidationException(RestoreDisabledMessage);
        }

        await RequirePermissionAsync(Modules.Identity.PermissionCodes.BackupsRestore, cancellationToken);
        var backup = await _dbContext.DatabaseBackups
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new DomainValidationException("Back-up niet gevonden.");
        if (backup.Status is not ("Completed" or "Restored"))
        {
            throw new DomainValidationException("Alleen een voltooide back-up kan worden teruggezet.");
        }

        // Typed confirmation: the admin must literally type the file name of THIS backup.
        if (!string.Equals(typedConfirmation?.Trim(), backup.FileName, StringComparison.Ordinal))
        {
            throw new DomainValidationException("confirmation",
                "Bevestiging klopt niet: typ exact de bestandsnaam van de back-up om terug te zetten.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(BackupDirectory, backup.FileName));
        if (!fullPath.StartsWith(Path.GetFullPath(BackupDirectory), StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            throw new DomainValidationException("Het back-upbestand ontbreekt op de server.");
        }

        // 1. Safety net FIRST: the current state is itself backed up before anything changes.
        var safety = await CreateAsync("PreRestore", $"Automatische veiligheidsback-up vóór terugzetten van {backup.FileName}", cancellationToken);

        backup.Status = "Restoring";
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "RestoreStarted",
            null, new { backup.FileName, SafetyBackupId = safety.Id }, cancellationToken);

        var conn = ResolveConnection();
        try
        {
            // 2. Maintenance lock: kick every OTHER connection so pg_restore gets exclusive
            // access; our own EF connections are pooled — clear them too. PostgreSQL-only
            // (the SQLite test double has no pg_stat_activity; the seam covers the rest).
            if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                    "WHERE datname = current_database() AND pid <> pg_backend_pid()", cancellationToken);
            }

            var (exitCode, stderr) = await _runner.RunAsync(
                _options.PgRestorePath,
                [
                    "-h", conn.Host, "-p", conn.Port, "-U", conn.Username, "-d", conn.Database,
                    "--clean", "--if-exists", "--no-owner", "--role", conn.Username,
                    "--exit-on-error", fullPath,
                ],
                PasswordEnvironment(conn), cancellationToken);
            LogToolOutput("pg_restore", backup.FileName, exitCode, stderr);
            if (exitCode != 0)
            {
                throw new DomainValidationException(
                    $"Terugzetten mislukt (pg_restore exitcode {exitCode}). De veiligheidsback-up {safety.FileName} bevat de vorige staat.");
            }

            // 3. Health check on the restored database.
            _ = await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            var schema = await LastAppliedMigrationAsync(cancellationToken);

            backup.Status = "Restored";
            backup.RestoredAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "RestoreCompleted",
                null, new { backup.FileName, SchemaVersion = schema, SafetyBackupId = safety.Id }, cancellationToken);

            return new RestoreResultDto(safety.Id, safety.FileName,
                $"Healthy (schema {schema ?? "onbekend"})",
                "Herstart de API-service gecontroleerd (caches/seeders) en verifieer de applicatie.");
        }
        catch
        {
            backup.Status = "Completed"; // the restore did not finish; the row is usable again
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "RestoreFailed",
                null, new { backup.FileName, SafetyBackupId = safety.Id }, CancellationToken.None);
            throw;
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var newestCompletedId = await _dbContext.DatabaseBackups.AsNoTracking()
            .Where(b => b.Status == "Completed" || b.Status == "Restored")
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var automaticCutoff = now.AddDays(-Math.Max(1, _options.AutomaticRetentionDays));
        var preRestoreCutoff = now.AddDays(-Math.Max(1, _options.PreRestoreRetentionDays));
        var expired = await _dbContext.DatabaseBackups
            .Where(b => b.Status != "Restoring" && b.Id != newestCompletedId
                        && ((b.Source == "Automatic" && b.CreatedAtUtc < automaticCutoff)
                            || (b.Source == "PreRestore" && b.CreatedAtUtc < preRestoreCutoff)))
            .ToListAsync(cancellationToken);
        foreach (var backup in expired)
        {
            DeleteBackupSet(backup.FileName);
            _dbContext.DatabaseBackups.Remove(backup);
        }

        if (expired.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("DatabaseBackup", "retention", "RetentionCleanup",
                null, new { Removed = expired.Count }, cancellationToken);
        }

        return expired.Count;
    }

    // --- helpers -------------------------------------------------------------------------

    /// <summary>A failed backup step; <see cref="Reason"/> is short, Dutch and secret-free.</summary>
    private sealed class BackupStepException : Exception
    {
        public BackupStepException(string reason, Exception? inner = null) : base(reason, inner) => Reason = reason;

        public string Reason { get; }
    }

    /// <summary>Fail-closed (L7): no wired authorization = no destructive backup rights.</summary>
    private async Task RequirePermissionAsync(string code, CancellationToken cancellationToken)
    {
        var allowed = _permissionService is not null && _currentUser?.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(userId, code, cancellationToken);
        if (!allowed)
        {
            throw new DomainValidationException("Je hebt geen rechten voor deze back-upactie.");
        }
    }

    private sealed record ConnectionParts(string Host, string Port, string Database, string Username, string? Password);

    private ConnectionParts ResolveConnection()
    {
        var raw = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new DomainValidationException("Er is geen databankverbinding geconfigureerd.");
        string host = "localhost", port = "5432";
        string? database = null, username = null, password = null;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim().Replace(" ", "").ToLowerInvariant();
            var value = part[(separator + 1)..].Trim();
            switch (key)
            {
                case "host": host = value; break;
                case "port": port = value; break;
                case "database": database = value; break;
                case "username" or "userid": username = value; break;
                case "password": password = value; break;
            }
        }

        if (database is null || username is null)
        {
            throw new DomainValidationException("De databankverbinding mist een database of gebruiker.");
        }

        return new ConnectionParts(host, port, database, username, password);
    }

    private static Dictionary<string, string> PasswordEnvironment(ConnectionParts conn) =>
        conn.Password is { Length: > 0 } password
            ? new Dictionary<string, string> { ["PGPASSWORD"] = password }
            : [];

    private async Task<string?> LastAppliedMigrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Removes the dump and its companions (archive + manifest); older DB-only backups
    /// simply have no companions.</summary>
    private void DeleteBackupSet(string dumpFileName)
    {
        TryDelete(Path.Combine(BackupDirectory, dumpFileName));
        TryDelete(Path.Combine(BackupDirectory, BackupArtifacts.ArchiveFileName(dumpFileName)));
        TryDelete(Path.Combine(BackupDirectory, BackupArtifacts.ManifestFileName(dumpFileName)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A leftover file is preferable to a failed API call; retention retries later.
        }
    }
}
