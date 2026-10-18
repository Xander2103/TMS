using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Drivers.Services;

/// <summary>
/// MD-H7 (master-data acceptance 2026-10-18): a driver with completed trips could be deleted — the
/// trip lost its driver and a vehicle's fixed driver pointed at a deleted record. Trips, activities,
/// POD, damage, fuel, incidents and execution exceptions are history: such a driver is deactivated,
/// never deleted. The standing vehicle slots (fixed / current driver) and a tank-card holder are NOT
/// history; they are released when an unused driver is deleted so nothing dangles.
/// </summary>
public static class DriverReferences
{
    public const string DeactivateHint =
        "Zet de chauffeur inactief: de historiek blijft bewaard en de chauffeur kan niet meer ingepland worden.";

    public static async Task<ReferenceReport> ForDriverAsync(
        TransportationDbContext db, Guid tenantId, Guid driverId, CancellationToken cancellationToken) =>
        new ReferenceReport()
            .Add("trips", await db.Trips.CountAsync(t => t.TenantId == tenantId && t.DriverId == driverId, cancellationToken), "rit", "ritten")
            .Add("activities", await db.DossierActivities.CountAsync(a => a.TenantId == tenantId && a.DriverId == driverId, cancellationToken),
                "activiteit", "activiteiten")
            .Add("pods", await db.ProofsOfDelivery.CountAsync(p => p.TenantId == tenantId && p.DriverId == driverId, cancellationToken),
                "afleverbewijs", "afleverbewijzen")
            .Add("damage", await db.DamageReports.CountAsync(d => d.TenantId == tenantId && d.DriverId == driverId, cancellationToken),
                "schademelding", "schademeldingen")
            .Add("fuel", await db.FuelTransactions.CountAsync(f => f.TenantId == tenantId && f.DriverId == driverId, cancellationToken),
                "tankbeurt", "tankbeurten")
            .Add("incidents", await db.Incidents.CountAsync(i => i.TenantId == tenantId && i.DriverId == driverId, cancellationToken),
                "afwijking", "afwijkingen")
            .Add("exceptions", await db.ExecutionExceptions.CountAsync(e => e.TenantId == tenantId && e.DriverId == driverId, cancellationToken),
                "uitvoeringsafwijking", "uitvoeringsafwijkingen")
            .Add("dockAppointments", await db.DockAppointments.CountAsync(a => a.TenantId == tenantId && a.DriverId == driverId, cancellationToken),
                "dockafspraak", "dockafspraken");

    /// <summary>Releases the non-historical slots that point at a driver that is going away.</summary>
    public static async Task ReleaseStandingSlotsAsync(
        TransportationDbContext db, Guid tenantId, Guid driverId, CancellationToken cancellationToken)
    {
        foreach (var vehicle in await db.Vehicles
                     .Where(v => v.TenantId == tenantId && (v.FixedDriverId == driverId || v.CurrentDriverId == driverId))
                     .ToListAsync(cancellationToken))
        {
            if (vehicle.FixedDriverId == driverId)
            {
                vehicle.FixedDriverId = null;
            }

            if (vehicle.CurrentDriverId == driverId)
            {
                vehicle.CurrentDriverId = null;
            }
        }

        foreach (var card in await db.TankCards.Where(c => c.TenantId == tenantId && c.DriverId == driverId).ToListAsync(cancellationToken))
        {
            card.DriverId = null;
        }
    }
}
