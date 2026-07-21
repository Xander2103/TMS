using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;

namespace TransportationService.Api.Modules.Packages.Labels;

/// <summary>All content a label ever shows — frozen into PackageLabel.SnapshotJson.</summary>
public sealed record LabelSnapshot(
    string TenantName,
    string PackageNumber,
    string BarcodeValue,
    bool IncludeQr,
    string OrderNumber,
    string CustomerName,
    string? LoadingLocation,
    string? DeliveryLocation,
    int? DeliveryStopSequence,
    string? CustomerReference,
    decimal? WeightKg,
    string UnitTypeLabel,
    string? HandlingInstructions,
    bool IsFragile,
    bool AdrRequired,
    bool RequiresTemperatureControl,
    bool RequiresSignature,
    /// <summary>"Collo n van m" within the cargo line (or order). Optional so historical snapshots keep deserializing.</summary>
    string? SequenceLabel = null,
    // --- Redesigned horizontal layout fields (all optional so old snapshots re-render) ---
    string? SenderName = null,
    string? SenderStreet = null,
    string? SenderPostalCodeCity = null,
    string? SenderCountry = null,
    string? PickupDate = null,
    string? PickupTime = null,
    string? RecipientName = null,
    string? RecipientStreet = null,
    string? RecipientPostalCodeCity = null,
    string? RecipientCountry = null,
    string? DeliveryDate = null,
    string? DeliveryTime = null,
    int? SequenceNumber = null,
    int? SequenceTotal = null,
    decimal? VolumeM3 = null,
    string? PurchaseOrderNumber = null,
    string? CashOnDelivery = null);

public interface ILabelRenderService
{
    /// <summary>Renders one PDF: one page per snapshot (thermal) or an 8-per-page A4 grid.</summary>
    byte[] Render(IReadOnlyList<LabelSnapshot> snapshots, Entities.LabelFormat format);
}

public class LabelRenderService : ILabelRenderService
{
    // MUST be the first static field: initializers run in textual order, and the XFont
    // fields below need the font source configured first. PDFsharp Core uses the Windows
    // platform fonts here; a custom IFontResolver slots in for Linux hosting.
    private static readonly bool FontsConfigured = ConfigureFonts();

