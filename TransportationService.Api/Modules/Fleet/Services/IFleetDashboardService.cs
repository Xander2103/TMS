using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFleetDashboardService
{
    Task<FleetDashboardDto> GetAsync(CancellationToken cancellationToken);
}
