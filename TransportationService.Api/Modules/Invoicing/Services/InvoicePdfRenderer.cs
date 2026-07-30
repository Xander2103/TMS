using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Invoicing.Services;

public sealed record InvoicePdfLine(string Description, decimal Quantity, decimal UnitPrice, decimal VatRatePercent, decimal LineTotal);

/// <summary>Everything the renderer needs; assembled by <see cref="IInvoicePdfService"/> from the
/// invoice's frozen seller/customer snapshot plus the (live, not snapshotted) legal-entity
/// footer/logo/banking details. Presentation only — never the structured Peppol document.</summary>
public sealed record InvoicePdfSnapshot(
    string SellerName,
    string? SellerAddressLine,
    string? SellerVatNumber,
    string? SellerIban,
    string? SellerBic,
    string? InvoiceFooter,
    string? VatLegalText,
    byte[]? LogoBytes,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CustomerName,
    string? CustomerAddressLine,
    string? CustomerVatNumber,
    string? PurchaseOrderNumber,
    IReadOnlyList<InvoicePdfLine> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    string Currency,
    string? Notes);

/// <summary>
/// Renders a one-page-per-overflow invoice PDF: header (seller + optional logo), invoice meta,
/// customer block, line table, VAT breakdown by rate, totals, payment block and footer. Follows
/// the same PDFsharp pattern as <c>IssuedItemAcknowledgementRenderer</c>/<c>LabelRenderService</c>
/// — QuestPDF is NOT a dependency of this codebase (only PDFsharp is referenced in the .csproj),
/// so this deliberately does not introduce it; see task report.
/// </summary>
public static class InvoicePdfRenderer
{
    // MUST be the first static field: initializers run in textual order, and the XFont fields
    // below need the font source configured first.
    private static readonly bool FontsConfigured = ConfigureFonts();

