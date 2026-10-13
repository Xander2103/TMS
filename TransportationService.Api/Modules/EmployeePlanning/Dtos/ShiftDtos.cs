using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.EmployeePlanning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Dtos;

public record CreateShiftRequest(
    Guid EmployeeId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    ShiftType Type,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes,
    bool Override = false);

public record UpdateShiftRequest(
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    ShiftType Type,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes,
    bool Override = false);

/// <summary>
/// Copies a whole week of shifts (as Draft) to a target week; collisions are skipped, never
/// overwritten. Scope = the employees of the planning selection (scalability wave 2026-10-07):
/// the list filters below resolve through the SAME employee filter as the grid, so "week
/// kopiëren" copies exactly the people on screen. Without any filter the whole tenant is copied
/// (the pre-wave behaviour); the legacy single <paramref name="EmployeeId"/>/<paramref name="DepartmentId"/>
/// keep working and narrow the scope further.
/// </summary>
public record CopyWeekRequest(
    DateOnly SourceWeekStart,
    DateOnly TargetWeekStart,
    Guid? EmployeeId,
    Guid? DepartmentId,
    IReadOnlyList<Guid>? DepartmentIds = null,
    IReadOnlyList<Guid>? JobFunctionIds = null,
    IReadOnlyList<Guid>? ContractTypeIds = null,
    IReadOnlyList<PlanningEmployeeStatus>? Statuses = null,
    bool DriversOnly = false,
    IReadOnlyList<Guid>? EmployeeIds = null)
{
    /// <summary>True when the request carries any of the selection filters (as opposed to the legacy whole-tenant copy).</summary>
    public bool HasSelectionFilters =>
        (DepartmentIds?.Count ?? 0) > 0 || (JobFunctionIds?.Count ?? 0) > 0 || (ContractTypeIds?.Count ?? 0) > 0
        || (Statuses?.Count ?? 0) > 0 || DriversOnly || (EmployeeIds?.Count ?? 0) > 0;

    public ScheduleQuery ToScheduleQuery(DateOnly from, DateOnly to) => new(
        from, to,
        DepartmentIds ?? [], JobFunctionIds ?? [], ContractTypeIds ?? [], Statuses ?? [], DriversOnly, EmployeeIds ?? []);
}

/// <summary>
/// Dienstverband-scope of the planning: the existing business value <c>Employee.IsActive</c>
/// (in dienst / uit dienst-inactief). No second status model — EmploymentStatus stays HR data.
/// </summary>
public enum PlanningEmployeeStatus
{
    Active,
    Inactive,
}

/// <summary>
/// The planning selection (scalability wave 2026-10-07). ONE definition shared by the grid, the
/// list view, "week kopiëren" and the Excel export, so no two features can disagree on who is
/// on screen. Semantics:
///
///  * within one dimension OR (two functions = either function);
///  * between dimensions AND (department AND function);
///  * <see cref="Statuses"/> is the scope (default: active employees only) and applies to the
///    group result, never to explicitly chosen people;
///  * <see cref="EmployeeIds"/> are EXPLICIT picks from the global search. With no group filter
///    they ARE the selection ("these two people"); combined with group filters they are pinned
///    on top of the group result ("the dispatchers plus Jan from Transport").
///
/// Filters are never a security boundary: tenant scoping and permissions are enforced by the
/// service/controller regardless of what is asked here.
/// </summary>
public record ScheduleQuery(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid> DepartmentIds,
    IReadOnlyList<Guid> JobFunctionIds,
    IReadOnlyList<Guid> ContractTypeIds,
    IReadOnlyList<PlanningEmployeeStatus> Statuses,
    bool DriversOnly,
    IReadOnlyList<Guid> EmployeeIds)
{
    public static ScheduleQuery Period(DateOnly from, DateOnly to) => new(from, to, [], [], [], [], false, []);

    /// <summary>Department / function / contract type / drivers-only: the dimensions that narrow the group.</summary>
    public bool HasGroupFilters =>
        DepartmentIds.Count > 0 || JobFunctionIds.Count > 0 || ContractTypeIds.Count > 0 || DriversOnly;

    public bool HasExplicitEmployees => EmployeeIds.Count > 0;

    /// <summary>Scope defaults to in-dienst only; an empty list never means "nobody".</summary>
    public IReadOnlyList<PlanningEmployeeStatus> EffectiveStatuses =>
        Statuses.Count == 0 ? [PlanningEmployeeStatus.Active] : Statuses;
}

