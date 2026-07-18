using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

/// <summary>
/// Manually registered refuelling for a vehicle. Consumption and anomaly warnings are computed
/// server-side from the odometer trail; they are never stored so corrections re-evaluate cleanly.
/// </summary>
public class FuelTransaction : AuditableTenantEntity
{
    public Guid VehicleId { get; set; }

    public Guid? DriverId { get; set; }
    public Guid? TankCardId { get; set; }

    public DateOnly TransactionDate { get; set; }

    public decimal Litres { get; set; }
    public decimal TotalAmount { get; set; }

    public int? OdometerKm { get; set; }
    public string? Station { get; set; }

    /// <summary>Consumption is only computable between two full-tank fills; partial fills skip the calculation.</summary>
    public bool FullTank { get; set; } = true;

    public string? Notes { get; set; }
}
