using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>FL-H1/H2/H4: the fleet readiness verdict of one asset for a day (default today).</summary>
[ApiController]
public class FleetReadinessController : ControllerBase
{
    private readonly IFleetReadinessService _readiness;

    public FleetReadinessController(IFleetReadinessService readiness) => _readiness = readiness;

    [HttpGet("api/vehicles/{vehicleId:guid}/readiness")]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<VehicleReadinessDto>> Vehicle(Guid vehicleId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var result = await _readiness.EvaluateVehicleAsync(vehicleId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("api/trailers/{trailerId:guid}/readiness")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<VehicleReadinessDto>> Trailer(Guid trailerId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var result = await _readiness.EvaluateTrailerAsync(trailerId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
