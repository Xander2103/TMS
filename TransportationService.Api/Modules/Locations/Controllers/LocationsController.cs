using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;

namespace TransportationService.Api.Modules.Locations.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _service;

    public LocationsController(ILocationService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<PagedResult<LocationListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] LocationType? type,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? customerId,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(search, type, isActive, customerId, sort, dir, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("options")]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<IReadOnlyList<LocationOptionDto>>> Options(
        [FromQuery] LocationType? type, [FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetOptionsAsync(type, customerId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.LocationsView)]
    public async Task<ActionResult<LocationDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var location = await _service.GetByIdAsync(id, cancellationToken);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.LocationsCreate)]
    public async Task<ActionResult<LocationDetailDto>> Create(CreateLocationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Code en naam zijn verplicht." });
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<ActionResult<LocationDetailDto>> Update(Guid id, UpdateLocationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Code en naam zijn verplicht." });
        }

        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/active")]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<IActionResult> SetActive(Guid id, SetLocationActiveRequest request, CancellationToken cancellationToken)
    {
        return await _service.SetActiveAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/defaults")]
    [RequirePermission(PermissionCodes.LocationsEdit)]
    public async Task<ActionResult<LocationDetailDto>> SetDefaults(
        Guid id, SetLocationDefaultsRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.SetDefaultsAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.LocationsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<LocationDetailDto> Handle(LocationOperationResult result, bool created) => result.Outcome switch
    {
        LocationOperationOutcome.Success when created => CreatedAtAction(nameof(GetById), new { id = result.Location!.Id }, result.Location),
        LocationOperationOutcome.Success => Ok(result.Location),
        LocationOperationOutcome.NotFound => NotFound(),
        LocationOperationOutcome.DuplicateCode => Conflict(new { message = "Er bestaat al een locatie met deze code." }),
        LocationOperationOutcome.InvalidCoordinates => BadRequest(new { message = "Ongeldige coördinaten (breedtegraad -90..90, lengtegraad -180..180)." }),
        LocationOperationOutcome.InvalidReference => BadRequest(new { message = "De gekoppelde klant bestaat niet." }),
        _ => Conflict(),
    };
}
