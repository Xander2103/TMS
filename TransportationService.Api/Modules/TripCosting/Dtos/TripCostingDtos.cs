using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.TripCosting.Dtos;

// --- Rate cards ---

public record CostRateSetDto(
    Guid Id,
    DateOnly EffectiveFrom,
    string? Name,
    decimal FuelPricePerLitre,
    decimal DefaultConsumptionLPer100Km,
    decimal VehicleCostPerKm,
    decimal VehicleCostPerHour,
    decimal DriverCostPerHour,
    decimal EmployerCostMultiplier,
    decimal MaintenanceCostPerKm,
    decimal DepreciationPerDay,
    decimal TrailerCostPerDay,
    decimal EquipmentCostPerDay,
    decimal DefaultTollPerTrip,
    int OvertimeThresholdMinutesPerDay,
    decimal OvertimeRateMultiplier,
    decimal WaitingTimeCostPerHour,
    decimal Co2KgPerLitreDiesel,
    decimal Co2KgPerLitreOther);

public record SaveCostRateSetRequest(
    DateOnly EffectiveFrom,
    string? Name,
    decimal FuelPricePerLitre,
    decimal DefaultConsumptionLPer100Km,
    decimal VehicleCostPerKm,
    decimal VehicleCostPerHour,
    decimal DriverCostPerHour,
    decimal EmployerCostMultiplier,
    decimal MaintenanceCostPerKm,
    decimal DepreciationPerDay,
    decimal TrailerCostPerDay,
    decimal EquipmentCostPerDay,
    decimal DefaultTollPerTrip,
    int OvertimeThresholdMinutesPerDay,
    decimal OvertimeRateMultiplier,
    decimal WaitingTimeCostPerHour,
    decimal Co2KgPerLitreDiesel,
    decimal Co2KgPerLitreOther);

// --- Trip costing ---

public record TripCostLineDto(
    Guid Id,
    TripCostPhase Phase,
    TripCostType CostType,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitRate,
    decimal Amount,
    string Source,
    bool IsManualOverride,
    string? OverrideReason,
    DateTime CalculatedAt);

public record TripOrderAllocationDto(
    Guid TransportOrderId,
    string OrderNumber,
    string CustomerName,
    Guid CustomerId,
    decimal Revenue,
    decimal AllocatedCost,
    decimal Profit,
    decimal? MarginPct);

public record TripProfitabilityDto(
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal? MarginPct,
    decimal? RevenuePerKm,
    decimal? CostPerKm,
    decimal? RevenuePerHour,
    decimal? CostPerHour,
    IReadOnlyList<TripOrderAllocationDto> PerOrder);

public record TripCostingDto(
    Guid TripId,
    string TripNumber,
    TripStatus TripStatus,
    bool IsFinalized,
    DateTime? FinalizedAt,
    decimal EstimatedTotal,
    decimal ActualTotal,
    decimal ProjectedTotal,
    decimal? FinalCost,
    decimal? PlannedDistanceKm,
    decimal? PlannedEmptyKm,
    decimal? ActualDistanceKm,
    decimal? ActualEmptyKm,
    IReadOnlyList<TripCostLineDto> Lines,
    TripProfitabilityDto? Profitability);

public record AddCostLineRequest(
    TripCostPhase Phase,
    TripCostType CostType,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitRate);

public record OverrideCostLineRequest(decimal Amount, string Reason);

public record UpdateTripActualsRequest(decimal? ActualDistanceKm, decimal? ActualEmptyKm);

public enum CostingOutcome
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed,
}

public record CostingOperationResult(CostingOutcome Outcome, TripCostingDto? Costing, string? Error = null)
{
    public static CostingOperationResult Success(TripCostingDto costing) => new(CostingOutcome.Success, costing);
    public static readonly CostingOperationResult NotFound = new(CostingOutcome.NotFound, null);
    public static CostingOperationResult InvalidState(string error) => new(CostingOutcome.InvalidState, null, error);
    public static CostingOperationResult Invalid(string error) => new(CostingOutcome.ValidationFailed, null, error);
}
