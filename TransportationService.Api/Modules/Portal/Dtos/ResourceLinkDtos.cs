using TransportationService.Api.Modules.Portal.Entities;

namespace TransportationService.Api.Modules.Portal.Dtos;

public record ResourceLinkDto(
    Guid Id,
    ResourceLinkKind Kind,
    string EntityType,
    Guid EntityId,
    string Label,
    string? Subtitle,
    string Route,
    int SortOrder,
    DateTime TouchedAt);

/// <summary>Upsert: creates the link or refreshes its display cache and touch time.</summary>
public record TouchResourceLinkRequest(
    ResourceLinkKind Kind,
    string EntityType,
    Guid EntityId,
    string Label,
    string? Subtitle,
    string Route);

public record ReorderResourceLinksRequest(IReadOnlyList<Guid> Ids);
