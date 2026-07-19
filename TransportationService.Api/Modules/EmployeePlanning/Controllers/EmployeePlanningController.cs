using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.EmployeePlanning.Controllers;

/// <summary>
/// Personnel planning (shifts), separate from transport trip planning. Viewing and managing
/// carry distinct permissions so HR/planning can be scoped apart from dispatch.
/// </summary>
[ApiController]
public class EmployeePlanningController : ControllerBase
{
    private readonly IShiftService _service;

    public EmployeePlanningController(IShiftService service)
    {
        _service = service;
    }

    [HttpGet("api/employee-planning")]
    [RequirePermission(PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ScheduleGridDto>> Schedule(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId, [FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            return BadRequest(new { message = "Kies een geldige periode van maximaal 62 dagen." });
        }

        return Ok(await _service.GetScheduleAsync(from, to, departmentId, employeeId, cancellationToken));
    }

    [HttpGet("api/shifts/{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ShiftDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var shift = await _service.GetByIdAsync(id, cancellationToken);
        return shift is null ? NotFound() : Ok(shift);
    }

    [HttpPost("api/shifts")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ShiftDto>> Create(CreateShiftRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPut("api/shifts/{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ShiftDto>> Update(Guid id, UpdateShiftRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken));
    }

    public record ChangeShiftStatusRequest(ShiftStatus Status);

    [HttpPost("api/shifts/{id:guid}/status")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ShiftDto>> ChangeStatus(
        Guid id, ChangeShiftStatusRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.ChangeStatusAsync(id, request.Status, cancellationToken));
    }

    public record CopyWeekResponse(int CopiedCount, int SkippedCount);

    [HttpPost("api/shifts/copy-week")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<CopyWeekResponse>> CopyWeek(CopyWeekRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CopyWeekAsync(request, cancellationToken);
        return result.Outcome switch
        {
            ShiftOutcome.Success => Ok(new CopyWeekResponse(result.CopiedCount, result.SkippedCount)),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    [HttpDelete("api/shifts/{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<ShiftDto> Handle(ShiftOperationResult result) => result.Outcome switch
    {
        ShiftOutcome.Success => Ok(result.Shift),
        ShiftOutcome.NotFound => NotFound(),
        ShiftOutcome.Overlap => Conflict(new { message = result.Error }),
        ShiftOutcome.InvalidState => BadRequest(new { message = result.Error }),
        ShiftOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
