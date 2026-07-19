using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Packages.Services;

namespace TransportationService.Api.Modules.Packages.Controllers;

/// <summary>
/// Warehouse loading surface: trips of the day with load completeness, and package search.
/// Everything here is warehouse-scoped — no HR, cost or profitability fields exist in
/// these DTOs by design.
/// </summary>
[ApiController]
public class WarehouseController : ControllerBase
{
    private readonly IWarehouseService _service;
    private readonly TimeProvider _timeProvider;

    public WarehouseController(IWarehouseService service, TimeProvider timeProvider)
    {
        _service = service;
        _timeProvider = timeProvider;
    }

    [HttpGet("api/warehouse/trips")]
    [RequirePermission(PermissionCodes.WarehouseView)]
    public async Task<ActionResult<IReadOnlyList<WarehouseTripDto>>> Trips(
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var day = date ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        return Ok(await _service.ListTripsAsync(day, cancellationToken));
    }

    [HttpGet("api/warehouse/packages")]
    [RequirePermission(PermissionCodes.WarehouseView)]
    public async Task<ActionResult<IReadOnlyList<WarehousePackageSearchRowDto>>> Search(
        [FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchPackagesAsync(search ?? string.Empty, cancellationToken));
    }
}
