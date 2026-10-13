using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Services;

namespace TransportationService.Api.Modules.Warehousing.Controllers;

/// <summary>Warehouse and dock master data (addresses live on the linked Location).</summary>
[ApiController]
[Route("api/warehouses")]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseAdminService _service;

    public WarehousesController(IWarehouseAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await _service.GetAsync(id, cancellationToken);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> Create(SaveWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> Update(
        Guid id, SaveWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await _service.UpdateAsync(id, request, cancellationToken);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpPost("{id:guid}/docks")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> CreateDock(
        Guid id, SaveDockRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await _service.SaveDockAsync(id, null, request, cancellationToken);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpPut("{id:guid}/docks/{dockId:guid}")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> UpdateDock(
        Guid id, Guid dockId, SaveDockRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await _service.SaveDockAsync(id, dockId, request, cancellationToken);
        return warehouse is null ? NotFound() : Ok(warehouse);
    }

    [HttpDelete("{id:guid}/docks/{dockId:guid}")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<IActionResult> DeleteDock(Guid id, Guid dockId, CancellationToken cancellationToken)
    {
        return await _service.DeleteDockAsync(id, dockId, cancellationToken) ? NoContent() : NotFound();
    }

    // --- Wave 4 §1: storage locations (zone → position). Scanners must be able to list
    // target locations, hence scanning.execute on the read. ---

    [HttpGet("{id:guid}/locations")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage, PermissionCodes.ScanningExecute)]
    public async Task<ActionResult<IReadOnlyList<WarehouseLocationDto>>> ListLocations(Guid id, CancellationToken cancellationToken)
    {
        var locations = await _service.ListLocationsAsync(id, cancellationToken);
        return locations is null ? NotFound() : Ok(locations);
    }

    [HttpPost("{id:guid}/locations")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseLocationDto>> CreateLocation(
        Guid id, SaveWarehouseLocationRequest request, CancellationToken cancellationToken)
    {
        var location = await _service.SaveLocationAsync(id, null, request, cancellationToken);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpPut("{id:guid}/locations/{locationId:guid}")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<ActionResult<WarehouseLocationDto>> UpdateLocation(
        Guid id, Guid locationId, SaveWarehouseLocationRequest request, CancellationToken cancellationToken)
    {
        var location = await _service.SaveLocationAsync(id, locationId, request, cancellationToken);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpDelete("{id:guid}/locations/{locationId:guid}")]
    [RequirePermission(PermissionCodes.WarehouseManage)]
    public async Task<IActionResult> DeleteLocation(Guid id, Guid locationId, CancellationToken cancellationToken)
    {
        return await _service.DeleteLocationAsync(id, locationId, cancellationToken) ? NoContent() : NotFound();
    }

    // --- Wave 4 §4: trace + overview (read-only) ---

    [HttpGet("/api/warehouse/trace")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage, PermissionCodes.ScanningView, PermissionCodes.ScanningExecute)]
    public async Task<ActionResult<WarehouseTraceDto>> Trace(
        [FromQuery] string barcode, [FromServices] IWarehouseTraceService trace, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest();
        }

        var result = await trace.TraceAsync(barcode, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/overview")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage, PermissionCodes.ScanningView, PermissionCodes.ScanningExecute)]
    public async Task<ActionResult<WarehouseOverviewDto>> Overview(
        Guid id, [FromServices] IWarehouseTraceService trace, CancellationToken cancellationToken)
    {
        var overview = await trace.GetOverviewAsync(
            id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return overview is null ? NotFound() : Ok(overview);
    }

    // --- Wave 5 §2: pallet-day derivation from the storage clock (read-only) ---

    [HttpGet("/api/customers/{customerId:guid}/storage")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseManage, PermissionCodes.TariffsView, PermissionCodes.InvoicesView)]
    public async Task<ActionResult<StorageBillingDto>> CustomerStorage(
        Guid customerId, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromServices] IStorageBillingService billing, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return BadRequest();
        }

        return Ok(await billing.ComputeAsync(customerId, from, to, cancellationToken));
    }
}
