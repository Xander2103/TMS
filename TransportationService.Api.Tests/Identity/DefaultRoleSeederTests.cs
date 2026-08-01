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

    private static readonly string[] Version3Codes =
    [
        // packages.export is uit de catalogus verwijderd (L5): de historische upgrade-stap
        // vermeldt hem nog, maar de seeder slaat onbekende codes over.
        PermissionCodes.PackagesView, PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage,
        PermissionCodes.PackagesCancel, PermissionCodes.PackagesRelabel,
        PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
        PermissionCodes.ScanningOverride,
        PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
        PermissionCodes.PackageReportsExport,
    ];

    private static string[] UpgradeCodes => [.. Version2Codes, .. Version3Codes];

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
        var oldCodes = template.PermissionCodes.Except(UpgradeCodes).Distinct().ToList();
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
            new[]
            {
                "Boekhouding", "Chauffeur", "Dispatcher", "HR", "Klantportaal",
                "Klantportaal - Documenten", "Klantportaal - Facturen", "Klantportaal - Gebruikersbeheer",
                "Magazijn", "Management", "Planner",
            },
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

        // Leave-balance permissions (v10): HR manages, driver views own, management views only.
        Assert.Contains(PermissionCodes.LeaveBalancesManage, hrCodes);
        Assert.Contains(PermissionCodes.LeaveBalancesAdjust, hrCodes);
        Assert.Contains(PermissionCodes.LeaveTypesManage, hrCodes);
        Assert.Contains(PermissionCodes.LeaveBalancesViewOwn, driverCodes);
        Assert.DoesNotContain(PermissionCodes.LeaveBalancesManage, driverCodes);
        Assert.Contains(PermissionCodes.LeaveBalancesView, managementCodes);
        Assert.DoesNotContain(PermissionCodes.LeaveBalancesManage, managementCodes);
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

        Assert.Equal(11, await db.Context.Roles.CountAsync(r => r.TenantId == tenantId));
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
            new[]
            {
                PermissionCodes.EmployeePlanningConflictOverride, PermissionCodes.TripCostsView,
                PermissionCodes.OrdersOverridePrice, PermissionCodes.OrdersLockPrice,
                // v23 (sprint): personal tasks for every employee-facing template.
                PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn,
            }
                .Concat(Version3Codes).OrderBy(c => c),
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
    public async Task Version14_GrantsOrdersLockPrice_ToPlannerManagementBoekhoudingOnly()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var planner = roles.Single(r => r.TemplateCode == "planner");
        var management = roles.Single(r => r.TemplateCode == "management");
        var boekhouding = roles.Single(r => r.TemplateCode == "boekhouding");
        var others = roles.Where(r => r.TemplateCode is not ("planner" or "management" or "boekhouding")).ToList();

        Assert.Contains(PermissionCodes.OrdersLockPrice, await CodesOfAsync(db, planner.Id));
        Assert.Contains(PermissionCodes.OrdersLockPrice, await CodesOfAsync(db, management.Id));
        Assert.Contains(PermissionCodes.OrdersLockPrice, await CodesOfAsync(db, boekhouding.Id));
        foreach (var other in others)
        {
            Assert.DoesNotContain(PermissionCodes.OrdersLockPrice, await CodesOfAsync(db, other.Id));
        }
    }

    [Fact]
    public async Task Version15_GrantsTariffsImport_ToManagementBoekhoudingOnly()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var management = roles.Single(r => r.TemplateCode == "management");
        var boekhouding = roles.Single(r => r.TemplateCode == "boekhouding");
        var others = roles.Where(r => r.TemplateCode is not ("management" or "boekhouding")).ToList();

        Assert.Contains(PermissionCodes.TariffsImport, await CodesOfAsync(db, management.Id));
        Assert.Contains(PermissionCodes.TariffsImport, await CodesOfAsync(db, boekhouding.Id));
        foreach (var other in others)
        {
            Assert.DoesNotContain(PermissionCodes.TariffsImport, await CodesOfAsync(db, other.Id));
        }
    }

    [Fact]
    public async Task Version16_GrantsAccounting_ToBoekhoudingManageAndManagementViewOnly()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var management = roles.Single(r => r.TemplateCode == "management");
        var boekhouding = roles.Single(r => r.TemplateCode == "boekhouding");
        var others = roles.Where(r => r.TemplateCode is not ("management" or "boekhouding")).ToList();

        Assert.Contains(PermissionCodes.AccountingView, await CodesOfAsync(db, boekhouding.Id));
        Assert.Contains(PermissionCodes.AccountingManage, await CodesOfAsync(db, boekhouding.Id));
        Assert.Contains(PermissionCodes.AccountingView, await CodesOfAsync(db, management.Id));
        Assert.DoesNotContain(PermissionCodes.AccountingManage, await CodesOfAsync(db, management.Id));
        foreach (var other in others)
        {
            Assert.DoesNotContain(PermissionCodes.AccountingView, await CodesOfAsync(db, other.Id));
            Assert.DoesNotContain(PermissionCodes.AccountingManage, await CodesOfAsync(db, other.Id));
        }
    }

    [Fact]
    public async Task Version17_GrantsEmployeeNotes_ToHrAndManagementOnly()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var hr = roles.Single(r => r.TemplateCode == "hr");
        var management = roles.Single(r => r.TemplateCode == "management");
        var others = roles.Where(r => r.TemplateCode is not ("hr" or "management")).ToList();

        foreach (var role in new[] { hr, management })
        {
            var codes = await CodesOfAsync(db, role.Id);
            Assert.Contains(PermissionCodes.EmployeeNotesView, codes);
            Assert.Contains(PermissionCodes.EmployeeNotesManage, codes);
            Assert.Contains(PermissionCodes.EmployeeNotesPin, codes);
        }

        foreach (var other in others)
        {
            var codes = await CodesOfAsync(db, other.Id);
            Assert.DoesNotContain(PermissionCodes.EmployeeNotesView, codes);
            Assert.DoesNotContain(PermissionCodes.EmployeeNotesManage, codes);
            Assert.DoesNotContain(PermissionCodes.EmployeeNotesPin, codes);
        }
    }

    [Fact]
    public async Task Version18_GrantsNotificationRules_ToManagementHrAndBoekhouding()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var management = roles.Single(r => r.TemplateCode == "management");
        var hr = roles.Single(r => r.TemplateCode == "hr");
        var boekhouding = roles.Single(r => r.TemplateCode == "boekhouding");
        var others = roles.Where(r => r.TemplateCode is not ("management" or "hr" or "boekhouding")).ToList();

        Assert.Contains(PermissionCodes.NotificationRulesView, await CodesOfAsync(db, management.Id));
        Assert.Contains(PermissionCodes.NotificationRulesManage, await CodesOfAsync(db, management.Id));

        Assert.Contains(PermissionCodes.NotificationRulesView, await CodesOfAsync(db, hr.Id));
        Assert.DoesNotContain(PermissionCodes.NotificationRulesManage, await CodesOfAsync(db, hr.Id));

        Assert.Contains(PermissionCodes.NotificationRulesView, await CodesOfAsync(db, boekhouding.Id));
        Assert.DoesNotContain(PermissionCodes.NotificationRulesManage, await CodesOfAsync(db, boekhouding.Id));

        foreach (var other in others)
        {
            var codes = await CodesOfAsync(db, other.Id);
            Assert.DoesNotContain(PermissionCodes.NotificationRulesView, codes);
            Assert.DoesNotContain(PermissionCodes.NotificationRulesManage, codes);
        }
    }

    [Fact]
    public async Task Version19_GrantsPortalMessagesAndCustomerMessages_AndSeedsAddOnTemplates()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var klantportaal = roles.Single(r => r.TemplateCode == "klantportaal");
        var planner = roles.Single(r => r.TemplateCode == "planner");
        var dispatcher = roles.Single(r => r.TemplateCode == "dispatcher");
        var management = roles.Single(r => r.TemplateCode == "management");

        Assert.Contains(PermissionCodes.CustomerPortalMessages, await CodesOfAsync(db, klantportaal.Id));
        // Documents/invoices/manage_users are NEVER on the base role — only the add-on templates.
        Assert.DoesNotContain(PermissionCodes.CustomerPortalViewDocuments, await CodesOfAsync(db, klantportaal.Id));
        Assert.DoesNotContain(PermissionCodes.CustomerPortalViewInvoices, await CodesOfAsync(db, klantportaal.Id));
        Assert.DoesNotContain(PermissionCodes.CustomerPortalManageUsers, await CodesOfAsync(db, klantportaal.Id));

        foreach (var role in new[] { planner, dispatcher })
        {
            var codes = await CodesOfAsync(db, role.Id);
            Assert.Contains(PermissionCodes.CustomerMessagesView, codes);
            Assert.Contains(PermissionCodes.CustomerMessagesSend, codes);
        }

        var managementCodes = await CodesOfAsync(db, management.Id);
        Assert.Contains(PermissionCodes.CustomerMessagesView, managementCodes);
        Assert.Contains(PermissionCodes.CustomerMessagesSend, managementCodes);
        Assert.Contains(PermissionCodes.PortalAnnouncementsManage, managementCodes);

        // The three add-on templates are seeded fresh with exactly their one permission each.
        var documenten = roles.Single(r => r.TemplateCode == "klantportaal_documenten");
        var facturen = roles.Single(r => r.TemplateCode == "klantportaal_facturen");
        var gebruikersbeheer = roles.Single(r => r.TemplateCode == "klantportaal_gebruikersbeheer");
        Assert.Equal([PermissionCodes.CustomerPortalViewDocuments], await CodesOfAsync(db, documenten.Id));
        Assert.Equal([PermissionCodes.CustomerPortalViewInvoices], await CodesOfAsync(db, facturen.Id));
        Assert.Equal([PermissionCodes.CustomerPortalManageUsers], await CodesOfAsync(db, gebruikersbeheer.Id));
    }

    [Fact]
    public async Task Version20_GrantsEdiViewTestRetry_ToTheSameTemplateAsEdiManage()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var management = roles.Single(r => r.TemplateCode == "management");
        var managementCodes = await CodesOfAsync(db, management.Id);
        Assert.Contains(PermissionCodes.EdiView, managementCodes);
        Assert.Contains(PermissionCodes.EdiManage, managementCodes);
        Assert.Contains(PermissionCodes.EdiTest, managementCodes);
        Assert.Contains(PermissionCodes.EdiRetry, managementCodes);

        // edi.manage is the only pre-existing holder; every other template stays untouched.
        var others = roles.Where(r => r.TemplateCode != "management" && r.TemplateCode != null);
        foreach (var other in others)
        {
            var codes = await CodesOfAsync(db, other.Id);
            Assert.DoesNotContain(PermissionCodes.EdiView, codes);
            Assert.DoesNotContain(PermissionCodes.EdiTest, codes);
            Assert.DoesNotContain(PermissionCodes.EdiRetry, codes);
        }
    }

    [Fact]
    public async Task Version21_GrantsPeppolPermissions_ConfigureOnlyToManagement()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var managementCodes = await CodesOfAsync(db, roles.Single(r => r.TemplateCode == "management").Id);
        var boekhoudingCodes = await CodesOfAsync(db, roles.Single(r => r.TemplateCode == "boekhouding").Id);

        string[] everyday =
        [
            PermissionCodes.PeppolView, PermissionCodes.PeppolValidate, PermissionCodes.PeppolSend,
            PermissionCodes.PeppolRetry, PermissionCodes.PeppolViewIncoming,
        ];
        foreach (var code in everyday)
        {
            Assert.Contains(code, managementCodes);
            Assert.Contains(code, boekhoudingCodes);
        }

        // Per-legal-entity configuration follows the accounting.manage precedent: management only.
        Assert.Contains(PermissionCodes.PeppolConfigure, managementCodes);
        Assert.DoesNotContain(PermissionCodes.PeppolConfigure, boekhoudingCodes);

        var others = roles.Where(r =>
            r.TemplateCode is not null and not "management" and not "boekhouding");
        foreach (var other in others)
        {
            Assert.DoesNotContain(PermissionCodes.PeppolView, await CodesOfAsync(db, other.Id));
        }
    }

    [Fact]
    public async Task Version22_GrantsMedicalAbsenceView_OnlyToHr()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        var hrCodes = await CodesOfAsync(db, roles.Single(r => r.TemplateCode == "hr").Id);
        Assert.Contains(PermissionCodes.AbsencesViewMedical, hrCodes);

        // Planning-side templates keep absences.view (who is absent, when) but never the
        // health data behind it (GDPR art. 9).
        foreach (var other in roles.Where(r => r.TemplateCode is not null and not "hr" and not "administrator"))
        {
            Assert.DoesNotContain(PermissionCodes.AbsencesViewMedical, await CodesOfAsync(db, other.Id));
        }
    }

    [Fact]
    public async Task Version23_GrantsSprintPermissions_PerTemplateScope()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var state = await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenantId);
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion, state.AppliedVersion);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        Guid RoleId(string code) => roles.Single(r => r.TemplateCode == code).Id;

        // Every employee-facing template can work its own tasks.
        foreach (var code in new[] { "magazijn", "management", "hr", "planner", "dispatcher", "boekhouding", "chauffeur" })
        {
            var codes = await CodesOfAsync(db, RoleId(code));
            Assert.Contains(PermissionCodes.TasksViewOwn, codes);
            Assert.Contains(PermissionCodes.TasksManageOwn, codes);
        }

        // Magazijn operates the reorder flow but never sets thresholds or overrides negative stock.
        var magazijn = await CodesOfAsync(db, RoleId("magazijn"));
        Assert.Contains(PermissionCodes.InventoryReorderManage, magazijn);
        Assert.Contains(PermissionCodes.InventoryLoansView, magazijn);
        Assert.DoesNotContain(PermissionCodes.InventoryManageThresholds, magazijn);
        Assert.DoesNotContain(PermissionCodes.InventoryOverrideNegativeStock, magazijn);
        Assert.DoesNotContain(PermissionCodes.TasksViewAll, magazijn);

        // Management owns thresholds, the full task surface, bulk/portal messaging and escalations.
        var management = await CodesOfAsync(db, RoleId("management"));
        Assert.Contains(PermissionCodes.InventoryManageThresholds, management);
        Assert.Contains(PermissionCodes.TasksViewAll, management);
        Assert.Contains(PermissionCodes.TasksReopen, management);
        Assert.Contains(PermissionCodes.MessagesSendBulk, management);
        Assert.Contains(PermissionCodes.PortalMessagesSendBulk, management);
        Assert.Contains(PermissionCodes.EscalationsManage, management);

        // HR coordinates team tasks and bulk employee messages, but no stock thresholds and
        // no tenant-wide task visibility.
        var hr = await CodesOfAsync(db, RoleId("hr"));
        Assert.Contains(PermissionCodes.TasksViewTeam, hr);
        Assert.Contains(PermissionCodes.TasksAssign, hr);
        Assert.Contains(PermissionCodes.MessagesSendBulk, hr);
        Assert.DoesNotContain(PermissionCodes.TasksViewAll, hr);
        Assert.DoesNotContain(PermissionCodes.InventoryManageThresholds, hr);

        // Drivers stay personal-scope only; portal templates get nothing from this sprint.
        var chauffeur = await CodesOfAsync(db, RoleId("chauffeur"));
        Assert.DoesNotContain(PermissionCodes.TasksViewTeam, chauffeur);
        Assert.DoesNotContain(PermissionCodes.MessagesSendBulk, chauffeur);
        foreach (var portal in roles.Where(r => r.TemplateCode != null && r.TemplateCode.StartsWith("klantportaal")))
        {
            var codes = await CodesOfAsync(db, portal.Id);
            Assert.DoesNotContain(PermissionCodes.TasksViewOwn, codes);
            Assert.DoesNotContain(PermissionCodes.PortalMessagesSend, codes);
        }
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
