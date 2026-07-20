using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Planning.Dtos;

namespace TransportationService.Api.Modules.Incidents.Controllers;

/// <summary>Driver-app incident reporting: self-scoped, idempotent, on the shared register.</summary>
[ApiController]
[Route("api/my/incidents")]
public class MyIncidentsController : ControllerBase
{
    private readonly IDriverIncidentService _service;

    public MyIncidentsController(IDriverIncidentService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<IncidentListItemDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListMineAsync(cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<IncidentDetailDto>> Create(
        CreateDriverIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.CreateMineAsync(request, cancellationToken);
        return incident is null
            ? NotFound(new { message = "Er is geen chauffeursprofiel gekoppeld aan dit account." })
            : Ok(incident);
    }
}
