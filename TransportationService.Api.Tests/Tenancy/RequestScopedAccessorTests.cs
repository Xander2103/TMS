using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Tests.Tenancy;

/// <summary>
/// The dependency-design contract behind the login-500 fix: resolving the tenant/user contexts
/// from DI must ALWAYS succeed (anonymous endpoints construct controllers whose graph contains
/// tenant-aware services), while READING them without a resolved request stays fail-closed. The
/// optional <see cref="ITenantAccessor"/> only reports — it never invents a tenant.
/// </summary>
public class RequestScopedAccessorTests
{
    private static ServiceProvider BuildProvider(HttpContext? httpContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddTenantContextAccessors();
        return services.BuildServiceProvider();
    }

    private static HttpContext ResolvedContext(Guid tenantId, Guid? userId)
    {
        var context = new DefaultHttpContext();
        context.Items[nameof(ITenantContext)] = new DevTenantContext(tenantId);
        context.Items[nameof(ICurrentUserContext)] = new DevCurrentUserContext(userId);
        return context;
    }

    [Fact]
    public void WithoutResolvedRequest_ResolvingTheContexts_Succeeds_ButReadingStaysFailClosed()
    {
        using var provider = BuildProvider(httpContext: null);
        using var scope = provider.CreateScope();

        // Construction (what controller activation does) must not throw ...
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var userContext = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();

        // ... the fail-closed guard fires on USE instead, and no fallback tenant is returned.
        var tenantError = Assert.Throws<InvalidOperationException>(() => tenantContext.TenantId);
        Assert.Contains("fail-closed", tenantError.Message);
        Assert.Throws<InvalidOperationException>(() => userContext.CurrentUserId);
    }

    [Fact]
    public void WithoutResolvedRequest_TheOptionalAccessor_ReportsNoTenant_AndNeverChoosesOne()
    {
        using var provider = BuildProvider(httpContext: new DefaultHttpContext());
        using var scope = provider.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<ITenantAccessor>();

        Assert.False(accessor.HasTenant);
        Assert.False(accessor.TryGetTenantId(out var tenantId));
        Assert.Equal(Guid.Empty, tenantId);
    }

    [Fact]
    public void WithResolvedRequest_AllAccessors_ExposeExactlyTheResolvedTenantAndUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var provider = BuildProvider(ResolvedContext(tenantId, userId));
        using var scope = provider.CreateScope();

        Assert.Equal(tenantId, scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId);
        Assert.Equal(userId, scope.ServiceProvider.GetRequiredService<ICurrentUserContext>().CurrentUserId);

        var accessor = scope.ServiceProvider.GetRequiredService<ITenantAccessor>();
        Assert.True(accessor.HasTenant);
        Assert.True(accessor.TryGetTenantId(out var reported));
        Assert.Equal(tenantId, reported);
    }
}
