namespace TransportationService.Api.Modules.Eligibility.Models;

public record DriverEligibilityRequest(
    string? RequiredDrivingLicenceCategory, // "B" | "C" | "CE" | null
    bool RequiresCode95,
    bool RequiresAdr,
    bool RequiresMedicalFitness,
    bool RequiresCraneCertificate,
    IReadOnlyList<string> RequiredAdditionalQualificationCodes,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate);
