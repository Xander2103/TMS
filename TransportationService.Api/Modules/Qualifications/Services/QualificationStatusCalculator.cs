namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;

public class QualificationStatusCalculator : IQualificationStatusCalculator
{
    public QualificationStatus CalculateEffectiveStatus(EmployeeQualification qualification, DateOnly evaluationDate, int expiryWarningDays)
    {
        if (qualification.Status is QualificationStatus.Suspended or QualificationStatus.Rejected or QualificationStatus.Pending)
        {
            return qualification.Status;
        }

        if (qualification.ExpiryDate is not { } expiryDate)
        {
            return QualificationStatus.Valid;
        }

        if (expiryDate < evaluationDate)
        {
            return QualificationStatus.Expired;
        }

        if (expiryDate <= evaluationDate.AddDays(expiryWarningDays))
        {
            return QualificationStatus.ExpiringSoon;
        }

        return QualificationStatus.Valid;
    }
}
