using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Drivers.Entities;

/// <summary>
/// Operational availability of a driver, independent of the active/inactive lifecycle flag.
/// </summary>
public enum DriverAvailabilityStatus
{
    Available,
    Unavailable,
    OnLeave,
    OnTrip,
}

/// <summary>
/// A first-class driver, backed by exactly one <c>Employee</c>. Driver-specific operational
/// attributes live here; personal/employment data stays on the employee record (no duplication).
/// Licence/qualification data is read through the existing qualification + eligibility services.
/// </summary>
public class Driver : AuditableTenantEntity
{
    public string DriverNumber { get; set; } = string.Empty;

    /// <summary>The employee this driver record belongs to (one driver per employee, per tenant).</summary>
    public Guid EmployeeId { get; set; }

    public Guid? DriverCategoryId { get; set; }

    public DriverAvailabilityStatus AvailabilityStatus { get; set; } = DriverAvailabilityStatus.Available;

    public bool IsActive { get; set; } = true;

    /// <summary>Operational block (e.g. licence withdrawn). Independent of active/inactive.</summary>
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    /// <summary>Prefer to always assign the same vehicle where possible.</summary>
    public bool FixedVehiclePreference { get; set; }

    // References into Fleet. All three are FK-constrained (see DriverConfiguration), SetNull on
    // delete so removing a vehicle/trailer never blocks the delete or removes the driver.
    public Guid? DefaultVehicleId { get; set; }
    public Guid? PreferredVehicleId { get; set; }
    public Guid? DefaultTrailerId { get; set; }

    public string? Notes { get; set; }
}
