using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.TripCosting.Services;

public interface ICostRateService
{
    Task<IReadOnlyList<CostRateSetDto>> ListAsync(CancellationToken cancellationToken);

    Task<CostRateSetDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest rate card with EffectiveFrom on or before the date; null when none is configured.</summary>
    Task<CostRateSet?> GetForDateAsync(DateOnly date, CancellationToken cancellationToken);

    Task<(CostRateSetDto? Result, string? Error)> CreateAsync(SaveCostRateSetRequest request, CancellationToken cancellationToken);

    Task<(CostRateSetDto? Result, string? Error)> UpdateAsync(Guid id, SaveCostRateSetRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
