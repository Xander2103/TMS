namespace TransportationService.Api.Common;

/// <summary>
/// Machine-readable foutcodes (i18n-wave, §24/§25). Contract:
///   • een code is een STABIELE identifier (`domein.snake_case`) waarop de frontend
///     vertaalt (t($"errors.{code}")) en desnoods logica bouwt — de Nederlandse
///     `message`/`detail` blijft als fallback meereizen en is NOOIT een contract;
///   • codes worden alleen toegevoegd, nooit hernoemd of hergebruikt;
///   • nieuwe user-facing fouten die de frontend moet kunnen onderscheiden of vertalen
///     krijgen hier een constante (zie docs/localization/developer-guide.md).
/// Bestaande responsvormen blijven ongewijzigd: `code` is overal een EXTRA veld naast
/// de bestaande body (anonieme `{ message }`-objecten en ProblemDetails-extensions).
/// </summary>
public static class ErrorCodes
{
    // Algemeen
    public const string ValidationFailed = "common.validation_failed";
    public const string Unauthenticated = "common.unauthenticated";
    public const string Forbidden = "common.forbidden";
    public const string NotFound = "common.not_found";
    public const string RateLimited = "common.rate_limited";
    public const string UnsupportedLanguage = "common.unsupported_language";
    public const string InvalidReference = "common.invalid_reference";

    // Attendance (spiegel van AttendancePunchOutcome / AttendanceCorrectionOutcome)
    public const string AttendanceAlreadyClockedIn = "attendance.already_clocked_in";
    public const string AttendanceNotClockedIn = "attendance.not_clocked_in";
    public const string AttendanceBreakAlreadyActive = "attendance.break_already_active";
    public const string AttendanceNoActiveBreak = "attendance.no_active_break";
    public const string AttendanceEmployeeInactive = "attendance.employee_inactive";
    public const string AttendanceEmployeeNotFound = "attendance.employee_not_found";
    public const string AttendanceSelfPunchDisabled = "attendance.self_punch_disabled";
    public const string AttendanceKioskDisabled = "attendance.kiosk_disabled";
    public const string AttendanceStaleVersion = "attendance.stale_version";
    public const string AttendanceSessionOverlap = "attendance.session_overlap";

    // Ritexecutie (vervangt de vroegere fouttekst-sniffing in de chauffeurs-UI)
    public const string TripReasonRequired = "trips.reason_required";
    public const string TripPackagesUnresolved = "trips.packages_unresolved";
}
