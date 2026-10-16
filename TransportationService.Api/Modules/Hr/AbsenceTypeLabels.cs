using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr;

/// <summary>Dutch absence-type name for server-side sentences ("… afwezig op 22/10/2026 (Verlof)");
/// the raw enum name ("Vacation") must never reach a user (HR acceptance P2-10).</summary>
public static class AbsenceTypeLabels
{
    public static string Dutch(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "Verlof",
        AbsenceType.Sick => "Ziekte",
        AbsenceType.Training => "Opleiding",
        AbsenceType.PersonalLeave => "Persoonlijk verlof",
        AbsenceType.Unpaid => "Onbetaald verlof",
        _ => "Afwezigheid",
    };
}
