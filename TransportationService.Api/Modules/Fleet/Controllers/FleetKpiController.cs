using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Vehicle/trailer KPIs. Operational values (trips, km, litres, downtime, incidents) are served to
/// any fleet viewer; monetary values (fuel cost, cost per km, maintenance and damage cost) only to
/// fleet_finance.view holders (FL-M11 — the server shapes the DTO, the UI no longer has to hide them).
/// </summary>
[ApiController]
public class FleetKpiController : ControllerBase
{
    private readonly IFleetKpiService _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public FleetKpiController(IFleetKpiService service, ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
    {
        _service = service;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/kpi")]
    [RequirePermission(PermissionCodes.KpiView, PermissionCodes.VehiclesView)]
    public async Task<ActionResult<FleetKpiDto>> Vehicle(
        Guid vehicleId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var kpi = await _service.GetVehicleKpiAsync(vehicleId, start, end, cancellationToken);
        return kpi is null ? NotFound() : Ok(await ShapeAsync(kpi, cancellationToken));
    }

    [HttpGet("api/trailers/{trailerId:guid}/kpi")]
    [RequirePermission(PermissionCodes.KpiView, PermissionCodes.TrailersView)]
    public async Task<ActionResult<FleetKpiDto>> Trailer(
        Guid trailerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var kpi = await _service.GetTrailerKpiAsync(trailerId, start, end, cancellationToken);
        return kpi is null ? NotFound() : Ok(await ShapeAsync(kpi, cancellationToken));
    }

    private async Task<FleetKpiDto> ShapeAsync(FleetKpiDto kpi, CancellationToken cancellationToken)
    {
        var financeView = _currentUser.CurrentUserId is { } userId
            && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.FleetFinanceView, cancellationToken);
        return financeView ? kpi : FleetKpiShaping.WithoutFinancials(kpi);
    }

    /// <summary>Defaults to the last 365 days; caps the window at ~2 years.</summary>
    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddYears(-1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber > 366 * 2)
        {
            start = end.AddYears(-2);
        }

        return (start, end);
    }
}

/// <summary>FL-M11: strips every monetary KPI value (unit EUR / EUR/km) for non-finance callers.</summary>
public static class FleetKpiShaping
{
    public static FleetKpiDto WithoutFinancials(FleetKpiDto kpi) =>
        kpi with { Values = kpi.Values.Where(v => !IsFinancial(v)).ToList() };

    public static bool IsFinancial(FleetKpiValueDto value) =>
        value.Unit.StartsWith("EUR", StringComparison.OrdinalIgnoreCase)
        || value.Key.Contains("cost", StringComparison.OrdinalIgnoreCase);
}
