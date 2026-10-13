namespace TransportationService.Api.Modules.Security;

/// <summary>
/// Adds the baseline security response headers. The API serves JSON and file downloads only (the
/// SPA is hosted separately), so the policy is deliberately restrictive: nothing may be framed,
/// nothing may be sniffed, and no script/resource may be loaded from an API response.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>
    /// API-appropriate CSP: this origin never serves HTML/JS, so everything is denied and framing
    /// is blocked outright. It also neutralises any content that would otherwise be sniffed out of
    /// an uploaded file that gets echoed back.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; sandbox";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        headers["Cross-Origin-Resource-Policy"] = "same-site";

        // Authenticated API payloads must not linger in shared/browser caches.
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        headers["Pragma"] = "no-cache";

        // Never advertise the stack.
        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        return _next(context);
    }
}
