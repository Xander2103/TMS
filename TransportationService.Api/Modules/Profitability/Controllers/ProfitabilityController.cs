using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Profitability.Dtos;
using TransportationService.Api.Modules.Profitability.Services;

namespace TransportationService.Api.Modules.Profitability.Controllers;

/// <summary>
/// Operational/commercial profitability analysis (read models over existing financial data).
/// Sensitive by nature: everything requires profitability.view; exports are gated separately.
/// </summary>
[ApiController]
[Route("api/profitability")]
public class ProfitabilityController : ControllerBase
{
    private readonly IProfitabilityQueryService _queryService;
    private readonly IProfitabilityExportService _exportService;

    public ProfitabilityController(
        IProfitabilityQueryService queryService,
        IProfitabilityExportService exportService)
    {
        _queryService = queryService;
        _exportService = exportService;
    }

    [HttpGet("trips")]
    [RequirePermission(PermissionCodes.ProfitabilityView)]
    public async Task<ActionResult<ProfitabilityOverviewDto>> Trips(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        return Ok(await _queryService.GetOverviewAsync(from, to, customerId, cancellationToken));
    }

    [HttpGet("grouped")]
    [RequirePermission(PermissionCodes.ProfitabilityView)]
    public async Task<ActionResult<IReadOnlyList<ProfitabilityGroupDto>>> Grouped(
        [FromQuery] ProfitabilityDimension dimension,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        return Ok(await _queryService.GetGroupedAsync(dimension, from, to, cancellationToken));
    }

    [HttpGet("trips/{tripId:guid}/explanation")]
    [RequirePermission(PermissionCodes.ProfitabilityView)]
    public async Task<ActionResult<TripProfitabilityExplanationDto>> Explanation(
        Guid tripId, CancellationToken cancellationToken)
    {
        var explanation = await _queryService.GetExplanationAsync(tripId, cancellationToken);
        return explanation is null ? NotFound() : Ok(explanation);
    }

    [HttpGet("export")]
    [RequirePermission(PermissionCodes.ProfitabilityExport)]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var (content, fileName) = await _exportService.BuildAsync(from, to, cancellationToken);
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
