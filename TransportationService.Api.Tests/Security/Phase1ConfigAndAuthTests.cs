using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Security;
using TransportationService.Api.Modules.Tenancy;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 1 (C1, C3-response-leak, H15): authentication is enforced fail-closed, the dev
/// impersonation headers cannot leak into non-Development, no default-tenant fallback exists,
/// startup refuses unsafe configuration, and the burned committed signing key is rejected.
/// </summary>
public class Phase1ConfigAndAuthTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ClaimsPrincipal Authenticated(Guid? userId = null, Guid? tenantId = null)
    {
        var claims = new List<Claim>();
        if (userId is { } u) claims.Add(new Claim("sub", u.ToString()));
        if (tenantId is { } t) claims.Add(new Claim(AppClaimTypes.TenantId, t.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestJwt"));
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static IHeaderDictionary DevHeaders(Guid? userId, Guid? tenantId)
    {
        var headers = new HeaderDictionary();
        if (userId is { } u) headers[TenantContextMiddleware.UserHeaderName] = u.ToString();
        if (tenantId is { } t) headers[TenantContextMiddleware.TenantHeaderName] = t.ToString();
        return headers;
    }

    // --- C1: identity provenance is the validated principal ---

    [Fact]
    public void Resolve_AuthenticatedPrincipal_UsesClaims_IgnoringDevHeaders()
    {
        var spoofUser = Guid.NewGuid();
        var result = TenantContextMiddleware.Resolve(
            Authenticated(User, Tenant), isDevelopment: true, allowImpersonationHeaders: true,
            DevHeaders(spoofUser, Guid.NewGuid()));

        Assert.Equal(User, result.UserId);
        Assert.Equal(Tenant, result.TenantId);
    }

    [Fact]
    public void Resolve_AuthenticatedWithoutTenantClaim_DoesNotSubstituteDefault()
    {
        var result = TenantContextMiddleware.Resolve(
            Authenticated(User, tenantId: null), isDevelopment: false, allowImpersonationHeaders: false,
            new HeaderDictionary());

        Assert.Equal(User, result.UserId);
        Assert.Null(result.TenantId); // fail-closed: no default/oldest tenant
    }

    [Fact]
    public void Resolve_DevHeaders_IgnoredOutsideDevelopment()
    {
        var result = TenantContextMiddleware.Resolve(
            Anonymous(), isDevelopment: false, allowImpersonationHeaders: false, DevHeaders(User, Tenant));

        Assert.Null(result.UserId);
        Assert.Null(result.TenantId);
    }

    [Fact]
    public void Resolve_DevHeaders_IgnoredWithoutOptIn_EvenInDevelopment()
    {
        var result = TenantContextMiddleware.Resolve(
            Anonymous(), isDevelopment: true, allowImpersonationHeaders: false, DevHeaders(User, Tenant));

        Assert.Null(result.UserId);
        Assert.Null(result.TenantId);
    }

    [Fact]
    public void Resolve_DevHeaders_HonouredOnlyInDevelopmentWithOptIn()
    {
        var result = TenantContextMiddleware.Resolve(
            Anonymous(), isDevelopment: true, allowImpersonationHeaders: true, DevHeaders(User, Tenant));

        Assert.Equal(User, result.UserId);
        Assert.Equal(Tenant, result.TenantId);
    }

    [Fact]
    public void Resolve_UnauthenticatedNonDev_HasNoAmbientContext()
    {
        var result = TenantContextMiddleware.Resolve(
            Anonymous(), isDevelopment: false, allowImpersonationHeaders: false, new HeaderDictionary());

        Assert.Null(result.UserId);
        Assert.Null(result.TenantId);
    }

    // --- C1: fallback authorization policy requires an authenticated user ---

    [Fact]
    public async Task AddJwtAuthentication_RegistersFallbackPolicyRequiringAuthentication()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "iss",
            ["Jwt:Audience"] = "aud",
            ["Jwt:SigningKey"] = "a-real-looking-random-signing-key-32b+",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuthentication(configuration);
        var provider = services.BuildServiceProvider();

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    // --- C1: no controller action is unintentionally anonymous ---

    [Fact]
    public void AllowAnonymousActions_AreLimitedToAnExplicitAllowlist()
    {
        // Any endpoint reachable without authentication must be a deliberate, reviewed choice.
        // Adding [AllowAnonymous] anywhere else fails this test until it is added here on purpose.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "HealthController.Get",
            "AuthController.Login",
            "AuthController.Refresh",
            "AuthController.ForgotPassword",
            "AuthController.ResetPassword",
            "PeppolWebhookController.Receive",
        };

        var assembly = typeof(TenantContextMiddleware).Assembly;
        var anonymousActions = new List<string>();
        foreach (var controller in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var controllerAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                if (controllerAnonymous || method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                {
                    anonymousActions.Add($"{controller.Name}.{method.Name}");
                }
            }
        }

        var unexpected = anonymousActions.Where(a => !allowed.Contains(a)).ToList();
        Assert.True(unexpected.Count == 0,
            "Unexpected [AllowAnonymous] endpoints (review before allowlisting): " + string.Join(", ", unexpected));
    }

    // --- H15: signing-key validation rejects the burned key, placeholders and short keys ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    [InlineData("dev-only-signing-key-change-me-32bytes-minimum!!")]
    [InlineData("change-me-change-me-change-me-change-me")]
    [InlineData("0000000000000000000000000000000000000000")]
    public void JwtOptionsValidator_RejectsUnsafeKeys(string key)
    {
        var result = new JwtOptionsValidator().Validate(null, new JwtOptions
        {
            Issuer = "iss", Audience = "aud", SigningKey = key,
        });

        Assert.True(result.Failed, $"Expected key to be rejected: '{key}'");
    }

    [Fact]
    public void JwtOptionsValidator_AcceptsAStrongKey()
    {
        var result = new JwtOptionsValidator().Validate(null, new JwtOptions
        {
            Issuer = "iss", Audience = "aud",
            SigningKey = "9f3c1a7e-b28d-4c60-a1f2-7e5d9b04c8e1-Qk7pZ",
        });

        Assert.True(result.Succeeded);
    }

    // --- C1/H15: startup validator fails fast on unsafe production configuration ---

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class RealEmailProvider : IEmailProvider
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IServiceProvider ServicesWith(IEmailProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(provider);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void StartupValidator_Throws_WhenImpersonationEnabledOutsideDevelopment()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dev:AllowImpersonationHeaders"] = "true",
        }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityValidator.Validate(new TestEnvironment("Production"), config, ServicesWith(new RealEmailProvider())));
    }

    [Fact]
    public void StartupValidator_Throws_WhenNoRealEmailProviderOutsideDevelopment()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityValidator.Validate(new TestEnvironment("Production"), config, ServicesWith(new UnconfiguredEmailProvider())));
    }

    [Fact]
    public void StartupValidator_Passes_InProductionWithRealProviderAndNoImpersonation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Fase 9: a non-Development host also needs the column-encryption key.
            ["ColumnEncryption:Key"] = Convert.ToBase64String(new byte[32]),
        }).Build();

        // Should not throw.
        StartupSecurityValidator.Validate(new TestEnvironment("Production"), config, ServicesWith(new RealEmailProvider()));
    }

    [Fact]
    public void StartupValidator_Throws_WithoutColumnEncryptionKeyOutsideDevelopment()
    {
        var config = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityValidator.Validate(new TestEnvironment("Production"), config, ServicesWith(new RealEmailProvider())));
        Assert.Contains("ColumnEncryption", exception.Message);
    }

    [Fact]
    public void StartupValidator_SkipsChecksInDevelopment()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dev:AllowImpersonationHeaders"] = "true",
        }).Build();

        // Development is allowed to use the sink + impersonation headers; must not throw.
        StartupSecurityValidator.Validate(new TestEnvironment("Development"), config, ServicesWith(new UnconfiguredEmailProvider()));
    }

    // --- C3 (response-leak): the fail-closed placeholder provider never silently sends ---

    [Fact]
    public async Task UnconfiguredEmailProvider_ThrowsRatherThanLeakingOrDroppingMail()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UnconfiguredEmailProvider().SendAsync(new OutboxMessage(), CancellationToken.None));
    }
}
