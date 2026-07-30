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
                  && p.Code == permissionCode
            select 1;

        return await query.AnyAsync(cancellationToken);
    }
}
