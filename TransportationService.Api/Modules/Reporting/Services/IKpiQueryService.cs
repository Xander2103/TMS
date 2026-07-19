using TransportationService.Api.Modules.Reporting.Dtos;

namespace TransportationService.Api.Modules.Reporting.Services;

public interface IKpiQueryService
{
    Task<KpiDashboardDto> GetDashboardAsync(KpiFilter filter, CancellationToken cancellationToken);

    /// <summary>Per-trip profitability rows for the drill-down report and the XLSX exports.</summary>
    Task<IReadOnlyList<TripProfitabilityRowDto>> GetTripProfitabilityAsync(
        KpiFilter filter, CancellationToken cancellationToken);
}
