using Microsoft.AspNetCore.Http;

namespace TransportationService.Api.Modules.Security;

/// <summary>
/// Shared upload gate (H6/L1/L3): the extension decides the server-side content type, and the
/// file's leading bytes must actually match that extension — a renamed executable or an HTML
/// polyglot is refused at the door instead of being stored and served back. SVG is deliberately
/// absent from every signature: scriptable image formats are not accepted as user uploads (L1).
/// </summary>
public static class UploadValidation
{
    private static readonly Dictionary<string, byte[][]> SignaturesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]],                                  // %PDF
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06]],       // zip (also empty zip)
    };

    /// <summary>Longest signature we ever need to peek (WebP: RIFF????WEBP → 12 bytes).</summary>
    private const int HeaderLength = 12;

    /// <summary>
    /// True when the stream's leading bytes match the extension's known signature. Extensions
    /// without a registered signature return true — the caller's extension whitelist remains the
    /// gate there. The stream is rewound afterwards; a non-seekable stream fails closed.
    /// </summary>
    public static bool MatchesSignature(string fileName, Stream content)
    {
        var extension = Path.GetExtension(fileName);
        var isWebp = string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(extension)
            || (!isWebp && !SignaturesByExtension.TryGetValue(extension, out _)))
        {
            return true;
        }

        if (!content.CanSeek)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        var position = content.Position;
        var read = content.Read(header);
        content.Position = position;

        if (isWebp)
        {
            // RIFF????WEBP — the 4 length bytes between the markers vary per file.
            return read >= 12
                && header[..4].SequenceEqual("RIFF"u8)
                && header[8..12].SequenceEqual("WEBP"u8);
        }

        foreach (var signature in SignaturesByExtension[extension])
        {
            if (read >= signature.Length && header[..signature.Length].SequenceEqual(signature))
            {
                return true;
            }
        }

        return false;
    }

    public const string SignatureMismatchMessage = "De inhoud van het bestand komt niet overeen met het bestandstype.";

    /// <summary>Drop-in addition for controllers with existing size/extension checks: returns the
    /// 400-message when the file's bytes don't match its extension, null when they do.</summary>
    public static string? SignatureError(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        return MatchesSignature(file.FileName, stream) ? null : SignatureMismatchMessage;
    }

    /// <summary>
    /// One-call controller gate: size window, extension whitelist and signature check. Returns a
    /// Dutch error message for a 400, or null when the file is acceptable.
    /// </summary>
    public static string? Validate(IFormFile file, long maxBytes, IReadOnlyCollection<string> allowedExtensions, string? sizeError = null, string? extensionError = null)
    {
        if (file.Length == 0 || file.Length > maxBytes)
        {
            return sizeError ?? $"Het bestand moet tussen 1 byte en {maxBytes / (1024 * 1024)} MB groot zijn.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            return extensionError ?? "Dit bestandstype is niet toegestaan.";
        }

        using var stream = file.OpenReadStream();
        return MatchesSignature(file.FileName, stream)
            ? null
            : "De inhoud van het bestand komt niet overeen met het bestandstype.";
    }
}
