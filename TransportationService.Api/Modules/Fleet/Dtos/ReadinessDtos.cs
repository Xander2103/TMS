namespace TransportationService.Api.Modules.Fleet.Dtos;

/// <summary>Overall readiness of a fleet asset for a given day.</summary>
public enum ReadinessLevel
{
    Deployable,
    Warning,
    Blocked,
}

public enum ReadinessSeverity
{
    Blocking,
    Warning,
    Info,
}

/// <summary>
/// One reason in the fleet readiness verdict. <c>Code</c> is stable (e.g. "maintenance.planned",
/// "inspection.expired"); <c>Message</c> is the Dutch sentence shown to planners and fleet managers.
/// <c>Conditional</c> marks reasons that only apply to specific work (e.g. "adr.expired" only
/// matters for ADR orders) — Planning decides whether the condition is met.
/// </summary>
public record ReadinessIssueDto(
    string Code,
    string Message,
    ReadinessSeverity Severity,
    DateOnly? From = null,
    DateOnly? Until = null,
    string? Conditional = null);

/// <summary>
/// FL-H1/H2/H4: the ONE fleet-side readiness verdict (manual operational status + active flag +
/// legal documents/inspections + maintenance windows + damage), evaluated for a date. Planning
/// combines it with driver readiness, double booking, order requirements and capacity.
/// </summary>
public record VehicleReadinessDto(
    Guid AssetId,
    string AssetKind,
    string InternalNumber,
    string LicensePlate,
    DateOnly Date,
    ReadinessLevel Level,
    bool Deployable,
    IReadOnlyList<ReadinessIssueDto> Issues,
    string Summary);
