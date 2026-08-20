using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Attendance.Entities;

/// <summary>
/// Auditbare manuele correctie op een attendance-sessie of -pauze. De originele waarde
/// blijft altijd bewaard (OldValue), de reden is verplicht en de corrector volgt uit
/// CreatedByUserId (interceptor). History wordt nooit overschreven alsof het originele
/// event niet bestond: naast deze rij wordt ook een ManualCorrection-event aan de
/// sessietimeline toegevoegd.
/// </summary>
public class AttendanceCorrection : AuditableTenantEntity
{
    public Guid SessionId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Gecorrigeerde pauze (alleen bij BreakStart/BreakEnd).</summary>
    public Guid? BreakId { get; set; }

    public AttendanceCorrectionKind Kind { get; set; }

    public DateTime? OldValue { get; set; }
    public DateTime? NewValue { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public enum AttendanceCorrectionKind
{
    ClockIn,
    ClockOut,
    BreakStart,
    BreakEnd,
    SessionCancelled,
    ManualSession,
}
