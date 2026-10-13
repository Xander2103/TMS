using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Immutable eventtimeline van een attendance-sessie. Events worden uitsluitend
/// toegevoegd, nooit gewijzigd of (soft-)verwijderd: dit ís de audittrail voor gewone
/// punches, dus die hoeven niet nogmaals in de algemene auditlog. Correcties muteren de
/// sessie/pauze maar laten een <see cref="AttendanceEventType.ManualCorrection"/>-event
/// plus een <see cref="AttendanceCorrection"/>-rij achter, zodat de originele waarde
/// altijd traceerbaar blijft.
/// </summary>
public class AttendanceEvent : ITenantOwned, IHasId, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid SessionId { get; set; }
    public Guid EmployeeId { get; set; }

    public AttendanceEventType EventType { get; set; }

    /// <summary>Effectief tijdstip van de gebeurtenis (UTC). Registratietijd staat in CreatedAt.</summary>
    public DateTime OccurredAt { get; set; }

    public AttendanceSource Source { get; set; }

    public Guid? KioskDeviceId { get; set; }
    public Guid? LocationId { get; set; }

    /// <summary>Koppeling naar de correctie die dit event veroorzaakte (alleen bij ManualCorrection).</summary>
    public Guid? CorrectionId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public enum AttendanceEventType
{
    ClockIn,
    BreakStarted,
    BreakEnded,
    ClockOut,
    ManualCorrection,
    AutoClosed,
    SessionCancelled,
    ManualSessionCreated,
}
