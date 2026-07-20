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
}
