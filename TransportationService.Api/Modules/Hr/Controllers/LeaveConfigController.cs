using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Hr.Controllers;

/// <summary>
/// Configurable leave-type and balance-type master data plus per-tenant leave settings.
/// Reads are gated on employees.view (used by pickers); writes on leave_types.manage /
/// leave_balances.manage.
/// </summary>
[ApiController]
[Route("api")]
public class LeaveConfigController : ControllerBase
{
    private readonly ILeaveBalanceService _service;

    public LeaveConfigController(ILeaveBalanceService service)
    {
        _service = service;
    }

    [HttpGet("leave-balance-types")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<IReadOnlyList<LeaveBalanceTypeDto>>> ListBalanceTypes(CancellationToken cancellationToken)
        => Ok(await _service.ListBalanceTypesAsync(cancellationToken));

    [HttpPost("leave-balance-types")]
    [RequirePermission(PermissionCodes.LeaveTypesManage)]
    public async Task<ActionResult<LeaveBalanceTypeDto>> CreateBalanceType(SaveLeaveBalanceTypeRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SaveBalanceTypeAsync(null, request, cancellationToken));

    [HttpPut("leave-balance-types/{id:guid}")]
    [RequirePermission(PermissionCodes.LeaveTypesManage)]
    public async Task<ActionResult<LeaveBalanceTypeDto>> UpdateBalanceType(Guid id, SaveLeaveBalanceTypeRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SaveBalanceTypeAsync(id, request, cancellationToken));

    [HttpGet("leave-types")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<IReadOnlyList<LeaveTypeDto>>> ListLeaveTypes(
        [FromQuery] bool activeOnly, [FromQuery] bool selfServiceOnly, CancellationToken cancellationToken)
        => Ok(await _service.ListLeaveTypesAsync(activeOnly, selfServiceOnly, cancellationToken));

    [HttpPost("leave-types")]
    [RequirePermission(PermissionCodes.LeaveTypesManage)]
    public async Task<ActionResult<LeaveTypeDto>> CreateLeaveType(SaveLeaveTypeRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SaveLeaveTypeAsync(null, request, cancellationToken));

    [HttpPut("leave-types/{id:guid}")]
    [RequirePermission(PermissionCodes.LeaveTypesManage)]
    public async Task<ActionResult<LeaveTypeDto>> UpdateLeaveType(Guid id, SaveLeaveTypeRequest request, CancellationToken cancellationToken)
        => Ok(await _service.SaveLeaveTypeAsync(id, request, cancellationToken));

    [HttpGet("hr/leave-settings")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<LeaveSettingsDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _service.GetSettingsAsync(cancellationToken));

    [HttpPut("hr/leave-settings")]
    [RequirePermission(PermissionCodes.LeaveBalancesManage)]
    public async Task<ActionResult<LeaveSettingsDto>> UpdateSettings(LeaveSettingsDto request, CancellationToken cancellationToken)
        => Ok(await _service.UpdateSettingsAsync(request, cancellationToken));
}
