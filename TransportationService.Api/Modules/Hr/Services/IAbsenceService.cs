using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Services;

public interface IAbsenceService
{
    /// <summary>Absences for one employee, newest first. Null when the employee is not in the tenant.</summary>
    Task<IReadOnlyList<AbsenceDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Range overview across all employees for planning; defaults to today .. today+60d.</summary>
    Task<IReadOnlyList<AbsenceDto>> ListAsync(
        DateOnly? from, DateOnly? to, AbsenceType? type, AbsenceStatus? status, CancellationToken cancellationToken);

    Task<AbsenceOperationResult> CreateForEmployeeAsync(Guid employeeId, CreateAbsenceRequest request, CancellationToken cancellationToken);

    /// <summary>Only requested absences can be edited; decided ones must be cancelled instead.</summary>
    Task<AbsenceOperationResult> UpdateAsync(Guid id, UpdateAbsenceRequest request, CancellationToken cancellationToken);

    /// <summary>Approve or reject a requested absence.</summary>
    Task<AbsenceOperationResult> DecideAsync(Guid id, DecideAbsenceRequest request, CancellationToken cancellationToken);

    /// <summary>Cancel a requested or approved absence (e.g. vacation withdrawn).</summary>
    Task<AbsenceOperationResult> CancelAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
