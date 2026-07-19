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

    /// <summary>Approve or reject a requested/under-review absence.</summary>
    Task<AbsenceOperationResult> DecideAsync(Guid id, DecideAbsenceRequest request, CancellationToken cancellationToken);

    /// <summary>Marks a requested absence as under review (visible to the employee).</summary>
    Task<AbsenceOperationResult> StartReviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sends the request back to the employee with a mandatory note and optional alternative dates.</summary>
    Task<AbsenceOperationResult> RequestChangesAsync(Guid id, RequestAbsenceChangesRequest request, CancellationToken cancellationToken);

    /// <summary>HR-only note; never surfaces in the portal.</summary>
    Task<AbsenceOperationResult> SetInternalNoteAsync(Guid id, string? note, CancellationToken cancellationToken);

    /// <summary>Planning conflicts, overlapping colleagues and balance info next to a request.</summary>
    Task<AbsenceReviewContextDto?> GetReviewContextAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Attaches a supporting document (pdf/jpg/png); replaces an existing attachment.</summary>
    Task<AbsenceOperationResult> AttachDocumentAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken);

    Task<(Stream Content, string FileName)?> OpenDocumentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Cancel a requested or approved absence (e.g. vacation withdrawn).</summary>
    Task<AbsenceOperationResult> CancelAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
