using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Identity.Services;

public class PermissionAuthorizationService : IPermissionAuthorizationService
{
    private readonly TransportationDbContext _dbContext;

    public PermissionAuthorizationService(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        // Identity-class guard (H-14): a user linked to a customer is a PORTAL identity and can
        // only ever satisfy customer_portal.* codes — whatever roles happen to hang on the
        // account. Fail-closed and evaluated in the same roundtrip, so a mis-seeded portal
        // account carrying an internal role is refused by the evaluator itself rather than by
        // whichever caller happened to remember the rule.
        var portalCode = IsPortalPermission(permissionCode);

        // Defense in depth: a role only grants permissions when it belongs to the user's own
        // tenant, even if a cross-tenant UserRole row were ever to exist.
        var query =
            from ur in _dbContext.UserRoles.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on ur.UserId equals u.Id
            join r in _dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
            join p in _dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where ur.UserId == userId
                  && u.IsActive
                  && !u.IsBlocked
                  && r.IsActive
                  && r.TenantId == u.TenantId
                  && (portalCode || u.CustomerId == null)
                  && p.Code == permissionCode
            select 1;

        return await query.AnyAsync(cancellationToken);
    }

    /// <summary>The customer_portal.* namespace — the only codes a customer-linked user may hold.</summary>
    internal static bool IsPortalPermission(string permissionCode) =>
        permissionCode.StartsWith(AccountSecurityService.PortalPermissionPrefix, StringComparison.OrdinalIgnoreCase);
}
