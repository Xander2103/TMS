using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Scanning.Services;

namespace TransportationService.Api.Modules.Scanning.Controllers;

/// <summary>
/// Wave 4 §3: standalone (trip-less) warehouse scans — Received, Moved, Staged and the
/// trip-less return check-in. Same pipeline, same ledger, same custody events as the
/// trip-scoped scanning endpoints. A warehouse station, so it needs a warehouse permission:
/// scanning.execute alone is the driver's trip-scoped right and must not reach every package of
/// the tenant by barcode (Security H1 class).
/// </summary>
[ApiController]
[Route("api/warehouse/scans")]
public class WarehouseScansController : ControllerBase
{
    private readonly IWarehouseScanService _service;

    public WarehouseScansController(IWarehouseScanService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseScanFeedbackDto>> Submit(
        WarehouseScanRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SubmitAsync(request, cancellationToken));
}
