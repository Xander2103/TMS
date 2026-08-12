using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Services;

namespace TransportationService.Api.Modules.Planning.Controllers;

/// <summary>
/// Driver workflow: "my trips" plus per-stop execution. Dispatchers with planning.edit may
/// operate any trip (corrections); drivers are restricted to their own assignments.
/// </summary>
[ApiController]
public class TripExecutionController : ControllerBase
{
    private readonly ITripExecutionService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly TransportationService.Api.Modules.Eta.Services.IEtaService _etaService;

    public TripExecutionController(
        ITripExecutionService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext,
        TransportationService.Api.Modules.Eta.Services.IEtaService etaService)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
        _etaService = etaService;
    }

    /// <summary>Stop progress shifts the remaining ETAs; recalculation is cheap and idempotent.</summary>
    private async Task RefreshEtaAsync(Guid tripId, ExecutionResult result, CancellationToken cancellationToken)
    {
        if (result.Outcome == ExecutionOutcome.Success)
        {
            await _etaService.RecalculateTripAsync(tripId, cancellationToken);
        }
    }

    private async Task<bool> IsDispatcherAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.PlanningEdit, cancellationToken);

    [HttpGet("api/my/trips")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<MyTripDto>>> MyTrips(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListMyTripsAsync(from, to, cancellationToken));
    }

    [HttpGet("api/trips/{tripId:guid}/execution")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<TripExecutionDto>> GetExecution(Guid tripId, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        return Handle(await _service.GetExecutionAsync(tripId, restrict, cancellationToken));
    }

    /// <summary>Generic controlled transition through the stop-status machine.</summary>
    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/transition")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<TripExecutionDto>> Transition(
        Guid tripId, Guid stopId, TransitionStopRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.TransitionAsync(tripId, stopId, request, restrict, cancellationToken);
        await RefreshEtaAsync(tripId, result, cancellationToken);
        return Handle(result);
    }

    [HttpGet("api/trips/{tripId:guid}/stops/{stopId:guid}/history")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<StopStatusHistoryDto>>> StopHistory(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.GetStopHistoryAsync(tripId, stopId, restrict, cancellationToken);
        return result.Outcome switch
        {
            ExecutionOutcome.Success => Ok(result.History),
            ExecutionOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
            _ => NotFound(),
        };
    }

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/arrive")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<TripExecutionDto>> Arrive(Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.ArriveAsync(tripId, stopId, restrict, cancellationToken);
        await RefreshEtaAsync(tripId, result, cancellationToken);
        return Handle(result);
    }

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/complete")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<TripExecutionDto>> Complete(
        Guid tripId, Guid stopId, CompleteStopRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        // Completing with unresolved packages is a separately guarded capability.
        var allowPackageOverride = _currentUserContext.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.ScanningOverride, cancellationToken);
        var result = await _service.CompleteAsync(tripId, stopId, request, restrict, allowPackageOverride, cancellationToken);
        await RefreshEtaAsync(tripId, result, cancellationToken);
        return Handle(result);
    }

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/skip")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<TripExecutionDto>> Skip(
        Guid tripId, Guid stopId, SkipStopRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.SkipAsync(tripId, stopId, request, restrict, cancellationToken);
        await RefreshEtaAsync(tripId, result, cancellationToken);
        return Handle(result);
    }

    private ActionResult<TripExecutionDto> Handle(ExecutionResult result) => result.Outcome switch
    {
        ExecutionOutcome.Success => Ok(result.Execution),
        ExecutionOutcome.NotFound => NotFound(),
        ExecutionOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        ExecutionOutcome.InvalidState => BadRequest(new { message = result.Error }),
        ExecutionOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
