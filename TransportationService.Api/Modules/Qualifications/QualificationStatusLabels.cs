using TransportationService.Api.Modules.Qualifications.Entities;

namespace TransportationService.Api.Modules.Qualifications;

/// <summary>Dutch wording for a qualification status inside a sentence ("Code 95 is verlopen") (B32).</summary>
public static class QualificationStatusLabels
{
    public static string Dutch(QualificationStatus status) => status switch
    {
        QualificationStatus.Pending => "in behandeling",
        QualificationStatus.Valid => "geldig",
        QualificationStatus.ExpiringSoon => "verloopt binnenkort",
        QualificationStatus.Expired => "verlopen",
        QualificationStatus.Rejected => "afgekeurd",
        QualificationStatus.Suspended => "geschorst",
        _ => status.ToString().ToLowerInvariant(),
    };

    public static string Dutch(string status) =>
        Enum.TryParse<QualificationStatus>(status, ignoreCase: true, out var parsed) ? Dutch(parsed) : status.ToLowerInvariant();
}
