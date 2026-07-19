using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Qualifications.Dtos;

namespace TransportationService.Api.Modules.Portal.Dtos;

/// <summary>Own, non-confidential profile: no national register number, no banking data.</summary>
public record MyProfileDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string? MobilePhone,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City,
    string? CountryCode,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    DateOnly EmploymentStartDate,
    string EmploymentStatus,
    string? DepartmentName,
    IReadOnlyList<string> JobFunctions,
    bool IsDriver,
    string? DriverNumber);

public record MyDashboardDto(
    string FirstName,
    int UnreadNotifications,
    int OpenAbsenceRequests,
    int UpcomingApprovedAbsences,
    int ExpiringQualifications,
    bool IsDriver,
    int TripsToday,
    int TripsThisWeek);

public record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);

public enum PortalOutcome
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed,
    NoEmployeeLink,
}

public record PortalOperationResult(PortalOutcome Outcome, string? Error = null)
{
    public static readonly PortalOperationResult Success = new(PortalOutcome.Success);
    public static readonly PortalOperationResult NotFound = new(PortalOutcome.NotFound);
    public static readonly PortalOperationResult NoEmployeeLink = new(PortalOutcome.NoEmployeeLink,
        "Er is geen personeelsdossier gekoppeld aan dit account.");
    public static PortalOperationResult InvalidState(string error) => new(PortalOutcome.InvalidState, error);
    public static PortalOperationResult Invalid(string error) => new(PortalOutcome.ValidationFailed, error);
}

public record PortalAbsenceResult(PortalOutcome Outcome, AbsenceDto? Absence, string? Error = null)
{
    public static PortalAbsenceResult Success(AbsenceDto absence) => new(PortalOutcome.Success, absence);
    public static readonly PortalAbsenceResult NotFound = new(PortalOutcome.NotFound, null);
    public static readonly PortalAbsenceResult NoEmployeeLink = new(PortalOutcome.NoEmployeeLink, null,
        "Er is geen personeelsdossier gekoppeld aan dit account.");
    public static PortalAbsenceResult InvalidState(string error) => new(PortalOutcome.InvalidState, null, error);
    public static PortalAbsenceResult Invalid(string error) => new(PortalOutcome.ValidationFailed, null, error);
}

/// <summary>Marker aliases so the controller reads naturally.</summary>
public static class PortalAliases
{
    public static IReadOnlyList<EmployeeQualificationDto> NoQualifications { get; } = [];
}
