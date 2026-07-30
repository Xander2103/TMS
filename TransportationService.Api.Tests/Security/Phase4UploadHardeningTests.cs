using System.Text;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Security;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 4: upload/XSS hardening. H6/L3 — magic-byte validation refuses files whose bytes don't
/// match their claimed extension; L1 — SVG never passes; L4 — storage keys cannot escape the
/// storage root, including the sibling-directory prefix trick; L10 — the malware-scan seam
/// blocks flagged uploads before anything hits disk; M5 — the parser-based sanitizer survives
/// the classic bypass repertoire.
/// </summary>
public class Phase4UploadHardeningTests
{
    // ===================== H6/L3 — magic bytes =====================

    private static MemoryStream Bytes(params byte[] data) => new(data);

    [Theory]
    [InlineData("doc.pdf", "%PDF-1.7 inhoud", true)]
    [InlineData("doc.pdf", "MZ een exe", false)]
    [InlineData("foto.png", "<html>nep</html>", false)]
    [InlineData("data.xlsx", "%PDF-1.7", false)]
    public void MatchesSignature_ChecksLeadingBytesPerExtension(string fileName, string content, bool expected)
    {
        var bytes = content.Select(c => (byte)c).ToArray();
        using var stream = new MemoryStream(bytes);
        Assert.Equal(expected, UploadValidation.MatchesSignature(fileName, stream));
    }

