using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/trailers")]
public class TrailersController : ControllerBase
{
    private readonly ITrailerService _service;
    private readonly IMaintenancePolicyService _maintenancePolicies;

    public TrailersController(ITrailerService service, IMaintenancePolicyService maintenancePolicies)
    {
        _service = service;
        _maintenancePolicies = maintenancePolicies;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<PagedResult<TrailerListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] TrailerOperationalStatus? status,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(search, status, isActive, categoryId, PageRequest.Of(page, pageSize), cancellationToken, sort, dir);
        return Ok(result);
    }

    [HttpGet("options")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<IReadOnlyList<TrailerOptionDto>>> Options(
        [FromQuery] DateOnly? date, [FromServices] IFleetReadinessService readinessService, CancellationToken cancellationToken)
    {
        var options = await _service.GetOptionsAsync(cancellationToken);
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var readiness = await readinessService.EvaluateTrailersAsync(options.Select(o => o.Id).ToList(), day, cancellationToken);
        return Ok(options.Select(o => readiness.TryGetValue(o.Id, out var r)
            ? o with { Readiness = r.Level, ReadinessSummary = r.Level == ReadinessLevel.Deployable ? null : r.Summary }
            : o).ToList());
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<TrailerDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var trailer = await _service.GetByIdAsync(id, cancellationToken);
        return trailer is null ? NotFound() : Ok(trailer);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TrailersCreate)]
    public async Task<ActionResult<TrailerDetailDto>> Create(CreateTrailerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        if (result.Outcome == TrailerOperationOutcome.Success)
        {
            // Plan the initial maintenance/inspection from the applicable policy (idempotent).
            await _maintenancePolicies.ApplyDefaultsAsync(
                FleetAssetKind.Trailer, result.Trailer!.Id, result.Trailer.CategoryId, currentOdometerKm: 0, cancellationToken);
        }

        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersEdit)]
    public async Task<ActionResult<TrailerDetailDto>> Update(Guid id, UpdateTrailerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/active")]
    [RequirePermission(PermissionCodes.TrailersEdit)]
    public async Task<IActionResult> SetActive(Guid id, SetTrailerActiveRequest request, CancellationToken cancellationToken)
    {
        return await _service.SetActiveAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Outcome switch
        {
            FleetDeleteOutcome.Deleted => NoContent(),
            FleetDeleteOutcome.Refused => Conflict(new { message = result.Message, references = result.References }),
            _ => NotFound(),
        };
    }

    /// <summary>FL-B1/M8: what still references the trailer (delete guard + deactivation warning).</summary>
    [HttpGet("{id:guid}/references")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<VehicleReferencesDto>> References(Guid id, CancellationToken cancellationToken)
    {
        var references = await _service.GetReferencesAsync(id, cancellationToken);
        return references is null ? NotFound() : Ok(references);
    }

    /// <summary>FL-M7: recent trips of the trailer (newest first), e.g. to link a damage report.</summary>
    [HttpGet("{id:guid}/trips")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<IReadOnlyList<TripReferenceDto>>> Trips(Guid id, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var trips = await _service.ListRecentTripsAsync(id, limit ?? 30, cancellationToken);
        return trips is null ? NotFound() : Ok(trips);
    }

    private ActionResult<TrailerDetailDto> Handle(TrailerOperationResult result, bool created) => result.Outcome switch
    {
        TrailerOperationOutcome.Success when created => CreatedAtAction(nameof(GetById), new { id = result.Trailer!.Id }, result.Trailer),
        TrailerOperationOutcome.Success => Ok(result.Trailer),
        TrailerOperationOutcome.NotFound => NotFound(),
        TrailerOperationOutcome.DuplicateLicensePlate => Conflict(new { message = result.Message ?? "Er bestaat al een oplegger met dit kenteken." }),
        TrailerOperationOutcome.DuplicateVin => Conflict(new { message = result.Message ?? "Er bestaat al een oplegger met dit chassisnummer (VIN)." }),
        TrailerOperationOutcome.InvalidReference => BadRequest(new { message = "De gekoppelde categorie bestaat niet." }),
        _ => Conflict(),
    };
}
