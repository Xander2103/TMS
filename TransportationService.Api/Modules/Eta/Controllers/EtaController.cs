using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Eta.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Eta.Controllers;

/// <summary>
/// Internal ETA foundation. Reads recalculate on the fly (idempotent); drivers may report a
/// delay, dispatchers override per stop. Explicitly a sequential raming, not route optimisation.
/// </summary>
[ApiController]
public class EtaController : ControllerBase
{
    private readonly IEtaService _service;

    public EtaController(IEtaService service)
    {
        _service = service;
    }

    [HttpGet("api/trips/{tripId:guid}/eta")]
    [RequirePermission(PermissionCodes.PlanningView, PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<TripEtaDto>> Get(Guid tripId, CancellationToken cancellationToken)
    {
        var result = await _service.RecalculateTripAsync(tripId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    public record SetDelayRequest(int Minutes, string? Reason);

    [HttpPost("api/trips/{tripId:guid}/delay")]
    [RequirePermission(PermissionCodes.PlanningEdit, PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<TripEtaDto>> SetDelay(
        Guid tripId, SetDelayRequest request, CancellationToken cancellationToken)
    {
        if (request.Minutes is < 0 or > 24 * 60)
        {
            return BadRequest(new { message = "De vertraging moet tussen 0 en 1440 minuten liggen." });
        }

        var result = await _service.SetTripDelayAsync(tripId, request.Minutes, request.Reason, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    public record OverrideEtaRequest(DateTime Eta, string? Note);

    [HttpPut("api/trips/{tripId:guid}/stops/{stopId:guid}/eta-override")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripEtaDto>> Override(
        Guid tripId, Guid stopId, OverrideEtaRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.OverrideStopEtaAsync(tripId, stopId, request.Eta, request.Note, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("api/trips/{tripId:guid}/stops/{stopId:guid}/eta-override")]
    [RequirePermission(PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<TripEtaDto>> ClearOverride(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var result = await _service.ClearStopEtaOverrideAsync(tripId, stopId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("api/trips/{tripId:guid}/stops/{stopId:guid}/eta-history")]
    [RequirePermission(PermissionCodes.PlanningView, PermissionCodes.PlanningEdit)]
    public async Task<ActionResult<IReadOnlyList<StopEtaHistoryDto>>> History(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var history = await _service.GetStopEtaHistoryAsync(tripId, stopId, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }
}
