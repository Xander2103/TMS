using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy;

/// <summary>
/// Resolves the current tenant and current user for the request pipeline.
/// When the request is authenticated the values come exclusively from the JWT claims
/// (a client-supplied tenant id is never trusted in that case). Only when there is no
/// authenticated principal — and only in Development — does it fall back to the trusted
/// X-Dev-* headers, so Scalar/manual testing keeps working without a login.
/// Runs after UseAuthentication so <see cref="HttpContext.User"/> is already populated.
/// </summary>
public class TenantContextMiddleware
{
    public const string TenantHeaderName = "X-Dev-Tenant-Id";
    public const string UserHeaderName = "X-Dev-User-Id";

    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public TenantContextMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context, TransportationDbContext dbContext)
    {
        Guid tenantId;
        Guid? userId;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            userId = context.User.GetUserId();
            tenantId = context.User.GetTenantId() ?? await GetDefaultTenantIdAsync(dbContext);
        }
        else if (_environment.IsDevelopment())
        {
            userId = ResolveDevUserId(context);
            tenantId = ResolveDevTenantId(context) ?? await GetDefaultTenantIdAsync(dbContext);
        }
        else
        {
            userId = null;
            tenantId = await GetDefaultTenantIdAsync(dbContext);
        }

        context.Items[nameof(ITenantContext)] = new DevTenantContext(tenantId);
        context.Items[nameof(ICurrentUserContext)] = new DevCurrentUserContext(userId);

        await _next(context);
    }

    private static Task<Guid> GetDefaultTenantIdAsync(TransportationDbContext dbContext) =>
        dbContext.Tenants
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .FirstOrDefaultAsync();

    private static Guid? ResolveDevTenantId(HttpContext context) =>
        context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValue)
        && Guid.TryParse(headerValue, out var parsed)
            ? parsed
            : null;

    private static Guid? ResolveDevUserId(HttpContext context) =>
        context.Request.Headers.TryGetValue(UserHeaderName, out var headerValue)
        && Guid.TryParse(headerValue, out var parsed)
            ? parsed
            : null;
}

public static class TenantContextServiceCollectionExtensions
{
    public static IServiceCollection AddTenantContextAccessors(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext>(sp =>
            (ITenantContext?)sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[nameof(ITenantContext)]
            ?? throw new InvalidOperationException("Tenant context was not resolved. Ensure TenantContextMiddleware runs before this service is used."));

        services.AddScoped<ICurrentUserContext>(sp =>
            (ICurrentUserContext?)sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[nameof(ICurrentUserContext)]
            ?? throw new InvalidOperationException("Current user context was not resolved. Ensure TenantContextMiddleware runs before this service is used."));

        return services;
    }
}
