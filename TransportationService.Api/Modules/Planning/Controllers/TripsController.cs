using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;

namespace TransportationService.Api.Modules.Planning.Controllers;

[ApiController]
[Route("api/trips")]
public class TripsController : ControllerBase
{
    private readonly ITripService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public TripsController(
        ITripService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<IReadOnlyList<TripListItemDto>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] TripStatus? status,
        [FromQuery] Guid? driverId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(from, to, status, driverId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<TripDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var trip = await _service.GetByIdAsync(id, cancellationToken);
        return trip is null ? NotFound() : Ok(trip);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.PlanningCreate)]
    public async Task<ActionResult<TripDetailDto>> Create(CreateTripRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> Update(Guid id, UpdateTripRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>Dry-run of the conflict engine; never mutates.</summary>
    [HttpPost("{id:guid}/validate")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<TripDetailDto>> Validate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ValidateAsync(id, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> ChangeStatus(
        Guid id, ChangeTripStatusRequest request, CancellationToken cancellationToken)
    {
        // Overriding blocking conflicts is a separately guarded capability.
        var allowOverride = false;
        if (request.Override)
        {
            allowOverride = _currentUserContext.CurrentUserId is { } userId
                && await _permissionService.UserHasPermissionAsync(
                    userId, PermissionCodes.PlanningOverrideRestriction, cancellationToken);
            if (!allowOverride)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Je hebt geen recht om planningsconflicten te overschrijven." });
            }
        }

        var result = await _service.ChangeStatusAsync(id, request.Status, allowOverride, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Outcome switch
        {
            TripOperationOutcome.Success => NoContent(),
            TripOperationOutcome.NotFound => NotFound(),
            TripOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    private ActionResult<TripDetailDto> Handle(TripOperationResult result, bool created) => result.Outcome switch
    {
        TripOperationOutcome.Success when created =>
            CreatedAtAction(nameof(GetById), new { id = result.Trip!.Id }, result.Trip),
        TripOperationOutcome.Success => Ok(result.Trip),
        TripOperationOutcome.NotFound => NotFound(),
        TripOperationOutcome.InvalidReference => BadRequest(new { message = result.Error }),
        TripOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
        TripOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        TripOperationOutcome.ConflictsBlock =>
            Conflict(new { message = result.Error, conflicts = result.Conflicts }),
        _ => Conflict(),
    };
}
