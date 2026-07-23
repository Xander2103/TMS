using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

/// <summary>
/// Inventory for issued items: reusable attribute definitions/options, template attributes,
/// variants, stock receipt/correction, movement history and current holders.
/// </summary>
[ApiController]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _service;

    public InventoryController(IInventoryService service)
    {
        _service = service;
    }

    // --- Attribute definitions (reusable master data) ---

    [HttpGet("api/issued-item-attributes")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IReadOnlyList<IssuedItemAttributeDefinitionDto>>> ListAttributes(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListAttributeDefinitionsAsync(includeInactive, cancellationToken));
    }

    [HttpPost("api/issued-item-attributes")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IssuedItemAttributeDefinitionDto>> CreateAttribute(
        SaveAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAttributeDefinitionAsync(request, cancellationToken));
    }

    [HttpPut("api/issued-item-attributes/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IssuedItemAttributeDefinitionDto>> UpdateAttribute(
        Guid id, SaveAttributeDefinitionRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAttributeDefinitionAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/issued-item-attributes/{id:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<IActionResult> DeleteAttribute(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAttributeDefinitionAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("api/issued-item-attributes/{id:guid}/options")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IssuedItemAttributeOptionDto>> AddOption(
        Guid id, SaveAttributeOptionRequest request, CancellationToken cancellationToken)
    {
        var option = await _service.AddAttributeOptionAsync(id, request, cancellationToken);
        return option is null ? NotFound() : Ok(option);
    }

    [HttpPut("api/issued-item-attributes/{id:guid}/options/{optionId:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IssuedItemAttributeOptionDto>> UpdateOption(
        Guid id, Guid optionId, SaveAttributeOptionRequest request, CancellationToken cancellationToken)
    {
        var option = await _service.UpdateAttributeOptionAsync(id, optionId, request, cancellationToken);
        return option is null ? NotFound() : Ok(option);
    }

    [HttpDelete("api/issued-item-attributes/{id:guid}/options/{optionId:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage)]
    public async Task<IActionResult> DeleteOption(Guid id, Guid optionId, CancellationToken cancellationToken)
    {
        return await _service.DeleteAttributeOptionAsync(id, optionId, cancellationToken) ? NoContent() : NotFound();
    }

    // --- Template detail, attributes, variants ---

    [HttpGet("api/issued-item-templates/{id:guid}/detail")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemTemplateDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _service.GetTemplateDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    public record SetTemplateAttributesRequest(IReadOnlyList<Guid> AttributeDefinitionIds);

    [HttpPut("api/issued-item-templates/{id:guid}/attributes")]
    [RequirePermission(PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemTemplateDetailDto>> SetAttributes(
        Guid id, SetTemplateAttributesRequest request, CancellationToken cancellationToken)
    {
        var detail = await _service.SetTemplateAttributesAsync(id, request.AttributeDefinitionIds, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("api/issued-item-templates/{id:guid}/variants")]
    [RequirePermission(PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemVariantDto>> CreateVariant(
        Guid id, SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var variant = await _service.CreateVariantAsync(id, request, cancellationToken);
        return variant is null ? NotFound() : Ok(variant);
    }

    [HttpPut("api/issued-item-templates/{id:guid}/variants/{variantId:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemVariantDto>> UpdateVariant(
        Guid id, Guid variantId, SaveVariantRequest request, CancellationToken cancellationToken)
    {
        var variant = await _service.UpdateVariantAsync(id, variantId, request, cancellationToken);
        return variant is null ? NotFound() : Ok(variant);
    }

    [HttpDelete("api/issued-item-templates/{id:guid}/variants/{variantId:guid}")]
    [RequirePermission(PermissionCodes.InventoryManage, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<IActionResult> DeleteVariant(Guid id, Guid variantId, CancellationToken cancellationToken)
    {
        return await _service.DeleteVariantAsync(id, variantId, cancellationToken) ? NoContent() : NotFound();
    }

    // --- Stock ---

    [HttpPost("api/issued-item-templates/{id:guid}/stock/receipts")]
    [RequirePermission(PermissionCodes.InventoryAdjust, PermissionCodes.InventoryManage)]
    public async Task<ActionResult<StockMovementDto>> ReceiveStock(
        Guid id, StockReceiptRequest request, CancellationToken cancellationToken)
    {
        var movement = await _service.ReceiveStockAsync(id, request, cancellationToken);
        return movement is null ? NotFound() : Ok(movement);
    }

    [HttpPost("api/issued-item-templates/{id:guid}/stock/corrections")]
    [RequirePermission(PermissionCodes.InventoryAdjust)]
    public async Task<ActionResult<StockMovementDto>> CorrectStock(
        Guid id, StockCorrectionRequest request, CancellationToken cancellationToken)
    {
        var movement = await _service.CorrectStockAsync(id, request, cancellationToken);
        return movement is null ? NotFound() : Ok(movement);
    }

    [HttpGet("api/issued-item-templates/{id:guid}/stock/movements")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.InventoryManage, PermissionCodes.InventoryAdjust)]
    public async Task<ActionResult<IReadOnlyList<StockMovementDto>>> ListMovements(Guid id, CancellationToken cancellationToken)
    {
        var movements = await _service.ListMovementsAsync(id, cancellationToken);
        return movements is null ? NotFound() : Ok(movements);
    }

    [HttpGet("api/issued-item-templates/{id:guid}/holders")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage)]
    public async Task<ActionResult<IReadOnlyList<CurrentHolderDto>>> ListHolders(Guid id, CancellationToken cancellationToken)
    {
        var holders = await _service.ListCurrentHoldersAsync(id, cancellationToken);
        return holders is null ? NotFound() : Ok(holders);
    }
}
