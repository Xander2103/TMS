using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Services;

namespace TransportationService.Api.Modules.Warehousing.Controllers;

/// <summary>Dock planning board and appointment lifecycle (/dock-planning).</summary>
[ApiController]
[Route("api/dock-appointments")]
public class DockPlanningController : ControllerBase
{
    private readonly IDockPlanningService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public DockPlanningController(
        IDockPlanningService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    [HttpGet("board")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseSchedule)]
    public async Task<ActionResult<DockBoardDto>> Board(
        [FromQuery] Guid warehouseId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var board = await _service.GetBoardAsync(
            warehouseId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return board is null ? NotFound() : Ok(board);
    }

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCodes.WarehouseView, PermissionCodes.WarehouseSchedule)]
    public async Task<ActionResult<WarehouseDashboardDto>> Dashboard(
        [FromQuery] Guid warehouseId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var dashboard = await _service.GetDashboardAsync(
            warehouseId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return dashboard is null ? NotFound() : Ok(dashboard);
    }

    /// <summary>Overriding blocking dock conflicts is separately guarded.</summary>
    private async Task<ObjectResult?> GuardOverrideAsync(bool requested, CancellationToken cancellationToken)
    {
        if (!requested)
        {
            return null;
        }

        var allowed = _currentUserContext.CurrentUserId is { } userId
            && await _permissionService.UserHasPermissionAsync(
                userId, PermissionCodes.WarehouseConflictOverride, cancellationToken);
        return allowed
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Je hebt geen recht om dockconflicten te overschrijven." });
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.WarehouseSchedule)]
    public async Task<ActionResult<DockAppointmentDto>> Create(
        SaveDockAppointmentRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.WarehouseSchedule)]
    public async Task<ActionResult<DockAppointmentDto>> Update(
        Guid id, SaveDockAppointmentRequest request, CancellationToken cancellationToken)
    {
        if (await GuardOverrideAsync(request.Override, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        return Handle(await _service.UpdateAsync(id, request, cancellationToken));
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.WarehouseSchedule)]
    public async Task<ActionResult<DockAppointmentDto>> ChangeStatus(
        Guid id, ChangeDockAppointmentStatusRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.ChangeStatusAsync(id, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.WarehouseSchedule)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] Guid? version, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, version, cancellationToken);
        return result.Outcome switch
        {
            DockOperationOutcome.Success => NoContent(),
            DockOperationOutcome.NotFound => NotFound(),
            DockOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            DockOperationOutcome.StaleVersion =>
                Conflict(new { message = result.Error, staleVersion = true, current = result.Appointment }),
            _ => Conflict(),
        };
    }

    private ActionResult<DockAppointmentDto> Handle(DockOperationResult result) => result.Outcome switch
    {
        DockOperationOutcome.Success => Ok(result.Appointment),
        DockOperationOutcome.NotFound => NotFound(),
        DockOperationOutcome.InvalidReference => BadRequest(new { message = result.Error }),
        DockOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
        DockOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        DockOperationOutcome.ConflictsBlock => Conflict(new { message = result.Error, conflicts = result.Conflicts }),
        DockOperationOutcome.StaleVersion =>
            Conflict(new { message = result.Error, staleVersion = true, current = result.Appointment }),
        _ => Conflict(),
    };
}
