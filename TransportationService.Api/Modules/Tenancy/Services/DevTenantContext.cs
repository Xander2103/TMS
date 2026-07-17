namespace TransportationService.Api.Modules.Tenancy.Services;

/// <summary>
/// Development-only tenant resolution. Reads the trusted X-Dev-Tenant-Id
/// header set by TenantContextMiddleware. Replace with a claims-based
/// implementation once JWT authentication issues a tenant_id claim -
/// no consuming service should need to change.
/// </summary>
public class DevTenantContext : ITenantContext
{
    public Guid TenantId { get; }

    public DevTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
