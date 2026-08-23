using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Geregistreerde prikklok (tablet/touch-pc aan de muur). Een kiosk is GEEN gewone
/// gebruikerssessie: het device authenticeert zich met een eigen 256-bit secret
/// (éénmalig getoond bij provisioning, hier enkel als SHA-256-hash opgeslagen) en mag
/// uitsluitend de kiosk-punch-endpoints aanroepen — nooit ERP-data. Het secret is
/// roteerbaar en het device is per direct uitschakelbaar. Eén tenant kan meerdere
/// prikklokken hebben, elk optioneel gekoppeld aan een bestaande Location die als
/// bronlocatie op punches wordt gestempeld.
/// </summary>
public class KioskDevice : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;

    public Guid? LocationId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Standaardtaal van het prikklokscherm (nl/fr/en, productcatalogus — geen vrije
    /// string). Het beginscherm toont deze taal; na identificatie mag de interactie
    /// tijdelijk naar de persoonlijke taal van de medewerker schakelen en na de reset
    /// keert de kiosk hiernaar terug.
    /// </summary>
    public string DefaultLanguage { get; set; } = "nl";

    /// <summary>SHA-256-hash van het device-secret (high-entropy random token).</summary>
    public string SecretHash { get; set; } = string.Empty;

    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastPunchAt { get; set; }

    // Brute-force-bescherming op deviceniveau: bij PIN-only-identificatie is een foute
    // code niet aan één credential toe te schrijven, dus de gok-teller hoort bij de
    // prikklok zelf (naast de per-IP rate limiting en de per-credential lockout).
    public int FailedAttemptCount { get; set; }
    public DateTime? LockedUntil { get; set; }
}
