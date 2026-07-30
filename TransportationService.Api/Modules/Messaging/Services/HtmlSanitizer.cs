using System.Net;
using System.Text.RegularExpressions;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>
/// Minimal, dependency-free allowlist HTML sanitizer for <c>MessageTemplate.BodyHtml</c>.
/// Keeps only p, br, strong, em, ul, ol, li, a (href http/https only), h1-h3; every other tag is
/// stripped (script/style are removed together with their content — anything else keeps its
/// inner text but loses its tags), and every attribute except a valid a[href] is dropped.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "em", "ul", "ol", "li", "a", "h1", "h2", "h3",
    };

    private static readonly HashSet<string> RemoveWithContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style",
    };

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStylePattern();

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:\s+[^<>]*)?)\s*/?>", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();

    [GeneratedRegex(@"^https?://", RegexOptions.IgnoreCase)]
    private static partial Regex HttpSchemePattern();

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        // Remove <script>/<style> together with their content first — their inner text is never
        // meant to be visible and must not leak into the sanitized output.
        var withoutScripts = ScriptOrStylePattern().Replace(html, string.Empty);

        return TagPattern().Replace(withoutScripts, match =>
        {
            var isClosing = match.Groups[1].Value == "/";
            var tagName = match.Groups[2].Value.ToLowerInvariant();

            if (RemoveWithContentTags.Contains(tagName))
            {
                // A lone/unbalanced script tag that survived the pass above (e.g. malformed
                // markup): still strip the tag itself.
                return string.Empty;
            }

            if (!AllowedTags.Contains(tagName))
            {
                return string.Empty;
            }

            if (isClosing)
            {
                return $"</{tagName}>";
            }

            if (tagName == "a")
            {
                var attrs = match.Groups[3].Value;
                var hrefMatch = HrefPattern().Match(attrs);
                var href = hrefMatch.Success
                    ? hrefMatch.Groups[1].Success ? hrefMatch.Groups[1].Value
                        : hrefMatch.Groups[2].Success ? hrefMatch.Groups[2].Value
                        : hrefMatch.Groups[3].Value
                    : null;

                return href is not null && HttpSchemePattern().IsMatch(href)
                    ? $"<a href=\"{WebUtility.HtmlEncode(href)}\">"
                    : "<a>";
            }

            return tagName == "br" ? "<br>" : $"<{tagName}>";
        });
    }
}
