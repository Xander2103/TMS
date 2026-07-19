using TransportationService.Api.Modules.Reporting.Dtos;

namespace TransportationService.Api.Modules.Reporting.Services;

public interface IKpiQueryService
{
    Task<KpiDashboardDto> GetDashboardAsync(KpiFilter filter, CancellationToken cancellationToken);

    /// <summary>Per-trip profitability rows for the drill-down report and the XLSX exports.</summary>
    Task<IReadOnlyList<TripProfitabilityRowDto>> GetTripProfitabilityAsync(
        KpiFilter filter, CancellationToken cancellationToken);

    /// <summary>Every customer (uncapped) with revenue-share cost allocation — the dashboard shows the top 10.</summary>
    Task<IReadOnlyList<KpiCustomerRowDto>> GetCustomerProfitabilityAsync(
        KpiFilter filter, CancellationToken cancellationToken);

    /// <summary>Every driver (uncapped) with kilometres and hours.</summary>
    Task<IReadOnlyList<KpiDriverKmRowDto>> GetDriverEffortAsync(KpiFilter filter, CancellationToken cancellationToken);

    /// <summary>Per-vehicle hours/km/utilisation against the Mon–Fri × 8h convention.</summary>
    Task<IReadOnlyList<KpiVehicleUtilisationRowDto>> GetVehicleUtilisationAsync(
        KpiFilter filter, CancellationToken cancellationToken);
}
