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

    private const int PermitLimit = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

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
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));
        });

        return services;
    }
}
