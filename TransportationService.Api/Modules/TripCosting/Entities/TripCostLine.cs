using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.TripCosting.Entities;

public enum TripCostPhase
{
    Estimated,
    Actual,
}

public enum TripCostType
{
    Fuel,
    Toll,
    DriverLabour,
    Overtime,
    WaitingTime,
    VehicleDistance,
    VehicleTime,
    Maintenance,
    Depreciation,
    Trailer,
    Equipment,
    Subcontractor,
    FerryTunnelParking,
    Manual,
    Correction,
}

/// <summary>
/// One cost component of a trip. Lines are SNAPSHOTS: quantity and rate are resolved at
/// calculation time and never drift when rate cards change later. Recalculation replaces
/// only non-overridden calculated lines of the requested phase; manual lines and manual
/// overrides always survive.
/// </summary>
public class TripCostLine : AuditableTenantEntity
{
    public const string SourceCalculated = "Berekend";
    public const string SourceManual = "Handmatig";
    public const string SourceFuelRecords = "Tankbeurten";

    public Guid TripId { get; set; }
    public TripCostPhase Phase { get; set; }
    public TripCostType CostType { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "stuk";
    public decimal UnitRate { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } = SourceCalculated;
    public bool IsManualOverride { get; set; }
    public string? OverrideReason { get; set; }
    public DateTime CalculatedAt { get; set; }
}
