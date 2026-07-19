using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/vehicles")]
public class VehiclesController : ControllerBase
{
    private readonly IVehicleService _service;
    private readonly IFleetAssignmentService _assignmentService;

    public VehiclesController(IVehicleService service, IFleetAssignmentService assignmentService)
    {
        _service = service;
        _assignmentService = assignmentService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<PagedResult<VehicleListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] VehicleOperationalStatus? status,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(search, status, isActive, categoryId, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("options")]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<IReadOnlyList<VehicleOptionDto>>> Options(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetOptionsAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<VehicleDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await _service.GetByIdAsync(id, cancellationToken);
        return vehicle is null ? NotFound() : Ok(vehicle);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.VehiclesCreate)]
    public async Task<ActionResult<VehicleDetailDto>> Create(CreateVehicleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.VehiclesEdit)]
    public async Task<ActionResult<VehicleDetailDto>> Update(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.VehiclesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/assignments/fixed-driver")]
    [RequirePermission(PermissionCodes.VehiclesEdit)]
    public Task<ActionResult<VehicleDetailDto>> SetFixedDriver(Guid id, AssignVehicleDriverRequest request, CancellationToken cancellationToken)
        => SetDriverAsync(id, AssignmentKind.Fixed, request, cancellationToken);

    [HttpPut("{id:guid}/assignments/current-driver")]
    [RequirePermission(PermissionCodes.VehiclesEdit)]
    public Task<ActionResult<VehicleDetailDto>> SetCurrentDriver(Guid id, AssignVehicleDriverRequest request, CancellationToken cancellationToken)
        => SetDriverAsync(id, AssignmentKind.Current, request, cancellationToken);

    private async Task<ActionResult<VehicleDetailDto>> SetDriverAsync(
        Guid id, AssignmentKind kind, AssignVehicleDriverRequest request, CancellationToken cancellationToken)
    {
        var result = await _assignmentService.SetVehicleDriverAsync(id, kind, request.DriverId, request.ReplaceExisting, cancellationToken);
        return result.Outcome switch
        {
            AssignmentOutcome.Success => Ok(await _service.GetByIdAsync(id, cancellationToken)),
            AssignmentOutcome.NotFound => NotFound(),
            AssignmentOutcome.InvalidReference => BadRequest(new { message = "De gekozen chauffeur bestaat niet." }),
            AssignmentOutcome.Conflict => Conflict(new
            {
                message = $"{result.Conflict!.DriverName} is al gekoppeld aan voertuig {result.Conflict.VehicleLabel}.",
                conflict = result.Conflict,
            }),
            _ => Conflict(),
        };
    }

    private ActionResult<VehicleDetailDto> Handle(VehicleOperationResult result, bool created) => result.Outcome switch
    {
        VehicleOperationOutcome.Success when created => CreatedAtAction(nameof(GetById), new { id = result.Vehicle!.Id }, result.Vehicle),
        VehicleOperationOutcome.Success => Ok(result.Vehicle),
        VehicleOperationOutcome.NotFound => NotFound(),
        VehicleOperationOutcome.DuplicateLicensePlate => Conflict(new { message = "Er bestaat al een voertuig met dit kenteken." }),
        VehicleOperationOutcome.InvalidReference => BadRequest(new { message = "Eén of meer gekoppelde records (categorie of chauffeur) bestaan niet." }),
        _ => Conflict(),
    };
}
