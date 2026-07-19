using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.TripCosting.Entities;

/// <summary>
/// Effective-dated tenant rate card for trip costing. The set applicable to a trip is the
/// newest EffectiveFrom on or before the trip date — historical calculations stay intact
/// because cost lines snapshot the resolved quantities and rates at calculation time.
/// </summary>
public class CostRateSet : AuditableTenantEntity
{
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Optional label, e.g. "Tarieven 2026 H2".</summary>
    public string? Name { get; set; }

    public decimal FuelPricePerLitre { get; set; }
    public decimal DefaultConsumptionLPer100Km { get; set; }
    public decimal VehicleCostPerKm { get; set; }
    public decimal VehicleCostPerHour { get; set; }
    public decimal DriverCostPerHour { get; set; }
    public decimal EmployerCostMultiplier { get; set; } = 1.35m;
    public decimal MaintenanceCostPerKm { get; set; }
    public decimal DepreciationPerDay { get; set; }
    public decimal TrailerCostPerDay { get; set; }
    public decimal EquipmentCostPerDay { get; set; }
    public decimal DefaultTollPerTrip { get; set; }
    public int OvertimeThresholdMinutesPerDay { get; set; } = 480;
    public decimal OvertimeRateMultiplier { get; set; } = 1.5m;
    public decimal WaitingTimeCostPerHour { get; set; }
    public decimal Co2KgPerLitreDiesel { get; set; } = 2.68m;
    public decimal Co2KgPerLitreOther { get; set; } = 2.31m;
}
