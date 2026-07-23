using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Hr.Controllers;

[ApiController]
[Route("api/employees/{employeeId:guid}/leave-balance")]
public class LeaveBalancesController : ControllerBase
{
    private readonly ILeaveBalanceService _service;

    public LeaveBalancesController(ILeaveBalanceService service)
    {
        _service = service;
    }

    private static int ResolveYear(int? year) => year ?? DateTime.UtcNow.Year;

    [HttpGet]
    [RequirePermission(PermissionCodes.LeaveBalancesView)]
    public async Task<ActionResult<EmployeeLeaveBalanceDto>> Get(Guid employeeId, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await _service.GetForEmployeeAsync(employeeId, ResolveYear(year), cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.LeaveBalancesManage)]
    public async Task<IActionResult> SetEntitlement(Guid employeeId, [FromQuery] int? year, SetLeaveEntitlementRequest request, CancellationToken cancellationToken)
    {
        await _service.SetEntitlementAsync(employeeId, ResolveYear(year), request, cancellationToken);
        return Ok(await _service.GetForEmployeeAsync(employeeId, ResolveYear(year), cancellationToken));
    }

    [HttpPost("adjustments")]
    [RequirePermission(PermissionCodes.LeaveBalancesAdjust)]
    public async Task<IActionResult> AddAdjustment(Guid employeeId, [FromQuery] int? year, AddLeaveAdjustmentRequest request, CancellationToken cancellationToken)
    {
        await _service.AddAdjustmentAsync(employeeId, ResolveYear(year), request, cancellationToken);
        return Ok(await _service.GetForEmployeeAsync(employeeId, ResolveYear(year), cancellationToken));
    }

    [HttpGet("adjustments")]
    [RequirePermission(PermissionCodes.LeaveBalancesView)]
    public async Task<ActionResult<IReadOnlyList<LeaveAdjustmentDto>>> GetAdjustments(
        Guid employeeId, [FromQuery] int? year, [FromQuery] Guid balanceTypeId, CancellationToken cancellationToken)
        => Ok(await _service.GetAdjustmentsAsync(employeeId, ResolveYear(year), balanceTypeId, cancellationToken));
}
