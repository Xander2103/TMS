using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

public class PermissionAuthorizationServiceTests
{
    [Fact]
    public async Task UserHasPermissionAsync_ReturnsTrue_WhenRoleGrantsPermission()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "a@b.com", FirstName = "A", LastName = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "employees.view", Module = "employees", Action = "view", Description = "x" });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        await db.Context.SaveChangesAsync();

        var sut = new PermissionAuthorizationService(db.Context);

        var result = await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task UserHasPermissionAsync_ReturnsFalse_WhenUserHasNoMatchingRole()
    {
        using var db = new SqliteTestDbContext();
        var userId = Guid.NewGuid();

        var sut = new PermissionAuthorizationService(db.Context);

        var result = await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task UserHasPermissionAsync_ReturnsFalse_WhenRoleBelongsToOtherTenant()
    {
        using var db = new SqliteTestDbContext();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        // The user's tenant differs from the role's tenant — the grant must not apply,
        // even though the UserRole row exists.
        db.Context.Users.Add(new User { Id = userId, TenantId = Guid.NewGuid(), Email = "a@b.com", FirstName = "A", LastName = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = Guid.NewGuid(), Name = "ForeignAdmin", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "employees.view", Module = "employees", Action = "view", Description = "x" });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        await db.Context.SaveChangesAsync();

        var sut = new PermissionAuthorizationService(db.Context);

        Assert.False(await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None));
    }

    [Fact]
    public async Task UserHasPermissionAsync_ReturnsFalse_WhenGrantingRoleIsInactive()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "a@b.com", FirstName = "A", LastName = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", IsActive = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "employees.view", Module = "employees", Action = "view", Description = "x" });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        await db.Context.SaveChangesAsync();

        var sut = new PermissionAuthorizationService(db.Context);

        var result = await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None);

        Assert.False(result);
    }
}
