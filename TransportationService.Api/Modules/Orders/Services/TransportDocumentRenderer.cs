using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Orders.Services;

public sealed record TransportDocumentParty(string Name, string? AddressLine, string? VatNumber);

public sealed record TransportDocumentStop(string Kind, string? Label, string? AddressLine, string? Reference);

public sealed record TransportDocumentLine(string Description, decimal Quantity, string? Unit, decimal? WeightKg);

/// <summary>
/// D2: the work-order (werkbon) block of on-site work. Times are tenant wall-clock; every field
/// is optional and only printed when filled — a lift load is described here, never as a goods line.
/// </summary>
public sealed record TransportDocumentWorkOrder(
    string? WorkDescription,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    decimal? DurationHours,
    decimal? LiftLoadWeightKg,
    string? LiftLoadDimensions,
    decimal? LiftRadiusMeters,
    decimal? LiftHeightMeters,
    string? LiftConditions,
    string? LiftEquipment);

/// <summary>
/// D6: the reference block of an ISSUED document — its own unique number, the dossier and (from the
/// snapshot) the order number, plus the pre-printed/external number when the user entered one.
/// Absent on the number-less streamed documents, which render exactly as before.
/// </summary>
public sealed record TransportDocumentReference(string DocumentNumber, string? DossierNumber, string? ExternalNumber);

