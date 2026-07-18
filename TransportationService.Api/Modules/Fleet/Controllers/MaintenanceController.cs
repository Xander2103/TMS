using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Maintenance jobs for vehicles and trailers. List/create nested under the owning resource;
/// item operations, completion, and the due overview are flat.
/// </summary>
[ApiController]
public class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _service;

    public MaintenanceController(IMaintenanceService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/maintenance")]
    [RequirePermission(PermissionCodes.MaintenanceView)]
    public async Task<ActionResult<IReadOnlyList<MaintenanceRecordDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var records = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return records is null ? NotFound() : Ok(records);
    }

    [HttpGet("api/trailers/{trailerId:guid}/maintenance")]
    [RequirePermission(PermissionCodes.MaintenanceView)]
    public async Task<ActionResult<IReadOnlyList<MaintenanceRecordDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var records = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return records is null ? NotFound() : Ok(records);
    }

    [HttpGet("api/maintenance/due")]
    [RequirePermission(PermissionCodes.MaintenanceView)]
    public async Task<ActionResult<IReadOnlyList<DueMaintenanceDto>>> ListDue(
        [FromQuery] int withinDays = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListDueAsync(withinDays, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/maintenance")]
    [RequirePermission(PermissionCodes.MaintenanceCreate)]
    public async Task<ActionResult<MaintenanceRecordDto>> CreateForVehicle(
        Guid vehicleId, CreateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/maintenance")]
    [RequirePermission(PermissionCodes.MaintenanceCreate)]
    public async Task<ActionResult<MaintenanceRecordDto>> CreateForTrailer(
        Guid trailerId, CreateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/maintenance/{id:guid}")]
    [RequirePermission(PermissionCodes.MaintenanceEdit)]
    public async Task<ActionResult<MaintenanceRecordDto>> Update(
        Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    public record CompleteMaintenanceResponse(MaintenanceRecordDto Record, MaintenanceRecordDto? FollowUp);

    [HttpPost("api/maintenance/{id:guid}/complete")]
    [RequirePermission(PermissionCodes.MaintenanceEdit)]
    public async Task<ActionResult<CompleteMaintenanceResponse>> Complete(
        Guid id, CompleteMaintenanceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CompleteAsync(id, request, cancellationToken);
        return result.Outcome switch
        {
            MaintenanceOperationOutcome.Success => Ok(new CompleteMaintenanceResponse(result.Record!, result.FollowUp)),
            MaintenanceOperationOutcome.NotFound => NotFound(),
            MaintenanceOperationOutcome.AlreadyCompleted => Conflict(new { message = "Dit onderhoud is al afgerond of geannuleerd." }),
            MaintenanceOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    [HttpDelete("api/maintenance/{id:guid}")]
    [RequirePermission(PermissionCodes.MaintenanceDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<MaintenanceRecordDto> Handle(MaintenanceOperationResult result, bool created) => result.Outcome switch
    {
        MaintenanceOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Record),
        MaintenanceOperationOutcome.Success => Ok(result.Record),
        MaintenanceOperationOutcome.NotFound => NotFound(),
        MaintenanceOperationOutcome.OwnerNotFound => NotFound(),
        MaintenanceOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
