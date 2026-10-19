using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using TransportationService.Api.Common.Diagnostics;
using TransportationService.Api.Common.Health;
using TransportationService.Api.Common.Jobs;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Controllers;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Security;
using TransportationService.Api.Modules.Tenancy;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>One captured log event with the scope text a console formatter with scopes prints.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, string Message, string Scopes, Exception? Exception);

/// <summary>
/// In-memory logger that renders scopes exactly like the console formatter does (it walks the
/// external scope provider), so a test can prove the correlation id reaches every log line.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = new StringBuilder();
            owner._scopes.ForEachScope((scope, builder) => builder.Append(" => ").Append(scope), scopes);
            owner.Entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), scopes.ToString(), exception));
        }
    }
}

/// <summary>
/// TestServer host that mirrors the production pipeline order of Program.cs (request logging →
/// exception handler → security headers → authentication → rate limiter → tenant context →
/// authorization → endpoints) with the REAL registrations under test: problem details + 503
/// mapping, operational logging, health checks and job coordination. Only the database and the
/// configuration are test-local.
/// </summary>
public static class OperationalPipelineHost
{
    public static async Task<IHost> StartAsync(
        Action<DbContextOptionsBuilder> database,
        CapturingLoggerProvider? logs = null,
        Action<IServiceCollection>? configureServices = null,
        Action<IEndpointRouteBuilder>? mapExtra = null)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseEnvironment("Production");
                webHost.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Issuer"] = "pipeline-test-issuer",
                        ["Jwt:Audience"] = "pipeline-test-audience",
                        ["Jwt:SigningKey"] = "pipeline-test-signing-key-with-enough-length!!",
                    }));
                webHost.ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddOperationalConsoleLogging(context.HostingEnvironment);
                    // The console provider stays registered (its options are asserted) but silent.
                    logging.AddFilter<ConsoleLoggerProvider>(_ => false);
                    logging.SetMinimumLevel(LogLevel.Debug);
                    if (logs is not null)
                    {
                        logging.AddProvider(logs);
                    }
                });
                webHost.ConfigureServices((context, services) =>
                {
                    services
                        .AddControllers(options =>
                            options.Filters.Add<TransportationService.Api.Modules.Identity.Authorization.AccountStateAuthorizationFilter>())
                        .AddApplicationPart(typeof(AuthController).Assembly);

                    services.AddApiProblemDetails();
                    services.AddJwtAuthentication(context.Configuration);
                    services.AddAuthRateLimiting();
                    services.AddHttpContextAccessor();
                    services.AddTenantContextAccessors();
                    services.AddSingleton<ITenantQueryFilterAccessor, HttpTenantQueryFilterAccessor>();
                    services.AddSingleton(TimeProvider.System);
                    services.AddDbContext<TransportationDbContext>(database);
                    services.AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>();

                    services.AddOperationalHealthChecks();
                    services.AddBackgroundJobCoordination();
                    services.AddSingleton<ICalendarProvider, NotConfiguredCalendarProvider>();
                    services.AddSingleton<ISmsProvider, UnconfiguredSmsProvider>();

                    configureServices?.Invoke(services);
                    services.RemoveAll<IHostedService>();
                });
                webHost.Configure(app =>
                {
                    app.UseMiddleware<RequestLoggingMiddleware>();
                    app.UseExceptionHandler();
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseRateLimiter();
                    app.UseMiddleware<TenantContextMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapOperationalHealthEndpoints();
                        endpoints.MapControllers();
                        mapExtra?.Invoke(endpoints);
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }
}
