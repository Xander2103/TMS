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
        return await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_dbContext.Roles.AsNoTracking().Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (ur, r) => r.Id)
            .Join(_dbContext.RolePermissions.AsNoTracking(), roleId => roleId, rp => rp.RoleId, (roleId, rp) => rp.PermissionId)
            .Join(_dbContext.Permissions.AsNoTracking(), permissionId => permissionId, p => p.Id, (permissionId, p) => p.Code)
            .AnyAsync(code => code == permissionCode, cancellationToken);
    }
}
