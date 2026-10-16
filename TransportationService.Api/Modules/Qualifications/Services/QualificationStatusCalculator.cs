namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;

public class QualificationStatusCalculator : IQualificationStatusCalculator
{
    public QualificationStatus CalculateEffectiveStatus(EmployeeQualification qualification, DateOnly evaluationDate, int expiryWarningDays)
    {
        if (qualification.Status is QualificationStatus.Suspended or QualificationStatus.Rejected)
        {
            return qualification.Status;
        }

        // Expiry is a fact about the document, independent of whether HR has verified it yet:
        // an unverified record past its expiry date reads as Expired (HR acceptance H3), so it
        // shows up under "Verlopen" instead of hiding as "in behandeling".
        if (qualification.ExpiryDate is { } expired && expired < evaluationDate)
        {
            return QualificationStatus.Expired;
        }

        if (qualification.Status == QualificationStatus.Pending)
        {
            return QualificationStatus.Pending;
        }

        if (qualification.ExpiryDate is not { } expiryDate)
        {
            return QualificationStatus.Valid;
        }

        if (expiryDate <= evaluationDate.AddDays(expiryWarningDays))
        {
            return QualificationStatus.ExpiringSoon;
        }

        return QualificationStatus.Valid;
    }
}
