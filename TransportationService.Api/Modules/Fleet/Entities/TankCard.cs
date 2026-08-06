using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

/// <summary>
/// Computed lifecycle status of a tank card; never stored, derived from blocking state and validity window.
/// </summary>
public enum TankCardStatus
{
    Active,
    ExpiringSoon,
    Expired,
    Blocked,
}

/// <summary>
/// Fuel card issued by a provider (DKV, Shell, ...), optionally assigned to one vehicle and/or one driver.
/// The PIN is deliberately never stored. External provider integrations are out of scope; transactions are
/// registered manually via <see cref="FuelTransaction"/>.
/// </summary>
public class TankCard : AuditableTenantEntity
{
    public string CardNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;

    public Guid? VehicleId { get; set; }

    /// <summary>Legacy pointer to the driver profile; kept in sync with <see cref="EmployeeId"/> by the
    /// service (the driver profile of that employee, or null when the employee has none).</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Canonical person link: the employee this card is assigned to.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>Free-text label used internally (e.g. "Vrachtwagen 3 - reserve"), independent of the provider's card number.</summary>
    public string? InternalName { get; set; }

    public string? FuelType { get; set; }

    public decimal? DailyLimit { get; set; }
    public decimal? WeeklyLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }

    public string? CostCenter { get; set; }

    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }

    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }

    public string? Notes { get; set; }
}
