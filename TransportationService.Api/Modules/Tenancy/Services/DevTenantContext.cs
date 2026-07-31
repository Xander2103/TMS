namespace TransportationService.Api.Modules.Tenancy.Services;

/// <summary>
/// Fixed-tenant context: the value <see cref="TenantContextMiddleware"/> stores on the request
/// once the tenant is resolved (from the validated JWT principal, or the Development-only
/// impersonation headers). Also used directly in tests. Doubles as an always-resolved
/// <see cref="ITenantAccessor"/>.
/// </summary>
public class DevTenantContext : ITenantContext, ITenantAccessor
{
    public Guid TenantId { get; }

    public DevTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public bool HasTenant => true;

    public bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = TenantId;
        return true;
    }
}
