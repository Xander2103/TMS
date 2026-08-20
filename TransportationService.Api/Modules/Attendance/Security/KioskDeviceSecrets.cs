using System.Security.Cryptography;
using System.Text;

namespace TransportationService.Api.Modules.Attendance.Security;

/// <summary>
/// Device-secret voor prikklokken: 256-bit random token, éénmalig getoond bij
/// provisioning als "deviceKey" ({deviceId:N}.{secret}) en opgeslagen als SHA-256-hash.
/// SHA-256 is hier correct (hoge-entropie token — bruteforce is zinloos, dus geen
/// password-grade werkfactor nodig; zelfde afweging als API-/refreshtokens).
/// Vergelijking altijd constant-time. Roteren = nieuw secret genereren; het oude is
/// onmiddellijk ongeldig.
/// </summary>
public static class KioskDeviceSecrets
{
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Verify(string? storedHash, string providedSecret)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(providedSecret))
        {
            return false;
        }

        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(providedSecret));
        byte[] stored;
        try
        {
            stored = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return stored.Length == provided.Length && CryptographicOperations.FixedTimeEquals(stored, provided);
    }

    public static string BuildDeviceKey(Guid deviceId, string secret) => $"{deviceId:N}.{secret}";

    public static bool TryParseDeviceKey(string? deviceKey, out Guid deviceId, out string secret)
    {
        deviceId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            return false;
        }

        var separator = deviceKey.IndexOf('.');
        if (separator <= 0 || separator == deviceKey.Length - 1)
        {
            return false;
        }

        if (!Guid.TryParseExact(deviceKey[..separator], "N", out deviceId))
        {
            return false;
        }

        secret = deviceKey[(separator + 1)..];
        return true;
    }
}
