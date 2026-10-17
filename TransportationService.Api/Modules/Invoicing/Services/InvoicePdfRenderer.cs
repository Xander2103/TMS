using TransportationService.Api.Common.Money;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace TransportationService.Api.Modules.Invoicing.Services;

public sealed record InvoicePdfLine(
    string Description, decimal Quantity, decimal UnitPrice, decimal VatRatePercent, decimal LineTotal,
    /// <summary>Human unit ("uur", "Europallet"); null = piece, printed blank.</summary>
    string? UnitLabel = null);

/// <summary>Everything the renderer needs; assembled by <see cref="IInvoicePdfService"/> from the
/// invoice's frozen seller/customer snapshot. Presentation only — never the structured Peppol document.</summary>
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
    string? Notes,
    bool IsCreditNote = false,
    string? CreditedInvoiceNumber = null,
    /// <summary>Wave 2: document language frozen on the invoice; unknown/missing = Dutch.</summary>
    string? LanguageCode = null,
    /// <summary>Draft preview: the document is stamped as such so it can never pass for the final invoice.</summary>
    bool IsDraft = false,
    /// <summary>ACC-M3: enterprise numbers next to the VAT numbers.</summary>
    string? SellerCompanyNumber = null,
    string? CustomerCompanyNumber = null,
    /// <summary>ACC-M3: "Betaling binnen 30 dagen, uiterlijk op …" (invoices only).</summary>
    string? PaymentTermsText = null,
    /// <summary>Structured payment reference (OGM) or the invoice number.</summary>
    string? PaymentReference = null,
    /// <summary>ACC-M11: why the credit note was made.</summary>
    string? CreditReason = null);

/// <summary>
/// Renders a one-page-per-overflow invoice PDF: header (seller + optional logo), invoice meta,
/// customer block, line table (description wrapped inside its column, unit column), VAT
/// breakdown by rate, totals, payment block and footer. Follows the same PDFsharp pattern as
/// <c>IssuedItemAcknowledgementRenderer</c>/<c>LabelRenderService</c> — QuestPDF is NOT a
/// dependency of this codebase (only PDFsharp is referenced in the .csproj).
/// </summary>
public static class InvoicePdfRenderer
{
    // MUST be the first static field: initializers run in textual order, and the XFont fields
    // below need the font source configured first.
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
    private static readonly XFont BodyBold = new("Arial", 9, XFontStyleEx.Bold);
    private static readonly XFont Small = new("Arial", 7.5, XFontStyleEx.Regular);

    private const double LineHeight = 12.0;

