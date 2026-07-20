using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.Profitability.Dtos;

/// <summary>Where the "used" revenue figure comes from — never merged into one ambiguous value.</summary>
public enum RevenueSource
{
    Invoiced,
    Agreed,
    None,
}

public record TripProfitabilityDto(
    Guid TripId,
    string TripNumber,
    DateOnly TripDate,
    TripStatus Status,
    string? DriverName,
    string? VehicleNumber,
    string? CustomerSummary,
    int OrderCount,
    int StopCount,
    int PackageCount,
    decimal? DistanceKm,
    bool DistanceIsActual,
    // Revenue, split by nature:
    decimal AgreedRevenue,
    decimal InvoicedRevenue,
    decimal PaidRevenue,
    decimal RevenueUsed,
    RevenueSource RevenueSourceUsed,
    // Costs, split by certainty:
    decimal ActualCost,
    decimal EstimatedCost,
    /// <summary>Per-type merge: actual where available, else estimated (the working total).</summary>
    decimal ProjectedCost,
    /// <summary>Core cost types with no line at all — the calculation is explicitly incomplete.</summary>
    IReadOnlyList<TripCostType> MissingCostTypes,
    decimal Margin,
    decimal? MarginPct,
    decimal? RevenuePerKm,
    decimal? CostPerKm,
    decimal? CostPerStop,
    decimal? CostPerPackage,
    bool IsFinalized);

public record ProfitabilitySummaryDto(
    int TripCount,
    decimal RevenueUsed,
    decimal ActualCost,
    decimal EstimatedCost,
    decimal ProjectedCost,
    decimal Margin,
    decimal? MarginPct,
    int TripsWithMissingData,
    int UnprofitableTrips);

public record ProfitabilityOverviewDto(
    DateOnly From,
    DateOnly To,
    ProfitabilitySummaryDto Summary,
    IReadOnlyList<TripProfitabilityDto> Trips);

public enum ProfitabilityDimension
{
    Customer,
    Driver,
    Vehicle,
    Week,
}

public record ProfitabilityGroupDto(
    string Key,
    string Label,
    int TripCount,
    decimal Revenue,
    decimal ProjectedCost,
    decimal Margin,
    decimal? MarginPct,
    /// <summary>True when costs were split across customers of shared trips (allocation, not booking).</summary>
    bool ContainsAllocatedCosts);

public record ProfitabilityExplanationLineDto(
    string Kind,
    string Description,
    decimal Amount,
    string? Phase,
    string? Source,
    bool IsManualOverride,
    string? OverrideReason);

/// <summary>Line-level explanation: every euro traceable to a revenue or cost line.</summary>
public record TripProfitabilityExplanationDto(
    Guid TripId,
    string TripNumber,
    IReadOnlyList<ProfitabilityExplanationLineDto> RevenueLines,
    IReadOnlyList<ProfitabilityExplanationLineDto> CostLines,
    IReadOnlyList<TripCostType> MissingCostTypes,
    string CalculationNote);
