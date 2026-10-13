using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Builds the negative-stock guard for inventory tests. AllowAll mirrors a user with
/// inventory.override_negative_stock; DenyAll a user without it.
/// </summary>
public static class InventoryTestFactory
{
    public sealed class AllowAllPermissionService : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    public sealed class DenyAllPermissionService : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    /// <summary>Guard for a caller that HOLDS the override permission.</summary>
    public static NegativeStockGuard Guard(ICurrentUserContext currentUser) =>
        new(currentUser, new AllowAllPermissionService());

    /// <summary>Guard for a caller WITHOUT the override permission.</summary>
    public static NegativeStockGuard DenyingGuard(ICurrentUserContext currentUser) =>
        new(currentUser, new DenyAllPermissionService());
}
