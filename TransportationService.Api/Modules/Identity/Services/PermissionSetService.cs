using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Identity.Services;

public interface IPermissionSetService
{
    /// <summary>
    /// All permission codes the user EFFECTIVELY holds, in one roundtrip — i.e. after the
    /// identity-class guard (H-14): a customer-linked user only ever keeps customer_portal.*.
    /// This is the set to gate features with.
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The raw codes ASSIGNED through the user's roles, without the identity-class guard. Only
    /// for privilege-comparison ("does the target hold anything the actor lacks"), where the
    /// stricter, larger set is the safe one; never for granting access.
    /// </summary>
    Task<IReadOnlySet<string>> GetAssignedPermissionCodesAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Loads a user's complete permission set at once — for callers that gate many items per
/// request (global search, report catalog) where per-code checks would mean N roundtrips.
/// Same tenant-defensive join and same identity-class guard as
/// <see cref="PermissionAuthorizationService"/>, so the two can never disagree about what a
/// portal identity may do.
/// </summary>
public class PermissionSetService : IPermissionSetService
{
    private readonly TransportationDbContext _dbContext;

    public PermissionSetService(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlySet<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        LoadAsync(userId, applyIdentityClassGuard: true, cancellationToken);

    public Task<IReadOnlySet<string>> GetAssignedPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        LoadAsync(userId, applyIdentityClassGuard: false, cancellationToken);

    private async Task<IReadOnlySet<string>> LoadAsync(
        Guid userId, bool applyIdentityClassGuard, CancellationToken cancellationToken)
    {
        var codes = await (
                from ur in _dbContext.UserRoles.AsNoTracking()
                join u in _dbContext.Users.AsNoTracking() on ur.UserId equals u.Id
                join r in _dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
                join rp in _dbContext.RolePermissions.AsNoTracking() on r.Id equals rp.RoleId
                join p in _dbContext.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                where ur.UserId == userId && u.IsActive && !u.IsBlocked && r.IsActive && r.TenantId == u.TenantId
                      && (!applyIdentityClassGuard || u.CustomerId == null
                          || p.Code.StartsWith(AccountSecurityService.PortalPermissionPrefix))
                select p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);
        return codes.ToHashSet();
    }
}
