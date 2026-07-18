using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Inspections for vehicles and trailers. List/create nested under the owning resource;
/// item operations, completion, and the due overview are flat.
/// </summary>
[ApiController]
public class InspectionsController : ControllerBase
{
    private readonly IInspectionService _service;

    public InspectionsController(IInspectionService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/inspections")]
    [RequirePermission(PermissionCodes.InspectionsView)]
    public async Task<ActionResult<IReadOnlyList<InspectionDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var inspections = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return inspections is null ? NotFound() : Ok(inspections);
    }

    [HttpGet("api/trailers/{trailerId:guid}/inspections")]
    [RequirePermission(PermissionCodes.InspectionsView)]
    public async Task<ActionResult<IReadOnlyList<InspectionDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var inspections = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return inspections is null ? NotFound() : Ok(inspections);
    }

    [HttpGet("api/inspections/due")]
    [RequirePermission(PermissionCodes.InspectionsView)]
    public async Task<ActionResult<IReadOnlyList<DueInspectionDto>>> ListDue(
        [FromQuery] int withinDays = 30, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListDueAsync(withinDays, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/inspections")]
    [RequirePermission(PermissionCodes.InspectionsCreate)]
    public async Task<ActionResult<InspectionDto>> CreateForVehicle(
        Guid vehicleId, CreateInspectionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/inspections")]
    [RequirePermission(PermissionCodes.InspectionsCreate)]
    public async Task<ActionResult<InspectionDto>> CreateForTrailer(
        Guid trailerId, CreateInspectionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/inspections/{id:guid}")]
    [RequirePermission(PermissionCodes.InspectionsEdit)]
    public async Task<ActionResult<InspectionDto>> Update(
        Guid id, UpdateInspectionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    public record CompleteInspectionResponse(InspectionDto Inspection, InspectionDto? FollowUp);

    [HttpPost("api/inspections/{id:guid}/complete")]
    [RequirePermission(PermissionCodes.InspectionsEdit)]
    public async Task<ActionResult<CompleteInspectionResponse>> Complete(
        Guid id, CompleteInspectionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CompleteAsync(id, request, cancellationToken);
        return result.Outcome switch
        {
            InspectionOperationOutcome.Success => Ok(new CompleteInspectionResponse(result.Inspection!, result.FollowUp)),
            InspectionOperationOutcome.NotFound => NotFound(),
            InspectionOperationOutcome.AlreadyCompleted => Conflict(new { message = "Deze keuring is al geregistreerd." }),
            InspectionOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    [HttpDelete("api/inspections/{id:guid}")]
    [RequirePermission(PermissionCodes.InspectionsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<InspectionDto> Handle(InspectionOperationResult result, bool created) => result.Outcome switch
    {
        InspectionOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Inspection),
        InspectionOperationOutcome.Success => Ok(result.Inspection),
        InspectionOperationOutcome.NotFound => NotFound(),
        InspectionOperationOutcome.OwnerNotFound => NotFound(),
        InspectionOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
