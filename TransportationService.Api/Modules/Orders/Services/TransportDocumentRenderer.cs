using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// A party block on a document. <c>Detail</c> is an optional second name line (the site/location
/// name when the party is a company that owns that site); <c>Source</c> says how the party was
/// derived (see <see cref="TransportDocumentPartySource"/>) so tests and support can see the rule.
/// </summary>
public sealed record TransportDocumentParty(
    string Name, string? AddressLine, string? VatNumber,
    string? Detail = null, string? Source = null);

/// <summary>How a CMR sender/consignee was derived — see <c>TransportDocumentService.ResolveStopParty</c>.</summary>
public static class TransportDocumentPartySource
{
    /// <summary>The stop's master location belongs to a customer: that customer is the party.</summary>
    public const string LocationOwner = "LocationOwner";

    /// <summary>A free-address / unowned location: the stop's own name and address.</summary>
    public const string StopLocation = "StopLocation";

    /// <summary>The stop is the ordering customer's own address, or the stop carries no name.</summary>
    public const string OrderingCustomer = "OrderingCustomer";

    /// <summary>The tenant's issuing legal entity (the carrier).</summary>
    public const string LegalEntity = "LegalEntity";
}

/// <summary>CMR box 3 (place of delivery) / box 4 (place and date of taking over).</summary>
public sealed record TransportDocumentPlace(string? Label, string? AddressLine, string? CountryCode, DateOnly? Date);

