namespace TransportationService.Api.Modules.Reporting.Dtos;

/// <summary>Shared filter for every KPI query; date range is mandatory and capped at 366 days.</summary>
public record KpiFilter(DateOnly From, DateOnly To, Guid? CustomerId, Guid? DriverId, Guid? VehicleId);

public record KpiCustomerRowDto(
    Guid CustomerId,
    string CustomerName,
    decimal Revenue,
    decimal AllocatedCost,
    decimal Profit,
    decimal? MarginPct);

public record KpiDriverKmRowDto(
    Guid DriverId,
    string DriverName,
    decimal Km,
    decimal Hours);

/// <summary>
/// All definitions live in docs/kpi-definitions.md and are unit-tested; every ratio is
/// zero-denominator-safe (null when undefined).
/// </summary>
public record KpiDashboardDto(
    // Financieel
    decimal RevenueToday,
    decimal RevenuePeriod,
    decimal ProfitToday,
    decimal ProfitPeriod,
    decimal? AverageMarginPct,
    decimal? ProfitPerTrip,
    int TripCount,
    // Vloot & kilometers
    decimal? VehicleUtilisationPct,
    decimal TotalKm,
    decimal EmptyKm,
    decimal? EmptyKmPct,
    decimal FuelLitres,
    decimal FuelCost,
    decimal Co2Kg,
    int OpenDamageCount,
    // Uitvoering
    decimal? DeliveryReliabilityPct,
    decimal? OnTimeArrivalPct,
    decimal? AvgEtaDeviationMinutes,
    int FailedDeliveries,
    int PartialDeliveries,
    int OpenExceptions,
    int CostOverrunTripCount,
    decimal? AvgCostOverrunPct,
    // Verdiepingen
    IReadOnlyList<KpiCustomerRowDto> TopCustomers,
    IReadOnlyList<KpiDriverKmRowDto> KmPerDriver);

public record TripProfitabilityRowDto(
    Guid TripId,
    string TripNumber,
    DateOnly TripDate,
    string? DriverName,
    string? VehicleNumber,
    string CustomerNames,
    decimal Revenue,
    decimal EstimatedCost,
    decimal ProjectedCost,
    decimal? FinalCost,
    decimal Profit,
    decimal? MarginPct,
    decimal TotalKm,
    decimal EmptyKm,
    string Status,
    bool IsFinalized);