    private static bool ConfigureFonts()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    }

    private static readonly XFont Title = new("Arial", 11, XFontStyleEx.Bold);
    private static readonly XFont Big = new("Arial", 16, XFontStyleEx.Bold);
    private static readonly XFont Body = new("Arial", 8.5, XFontStyleEx.Regular);
    private static readonly XFont Small = new("Arial", 7, XFontStyleEx.Regular);
    private static readonly XFont Mono = new("Arial", 8, XFontStyleEx.Regular);

    public byte[] Render(IReadOnlyList<LabelSnapshot> snapshots, Entities.LabelFormat format)
    {
        using var document = new PdfDocument();
        if (format == Entities.LabelFormat.Thermal100x150)
        {
            foreach (var snapshot in snapshots)
            {
                var page = document.AddPage();
                // Redesigned horizontal label: 150 mm wide × 100 mm high (landscape).
                page.Width = XUnit.FromMillimeter(150);
                page.Height = XUnit.FromMillimeter(100);
                using var gfx = XGraphics.FromPdfPage(page);
                DrawHorizontalLabel(gfx, snapshot, new XRect(0, 0, page.Width.Point, page.Height.Point));
            }
        }
        else
        {
            const int columns = 2;
            const int rows = 4;
            for (var index = 0; index < snapshots.Count; index += columns * rows)
            {
                var page = document.AddPage();
                page.Width = XUnit.FromMillimeter(210);
                page.Height = XUnit.FromMillimeter(297);
                using var gfx = XGraphics.FromPdfPage(page);
                var cellWidth = page.Width.Point / columns;
                var cellHeight = page.Height.Point / rows;
                for (var cell = 0; cell < columns * rows && index + cell < snapshots.Count; cell += 1)
                {
                    var rect = new XRect(
                        cell % columns * cellWidth + 8, cell / columns * cellHeight + 8,
                        cellWidth - 16, cellHeight - 16);
                    gfx.DrawRectangle(new XPen(XColors.LightGray, 0.5), rect);
                    DrawLabel(gfx, snapshots[index + cell], rect, compact: true);
                }
            }
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Fixed zones so nothing can ever overlap or clip:
    /// 1. header band — company name (left) + order number (right);
    /// 2. identity block LEFT of a RESERVED QR column — package number, sequence, type;
    /// 3. info lines (customer, addresses, reference, weight, instructions), narrowed while
    ///    beside the QR, full-width below it, truncated with an ellipsis when too long and
    ///    hard-capped above the barcode band;
    /// 4. indicator band (BREEKBAAR/ADR/…);
    /// 5. bottom band — full-width Code 128 with the human-readable value beneath it.
    /// </summary>
    private static void DrawLabel(XGraphics gfx, LabelSnapshot label, XRect area, bool compact)
    {
        var x = area.X + 10;
        var width = area.Width - 20;
        var y = area.Y + 8;
        var lineHeight = compact ? 10.0 : 13.0;

        string Fit(string text, double maxWidth, XFont font)
        {
            if (gfx.MeasureString(text, font).Width <= maxWidth)
            {
                return text;
            }

            while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > maxWidth)
            {
                text = text[..^1];
            }

            return text + "…";
        }

        // 1. Header band.
        gfx.DrawString(Fit(label.TenantName, width * 0.55, Title), Title, XBrushes.Black, new XPoint(x, y + 9));
        gfx.DrawString($"Opdracht {label.OrderNumber}", Body, XBrushes.Black,
            new XRect(x, y, width, 12), XStringFormats.TopRight);
        y += compact ? 15 : 18;
        gfx.DrawLine(new XPen(XColors.Black, 0.8), x, y, x + width, y);
        y += compact ? 4 : 6;

        // 2. Reserved QR column: text in this vertical band NEVER extends into it.
        var qrSize = label.IncludeQr ? (compact ? 46.0 : 66.0) : 0.0;
        var qrTop = y;
        if (label.IncludeQr)
        {
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(label.BarcodeValue, QRCodeGenerator.ECCLevel.M);
            var moduleSize = qrSize / qr.ModuleMatrix.Count;
            var originX = area.Right - 10 - qrSize;
            for (var row = 0; row < qr.ModuleMatrix.Count; row += 1)
            {
                for (var column = 0; column < qr.ModuleMatrix.Count; column += 1)
                {
                    if (qr.ModuleMatrix[row][column])
                    {
                        gfx.DrawRectangle(XBrushes.Black,
                            originX + column * moduleSize, qrTop + row * moduleSize, moduleSize, moduleSize);
                    }
                }
            }
        }

        var textWidthBesideQr = qrSize > 0 ? width - qrSize - 12 : width;

        // Identity block: package number, sequence, packaging type.
        gfx.DrawString(Fit(label.PackageNumber, textWidthBesideQr, Big), Big, XBrushes.Black, new XPoint(x, y + 13));
        y += compact ? 17 : 20;
        if (label.SequenceLabel is { Length: > 0 } sequenceLabel)
        {
            gfx.DrawString(Fit(sequenceLabel, textWidthBesideQr, Title), Title, XBrushes.Black, new XPoint(x, y + 9));
            y += compact ? 12 : 14;
        }

        gfx.DrawString(Fit(label.UnitTypeLabel, textWidthBesideQr, Body), Body, XBrushes.Black, new XPoint(x, y + 8));
        y += lineHeight;

        // 5. Bottom band is reserved first so the info lines know where to stop.
        var barHeight = compact ? 30.0 : 48.0;
        var indicators = new List<string>();
        if (label.IsFragile) indicators.Add("BREEKBAAR");
        if (label.AdrRequired) indicators.Add("ADR");
        if (label.RequiresTemperatureControl) indicators.Add("TEMPERATUUR");
        if (label.RequiresSignature) indicators.Add("HANDTEKENING");
        var indicatorHeight = indicators.Count > 0 ? (compact ? 12.0 : 16.0) : 0.0;
        var bottomBandTop = area.Bottom - 8 - 12 - barHeight - indicatorHeight;

        // 3. Info lines.
        void Line(string text, XFont? font = null)
        {
            var lineFont = font ?? Body;
            if (y + lineHeight > bottomBandTop)
            {
                return; // hard cap: never run into the indicator/barcode band
            }

            var maxWidth = y < qrTop + qrSize ? textWidthBesideQr : width;
            gfx.DrawString(Fit(text, maxWidth, lineFont), lineFont, XBrushes.Black,
                new XRect(x, y, maxWidth, 12), XStringFormats.TopLeft);
            y += lineHeight;
        }

        Line($"Klant: {label.CustomerName}");
        if (label.LoadingLocation is not null)
        {
            Line($"Laden: {label.LoadingLocation}");
        }

        Line($"Leveren: {label.DeliveryLocation ?? "—"}"
             + (label.DeliveryStopSequence is { } sequence ? $" (stop {sequence})" : string.Empty));
        if (label.CustomerReference is not null)
        {
            Line($"Referentie: {label.CustomerReference}");
        }

        if (label.WeightKg is { } weight)
        {
            Line($"Gewicht: {weight:0.##} kg");
        }

        if (label.HandlingInstructions is { Length: > 0 } instructions)
        {
            Line($"Instructies: {instructions}", Small);
        }

        // 4. Indicator band.
        if (indicators.Count > 0)
        {
            gfx.DrawString(string.Join("  ·  ", indicators), Title, XBrushes.Black,
                new XRect(x, bottomBandTop, width, indicatorHeight), XStringFormats.TopLeft);
        }

        // 5. Full-width Code 128 + human-readable value.
        var modules = Code128Encoder.ModuleWidths(label.BarcodeValue);
        if (modules is not null)
        {
            var totalModules = modules.Sum();
            var moduleWidth = Math.Min(width / totalModules, 1.6);
            var barsWidth = totalModules * moduleWidth;
            var barX = x + (width - barsWidth) / 2; // centred, quiet zones on both sides
            var barY = bottomBandTop + indicatorHeight;
            var isBar = true;
            foreach (var module in modules)
            {
                var barWidth = module * moduleWidth;
                if (isBar)
                {
                    gfx.DrawRectangle(XBrushes.Black, barX, barY, barWidth, barHeight);
                }

                barX += barWidth;
                isBar = !isBar;
            }

            gfx.DrawString(label.BarcodeValue, Mono, XBrushes.Black,
                new XRect(x, barY + barHeight + 2, width, 10), XStringFormats.TopCenter);
        }
    }

    private static readonly XFont HeaderCompany = new("Arial", 13, XFontStyleEx.Bold);
    private static readonly XFont ZoneHeading = new("Arial", 7.5, XFontStyleEx.Bold);
    private static readonly XFont ZoneBody = new("Arial", 9, XFontStyleEx.Regular);
    private static readonly XFont SummaryLabel = new("Arial", 7, XFontStyleEx.Regular);
    private static readonly XFont SummaryValue = new("Arial", 9.5, XFontStyleEx.Bold);

    /// <summary>
    /// Redesigned horizontal label (150×100 mm). Deterministic fixed zones:
    /// header band (company + human-readable barcode number), a left column with grouped
    /// EXPÉDITEUR/VERZENDER and DESTINATAIRE/BESTEMMING blocks, a right summary column
    /// (reference, collo x/y, weight, volume, PO, indicators), and a full-width Code 128 at
    /// the bottom. All text is truncated with an ellipsis; nothing clips. Falls back to the
    /// legacy snapshot fields when the redesign fields are absent (old snapshots re-render).
    /// </summary>
    private static void DrawHorizontalLabel(XGraphics gfx, LabelSnapshot label, XRect area)
    {
        const double margin = 12;
        var left = area.X + margin;
        var right = area.Right - margin;
        var contentWidth = right - left;
        var summaryWidth = contentWidth * 0.34;
        var addressWidth = contentWidth - summaryWidth - 12;
        var addressRight = left + addressWidth;

        string Fit(string text, double maxWidth, XFont font)
        {
            if (string.IsNullOrEmpty(text) || gfx.MeasureString(text, font).Width <= maxWidth)
            {
                return text ?? string.Empty;
            }

            while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > maxWidth)
            {
                text = text[..^1];
            }

            return text + "…";
        }

        // 1. Header band: company (left), human-readable barcode number (right).
        var y = area.Y + margin;
        gfx.DrawString(Fit(label.TenantName, contentWidth * 0.6, HeaderCompany), HeaderCompany, XBrushes.Black, new XPoint(left, y + 12));
        gfx.DrawString(label.BarcodeValue, Mono, XBrushes.Black, new XRect(left, y, contentWidth, 14), XStringFormats.TopRight);
        y += 22;
        gfx.DrawLine(new XPen(XColors.Black, 1.0), left, y, right, y);
        y += 8;

        // 2. Address column: sender + destination blocks.
        var senderName = label.SenderName ?? label.CustomerName;
        var senderCity = label.SenderPostalCodeCity ?? label.LoadingLocation;
        var blockTop = y;
        y = DrawAddressBlock(gfx, "EXPÉDITEUR - VERZENDER", senderName,
            label.SenderStreet, senderCity, label.SenderCountry, label.PickupDate, label.PickupTime,
            left, addressRight - left, y, Fit);

        y += 6;
        var recipientName = label.RecipientName ?? label.DeliveryLocation;
        var recipientCity = label.RecipientPostalCodeCity ?? label.DeliveryLocation;
        DrawAddressBlock(gfx, "DESTINATAIRE - BESTEMMING", recipientName,
            label.RecipientStreet, recipientCity, label.RecipientCountry, label.DeliveryDate, label.DeliveryTime,
            left, addressRight - left, y, Fit);

        // Vertical divider between addresses and summary.
        var summaryX = addressRight + 12;
        gfx.DrawLine(new XPen(XColors.LightGray, 0.6), addressRight + 6, blockTop, addressRight + 6, area.Bottom - 34);

        // 3. Right summary column.
        var sy = blockTop;
        void Summary(string labelText, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            gfx.DrawString(labelText, SummaryLabel, XBrushes.Gray, new XPoint(summaryX, sy + 6));
            gfx.DrawString(Fit(value, summaryWidth, SummaryValue), SummaryValue, XBrushes.Black, new XPoint(summaryX, sy + 17));
            sy += 22;
        }

        var sequence = label.SequenceNumber is { } n && label.SequenceTotal is { } t ? $"{n} / {t}" : label.SequenceLabel;
        Summary("Referentie", label.CustomerReference);
        Summary("Collo", sequence);
        Summary("Gewicht", label.WeightKg is { } w ? $"{w:0.##} kg" : null);
        Summary("Volume", label.VolumeM3 is { } v ? $"{v:0.###} m³" : null);
        Summary("PO-nummer", label.PurchaseOrderNumber);
        Summary("Rembours", label.CashOnDelivery);

        var indicators = new List<string>();
        if (label.AdrRequired) indicators.Add("ADR");
        if (label.RequiresTemperatureControl) indicators.Add("TEMP");
        if (label.IsFragile) indicators.Add("BREEKBAAR");
        if (label.RequiresSignature) indicators.Add("HANDTEK.");
        if (indicators.Count > 0)
        {
            gfx.DrawString(Fit(string.Join("  ·  ", indicators), summaryWidth, ZoneHeading), ZoneHeading, XBrushes.Black, new XPoint(summaryX, sy + 8));
        }

        if (!string.IsNullOrWhiteSpace(label.HandlingInstructions))
        {
            gfx.DrawString(Fit(label.HandlingInstructions, summaryWidth, Small), Small, XBrushes.Black, new XPoint(summaryX, sy + 22));
        }

        // 4. Full-width Code 128 at the bottom.
        var modules = Code128Encoder.ModuleWidths(label.BarcodeValue);
        if (modules is not null)
        {
            var totalModules = modules.Sum();
            var barHeight = 26.0;
            var barY = area.Bottom - margin - barHeight;
            var moduleWidth = Math.Min(contentWidth / totalModules, 1.4);
            var barsWidth = totalModules * moduleWidth;
            var barX = left + (contentWidth - barsWidth) / 2;
            var isBar = true;
            foreach (var module in modules)
            {
                var barWidth = module * moduleWidth;
                if (isBar)
                {
                    gfx.DrawRectangle(XBrushes.Black, barX, barY, barWidth, barHeight);
                }

                barX += barWidth;
                isBar = !isBar;
            }
        }
    }

    private static double DrawAddressBlock(
        XGraphics gfx, string heading, string? name, string? street, string? city, string? country,
        string? date, string? time, double x, double width, double y, Func<string, double, XFont, string> fit)
    {
        gfx.DrawString(heading, ZoneHeading, XBrushes.Black, new XPoint(x, y + 7));
        y += 12;
        if (!string.IsNullOrWhiteSpace(name))
        {
            gfx.DrawString(fit(name, width, new XFont("Arial", 9, XFontStyleEx.Bold)), new XFont("Arial", 9, XFontStyleEx.Bold), XBrushes.Black, new XPoint(x, y + 8));
            y += 12;
        }

        if (!string.IsNullOrWhiteSpace(street))
        {
            gfx.DrawString(fit(street, width, ZoneBody), ZoneBody, XBrushes.Black, new XPoint(x, y + 8));
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            gfx.DrawString(fit(city, width, ZoneBody), ZoneBody, XBrushes.Black, new XPoint(x, y + 8));
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            gfx.DrawString(fit(country, width, ZoneBody), ZoneBody, XBrushes.Black, new XPoint(x, y + 8));
            y += 11;
        }

        var dateTime = string.Join("  ", new[] { date, time }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(dateTime))
        {
            gfx.DrawString(fit(dateTime, width, Small), Small, XBrushes.Black, new XPoint(x, y + 8));
            y += 11;
        }

        return y;
    }
}