/// <summary>
/// The CMR consignment parties, derived from the order (never typed by hand):
/// sender = loading party, consignee = unloading party, carrier = the tenant's legal entity, and the
/// registration of the trip's vehicle/trailer when the order is planned (null = blank line to fill in).
/// </summary>
public sealed record TransportDocumentConsignment(
    TransportDocumentParty Sender,
    TransportDocumentParty Consignee,
    TransportDocumentParty Carrier,
    TransportDocumentPlace? TakingOver,
    TransportDocumentPlace? Delivery,
    string? VehicleRegistration,
    string? TrailerRegistration,
    /// <summary>Driver of the trip (work order "operator"); never printed on a CMR.</summary>
    string? DriverName = null);

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
    TransportDocumentReference? Reference = null,
    /// <summary>
    /// Sender / consignee / carrier / places / vehicle. <see cref="Seller"/> stays the issuing
    /// entity and <see cref="Customer"/> the ORDERING party (delivery note "Klant", invoicing) —
    /// on a CMR neither is printed as sender or consignee.
    /// </summary>
    TransportDocumentConsignment? Consignment = null);

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

        if (snapshot.Kind == "Cmr" && snapshot.Consignment is not null)
        {
            AppendCmrPage(document, snapshot, snapshot.Consignment);
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

        // (A CMR without consignment data — only a hand-built snapshot — keeps the legacy layout.)
        Party(snapshot.Kind == "Cmr" ? "1. Afzender / Expéditeur" : "Vervoerder", snapshot.Seller);
        Party(snapshot.Kind == "Cmr" ? "2. Geadresseerde / Destinataire" : "Klant", snapshot.Customer);
        if (snapshot.Kind != "Cmr" && snapshot.Consignment is { } deliveryConsignment)
        {
            // Leveringsbon: who receives the goods = the unloading party (not the ordering customer).
            Party("Leveren aan", deliveryConsignment.Consignee);
        }

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

    private static readonly XFont BoxLabel = new("Arial", 7, XFontStyleEx.Bold);

    /// <summary>
    /// CMR in the boxes of the convention: 1 sender, 2 consignee, 3 place of delivery, 4 place and
    /// date of taking over (left); reference, 16 carrier, 25 registration and the ordering party
    /// (right); then goods (6-9, 11), remarks (13) and the signature boxes 22-24. The carrier is the
    /// tenant's legal entity — never the sender. An unplanned order prints an empty registration
    /// line to be filled in by hand.
    /// </summary>
    private static void AppendCmrPage(PdfDocument document, TransportDocumentSnapshot snapshot, TransportDocumentConsignment consignment)
    {
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        const double margin = 40.0;
        const double gap = 6.0;
        var width = page.Width.Point - margin * 2;
        var columnWidth = (width - gap) / 2;
        var leftX = margin;
        var rightX = margin + columnWidth + gap;
        var pen = new XPen(XColors.Black, 0.75);
        var y = margin;

        gfx.DrawString("CMR — VRACHTBRIEF / LETTRE DE VOITURE", Title, XBrushes.Black, new XPoint(margin, y + 14));
        y += 22;
        gfx.DrawString($"Opdracht {snapshot.OrderNumber} · {snapshot.OrderDate:dd-MM-yyyy}", Small, XBrushes.Black, new XPoint(margin, y + 8));
        y += 14;

        string Fit(string text, XFont font, double maxWidth)
        {
            if (gfx.MeasureString(text, font).Width <= maxWidth)
            {
                return text;
            }

            var cut = text;
            while (cut.Length > 1 && gfx.MeasureString(cut + "…", font).Width > maxWidth)
            {
                cut = cut[..^1];
            }

            return cut + "…";
        }

        void Box(double x, double top, double boxWidth, double height, string label, IEnumerable<(string Text, XFont Font)> lines)
        {
            gfx.DrawRectangle(pen, x, top, boxWidth, height);
            gfx.DrawString(Fit(label, BoxLabel, boxWidth - 8), BoxLabel, XBrushes.Black, new XPoint(x + 4, top + 9));
            var lineY = top + 21;
            foreach (var (text, font) in lines)
            {
                if (lineY > top + height - 3)
                {
                    break;
                }

                gfx.DrawString(Fit(text, font, boxWidth - 8), font, XBrushes.Black, new XPoint(x + 4, lineY));
                lineY += ReferenceEquals(font, Small) ? 10 : 11.5;
            }
        }

        static IEnumerable<(string, XFont)> PartyLines(TransportDocumentParty party)
        {
            yield return (party.Name, Heading);
            if (party.Detail is { Length: > 0 } detail)
            {
                yield return (detail, Body);
            }

            if (party.AddressLine is { Length: > 0 } address)
            {
                yield return (address, Body);
            }

            if (party.VatNumber is { Length: > 0 } vat)
            {
                yield return ($"BTW / TVA: {vat}", Small);
            }
        }

        static IEnumerable<(string, XFont)> PlaceLines(TransportDocumentPlace? place, bool withDate)
        {
            if (place is null)
            {
                yield return ("—", Body);
                yield break;
            }

            if (place.Label is { Length: > 0 } label)
            {
                yield return (label, Body);
            }

            var address = string.Join(" ", new[] { place.AddressLine, place.CountryCode }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (address.Length > 0)
            {
                yield return (address, Body);
            }

            if (withDate)
            {
                yield return (place.Date is { } date ? $"Datum / Date: {date:dd-MM-yyyy}" : "Datum / Date: ____ - ____ - ________", Body);
            }
        }

        IEnumerable<(string, XFont)> ReferenceLines()
        {
            if (snapshot.Reference is { } reference)
            {
                yield return ($"Documentnr.: {reference.DocumentNumber}", Heading);
                if (reference.DossierNumber is { Length: > 0 } dossier)
                {
                    yield return ($"Dossier: {dossier}", Body);
                }

                yield return ($"Opdracht: {snapshot.OrderNumber}", Body);
                if (reference.ExternalNumber is { Length: > 0 } external)
                {
                    yield return ($"Extern nr.: {external}", Body);
                }
            }
            else
            {
                yield return ($"Opdracht: {snapshot.OrderNumber}", Heading);
                yield return ("Ongenummerd voorbeeld — geen uitgegeven vrachtbrief", Small);
            }
        }

        IEnumerable<(string, XFont)> RegistrationLines()
        {
            yield return ($"Trekker / Tracteur: {consignment.VehicleRegistration ?? "______________________"}", Body);
            yield return ($"Oplegger / Remorque: {consignment.TrailerRegistration ?? (consignment.VehicleRegistration is null ? "______________________" : "—")}", Body);
        }

        IEnumerable<(string, XFont)> OrderingPartyLines()
        {
            yield return (snapshot.Customer.Name, Body);
            if (snapshot.CustomerReference is { Length: > 0 } customerReference)
            {
                yield return ($"Klantreferentie: {customerReference}", Body);
            }
        }

        const double partyHeight = 74;
        const double placeHeight = 52;
        Box(leftX, y, columnWidth, partyHeight, "1. Afzender / Expéditeur", PartyLines(consignment.Sender));
        Box(rightX, y, columnWidth, partyHeight, "Referentie / Référence", ReferenceLines());
        y += partyHeight;
        Box(leftX, y, columnWidth, partyHeight, "2. Geadresseerde / Destinataire", PartyLines(consignment.Consignee));
        Box(rightX, y, columnWidth, partyHeight, "16. Vervoerder / Transporteur", PartyLines(consignment.Carrier));
        y += partyHeight;
        Box(leftX, y, columnWidth, placeHeight, "3. Plaats van aflevering / Lieu de livraison", PlaceLines(consignment.Delivery, withDate: false));
        Box(rightX, y, columnWidth, placeHeight, "25. Kenteken / Immatriculation", RegistrationLines());
        y += placeHeight;
        Box(leftX, y, columnWidth, placeHeight, "4. Plaats en datum inontvangstneming / Prise en charge",
            PlaceLines(consignment.TakingOver, withDate: true));
        Box(rightX, y, columnWidth, placeHeight, "Opdrachtgever / Donneur d'ordre", OrderingPartyLines());
        y += placeHeight;

        // Goods: one line per goods line, then the gross weight (box 11).
        var goodsLines = snapshot.Lines
            .Select(line => ($"{line.Quantity:0.##} {line.Unit ?? "stuks"} — {line.Description}"
                + (line.WeightKg is { } weight ? $" ({weight:0.#} kg)" : ""), Body))
            .ToList();
        if (snapshot.TotalWeightKg is { } total)
        {
            goodsLines.Add(($"11. Brutogewicht / Poids brut: {total:0.#} kg", Heading));
        }

        var goodsHeight = Math.Clamp(26 + goodsLines.Count * 11.5, 60, 200);
        Box(leftX, y, width, goodsHeight, "6-9. Merken, aantal, verpakking, aard der goederen / Marques, nombre, emballage, nature", goodsLines);
        y += goodsHeight;

        var remarks = new List<(string, XFont)>();
        if (snapshot.Notes is { Length: > 0 } notes)
        {
            remarks.AddRange(WrapText(gfx, notes, Body, width - 8).Take(4).Select(line => (line, Body)));
        }

        Box(leftX, y, width, 62, "13. Instructies afzender, opmerkingen / Instructions de l'expéditeur", remarks);

        var signatureTop = page.Height.Point - margin - 90;
        var boxWidth = (width - 20) / 3;
        var labels = new[] { "22. Handtekening afzender", "23. Handtekening vervoerder", "24. Ontvangst geadresseerde" };
        for (var i = 0; i < labels.Length; i += 1)
        {
            var x = margin + i * (boxWidth + 10);
            gfx.DrawRectangle(pen, x, signatureTop, boxWidth, 70);
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

        // Who/what executes the work: the trip's vehicle (crane) and driver (operator) when planned.
        if (snapshot.Consignment is { } execution
            && (execution.VehicleRegistration is not null || execution.TrailerRegistration is not null || execution.DriverName is not null))
        {
            Section("Uitvoering");
            if (execution.VehicleRegistration is { } vehicle)
            {
                Line($"Voertuig / kraan: {vehicle}");
            }

            if (execution.TrailerRegistration is { } trailer)
            {
                Line($"Aanhangwagen: {trailer}");
            }

            if (execution.DriverName is { } driver)
            {
                Line($"Operator: {driver}");
            }
        }

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
