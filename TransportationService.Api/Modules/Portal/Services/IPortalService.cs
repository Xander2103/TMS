using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Qualifications.Dtos;

namespace TransportationService.Api.Modules.Portal.Services;

/// <summary>
/// Everything here is self-scoped: the employee is always resolved from the logged-in user's
/// employee link, never from a client-supplied id. Users without an employee link get null /
/// NoEmployeeLink — the portal simply has nothing to show them.
/// </summary>
public interface IPortalService
{
    Task<MyProfileDto?> GetMyProfileAsync(CancellationToken cancellationToken);

    Task<MyDashboardDto?> GetMyDashboardAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AbsenceDto>?> ListMyAbsencesAsync(CancellationToken cancellationToken);

    Task<PortalAbsenceResult> CreateMyAbsenceAsync(CreateAbsenceRequest request, CancellationToken cancellationToken);
    Task<EmployeeLeaveBalanceDto?> GetMyLeaveBalanceAsync(int year, CancellationToken cancellationToken);

    /// <summary>Withdraw an own, still pending request; decided absences are HR territory.</summary>
    Task<PortalAbsenceResult> CancelMyAbsenceAsync(Guid absenceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeQualificationDto>?> ListMyQualificationsAsync(CancellationToken cancellationToken);

    Task<(Stream Content, string FileName)?> OpenMyQualificationDocumentAsync(Guid qualificationId, CancellationToken cancellationToken);

    Task<PortalOperationResult> ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Own schedule days (shifts + absences + personal notes), for "Mijn planning".</summary>
    Task<IReadOnlyList<ScheduleDayDto>?> GetMyPlanningAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    // Personal calendar notes (self-service only; strictly scoped to the own employee).
    Task<IReadOnlyList<PersonalCalendarNoteDto>?> ListMyCalendarNotesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<PersonalCalendarNoteDto?> CreateMyCalendarNoteAsync(SavePersonalCalendarNoteRequest request, CancellationToken cancellationToken);
    Task<PersonalCalendarNoteDto?> UpdateMyCalendarNoteAsync(Guid id, SavePersonalCalendarNoteRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteMyCalendarNoteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Attach a supporting document to an own, still open absence request.</summary>
    Task<PortalAbsenceResult> AttachMyAbsenceDocumentAsync(
        Guid absenceId, string fileName, Stream content, CancellationToken cancellationToken);

    Task<(Stream Content, string FileName)?> OpenMyAbsenceDocumentAsync(Guid absenceId, CancellationToken cancellationToken);
}
