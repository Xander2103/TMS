using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Iteration 2 (C2, H2, H3): no actor can obtain — via user management, password reset, role
/// assignment or permission assignment — any privilege they do not already hold, and system /
/// customer-portal roles are structurally protected.
/// </summary>
public class Phase2PrivilegeEscalationTests
{
    private const string ResetPw = "users.reset_password";
    private const string UsersView = "users.view";
    private const string UsersEdit = "users.edit";
    private const string FinanceView = "finance.view";
    private const string PortalDocs = "customer_portal.view_documents";

    private sealed class World
    {
        public required SqliteTestDbContext Db;
        public Guid TenantId;
        public Dictionary<string, Guid> PermissionIds = new();
    }

    private static async Task<World> SeedAsync(params string[] catalogCodes)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = "t", IsActive = true, CreatedAt = DateTime.UtcNow });
        var world = new World { Db = db, TenantId = tenantId };
        foreach (var code in catalogCodes.Distinct())
        {
            var id = Guid.NewGuid();
            world.PermissionIds[code] = id;
            var parts = code.Split('.', 2);
            db.Context.Permissions.Add(new Permission { Id = id, Code = code, Module = parts[0], Action = parts.Length > 1 ? parts[1] : code, Description = code });
        }

        await db.Context.SaveChangesAsync();
        return world;
    }

    private static async Task<Guid> AddRoleAsync(World w, string name, bool system, params string[] codes)
    {
        var roleId = Guid.NewGuid();
        w.Db.Context.Roles.Add(new Role
        {
            Id = roleId, TenantId = w.TenantId, Name = name, IsSystemRole = system, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        foreach (var code in codes)
        {
            w.Db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = w.PermissionIds[code] });
        }

        await w.Db.Context.SaveChangesAsync();
        return roleId;
    }

    private static async Task<Guid> AddUserAsync(World w, string email, params Guid[] roleIds)
    {
        var userId = Guid.NewGuid();
        w.Db.Context.Users.Add(new User
        {
            Id = userId, TenantId = w.TenantId, Email = email, FirstName = "F", LastName = "L",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        foreach (var roleId in roleIds)
        {
            w.Db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        await w.Db.Context.SaveChangesAsync();
        return userId;
    }

    private static async Task<string> PortalTemplateRoleAsync(World w)
    {
        // A role flagged as a customer-portal template (matched by TemplateCode prefix).
        var roleId = Guid.NewGuid();
        var role = new Role
        {
            Id = roleId, TenantId = w.TenantId, Name = "Klantportaal", TemplateCode = "klantportaal",
            IsSystemRole = false, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        w.Db.Context.Roles.Add(role);
        await w.Db.Context.SaveChangesAsync();
        return roleId.ToString();
    }

    // ===================== C2 — administrative password reset =====================

    [Fact]
    public async Task Reset_HigherPrivilegedTarget_IsForbidden()
    {
        var w = await SeedAsync(ResetPw, UsersView, FinanceView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, ResetPw, UsersView);
        var highRole = await AddRoleAsync(w, "High", false, FinanceView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var target = await AddUserAsync(w, "high@t", highRole);
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.SetPasswordAsync(target, "a-new-password", CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Reset_SystemAdministratorTarget_IsForbidden()
    {
        var w = await SeedAsync(ResetPw, UsersView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, ResetPw, UsersView);
        var adminRole = await AddRoleAsync(w, "Administrator", true, ResetPw, UsersView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.SetPasswordAsync(admin, "a-new-password", CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Reset_Self_ViaAdminEndpoint_IsForbidden()
    {
        var w = await SeedAsync(ResetPw);
        using var _ = w.Db;
        var role = await AddRoleAsync(w, "Manager", false, ResetPw);
        var manager = await AddUserAsync(w, "manager@t", role);
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.SetPasswordAsync(manager, "a-new-password", CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Reset_AllowedByHigherPrivilege_RevokesSessions_SetsMustChange_AndAuditsWithoutSecrets()
    {
        var w = await SeedAsync(ResetPw, UsersView, FinanceView);
        using var _ = w.Db;
        var adminRole = await AddRoleAsync(w, "Administrator", true, ResetPw, UsersView, FinanceView);
        var lowRole = await AddRoleAsync(w, "Low", false, UsersView);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var target = await AddUserAsync(w, "low@t", lowRole);
        var originalStamp = (await w.Db.Context.Users.SingleAsync(u => u.Id == target)).SecurityStamp;
        w.Db.Context.Set<RefreshToken>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = target, TenantId = w.TenantId, TokenHash = "hash",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await w.Db.Context.SaveChangesAsync();
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, admin);

        var result = await sut.SetPasswordAsync(target, "a-brand-new-password", CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Success, result.Outcome);
        var reloaded = await w.Db.Context.Users.SingleAsync(u => u.Id == target);
        Assert.True(reloaded.MustChangePassword);
        Assert.NotEqual(originalStamp, reloaded.SecurityStamp); // access-token revocation
        Assert.All(await w.Db.Context.Set<RefreshToken>().Where(t => t.UserId == target).ToListAsync(),
            t => Assert.NotNull(t.RevokedAt)); // refresh-token revocation
        var audit = await w.Db.Context.AuditLogs.SingleAsync(a => a.EntityType == "User" && a.Action == "PasswordResetByAdmin");
        var blob = (audit.OldValuesJson ?? "") + (audit.NewValuesJson ?? "");
        Assert.DoesNotContain("password", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", blob, StringComparison.OrdinalIgnoreCase);
    }

    // ===================== H2 — role assignment =====================

    [Fact]
    public async Task AssignRoles_ToSelf_IsForbidden()
    {
        var w = await SeedAsync(UsersView);
        using var _ = w.Db;
        var role = await AddRoleAsync(w, "Manager", false, UsersView);
        var manager = await AddUserAsync(w, "manager@t", role);
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.AssignRolesAsync(manager, new AssignRolesRequest([role]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignRoles_GrantingRoleWithPermissionsActorLacks_IsForbidden()
    {
        var w = await SeedAsync(UsersView, FinanceView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var financeRole = await AddRoleAsync(w, "Finance", false, FinanceView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var target = await AddUserAsync(w, "target@t");
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.AssignRolesAsync(target, new AssignRolesRequest([financeRole]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignRoles_GrantingSystemRole_ByNonSystemActor_IsForbidden()
    {
        var w = await SeedAsync(UsersView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var systemRole = await AddRoleAsync(w, "Administrator", true, UsersView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var target = await AddUserAsync(w, "target@t");
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.AssignRolesAsync(target, new AssignRolesRequest([systemRole]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignRoles_RemovingHigherPrivilegedRoleFromTarget_IsForbidden()
    {
        var w = await SeedAsync(UsersView, FinanceView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var financeRole = await AddRoleAsync(w, "Finance", false, FinanceView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var target = await AddUserAsync(w, "target@t", financeRole);
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        // Removing the finance role (which the manager cannot manage) is an unauthorized change.
        var result = await sut.AssignRolesAsync(target, new AssignRolesRequest([]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignRoles_WithinActorPrivilege_Succeeds_AndAudits()
    {
        var w = await SeedAsync(UsersView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var grantable = await AddRoleAsync(w, "Viewer", false, UsersView);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var target = await AddUserAsync(w, "target@t");
        var sut = IdentityTestFactory.UserService(w.Db, w.TenantId, manager);

        var result = await sut.AssignRolesAsync(target, new AssignRolesRequest([grantable]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Success, result.Outcome);
        Assert.True(await w.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "User" && a.Action == "AssignRoles"));
    }

    // ===================== H3 — permission assignment =====================

    [Fact]
    public async Task AssignPermissions_GrantingPermissionActorLacks_IsForbidden()
    {
        var w = await SeedAsync(UsersView, FinanceView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var editableRole = await AddRoleAsync(w, "Editable", false);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, manager);

        var result = await sut.AssignPermissionsAsync(editableRole, new AssignPermissionsRequest([FinanceView]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignPermissions_WithinActorPrivilege_Succeeds()
    {
        var w = await SeedAsync(UsersView);
        using var _ = w.Db;
        var managerRole = await AddRoleAsync(w, "Manager", false, UsersView);
        var editableRole = await AddRoleAsync(w, "Editable", false);
        var manager = await AddUserAsync(w, "manager@t", managerRole);
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, manager);

        var result = await sut.AssignPermissionsAsync(editableRole, new AssignPermissionsRequest([UsersView]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Success, result.Outcome);
        Assert.Contains(UsersView, result.Role!.PermissionCodes);
    }

    [Fact]
    public async Task AssignPermissions_PortalRole_RejectsInternalPermission()
    {
        var w = await SeedAsync(UsersEdit, PortalDocs);
        using var _ = w.Db;
        var adminRole = await AddRoleAsync(w, "Administrator", true, UsersEdit, PortalDocs);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var portalRole = Guid.Parse(await PortalTemplateRoleAsync(w));
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, admin);

        var result = await sut.AssignPermissionsAsync(portalRole, new AssignPermissionsRequest([UsersEdit]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task AssignPermissions_PortalRole_RejectsWrongCasingInternalPermission()
    {
        var w = await SeedAsync(UsersEdit, PortalDocs);
        using var _ = w.Db;
        var adminRole = await AddRoleAsync(w, "Administrator", true, UsersEdit, PortalDocs);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var portalRole = Guid.Parse(await PortalTemplateRoleAsync(w));
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, admin);

        // Alternative casing must not smuggle an internal permission through — rejected as unknown.
        var result = await sut.AssignPermissionsAsync(portalRole, new AssignPermissionsRequest(["USERS.EDIT"]), CancellationToken.None);

        Assert.NotEqual(RoleOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task AssignPermissions_PortalRole_AllowsPortalPermission()
    {
        var w = await SeedAsync(PortalDocs);
        using var _ = w.Db;
        var adminRole = await AddRoleAsync(w, "Administrator", true, PortalDocs);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var portalRole = Guid.Parse(await PortalTemplateRoleAsync(w));
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, admin);

        var result = await sut.AssignPermissionsAsync(portalRole, new AssignPermissionsRequest([PortalDocs]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task AssignPermissions_UnknownCode_IsRejected()
    {
        var w = await SeedAsync(UsersView);
        using var _ = w.Db;
        var adminRole = await AddRoleAsync(w, "Administrator", true, UsersView);
        var admin = await AddUserAsync(w, "admin@t", adminRole);
        var editableRole = await AddRoleAsync(w, "Editable", false);
        var sut = IdentityTestFactory.RoleService(w.Db, w.TenantId, admin);

        var result = await sut.AssignPermissionsAsync(editableRole, new AssignPermissionsRequest(["does.not.exist"]), CancellationToken.None);

        Assert.Equal(RoleOperationOutcome.ValidationFailed, result.Outcome);
    }
}
