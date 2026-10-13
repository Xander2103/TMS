using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
public class FleetKpiController : ControllerBase
{
    private readonly IFleetKpiService _service;

    public FleetKpiController(IFleetKpiService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/kpi")]
    [RequirePermission(PermissionCodes.KpiView, PermissionCodes.VehiclesView)]
    public async Task<ActionResult<FleetKpiDto>> Vehicle(
        Guid vehicleId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var kpi = await _service.GetVehicleKpiAsync(vehicleId, start, end, cancellationToken);
        return kpi is null ? NotFound() : Ok(kpi);
    }

    [HttpGet("api/trailers/{trailerId:guid}/kpi")]
    [RequirePermission(PermissionCodes.KpiView, PermissionCodes.TrailersView)]
    public async Task<ActionResult<FleetKpiDto>> Trailer(
        Guid trailerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var kpi = await _service.GetTrailerKpiAsync(trailerId, start, end, cancellationToken);
        return kpi is null ? NotFound() : Ok(kpi);
    }

    /// <summary>Defaults to the last 365 days; caps the window at ~2 years.</summary>
    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddYears(-1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber > 366 * 2)
        {
            start = end.AddYears(-2);
        }

        return (start, end);
    }
}
