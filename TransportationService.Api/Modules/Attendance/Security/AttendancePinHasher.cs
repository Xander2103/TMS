using System.Security.Cryptography;
using System.Text;
using TransportationService.Api.Modules.Authentication.Services;

namespace TransportationService.Api.Modules.Attendance.Security;

public interface IAttendancePinHasher
{
    /// <summary>Zonder geldige pepper is elke kioskfunctie fail-closed uitgeschakeld.</summary>
    bool IsConfigured { get; }

    string HashPin(string pin);
    bool VerifyPin(string? secretHash, string pin);

    /// <summary>Keyed HMAC-SHA256 over tenant + PIN, uitsluitend voor O(1)-identificatie.</summary>
    string ComputeLookupHash(Guid tenantId, string pin);
}

/// <summary>
/// PIN-opslag in twee lagen (zie docs/attendance/security.md):
///   1. SecretHash — password-grade PBKDF2 via de bestaande <see cref="IPasswordHasher"/>
///      (geen SHA256(PIN), geen plaintext, geen omkeerbare encryptie);
///   2. LookupHash — HMAC-SHA256 met een serverside pepper (config "Attendance:PinPepper",
///      base64, ≥ 32 bytes) zodat de kiosk een code in O(1) aan een medewerker kan
///      koppelen zonder alle PBKDF2-hashes af te lopen. Zonder de pepper valt er uit een
///      gelekte databank niets over PIN's af te leiden; de échte bescherming van de
///      kleine PIN-ruimte komt van device-auth + rate limiting + credential-lockout.
/// De pepper wordt bewust niet geroteerd via een ring: bij rotatie worden credentials
/// opnieuw gezet (PIN-reset), wat operationeel triviaal is.
/// </summary>
public sealed class AttendancePinHasher : IAttendancePinHasher
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly byte[]? _pepper;

    public AttendancePinHasher(IPasswordHasher passwordHasher, IConfiguration configuration)
    {
        _passwordHasher = passwordHasher;
        _pepper = DecodePepper(configuration["Attendance:PinPepper"]);
    }

    public bool IsConfigured => _pepper is not null;

    public string HashPin(string pin) => _passwordHasher.Hash(pin);

    public bool VerifyPin(string? secretHash, string pin) =>
        _passwordHasher.Verify(secretHash, pin) is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    public string ComputeLookupHash(Guid tenantId, string pin)
    {
        if (_pepper is null)
        {
            throw new InvalidOperationException("Attendance:PinPepper is niet geconfigureerd.");
        }

        var payload = Encoding.UTF8.GetBytes($"{tenantId:N}:{pin}");
        return Convert.ToBase64String(HMACSHA256.HashData(_pepper, payload));
    }

    internal static byte[]? DecodePepper(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(configured);
            return bytes.Length >= 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