    [Fact]
    public void MatchesSignature_Png_RequiresTheFullEightByteSignature()
    {
        using var real = Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00);
        Assert.True(UploadValidation.MatchesSignature("foto.png", real));
    }

    [Fact]
    public void MatchesSignature_Xlsx_AcceptsZipContainers()
    {
        using var zip = Bytes(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00);
        Assert.True(UploadValidation.MatchesSignature("data.xlsx", zip));
    }

    [Fact]
    public void MatchesSignature_Jpeg_RequiresJpegMarker()
    {
        using var real = Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0x00);
        Assert.True(UploadValidation.MatchesSignature("foto.jpg", real));

        using var fake = Bytes(0x89, 0x50, 0x4E, 0x47);
        Assert.False(UploadValidation.MatchesSignature("foto.jpeg", fake));
    }

    [Fact]
    public void MatchesSignature_Webp_RequiresBothRiffAndWebpMarkers()
    {
        // Layout: "RIFF" + 4 length bytes + "WEBP".
        using var real = Bytes(0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50);
        Assert.True(UploadValidation.MatchesSignature("foto.webp", real));

        // A WAV file is RIFF too — the second marker is what makes it WebP.
        using var wav = Bytes(0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45);
        Assert.False(UploadValidation.MatchesSignature("foto.webp", wav));
    }

    [Fact]
    public void MatchesSignature_UnknownExtension_IsLeftToTheExtensionWhitelist()
    {
        using var stream = Bytes(0x00, 0x01);
        Assert.True(UploadValidation.MatchesSignature("tacho.ddd", stream));
    }

    [Fact]
    public void MatchesSignature_NonSeekableStream_FailsClosed()
    {
        using var nonSeekable = new NonSeekableStream("%PDF-1.7"u8.ToArray());
        Assert.False(UploadValidation.MatchesSignature("doc.pdf", nonSeekable));
    }

    [Fact]
    public void MatchesSignature_RewindsTheStream()
    {
        using var stream = new MemoryStream("%PDF-1.7 inhoud"u8.ToArray());
        Assert.True(UploadValidation.MatchesSignature("doc.pdf", stream));
        Assert.Equal(0, stream.Position);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    // ===================== L4 — storage-root containment =====================

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "ts-phase4-tests", Guid.NewGuid().ToString("N"), "data");

    [Fact]
    public async Task StorageKey_WithParentTraversal_IsRejected()
    {
        var root = NewRoot();
        var storage = new LocalFileStorageService(root);
        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync("../buiten/geheim.txt", CancellationToken.None));
    }

    [Fact]
    public async Task StorageKey_ResolvingToASiblingDirectoryWithTheRootAsPrefix_IsRejected()
    {
        // "…\data-evil\secret.txt" starts with "…\data" as a STRING but is outside the root; the
        // trailing-separator comparison is what catches it.
        var root = NewRoot();
        var storage = new LocalFileStorageService(root);
        var sibling = root + "-evil/secret.txt";
        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync(sibling, CancellationToken.None));
    }

    [Fact]
    public async Task StorageRoundTrip_InsideTheRoot_StillWorks()
    {
        var root = NewRoot();
        try
        {
            var storage = new LocalFileStorageService(root);
            var key = await storage.SaveAsync(Guid.NewGuid(), "docs", "test.pdf",
                new MemoryStream("%PDF-1.7"u8.ToArray()), CancellationToken.None);
            await using var read = await storage.OpenReadAsync(key, CancellationToken.None);
            Assert.Equal("%PDF-1.7", await new StreamReader(read).ReadToEndAsync());
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { /* best effort */ }
        }
    }

    // ===================== L10 — malware-scan seam =====================

    private sealed class AlwaysInfectedScanner : IUploadScanner
    {
        public Task<UploadScanResult> ScanAsync(string fileName, Stream content, CancellationToken cancellationToken)
            => Task.FromResult(new UploadScanResult(UploadScanVerdict.Infected, "EICAR"));
    }

    [Fact]
    public async Task FlaggedUpload_IsRefusedBeforeAnythingIsWritten()
    {
        var root = NewRoot();
        try
        {
            var storage = new LocalFileStorageService(root, new AlwaysInfectedScanner());
            await Assert.ThrowsAsync<InfectedUploadException>(() => storage.SaveAsync(
                Guid.NewGuid(), "docs", "besmet.pdf",
                new MemoryStream("%PDF-1.7"u8.ToArray()), CancellationToken.None));

            Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task PassThroughScanner_LetsCleanContentThrough_AndRewindsForTheWrite()
    {
        var root = NewRoot();
        try
        {
            var storage = new LocalFileStorageService(root, new PassThroughUploadScanner());
            var key = await storage.SaveAsync(Guid.NewGuid(), "docs", "schoon.pdf",
                new MemoryStream("%PDF-1.7 inhoud"u8.ToArray()), CancellationToken.None);
            await using var read = await storage.OpenReadAsync(key, CancellationToken.None);
            Assert.Equal("%PDF-1.7 inhoud", await new StreamReader(read).ReadToEndAsync());
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { /* best effort */ }
        }
    }

    // ===================== M5 — parser-based sanitizer bypass repertoire =====================

    [Theory]
    [InlineData("<svg onload=\"alert(1)\"><circle/></svg>")]
    [InlineData("<ScRiPt>alert(1)</sCrIpT>")]
    [InlineData("<a/href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"jAvAsCrIpT:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">x</a>")]
    [InlineData("<a href=\" javascript:alert(1)\">x</a>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<p onclick=\"alert(1)\">x</p>")]
    [InlineData("<math><mtext><script>alert(1)</script></mtext></math>")]
    public void Sanitize_NeverEmitsExecutableConstructs(string payload)
    {
        var result = HtmlSanitizer.Sanitize(payload);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", result);
    }

    [Fact]
    public void Sanitize_MalformedNesting_CannotReassembleIntoAScriptTag()
    {
        // The classic strip-once bypass: removing "<script>" once from "<scr<script>ipt>" leaves
        // "<script>". A parser-based rebuild never re-concatenates stripped fragments — whatever
        // remains is inert, encoded text, never a script ELEMENT.
        var result = HtmlSanitizer.Sanitize("<scr<script>ipt>alert(1)</scr</script>ipt>");
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_EncodesTextContent()
    {
        var result = HtmlSanitizer.Sanitize("<p>a &lt; b &amp; c</p>");
        Assert.Contains("&lt;", result);
        Assert.Contains("&amp;", result);
    }

    [Fact]
    public void Sanitize_KeepsTheAllowedSubset()
    {
        var result = HtmlSanitizer.Sanitize(
            "<h1>Titel</h1><p>Para <strong>vet</strong> <em>cursief</em></p><ul><li>Punt</li></ul><br>"
            + "<a href=\"https://example.com/x\">Link</a>");
        Assert.Contains("<h1>Titel</h1>", result);
        Assert.Contains("<strong>vet</strong>", result);
        Assert.Contains("<ul><li>Punt</li></ul>", result);
        Assert.Contains("<br>", result);
        Assert.Contains("<a href=\"https://example.com/x\">Link</a>", result);
    }
}