    public static byte[] Render(InvoicePdfSnapshot invoice)
    {
        var strings = InvoicePdfStrings.For(invoice.LanguageCode);
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

        gfx.DrawString(Fit(gfx, invoice.SellerName, Title, width * 0.7), Title, XBrushes.Black, new XPoint(margin, y + 16));
        y += 22;
        if (invoice.SellerAddressLine is { Length: > 0 })
        {
            gfx.DrawString(Fit(gfx, invoice.SellerAddressLine, Body, width * 0.7), Body, XBrushes.Black, new XPoint(margin, y + 10));
            y += 14;
        }

        var sellerIds = string.Join("   ", new[]
        {
            invoice.SellerVatNumber is { Length: > 0 } ? $"{strings.VatNumberLabel}: {invoice.SellerVatNumber}" : null,
            invoice.SellerCompanyNumber is { Length: > 0 } ? $"{strings.CompanyNumberLabel} {invoice.SellerCompanyNumber}" : null,
        }.Where(v => v is not null));
        if (sellerIds.Length > 0)
        {
            gfx.DrawString(sellerIds, Body, XBrushes.Black, new XPoint(margin, y + 10));
            y += 14;
        }

        y += 14;
        gfx.DrawLine(new XPen(XColors.Black, 1.0), margin, y, margin + width, y);
        y += 16;

        // --- Invoice meta + customer block, side by side ---
        var metaX = margin + width * 0.55;
        var metaWidth = width - width * 0.55;
        var customerWidth = width * 0.52;
        var documentTitle = invoice.IsCreditNote ? strings.CreditNoteTitle : strings.Title;
        if (invoice.IsDraft) documentTitle = $"{documentTitle} — {strings.DraftLabel}";
        gfx.DrawString(documentTitle, Heading, XBrushes.Black, new XPoint(metaX, y + 9));
        y += 16;
        gfx.DrawString(
            $"{(invoice.IsCreditNote ? strings.CreditNoteNumberLabel : strings.InvoiceNumberLabel)}: {invoice.InvoiceNumber}",
            Body, XBrushes.Black, new XPoint(metaX, y + 9));
        var customerY = y - 16;
        y += 14;
        gfx.DrawString($"{strings.DateLabel}: {invoice.InvoiceDate.ToString(strings.DateFormat)}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
        y += 14;
        if (!invoice.IsCreditNote)
        {
            gfx.DrawString($"{strings.DueDateLabel}: {invoice.DueDate.ToString(strings.DateFormat)}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
            y += 14;
        }

        if (invoice.CreditedInvoiceNumber is { Length: > 0 })
        {
            gfx.DrawString($"{strings.CreditsInvoiceLabel}: {invoice.CreditedInvoiceNumber}", Body, XBrushes.Black, new XPoint(metaX, y + 9));
            y += 14;
        }
        if (invoice.PurchaseOrderNumber is { Length: > 0 })
        {
            gfx.DrawString(Fit(gfx, $"{strings.PoNumberLabel}: {invoice.PurchaseOrderNumber}", Body, metaWidth), Body, XBrushes.Black, new XPoint(metaX, y + 9));
            y += 14;
        }

        gfx.DrawString(strings.BillingAddressHeading, Heading, XBrushes.Black, new XPoint(margin, customerY + 9));
        var cy = customerY + 14;
        // A long legal name wraps inside the customer block instead of running into the meta column.
        foreach (var nameLine in Wrap(gfx, invoice.CustomerName, BodyBold, customerWidth, 3))
        {
            gfx.DrawString(nameLine, BodyBold, XBrushes.Black, new XPoint(margin, cy + 9));
            cy += 14;
        }

        if (invoice.CustomerAddressLine is { Length: > 0 })
        {
            foreach (var addressLine in Wrap(gfx, invoice.CustomerAddressLine, Body, customerWidth, 2))
            {
                gfx.DrawString(addressLine, Body, XBrushes.Black, new XPoint(margin, cy + 9));
                cy += 14;
            }
        }

        var customerIds = string.Join("   ", new[]
        {
            invoice.CustomerVatNumber is { Length: > 0 } ? $"{strings.VatNumberLabel}: {invoice.CustomerVatNumber}" : null,
            invoice.CustomerCompanyNumber is { Length: > 0 } ? $"{strings.CompanyNumberLabel} {invoice.CustomerCompanyNumber}" : null,
        }.Where(v => v is not null));
        if (customerIds.Length > 0)
        {
            gfx.DrawString(Fit(gfx, customerIds, Body, customerWidth), Body, XBrushes.Black, new XPoint(margin, cy + 9));
            cy += 14;
        }

        y = Math.Max(y, cy) + 16;

        // --- Line table: fixed columns, description wrapped inside its own width (ACC-M3) ---
        var colDesc = margin;
        var colQty = margin + width * 0.46;
        var colUnit = margin + width * 0.54;
        var colPrice = margin + width * 0.66;
        var colVat = margin + width * 0.80;
        var colTotal = margin + width * 0.88;
        var descWidth = colQty - colDesc - 6;
        var unitWidth = colPrice - colUnit - 4;

        void DrawTableHeader()
        {
            gfx.DrawString(strings.DescriptionColumn, Heading, XBrushes.Black, new XPoint(colDesc, y + 9));
            gfx.DrawString(strings.QuantityColumn, Heading, XBrushes.Black, new XPoint(colQty, y + 9));
            gfx.DrawString(strings.UnitColumn, Heading, XBrushes.Black, new XPoint(colUnit, y + 9));
            gfx.DrawString(strings.PriceColumn, Heading, XBrushes.Black, new XPoint(colPrice, y + 9));
            gfx.DrawString(strings.VatColumn, Heading, XBrushes.Black, new XPoint(colVat, y + 9));
            gfx.DrawString(strings.TotalColumn, Heading, XBrushes.Black, new XPoint(colTotal, y + 9));
            y += 14;
            gfx.DrawLine(new XPen(XColors.Black, 0.75), margin, y, margin + width, y);
            y += 8;
        }

        DrawTableHeader();
        foreach (var line in invoice.Lines)
        {
            var descriptionLines = Wrap(gfx, line.Description, Body, descWidth, 3);
            var rowHeight = Math.Max(1, descriptionLines.Count) * LineHeight + 2;
            if (y + rowHeight > page.Height.Point - 220)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = margin;
                DrawTableHeader();
            }

            for (var i = 0; i < descriptionLines.Count; i++)
            {
                gfx.DrawString(descriptionLines[i], Body, XBrushes.Black, new XPoint(colDesc, y + 9 + i * LineHeight));
            }

            gfx.DrawString(line.Quantity.ToString("0.###"), Body, XBrushes.Black, new XPoint(colQty, y + 9));
            if (line.UnitLabel is { Length: > 0 })
            {
                gfx.DrawString(Fit(gfx, line.UnitLabel, Body, unitWidth), Body, XBrushes.Black, new XPoint(colUnit, y + 9));
            }

            gfx.DrawString(FormatPrice(line.UnitPrice), Body, XBrushes.Black, new XPoint(colPrice, y + 9));
            gfx.DrawString(line.VatRatePercent.ToString("0.##"), Body, XBrushes.Black, new XPoint(colVat, y + 9));
            gfx.DrawString(line.LineTotal.ToString("0.00"), Body, XBrushes.Black, new XPoint(colTotal, y + 9));
            y += rowHeight;
        }

        y += 4;
        gfx.DrawLine(new XPen(XColors.LightGray, 0.5), margin, y, margin + width, y);
        y += 14;

        // --- VAT breakdown by rate ---
        var byRate = invoice.Lines
            .GroupBy(l => l.VatRatePercent)
            .Select(g => new { Rate = g.Key, Base = g.Sum(l => l.LineTotal), Vat = MoneyRounding.Round2(g.Sum(l => l.LineTotal) * g.Key / 100m) })
            .OrderBy(g => g.Rate)
            .ToList();

        // Everything from here down (VAT breakdown, totals, payment block, notes) is one visual
        // unit that must never straddle the fixed-position footer — estimate its height up front
        // and start a fresh page if it would run into the footer zone (same page-break idiom as
        // the line-item loop above, just applied to this block as a whole instead of per-line).
        var postTableHeight =
            (byRate.Count > 0 ? 14 + byRate.Count * 12 + 8 : 0)
            + 3 * 15 + 4 + 12 // subtotal/btw/totaal lines + divider gap + trailing gap
            + (invoice.SellerIban is { Length: > 0 } && !invoice.IsCreditNote ? 4 * 14 : 0)
            + (invoice.CreditReason is { Length: > 0 } ? 14 : 0)
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
            gfx.DrawString(strings.VatBreakdownHeading, Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            foreach (var group in byRate)
            {
                gfx.DrawString(
                    $"{group.Rate:0.##}% {strings.VatOverWord} {invoice.Currency} {group.Base:0.00} = {invoice.Currency} {group.Vat:0.00}",
                    Small, XBrushes.Black, new XPoint(margin, y + 8));
                y += 12;
            }

            y += 8;
        }

        // --- Totals block (right-aligned); a credit note reads as a credit ("Te crediteren") ---
        var totalsX = margin + width * 0.62;
        var totalsWidth = width - width * 0.62;
        void TotalLine(string label, decimal amount, XFont font)
        {
            gfx.DrawString(label, font, XBrushes.Black, new XRect(totalsX, y, totalsWidth * 0.5, 12), XStringFormats.TopLeft);
            gfx.DrawString($"{invoice.Currency} {amount:0.00}", font, XBrushes.Black,
                new XRect(totalsX + totalsWidth * 0.5, y, totalsWidth * 0.5, 12), XStringFormats.TopRight);
            y += 15;
        }

        TotalLine(strings.SubtotalLabel, invoice.Subtotal, Body);
        TotalLine(strings.VatLabel, invoice.VatAmount, Body);
        gfx.DrawLine(new XPen(XColors.Black, 0.75), totalsX, y, totalsX + totalsWidth, y);
        y += 4;
        TotalLine(invoice.IsCreditNote ? strings.CreditTotalLabel : strings.TotalLabel, invoice.Total, BodyBold);
        y += 12;

        // --- Payment block: invoices only; a credit note has nothing to pay ---
        if (!invoice.IsCreditNote && invoice.SellerIban is { Length: > 0 })
        {
            gfx.DrawString(strings.PaymentHeading, Heading, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            if (invoice.PaymentTermsText is { Length: > 0 })
            {
                gfx.DrawString(invoice.PaymentTermsText, Body, XBrushes.Black, new XPoint(margin, y + 9));
                y += 14;
            }

            gfx.DrawString(
                $"IBAN: {invoice.SellerIban}" + (invoice.SellerBic is { Length: > 0 } bic ? $"  BIC: {bic}" : string.Empty),
                Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
            gfx.DrawString($"{strings.PaymentReferenceLabel}: {invoice.PaymentReference ?? invoice.InvoiceNumber}", Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
        }

        if (invoice.IsCreditNote && invoice.CreditReason is { Length: > 0 })
        {
            gfx.DrawString(Fit(gfx, $"{strings.CreditReasonLabel}: {invoice.CreditReason}", Body, width), Body, XBrushes.Black, new XPoint(margin, y + 9));
            y += 14;
        }

        if (invoice.Notes is { Length: > 0 })
        {
            y += 6;
            foreach (var noteLine in Wrap(gfx, invoice.Notes, Small, width, 4))
            {
                gfx.DrawString(noteLine, Small, XBrushes.Black, new XPoint(margin, y + 8));
                y += 10;
            }

            y += 6;
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
            gfx.DrawString(Fit(gfx, invoice.InvoiceFooter, Small, width), Small, XBrushes.Gray, new XPoint(margin, footerY));
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Unit prices show their real precision (0,335) but never fewer than two decimals.</summary>
    private static string FormatPrice(decimal value)
    {
        var text = value.ToString("0.00##");
        return text;
    }

    /// <summary>Truncates with an ellipsis so the text fits the given width in points.</summary>
    private static string Fit(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (gfx.MeasureString(text, font).Width <= maxWidth)
        {
            return text;
        }

        var end = text.Length - 1;
        while (end > 0 && gfx.MeasureString(text[..end] + "…", font).Width > maxWidth)
        {
            end--;
        }

        return text[..Math.Max(1, end)].TrimEnd() + "…";
    }

    /// <summary>Word-wraps to the column width; the last allowed line is truncated with an ellipsis.</summary>
    private static List<string> Wrap(XGraphics gfx, string text, XFont font, double maxWidth, int maxLines)
    {
        var result = new List<string>();
        var words = text.Replace("\r", string.Empty).Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                result.Add(current);
                current = word;
            }
            else
            {
                // A single word wider than the column is hard-cut.
                result.Add(Fit(gfx, word, font, maxWidth));
                current = string.Empty;
            }

            if (result.Count == maxLines)
            {
                break;
            }
        }

        if (result.Count < maxLines && current.Length > 0)
        {
            result.Add(current);
        }
        else if (result.Count == maxLines)
        {
            var rest = string.Join(' ', words.Skip(Math.Max(0, words.Length - 1)));
            if (current.Length > 0 || gfx.MeasureString(string.Join(' ', result), font).Width < gfx.MeasureString(text, font).Width)
            {
                result[^1] = Fit(gfx, result[^1] + " " + current + (rest.Length > 0 ? " …" : string.Empty), font, maxWidth);
            }
        }

        if (result.Count == 0)
        {
            result.Add(string.Empty);
        }

        return result;
    }
}
