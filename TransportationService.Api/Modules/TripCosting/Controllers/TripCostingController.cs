using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Modules.TripCosting.Controllers;

/// <summary>
/// Trip cost lines, totals and profitability. Cost data never rides on the plain trip DTO:
/// this surface is separately permission-gated and profitability additionally requires
/// profitability.view.
/// </summary>
[ApiController]
public class TripCostingController : ControllerBase
{
    private readonly ITripCostingService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public TripCostingController(
        ITripCostingService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    private async Task<bool> MayViewProfitabilityAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.ProfitabilityView, cancellationToken);

    [HttpGet("api/trips/{tripId:guid}/costing")]
    [RequirePermission(PermissionCodes.TripCostsView, PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> Get(Guid tripId, CancellationToken cancellationToken)
    {
        var includeProfitability = await MayViewProfitabilityAsync(cancellationToken);
        var costing = await _service.GetAsync(tripId, includeProfitability, cancellationToken);
        return costing is null ? NotFound() : Ok(costing);
    }

    [HttpPost("api/trips/{tripId:guid}/costing/recalculate-estimate")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> RecalculateEstimate(Guid tripId, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.RecalculateAsync(tripId, TripCostPhase.Estimated, cancellationToken), cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/costing/recalculate-actual")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> RecalculateActual(Guid tripId, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.RecalculateAsync(tripId, TripCostPhase.Actual, cancellationToken), cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/costing/lines")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> AddLine(
        Guid tripId, AddCostLineRequest request, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.AddManualLineAsync(tripId, request, cancellationToken), cancellationToken);

    [HttpPut("api/trips/{tripId:guid}/costing/lines/{lineId:guid}/override")]
    [RequirePermission(PermissionCodes.TripCostsOverride)]
    public async Task<ActionResult<TripCostingDto>> OverrideLine(
        Guid tripId, Guid lineId, OverrideCostLineRequest request, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.OverrideLineAsync(tripId, lineId, request, cancellationToken), cancellationToken);

    [HttpDelete("api/trips/{tripId:guid}/costing/lines/{lineId:guid}")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> DeleteLine(
        Guid tripId, Guid lineId, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.DeleteLineAsync(tripId, lineId, cancellationToken), cancellationToken);

    [HttpPut("api/trips/{tripId:guid}/costing/actuals")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> UpdateActuals(
        Guid tripId, UpdateTripActualsRequest request, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.UpdateActualsAsync(tripId, request, cancellationToken), cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/costing/finalize")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<TripCostingDto>> Finalize(Guid tripId, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.FinalizeAsync(tripId, cancellationToken), cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/costing/reopen")]
    [RequirePermission(PermissionCodes.TripCostsOverride)]
    public async Task<ActionResult<TripCostingDto>> Reopen(Guid tripId, CancellationToken cancellationToken) =>
        await HandleAsync(() => _service.ReopenAsync(tripId, cancellationToken), cancellationToken);

    private async Task<ActionResult<TripCostingDto>> HandleAsync(
        Func<Task<CostingOperationResult>> action, CancellationToken cancellationToken)
    {
        var result = await action();
        if (result.Outcome != CostingOutcome.Success)
        {
            return result.Outcome switch
            {
                CostingOutcome.NotFound => NotFound(),
                CostingOutcome.InvalidState => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error }),
            };
        }

        // Trim profitability for callers without the extra permission.
        if (!await MayViewProfitabilityAsync(cancellationToken))
        {
            return Ok(result.Costing! with { Profitability = null });
        }

        return Ok(result.Costing);
    }
}
