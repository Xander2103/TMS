using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/fleet/dashboard")]
public class FleetDashboardController : ControllerBase
{
    private readonly IFleetDashboardService _service;

    public FleetDashboardController(IFleetDashboardService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<FleetDashboardDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAsync(cancellationToken));
    }
}
