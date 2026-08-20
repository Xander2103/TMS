using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TransportationService.Api.Modules.Authentication;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>Fixed-window policy applied to the anonymous auth endpoints (login,
    /// forgot-password, reset-password/activation): 10 requests/minute per client IP, no
    /// queueing (the 11th request in the window is rejected immediately, not delayed).</summary>
    public const string AuthPolicy = "auth";

    /// <summary>Throttles the anonymous provider webhook so a shared secret cannot be brute-forced
    /// at line rate and a flood cannot amplify into database work.</summary>
    public const string WebhookPolicy = "webhook";

    /// <summary>Throttles token refresh/logout (authenticated but unauthenticated-reachable).</summary>
    public const string SessionPolicy = "session";

    /// <summary>Throttles the anonymous kiosk (prikklok) punch endpoints: PIN's leven in een
    /// kleine sleutelruimte, dus naast de credential-lockout mag één adres nooit aan gokrate
    /// komen. 15/minuut dekt normaal ploegverkeer (identify + punch per medewerker) ruim.</summary>
    public const string KioskPolicy = "kiosk";

    private const int PermitLimit = 10;
    private const int WebhookPermitLimit = 60;
    private const int SessionPermitLimit = 30;
    private const int KioskPermitLimit = 15;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The real client IP: behind a known proxy <c>UseForwardedHeaders</c> has already replaced
    /// <c>RemoteIpAddress</c> with the forwarded value, so partitioning on it is correct and an
    /// unknown proxy cannot spoof its way into someone else's bucket.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                var payload = JsonSerializer.Serialize(new
                {
                    type = $"https://httpstatuses.io/{StatusCodes.Status429TooManyRequests}",
                    title = "Te veel aanvragen",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Te veel pogingen vanaf dit adres. Probeer het over enkele minuten opnieuw.",
                });
                await context.HttpContext.Response.WriteAsync(payload, cancellationToken);
            };

            // Partitioned per client IP — AddFixedWindowLimiter(name, ...) alone would share ONE
            // global counter across every caller, which is not what "per IP" requires.
            options.AddPolicy(AuthPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));

            options.AddPolicy(WebhookPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = WebhookPermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));

            options.AddPolicy(SessionPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = SessionPermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));

            options.AddPolicy(KioskPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = KioskPermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));
        });

        return services;
    }
}
