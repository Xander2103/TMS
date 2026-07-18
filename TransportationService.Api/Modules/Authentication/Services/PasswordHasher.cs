using AspNetIdentity = Microsoft.AspNetCore.Identity;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Authentication.Services;

/// <summary>
/// Wraps ASP.NET Core's <c>PasswordHasher&lt;TUser&gt;</c> (PBKDF2-HMAC-SHA256, per-password
/// salt, iteration count managed by the framework). Never stores plain text.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly AspNetIdentity.PasswordHasher<User> _inner = new();
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
