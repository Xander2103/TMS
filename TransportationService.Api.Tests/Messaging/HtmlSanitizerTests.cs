using TransportationService.Api.Modules.Messaging.Services;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>Phase 6 (corrections wave 4): allowlist HTML sanitizer for MessageTemplate.BodyHtml.</summary>
public class HtmlSanitizerTests
{
    [Fact]
    public void Sanitize_StripsScriptTagAndContent()
    {
        var result = HtmlSanitizer.Sanitize("<p>Hallo</p><script>alert('x')</script><p>Doei</p>");

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Equal("<p>Hallo</p><p>Doei</p>", result);
    }

    [Fact]
    public void Sanitize_StripsOnClickAttribute_ButKeepsAllowedTag()
    {
        var result = HtmlSanitizer.Sanitize("<p onclick=\"evil()\">Tekst</p>");

        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<p>Tekst</p>", result);
    }

    [Fact]
    public void Sanitize_RejectsJavascriptHref_DropsHrefButKeepsAnchor()
    {
        var result = HtmlSanitizer.Sanitize("<a href=\"javascript:alert(1)\">Klik</a>");

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<a>Klik</a>", result);
    }

    [Fact]
    public void Sanitize_KeepsHttpsHref()
    {
        var result = HtmlSanitizer.Sanitize("<a href=\"https://example.com/x\">Link</a>");

        Assert.Equal("<a href=\"https://example.com/x\">Link</a>", result);
    }

    [Fact]
    public void Sanitize_StripsDisallowedNestedTags_ButKeepsText()
    {
        var result = HtmlSanitizer.Sanitize("<div class=\"x\"><p>Binnen <span style=\"color:red\">tekst</span></p></div>");

        Assert.Equal("<p>Binnen tekst</p>", result);
    }

    [Fact]
    public void Sanitize_KeepsAllowlistedTags()
    {
        var result = HtmlSanitizer.Sanitize("<h1>Titel</h1><p>Para <strong>vet</strong> <em>cursief</em></p><ul><li>Punt</li></ul><br>");

        Assert.Equal("<h1>Titel</h1><p>Para <strong>vet</strong> <em>cursief</em></p><ul><li>Punt</li></ul><br>", result);
    }

    [Fact]
    public void Sanitize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HtmlSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, HtmlSanitizer.Sanitize(string.Empty));
    }

    [Fact]
    public void Sanitize_StripsImgTag_KeepsSurroundingText()
    {
        var result = HtmlSanitizer.Sanitize("<p>Voor<img src=\"x.png\" onerror=\"evil()\">Na</p>");

        Assert.DoesNotContain("img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<p>VoorNa</p>", result);
    }
}