/// <summary>One hit of the GLOBAL employee search of the planning: every employee the caller may
/// see in this tenant, regardless of the active planning filters. Presentation fields only.</summary>
public record PlanningEmployeeOptionDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? DepartmentName,
    IReadOnlyList<string> FunctionNames,
    bool IsActive);

/// <summary>Filter vocabulary for the planning toolbar, served under the planning permission so a
/// planner does not need the master-data view rights to filter by department or function.</summary>
public record PlanningFilterOptionsDto(
    IReadOnlyList<PlanningFilterOptionDto> Departments,
    IReadOnlyList<PlanningFilterOptionDto> JobFunctions,
    IReadOnlyList<PlanningFilterOptionDto> ContractTypes,
    /// <summary>Department of the caller's own employee record ("Mijn afdeling"); null without one.</summary>
    Guid? MyDepartmentId,
    /// <summary>True when at least one employee carries a driver profile ("Alle chauffeurs" shortcut).</summary>
    bool HasDrivers);

public record PlanningFilterOptionDto(Guid Id, string Name);

public record ShiftDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    int PlannedMinutes,
    ShiftType Type,
    ShiftStatus Status,
    string? WorkLocation,
    string? RoleLabel,
    string? Notes);

/// <summary>
/// One explainable schedule cell state. Shift statuses and absence outcomes share the same
/// vocabulary so the grid legend covers everything an employee's day can be.
/// </summary>
public enum ScheduleEntryState
{
    Draft,
    Planned,
    Confirmed,
    Standby,
    Training,
    LeaveRequested,
    LeaveApproved,
    LeaveRejected,
    Sick,
    Unavailable,
    Trip,
    TripCancelled,

    /// <summary>Personal calendar note of the employee (self-service only).</summary>
    Note,
}

public record ScheduleEntryDto(
    ScheduleEntryState State,
    Guid? ShiftId,
    Guid? AbsenceId,
    Guid? TripId,
    string SourceType,
    string Label,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    ShiftType? ShiftType,
    string? WorkLocation,
    string? VehicleSummary,
    string? StatusLabel,
    ConflictSeverity? ConflictSeverity = null,
    IReadOnlyList<string>? ConflictNotes = null,
    /// <summary>Planning colour: leave-type colour for absences, chosen colour for notes.</summary>
    string? Colour = null,
    Guid? NoteId = null);

public record ScheduleDayDto(DateOnly Date, IReadOnlyList<ScheduleEntryDto> Entries);

public record ScheduleRowDto(
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    string? DepartmentName,
    int PlannedMinutes,
    IReadOnlyList<ScheduleDayDto> Days,
    IReadOnlyList<string>? FunctionNames = null,
    string? ContractTypeName = null,
    bool IsActive = true,
    /// <summary>False for a row that is on screen only because it was explicitly picked and falls
    /// outside the active group filters ("Buiten huidige filters"). Never blocks the row.</summary>
    bool MatchesFilters = true);

/// <param name="TotalInScope">Employees in the tenant that fall within the status scope of the
/// query, before group filters ("38 van 214 medewerkers").</param>
public record ScheduleGridDto(
    DateOnly From, DateOnly To, IReadOnlyList<ScheduleRowDto> Rows, int TotalInScope = 0);

public enum ShiftOutcome
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed,
    Overlap,
    Conflict,
}

public record ShiftOperationResult(
    ShiftOutcome Outcome, ShiftDto? Shift, string? Error = null, int CopiedCount = 0, int SkippedCount = 0,
    IReadOnlyList<string>? Conflicts = null)
{
    public static ShiftOperationResult Success(ShiftDto shift) => new(ShiftOutcome.Success, shift);
    public static ShiftOperationResult Copied(int copied, int skipped) => new(ShiftOutcome.Success, null, null, copied, skipped);
    public static readonly ShiftOperationResult NotFound = new(ShiftOutcome.NotFound, null);
    public static ShiftOperationResult InvalidState(string error) => new(ShiftOutcome.InvalidState, null, error);
    public static ShiftOperationResult Invalid(string error) => new(ShiftOutcome.ValidationFailed, null, error);
    public static ShiftOperationResult OverlapError(string error) => new(ShiftOutcome.Overlap, null, error);
    public static ShiftOperationResult ConflictBlocked(IReadOnlyList<string> conflicts) =>
        new(ShiftOutcome.Conflict, null, "De shift botst met bestaande planning.", 0, 0, conflicts);
}
