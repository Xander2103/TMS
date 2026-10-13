using System.Security.Cryptography;
using System.Text;

namespace TransportationService.Api.Modules.Security;

/// <summary>
/// Application-level column encryption (Fase 9) for special-category identifiers (national
/// register number, identity-card number). AES-256-GCM with a random nonce per value and a
/// version/prefix marker, so:
///
///  - values are unreadable in the database, in backups and in ad-hoc SQL exports;
///  - tampering is detected (GCM tag);
///  - legacy plaintext rows keep reading (no marker → returned as-is) and become encrypted on
///    their next write — no big-bang data migration;
///  - key rotation is a config change: new writes use the active key, reads fall back to the
///    previous keys until the data has churned.
///
/// Keys live OUTSIDE repo and database (env/vault): <c>ColumnEncryption:Key</c> (base64, 32
/// bytes) and optional <c>ColumnEncryption:PreviousKeys</c>. Without a configured key the layer
/// is pass-through — acceptable in Development only; the startup validator refuses a
/// non-Development host without a key.
/// </summary>
public static class ColumnEncryption
{
    /// <summary>Marker + scheme version. A stored value carrying this prefix is ciphertext.</summary>
    public const string Prefix = "enc:v1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static byte[]? _activeKey;
    private static byte[][] _decryptKeys = [];

    /// <summary>True when an active key is configured (writes will encrypt).</summary>
    public static bool IsConfigured => _activeKey is not null;

    /// <summary>
    /// (Re)initialises the key ring. Base64 keys must decode to exactly 32 bytes. Null/empty
    /// active key = pass-through mode (Development).
    /// </summary>
    public static void Initialize(string? activeKeyBase64, IReadOnlyList<string>? previousKeysBase64 = null)
    {
        _activeKey = string.IsNullOrWhiteSpace(activeKeyBase64) ? null : DecodeKey(activeKeyBase64);
        var decryptKeys = new List<byte[]>();
        if (_activeKey is not null)
        {
            decryptKeys.Add(_activeKey);
        }

        foreach (var previous in previousKeysBase64 ?? [])
        {
            if (!string.IsNullOrWhiteSpace(previous))
            {
                decryptKeys.Add(DecodeKey(previous));
            }
        }

        _decryptKeys = [.. decryptKeys];
    }

    private static byte[] DecodeKey(string base64)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("ColumnEncryption key is not valid base64.");
        }

        return key.Length == 32
            ? key
            : throw new InvalidOperationException("ColumnEncryption keys must be exactly 32 bytes (AES-256).");
    }

    public static string Protect(string plaintext)
    {
        if (_activeKey is null)
        {
            return plaintext;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_activeKey, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(payload);
    }

    public static string Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Legacy plaintext row: readable until its next write encrypts it.
            return stored;
        }

        var payload = Convert.FromBase64String(stored[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Encrypted column value is malformed.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        foreach (var key in _decryptKeys)
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (AuthenticationTagMismatchException)
            {
                // Next key in the ring (rotation).
            }
        }

        throw new InvalidOperationException(
            "Encrypted column value could not be decrypted with any configured key. "
            + "Check ColumnEncryption:Key / ColumnEncryption:PreviousKeys.");
    }
}
