using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests;

public class SeedingTests
{
    [Fact]
    public async Task SeedAsync_CreatesAdministratorRole_WithEveryPermission()
    {
        using var db = new SqliteTestDbContext();

        await MasterDataSeeder.SeedAsync(db.Context);

        var permissionCount = await db.Context.Permissions.CountAsync();
        var adminRole = await db.Context.Roles.FirstAsync(r => r.Name == "Administrator");
        var adminPermissionCount = await db.Context.RolePermissions.CountAsync(rp => rp.RoleId == adminRole.Id);

        Assert.True(permissionCount > 0);
        Assert.Equal(permissionCount, adminPermissionCount);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = new SqliteTestDbContext();

        await MasterDataSeeder.SeedAsync(db.Context);
        await MasterDataSeeder.SeedAsync(db.Context);

        var tenantCount = await db.Context.Tenants.CountAsync();
        Assert.Equal(1, tenantCount);
    }
}
