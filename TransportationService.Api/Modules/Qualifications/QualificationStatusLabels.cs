using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Qualifications;

/// <summary>Dutch wording for a qualification status inside a sentence ("Code 95 is verlopen") (B32).
/// Same vocabulary as the employee screens: in behandeling / geldig / verloopt binnenkort / verlopen /
/// afgewezen / ingetrokken (HR acceptance P2-11).</summary>
public static class QualificationStatusLabels
{
    public static string Dutch(QualificationStatus status) => status switch
    {
        QualificationStatus.Pending => "in behandeling",
        QualificationStatus.Valid => "geldig",
        QualificationStatus.ExpiringSoon => "verloopt binnenkort",
        QualificationStatus.Expired => "verlopen",
        QualificationStatus.Rejected => "afgewezen",
        QualificationStatus.Suspended => "ingetrokken",
        _ => status.ToString().ToLowerInvariant(),
    };

    public static string Dutch(string status) =>
        Enum.TryParse<QualificationStatus>(status, ignoreCase: true, out var parsed) ? Dutch(parsed) : status.ToLowerInvariant();
}
