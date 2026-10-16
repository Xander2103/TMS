using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

/// <summary>
/// T-H6: the dossier's operational history ("Wijzigingshistoriek"). It is part of the dossier, so it
/// follows the dossier's own read right (<c>dossiers.view</c> / <c>dossiers.manage</c>) — planners
/// and dispatchers read it without the global <c>audit_logs.view</c>, which still guards the raw
/// tenant-wide audit log. A dossier of another tenant is a 404.
/// </summary>
[ApiController]
[Route("api/dossiers/{dossierId:guid}/timeline")]
public class DossierTimelineController : ControllerBase
{
    private readonly IDossierTimelineService _service;

    public DossierTimelineController(IDossierTimelineService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierTimelineEventDto>>> Get(Guid dossierId, CancellationToken cancellationToken)
    {
        var timeline = await _service.GetAsync(dossierId, cancellationToken);
        return timeline is null ? NotFound() : Ok(timeline);
    }
}
