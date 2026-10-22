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

    public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
        UserHasAnyPermissionAsync(userId, [permissionCode], cancellationToken);

    public async Task<bool> UserHasAnyPermissionAsync(
        Guid userId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
    {
        if (permissionCodes.Count == 0)
        {
            return false;
        }

        return await HeldCodes(userId, permissionCodes).AnyAsync(cancellationToken);
    }

    /// <summary>One roundtrip for a multi-section read model (the dashboard): the held subset of the codes.</summary>
    public async Task<IReadOnlySet<string>> UserPermissionsAmongAsync(
        Guid userId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
    {
        if (permissionCodes.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var held = await HeldCodes(userId, permissionCodes).Distinct().ToListAsync(cancellationToken);
        return held.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The permission codes among <paramref name="permissionCodes"/> that the user's active roles
    /// grant — the ONE evaluator both the yes/no and the subset questions are answered from.
    /// </summary>
    private IQueryable<string> HeldCodes(Guid userId, IReadOnlyList<string> permissionCodes)
    {
        // Identity-class guard (H-14): a user linked to a customer is a PORTAL identity and can
        // only ever satisfy customer_portal.* codes — whatever roles happen to hang on the
        // account. Fail-closed and evaluated in the same roundtrip, so a mis-seeded portal
        // account carrying an internal role is refused by the evaluator itself rather than by
        // whichever caller happened to remember the rule.
        // Mixed portal/internal lists are evaluated per class so the guard never widens.
        var portalCodes = permissionCodes.Where(PortalPermissionScope.Covers).ToList();
        var internalCodes = permissionCodes.Where(c => !PortalPermissionScope.Covers(c)).ToList();

        // Defense in depth: a role only grants permissions when it belongs to the user's own
        // tenant, even if a cross-tenant UserRole row were ever to exist.
        return
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
                  && (portalCodes.Contains(p.Code) || (u.CustomerId == null && internalCodes.Contains(p.Code)))
            select p.Code;
    }
}
