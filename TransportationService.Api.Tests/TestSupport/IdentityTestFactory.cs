using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Builds the identity services with their post-hardening dependency graph (acting-user context
/// + shared AccountSecurityService), so the privilege-escalation guards are exercised the same way
/// they are in production.
/// </summary>
public static class IdentityTestFactory
{
    public static UserService UserService(SqliteTestDbContext db, Guid tenantId, Guid? actorUserId)
    {
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(actorUserId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var accountSecurity = new AccountSecurityService(
            db.Context, tenant, currentUser, new PermissionSetService(db.Context), TimeProvider.System);
        return new UserService(db.Context, tenant, audit, new PasswordHasher(), currentUser, accountSecurity,
            TransportationService.Api.Modules.Authentication.PasswordPolicy.Default);
    }

    public static RoleService RoleService(SqliteTestDbContext db, Guid tenantId, Guid? actorUserId)
    {
        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(actorUserId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var accountSecurity = new AccountSecurityService(
            db.Context, tenant, currentUser, new PermissionSetService(db.Context), TimeProvider.System);
        return new RoleService(db.Context, tenant, audit, currentUser, accountSecurity);
    }

    public static AccountSecurityService AccountSecurity(SqliteTestDbContext db, Guid tenantId, Guid? actorUserId) =>
        new(db.Context, new DevTenantContext(tenantId), new DevCurrentUserContext(actorUserId),
            new PermissionSetService(db.Context), TimeProvider.System);
}
