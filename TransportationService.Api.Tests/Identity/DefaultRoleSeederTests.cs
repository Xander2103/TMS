using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Identity;

public class DefaultRoleSeederTests
{
    private static async Task<(SqliteTestDbContext Db, Guid TenantId)> SeedTenantWithCatalogAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await PermissionCatalogSeeder.SyncAsync(db.Context);
        return (db, tenantId);
    }

    [Fact]
    public async Task SyncAsync_CreatesDefaultRoles_WithExpectedPermissions()
    {
        var (db, tenantId) = await SeedTenantWithCatalogAsync();
        using var _ = db;

        await DefaultRoleSeeder.SyncAsync(db.Context);

        var roles = await db.Context.Roles.Where(r => r.TenantId == tenantId).ToListAsync();
        Assert.Equal(
            new[] { "Boekhouding", "Chauffeur", "Dispatcher", "HR", "Klantportaal", "Management", "Planner" },
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

        Assert.Equal(7, await db.Context.Roles.CountAsync(r => r.TenantId == tenantId));
        Assert.False(await db.Context.RolePermissions
            .AnyAsync(rp => rp.RoleId == planner.Id && rp.PermissionId == cancelPermission.Id));
    }
}
