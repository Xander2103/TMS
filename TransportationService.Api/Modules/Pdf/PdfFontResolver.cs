using System.Collections.Concurrent;
using System.Reflection;
using PdfSharp.Fonts;

namespace TransportationService.Api.Modules.Pdf;

/// <summary>
/// Cross-platform font source for every PDFsharp renderer in the API (receipts, invoices,
/// transport documents, labels).
///
/// PDFsharp Core only knows Windows platform fonts; on Linux hosting an <see cref="XFont"/>
/// for "Arial" throws <c>No appropriate font found … Implement IFontResolver</c>. This resolver
/// maps every requested family onto one regular and one bold sans-serif face and finds the
/// bytes in this order: Arial (Windows font folder, keeps existing PDFs identical), Liberation
/// Sans / DejaVu Sans from the OS font folders, and finally the DejaVu Sans files embedded in
/// this assembly (Bitstream Vera licence, redistributable). The embedded fallback guarantees
/// document generation works on any host without extra packages.
/// </summary>
public sealed class PdfFontResolver : IFontResolver
{
    public const string RegularFace = "tms-sans";
    public const string BoldFace = "tms-sans-bold";

    private static readonly object RegisterLock = new();

    private static readonly string[] RegularCandidates = ["arial.ttf", "LiberationSans-Regular.ttf", "DejaVuSans.ttf"];
    private static readonly string[] BoldCandidates = ["arialbd.ttf", "LiberationSans-Bold.ttf", "DejaVuSans-Bold.ttf"];

    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Installs the resolver once; safe to call from every renderer's static initialiser.</summary>
    public static void Register()
    {
        lock (RegisterLock)
        {
            if (GlobalFontSettings.FontResolver is PdfFontResolver)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new PdfFontResolver();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // One sans-serif family serves every request; italic is simulated so a single bold/regular
        // pair covers all renderers (none of them draw italics today).
        return new FontResolverInfo(bold ? BoldFace : RegularFace, false, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        return _cache.GetOrAdd(faceName, LoadFace);
    }

    private static byte[] LoadFace(string faceName)
    {
        var bold = string.Equals(faceName, BoldFace, StringComparison.OrdinalIgnoreCase);
        var candidates = bold ? BoldCandidates : RegularCandidates;

        foreach (var directory in FontDirectories())
        {
            foreach (var candidate in candidates)
            {
                var bytes = TryReadFromDirectory(directory, candidate);
                if (bytes is not null)
                {
                    return bytes;
                }
            }
        }

        return ReadEmbedded(bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf");
    }

    private static IEnumerable<string> FontDirectories()
    {
        var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(windowsFonts))
        {
            yield return windowsFonts;
        }

        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".fonts");
            yield return Path.Combine(home, ".local", "share", "fonts");
        }
    }

    private static byte[]? TryReadFromDirectory(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var direct = Path.Combine(directory, fileName);
            if (File.Exists(direct))
            {
                return File.ReadAllBytes(direct);
            }

            var match = Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories).FirstOrDefault();
            return match is null ? null : File.ReadAllBytes(match);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] ReadEmbedded(string fileName)
    {
        var assembly = typeof(PdfFontResolver).GetTypeInfo().Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded font '{fileName}' is missing from the API assembly.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font '{resourceName}' could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
