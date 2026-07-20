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

    /// <summary>Attaching orders to a trip is separately permissioned (orders.assign).</summary>
    private async Task<bool> MayAssignOrdersAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        return await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.OrdersAssign, cancellationToken)
               || await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.OrdersManage, cancellationToken);
    }

    private ObjectResult AssignForbidden() => StatusCode(StatusCodes.Status403Forbidden,
        new { message = "Je hebt geen recht om opdrachten aan ritten te koppelen (orders.assign)." });

    [HttpPost]
    [RequirePermission(PermissionCodes.PlanningCreate)]
    public async Task<ActionResult<TripDetailDto>> Create(CreateTripRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderIds.Count > 0 && !await MayAssignOrdersAsync(cancellationToken))
        {
            return AssignForbidden();
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> Update(Guid id, UpdateTripRequest request, CancellationToken cancellationToken)
    {
        // Only require orders.assign when the set of attached orders actually changes,
        // so a planner without it can still adjust driver/vehicle/times.
        var current = await _service.GetByIdAsync(id, cancellationToken);
        if (current is not null)
        {
            var currentOrderIds = current.Orders.Select(o => o.TransportOrderId).OrderBy(x => x).ToList();
            var requestedOrderIds = request.OrderIds.Distinct().OrderBy(x => x).ToList();
            if (!currentOrderIds.SequenceEqual(requestedOrderIds) && !await MayAssignOrdersAsync(cancellationToken))
            {
                return AssignForbidden();
            }
        }

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

        // Releasing a trip that departs without all mandatory packages is guarded separately again.
        var releaseOverride = false;
        if (request.ReleaseOverride)
        {
            releaseOverride = _currentUserContext.CurrentUserId is { } releaseUserId
                && await _permissionService.UserHasPermissionAsync(
                    releaseUserId, PermissionCodes.WarehouseReleaseTrip, cancellationToken);
            if (!releaseOverride)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Je hebt geen recht om een rit met ontbrekende colli vrij te geven." });
            }
        }

        var result = await _service.ChangeStatusAsync(
            id, request.Status, allowOverride, releaseOverride, request.OverrideReason, request.Version, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>Overriding blocking conflicts on a Planned trip is separately guarded.</summary>
    private async Task<ObjectResult?> GuardOverrideAsync(bool requested, CancellationToken cancellationToken)
    {
        if (!requested)
        {
            return null;
        }

        var allowed = _currentUserContext.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(
                userId, PermissionCodes.PlanningOverrideRestriction, cancellationToken);
        return allowed
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Je hebt geen recht om planningsconflicten te overschrijven." });
    }

    // --- Targeted planning-center commands (drag-and-drop mutations) ---

    [HttpPost("{id:guid}/orders")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> AssignOrders(
        Guid id, AssignOrdersRequest request, CancellationToken cancellationToken)
    {
        if (!await MayAssignOrdersAsync(cancellationToken))
        {
            return AssignForbidden();
        }

        return Handle(await _service.AssignOrdersAsync(id, request, cancellationToken), created: false);
    }

    [HttpDelete("{id:guid}/orders/{orderId:guid}")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> RemoveOrder(
        Guid id, Guid orderId, [FromQuery] Guid? version, CancellationToken cancellationToken)
    {
        if (!await MayAssignOrdersAsync(cancellationToken))
        {
            return AssignForbidden();
        }

        return Handle(await _service.RemoveOrderAsync(id, orderId, version, cancellationToken), created: false);
    }

    [HttpPost("{id:guid}/orders/reorder")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> ReorderOrders(
        Guid id, ReorderTripOrdersRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.ReorderOrdersAsync(id, request, cancellationToken), created: false);
    }

    [HttpPut("{id:guid}/driver")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> AssignDriver(
        Guid id, AssignResourceRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.AssignDriverAsync(id, request, cancellationToken), created: false);
    }

    [HttpPut("{id:guid}/vehicle")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> AssignVehicle(
        Guid id, AssignResourceRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.AssignVehicleAsync(id, request, cancellationToken), created: false);
    }

    [HttpPut("{id:guid}/trailer")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> AssignTrailer(
        Guid id, AssignResourceRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.AssignTrailerAsync(id, request, cancellationToken), created: false);
    }

    [HttpPost("{id:guid}/reschedule")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripDetailDto>> Reschedule(
        Guid id, RescheduleTripRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.RescheduleAsync(id, request, cancellationToken), created: false);
    }

    /// <summary>Dry-run for drag-over feedback: which conflicts WOULD this assignment create?</summary>
    [HttpPost("{id:guid}/validate-assignment")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<ValidateAssignmentResultDto>> ValidateAssignment(
        Guid id, ValidateAssignmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ValidateAssignmentAsync(id, request, cancellationToken);
        if (result.Outcome == TripOperationOutcome.NotFound)
        {
            return NotFound();
        }

        var conflicts = result.Conflicts ?? [];
        return Ok(new ValidateAssignmentResultDto(conflicts, conflicts.Any(c => c.Blocking)));
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
        TripOperationOutcome.PackagesBlock =>
            Conflict(new { message = result.Error, packageReadiness = result.PackageReadiness }),
        // 409 with the CURRENT server state so the client can rebase its edit.
        TripOperationOutcome.StaleVersion =>
            Conflict(new { message = result.Error, staleVersion = true, current = result.Trip }),
        _ => Conflict(),
    };
}
