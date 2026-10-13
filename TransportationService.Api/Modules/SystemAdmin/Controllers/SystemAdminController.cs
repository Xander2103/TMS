using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.SystemAdmin.Services;

namespace TransportationService.Api.Modules.SystemAdmin.Controllers;

/// <summary>
/// Settings/system wave 2026-08: Systeeminformatie + back-upbeheer. Every action is
/// permission-gated here AND (for the destructive ones) fail-closed re-checked inside
/// BackupService. The frontend only ever sends server-generated backup ids — no file
/// names, no paths.
/// </summary>
[ApiController]
public class SystemAdminController : ControllerBase
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IBackupService _backups;

    public SystemAdminController(ISystemInfoService systemInfo, IBackupService backups)
    {
        _systemInfo = systemInfo;
        _backups = backups;
    }

    [HttpGet("api/system-info")]
    [RequirePermission(PermissionCodes.SystemInfoView)]
    public async Task<ActionResult<SystemInfoDto>> Info(CancellationToken cancellationToken) =>
        Ok(await _systemInfo.GetAsync(cancellationToken));

    [HttpGet("api/system-info/backups")]
    [RequirePermission(PermissionCodes.BackupsView)]
    public async Task<ActionResult<BackupOverviewDto>> Backups(CancellationToken cancellationToken) =>
        Ok(await _backups.ListAsync(cancellationToken));

    public record CreateBackupRequest(string? Note);

    [HttpPost("api/system-info/backups")]
    [RequirePermission(PermissionCodes.BackupsCreate)]
    public async Task<ActionResult<BackupDto>> Create(CreateBackupRequest request, CancellationToken cancellationToken) =>
        Ok(await _backups.CreateAsync("Manual", request.Note, cancellationToken));

    [HttpGet("api/system-info/backups/{id:guid}/download")]
    [RequirePermission(PermissionCodes.BackupsDownload)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _backups.OpenDownloadAsync(id, cancellationToken);
        return result is null
            ? NotFound()
            : File(result.Value.Content, "application/octet-stream", result.Value.FileName);
    }

    [HttpDelete("api/system-info/backups/{id:guid}")]
    [RequirePermission(PermissionCodes.BackupsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _backups.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    public record RestoreBackupRequest(
        /// <summary>Must be the EXACT file name of the backup — the typed confirmation.</summary>
        string Confirmation);

    /// <summary>TERUGZETTEN — destructive: replaces the current data with the backup's
    /// contents after an automatic safety backup. Never called "activeren".</summary>
    [HttpPost("api/system-info/backups/{id:guid}/restore")]
    [RequirePermission(PermissionCodes.BackupsRestore)]
    public async Task<ActionResult<RestoreResultDto>> Restore(
        Guid id, RestoreBackupRequest request, CancellationToken cancellationToken) =>
        Ok(await _backups.RestoreAsync(id, request.Confirmation, cancellationToken));
}
