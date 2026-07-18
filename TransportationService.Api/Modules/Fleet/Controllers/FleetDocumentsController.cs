using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Documents for vehicles and trailers. List/create are nested under the owning resource;
/// item operations and the expiring-documents overview are flat.
/// </summary>
[ApiController]
public class FleetDocumentsController : ControllerBase
{
    private readonly IFleetDocumentService _service;

    public FleetDocumentsController(IFleetDocumentService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<FleetDocumentDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpGet("api/trailers/{trailerId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<FleetDocumentDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpGet("api/fleet-documents/expiring")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<ExpiringFleetDocumentDto>>> ListExpiring(
        [FromQuery] int withinDays = 60, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListExpiringAsync(withinDays, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsCreate)]
    public async Task<ActionResult<FleetDocumentDto>> CreateForVehicle(
        Guid vehicleId, CreateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsCreate)]
    public async Task<ActionResult<FleetDocumentDto>> CreateForTrailer(
        Guid trailerId, CreateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/fleet-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetDocumentsEdit)]
    public async Task<ActionResult<FleetDocumentDto>> Update(
        Guid id, UpdateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    [HttpDelete("api/fleet-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetDocumentsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<FleetDocumentDto> Handle(FleetDocumentOperationResult result, bool created) => result.Outcome switch
    {
        FleetDocumentOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Document),
        FleetDocumentOperationOutcome.Success => Ok(result.Document),
        FleetDocumentOperationOutcome.NotFound => NotFound(),
        FleetDocumentOperationOutcome.OwnerNotFound => NotFound(),
        FleetDocumentOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
