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
    bool RequiresSignature);

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
                page.Width = XUnit.FromMillimeter(100);
                page.Height = XUnit.FromMillimeter(150);
                using var gfx = XGraphics.FromPdfPage(page);
                DrawLabel(gfx, snapshot, new XRect(0, 0, page.Width.Point, page.Height.Point), compact: false);
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

    private static void DrawLabel(XGraphics gfx, LabelSnapshot label, XRect area, bool compact)
    {
        var x = area.X + 10;
        var width = area.Width - 20;
        var y = area.Y + 8;

        gfx.DrawString(label.TenantName, Title, XBrushes.Black, new XPoint(x, y + 10));
        y += 18;
        gfx.DrawString($"{label.PackageNumber} · {label.UnitTypeLabel}", Big, XBrushes.Black, new XPoint(x, y + 14));
        y += 24;

        // Code 128 barcode (bars only; value printed beneath).
        var modules = Code128Encoder.ModuleWidths(label.BarcodeValue);
        if (modules is not null)
        {
            var totalModules = modules.Sum();
            var moduleWidth = Math.Min(width / totalModules, 1.6);
            var barHeight = compact ? 34.0 : 52.0;
            var barX = x;
            var isBar = true;
            foreach (var module in modules)
            {
                var barWidth = module * moduleWidth;
                if (isBar)
                {
                    gfx.DrawRectangle(XBrushes.Black, barX, y, barWidth, barHeight);
                }

                barX += barWidth;
                isBar = !isBar;
            }

            y += barHeight + 4;
            gfx.DrawString(label.BarcodeValue, Mono, XBrushes.Black, new XPoint(x, y + 7));
            y += 14;
        }

        // QR (same payload) top-right, when enabled.
        if (label.IncludeQr)
        {
            using var generator = new QRCodeGenerator();
            var qr = generator.CreateQrCode(label.BarcodeValue, QRCodeGenerator.ECCLevel.M);
            var size = compact ? 44.0 : 62.0;
            var moduleSize = size / qr.ModuleMatrix.Count;
            var originX = area.Right - size - 10;
            var originY = area.Y + 8;
            for (var row = 0; row < qr.ModuleMatrix.Count; row += 1)
            {
                for (var column = 0; column < qr.ModuleMatrix.Count; column += 1)
                {
                    if (qr.ModuleMatrix[row][column])
                    {
                        gfx.DrawRectangle(XBrushes.Black,
                            originX + column * moduleSize, originY + row * moduleSize, moduleSize, moduleSize);
                    }
                }
            }
        }

        void Line(string text, XFont? font = null)
        {
            gfx.DrawString(text, font ?? Body, XBrushes.Black,
                new XRect(x, y, width, 12), XStringFormats.TopLeft);
            y += compact ? 10 : 12;
        }

        Line($"Opdracht: {label.OrderNumber} — {label.CustomerName}");
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
            Line($"Instructies: {(instructions.Length > 90 ? instructions[..90] + "…" : instructions)}", Small);
        }

        var indicators = new List<string>();
        if (label.IsFragile) indicators.Add("BREEKBAAR");
        if (label.AdrRequired) indicators.Add("ADR");
        if (label.RequiresTemperatureControl) indicators.Add("TEMPERATUUR");
        if (label.RequiresSignature) indicators.Add("HANDTEKENING");
        if (indicators.Count > 0)
        {
            gfx.DrawString(string.Join("  ·  ", indicators), Title, XBrushes.Black,
                new XRect(x, area.Bottom - 20, width, 14), XStringFormats.TopLeft);
        }
    }
}
