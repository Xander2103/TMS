using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFleetDashboardService
{
    /// <param name="withDeployability">UX-007: the fleet page's truthful "Beschikbaar"/"Niet inzetbaar" counts. Callers that
    /// never show them (the main dashboard reads only the due lists) skip the readiness sources.</param>
    Task<FleetDashboardDto> GetAsync(CancellationToken cancellationToken, bool withDeployability = true);
}
