using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Tenancy.Services;

/// <summary>
/// The mandatory, fail-closed tenant context. Constructing it never throws — controllers whose
/// dependency graph merely CONTAINS a tenant-aware service must remain buildable for anonymous
/// endpoints ([AllowAnonymous] login/forgot-password/webhook). The guard sits on USE instead:
/// reading <see cref="TenantId"/> on a request without a resolved tenant throws, so tenant-bound
/// business code stays fail-closed and no default/oldest tenant is ever substituted.
/// </summary>
public sealed class RequestTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId =>
        ((ITenantContext?)_httpContextAccessor.HttpContext?.Items[nameof(ITenantContext)])?.TenantId
        ?? throw new InvalidOperationException(
            "Tenant context was not resolved for this request. This is fail-closed behaviour: the "
            + "endpoint requires an authenticated principal with a tenant claim (see TenantContextMiddleware).");
}

/// <summary>Same lazy fail-closed pattern for the acting user (see <see cref="RequestTenantContext"/>).</summary>
public sealed class RequestCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentUserId =>
        (_httpContextAccessor.HttpContext?.Items[nameof(ICurrentUserContext)] as ICurrentUserContext
         ?? throw new InvalidOperationException(
             "Current user context was not resolved for this request. This is fail-closed behaviour: the "
             + "endpoint requires an authenticated principal (see TenantContextMiddleware).")).CurrentUserId;
}

/// <summary>The optional accessor (see <see cref="ITenantAccessor"/>): reports, never chooses.</summary>
public sealed class RequestTenantAccessor : ITenantAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestTenantAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool HasTenant => TryGetTenantId(out _);

    public bool TryGetTenantId(out Guid tenantId)
    {
        var resolved = (ITenantContext?)_httpContextAccessor.HttpContext?.Items[nameof(ITenantContext)];
        tenantId = resolved?.TenantId ?? Guid.Empty;
        return resolved is not null;
    }
}
