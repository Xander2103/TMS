using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// Closure sprint 2026-09-23 (P0) — the permission catalogue and the role templates are part
/// of the normal release flow in EVERY environment, not only in Development. These tests
/// replay the state the production database was found in (catalogue frozen at an older
/// release, role templates at v24) and prove that one startup sync brings it to the current
/// release, idempotently, without any manual database work.
/// </summary>
public class AuthorizationCatalogSyncTests
{
    /// <summary>The codes that were missing in production on 2026-09-21 (all introduced after role template v24).</summary>
    private static readonly string[] CodesMissingInProduction =
    [
        PermissionCodes.DossiersPrice, PermissionCodes.DossiersOverrideEntity,
        PermissionCodes.ActivityTypesView, PermissionCodes.ActivityTypesManage,
        PermissionCodes.LocationsViewSensitive, PermissionCodes.OrderImportsManageProfiles,
        PermissionCodes.ProblemsApproveCharge, PermissionCodes.SystemInfoView,
        PermissionCodes.BackupsView, PermissionCodes.BackupsCreate, PermissionCodes.BackupsDownload,
        PermissionCodes.BackupsDelete, PermissionCodes.BackupsRestore,
        PermissionCodes.AttendanceSelf, PermissionCodes.AttendanceView, PermissionCodes.AttendanceCorrect,
        PermissionCodes.AttendanceReport, PermissionCodes.AttendanceManageKiosks,
        PermissionCodes.AttendanceManageCredentials, PermissionCodes.AttendanceManageSettings,
    ];

    private const int ProductionAppliedVersion = 24;

    private static async Task<(SqliteTestDbContext Db, Guid TenantId, Guid AdminRoleId, Guid PlannerRoleId)> SeedProductionLikeStateAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Prod", Slug = "prod", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        // Catalogue as of an older release: everything except the codes introduced afterwards.
        var missing = CodesMissingInProduction.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, module, action, description) in PermissionCodes.All.Where(p => !missing.Contains(p.Code)))
        {
            db.Context.Permissions.Add(new Permission { Id = Guid.NewGuid(), Code = code, Module = module, Action = action, Description = description });
        }
        await db.Context.SaveChangesAsync();

        var adminRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true };
        var plannerRole = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Planner", TemplateCode = "planner", IsActive = true };
        db.Context.Roles.AddRange(adminRole, plannerRole);
        db.Context.RoleTemplateStates.Add(new RoleTemplateState
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AppliedVersion = ProductionAppliedVersion, UpdatedAt = DateTime.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        return (db, tenantId, adminRole.Id, plannerRole.Id);
    }

    private static async Task<HashSet<string>> CodesOfAsync(SqliteTestDbContext db, Guid roleId) =>
        (await db.Context.RolePermissions.Where(rp => rp.RoleId == roleId)
            .Join(db.Context.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Code)
            .ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task OneReleaseSync_AddsTheMissingCodes_AndUpgradesTheRolesToTheCurrentVersion()
    {
        var (db, tenantId, adminRoleId, plannerRoleId) = await SeedProductionLikeStateAsync();
        using var _ = db;
        foreach (var code in CodesMissingInProduction)
        {
            Assert.False(await db.Context.Permissions.AnyAsync(p => p.Code == code), $"precondition: {code} absent");
        }

        await AuthorizationCatalogSync.SyncAsync(db.Context, NullLogger.Instance);

        var catalogue = (await db.Context.Permissions.Select(p => p.Code).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(PermissionCodes.All.Count, catalogue.Count);
        Assert.All(CodesMissingInProduction, code => Assert.Contains(code, catalogue));

        // The system role holds the full catalogue; the stamped template role received every
        // grant of the upgrade steps after v24 (dossiers.price arrives for planner in v32).
        Assert.Equal(catalogue, await CodesOfAsync(db, adminRoleId));
        Assert.Contains(PermissionCodes.DossiersPrice, await CodesOfAsync(db, plannerRoleId));
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion,
            (await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId)).AppliedVersion);
    }

    [Fact]
    public async Task RunningTheReleaseSyncAgain_ChangesNothing()
    {
        var (db, _, adminRoleId, plannerRoleId) = await SeedProductionLikeStateAsync();
        using var _ = db;

        await AuthorizationCatalogSync.SyncAsync(db.Context, NullLogger.Instance);
        var permissionsAfterFirst = await db.Context.Permissions.CountAsync();
        var grantsAfterFirst = await db.Context.RolePermissions.CountAsync();
        var plannerAfterFirst = await CodesOfAsync(db, plannerRoleId);

        await AuthorizationCatalogSync.SyncAsync(db.Context, NullLogger.Instance);

        Assert.Equal(permissionsAfterFirst, await db.Context.Permissions.CountAsync());
        Assert.Equal(grantsAfterFirst, await db.Context.RolePermissions.CountAsync());
        Assert.Equal(plannerAfterFirst, await CodesOfAsync(db, plannerRoleId));
        Assert.Equal(PermissionCodes.All.Count, (await CodesOfAsync(db, adminRoleId)).Count);
    }

    [Fact]
    public async Task ATenantCustomisation_SurvivesTheReleaseSync()
    {
        var (db, _, _, plannerRoleId) = await SeedProductionLikeStateAsync();
        using var _ = db;
        await AuthorizationCatalogSync.SyncAsync(db.Context, NullLogger.Instance);

        // The tenant deliberately takes dossiers.price away from planners after the upgrade.
        var priceId = await db.Context.Permissions.Where(p => p.Code == PermissionCodes.DossiersPrice).Select(p => p.Id).SingleAsync();
        db.Context.RolePermissions.RemoveRange(db.Context.RolePermissions.Where(rp => rp.RoleId == plannerRoleId && rp.PermissionId == priceId));
        await db.Context.SaveChangesAsync();

        await AuthorizationCatalogSync.SyncAsync(db.Context, NullLogger.Instance);

        Assert.DoesNotContain(PermissionCodes.DossiersPrice, await CodesOfAsync(db, plannerRoleId));
    }

    [Fact]
    public void Startup_RunsTheReleaseSync_OutsideTheDevelopmentBlock()
    {
        // Guard against the regression that caused the production gap: the sync must sit in
        // the environment-agnostic startup section, BEFORE the Development-only block.
        var programPath = Path.Combine(FindRepoRoot(), "TransportationService.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        var syncIndex = source.IndexOf("AuthorizationCatalogSync.SyncAsync(", StringComparison.Ordinal);
        var developmentIndex = source.IndexOf("if (app.Environment.IsDevelopment())", StringComparison.Ordinal);

        Assert.True(syncIndex >= 0, "Program.cs must call AuthorizationCatalogSync.SyncAsync");
        Assert.True(developmentIndex >= 0, "Program.cs must still have the Development block");
        Assert.True(syncIndex < developmentIndex, "The release sync must run before (outside) the Development-only block");
        Assert.DoesNotContain("PermissionCatalogSeeder.SyncAsync(dbContext)", source);
        Assert.DoesNotContain("DefaultRoleSeeder.SyncAsync(dbContext)", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TransportationService.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
