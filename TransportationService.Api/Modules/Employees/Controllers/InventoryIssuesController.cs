using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

/// <summary>
/// WH-H3/H4/H5: the warehouse issue/return desk — "Uitgeven", "Retour verwerken", open issuances
/// (who has what, overdue, inactive employees), searchable issuance history (item, code, serial,
/// employee) and the outstanding-equipment summary the HR offboarding dialog shows.
/// None of these need an HR employee permission: the employee picker returns number, name and
/// active status only.
/// </summary>
[ApiController]
[Route("api/inventory")]
public class InventoryIssuesController : ControllerBase
{
    private readonly IWarehouseIssueService _service;

    public InventoryIssuesController(IWarehouseIssueService service)
    {
        _service = service;
    }

    [HttpGet("employee-options")]
    [RequirePermission(PermissionCodes.InventoryIssue, PermissionCodes.InventoryReturn, PermissionCodes.InventoryLoansView)]
    public async Task<ActionResult<IReadOnlyList<InventoryEmployeeOptionDto>>> EmployeeOptions(
        [FromQuery] string? search = null, [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.SearchEmployeesAsync(search, includeInactive, cancellationToken));
    }

    [HttpGet("issues")]
    [RequirePermission(PermissionCodes.InventoryLoansView, PermissionCodes.InventoryIssue, PermissionCodes.InventoryReturn,
        PermissionCodes.IssuedItemsManage)]
    public async Task<ActionResult<IReadOnlyList<InventoryIssueRowDto>>> Issues(
        [FromQuery] string scope = "open", [FromQuery] string? search = null, [FromQuery] Guid? employeeId = null,
        [FromQuery] Guid? templateId = null, [FromQuery] string? serial = null, [FromQuery] bool overdueOnly = false,
        [FromQuery] bool inactiveEmployeesOnly = false, [FromQuery] bool returnRequiredOnly = false,
        [FromQuery] DateOnly? dueBefore = null, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListIssuesAsync(
            new InventoryIssueQuery(scope, search, employeeId, templateId, serial, overdueOnly, inactiveEmployeesOnly,
                returnRequiredOnly, dueBefore),
            cancellationToken));
    }

    [HttpPost("issues")]
    [RequirePermission(PermissionCodes.InventoryIssue)]
    public async Task<ActionResult<EmployeeIssuedItemDto>> Issue(WarehouseIssueRequest request, CancellationToken cancellationToken)
    {
        var issued = await _service.IssueAsync(request, cancellationToken);
        return issued is null ? NotFound() : Ok(issued);
    }

    [HttpPost("issues/{itemId:guid}/return")]
    [RequirePermission(PermissionCodes.InventoryReturn)]
    public async Task<ActionResult<EmployeeIssuedItemDto>> Return(
        Guid itemId, ReturnIssuedItemRequest request, CancellationToken cancellationToken)
    {
        var returned = await _service.ReturnAsync(itemId, request, cancellationToken);
        return returned is null ? NotFound() : Ok(returned);
    }

    /// <summary>WH-H5: open return-required items of one employee (the offboarding warning).</summary>
    [HttpGet("employees/{employeeId:guid}/outstanding")]
    [RequirePermission(PermissionCodes.EmployeesDeactivate, PermissionCodes.IssuedItemsView, PermissionCodes.InventoryLoansView,
        PermissionCodes.InventoryIssue, PermissionCodes.InventoryReturn)]
    public async Task<ActionResult<OutstandingEquipmentDto>> Outstanding(Guid employeeId, CancellationToken cancellationToken)
    {
        var outstanding = await _service.OutstandingForEmployeeAsync(employeeId, cancellationToken);
        return outstanding is null ? NotFound() : Ok(outstanding);
    }
}