    private static bool ConfigureFonts()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    }

    private static readonly XFont Title = new("Arial", 16, XFontStyleEx.Bold);
    private static readonly XFont Heading = new("Arial", 10, XFontStyleEx.Bold);
    private static readonly XFont Body = new("Arial", 9, XFontStyleEx.Regular);
    private static readonly XFont BodyBold = new("Arial", 9, XFontStyleEx.Bold);
    private static readonly XFont Small = new("Arial", 7.5, XFontStyleEx.Regular);

    public static byte[] Render(InvoicePdfSnapshot invoice)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);

        const double margin = 40.0;
        var width = page.Width.Point - 2 * margin;
        var y = margin;

        // --- Header: seller name/address (+ logo, right-aligned) ---
        if (invoice.LogoBytes is { Length: > 0 } logoBytes)
        {
            try
            {
                using var logoStream = new MemoryStream(logoBytes);
                using var logo = XImage.FromStream(logoStream);
                var logoHeight = 40.0;
                var logoWidth = logo.PixelWidth > 0 ? logoHeight * logo.PixelWidth / logo.PixelHeight : logoHeight;
                gfx.DrawImage(logo, margin + width - logoWidth, y, logoWidth, logoHeight);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
            {
                // Corrupt/unsupported logo bytes never block invoice PDF generation.
            }
        }

        gfx.DrawString(invoice.SellerName, Title, XBrushes.Black, new XPoint(margin, y + 16));
        y += 22;
        if (invoice.SellerAddressLine is { Length: > 0 })
        {
            gfx.DrawString(invoice.SellerAddressLine, Body, XBrushes.Black, new XPoint(margin, y + 10));
            y += 14;
        }

        if (invoice.SellerVatNumber is { Length: > 0 })
        {
            gfx.DrawString($"BTW: {invoice.SellerVatNumber}", Body, XBrushes.Black, new XPoint(margin, y + 10));
            y += 14;
        }

        y += 14;
        gfx.DrawLine(new XPen(XColors.Black, 1.0), margin, y, margin + width, y);
        y += 16;

        // --- Invoice meta + customer block, side by side ---
        var metaX = margin + width * 0.55;
        gfx.DrawString("FACTUUR", Heading, XBrushes.Black, new XPoint(metaX, y + 9));
        y += 16;
        gfx.DrawString($"Factuurnummer: {invoice.InvoiceNumber}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
        var customerY = y - 16;
        y += 14;
        gfx.DrawString($"Factuurdatum: {invoice.InvoiceDate:dd-MM-yyyy}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
        y += 14;
        gfx.DrawString($"Vervaldatum: {invoice.DueDate:dd-MM-yyyy}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
        y += 14;
        if (invoice.PurchaseOrderNumber is { Length: > 0 })
        {
            gfx.DrawString($"PO-nummer: {invoice.PurchaseOrderNumber}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
            y += 14;
        }

        gfx.DrawString("Factuuradres", Heading, XBrushes.Black, new XPoint(margin, customerY + 9));
        var cy = customerY + 14;
        gfx.DrawString(invoice.CustomerName, BodyBold, XBrushes.Black, new XPoint(margin, cy + 9));
        cy += 14;
        if (invoice.CustomerAddressLine is { Length: > 0 })
        {
            gfx.DrawString(invoice.CustomerAddressLine, Body, XBrushes.Black, new XPoint(margin, cy + 9));
            cy += 14;
        }

        if (invoice.CustomerVatNumber is { Length: > 0 })
        {
            gfx.DrawString($"BTW: {invoice.CustomerVatNumber}", Body, XBrushes.Black, new XPoint(margin, cy + 9));
            cy += 14;
        }

        y = Math.Max(y, cy) + 16;

        // --- Line table ---
        var colDesc = margin;
        var colQty = margin + width * 0.52;
        var colPrice = margin + width * 0.64;
        var colVat = margin + width * 0.80;
        var colTotal = margin + width * 0.88;

        void DrawTableHeader()
        {
            gfx.DrawString("Omschrijving", Heading, XBrushes.Black, new XPoint(colDesc, y + 9));
            gfx.DrawString("Aantal", Heading, XBrushes.Black, new XPoint(colQty, y + 9));
            gfx.DrawString("Prijs", Heading, XBrushes.Black, new XPoint(colPrice, y + 9));
            gfx.DrawString("BTW%", Heading, XBrushes.Black, new XPoint(colVat, y + 9));
            gfx.DrawString("Totaal", Heading, XBrushes.Black, new XPoint(colTotal, y + 9));
            y += 14;
            gfx.DrawLine(new XPen(XColors.Black, 0.75), margin, y, margin + width, y);
            y += 8;
        }

        DrawTableHeader();
        foreach (var line in invoice.Lines)
        {
            if (y > page.Height.Point - 220)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
                DrawTableHeader();
            }

            gfx.DrawString(Truncate(line.Description, 60), Body, XBrushes.Black, new XPoint(colDesc, y + 9));
            gfx.DrawString(line.Quantity.ToString("0.##"), Body, XBrushes.Black, new XPoint(colQty, y + 9));
            gfx.DrawString(line.UnitPrice.ToString("0.00"), Body, XBrushes.Black, new XPoint(colPrice, y + 9));
            gfx.DrawString(line.VatRatePercent.ToString("0.##"), Body, XBrushes.Black, new XPoint(colVat, y + 9));
            gfx.DrawString(line.LineTotal.ToString("0.00"), Body, XBrushes.Black, new XPoint(colTotal, y + 9));
            y += 14;
        }

        y += 4;
        gfx.DrawLine(new XPen(XColors.LightGray, 0.5), margin, y, margin + width, y);
        y += 14;

        // --- VAT breakdown by rate ---
        var byRate = invoice.Lines
            .GroupBy(l => l.VatRatePercent)
            .Select(g => new { Rate = g.Key, Base = g.Sum(l => l.LineTotal), Vat = Math.Round(g.Sum(l => l.LineTotal) * g.Key / 100m, 2) })
            .OrderBy(g => g.Rate)
            .ToList();

        // Everything from here down (VAT breakdown, totals, payment block, notes) is one visual
        // unit that must never straddle the fixed-position footer — estimate its height up front
        // and start a fresh page if it would run into the footer zone (same page-break idiom as
        // the line-item loop above, just applied to this block as a whole instead of per-line).
        var postTableHeight =
            (byRate.Count > 0 ? 14 + byRate.Count * 12 + 8 : 0)
            + 3 * 15 + 4 + 12 // subtotal/btw/totaal lines + divider gap + trailing gap
            + (invoice.SellerIban is { Length: > 0 } ? 3 * 14 : 0)
            + (invoice.Notes is { Length: > 0 } ? 6 + 16 : 0);
        const double footerReserve = 50;
        if (y + postTableHeight > page.Height.Point - margin - footerReserve)
        {
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            y = margin;
        }

        if (byRate.Count > 0)
        {
            gfx.DrawString("BTW-overzicht", Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            foreach (var group in byRate)
            {
                gfx.DrawString(
                    $"{group.Rate:0.##}% over {invoice.Currency} {group.Base:0.00} = {invoice.Currency} {group.Vat:0.00}",
                    Small, XBrushes.Black, new XPoint(margin, y + 8));
                y += 12;
            }

            y += 8;
        }

        // --- Totals block (right-aligned) ---
        var totalsX = margin + width * 0.62;
        var totalsWidth = width - width * 0.62;
        void TotalLine(string label, decimal amount, XFont font)
        {
            gfx.DrawString(label, font, XBrushes.Black, new XRect(totalsX, y, totalsWidth * 0.5, 12), XStringFormats.TopLeft);
            gfx.DrawString($"{invoice.Currency} {amount:0.00}", font, XBrushes.Black,
                new XRect(totalsX + totalsWidth * 0.5, y, totalsWidth * 0.5, 12), XStringFormats.TopRight);
            y += 15;
        }

        TotalLine("Subtotaal", invoice.Subtotal, Body);
        TotalLine("BTW", invoice.VatAmount, Body);
        gfx.DrawLine(new XPen(XColors.Black, 0.75), totalsX, y, totalsX + totalsWidth, y);
        y += 4;
        TotalLine("Totaal", invoice.Total, BodyBold);
        y += 12;

        // --- Payment block ---
        if (invoice.SellerIban is { Length: > 0 })
        {
            gfx.DrawString("Betaalgegevens", Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            gfx.DrawString(
                $"IBAN: {invoice.SellerIban}" + (invoice.SellerBic is { Length: > 0 } bic ? $"  BIC: {bic}" : string.Empty),
                Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            gfx.DrawString($"Mededeling: {invoice.InvoiceNumber}", Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
        }

        if (invoice.Notes is { Length: > 0 })
        {
            y += 6;
            gfx.DrawString(invoice.Notes, Small, XBrushes.Black, new XPoint(margin, y + 8));
            y += 16;
        }

        // --- Footer ---
        var footerY = page.Height.Point - margin - 24;
        if (invoice.VatLegalText is { Length: > 0 })
        {
            gfx.DrawString(invoice.VatLegalText, Small, XBrushes.Black, new XPoint(margin, footerY));
            footerY += 11;
        }

        if (invoice.InvoiceFooter is { Length: > 0 })
        {
            gfx.DrawString(invoice.InvoiceFooter, Small, XBrushes.Gray, new XPoint(margin, footerY));
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
