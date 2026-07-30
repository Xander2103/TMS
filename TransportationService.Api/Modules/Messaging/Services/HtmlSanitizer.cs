using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Allowlist HTML sanitizer for <c>MessageTemplate.BodyHtml</c>, built on a real HTML parser
/// (AngleSharp) instead of regexes (M5): the browser's own parsing rules decide what a tag is,
/// so malformed markup, weird casing, slash-separated attributes or half-closed elements cannot
/// smuggle anything past a pattern. Output is REBUILT from the parsed tree — only p, br, strong,
/// em, ul, ol, li, a (href http/https only), h1-h3 survive; script/style/svg and friends are
/// dropped with their content; every other tag is stripped but keeps its inner text; all text is
/// HTML-encoded on the way out.
/// </summary>
public static class HtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "em", "ul", "ol", "li", "a", "h1", "h2", "h3",
    };

    /// <summary>Content of these is never user-visible text — remove the subtree entirely.</summary>
    private static readonly HashSet<string> RemoveWithContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "svg", "math", "template", "iframe", "object", "embed", "noscript",
    };

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument("<html><body></body></html>");
        var nodes = parser.ParseFragment(html, document.Body!);

        var builder = new StringBuilder(html.Length);
        foreach (var node in nodes)
        {
            AppendSanitized(node, builder);
        }

        return builder.ToString();
    }

    private static void AppendSanitized(INode node, StringBuilder builder)
    {
        switch (node)
        {
            case IText text:
                builder.Append(WebUtility.HtmlEncode(text.Data));
                return;

            case IElement element:
                var tagName = element.LocalName.ToLowerInvariant();
                if (RemoveWithContentTags.Contains(tagName))
                {
                    return;
                }

                if (!AllowedTags.Contains(tagName))
                {
                    // Unknown/disallowed tag: the wrapper goes, the readable content stays.
                    AppendChildren(element, builder);
                    return;
                }

                if (tagName == "br")
                {
                    builder.Append("<br>");
                    return;
                }

                if (tagName == "a")
                {
                    var href = element.GetAttribute("href");
                    builder.Append(IsSafeHref(href)
                        ? $"<a href=\"{WebUtility.HtmlEncode(href)}\">"
                        : "<a>");
                    AppendChildren(element, builder);
                    builder.Append("</a>");
                    return;
                }

                builder.Append('<').Append(tagName).Append('>');
                AppendChildren(element, builder);
                builder.Append("</").Append(tagName).Append('>');
                return;

            default:
                // Comments, processing instructions, doctypes: never part of the output.
                return;
        }
    }

    private static void AppendChildren(IElement element, StringBuilder builder)
    {
        foreach (var child in element.ChildNodes)
        {
            AppendSanitized(child, builder);
        }
    }

    /// <summary>Absolute http/https only — javascript:, data:, vbscript:, protocol-relative and
    /// scheme-obfuscating whitespace/control characters all fail the Uri parse or scheme check.</summary>
    private static bool IsSafeHref(string? href) =>
        !string.IsNullOrWhiteSpace(href)
        && Uri.TryCreate(href, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
