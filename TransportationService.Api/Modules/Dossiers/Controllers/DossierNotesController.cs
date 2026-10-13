using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

/// <summary>
/// D7: dossier notes (dossier-level, or about one activity). Reading follows the dossier
/// (<c>dossiers.view</c>), writing is a dossier edit (<c>dossiers.manage</c>). A note of another
/// dossier or tenant is a 404, never a 403 that would confirm it exists.
/// </summary>
[ApiController]
[Route("api/dossiers/{dossierId:guid}/notes")]
public class DossierNotesController : ControllerBase
{
    private readonly IDossierNoteService _service;

    public DossierNotesController(IDossierNoteService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierNoteDto>>> List(
        Guid dossierId, [FromQuery] Guid? activityId, CancellationToken cancellationToken)
    {
        var notes = await _service.ListAsync(dossierId, activityId, cancellationToken);
        return notes is null ? NotFound() : Ok(notes);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierNoteDto>> Create(
        Guid dossierId, CreateDossierNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await _service.CreateAsync(dossierId, request, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpPut("{noteId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierNoteDto>> Update(
        Guid dossierId, Guid noteId, UpdateDossierNoteRequest request, CancellationToken cancellationToken)
    {
        var note = await _service.UpdateAsync(dossierId, noteId, request, cancellationToken);
        return note is null ? NotFound() : Ok(note);
    }

    [HttpDelete("{noteId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<IActionResult> Delete(Guid dossierId, Guid noteId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(dossierId, noteId, cancellationToken) ? NoContent() : NotFound();
    }
}