/// <summary>Everything one delivery note / CMR / work order needs — assembled from frozen order data.</summary>
public sealed record TransportDocumentSnapshot(
    /// <summary>"DeliveryNote" | "Cmr" | "WorkOrder".</summary>
    string Kind,
    string OrderNumber,
    DateOnly OrderDate,
    TransportDocumentParty Seller,
    TransportDocumentParty Customer,
    IReadOnlyList<TransportDocumentStop> Stops,
    IReadOnlyList<TransportDocumentLine> Lines,
    decimal? TotalWeightKg,
    string? CustomerReference,
    string? Notes,
    /// <summary>D2: only for Kind == "WorkOrder".</summary>
    TransportDocumentWorkOrder? WorkOrder = null,
    /// <summary>D6: only for an issued document (see <see cref="TransportDocumentReference"/>).</summary>
    TransportDocumentReference? Reference = null);

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
        // Platform-independent font source (Windows Arial when present, else Liberation/DejaVu,
        // else the embedded DejaVu Sans). Without it Linux hosting throws "No appropriate font".
        TransportationService.Api.Modules.Pdf.PdfFontResolver.Register();
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
        // D2: the work order has its own layout; delivery note and CMR below are untouched.
        if (snapshot.Kind == DocumentStrategyResolver.KindWorkOrder)
        {
            AppendWorkOrderPage(document, snapshot);
            return;
        }

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
        y = DrawReference(gfx, snapshot, margin, y);

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

    /// <summary>
    /// D2: werkbon for on-site work — order number + date, executing company and customer, site
    /// address, work description, planned start/end + duration, lift data (filled fields only),
    /// goods only when real goods lines exist, and the signature boxes. One page, like its siblings.
    /// </summary>
    private static void AppendWorkOrderPage(PdfDocument document, TransportDocumentSnapshot snapshot)
    {
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        const double margin = 40.0;
        var width = page.Width.Point - margin * 2;
        var y = margin;
        // Everything above the fixed signature block; text that would run into it is cut off.
        var bottom = page.Height.Point - margin - 130;

        gfx.DrawString("WERKBON", Title, XBrushes.Black, new XPoint(margin, y + 14));
        gfx.DrawString($"{snapshot.OrderNumber} · {snapshot.OrderDate:dd-MM-yyyy}", Body, XBrushes.Black,
            new XPoint(margin + width - 160, y + 14));
        y += 30;
        gfx.DrawLine(new XPen(XColors.Black, 1.0), margin, y, margin + width, y);
        y += 12;
        y = DrawReference(gfx, snapshot, margin, y);

        void Section(string label)
        {
            y += 6;
            gfx.DrawString(label, Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
        }

        void Line(string text, XFont? font = null)
        {
            if (y > bottom)
            {
                return;
            }

            gfx.DrawString(text, font ?? Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 12;
        }

        void Wrapped(string text)
        {
            foreach (var line in WrapText(gfx, text, Body, width))
            {
                Line(line);
            }
        }

        void Party(string label, TransportDocumentParty party)
        {
            Section(label);
            Line(party.Name);
            if (party.AddressLine is { Length: > 0 })
            {
                Line(party.AddressLine);
            }

            if (party.VatNumber is { Length: > 0 })
            {
                Line($"BTW: {party.VatNumber}", Small);
            }
        }

        Party("Uitvoerder", snapshot.Seller);
        Party("Klant", snapshot.Customer);

        Section("Werfadres");
        foreach (var stop in snapshot.Stops)
        {
            var line = $"{stop.Label ?? "?"}{(stop.AddressLine is { Length: > 0 } ? $" — {stop.AddressLine}" : "")}"
                + (stop.Reference is { Length: > 0 } ? $" (ref. {stop.Reference})" : "");
            Line(line.Length > 110 ? line[..109] + "…" : line);
        }

        var work = snapshot.WorkOrder;
        Section("Werkomschrijving");
        if (work?.WorkDescription is { Length: > 0 } description)
        {
            Wrapped(description);
        }

        if (work is not null && (work.PlannedStart is not null || work.PlannedEnd is not null || work.DurationHours is not null))
        {
            Section("Planning");
            if (work.PlannedStart is { } start)
            {
                Line($"Geplande start: {start:dd-MM-yyyy HH:mm}");
            }

            if (work.PlannedEnd is { } end)
            {
                Line($"Gepland einde: {end:dd-MM-yyyy HH:mm}");
            }

            if (work.DurationHours is { } hours)
            {
                Line($"Geplande duur: {FormatDuration(hours)}");
            }
        }

        var liftLines = new List<string>();
        if (work?.LiftLoadWeightKg is { } weight)
        {
            liftLines.Add($"Gewicht last: {weight:0.##} kg");
        }

        if (work?.LiftLoadDimensions is { Length: > 0 } dimensions)
        {
            liftLines.Add($"Afmetingen last: {dimensions}");
        }

        if (work?.LiftRadiusMeters is { } radius)
        {
            liftLines.Add($"Vlucht (radius): {radius:0.##} m");
        }

        if (work?.LiftHeightMeters is { } height)
        {
            liftLines.Add($"Hijshoogte: {height:0.##} m");
        }

        if (work?.LiftEquipment is { Length: > 0 } equipment)
        {
            liftLines.Add($"Hijsmateriaal: {equipment}");
        }

        if (work?.LiftConditions is { Length: > 0 } conditions)
        {
            liftLines.Add($"Omstandigheden: {conditions}");
        }

        if (liftLines.Count > 0)
        {
            Section("Lastgegevens");
            foreach (var liftLine in liftLines)
            {
                Wrapped(liftLine);
            }
        }

        if (snapshot.Lines.Count > 0)
        {
            Section("Goederen");
            foreach (var line in snapshot.Lines)
            {
                var text = $"{line.Quantity:0.##} {line.Unit ?? "stuks"} — {line.Description}"
                    + (line.WeightKg is { } lineWeight ? $" ({lineWeight:0.#} kg)" : "");
                Line(text.Length > 110 ? text[..109] + "…" : text);
            }
        }

        if (snapshot.CustomerReference is { Length: > 0 })
        {
            y += 6;
            Line($"Klantreferentie: {snapshot.CustomerReference}");
        }

        if (snapshot.Notes is { Length: > 0 })
        {
            var notes = snapshot.Notes.Length > 200 ? snapshot.Notes[..199] + "…" : snapshot.Notes;
            Line($"Opmerkingen: {notes}", Small);
        }

        // Filled in by hand on site: the actual times, then the two signatures.
        var signatureTop = page.Height.Point - margin - 90;
        gfx.DrawString("Werkelijke start: ____ : ____      Werkelijk einde: ____ : ____      Datum: ____ - ____ - ________",
            Body, XBrushes.Black, new XPoint(margin, signatureTop - 14));
        var boxWidth = (width - 10) / 2;
        var labels = new[] { "Handtekening uitvoerder", "Handtekening klant / werfverantwoordelijke" };
        for (var i = 0; i < labels.Length; i += 1)
        {
            var x = margin + i * (boxWidth + 10);
            gfx.DrawRectangle(new XPen(XColors.Black, 0.75), x, signatureTop, boxWidth, 70);
            gfx.DrawString(labels[i], Small, XBrushes.Black, new XPoint(x + 4, signatureTop + 12));
        }
    }

    /// <summary>
    /// D6: the reference area of an ISSUED document, directly under the header rule — own document
    /// number (bold), dossier number, order number and, when entered, the external number. Returns
    /// the new y; a number-less (streamed) document draws nothing and keeps its layout.
    /// </summary>
    private static double DrawReference(XGraphics gfx, TransportDocumentSnapshot snapshot, double margin, double y)
    {
        if (snapshot.Reference is not { } reference)
        {
            return y;
        }

        gfx.DrawString($"Documentnr.: {reference.DocumentNumber}", Heading, XBrushes.Black, new XPoint(margin, y + 9));
        y += 13;
        var parts = new List<string>();
        if (reference.DossierNumber is { Length: > 0 } dossierNumber)
        {
            parts.Add($"Dossier: {dossierNumber}");
        }

        parts.Add($"Opdracht: {snapshot.OrderNumber}");
        if (reference.ExternalNumber is { Length: > 0 } externalNumber)
        {
            parts.Add($"Extern nr.: {externalNumber}");
        }

        gfx.DrawString(string.Join("   ", parts), Body, XBrushes.Black, new XPoint(margin, y + 9));
        return y + 18;
    }

    /// <summary>1.5 → "1 u 30 min", 4 → "4 u", 0.25 → "15 min".</summary>
    private static string FormatDuration(decimal hours)
    {
        var totalMinutes = (int)decimal.Round(hours * 60m, 0, MidpointRounding.AwayFromZero);
        var wholeHours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return (wholeHours, minutes) switch
        {
            (0, _) => $"{minutes} min",
            (_, 0) => $"{wholeHours} u",
            _ => $"{wholeHours} u {minutes} min",
        };
    }

    /// <summary>Greedy word wrap on the measured width; explicit line breaks are kept.</summary>
    private static IEnumerable<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth)
                {
                    yield return current;
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            yield return current;
        }
    }
}
