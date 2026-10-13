using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Scanning.Services;

namespace TransportationService.Api.Modules.Scanning.Controllers;

/// <summary>
/// Wave 4 §3: standalone (trip-less) warehouse scans — Received, Moved, Staged and the
/// trip-less return check-in. Same pipeline, same ledger, same custody events as the
/// trip-scoped scanning endpoints.
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
    [RequirePermission(PermissionCodes.ScanningExecute)]
    public async Task<ActionResult<WarehouseScanFeedbackDto>> Submit(
        WarehouseScanRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SubmitAsync(request, cancellationToken));
}
