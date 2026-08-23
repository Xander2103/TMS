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
    /// kleine sleutelruimte, dus naast de device-/credential-lockout mag één bron nooit aan
    /// gokrate komen. Partitionering per DEVICE (uit de X-Kiosk-Device-header) zodat een
    /// ploegwissel aan één prikklok — of meerdere prikklokken achter één NAT — elkaar niet
    /// uithongeren; requests zonder parseerbaar device-id delen de per-IP-bucket. 60/minuut
    /// per device dekt piekverkeer (ping + identify + punch per medewerker) ruim, terwijl
    /// bruteforce primair door de device-lockout met backoff wordt gesmoord.</summary>
    public const string KioskPolicy = "kiosk";

    private const int PermitLimit = 10;
    private const int WebhookPermitLimit = 60;
    private const int SessionPermitLimit = 30;
    private const int KioskPermitLimit = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The real client IP: behind a known proxy <c>UseForwardedHeaders</c> has already replaced
    /// <c>RemoteIpAddress</c> with the forwarded value, so partitioning on it is correct and an
    /// unknown proxy cannot spoof its way into someone else's bucket.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Kioskpartitie: het device-id vóór de punt in de X-Kiosk-Device-header (alleen het
    /// publieke id, nooit het secret). Ongeldige/ontbrekende headers vallen terug op de
    /// per-IP-bucket zodat header-spam geen onbeperkte verse buckets kan claimen — dat pad
    /// haalt sowieso nooit een geldige identificatie.
    /// </summary>
    private static string KioskKey(HttpContext context)
    {
        var header = context.Request.Headers["X-Kiosk-Device"].ToString();
        var separator = header.IndexOf('.');
        if (separator > 0 && Guid.TryParseExact(header[..separator], "N", out var deviceId))
        {
            return $"dev:{deviceId:N}";
        }

        return $"ip:{ClientKey(context)}";
    }

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
                    code = TransportationService.Api.Common.ErrorCodes.RateLimited,
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
                    partitionKey: KioskKey(httpContext),
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
