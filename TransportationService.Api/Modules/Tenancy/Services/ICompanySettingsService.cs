using TransportationService.Api.Modules.Tenancy.Dtos;

namespace TransportationService.Api.Modules.Tenancy.Services;

public interface ICompanySettingsService
{
    Task<CompanySettingsDto> GetAsync(CancellationToken cancellationToken);

    Task<CompanySettingsDto> UpdateAsync(UpdateCompanySettingsRequest request, CancellationToken cancellationToken);
}
