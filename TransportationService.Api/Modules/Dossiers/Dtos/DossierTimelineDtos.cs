namespace TransportationService.Api.Modules.Dossiers.Dtos;

/// <summary>
/// T-H6: one event of the dossier's business story (WHEN / WHO / WHAT).
/// <paramref name="Category"/> drives the filter chips: <c>dossier</c>, <c>orders</c>, <c>planning</c>
/// (trips + execution), <c>pricing</c>, <c>documents</c> (incl. POD) and <c>incidents</c> (incl. afwijkingen).
/// <paramref name="Type"/> is a stable machine code (e.g. <c>trip.planned</c>) for tests and styling;
/// <paramref name="Title"/> is the Dutch sentence the user reads.
/// <paramref name="ActorKind"/>: <c>User</c> (a person acted, <paramref name="ActorName"/> set),
/// <c>System</c> (an automatic event — never the human whose action happened to trigger it) or
/// <c>Unknown</c> (a person acted but can no longer be resolved).
/// </summary>
public record DossierTimelineEventDto(
    string Id,
    DateTime Timestamp,
    string Category,
    string Type,
    string Title,
    string? Detail,
    string ActorKind,
    string? ActorName,
    IReadOnlyList<DossierTimelineFieldDto> Fields);

/// <summary>
/// Expandable detail with Dutch labels. <paramref name="Before"/> is set only for a real change
/// (old → new); a plain fact carries just <paramref name="Value"/>.
/// </summary>
public record DossierTimelineFieldDto(string Label, string? Value, string? Before = null);
