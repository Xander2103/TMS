using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>What one offboarding switched off — audited and returned to the UI.</summary>
public sealed record EmployeeOffboardingSummary(
    int UsersDeactivated, int DriversDeactivated, int VehicleSlotsReleased, int UpcomingTrips);

/// <summary>
/// MD-H6 / Security H2 (master-data + security acceptance 2026-10-18): HR deactivated an employee
/// but the linked user kept its session, a fresh login still worked, the driver stayed plannable
/// and the vehicle still named them as fixed driver. Deactivating an employee now ends the whole
/// working relationship in the SAME save:
/// <list type="number">
/// <item>every linked user is deactivated and its sessions, refresh tokens and pending
/// activation / reset links are revoked (the shared <see cref="AccountSessionRevocation"/>);</item>
/// <item>every linked driver is deactivated, so planning no longer offers them;</item>
/// <item>fixed / current vehicle slots naming such a driver are released;</item>
/// <item>history is untouched — trips, POD and audit keep the driver; nothing is deleted.</item>
/// </list>
/// HR needs only <c>employees.deactivate</c>: this is part of the employee operation, not a hidden
/// user-administration right. A user that is the tenant's LAST active administrator is never
/// switched off this way (the tenant would lock itself out) — HR gets a clear refusal.
/// Reactivating the employee deliberately does NOT re-enable the login or the driver: an
/// administrator re-enables the account explicitly (users.edit) when the person returns.
/// </summary>
public static class EmployeeOffboarding
{
    private const string AdministratorRoleName = "Administrator";

    public static async Task<EmployeeOffboardingSummary> ApplyAsync(
        TransportationDbContext db, Guid tenantId, Employee employee, DateTime nowUtc, DateOnly today, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Where(u => u.TenantId == tenantId && u.EmployeeId == employee.Id)
            .ToListAsync(cancellationToken);
        foreach (var user in users.Where(u => u.IsActive))
        {
            if (await IsLastActiveAdministratorAsync(db, tenantId, user.Id, cancellationToken))
            {
                throw new DomainValidationException(
                    $"Deze medewerker is gekoppeld aan het laatste actieve beheerdersaccount ({user.Email}). Wijs eerst een andere beheerder aan voor je de medewerker inactief zet.");
            }
        }

        var usersDeactivated = 0;
        foreach (var user in users)
        {
            if (user.IsActive)
            {
                usersDeactivated++;
            }

            user.IsActive = false;
            user.UpdatedAt = nowUtc;
            // Also for an already-inactive account: a pending activation link must die with it.
            await AccountSessionRevocation.RevokeAllAsync(db, user, nowUtc, cancellationToken);
        }

        var drivers = await db.Drivers
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employee.Id && d.IsActive)
            .ToListAsync(cancellationToken);
        var driverIds = drivers.Select(d => d.Id).ToList();
        foreach (var driver in drivers)
        {
            driver.IsActive = false;
        }

        var slotsReleased = 0;
        if (driverIds.Count > 0)
        {
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
        }

        // Upcoming work is NOT silently unassigned (a planner decides); it is counted so the UI
        // and the audit say how many trips still need another driver.
        var upcoming = driverIds.Count == 0
            ? 0
            : await db.Trips.CountAsync(t => t.TenantId == tenantId && t.DriverId != null && driverIds.Contains(t.DriverId.Value)
                                             && t.TripDate >= today
                                             && (t.Status == TripStatus.Draft || t.Status == TripStatus.Planned || t.Status == TripStatus.InProgress),
                cancellationToken);

        return new EmployeeOffboardingSummary(usersDeactivated, drivers.Count, slotsReleased, upcoming);
    }

    private static async Task<bool> IsLastActiveAdministratorAsync(TransportationDbContext db, Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var adminUserIds = db.UserRoles
            .Join(db.Roles.Where(r => r.Name == AdministratorRoleName && r.IsSystemRole), ur => ur.RoleId, r => r.Id, (ur, r) => ur.UserId);
        if (!await adminUserIds.AnyAsync(id => id == userId, cancellationToken))
        {
            return false;
        }

        return !await db.Users.AnyAsync(u => u.TenantId == tenantId && u.Id != userId && u.IsActive && !u.IsBlocked
                                              && adminUserIds.Contains(u.Id), cancellationToken);
    }
}
