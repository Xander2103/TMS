using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// The ONE answer to "which driver is the signed-in user?": the ACTIVE driver profile linked to
/// the user's employee, within the current tenant. Null when the user is not (or no longer) a
/// driver: a deactivated driver profile keeps its history but executes nothing (driver-only
/// deactivation leaves the account, so this is where its driver access ends).
/// </summary>
public static class DriverIdentity
{
    public static async Task<Guid?> ResolveAsync(
        TransportationDbContext db, Guid tenantId, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return null;
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.TenantId == tenantId && u.EmployeeId != null)
            .Join(db.Drivers.AsNoTracking().Where(d => d.TenantId == tenantId && d.IsActive),
                u => u.EmployeeId, d => d.EmployeeId, (u, d) => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>
/// Server-side trip scope for driver-reachable endpoints (Security H1). A caller holding the
/// endpoint's office permission (planning.view / planning.edit …) may reach any trip of the
/// tenant; everyone else — a Chauffeur — only a trip assigned to their own driver profile.
/// Out-of-scope trips are indistinguishable from unknown ones, so callers answer 404.
/// </summary>
public interface IDriverTripScope
{
    Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken);

    /// <summary>True when the caller holds one of the office permissions or drives the trip.</summary>
    Task<bool> CanAccessTripAsync(Guid tripId, string officePermission, CancellationToken cancellationToken);

    /// <summary>True when the caller holds one of the office permissions or drives a trip carrying the package's order.</summary>
    Task<bool> CanAccessPackageAsync(Guid packageId, IReadOnlyList<string> officePermissions, CancellationToken cancellationToken);
}

public sealed class DriverTripScope : IDriverTripScope
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPermissionAuthorizationService _permissionService;

    public DriverTripScope(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IPermissionAuthorizationService permissionService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _permissionService = permissionService;
    }

    public Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken) =>
        DriverIdentity.ResolveAsync(_dbContext, _tenantContext.TenantId, _currentUserContext.CurrentUserId, cancellationToken);

    public async Task<bool> CanAccessTripAsync(Guid tripId, string officePermission, CancellationToken cancellationToken)
    {
        if (await HasOfficeScopeAsync([officePermission], cancellationToken))
        {
            return true;
        }

        return await CurrentDriverIdAsync(cancellationToken) is { } driverId
               && await _dbContext.Trips.AsNoTracking()
                   .AnyAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId && t.DriverId == driverId,
                       cancellationToken);
    }

    public async Task<bool> CanAccessPackageAsync(
        Guid packageId, IReadOnlyList<string> officePermissions, CancellationToken cancellationToken)
    {
        if (await HasOfficeScopeAsync(officePermissions, cancellationToken))
        {
            return true;
        }

        if (await CurrentDriverIdAsync(cancellationToken) is not { } driverId)
        {
            return false;
        }

        var tenantId = _tenantContext.TenantId;
        var orderId = await _dbContext.Packages.AsNoTracking()
            .Where(p => p.Id == packageId && p.TenantId == tenantId)
            .Select(p => (Guid?)p.TransportOrderId)
            .FirstOrDefaultAsync(cancellationToken);
        return orderId is { } id
               && await _dbContext.Trips.AsNoTracking()
                   .Where(t => t.TenantId == tenantId && t.DriverId == driverId)
                   .SelectMany(t => t.Orders)
                   .AnyAsync(o => o.TransportOrderId == id && !o.IsDeleted, cancellationToken);
    }

    private async Task<bool> HasOfficeScopeAsync(IReadOnlyList<string> officePermissions, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        foreach (var permission in officePermissions)
        {
            if (await _permissionService.UserHasPermissionAsync(userId, permission, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
