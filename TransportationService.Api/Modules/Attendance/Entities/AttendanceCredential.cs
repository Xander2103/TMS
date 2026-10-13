using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Identificatiemiddel voor de prikklok (kiosk). Bewust generiek gemodelleerd: v1 kent
/// alleen PIN-codes, maar badge/NFC/QR/hardwaretokens krijgen later een eigen
/// <see cref="AttendanceCredentialType"/> zonder de attendance-engine te herbouwen.
///
/// Beveiliging: het geheim wordt NOOIT plaintext opgeslagen. <see cref="SecretHash"/> is
/// een password-grade PBKDF2-hash (bestaande PasswordHasher); <see cref="LookupHash"/>
/// is een keyed HMAC-SHA256 (serverside pepper) die uitsluitend dient om de credential
/// in O(1) te vinden — zonder de pepper valt er niets offline te bruteforcen. De API
/// geeft een PIN nooit terug; een beheerder kan enkel resetten, nooit uitlezen.
/// </summary>
public class AttendanceCredential : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }

    public AttendanceCredentialType Type { get; set; } = AttendanceCredentialType.Pin;

    /// <summary>PBKDF2-hash van het geheim (verificatie).</summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Keyed HMAC-SHA256 van tenant+geheim (identificatie, uniek per tenant).</summary>
    public string LookupHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Brute-force-bescherming per credential (naast device-rate-limiting).
    public int FailedAttemptCount { get; set; }
    public DateTime? LockedUntil { get; set; }

    public DateTime? LastUsedAt { get; set; }
}

public enum AttendanceCredentialType
{
    Pin,
}
