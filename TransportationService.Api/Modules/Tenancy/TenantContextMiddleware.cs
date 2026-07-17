using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy;

/// <summary>
/// Resolves the current tenant and (dev-mode) current user from trusted
/// development headers and registers scoped ITenantContext / ICurrentUserContext
/// instances for the rest of the request pipeline. This is the single seam
/// to replace when real authentication (JWT claims) is introduced.
/// </summary>
public class TenantContextMiddleware
{
    public const string TenantHeaderName = "X-Dev-Tenant-Id";
    public const string UserHeaderName = "X-Dev-User-Id";

    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, TransportationDbContext dbContext)
    {
        var tenantId = await ResolveTenantIdAsync(context, dbContext);
        var userId = ResolveUserId(context);

        context.Items[nameof(ITenantContext)] = new DevTenantContext(tenantId);
        context.Items[nameof(ICurrentUserContext)] = new DevCurrentUserContext(userId);

        await _next(context);
    }

    private static async Task<Guid> ResolveTenantIdAsync(HttpContext context, TransportationDbContext dbContext)
    {
        if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed))
        {
            return parsed;
        }

        var defaultTenantId = await dbContext.Tenants
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .FirstOrDefaultAsync();

        return defaultTenantId;
    }

    private static Guid? ResolveUserId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(UserHeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed))
        {
            return parsed;
        }

        return null;
    }
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
