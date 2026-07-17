namespace TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Eligibility.Dtos;

public interface IEligibilityOverrideService
{
    Task<EligibilityOverrideDto> CreateAsync(Guid approvingUserId, CreateEligibilityOverrideRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<EligibilityOverrideDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
}
