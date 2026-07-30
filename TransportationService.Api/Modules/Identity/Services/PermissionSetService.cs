using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Identity.Services;

public interface IPermissionSetService
{
    /// <summary>All permission codes the user holds, in one roundtrip.</summary>
    Task<IReadOnlySet<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Loads a user's complete permission set at once — for callers that gate many items per
/// request (global search, report catalog) where per-code checks would mean N roundtrips.
/// Same tenant-defensive join as <see cref="PermissionAuthorizationService"/>.
/// </summary>
public class PermissionSetService : IPermissionSetService
{
    private readonly TransportationDbContext _dbContext;

    public PermissionSetService(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlySet<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var codes = await (
                from ur in _dbContext.UserRoles.AsNoTracking()
                join u in _dbContext.Users.AsNoTracking() on ur.UserId equals u.Id
                join r in _dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
                join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                join p in _dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where ur.UserId == userId && u.IsActive && !u.IsBlocked && r.IsActive && r.TenantId == u.TenantId
                select p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);
        return codes.ToHashSet();
    }
}
