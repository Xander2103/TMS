using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Authenticates provider webhook calls (M2). Configuration is resolved per provider key with a
/// global fallback: a <c>Peppol:Webhook:Providers:{providerKey}</c> section that defines any
/// secret material fully replaces the global <c>Peppol:Webhook</c> material for that provider —
/// a secret issued to provider A can never authenticate a call on provider B's route.
///
/// Two modes, strongest wins:
///  - HMAC mode (active as soon as an <c>Hmac:Secret</c> is configured): the request must carry
///    a unix-seconds timestamp header inside the replay window (<c>Hmac:ToleranceSeconds</c>,
///    default 300) and a hex HMAC-SHA256 signature over <c>"{timestamp}.{rawBody}"</c>.
///    Replays outside the window are refused outright; replays inside the window are neutralised
///    by the idempotent processing (provider message ids are deduplicated).
///  - Shared-secret mode (baseline): constant-time comparison of the secret header.
///
/// Both modes support zero-downtime rotation: <c>PreviousSecrets</c> stay valid alongside the
/// current secret until the rotation window closes and they are removed from configuration.
/// No configured secret material at all = every call refused (secure by default).
/// </summary>
public static class PeppolWebhookAuthenticator
{
    public const string SecretHeaderName = "X-Peppol-Webhook-Secret";
    public const string TimestampHeaderName = "X-Peppol-Timestamp";
    public const string SignatureHeaderName = "X-Peppol-Signature";
    public const int DefaultToleranceSeconds = 300;

    public static bool Authenticate(
        IConfiguration configuration,
        string providerKey,
        IHeaderDictionary headers,
        string rawBody,
        DateTimeOffset now)
    {
        var scope = SelectScope(configuration, providerKey);
        if (scope is null)
        {
            return false;
        }

        var hmacRing = ReadRing(scope, "Hmac:Secret", "Hmac:PreviousSecrets");
        if (hmacRing.Count > 0)
        {
            return AuthenticateHmac(scope, hmacRing, headers, rawBody, now);
        }

        var secretRing = ReadRing(scope, "Secret", "PreviousSecrets");
        if (secretRing.Count == 0)
        {
            return false;
        }

        var provided = headers[SecretHeaderName].ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return secretRing.Any(secret => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(secret), providedBytes));
    }

    /// <summary>
    /// The provider-scoped section when it defines any secret material; otherwise the global
    /// webhook section. Null when neither holds anything — the caller refuses everything.
    /// </summary>
    private static IConfiguration? SelectScope(IConfiguration configuration, string providerKey)
    {
        var provider = configuration.GetSection($"Peppol:Webhook:Providers:{providerKey}");
        if (HasSecretMaterial(provider))
        {
            return provider;
        }

        var global = configuration.GetSection("Peppol:Webhook");
        return HasSecretMaterial(global) ? global : null;
    }

    private static bool HasSecretMaterial(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section["Secret"])
        || !string.IsNullOrWhiteSpace(section["Hmac:Secret"])
        || section.GetSection("PreviousSecrets").GetChildren().Any()
        || section.GetSection("Hmac:PreviousSecrets").GetChildren().Any();

    /// <summary>Current secret first, then rotation-window previous secrets; blanks dropped.</summary>
    private static IReadOnlyList<string> ReadRing(IConfiguration scope, string currentKey, string previousKey)
    {
        var ring = new List<string>();
        var current = scope[currentKey];
        if (!string.IsNullOrWhiteSpace(current))
        {
            ring.Add(current);
        }

        ring.AddRange(scope.GetSection(previousKey).GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))!
            .Cast<string>());
        return ring;
    }

    private static bool AuthenticateHmac(
        IConfiguration scope,
        IReadOnlyList<string> ring,
        IHeaderDictionary headers,
        string rawBody,
        DateTimeOffset now)
    {
        var timestampRaw = headers[TimestampHeaderName].ToString();
        var signatureRaw = headers[SignatureHeaderName].ToString();
        if (string.IsNullOrEmpty(timestampRaw) || string.IsNullOrEmpty(signatureRaw))
        {
            return false;
        }

        if (!long.TryParse(timestampRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        var tolerance = scope.GetValue<int?>("Hmac:ToleranceSeconds") ?? DefaultToleranceSeconds;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((now - timestamp).TotalSeconds) > tolerance)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes(timestampRaw + "." + rawBody);
        var providedBytes = Encoding.UTF8.GetBytes(signatureRaw.ToUpperInvariant());
        foreach (var key in ring)
        {
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payload));
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), providedBytes))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Helper for tests and provider integrations: the signature the endpoint expects.</summary>
    public static string ComputeSignature(string secret, long unixTimestampSeconds, string rawBody) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(unixTimestampSeconds.ToString(CultureInfo.InvariantCulture) + "." + rawBody)));
}
