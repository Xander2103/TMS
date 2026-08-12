using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Orders.Services;

public sealed record TransportDocumentParty(string Name, string? AddressLine, string? VatNumber);

public sealed record TransportDocumentStop(string Kind, string? Label, string? AddressLine, string? Reference);

public sealed record TransportDocumentLine(string Description, decimal Quantity, string? Unit, decimal? WeightKg);

/// <summary>Everything one delivery note / CMR needs — assembled from frozen order data.</summary>
public sealed record TransportDocumentSnapshot(
    /// <summary>"DeliveryNote" | "Cmr".</summary>
    string Kind,
    string OrderNumber,
    DateOnly OrderDate,
    TransportDocumentParty Seller,
    TransportDocumentParty Customer,
    IReadOnlyList<TransportDocumentStop> Stops,
    IReadOnlyList<TransportDocumentLine> Lines,
    decimal? TotalWeightKg,
    string? CustomerReference,
    string? Notes);

/// <summary>
/// Wave 9: delivery-note (leveringsbon) and CMR rendering — the same PDFsharp house pattern
/// as the invoice/label renderers (QuestPDF stays a non-dependency). One page per document;
/// a batch is these pages appended into one PdfDocument (see RenderBatch).
/// </summary>
public static class TransportDocumentRenderer
{
    // MUST be the first static field: initializers run in textual order, and the XFont fields
    // below need the font source configured first (same pattern as InvoicePdfRenderer).
    private static readonly bool FontsConfigured = ConfigureFonts();

    private static bool ConfigureFonts()
    {
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    }

    private static readonly XFont Title = new("Arial", 16, XFontStyleEx.Bold);
    private static readonly XFont Heading = new("Arial", 10, XFontStyleEx.Bold);
    private static readonly XFont Body = new("Arial", 9, XFontStyleEx.Regular);
    private static readonly XFont Small = new("Arial", 7.5, XFontStyleEx.Regular);

    public static byte[] Render(TransportDocumentSnapshot snapshot)
    {
        using var document = new PdfDocument();
        AppendPage(document, snapshot);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>One merged PDF, one page per document — the batch print run.</summary>
    public static byte[] RenderBatch(IReadOnlyList<TransportDocumentSnapshot> snapshots)
    {
        using var document = new PdfDocument();
        foreach (var snapshot in snapshots)
        {
            AppendPage(document, snapshot);
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void AppendPage(PdfDocument document, TransportDocumentSnapshot snapshot)
    {
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        const double margin = 40.0;
        var width = page.Width.Point - margin * 2;
        var y = margin;

        var title = snapshot.Kind == "Cmr" ? "CMR — VRACHTBRIEF" : "LEVERINGSBON";
        gfx.DrawString(title, Title, XBrushes.Black, new XPoint(margin, y + 14));
        gfx.DrawString($"{snapshot.OrderNumber} · {snapshot.OrderDate:dd-MM-yyyy}", Body, XBrushes.Black,
            new XPoint(margin + width - 160, y + 14));
        y += 30;
        gfx.DrawLine(new XPen(XColors.Black, 1.0), margin, y, margin + width, y);
        y += 12;

        // Parties: sender (1) and consignee (2) — CMR box numbering in the labels.
        void Party(string label, TransportDocumentParty party)
        {
            gfx.DrawString(label, Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 13;
            gfx.DrawString(party.Name, Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
            if (party.AddressLine is { Length: > 0 })
            {
                gfx.DrawString(party.AddressLine, Body, XBrushes.Black, new XPoint(margin, y + 9));
                y += 12;
            }

            if (party.VatNumber is { Length: > 0 })
            {
                gfx.DrawString($"BTW: {party.VatNumber}", Small, XBrushes.Black, new XPoint(margin, y + 8));
                y += 11;
            }

            y += 6;
        }

        Party(snapshot.Kind == "Cmr" ? "1. Afzender / Expéditeur" : "Vervoerder", snapshot.Seller);
        Party(snapshot.Kind == "Cmr" ? "2. Geadresseerde / Destinataire" : "Klant", snapshot.Customer);

        gfx.DrawString(snapshot.Kind == "Cmr" ? "3-4. Laad- en losplaats" : "Route", Heading, XBrushes.Black, new XPoint(margin, y + 9));
        y += 14;
        foreach (var stop in snapshot.Stops)
        {
            var line = $"{stop.Kind}: {stop.Label ?? "?"}{(stop.AddressLine is { Length: > 0 } ? $" — {stop.AddressLine}" : "")}"
                + (stop.Reference is { Length: > 0 } ? $" (ref. {stop.Reference})" : "");
            gfx.DrawString(line.Length > 110 ? line[..109] + "…" : line, Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
        }

        y += 8;
        gfx.DrawString(snapshot.Kind == "Cmr" ? "6-9. Goederen" : "Goederen", Heading, XBrushes.Black, new XPoint(margin, y + 9));
        y += 14;
        foreach (var line in snapshot.Lines)
        {
            var text = $"{line.Quantity:0.##} {line.Unit ?? "stuks"} — {line.Description}"
                + (line.WeightKg is { } weight ? $" ({weight:0.#} kg)" : "");
            gfx.DrawString(text.Length > 110 ? text[..109] + "…" : text, Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
        }

        if (snapshot.TotalWeightKg is { } total)
        {
            gfx.DrawString($"Totaal gewicht: {total:0.#} kg", Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
        }

        if (snapshot.CustomerReference is { Length: > 0 })
        {
            gfx.DrawString($"Klantreferentie: {snapshot.CustomerReference}", Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
        }

        if (snapshot.Notes is { Length: > 0 })
        {
            var notes = snapshot.Notes.Length > 200 ? snapshot.Notes[..199] + "…" : snapshot.Notes;
            gfx.DrawString($"Opmerkingen: {notes}", Small, XBrushes.Black, new XPoint(margin, y + 8));
            y += 12;
        }

        // Signature boxes at a fixed position above the footer.
        var signatureTop = page.Height.Point - margin - 90;
        var boxWidth = (width - 20) / (snapshot.Kind == "Cmr" ? 3 : 2);
        var labels = snapshot.Kind == "Cmr"
            ? new[] { "22. Handtekening afzender", "23. Handtekening vervoerder", "24. Ontvangst geadresseerde" }
            : ["Handtekening vervoerder", "Handtekening ontvanger"];
        for (var i = 0; i < labels.Length; i += 1)
        {
            var x = margin + i * (boxWidth + 10);
            gfx.DrawRectangle(new XPen(XColors.Black, 0.75), x, signatureTop, boxWidth, 70);
            gfx.DrawString(labels[i], Small, XBrushes.Black, new XPoint(x + 4, signatureTop + 12));
        }
    }
}
