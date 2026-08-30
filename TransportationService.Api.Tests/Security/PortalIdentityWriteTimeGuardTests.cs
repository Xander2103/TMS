using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// H-14 fix round 1: the identity-class rule is enforced at READ time by the permission evaluator,
/// but `User.CustomerId` is a free field on the internal user form. Without a write-time rule an
/// administrator can silently brick an internal account — every permission evaporates and the 403
/// says "Missing permission: orders.view", which is true but actively misleading. So the same
/// invariant is refused where the data is created: a customer-linked user may only hold roles whose
/// permissions all live in the customer_portal.* namespace.
/// </summary>
public class PortalIdentityWriteTimeGuardTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid AdminUserId, Guid CustomerId,
        Guid InternalRoleId, Guid PortalRoleId, Guid TargetUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var internalRoleId = Guid.NewGuid();
        var portalRoleId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Users.AddRange(
            new User
            {
                Id = adminUserId, TenantId = tenantId, Email = "admin@acme.be", FirstName = "Ada", LastName = "Admin",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
            new User
            {
                Id = targetUserId, TenantId = tenantId, Email = "target@acme.be", FirstName = "Tim", LastName = "Target",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        await db.Context.SaveChangesAsync();
        await PermissionCatalogSeeder.SyncAsync(db.Context);

        async Task<Guid> RoleAsync(Guid id, string name, params string[] codes)
        {
            db.Context.Roles.Add(new Role
            {
                Id = id, TenantId = tenantId, Name = name, IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var permissionIds = await db.Context.Permissions.Where(p => codes.Contains(p.Code))
                .Select(p => p.Id).ToListAsync();
            Assert.Equal(codes.Length, permissionIds.Count);
            foreach (var permissionId in permissionIds)
            {
                db.Context.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = permissionId });
            }

            return id;
        }

        await RoleAsync(internalRoleId, "Planner", PermissionCodes.OrdersView, PermissionCodes.OrdersEdit);
        await RoleAsync(portalRoleId, "Klantportaal", PermissionCodes.CustomerPortalView, PermissionCodes.CustomerPortalSubmitOrders);

        // The acting administrator holds everything, so the escalation guard never masks the
        // identity-class refusal we are actually testing.
        db.Context.Roles.Add(new Role
        {
            Id = adminRoleId, TenantId = tenantId, Name = "Administrator", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        foreach (var permission in await db.Context.Permissions.ToListAsync())
        {
            db.Context.RolePermissions.Add(new RolePermission { RoleId = adminRoleId, PermissionId = permission.Id });
        }

        db.Context.UserRoles.Add(new UserRole { UserId = adminUserId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantId, adminUserId, customerId, internalRoleId, portalRoleId, targetUserId);
    }

    [Fact]
    public async Task Create_WithCustomerLinkAndAnInternalRole_IsRefusedWithADutchMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(
            new CreateUserRequest("nieuw@haven.be", "Nieuwe", "Klant", null, h.CustomerId, [h.InternalRoleId]),
            CancellationToken.None));

        Assert.Contains("klantportaal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders.view", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Db.Context.Users.ToList(), u => u.Email == "nieuw@haven.be");
    }

    [Fact]
    public async Task Create_WithCustomerLinkAndOnlyPortalRoles_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        var created = await sut.CreateAsync(
            new CreateUserRequest("portaal@haven.be", "Pia", "Portaal", null, h.CustomerId, [h.PortalRoleId]),
            CancellationToken.None);

        Assert.Equal(h.CustomerId, h.Db.Context.Users.Single(u => u.Id == created.Id).CustomerId);
    }

    [Fact]
    public async Task Create_WithoutACustomerLink_KeepsAcceptingInternalRoles()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        var created = await sut.CreateAsync(
            new CreateUserRequest("planner2@acme.be", "Piet", "Planner", null, null, [h.InternalRoleId]),
            CancellationToken.None);

        Assert.Null(h.Db.Context.Users.Single(u => u.Id == created.Id).CustomerId);
    }

    /// <summary>Linking an EXISTING internal user to a customer is the exact bricking scenario.</summary>
    [Fact]
    public async Task Update_LinkingAnInternalUserToACustomer_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.TargetUserId, RoleId = h.InternalRoleId });
        await h.Db.Context.SaveChangesAsync();
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateAsync(
            h.TargetUserId, new UpdateUserRequest("Tim", "Target", null, h.CustomerId), CancellationToken.None));

        Assert.Null(h.Db.Context.Users.Single(u => u.Id == h.TargetUserId).CustomerId);
    }

    [Fact]
    public async Task Update_LinkingAPortalOnlyUserToACustomer_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.TargetUserId, RoleId = h.PortalRoleId });
        await h.Db.Context.SaveChangesAsync();
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        await sut.UpdateAsync(h.TargetUserId, new UpdateUserRequest("Tim", "Target", null, h.CustomerId), CancellationToken.None);

        Assert.Equal(h.CustomerId, h.Db.Context.Users.Single(u => u.Id == h.TargetUserId).CustomerId);
    }

    /// <summary>The other direction: giving an internal role to an already customer-linked user.</summary>
    [Fact]
    public async Task AssignRoles_GivingAnInternalRoleToACustomerLinkedUser_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var target = h.Db.Context.Users.Single(u => u.Id == h.TargetUserId);
        target.CustomerId = h.CustomerId;
        await h.Db.Context.SaveChangesAsync();
        var sut = IdentityTestFactory.UserService(h.Db, h.TenantId, h.AdminUserId);

        var refused = await sut.AssignRolesAsync(
            h.TargetUserId, new AssignRolesRequest([h.InternalRoleId]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains("klantportaal", refused.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Db.Context.UserRoles.Where(ur => ur.UserId == h.TargetUserId).ToList());

        // The portal role on the same account is still assignable.
        var allowed = await sut.AssignRolesAsync(
            h.TargetUserId, new AssignRolesRequest([h.PortalRoleId]), CancellationToken.None);
        Assert.Equal(UserOperationOutcome.Success, allowed.Outcome);
    }
}
