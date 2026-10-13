using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Common;
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

    /// <summary>Storage directory; default resolves under the content root's App_Data.</summary>
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
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var tail = stderr.Length > 800 ? stderr[^800..] : stderr;
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
    bool StorageAvailable = true);

public record RestoreResultDto(
    Guid SafetyBackupId, string SafetyBackupFileName, string DatabaseStatus, string Advice);

public interface IBackupService
{
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
    private readonly TransportationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IBackupProcessRunner _runner;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly BackupOptions _options;
    private readonly IPermissionAuthorizationService? _permissionService;
    private readonly ICurrentUserContext? _currentUser;

    public BackupService(
        TransportationDbContext dbContext, IConfiguration configuration, IHostEnvironment environment,
        IBackupProcessRunner runner, IAuditService auditService, TimeProvider timeProvider,
        IOptions<BackupOptions>? options = null,
        IPermissionAuthorizationService? permissionService = null,
        ICurrentUserContext? currentUser = null)
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
    }

    private string BackupDirectory =>
        _options.Directory ?? Path.Combine(_environment.ContentRootPath, "App_Data", "backups");

    public async Task<BackupOverviewDto> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.DatabaseBackups.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var newestCompletedId = rows.Where(b => b.Status is "Completed" or "Restored")
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefault();

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

        return new BackupOverviewDto(
            backups.OrderByDescending(b => b.CreatedAtUtc).ToList(),
            _options.AutomaticRetentionDays, _options.PreRestoreRetentionDays,
            _options.AutomaticEnabled,
            storageAvailable ? "Beschikbaar" : "Opslagmap niet beschikbaar",
            storageAvailable);
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
        var fullPath = Path.Combine(BackupDirectory, fileName);

        var (exitCode, stderr) = await _runner.RunAsync(
            _options.PgDumpPath,
            ["-h", conn.Host, "-p", conn.Port, "-U", conn.Username, "-d", conn.Database, "-Fc", "-f", fullPath],
            PasswordEnvironment(conn), cancellationToken);
        if (exitCode != 0)
        {
            TryDelete(fullPath);
            await _auditService.RecordAsync("DatabaseBackup", id.ToString(), "BackupFailed",
                null, new { Source = source, ExitCode = exitCode }, cancellationToken);
            throw new DomainValidationException(
                $"Back-up mislukt (pg_dump exitcode {exitCode}). Controleer de serverlogs.");
        }

        var backup = new DatabaseBackup
        {
            Id = id,
            FileName = fileName,
            CreatedAtUtc = now,
            SizeBytes = new FileInfo(fullPath).Length,
            Source = source,
            Status = "Completed",
            SchemaVersion = await LastAppliedMigrationAsync(cancellationToken),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedByUserId = _currentUser?.CurrentUserId,
        };
        _dbContext.DatabaseBackups.Add(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "BackupCreated",
            null, new { backup.FileName, backup.Source, backup.SizeBytes }, cancellationToken);

        await CleanupExpiredAsync(cancellationToken);
        return new BackupDto(backup.Id, backup.FileName, backup.CreatedAtUtc, backup.SizeBytes,
            backup.Source, backup.Status, backup.SchemaVersion, backup.Note, null, Protected: true);
    }

    public async Task<(Stream Content, string FileName)?> OpenDownloadAsync(Guid id, CancellationToken cancellationToken)
    {
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

        TryDelete(Path.Combine(BackupDirectory, backup.FileName));
        _dbContext.DatabaseBackups.Remove(backup);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("DatabaseBackup", backup.Id.ToString(), "BackupDeleted",
            new { backup.FileName, backup.Source, backup.SizeBytes }, null, cancellationToken);
    }

    public async Task<RestoreResultDto> RestoreAsync(
        Guid id, string typedConfirmation, CancellationToken cancellationToken)
    {
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
            TryDelete(Path.Combine(BackupDirectory, backup.FileName));
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
