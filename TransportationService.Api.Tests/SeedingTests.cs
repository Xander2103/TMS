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

    /// <summary>
    /// Startup order in every environment is: release-flow AuthorizationCatalogSync, THEN the
    /// Development-only MasterDataSeeder. On an empty database the catalogue therefore already
    /// exists when the dev tenant is created — the seeder must reuse it, never duplicate codes.
    /// </summary>
    [Fact]
    public async Task SeedAsync_AfterTheReleaseFlowCatalogSync_ReusesTheCatalogue()
    {
        using var db = new SqliteTestDbContext();
        await AuthorizationCatalogSync.SyncAsync(db.Context, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var catalogueBefore = await db.Context.Permissions.CountAsync();

        await MasterDataSeeder.SeedAsync(db.Context);

        Assert.Equal(catalogueBefore, await db.Context.Permissions.CountAsync());
        var adminRole = await db.Context.Roles.FirstAsync(r => r.Name == "Administrator");
        Assert.Equal(catalogueBefore, await db.Context.RolePermissions.CountAsync(rp => rp.RoleId == adminRole.Id));
    }
}
