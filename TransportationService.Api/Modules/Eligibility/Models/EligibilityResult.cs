namespace TransportationService.Api.Modules.Eligibility.Models;

public record EligibilityCheckedQualification(string QualificationTypeCode, bool Satisfied, string? Reason);

public record EligibilityResult(
    bool IsEligible,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EligibilityCheckedQualification> CheckedQualifications);
