using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

/// <summary>
/// T-H6: the dossier's operational history ("Wijzigingshistoriek"). It is part of the dossier, so it
/// follows the dossier's own read right (<c>dossiers.view</c> / <c>dossiers.manage</c>) — planners
/// and dispatchers read it without the global <c>audit_logs.view</c>, which still guards the raw
/// tenant-wide audit log. A dossier of another tenant is a 404. The "pricing" events (prices,
/// sales lines, invoices) only reach a caller who may see prices (Security M1).
/// </summary>
[ApiController]
[Route("api/dossiers/{dossierId:guid}/timeline")]
public class DossierTimelineController : ControllerBase
{
    private readonly IDossierTimelineService _service;
    private readonly IPermissionAuthorizationService _permissions;
    private readonly ICurrentUserContext _currentUser;

    public DossierTimelineController(
        IDossierTimelineService service, IPermissionAuthorizationService permissions, ICurrentUserContext currentUser)
    {
        _service = service;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierTimelineEventDto>>> Get(Guid dossierId, CancellationToken cancellationToken)
    {
        var timeline = await _service.GetAsync(dossierId, cancellationToken);
        if (timeline is null)
        {
            return NotFound();
        }

        return await CommercialVisibility.CanViewPricesAsync(_permissions, _currentUser.CurrentUserId, cancellationToken)
            ? Ok(timeline)
            : Ok(timeline.Where(e => e.Category != "pricing").ToList());
    }
}
