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

/// <summary>Package XLSX reports; every workbook carries its criteria sheet.</summary>
[ApiController]
public class PackageReportsController : ControllerBase
{
    private readonly IPackageReportService _service;
    private readonly TimeProvider _timeProvider;

    public PackageReportsController(IPackageReportService service, TimeProvider timeProvider)
    {
        _service = service;
        _timeProvider = timeProvider;
    }

    [HttpGet("api/reports/packages")]
    [RequirePermission(PermissionCodes.PackageReportsExport)]
    public ActionResult<IReadOnlyList<string>> Keys() => Ok(_service.ReportKeys);

    [HttpGet("api/reports/packages/{report}")]
    [RequirePermission(PermissionCodes.PackageReportsExport)]
    public async Task<IActionResult> Download(
        string report, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var result = await _service.BuildAsync(report, from ?? today.AddDays(-30), to ?? today, cancellationToken);
        if (result is null)
        {
            return NotFound(new { message = "Onbekend rapport." });
        }
        return File(result.Value.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.FileName);
    }
}
