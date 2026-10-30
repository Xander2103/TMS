using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>What one offboarding switched off — audited and returned to the UI.</summary>
public sealed record EmployeeOffboardingSummary(
    int UsersDeactivated, int DriversDeactivated, int VehicleSlotsReleased, int UpcomingTrips, int TrailersReleased = 0,
    int OutstandingEquipment = 0);

/// <summary>
/// UX-012: what offboarding would touch, for the confirmation dialog. Read-only; the same trip and slot
/// definitions as <see cref="EmployeeOffboardingService.OffboardAsync"/> so the preview cannot disagree with it.
/// Outstanding company items have their own read (<c>employees/{id}/outstanding</c>).
/// </summary>
public sealed record EmployeeOffboardingImpact(
    int ActiveAccounts,
    int UpcomingTripCount,
    IReadOnlyList<OffboardingImpactTrip> UpcomingTrips,
    IReadOnlyList<OffboardingImpactVehicle> Vehicles,
    string? FixedTrailerNumber);

public sealed record OffboardingImpactTrip(Guid Id, string TripNumber, DateOnly TripDate, TripStatus Status);

public sealed record OffboardingImpactVehicle(Guid Id, string InternalNumber, string LicensePlate, bool IsFixedDriver, bool IsCurrentDriver);

/// <summary>Which business path ended the employment (audited as the reason).</summary>
public enum OffboardingTrigger
{
    /// <summary>HR's explicit "Medewerker inactief zetten" (<c>POST /api/employees/{id}/deactivate</c>).</summary>
    ExplicitDeactivation,

    /// <summary>An employee edit that sets the status to Terminated or an end date already in the past.</summary>
    TerminatedByEdit,

    /// <summary>The background lifecycle sweep: the last working day has passed.</summary>
    EmploymentEndDatePassed,

    /// <summary>The sweep found an ended employment whose user / driver was still active (legacy data).</summary>
    ResidualAccess,
}

/// <summary>
/// MD-H6 / Security H2 — the ONE authoritative offboarding flow. Every business path that ends an
/// employment reaches this orchestrator, so they all produce the same invariant: an ended
/// employment never leaves an active login or a plannable driver behind.
/// <list type="bullet">
/// <item>explicit deactivation (<see cref="OffboardingTrigger.ExplicitDeactivation"/>);</item>
/// <item>an employee edit that sets Terminated or a past end date (<see cref="OffboardingTrigger.TerminatedByEdit"/>);</item>
/// <item>the restart-safe <see cref="EmploymentEndSweepWorker"/> once a future end date has passed;</item>
/// <item>defense in depth: login, refresh and every authenticated request also refuse a user whose
/// linked employment has ended (<see cref="EmploymentAccess"/>), even before the sweep ran.</item>
/// </list>
/// In ONE save:
/// <list type="number">
/// <item>the employee is inactive / Terminated (end date = tenant-local today when none was set);</item>
/// <item>every linked user is deactivated; sessions, refresh tokens and pending activation / reset
/// links are revoked (the shared <see cref="AccountSessionRevocation"/>);</item>
/// <item>every linked driver is switched off through <see cref="DriverDeactivation"/> — vehicle
/// fixed / current slots and the driver's fixed trailer are released;</item>
/// <item>the clock-in code is revoked and an open attendance session is closed;</item>
/// <item>history is untouched — trips, POD and audit keep the driver; nothing is deleted.</item>
/// </list>
/// Driver-only deactivation (the driver edit) is NOT offboarding: it takes the driver role out of
/// service and leaves the person's account alone.
/// HR needs only <c>employees.deactivate</c>. The tenant's LAST active administrator is never
/// switched off this way. Reactivating the employee deliberately does NOT re-enable the login or
/// the driver: an administrator re-enables the account explicitly.
/// </summary>
public sealed class EmployeeOffboardingService
{
    private const string EmployeeEntityType = "Employee";
    private const string AdministratorRoleName = "Administrator";

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _clock;

