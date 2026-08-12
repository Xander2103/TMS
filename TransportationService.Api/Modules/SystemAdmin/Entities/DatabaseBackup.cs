using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.SystemAdmin.Entities;

/// <summary>
/// Metadata of one database backup managed from the ERP (settings/system wave 2026-08).
/// SYSTEM-scoped on purpose: a backup covers the WHOLE multi-tenant database, so this
/// entity is deliberately NOT tenant-owned — access is permission-gated
/// (backups.view/create/download/delete/restore), never tenant-filtered.
///
/// The Id is the ONLY handle the frontend ever sends: file names and paths are
/// server-generated and immutable, the storage directory lives outside any web root, and
/// no connection details are ever stored here.
/// </summary>
public class DatabaseBackup : IHasId
{
    public Guid Id { get; set; }

    /// <summary>Server-generated file name inside the configured backup directory.</summary>
    public string FileName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Manual | Automatic | PreRestore (safety copy made before a restore).
    /// PreDeployment dumps of the deploy script are listed read-only from disk.</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>Completed | Failed | Restoring | Restored.</summary>
    public string Status { get; set; } = "Completed";

    /// <summary>Last applied EF migration at backup time — tells an admin which schema
    /// the dump contains before restoring it.</summary>
    public string? SchemaVersion { get; set; }

    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>When this backup was last used as a restore source.</summary>
    public DateTime? RestoredAtUtc { get; set; }
}
