using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Damage/incident reports for vehicles and trailers. List/create nested under the owning
/// resource; item operations and the recent overview are flat.
/// </summary>
[ApiController]
public class DamageReportsController : ControllerBase
{
    private readonly IDamageReportService _service;

    public DamageReportsController(IDamageReportService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<DamageReportDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var reports = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return reports is null ? NotFound() : Ok(reports);
    }

    [HttpGet("api/trailers/{trailerId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<DamageReportDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var reports = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return reports is null ? NotFound() : Ok(reports);
    }

    [HttpGet("api/damage-reports/recent")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<RecentDamageDto>>> ListRecent(
        [FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListRecentAsync(take, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsCreate)]
    public async Task<ActionResult<DamageReportDto>> CreateForVehicle(
        Guid vehicleId, CreateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsCreate)]
    public async Task<ActionResult<DamageReportDto>> CreateForTrailer(
        Guid trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/damage-reports/{id:guid}")]
    [RequirePermission(PermissionCodes.DamageReportsEdit)]
    public async Task<ActionResult<DamageReportDto>> Update(
        Guid id, UpdateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    [HttpDelete("api/damage-reports/{id:guid}")]
    [RequirePermission(PermissionCodes.DamageReportsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<DamageReportDto> Handle(DamageOperationResult result, bool created) => result.Outcome switch
    {
        DamageOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Report),
        DamageOperationOutcome.Success => Ok(result.Report),
        DamageOperationOutcome.NotFound => NotFound(),
        DamageOperationOutcome.OwnerNotFound => NotFound(),
        DamageOperationOutcome.InvalidReference => BadRequest(new { message = "De gekoppelde chauffeur bestaat niet." }),
        DamageOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
