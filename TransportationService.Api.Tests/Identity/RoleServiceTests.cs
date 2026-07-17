using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

public class RoleServiceTests
{
    [Fact]
    public async Task DeactivateAsync_RejectsDeactivatingSystemRole()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var sut = new RoleService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.DeactivateAsync(roleId, CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.SystemRoleProtected, result.Outcome);
    }

    [Fact]
    public async Task DeactivateAsync_AllowsDeactivatingNonSystemRole()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var sut = new RoleService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.DeactivateAsync(roleId, CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Success, result.Outcome);
        Assert.False(result.Role!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_RejectsRenamingSystemRole()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var sut = new RoleService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.UpdateAsync(roleId, new UpdateRoleRequest("SuperAdmin", "renamed"), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.SystemRoleProtected, result.Outcome);
    }

    [Fact]
    public async Task AssignPermissionsAsync_AllowsExtendingSystemRolePermissions()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "users.view", Module = "users", Action = "view", Description = "x" });
        await db.Context.SaveChangesAsync();

        var sut = new RoleService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.AssignPermissionsAsync(roleId, new AssignPermissionsRequest(["users.view"]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Success, result.Outcome);
        Assert.Contains("users.view", result.Role!.PermissionCodes);
    }
}
