using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.EmployeePlanning.Controllers;

/// <summary>
/// Personnel planning (shifts), separate from transport trip planning. Viewing and managing
/// carry distinct permissions so HR/planning can be scoped apart from dispatch.
/// </summary>
[ApiController]
public class EmployeePlanningController : ControllerBase
{
    private readonly IShiftService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public EmployeePlanningController(
        IShiftService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    /// <summary>Overriding blocking schedule conflicts is a separately guarded capability.</summary>
    private async Task<bool> MayOverrideConflictsAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(
            userId, PermissionCodes.EmployeePlanningConflictOverride, cancellationToken);

    private ObjectResult OverrideForbidden() => StatusCode(StatusCodes.Status403Forbidden,
        new { message = "Je hebt geen recht om planningsconflicten te overschrijven (employee_planning.conflict_override)." });

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
        var allowOverride = false;
        if (request.Override)
        {
            allowOverride = await MayOverrideConflictsAsync(cancellationToken);
            if (!allowOverride)
            {
                return OverrideForbidden();
            }
        }

        return Handle(await _service.CreateAsync(request, allowOverride, cancellationToken));
    }

    [HttpPut("api/shifts/{id:guid}")]
    [RequirePermission(PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<ShiftDto>> Update(Guid id, UpdateShiftRequest request, CancellationToken cancellationToken)
    {
        var allowOverride = false;
        if (request.Override)
        {
            allowOverride = await MayOverrideConflictsAsync(cancellationToken);
            if (!allowOverride)
            {
                return OverrideForbidden();
            }
        }

        return Handle(await _service.UpdateAsync(id, request, allowOverride, cancellationToken));
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
        ShiftOutcome.Conflict => Conflict(new { message = result.Error, conflicts = result.Conflicts }),
        ShiftOutcome.InvalidState => BadRequest(new { message = result.Error }),
        ShiftOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
