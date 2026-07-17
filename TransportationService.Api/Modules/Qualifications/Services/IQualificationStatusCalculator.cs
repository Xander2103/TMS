namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;

public interface IQualificationStatusCalculator
{
    /// <summary>
    /// Computes the *effective* status for display/eligibility purposes.
    /// Suspended/Rejected/Pending are authoritative (never overridden by dates).
    /// For Valid/ExpiringSoon/Expired, the stored Status is recomputed from
    /// ExpiryDate vs. evaluationDate and the tenant's warning window, so a
    /// row that was "Valid" yesterday correctly reads "Expired" today
    /// without a background job having to touch every row.
    /// </summary>
    QualificationStatus CalculateEffectiveStatus(EmployeeQualification qualification, DateOnly evaluationDate, int expiryWarningDays);
}
