using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// FL-H3: the vehicle/trailer timeline. Readable by every fleet viewer; lines that carry amounts
/// (maintenance cost, repair cost, fuel amount, lease instalment) only include those amounts for
/// fleet_finance.view holders — the server shapes the payload, the UI hides nothing.
/// </summary>
[ApiController]
public class FleetHistoryController : ControllerBase
{
    private readonly IFleetHistoryService _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public FleetHistoryController(IFleetHistoryService service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
    {
        _service = service;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    private async Task<bool> HasFinanceViewAsync(CancellationToken cancellationToken)
        => _currentUser.CurrentUserId is { } userId
           && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.FleetFinanceView, cancellationToken);

    [HttpGet("api/vehicles/{vehicleId:guid}/history")]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<IReadOnlyList<FleetHistoryEventDto>>> ForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var rows = await _service.GetVehicleHistoryAsync(vehicleId, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpGet("api/trailers/{trailerId:guid}/history")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<IReadOnlyList<FleetHistoryEventDto>>> ForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var rows = await _service.GetTrailerHistoryAsync(trailerId, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }
}
