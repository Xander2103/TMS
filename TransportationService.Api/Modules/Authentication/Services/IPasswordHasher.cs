namespace TransportationService.Api.Modules.Authentication.Services;

public enum PasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

/// <summary>Abstraction over ASP.NET Core password hashing (PBKDF2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerificationResult Verify(string? hash, string providedPassword);
}
