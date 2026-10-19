using System.Net;

namespace TransportationService.Api.Modules.Security;

/// <summary>
/// CFG-06 / CONFIGURATION: the public URLs and the proxy list a real installation must set
/// explicitly. Run outside Development from <see cref="StartupSecurityValidator"/>; any problem
/// refuses the start with a message that names the key (values here are not secrets).
///
/// <list type="bullet">
/// <item><c>Frontend:BaseUrl</c> (activation/invite links): required, absolute <c>https://</c>,
/// no query or fragment, not loopback, not the demo domain.</item>
/// <item><c>Cors:AllowedOrigins</c>: at least one; every entry an <c>https://</c> origin (scheme +
/// host [+ port], no path/query), not loopback, not the demo domain.</item>
/// <item><c>Network:KnownProxies</c> / <c>Network:KnownNetworks</c>: every entry must parse (an
/// IP address / an <c>address/prefix</c> CIDR). Previously an invalid entry was skipped silently,
/// which disabled forwarded headers (wrong client IP, cookie without Secure).</item>
/// </list>
///
/// <c>Deployment:AllowNonProductionUrls=true</c> relaxes ONLY the URL rules (http, loopback, the
/// demo domain) for a demo or a local acceptance run in Production mode; it is reported as a
/// startup warning and must never be set for a real company installation.
/// </summary>
public static class DeploymentConfigurationValidator
{
    public const string AllowNonProductionUrlsKey = "Deployment:AllowNonProductionUrls";

    /// <summary>Shipped demo/sample hosts that a real installation must never point at.</summary>
    public static readonly IReadOnlyList<string> DemoHosts = ["tms-demo.vanmalderstudio.be"];

    public static void Validate(IConfiguration configuration)
    {
        var errors = Problems(configuration);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Deployment configuration is not valid for a non-Development environment: "
                + string.Join(" ", errors)
                + " Set the real public values (e.g. Frontend__BaseUrl, Cors__AllowedOrigins__0, "
                + "Network__KnownProxies__0 in /etc/transportationservice/tms.env); refusing to start.");
        }
    }

    /// <summary>Warnings worth one startup log line (relaxed URL mode, no proxies configured).</summary>
    public static IReadOnlyList<string> Warnings(IConfiguration configuration)
    {
        var warnings = new List<string>();
        if (configuration.GetValue<bool>(AllowNonProductionUrlsKey))
        {
            warnings.Add($"{AllowNonProductionUrlsKey} is enabled: http/loopback/demo URLs are accepted. "
                         + "Only for demo or acceptance hosts, never for a real installation.");
        }

        if ((configuration.GetSection("Network:KnownProxies").Get<string[]>() ?? []).Length == 0
            && (configuration.GetSection("Network:KnownNetworks").Get<string[]>() ?? []).Length == 0)
        {
            warnings.Add("Network:KnownProxies is empty: X-Forwarded-For/Proto are ignored, so behind a reverse "
                         + "proxy every client shares one rate-limit bucket and the refresh cookie is not Secure.");
        }

        return warnings;
    }

    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var errors = new List<string>();
        var relaxed = configuration.GetValue<bool>(AllowNonProductionUrlsKey);

        CheckUrl("Frontend:BaseUrl", configuration["Frontend:BaseUrl"], originOnly: false, relaxed, errors);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            errors.Add("Cors:AllowedOrigins is empty (set the public origin of the web app, e.g. https://tms.example.be).");
        }

        for (var index = 0; index < origins.Length; index++)
        {
            CheckUrl($"Cors:AllowedOrigins:{index}", origins[index], originOnly: true, relaxed, errors);
        }

        var proxies = configuration.GetSection("Network:KnownProxies").Get<string[]>() ?? [];
        for (var index = 0; index < proxies.Length; index++)
        {
            if (!IPAddress.TryParse(proxies[index]?.Trim(), out _))
            {
                errors.Add($"Network:KnownProxies:{index} ('{proxies[index]}') is not a valid IP address.");
            }
        }

        var networks = configuration.GetSection("Network:KnownNetworks").Get<string[]>() ?? [];
        for (var index = 0; index < networks.Length; index++)
        {
            if (!IsValidNetwork(networks[index]))
            {
                errors.Add($"Network:KnownNetworks:{index} ('{networks[index]}') is not a valid CIDR network (address/prefix).");
            }
        }

        return errors;
    }

    private static void CheckUrl(string key, string? value, bool originOnly, bool relaxed, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is missing.");
            return;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add($"{key} ('{value}') is not an absolute http(s) URL.");
            return;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add($"{key} ('{value}') must not contain a query string or fragment.");
        }

        if (originOnly && uri.AbsolutePath != "/")
        {
            errors.Add($"{key} ('{value}') must be an origin (scheme://host[:port]) without a path.");
        }

        if (relaxed)
        {
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{key} ('{value}') must use https.");
        }

        if (IsLoopback(uri.Host))
        {
            errors.Add($"{key} ('{value}') points at localhost; use the real public URL.");
        }

        if (DemoHosts.Any(host => string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{key} ('{value}') is still the demo domain; use this installation's own URL.");
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address)
            && (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)));

    /// <summary>Exactly what the forwarded-headers setup in Program.cs does with the entry
    /// (address/prefix split + <see cref="System.Net.IPNetwork"/>), so "valid here" means
    /// "actually trusted there".</summary>
    private static bool IsValidNetwork(string? network)
    {
        var parts = (network ?? string.Empty).Split('/', 2);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
        {
            return false;
        }

        try
        {
            _ = new System.Net.IPNetwork(prefix, length);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
