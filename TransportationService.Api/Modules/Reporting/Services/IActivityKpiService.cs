using TransportationService.Api.Modules.Reporting.Dtos;

namespace TransportationService.Api.Modules.Reporting.Services;

public interface IActivityKpiService
{
    /// <summary>Activity-based KPI rows (P11): counts/revenue per activity type plus totals,
    /// per-category rollup and the pallet-day total for the period.</summary>
    Task<ActivityKpiReportDto> GetActivityKpisAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
