using System.Text.RegularExpressions;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Pdf;

/// <summary>
/// MD-B1 / MD-H3: an ISSUED legal document (CMR, delivery note, work order, invoice, credit note)
/// renders byte-identically on every download. PDFsharp writes four volatile values: creation /
/// modification date, the trailer document id, random font-subset tags and the XMP
/// DocumentID/InstanceID uuids. All are pinned to the issued document — the dates to its issue,
/// the ids to its own id — with same-length replacements, so every byte offset in the
/// cross-reference table stays valid (Latin-1 maps bytes 1:1).
/// </summary>
public static partial class DeterministicPdf
{
    public static byte[] Save(PdfDocument document, Guid documentId, DateTime issuedAtUtc)
    {
        var stamp = DateTime.SpecifyKind(issuedAtUtc, DateTimeKind.Utc);
        document.Info.CreationDate = stamp;
        document.Info.ModificationDate = stamp;
        var id = documentId.ToString("N").ToUpperInvariant();
        document.Internals.FirstDocumentID = id;
        document.Internals.SecondDocumentID = id;
        using var stream = new MemoryStream();
        document.Save(stream);
        return Pin(stream.ToArray(), documentId);
    }

    // Only in /BaseFont and /FontName entries — never inside binary (compressed) stream data.
    [GeneratedRegex("(?<=/(?:BaseFont|FontName)\\s*/)[A-Z]{6}(?=\\+)", RegexOptions.CultureInvariant)]
    private static partial Regex SubsetTag();

    [GeneratedRegex("(?<=uuid:)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex XmpUuid();

    private static byte[] Pin(byte[] pdf, Guid documentId)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        text = SubsetTag().Replace(text, match =>
        {
            if (!tags.TryGetValue(match.Value, out var tag))
            {
                var n = tags.Count;
                var chars = new char[6];
                for (var i = 5; i >= 0; i--)
                {
                    chars[i] = (char)('A' + n % 26);
                    n /= 26;
                }

                tag = new string(chars);
                tags[match.Value] = tag;
            }

            return tag;
        });

        var bytes = documentId.ToByteArray();
        var index = 0;
        text = XmpUuid().Replace(text, _ =>
        {
            var derived = (byte[])bytes.Clone();
            derived[15] ^= (byte)index++;
            return new Guid(derived).ToString("D");
        });
        return System.Text.Encoding.Latin1.GetBytes(text);
    }
}
