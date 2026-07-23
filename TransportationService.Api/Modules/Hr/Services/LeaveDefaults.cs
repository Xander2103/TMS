using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Services;

/// <summary>
/// The default balance types and leave types seeded per tenant (add-if-missing, never
/// overwriting tenant customisation). Codes are stable identities used for seeding + mapping.
/// </summary>
public static class LeaveDefaults
{
    public sealed record BalanceTypeSeed(string Code, string Name, int SortOrder);

    public sealed record LeaveTypeSeed(
        string Code, string Name, AbsenceType AbsenceType, bool DeductsFromBalance, string? BalanceCode,
        bool IsPaid, bool AllowsHalfDays, bool RequiresReason, bool RequiresAttachment, bool VisibleInSelfService,
        string Colour, int SortOrder);

    public const string StatutoryBalanceCode = "WETTELIJK";

    public static readonly IReadOnlyList<BalanceTypeSeed> BalanceTypes =
    [
        new("WETTELIJK", "Wettelijk verlof", 1),
        new("ADV", "ADV", 2),
        new("RECUP", "Recuperatie", 3),
        new("ANCIENNITEIT", "Anciënniteitsdagen", 4),
        new("COMPENSATIE", "Compensatie", 5),
    ];

    public static readonly IReadOnlyList<LeaveTypeSeed> LeaveTypes =
    [
        new("WETTELIJK", "Wettelijk verlof", AbsenceType.Vacation, true, "WETTELIJK", true, true, false, false, true, "#2563eb", 1),
        new("ADV", "ADV", AbsenceType.Vacation, true, "ADV", true, true, false, false, true, "#0891b2", 2),
        new("ANCIENNITEIT", "Anciënniteitsdagen", AbsenceType.Vacation, true, "ANCIENNITEIT", true, true, false, false, true, "#7c3aed", 3),
        new("RECUP", "Recuperatie", AbsenceType.PersonalLeave, true, "RECUP", true, true, false, false, true, "#16a34a", 4),
        new("COMPENSATIE", "Compensatie", AbsenceType.PersonalLeave, true, "COMPENSATIE", true, true, false, false, true, "#65a30d", 5),
        new("KLEIN_VERLET", "Klein verlet", AbsenceType.PersonalLeave, false, null, true, false, true, false, true, "#f59e0b", 6),
        new("ONBETAALD", "Onbetaald verlof", AbsenceType.Unpaid, false, null, false, false, true, false, true, "#a16207", 7),
        new("ZIEKTE", "Ziekte", AbsenceType.Sick, false, null, true, true, false, true, true, "#dc2626", 8),
        new("TIJDSKREDIET", "Tijdskrediet", AbsenceType.Unpaid, false, null, false, false, true, false, true, "#92400e", 9),
        new("OPLEIDING", "Opleiding", AbsenceType.Training, false, null, true, false, false, false, true, "#4f46e5", 10),
        new("ANDERE", "Andere", AbsenceType.Other, false, null, true, false, false, false, false, "#6b7280", 11),
    ];
}
