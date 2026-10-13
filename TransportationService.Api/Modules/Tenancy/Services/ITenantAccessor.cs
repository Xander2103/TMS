namespace TransportationService.Api.Modules.Tenancy.Services;

/// <summary>
/// Optional, information-only view of the request's tenant resolution, for services that serve
/// BOTH authenticated and anonymous flows (login, forgot/reset-password, activation). It reports
/// whether a tenant was resolved — it never throws for the anonymous case and it NEVER selects a
/// tenant itself (no default, no "first" tenant). Tenant-bound business code must keep using the
/// mandatory <see cref="ITenantContext"/>, which stays fail-closed.
/// </summary>
public interface ITenantAccessor
{
    bool HasTenant { get; }

    /// <summary>True with the resolved tenant, or false leaving <paramref name="tenantId"/> empty.</summary>
    bool TryGetTenantId(out Guid tenantId);
}
