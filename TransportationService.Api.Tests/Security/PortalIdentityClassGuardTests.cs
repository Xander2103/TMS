using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// H-14: identity-class guard. A user that is linked to a customer (User.CustomerId != null)
/// is a PORTAL identity and may only ever satisfy customer_portal.* permissions — regardless of
/// which roles happen to be attached to the account. This is fail-closed: a mis-seeded or
/// legacy portal account that still carries an internal role (orders.view is the classic one,
/// see S-14) must be refused by the permission evaluator itself, not by UI hiding.
/// </summary>
public class PortalIdentityClassGuardTests
{
    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid PortalUserId, Guid InternalUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Users.AddRange(
            new User
            {
                Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant",
                CustomerId = customerId, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
            new User
            {
                Id = internalUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Peter", LastName = "Planner",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });

        // ONE role carrying both an internal and a portal permission, granted to BOTH users:
        // the only difference between them is the customer link.
        db.Context.Roles.Add(new Role
        {
            Id = roleId, TenantId = tenantId, Name = "Gemengd", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        var internalPermissionId = Guid.NewGuid();
        var portalPermissionId = Guid.NewGuid();
        db.Context.Permissions.AddRange(
            new Permission { Id = internalPermissionId, Code = PermissionCodes.OrdersView, Module = "orders", Action = "view", Description = "x" },
            new Permission { Id = portalPermissionId, Code = PermissionCodes.CustomerPortalView, Module = "customer_portal", Action = "view", Description = "y" });
        db.Context.UserRoles.AddRange(
            new UserRole { UserId = portalUserId, RoleId = roleId },
            new UserRole { UserId = internalUserId, RoleId = roleId });
        db.Context.RolePermissions.AddRange(
            new RolePermission { RoleId = roleId, PermissionId = internalPermissionId },
            new RolePermission { RoleId = roleId, PermissionId = portalPermissionId });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, portalUserId, internalUserId);
    }

    [Fact]
    public async Task CustomerLinkedUser_HoldingOrdersView_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new PermissionAuthorizationService(h.Db.Context);

        Assert.False(await sut.UserHasPermissionAsync(h.PortalUserId, PermissionCodes.OrdersView, CancellationToken.None));
    }

    [Fact]
    public async Task CustomerLinkedUser_KeepsItsPortalPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new PermissionAuthorizationService(h.Db.Context);

        Assert.True(await sut.UserHasPermissionAsync(h.PortalUserId, PermissionCodes.CustomerPortalView, CancellationToken.None));
    }

    [Fact]
    public async Task InternalUser_IsUnaffected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new PermissionAuthorizationService(h.Db.Context);

        Assert.True(await sut.UserHasPermissionAsync(h.InternalUserId, PermissionCodes.OrdersView, CancellationToken.None));
        Assert.True(await sut.UserHasPermissionAsync(h.InternalUserId, PermissionCodes.CustomerPortalView, CancellationToken.None));
    }

    /// <summary>
    /// The bulk evaluator feeds global search, the report catalog and the resource links — it must
    /// classify identically, or a portal user would still see internal search hits.
    /// </summary>
    [Fact]
    public async Task PermissionSet_OfACustomerLinkedUser_ContainsOnlyPortalCodes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new PermissionSetService(h.Db.Context);

        var portalCodes = await sut.GetPermissionCodesAsync(h.PortalUserId, CancellationToken.None);
        Assert.Equal([PermissionCodes.CustomerPortalView], portalCodes.OrderBy(c => c).ToArray());

        var internalCodes = await sut.GetPermissionCodesAsync(h.InternalUserId, CancellationToken.None);
        Assert.Contains(PermissionCodes.OrdersView, internalCodes);
    }
}
