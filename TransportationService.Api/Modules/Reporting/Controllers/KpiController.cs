using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Reporting.Services;

namespace TransportationService.Api.Modules.Reporting.Controllers;

/// <summary>Management KPI read models. Financial drill-down additionally needs profitability.view.</summary>
[ApiController]
[Route("api/kpi")]
public class KpiController : ControllerBase
{
    private const int MaxRangeDays = 366;

    private readonly IKpiQueryService _service;
    private readonly IKpiExportService _exportService;
    private readonly IAuditService _auditService;

    public KpiController(IKpiQueryService service, IKpiExportService exportService, IAuditService auditService)
    {
        _service = service;
        _exportService = exportService;
        _auditService = auditService;
    }

    private static string? ValidateRange(DateOnly from, DateOnly to) =>
        to < from ? "De einddatum ligt voor de begindatum."
        : to.DayNumber - from.DayNumber > MaxRangeDays ? $"Kies een periode van maximaal {MaxRangeDays} dagen."
        : null;

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCodes.KpiView)]
    public async Task<ActionResult<KpiDashboardDto>> Dashboard(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? customerId, [FromQuery] Guid? driverId, [FromQuery] Guid? vehicleId,
        CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
        {
            return BadRequest(new { message = error });
        }

        return Ok(await _service.GetDashboardAsync(
            new KpiFilter(from, to, customerId, driverId, vehicleId), cancellationToken));
    }

    [HttpGet("trip-profitability")]
    [RequirePermission(PermissionCodes.ProfitabilityView)]
    public async Task<ActionResult<IReadOnlyList<TripProfitabilityRowDto>>> TripProfitability(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? customerId, [FromQuery] Guid? driverId, [FromQuery] Guid? vehicleId,
        CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
        {
            return BadRequest(new { message = error });
        }

        return Ok(await _service.GetTripProfitabilityAsync(
            new KpiFilter(from, to, customerId, driverId, vehicleId), cancellationToken));
    }

    [HttpGet("export")]
    [RequirePermission(PermissionCodes.KpiExport)]
    public async Task<IActionResult> Export(
        [FromQuery] string report,
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? customerId, [FromQuery] Guid? driverId, [FromQuery] Guid? vehicleId,
        CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
        {
            return BadRequest(new { message = error });
        }

        var result = await _exportService.BuildAsync(
            report, new KpiFilter(from, to, customerId, driverId, vehicleId), cancellationToken);
        if (result is null)
        {
            return BadRequest(new
            {
                message = $"Onbekend rapport '{report}'. Beschikbaar: {string.Join(", ", _exportService.ReportKeys)}.",
            });
        }

        await _auditService.RecordExportAsync(
            $"kpi:{report}", new { from, to, customerId, driverId, vehicleId }, cancellationToken);

        return File(result.Value.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.Value.FileName);
    }
}
