using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Identity;

public class DefaultRoleSeederTests
{
    private static readonly string[] Version2Codes =
    [
        PermissionCodes.EmployeePlanningConflictOverride,
        PermissionCodes.TripCostsView, PermissionCodes.TripCostsManage, PermissionCodes.TripCostsOverride,
        PermissionCodes.ProfitabilityView, PermissionCodes.KpiView, PermissionCodes.KpiExport,
    ];

    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedTenantWithCatalogAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await PermissionCatalogSeeder.SyncAsync(db.Context);
        return (db, tenantId);
    }

    private static async Task AddTenantAsync(SqliteTestDbContext db, Guid tenantId, string slug)
    {
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
    }

    /// <summary>
    /// Simulates a role from a database created BEFORE template stamping and the costing/KPI
    /// permissions: exact template display name, no TemplateCode, and the template's
    /// permission set minus everything introduced in upgrade version 2.
    /// </summary>
    private static async Task<Role> CreateLegacyRoleAsync(SqliteTestDbContext db, Guid tenantId, string templateCode)
    {
        var template = DefaultRoleDefinitions.All.Single(t => t.Code == templateCode);
        var role = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = template.Name,
            Description = template.Description, IsSystemRole = false, IsActive = true,
            TemplateCode = null, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Roles.Add(role);
        var oldCodes = template.PermissionCodes.Except(Version2Codes).Distinct().ToList();
        var permissions = await db.Context.Permissions.Where(p => oldCodes.Contains(p.Code)).ToListAsync();
        foreach (var permission in permissions)
        {
            db.Context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        }

        await db.Context.SaveChangesAsync();
        return role;
    }

    private static Task<List<string>> CodesOfAsync(SqliteTestDbContext db, Guid roleId) =>
        (from rp in db.Context.RolePermissions
         join p in db.Context.Permissions on rp.PermissionId equals p.Id
         where rp.RoleId == roleId
         select p.Code).ToListAsync();

    [Fact]
    public async Task SyncAsync_CreatesDefaultRoles_WithExpectedPermissions()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        Assert.Equal(
            new[] { "Boekhouding", "Chauffeur", "Dispatcher", "HR", "Klantportaal", "Magazijn", "Management", "Planner" },
            roles.Select(r => r.Name).OrderBy(n => n).ToArray());
        Assert.All(roles, r => Assert.False(r.IsSystemRole));

        var hr = roles.Single(r => r.Name == "HR");
        var hrCodes = await (from rp in db.Context.RolePermissions
                             join p in db.Context.Permissions on rp.PermissionId equals p.Id
                             where rp.RoleId == hr.Id
                             select p.Code).ToListAsync();
        Assert.Contains(PermissionCodes.EmployeePlanningManage, hrCodes);
        Assert.Contains(PermissionCodes.AbsencesApprove, hrCodes);
        Assert.Contains(PermissionCodes.EmployeePlanningConflictOverride, hrCodes);
        Assert.DoesNotContain(PermissionCodes.TripCostsView, hrCodes);
        Assert.DoesNotContain(PermissionCodes.KpiView, hrCodes);
        Assert.DoesNotContain(PermissionCodes.ProfitabilityView, hrCodes);

        var management = roles.Single(r => r.Name == "Management");
        var managementCodes = await (from rp in db.Context.RolePermissions
                                     join p in db.Context.Permissions on rp.PermissionId equals p.Id
                                     where rp.RoleId == management.Id
                                     select p.Code).ToListAsync();
        Assert.Contains(PermissionCodes.KpiView, managementCodes);
        Assert.Contains(PermissionCodes.KpiExport, managementCodes);
        Assert.Contains(PermissionCodes.ProfitabilityView, managementCodes);
        Assert.Contains(PermissionCodes.TripCostsOverride, managementCodes);

        var planner = roles.Single(r => r.Name == "Planner");
        var plannerCodes = await (from rp in db.Context.RolePermissions
                                  join p in db.Context.Permissions on rp.PermissionId equals p.Id
                                  where rp.RoleId == planner.Id
                                  select p.Code).ToListAsync();
        Assert.Contains(PermissionCodes.OrdersCancel, plannerCodes);
        Assert.Contains(PermissionCodes.OrdersAssign, plannerCodes);
        Assert.DoesNotContain(PermissionCodes.OrdersDelete, plannerCodes);
        Assert.DoesNotContain(PermissionCodes.OrdersManage, plannerCodes);
        Assert.DoesNotContain(PermissionCodes.UsersEdit, plannerCodes);

        var driver = roles.Single(r => r.Name == "Chauffeur");
        var driverCodes = await (from rp in db.Context.RolePermissions
                                 join p in db.Context.Permissions on rp.PermissionId equals p.Id
                                 where rp.RoleId == driver.Id
                                 select p.Code).ToListAsync();
        Assert.Contains(PermissionCodes.DriverWorkflowExecute, driverCodes);
        Assert.DoesNotContain(PermissionCodes.OrdersEdit, driverCodes);
    }

    [Fact]
    public async Task SyncAsync_IsIdempotent_AndPreservesTenantCustomisation()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        await DefaultRoleSeeder.SyncAsync(db.Context);

        // Tenant admin strips a permission from Planner…
        var planner = await db.Context.Roles.SingleAsync(r => r.TenantId == tenantId && r.Name == "Planner");
        var cancelPermission = await db.Context.Permissions.SingleAsync(p => p.Code == PermissionCodes.OrdersCancel);
        var link = await db.Context.RolePermissions
            .SingleAsync(rp => rp.RoleId == planner.Id && rp.PermissionId == cancelPermission.Id);
        db.Context.RolePermissions.Remove(link);
        await db.Context.SaveChangesAsync();

        // …and the next startup must NOT re-grant it or duplicate roles.
        await DefaultRoleSeeder.SyncAsync(db.Context);

        Assert.Equal(8, await db.Context.Roles.CountAsync(r => r.TenantId == tenantId));
        Assert.False(await db.Context.RolePermissions
            .AnyAsync(rp => rp.RoleId == planner.Id && rp.PermissionId == cancelPermission.Id));
    }

    [Fact]
    public async Task FreshTenant_RolesStamped_AndVersionRecorded()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        Assert.All(roles, r => Assert.NotNull(r.TemplateCode));
        Assert.Equal("planner", roles.Single(r => r.Name == "Planner").TemplateCode);
        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);
        // Fresh creation already includes the v2 defaults — the upgrade pass adds nothing twice.
        var plannerCodes = await CodesOfAsync(db, roles.Single(r => r.Name == "Planner").Id);
        Assert.Equal(plannerCodes.Count, plannerCodes.Distinct().Count());
        Assert.Contains(PermissionCodes.TripCostsView, plannerCodes);
    }

    [Fact]
    public async Task LegacyTenant_IsBackfilled_AndReceivesExactlyTheNewDefaults()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var planner = await CreateLegacyRoleAsync(db, tenantId, "planner");
        var management = await CreateLegacyRoleAsync(db, tenantId, "management");
        var boekhouding = await CreateLegacyRoleAsync(db, tenantId, "boekhouding");
        var plannerBefore = (await CodesOfAsync(db, planner.Id)).ToHashSet();

        await DefaultRoleSeeder.SyncAsync(db.Context);

        // Stamped by exact legacy name, upgraded by code.
        Assert.Equal("planner", (await db.Context.Roles.SingleAsync(r => r.Id == planner.Id)).TemplateCode);
        var plannerAfter = (await CodesOfAsync(db, planner.Id)).ToHashSet();
        Assert.True(plannerBefore.IsSubsetOf(plannerAfter), "no permission may ever be removed");
        Assert.Equal(
            new[] { PermissionCodes.EmployeePlanningConflictOverride, PermissionCodes.TripCostsView }.OrderBy(c => c),
            plannerAfter.Except(plannerBefore).OrderBy(c => c));

        var managementAfter = (await CodesOfAsync(db, management.Id)).ToHashSet();
        Assert.True(Version2Codes.All(managementAfter.Contains), "management receives all v2 codes");
        var boekhoudingAfter = (await CodesOfAsync(db, boekhouding.Id)).ToHashSet();
        Assert.Contains(PermissionCodes.ProfitabilityView, boekhoudingAfter);
        Assert.Contains(PermissionCodes.KpiExport, boekhoudingAfter);
        Assert.DoesNotContain(PermissionCodes.TripCostsManage, boekhoudingAfter);

        // HR did not exist on the legacy DB → created fresh, stamped, with its full set.
        var hr = await db.Context.Roles.SingleAsync(r => r.TenantId == tenantId && r.TemplateCode == "hr");
        Assert.Contains(PermissionCodes.EmployeePlanningConflictOverride, await CodesOfAsync(db, hr.Id));

        Assert.Equal(DefaultRoleUpgrades.CurrentVersion,
            (await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId)).AppliedVersion);
    }

    [Fact]
    public async Task CustomisedLegacyRole_KeepsCustomisation_GainsOnlyNewDefaults()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var planner = await CreateLegacyRoleAsync(db, tenantId, "planner");
        // Tenant had stripped orders.cancel long ago.
        var cancelPermission = await db.Context.Permissions.SingleAsync(p => p.Code == PermissionCodes.OrdersCancel);
        var link = await db.Context.RolePermissions
            .SingleAsync(rp => rp.RoleId == planner.Id && rp.PermissionId == cancelPermission.Id);
        db.Context.RolePermissions.Remove(link);
        await db.Context.SaveChangesAsync();

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var codes = await CodesOfAsync(db, planner.Id);
        Assert.DoesNotContain(PermissionCodes.OrdersCancel, codes); // customisation intact
        Assert.Contains(PermissionCodes.TripCostsView, codes);      // new default added
    }

    [Fact]
    public async Task RenamedStampedRole_StillReceivesUpgrades_WithoutDuplicateCreation()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var planner = await CreateLegacyRoleAsync(db, tenantId, "planner");
        planner.TemplateCode = "planner"; // stamped in an earlier run…
        planner.Name = "Planningsteam";   // …then renamed by the tenant
        await db.Context.SaveChangesAsync();

        await DefaultRoleSeeder.SyncAsync(db.Context);

        Assert.Contains(PermissionCodes.TripCostsView, await CodesOfAsync(db, planner.Id));
        // The template slot is occupied by code — no fresh "Planner" appears next to it.
        Assert.Equal(1, await db.Context.Roles
            .CountAsync(r => r.TenantId == tenantId && r.TemplateCode == "planner"));
        Assert.False(await db.Context.Roles.AnyAsync(r => r.TenantId == tenantId && r.Name == "Planner"));
    }

    [Fact]
    public async Task SimilarAndExactNamedCustomRoles_AreNeverStampedOrGranted()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var similar = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Super Planner",
            IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        // Exact "HR" on a legacy database can only be tenant-created (the template is newer).
        var tenantHr = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "HR",
            IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Roles.AddRange(similar, tenantHr);
        await db.Context.SaveChangesAsync();

        await DefaultRoleSeeder.SyncAsync(db.Context);

        Assert.Null((await db.Context.Roles.SingleAsync(r => r.Id == similar.Id)).TemplateCode);
        Assert.Empty(await CodesOfAsync(db, similar.Id));
        Assert.Null((await db.Context.Roles.SingleAsync(r => r.Id == tenantHr.Id)).TemplateCode);
        Assert.Empty(await CodesOfAsync(db, tenantHr.Id));
        // The occupied name blocks template creation — exactly one HR row exists.
        Assert.Equal(1, await db.Context.Roles.CountAsync(r => r.TenantId == tenantId && r.Name == "HR"));
    }

    [Fact]
    public async Task RepeatedRuns_NeverDuplicate_AndRespectLaterCustomisation()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var planner = await CreateLegacyRoleAsync(db, tenantId, "planner");

        await DefaultRoleSeeder.SyncAsync(db.Context);
        var grantCountAfterUpgrade = await db.Context.RolePermissions.CountAsync();

        // Tenant deliberately removes an UPGRADED permission afterwards…
        var tripCosts = await db.Context.Permissions.SingleAsync(p => p.Code == PermissionCodes.TripCostsView);
        var link = await db.Context.RolePermissions
            .SingleAsync(rp => rp.RoleId == planner.Id && rp.PermissionId == tripCosts.Id);
        db.Context.RolePermissions.Remove(link);
        await db.Context.SaveChangesAsync();

        // …and repeated runs neither re-add it nor duplicate anything else.
        await DefaultRoleSeeder.SyncAsync(db.Context);
        await DefaultRoleSeeder.SyncAsync(db.Context);

        Assert.Equal(grantCountAfterUpgrade - 1, await db.Context.RolePermissions.CountAsync());
        Assert.DoesNotContain(PermissionCodes.TripCostsView, await CodesOfAsync(db, planner.Id));
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion,
            (await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId)).AppliedVersion);
    }

    [Fact]
    public async Task Upgrades_AreTenantIsolated()
    {
        var (db, tenantA) = await SeedTenantWithCatalogAsync();
        using var _ = db;
        var tenantB = Guid.NewGuid();
        await AddTenantAsync(db, tenantB, "other");
        var legacyPlannerA = await CreateLegacyRoleAsync(db, tenantA, "planner");
        var customB = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantB, Name = "Eigen rol",
            IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.Roles.Add(customB);
        await db.Context.SaveChangesAsync();

        await DefaultRoleSeeder.SyncAsync(db.Context);

        Assert.Contains(PermissionCodes.TripCostsView, await CodesOfAsync(db, legacyPlannerA.Id));
        Assert.Empty(await CodesOfAsync(db, customB.Id));
        // Each tenant tracks its own applied version; B also got its own (fresh) template roles.
        Assert.Equal(2, await db.Context.RoleTemplateStates.CountAsync());
        Assert.True(await db.Context.Roles.AnyAsync(r => r.TenantId == tenantB && r.TemplateCode == "planner"));
        Assert.Null((await db.Context.Roles.SingleAsync(r => r.Id == customB.Id)).TemplateCode);
    }
}
