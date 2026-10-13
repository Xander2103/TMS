using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Per-tenant urenregistratie-instellingen (één rij per tenant, lui aangemaakt met
/// defaults — HrReminderSettings-patroon). Originele punchtijden blijven ALTIJD bewaard;
/// er is bewust geen afrondingslogica in v1 (payroll-afronding is een aparte, latere
/// businesslaag die de brondata onaangeroerd laat).
/// </summary>
public class AttendanceSettings : AuditableTenantEntity
{
    /// <summary>Medewerkers mogen zelf punchen via dashboard/portaal.</summary>
    public bool SelfPunchEnabled { get; set; } = true;

    /// <summary>Prikklokken (kiosk) actief voor deze tenant.</summary>
    public bool KioskEnabled { get; set; } = true;

    /// <summary>Lengte van nieuw gegenereerde/ingestelde PIN-codes (4–8 cijfers).</summary>
    public int PinLength { get; set; } = 4;

    /// <summary>Actieve sessie langer dan dit aantal uren ⇒ waarschuwing "mogelijk vergeten uit te punten".</summary>
    public int ForgottenClockOutAfterHours { get; set; } = 16;

    /// <summary>Automatisch afsluiten van extreem lange sessies. Default UIT: v1 waarschuwt alleen.</summary>
    public bool AutoCloseEnabled { get; set; }

    /// <summary>Pas na dit aantal uren wordt (indien ingeschakeld) automatisch afgesloten, met audittrail.</summary>
    public int AutoCloseAfterHours { get; set; } = 18;

    /// <summary>Marge (minuten) na geplande start voordat "gepland maar niet ingepunt" als anomalie telt.</summary>
    public int PlannedNotClockedInGraceMinutes { get; set; } = 30;
}
