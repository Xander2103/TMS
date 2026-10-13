using Microsoft.AspNetCore.Http;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// Ambient tenant for the global EF query filter (H1). Inside a resolved request this is the
/// authenticated tenant; everywhere else (background jobs, seeders, migrations, anonymous
/// endpoints such as login/webhook that legitimately look across tenants) it is null and the
/// filter stays open. That null-path IS the documented bypass: system code runs tenant-agnostic
/// by construction, while every request-scoped query is fenced to its own tenant even when a
/// service forgets its explicit <c>Where(TenantId == …)</c>.
/// </summary>
public interface ITenantQueryFilterAccessor
{
    Guid? CurrentTenantIdOrNull { get; }
}

/// <summary>Reads the tenant the <c>TenantContextMiddleware</c> resolved for this request.</summary>
public sealed class HttpTenantQueryFilterAccessor : ITenantQueryFilterAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantQueryFilterAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentTenantIdOrNull =>
        (_httpContextAccessor.HttpContext?.Items[nameof(ITenantContext)] as ITenantContext)?.TenantId;
}

/// <summary>Fixed-tenant accessor for tests and tenant-pinned tooling.</summary>
public sealed class FixedTenantQueryFilterAccessor : ITenantQueryFilterAccessor
{
    public FixedTenantQueryFilterAccessor(Guid? tenantId) => CurrentTenantIdOrNull = tenantId;

    public Guid? CurrentTenantIdOrNull { get; }
}
