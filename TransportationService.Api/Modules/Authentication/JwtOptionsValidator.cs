using System.Text;
using Microsoft.Extensions.Options;

namespace TransportationService.Api.Modules.Authentication;

/// <summary>
/// Startup validation for the JWT signing key, enforced in EVERY environment via
/// <c>ValidateOnStart</c>. Beyond the data-annotation checks (Required, MinLength 32) this rejects
/// the development signing key that was committed to source control (now considered compromised)
/// and obvious placeholders, so a burned or unset key can never boot any host.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    /// <summary>Keys that must never be accepted — the committed dev key is permanently burned.</summary>
    public static readonly IReadOnlyList<string> ForbiddenKeys =
    [
        "dev-only-signing-key-change-me-32bytes-minimum!!",
    ];

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey is not configured. Supply it via user-secrets (Development) or an "
                + "environment variable / secret store (other environments).");
        }

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            return ValidateOptionsResult.Fail("Jwt:SigningKey must be at least 32 bytes.");
        }

        if (ForbiddenKeys.Contains(options.SigningKey, StringComparer.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey is a known/compromised key that was previously committed to source control. "
                + "Rotate it and supply a fresh random secret.");
        }

        if (LooksLikePlaceholder(options.SigningKey))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey looks like a placeholder. Supply a real, high-entropy random secret.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool LooksLikePlaceholder(string key)
    {
        var lower = key.ToLowerInvariant();
        // Common placeholder markers, or a degenerate key made of one/two repeated characters.
        return lower.Contains("change-me")
            || lower.Contains("changeme")
            || lower.Contains("placeholder")
            || lower.Contains("your-secret")
            || key.Distinct().Count() <= 2;
    }
}
