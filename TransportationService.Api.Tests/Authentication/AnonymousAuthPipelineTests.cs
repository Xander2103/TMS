using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Controllers;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Tests.Authentication;

/// <summary>
/// Full-pipeline regression tests for the login-500: the anonymous identity endpoints must be
/// servable WITHOUT a resolved tenant context, while the fail-closed tenant guard keeps rejecting
/// unauthenticated business requests. The host mirrors the production pipeline order exactly
/// (authentication → tenant context → authorization) with the real controllers, middleware and
/// DI registrations from the API assembly — only the database (SQLite in-memory) and
/// configuration are test-local.
/// </summary>
public sealed class AnonymousAuthPipelineTests : IAsyncLifetime
{
    private const string Password = "Sterk!Wachtwoord9";
    private const string AmbiguousEmail = "shared@example.com";

    private SqliteConnection _connection = null!;
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Issuer"] = "pipeline-test-issuer",
                        ["Jwt:Audience"] = "pipeline-test-audience",
                        ["Jwt:SigningKey"] = "pipeline-test-signing-key-with-enough-length!!",
                    }));
                webHost.ConfigureServices((context, services) =>
                {
                    services
                        .AddControllers(options =>
                        {
                            options.Filters.Add<TransportationService.Api.Common.InvalidTenantReferenceExceptionFilter>();
                            options.Filters.Add<TransportationService.Api.Common.DomainValidationExceptionFilter>();
                            options.Filters.Add<TransportationService.Api.Modules.Identity.Authorization.AccountStateAuthorizationFilter>();
                        })
                        .AddApplicationPart(typeof(AuthController).Assembly);

                    services.AddProblemDetails();
                    services.AddJwtAuthentication(context.Configuration);
                    services.AddAuthRateLimiting();

                    services.AddHttpContextAccessor();
                    services.AddTenantContextAccessors();
                    services.AddSingleton<ITenantQueryFilterAccessor, HttpTenantQueryFilterAccessor>();
                    services.AddSingleton(TimeProvider.System);

                    services.AddDbContext<TransportationDbContext>(options => options.UseSqlite(_connection));
                    services.AddScoped<IAuditService, AuditService>();
                    services.AddScoped<IUserAccountFlowService, UserAccountFlowService>();

                    // Background sweeps (token retention) are irrelevant here and would only race
                    // the in-memory database.
                    services.RemoveAll<IHostedService>();
                });
                webHost.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseRateLimiter();
                    app.UseMiddleware<TenantContextMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        _host = await hostBuilder.StartAsync();
        _client = _host.GetTestServer().CreateClient();

        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();
        await db.Database.EnsureCreatedAsync();

        _tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var hash = new PasswordHasher().Hash(Password);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = now },
            new Tenant { Id = otherTenantId, Name = "Globex", Slug = "globex", IsActive = true, CreatedAt = now });
        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, Email = "jan@acme.be",
                FirstName = "Jan", LastName = "Janssens", PasswordHash = hash,
                IsActive = true, CreatedAt = now, UpdatedAt = now,
            },
            // The same email in TWO tenants: the M4 disambiguation guard must refuse the login
            // rather than picking either row.
            new User
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, Email = AmbiguousEmail,
                FirstName = "Ann", LastName = "Acme", PasswordHash = hash,
                IsActive = true, CreatedAt = now, UpdatedAt = now,
            },
            new User
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, Email = AmbiguousEmail,
                FirstName = "Gert", LastName = "Globex", PasswordHash = hash,
                IsActive = true, CreatedAt = now, UpdatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _connection.Dispose();
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Login_WithoutBearerToken_DoesNotProduceA500()
    {
        using var response = await _client.PostAsync(
            "/api/auth/login", Json(new { email = "nobody@acme.be", password = "whatever-wrong" }));

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200_WithTokensAndRefreshCookie()
    {
        using var response = await _client.PostAsync(
            "/api/auth/login", Json(new { email = "jan@acme.be", password = Password }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("accessToken").GetString()));
        // H5 session architecture: the raw refresh token lives in the HttpOnly cookie only.
        Assert.Equal(string.Empty, body.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(_tenantId, body.RootElement.GetProperty("user").GetProperty("tenantId").GetGuid());

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("ts_refresh=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsTheUniformUnauthorizedProblem()
    {
        using var response = await _client.PostAsync(
            "/api/auth/login", Json(new { email = "jan@acme.be", password = "totally-wrong" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Invalid credentials", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_AccountInASingleTenant_LandsInExactlyThatTenant()
    {
        using var response = await _client.PostAsync(
            "/api/auth/login", Json(new { email = "jan@acme.be", password = Password }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_tenantId, body.RootElement.GetProperty("user").GetProperty("tenantId").GetGuid());
        Assert.Equal("Acme", body.RootElement.GetProperty("user").GetProperty("tenantName").GetString());
    }

    [Fact]
    public async Task Login_EmailExistingInMultipleTenants_IsRefused_NotSilentlyAssigned()
    {
        // M4: ambiguous accounts are refused with the uniform response — never "first tenant wins".
        using var response = await _client.PostAsync(
            "/api/auth/login", Json(new { email = AmbiguousEmail, password = Password }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForgotPassword_WithoutJwt_Returns204_NoTenantContextException()
    {
        using var response = await _client.PostAsync(
            "/api/auth/forgot-password", Json(new { email = "jan@acme.be" }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_IsIndistinguishable_NoExistenceOracle()
    {
        using var known = await _client.PostAsync(
            "/api/auth/forgot-password", Json(new { email = "jan@acme.be" }));
        using var unknown = await _client.PostAsync(
            "/api/auth/forgot-password", Json(new { email = "bestaat-niet@acme.be" }));

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetPassword_WithoutJwt_ReturnsTheUniformBadRequest_NoTenantContextException()
    {
        using var response = await _client.PostAsync(
            "/api/auth/reset-password", Json(new { token = "not-a-real-token", newPassword = "NieuwWachtwoord1!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ongeldig of verlopen", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BusinessEndpoint_WithoutJwt_Remains401()
    {
        using var response = await _client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
