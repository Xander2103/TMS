using System.Security.Claims;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy;

/// <summary>
/// Resolves the current tenant and user for the request pipeline. Identity provenance is
/// ALWAYS the validated JWT principal (<see cref="HttpContext.User"/>) — never a client-supplied
/// header. The <c>X-Dev-*</c> impersonation headers are honoured strictly in Development and only
/// when explicitly opted in via <c>Dev:AllowImpersonationHeaders</c>; in every other environment
/// they are ignored completely.
///
/// Fail-closed: this middleware never falls back to a default/oldest tenant. A request that cannot
/// be resolved gets NO ambient context — protected endpoints are then rejected by the authorization
/// fallback policy / permission filter, and anonymous endpoints (login, webhook) resolve their own
/// scope. Runs after <c>UseAuthentication</c> so the principal is already populated.
/// </summary>
public class TenantContextMiddleware
{
    public const string TenantHeaderName = "X-Dev-Tenant-Id";
    public const string UserHeaderName = "X-Dev-User-Id";
    public const string ImpersonationFlag = "Dev:AllowImpersonationHeaders";

    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;
    private readonly bool _allowImpersonationHeaders;

    public TenantContextMiddleware(RequestDelegate next, IWebHostEnvironment environment, IConfiguration configuration)
    {
        _next = next;
        _environment = environment;
        // The opt-in only ever takes effect in Development; StartupSecurityValidator additionally
        // refuses to boot a non-Development host that has the flag enabled.
        _allowImpersonationHeaders = environment.IsDevelopment() && configuration.GetValue<bool>(ImpersonationFlag);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var resolution = Resolve(
            context.User, _environment.IsDevelopment(), _allowImpersonationHeaders, context.Request.Headers);

        if (resolution.TenantId is { } tenantId)
        {
            context.Items[nameof(ITenantContext)] = new DevTenantContext(tenantId);
        }

        if (resolution.UserId is { } userId)
        {
            context.Items[nameof(ICurrentUserContext)] = new DevCurrentUserContext(userId);
        }

        await _next(context);
    }

    public readonly record struct TenantResolution(Guid? TenantId, Guid? UserId);

    /// <summary>
    /// Pure, side-effect-free resolution of the request's tenant/user context. Unit-tested in
    /// isolation. An authenticated principal always wins; a missing tenant claim is treated as a
    /// malformed token and is NOT substituted with a default tenant (fail-closed).
    /// </summary>
    public static TenantResolution Resolve(
        ClaimsPrincipal user, bool isDevelopment, bool allowImpersonationHeaders, IHeaderDictionary headers)
    {
        if (user.Identity?.IsAuthenticated == true)
        {
            return new TenantResolution(user.GetTenantId(), user.GetUserId());
        }

        if (isDevelopment && allowImpersonationHeaders)
        {
            return new TenantResolution(
                ParseHeaderGuid(headers, TenantHeaderName), ParseHeaderGuid(headers, UserHeaderName));
        }

        return new TenantResolution(null, null);
    }

    private static Guid? ParseHeaderGuid(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var value) && Guid.TryParse(value, out var parsed) ? parsed : null;
}

public static class TenantContextServiceCollectionExtensions
{
    /// <summary>
    /// The mandatory contexts are lazy proxies (<see cref="Services.RequestTenantContext"/>):
    /// resolving them from DI always succeeds — the fail-closed exception moves to the moment
    /// tenant/user data is actually READ without a resolved principal. Throwing at resolution
    /// time made every [AllowAnonymous] endpoint 500 whose controller merely depends on a
    /// tenant-aware service. <see cref="Services.ITenantAccessor"/> is the optional counterpart
    /// for identity flows that legitimately run without a tenant.
    /// </summary>
    public static IServiceCollection AddTenantContextAccessors(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, Services.RequestTenantContext>();
        services.AddScoped<ICurrentUserContext, Services.RequestCurrentUserContext>();
        services.AddScoped<Services.ITenantAccessor, Services.RequestTenantAccessor>();
        return services;
    }
}
