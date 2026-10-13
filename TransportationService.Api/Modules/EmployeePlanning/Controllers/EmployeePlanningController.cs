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
    /// <summary>Upper bound per id list: keeps the IN-clauses and URLs bounded for any tenant size.</summary>
    public const int MaxFilterIds = 200;
    private const int MaxPeriodDays = 62;

    private readonly IShiftService _service;
    private readonly IPlanningEmployeeDirectoryService _directory;
    private readonly IEmployeePlanningExportService _exportService;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public EmployeePlanningController(
        IShiftService service,
        IPlanningEmployeeDirectoryService directory,
        IEmployeePlanningExportService exportService,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _directory = directory;
        _exportService = exportService;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    /// <summary>
    /// Selection parameters of the grid, list view and export (one binder for all three). Lists
    /// bind as repeated query keys (<c>departmentIds=a&amp;departmentIds=b</c>); the legacy single
    /// <c>departmentId</c>/<c>employeeId</c> keep working and fold into the lists.
    /// </summary>
    public sealed class ScheduleQueryParameters
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public Guid? DepartmentId { get; set; }
        public Guid? EmployeeId { get; set; }
        public Guid[] DepartmentIds { get; set; } = [];
        public Guid[] JobFunctionIds { get; set; } = [];
        public Guid[] ContractTypeIds { get; set; } = [];
        public PlanningEmployeeStatus[] Statuses { get; set; } = [];
        public bool DriversOnly { get; set; }
        public Guid[] EmployeeIds { get; set; } = [];

        public string? Validate()
        {
            if (To < From || To.DayNumber - From.DayNumber > MaxPeriodDays)
            {
                return $"Kies een geldige periode van maximaal {MaxPeriodDays} dagen.";
            }

            if (DepartmentIds.Length > MaxFilterIds || JobFunctionIds.Length > MaxFilterIds
                || ContractTypeIds.Length > MaxFilterIds || EmployeeIds.Length > MaxFilterIds)
            {
                return $"Maximaal {MaxFilterIds} waarden per filter.";
            }

            return null;
        }

        public ScheduleQuery ToQuery()
        {
            var departments = DepartmentIds.ToList();
            if (DepartmentId is { } d && !departments.Contains(d))
            {
                departments.Add(d);
            }

            var employees = EmployeeIds.ToList();
            if (EmployeeId is { } e && !employees.Contains(e))
            {
                employees.Add(e);
            }

            return new ScheduleQuery(
                From, To,
                departments.Distinct().ToList(),
                JobFunctionIds.Distinct().ToList(),
                ContractTypeIds.Distinct().ToList(),
                Statuses.Distinct().ToList(),
                DriversOnly,
                employees.Distinct().ToList());
        }
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
        [FromQuery] ScheduleQueryParameters parameters, CancellationToken cancellationToken)
    {
        if (parameters.Validate() is { } error)
        {
            return BadRequest(new { message = error });
        }

        return Ok(await _service.GetScheduleAsync(parameters.ToQuery(), cancellationToken));
    }

    /// <summary>
    /// GLOBAL employee search for the planning toolbar: every employee of the tenant the caller
    /// may plan, independent of the active planning filters (those only decide which rows are
    /// shown, never who can be found). Presentation fields only — no confidential HR data.
    /// </summary>
    [HttpGet("api/employee-planning/employees")]
    [RequirePermission(PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<IReadOnlyList<PlanningEmployeeOptionDto>>> SearchEmployees(
        [FromQuery] string? search, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        return Ok(await _directory.SearchAsync(
            search, limit ?? PlanningEmployeeDirectoryService.DefaultLimit, cancellationToken));
    }

    [HttpGet("api/employee-planning/filter-options")]
    [RequirePermission(PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage)]
    public async Task<ActionResult<PlanningFilterOptionsDto>> FilterOptions(CancellationToken cancellationToken)
    {
        return Ok(await _directory.GetFilterOptionsAsync(cancellationToken));
    }

    /// <summary>Excel export of exactly the selection the grid shows (same parameters, same query).</summary>
    [HttpGet("api/employee-planning/export")]
    [RequirePermission(PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage)]
    public async Task<IActionResult> Export(
        [FromQuery] ScheduleQueryParameters parameters, CancellationToken cancellationToken)
    {
        if (parameters.Validate() is { } error)
        {
            return BadRequest(new { message = error });
        }

        var (content, fileName) = await _exportService.BuildAsync(parameters.ToQuery(), cancellationToken);
        return File(content, EmployeePlanningExportService.ContentType, fileName);
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
