using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

public class AdminSafeguardTests
{
    private static async Task<(SqliteTestDbContext Db, Guid TenantId, Guid AdminRoleId, Guid UserId)> SeedSingleAdministratorAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Roles.Add(new Role { Id = adminRoleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "admin@tenant.com", FirstName = "Admin", LastName = "User", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        return (db, tenantId, adminRoleId, userId);
    }

    [Fact]
    public async Task SetActiveAsync_RejectsDeactivatingLastActiveAdministrator()
    {
        var (db, tenantId, _, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.SetActiveAsync(userId, false, CancellationToken.None);

        Assert.Equal(UserOperationOutcome.LastActiveAdministrator, result.Outcome);
        var reloaded = await sut.GetByIdAsync(userId, CancellationToken.None);
        Assert.True(reloaded!.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_AllowsDeactivatingAdministrator_WhenAnotherActiveAdministratorExists()
    {
        var (db, tenantId, adminRoleId, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var secondAdminId = Guid.NewGuid();
        db.Context.Users.Add(new User { Id = secondAdminId, TenantId = tenantId, Email = "admin2@tenant.com", FirstName = "Second", LastName = "Admin", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.UserRoles.Add(new UserRole { UserId = secondAdminId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.SetActiveAsync(userId, false, CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task SetBlockedAsync_RejectsBlockingLastActiveAdministrator()
    {
        var (db, tenantId, _, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.SetBlockedAsync(userId, true, CancellationToken.None);

        Assert.Equal(UserOperationOutcome.LastActiveAdministrator, result.Outcome);
    }

    [Fact]
    public async Task AssignRolesAsync_RejectsRemovingAdministratorRole_FromLastActiveAdministrator()
    {
        var (db, tenantId, _, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.AssignRolesAsync(userId, new AssignRolesRequest([]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.LastActiveAdministrator, result.Outcome);
    }

    [Fact]
    public async Task AssignRolesAsync_AllowsRemovingAdministratorRole_WhenAnotherActiveAdministratorExists()
    {
        var (db, tenantId, adminRoleId, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var secondAdminId = Guid.NewGuid();
        db.Context.Users.Add(new User { Id = secondAdminId, TenantId = tenantId, Email = "admin2@tenant.com", FirstName = "Second", LastName = "Admin", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.UserRoles.Add(new UserRole { UserId = secondAdminId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.AssignRolesAsync(userId, new AssignRolesRequest([]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Success, result.Outcome);
    }
}
