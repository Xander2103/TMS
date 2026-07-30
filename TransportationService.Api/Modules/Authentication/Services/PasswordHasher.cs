using AspNetIdentity = Microsoft.AspNetCore.Identity;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Authentication.Services;

/// <summary>
/// Wraps ASP.NET Core's <c>PasswordHasher&lt;TUser&gt;</c> (PBKDF2-HMAC-SHA512, per-password salt).
/// The iteration count is set EXPLICITLY here rather than inheriting the framework default, so the
/// work factor is a reviewed, versioned decision. Raising it stays safe: existing hashes keep
/// verifying and are transparently upgraded on the next successful sign-in (rehash-on-login).
/// Never stores plain text.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>OWASP-aligned PBKDF2-HMAC-SHA512 work factor (2024+ guidance ≥ 210 000).</summary>
    public const int IterationCount = 210_000;

    private readonly AspNetIdentity.PasswordHasher<User> _inner =
        new(Microsoft.Extensions.Options.Options.Create(new AspNetIdentity.PasswordHasherOptions
        {
            CompatibilityMode = AspNetIdentity.PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = IterationCount,
        }));

    private static readonly User Placeholder = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _inner.HashPassword(Placeholder, password);
    }

    public PasswordVerificationResult Verify(string? hash, string providedPassword)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        return _inner.VerifyHashedPassword(Placeholder, hash, providedPassword) switch
        {
            AspNetIdentity.PasswordVerificationResult.Success => PasswordVerificationResult.Success,
            AspNetIdentity.PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationResult.SuccessRehashNeeded,
            _ => PasswordVerificationResult.Failed,
        };
    }
}
