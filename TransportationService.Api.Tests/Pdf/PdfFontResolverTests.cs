using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using TransportationService.Api.Modules.Pdf;
using Xunit;

namespace TransportationService.Api.Tests.Pdf;

/// <summary>
/// Regression for the production receipt 500 ("No appropriate font found for family name
/// 'Arial'. Implement IFontResolver…"): every PDF renderer must resolve fonts without Windows.
/// </summary>
public class PdfFontResolverTests
{
    [Fact]
    public void Register_InstallsTheResolverOnce()
    {
        PdfFontResolver.Register();
        var first = GlobalFontSettings.FontResolver;
        PdfFontResolver.Register();

        Assert.IsType<PdfFontResolver>(first);
        Assert.Same(first, GlobalFontSettings.FontResolver);
    }

    [Theory]
    [InlineData("Arial", false, PdfFontResolver.RegularFace)]
    [InlineData("Arial", true, PdfFontResolver.BoldFace)]
    [InlineData("Helvetica", false, PdfFontResolver.RegularFace)]
    [InlineData("Any Unknown Family", true, PdfFontResolver.BoldFace)]
    public void ResolveTypeface_MapsEveryFamilyToTheBundledFaces(string family, bool bold, string expectedFace)
    {
        var resolver = new PdfFontResolver();

        var info = resolver.ResolveTypeface(family, bold, italic: false);

        Assert.NotNull(info);
        Assert.Equal(expectedFace, info!.FaceName);
    }

    [Theory]
    [InlineData(PdfFontResolver.RegularFace)]
    [InlineData(PdfFontResolver.BoldFace)]
    public void GetFont_ReturnsTrueTypeBytes_FromOsFontsOrEmbeddedFallback(string face)
    {
        var resolver = new PdfFontResolver();

        var bytes = resolver.GetFont(face);

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 10_000);
        // TrueType sfnt header: 0x00010000 or 'true'.
        Assert.True((bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0) || (bytes[0] == (byte)'t' && bytes[1] == (byte)'r'));
    }

    [Fact]
    public void EmbeddedFallbackFonts_AreShippedInTheAssembly()
    {
        var names = typeof(PdfFontResolver).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("DejaVuSans.ttf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("DejaVuSans-Bold.ttf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ArialFont_CanBeCreated_AndDrawn_WithTheResolverInstalled()
    {
        PdfFontResolver.Register();
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);

        var font = new XFont("Arial", 10, XFontStyleEx.Bold);
        gfx.DrawString("Ontvangstbewijs", font, XBrushes.Black, new XPoint(10, 20));
        using var stream = new MemoryStream();
        document.Save(stream);

        Assert.True(stream.Length > 100);
    }
}
