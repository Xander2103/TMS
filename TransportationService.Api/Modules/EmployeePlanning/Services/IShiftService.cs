using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

public interface IShiftService
{
    /// <summary>Schedule grid: shifts merged with absences into explainable day states.</summary>
    Task<ScheduleGridDto> GetScheduleAsync(
        DateOnly from, DateOnly to, Guid? departmentId, Guid? employeeId, CancellationToken cancellationToken);

    Task<ShiftDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Blocking schedule conflicts (trips, approved absences) refuse the save with outcome
    /// Conflict unless the request asks to override AND the caller may (allowConflictOverride).
    /// </summary>
    Task<ShiftOperationResult> CreateAsync(
        CreateShiftRequest request, bool allowConflictOverride, CancellationToken cancellationToken);

    Task<ShiftOperationResult> UpdateAsync(
        Guid id, UpdateShiftRequest request, bool allowConflictOverride, CancellationToken cancellationToken);

    /// <summary>Draft -> Planned -> Confirmed; Confirmed falls back to Planned, Planned to Draft.</summary>
    Task<ShiftOperationResult> ChangeStatusAsync(Guid id, ShiftStatus status, CancellationToken cancellationToken);

    Task<ShiftOperationResult> CopyWeekAsync(CopyWeekRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Own schedule for the portal (self-scoped by employee id).</summary>
    Task<IReadOnlyList<ScheduleDayDto>> GetEmployeeScheduleAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
