using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// Renders a bilingual (NL/FR) acknowledgement document for the items issued to an employee:
/// items, quantities, issue/return dates and signature lines. Generated from structured data
/// (never a hand-filled form), so the document always reflects the current issued-item state.
/// </summary>
public static class IssuedItemAcknowledgementRenderer
{
    // MUST be the first static field: initializers run in textual order, and the XFont
    // fields below need the font source configured first (see LabelRenderService).
    private static readonly bool FontsConfigured = ConfigureFonts();

    private static bool ConfigureFonts()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    }

    private static readonly XFont Title = new("Arial", 14, XFontStyleEx.Bold);
    private static readonly XFont Heading = new("Arial", 10, XFontStyleEx.Bold);
    private static readonly XFont Body = new("Arial", 9, XFontStyleEx.Regular);
    private static readonly XFont Small = new("Arial", 8, XFontStyleEx.Regular);

    public static byte[] Render(string companyName, string employeeName, string employeeNumber, IReadOnlyList<EmployeeIssuedItem> items)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);

        var margin = 40.0;
        var width = page.Width.Point - 2 * margin;
        var y = margin;

        gfx.DrawString(companyName, Title, XBrushes.Black, new XPoint(margin, y + 12));
        y += 30;
        gfx.DrawString("Ontvangstbewijs bedrijfsmiddelen / Reçu de matériel d'entreprise", Heading, XBrushes.Black, new XPoint(margin, y));
        y += 20;
        gfx.DrawString($"Medewerker / Collaborateur: {employeeName} ({employeeNumber})", Body, XBrushes.Black, new XPoint(margin, y));
        y += 22;

        // Table header.
        var colName = margin;
        var colQty = margin + width * 0.50;
        var colIssued = margin + width * 0.62;
        var colReturned = margin + width * 0.80;
        gfx.DrawString("Item", Heading, XBrushes.Black, new XPoint(colName, y));
        gfx.DrawString("Aantal", Heading, XBrushes.Black, new XPoint(colQty, y));
        gfx.DrawString("Uitgereikt", Heading, XBrushes.Black, new XPoint(colIssued, y));
        gfx.DrawString("Terug", Heading, XBrushes.Black, new XPoint(colReturned, y));
        y += 4;
        gfx.DrawLine(XPens.Black, margin, y, margin + width, y);
        y += 14;

        if (items.Count == 0)
        {
            gfx.DrawString("Geen uitgereikte middelen geregistreerd.", Small, XBrushes.Gray, new XPoint(colName, y));
            y += 16;
        }

        foreach (var item in items)
        {
            var label = item.SerialNumber is { Length: > 0 } serial ? $"{item.NameSnapshot} (nr. {serial})" : item.NameSnapshot;
            gfx.DrawString(Truncate(label, 48), Body, XBrushes.Black, new XPoint(colName, y));
            gfx.DrawString(item.Quantity.ToString(), Body, XBrushes.Black, new XPoint(colQty, y));
            gfx.DrawString(item.IssuedDate?.ToString("dd-MM-yyyy") ?? "-", Body, XBrushes.Black, new XPoint(colIssued, y));
            gfx.DrawString(item.ReturnedDate?.ToString("dd-MM-yyyy") ?? "-", Body, XBrushes.Black, new XPoint(colReturned, y));
            y += 15;

            if (y > page.Height.Point - 130)
            {
                page = document.AddPage();
                y = margin;
            }
        }

        y = Math.Max(y, page.Height.Point - 120);
        gfx.DrawString(
            "Ik bevestig de ontvangst van bovenstaande middelen. / Je confirme la réception du matériel ci-dessus.",
            Small, XBrushes.Black, new XPoint(margin, y));
        y += 40;
        gfx.DrawString("Handtekening / Signature: ______________________", Body, XBrushes.Black, new XPoint(margin, y));
        gfx.DrawString("Datum / Date: ______________", Body, XBrushes.Black, new XPoint(margin + width * 0.6, y));

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