    public EmployeeOffboardingService(TransportationDbContext db, ITenantContext tenantContext, IAuditService auditService, TimeProvider clock)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _clock = clock;
    }

    /// <summary>
    /// Ends the employment of a tracked <paramref name="employee"/> and saves it together with every
    /// other pending change of the caller's unit of work (an edit is committed in the same save).
    /// Throws <see cref="DomainValidationException"/> before anything is saved when the employee is
    /// linked to the tenant's last active administrator.
    /// </summary>
    public async Task<EmployeeOffboardingSummary> OffboardAsync(
        Employee employee, OffboardingTrigger trigger, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var today = await TenantTimeZone.TodayAsync(_db, tenantId, _clock, cancellationToken);

        var wasActive = employee.IsActive;
        employee.IsActive = false;
        if (trigger != OffboardingTrigger.ResidualAccess)
        {
            employee.EmploymentEndDate ??= today;
        }

        if (employee.EmploymentStatus != EmploymentStatus.Terminated)
        {
            employee.EmploymentStatus = EmploymentStatus.Terminated;
        }

        var users = await _db.Users
            .Where(u => u.TenantId == tenantId && u.EmployeeId == employee.Id)
            .ToListAsync(cancellationToken);
        foreach (var user in users.Where(u => u.IsActive))
        {
            if (await IsLastActiveAdministratorAsync(tenantId, user.Id, cancellationToken))
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
            await AccountSessionRevocation.RevokeAllAsync(_db, user, nowUtc, cancellationToken);
        }

        var drivers = await _db.Drivers
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employee.Id && d.IsActive)
            .ToListAsync(cancellationToken);
        var released = await DriverDeactivation.DeactivateAsync(_db, tenantId, drivers, cancellationToken);
        var driverIds = drivers.Select(d => d.Id).ToList();

        // Upcoming work is NOT silently unassigned (a planner decides); it is counted so the UI
        // and the audit say how many trips still need another driver.
        var upcoming = driverIds.Count == 0
            ? 0
            : await UpcomingTrips(tenantId, driverIds, today).CountAsync(cancellationToken);

        await EndAttendanceAsync(tenantId, employee.Id, nowUtc, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // WH-H5: company items still to be returned are counted (and shown in the dialog before
        // confirming), never returned automatically — and they never block ending the access.
        var outstanding = await WarehouseIssueService.CountOutstandingAsync(_db, tenantId, employee.Id, cancellationToken);

        var summary = new EmployeeOffboardingSummary(
            usersDeactivated, drivers.Count, released.VehicleSlotsReleased, upcoming, released.TrailersReleased, outstanding);
        await _auditService.RecordAsync(EmployeeEntityType, employee.Id.ToString(), "Deactivated", new { IsActive = wasActive },
            new
            {
                employee.IsActive, employee.EmploymentEndDate, employee.EmploymentStatus, Trigger = trigger,
                AccountsDeactivated = summary.UsersDeactivated, DriversDeactivated = summary.DriversDeactivated,
                VehicleSlotsReleased = summary.VehicleSlotsReleased, TrailersReleased = summary.TrailersReleased,
                UpcomingTrips = summary.UpcomingTrips, OutstandingEquipment = summary.OutstandingEquipment,
            }, cancellationToken);
        foreach (var user in users)
        {
            // Visible in the user's own security history too (who / when / why).
            await _auditService.RecordAsync("User", user.Id.ToString(), SecurityAuditEvents.UserDeactivated,
                null, new { IsActive = false, SessionsRevoked = true, Reason = ReasonFor(trigger) }, cancellationToken);
        }

        return summary;
    }

    /// <summary>Work that needs another driver once this employment ends: not silently unassigned.</summary>
    private IQueryable<Trip> UpcomingTrips(Guid tenantId, List<Guid> driverIds, DateOnly today) =>
        _db.Trips.Where(t => t.TenantId == tenantId && t.DriverId != null && driverIds.Contains(t.DriverId.Value)
                             && (t.Status == TripStatus.InProgress
                                 || (t.TripDate >= today && (t.Status == TripStatus.Draft || t.Status == TripStatus.Planned))));

    /// <summary>UX-012: the preview for the dialog (see <see cref="EmployeeOffboardingImpact"/>).</summary>
    public async Task<EmployeeOffboardingImpact?> GetImpactAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        const int tripCap = 10;
        var tenantId = _tenantContext.TenantId;
        if (!await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.Id == employeeId, cancellationToken))
        {
            return null;
        }

        var today = await TenantTimeZone.TodayAsync(_db, tenantId, _clock, cancellationToken);
        var accounts = await _db.Users.CountAsync(u => u.TenantId == tenantId && u.EmployeeId == employeeId && u.IsActive, cancellationToken);
        var drivers = await _db.Drivers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == employeeId && d.IsActive)
            .Select(d => new { d.Id, d.FixedTrailerId })
            .ToListAsync(cancellationToken);
        if (drivers.Count == 0)
        {
            return new EmployeeOffboardingImpact(accounts, 0, [], [], null);
        }

        var driverIds = drivers.Select(d => d.Id).ToList();
        var upcoming = UpcomingTrips(tenantId, driverIds, today).AsNoTracking();
        var tripCount = await upcoming.CountAsync(cancellationToken);
        var trips = await upcoming
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .Take(tripCap)
            .Select(t => new OffboardingImpactTrip(t.Id, t.TripNumber, t.TripDate, t.Status))
            .ToListAsync(cancellationToken);
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == tenantId
                        && ((v.FixedDriverId != null && driverIds.Contains(v.FixedDriverId.Value))
                            || (v.CurrentDriverId != null && driverIds.Contains(v.CurrentDriverId.Value))))
            .Select(v => new OffboardingImpactVehicle(
                v.Id, v.InternalNumber, v.LicensePlate,
                v.FixedDriverId != null && driverIds.Contains(v.FixedDriverId.Value),
                v.CurrentDriverId != null && driverIds.Contains(v.CurrentDriverId.Value)))
            .ToListAsync(cancellationToken);
        var fixedTrailerId = drivers.Select(d => d.FixedTrailerId).FirstOrDefault(id => id != null);
        var trailerNumber = fixedTrailerId is null
            ? null
            : await _db.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.Id == fixedTrailerId)
                .Select(t => t.InternalNumber)
                .FirstOrDefaultAsync(cancellationToken);
        return new EmployeeOffboardingImpact(accounts, tripCount, trips, vehicles, trailerNumber);
    }

    private static string ReasonFor(OffboardingTrigger trigger) => trigger switch
    {
        OffboardingTrigger.TerminatedByEdit => "Medewerker uit dienst gezet (status/einddatum gewijzigd)",
        OffboardingTrigger.EmploymentEndDatePassed => "Einddatum tewerkstelling verstreken",
        OffboardingTrigger.ResidualAccess => "Medewerker uit dienst — achtergebleven toegang afgesloten",
        _ => "Medewerker inactief gezet (uitdiensttreding)",
    };

    /// <summary>
    /// Urenregistratie: the clock-in code is revoked so a departed employee can no longer punch;
    /// an open session is closed (the employee can no longer clock out themselves) with a
    /// traceable ClockOut event. Attendance history itself is untouched.
    /// </summary>
    private async Task EndAttendanceAsync(Guid tenantId, Guid employeeId, DateTime closeAt, CancellationToken cancellationToken)
    {
        foreach (var credential in await _db.AttendanceCredentials
                     .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.IsActive)
                     .ToListAsync(cancellationToken))
        {
            credential.IsActive = false;
        }

        var openSessions = await _db.AttendanceSessions
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ClockOutAt == null)
            .ToListAsync(cancellationToken);
        if (openSessions.Count == 0)
        {
            return;
        }

        var sessionIds = openSessions.Select(s => s.Id).ToList();
        foreach (var openBreak in await _db.AttendanceBreaks
                     .Where(b => b.TenantId == tenantId && sessionIds.Contains(b.SessionId) && b.EndedAt == null)
                     .ToListAsync(cancellationToken))
        {
            openBreak.EndedAt = closeAt;
        }

        foreach (var session in openSessions)
        {
            session.ClockOutAt = closeAt;
            session.ClockOutSource = AttendanceSource.Api;
            session.Status = AttendanceSessionStatus.Completed;
            _db.AttendanceEvents.Add(new AttendanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SessionId = session.Id,
                EmployeeId = employeeId,
                EventType = AttendanceEventType.ClockOut,
                OccurredAt = closeAt,
                Source = AttendanceSource.Api,
                Note = "Afgesloten bij deactivering van de medewerker.",
            });
        }
    }

    private async Task<bool> IsLastActiveAdministratorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var adminUserIds = _db.UserRoles
            .Join(_db.Roles.Where(r => r.Name == AdministratorRoleName && r.IsSystemRole), ur => ur.RoleId, r => r.Id, (ur, r) => ur.UserId);
        if (!await adminUserIds.AnyAsync(id => id == userId, cancellationToken))
        {
            return false;
        }

        return !await _db.Users.AnyAsync(u => u.TenantId == tenantId && u.Id != userId && u.IsActive && !u.IsBlocked
                                              && adminUserIds.Contains(u.Id), cancellationToken);
    }
}
