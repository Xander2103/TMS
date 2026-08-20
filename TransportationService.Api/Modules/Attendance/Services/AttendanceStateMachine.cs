using TransportationService.Api.Modules.Attendance.Entities;

namespace TransportationService.Api.Modules.Attendance.Services;

/// <summary>
/// Server-side state machine voor attendance-punches. De frontend mag knoppen
/// verbergen, maar de backend blijft authoritative: elke transitie loopt hier langs.
///
///   NotClockedIn → (ClockIn) → Working → (StartBreak) → OnBreak → (EndBreak) → Working
///   Working|OnBreak → (ClockOut) → Completed
///
/// Expliciet ongeldig: twee keer inpunten, uitpunten zonder actieve sessie, pauze
/// starten zonder ingepunt te zijn, pauze stoppen zonder actieve pauze, tweede actieve
/// sessie, tweede actieve pauze. Uitpunten tijdens een pauze is WEL geldig en sluit de
/// pauze op hetzelfde tijdstip. Completed/AutoClosed/Cancelled zijn eindtoestanden.
/// </summary>
public static class AttendanceStateMachine
{
    public static bool CanClockIn(AttendanceSession? activeSession) => activeSession is null;

    public static bool CanClockOut(AttendanceSession? activeSession) =>
        activeSession is { Status: AttendanceSessionStatus.Working or AttendanceSessionStatus.OnBreak };

    public static bool CanStartBreak(AttendanceSession? activeSession) =>
        activeSession is { Status: AttendanceSessionStatus.Working };

    public static bool CanEndBreak(AttendanceSession? activeSession) =>
        activeSession is { Status: AttendanceSessionStatus.OnBreak };

    /// <summary>Een sessie is actief zolang er niet is uitgepunt.</summary>
    public static bool IsActive(AttendanceSession session) =>
        session.ClockOutAt is null &&
        session.Status is AttendanceSessionStatus.Working or AttendanceSessionStatus.OnBreak;
}
