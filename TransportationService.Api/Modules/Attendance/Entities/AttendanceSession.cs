using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Eén volledige werkperiode van een medewerker: van inpunten tot uitpunten, eventueel
/// over middernacht heen (een sessie wordt nooit om 00:00 geknipt; rapportering splitst
/// per kalenderdag in de tenant-tijdzone). Alle tijdstippen zijn UTC. De sessie draagt
/// een status die transactioneel consistent wordt gehouden met de open pauze; de harde
/// invariant "maximaal één actieve sessie per medewerker" wordt door een gefilterde
/// unieke index in de database afgedwongen, niet enkel door applicatiecode.
/// Attendance registreert werkaanwezigheid — rijtijden/tachograafdata horen hier
/// uitdrukkelijk NIET thuis (aparte toekomstige DriverActivity-module).
/// </summary>
public class AttendanceSession : AuditableTenantEntity, IVersionedEntity
{
    public Guid EmployeeId { get; set; }

    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }

    public AttendanceSessionStatus Status { get; set; } = AttendanceSessionStatus.Working;

    public AttendanceSource ClockInSource { get; set; }
    public AttendanceSource? ClockOutSource { get; set; }

    /// <summary>Prikklok waarop ingepunt werd (indien via kiosk).</summary>
    public Guid? KioskDeviceId { get; set; }

    /// <summary>Bronlocatie van de punch (via de locatie van de prikklok).</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Denormalisatie voor de UI-indicator; de volledige historie staat in AttendanceCorrection.</summary>
    public bool HasCorrections { get; set; }

    /// <summary>One-shot-stempel van de sweep zodat "vergeten uitpunten" maar één keer meldt.</summary>
    public DateTime? ForgottenClockOutNotifiedAt { get; set; }

    public Guid Version { get; set; }
}

public enum AttendanceSessionStatus
{
    /// <summary>Ingepunt en aan het werk.</summary>
    Working,

    /// <summary>Ingepunt met een lopende pauze.</summary>
    OnBreak,

    /// <summary>Normaal uitgepunt.</summary>
    Completed,

    /// <summary>Automatisch afgesloten door het systeem (configureerbaar); altijd met audittrail.</summary>
    AutoClosed,

    /// <summary>Door HR geannuleerd (bv. foutieve registratie); telt niet mee in rapportering.</summary>
    Cancelled,
}

/// <summary>
/// Kanaal waarlangs een punch binnenkwam. Extensible: toekomstige bronnen (NFC-lezer,
/// import van een externe prikklok, tachograaf-afgeleide events) krijgen hier een eigen
/// waarde zodat externe aanvoer nooit de employee-selfpunch-securityflow hoeft te delen.
/// </summary>
public enum AttendanceSource
{
    Web,
    Kiosk,
    Mobile,
    Api,
    Import,
}
