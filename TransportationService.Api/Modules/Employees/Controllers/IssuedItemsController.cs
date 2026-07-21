using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

/// <summary>Issued-item templates (Settings) and per-employee "bedrijfsmiddelen" checklists.</summary>
[ApiController]
public class IssuedItemsController : ControllerBase
{
    private readonly IIssuedItemService _service;

    public IssuedItemsController(IIssuedItemService service)
    {
        _service = service;
    }

    // --- Templates ---

    [HttpGet("api/issued-item-templates")]
    [RequirePermission(PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IReadOnlyList<IssuedItemTemplateDto>>> ListTemplates(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListTemplatesAsync(includeInactive, cancellationToken));
    }

    [HttpPost("api/issued-item-templates")]
    [RequirePermission(PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemTemplateDto>> CreateTemplate(SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateTemplateAsync(request, cancellationToken));
    }

    [HttpPut("api/issued-item-templates/{id:guid}")]
    [RequirePermission(PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<ActionResult<IssuedItemTemplateDto>> UpdateTemplate(Guid id, SaveIssuedItemTemplateRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateTemplateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/issued-item-templates/{id:guid}")]
    [RequirePermission(PermissionCodes.IssuedItemsManageTemplates)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteTemplateAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    // --- Per-employee issued items ---

    [HttpGet("api/employees/{employeeId:guid}/issued-items")]
    [RequirePermission(PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage)]
    public async Task<ActionResult<IReadOnlyList<EmployeeIssuedItemDto>>> List(Guid employeeId, CancellationToken cancellationToken)
    {
        var items = await _service.ListForEmployeeAsync(employeeId, cancellationToken);
        return items is null ? NotFound() : Ok(items);
    }

    [HttpPost("api/employees/{employeeId:guid}/issued-items")]
    [RequirePermission(PermissionCodes.IssuedItemsManage)]
    public async Task<ActionResult<EmployeeIssuedItemDto>> Create(Guid employeeId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.UpsertAsync(employeeId, null, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    [HttpPut("api/employees/{employeeId:guid}/issued-items/{itemId:guid}")]
    [RequirePermission(PermissionCodes.IssuedItemsManage)]
    public async Task<ActionResult<EmployeeIssuedItemDto>> Update(Guid employeeId, Guid itemId, SaveEmployeeIssuedItemRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpsertAsync(employeeId, itemId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/employees/{employeeId:guid}/issued-items/{itemId:guid}")]
    [RequirePermission(PermissionCodes.IssuedItemsManage)]
    public async Task<IActionResult> Delete(Guid employeeId, Guid itemId, CancellationToken cancellationToken)
    {
        return await _service.DeleteItemAsync(employeeId, itemId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpGet("api/employees/{employeeId:guid}/issued-items/document")]
    [RequirePermission(PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage)]
    public async Task<IActionResult> Document(Guid employeeId, CancellationToken cancellationToken)
    {
        var pdf = await _service.BuildAcknowledgementAsync(employeeId, cancellationToken);
        return pdf is null ? NotFound() : File(pdf, "application/pdf", "ontvangstbewijs-bedrijfsmiddelen.pdf");
    }
}
