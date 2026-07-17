namespace TransportationService.Api.Modules.Eligibility.Dtos;

public record CheckEligibilityRequest(
    Guid EmployeeId, string? RequiredDrivingLicenceCategory, bool RequiresCode95, bool RequiresAdr,
    bool RequiresMedicalFitness, bool RequiresCraneCertificate, IReadOnlyList<string> RequiredAdditionalQualificationCodes,
    DateOnly PlannedStartDate, DateOnly PlannedEndDate);
