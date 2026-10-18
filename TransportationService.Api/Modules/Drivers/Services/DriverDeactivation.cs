using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Drivers.Entities;

namespace TransportationService.Api.Modules.Drivers.Services;

/// <summary>What switching drivers off released.</summary>
public sealed record DriverSlotRelease(int VehicleSlotsReleased, int TrailersReleased);

/// <summary>
/// MD-H6 follow-up: the ONE place that takes drivers out of operational service. Used by
/// <list type="bullet">
/// <item>the driver edit (<c>isActive=false</c>): the ROLE is switched off — "niet meer
/// inzetbaar als chauffeur". The person's user account and other roles are untouched;</item>
/// <item>employee offboarding: the whole working relationship ends (the orchestrator also ends
/// access); it reuses this for the driver part.</item>
/// </list>
/// An inactive driver holds no standing slot that planning or the fleet screens would still
/// present as "theirs": the fixed / current vehicle slots and the driver's own fixed trailer are
/// released. History (trips, activities, POD, audit) keeps the driver; nothing is deleted.
/// Not saved here: the caller's SaveChanges commits it with the rest of its change.
/// </summary>
public static class DriverDeactivation
{
    public static async Task<DriverSlotRelease> DeactivateAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Driver> drivers, CancellationToken cancellationToken)
    {
        if (drivers.Count == 0)
        {
            return new DriverSlotRelease(0, 0);
        }

        var driverIds = drivers.Select(d => d.Id).ToList();
        var trailersReleased = 0;
        foreach (var driver in drivers)
        {
            driver.IsActive = false;
            if (driver.FixedTrailerId is not null)
            {
                driver.FixedTrailerId = null;
                trailersReleased++;
            }
        }

        var slotsReleased = 0;
        foreach (var vehicle in await db.Vehicles
                     .Where(v => v.TenantId == tenantId
                                 && ((v.FixedDriverId != null && driverIds.Contains(v.FixedDriverId.Value))
                                     || (v.CurrentDriverId != null && driverIds.Contains(v.CurrentDriverId.Value))))
                     .ToListAsync(cancellationToken))
        {
            if (vehicle.FixedDriverId is { } fixedId && driverIds.Contains(fixedId))
            {
                vehicle.FixedDriverId = null;
                slotsReleased++;
            }

            if (vehicle.CurrentDriverId is { } currentId && driverIds.Contains(currentId))
            {
                vehicle.CurrentDriverId = null;
                slotsReleased++;
            }
        }

        return new DriverSlotRelease(slotsReleased, trailersReleased);
    }
}
