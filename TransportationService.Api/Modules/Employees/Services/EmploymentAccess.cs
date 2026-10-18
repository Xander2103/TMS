using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// MD-H6 follow-up, defense in depth: login, token refresh and every authenticated request refuse
/// a user whose LINKED employment has ended (<see cref="EmploymentState"/>) — even when the
/// offboarding cascade has not run yet (an end date that passed overnight, before the sweep).
/// A user that is intentionally not linked to an employee (administrator, customer-portal user)
/// is never affected.
/// </summary>
public static class EmploymentAccess
{
    public static async Task<bool> HasEndedAsync(
        TransportationDbContext db, Guid tenantId, Guid? employeeId, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (employeeId is not { } id)
        {
            return false;
        }

        var employment = await db.Employees.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.Id == id && e.TenantId == tenantId)
            .Select(e => new { e.IsActive, e.EmploymentStatus, e.EmploymentEndDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (employment is null)
        {
            return false;
        }

        // Only an end date needs the calendar: skip the time-zone lookup otherwise.
        if (!employment.IsActive || employment.EmploymentStatus == Entities.EmploymentStatus.Terminated)
        {
            return true;
        }

        if (employment.EmploymentEndDate is null)
        {
            return false;
        }

        var today = await TenantTimeZone.TodayAsync(db, tenantId, clock, cancellationToken);
        return EmploymentState.HasEnded(employment.IsActive, employment.EmploymentStatus, employment.EmploymentEndDate, today);
    }
}
