namespace TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Eligibility.Models;

public interface IDriverEligibilityService
{
    Task<EligibilityResult> CheckEligibilityAsync(Guid employeeId, DriverEligibilityRequest request, CancellationToken cancellationToken);
}
