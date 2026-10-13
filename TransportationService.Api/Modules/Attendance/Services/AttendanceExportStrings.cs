namespace TransportationService.Api.Modules.Attendance.Services;

/// <summary>
/// Taalcatalogus voor de urenregistratie-export (InvoicePdfStrings-patroon): een taal
/// toevoegen = hier één record toevoegen — de exportcode bevat nooit labelteksten.
/// Alleen de KOPTEKSTEN zijn taalafhankelijk; kolomvolgorde en celwaarden (het
/// machineleesbare deel) zijn identiek in elke taal (§66).
/// </summary>
public sealed record AttendanceExportStrings(
    string SheetName,
    string Employee,
    string EmployeeNumber,
    string Department,
    string Date,
    string GrossMinutes,
    string BreakMinutes,
    string NetMinutes,
    string PlannedMinutes,
    string DeviationMinutes,
    string MissingClockOut,
    string Corrections,
    string Total,
    string Yes,
    string CriteriaReport,
    string CriteriaFrom,
    string CriteriaTo,
    string CriteriaEmployee,
    string CriteriaDepartment,
    string CriteriaGeneratedAt,
    string CriteriaByUser,
    string CriteriaAll)
{
    public static readonly AttendanceExportStrings Nl = new(
        "Urenregistratie", "Medewerker", "Personeelsnummer", "Afdeling", "Datum",
        "Bruto (min)", "Pauze (min)", "Netto (min)", "Gepland (min)", "Afwijking (min)",
        "Uitpunt ontbreekt", "Correcties", "Totaal", "Ja",
        "Rapport", "Periode van", "Periode tot", "Medewerker", "Afdeling",
        "Gegenereerd op", "Door gebruiker", "alle");

    public static readonly AttendanceExportStrings Fr = new(
        "Enregistrement des heures", "Collaborateur", "Numéro de personnel", "Département", "Date",
        "Brut (min)", "Pause (min)", "Net (min)", "Prévu (min)", "Écart (min)",
        "Sortie manquante", "Corrections", "Total", "Oui",
        "Rapport", "Période du", "Période au", "Collaborateur", "Département",
        "Généré le", "Par l'utilisateur", "tous");

    public static readonly AttendanceExportStrings En = new(
        "Time registration", "Employee", "Employee number", "Department", "Date",
        "Gross (min)", "Break (min)", "Net (min)", "Planned (min)", "Deviation (min)",
        "Missing clock-out", "Corrections", "Total", "Yes",
        "Report", "Period from", "Period to", "Employee", "Department",
        "Generated at", "By user", "all");

    public static AttendanceExportStrings For(string? languageCode) =>
        Common.SupportedLanguages.Normalize(languageCode) switch
        {
            Common.SupportedLanguages.Fr => Fr,
            Common.SupportedLanguages.En => En,
            _ => Nl,
        };
}
