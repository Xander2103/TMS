using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Portal.Entities;

public enum ResourceLinkKind
{
    Favorite,
    Recent,
    Pinned,
}

/// <summary>
/// A user's personal link to a resource: favorite, recently-viewed or pinned. Stores only the
/// relationship plus a small display cache (label/route, refreshed on every touch) — never a
/// copy of entity data. Listing rechecks the view permission per entity type and drops rows
/// whose target no longer exists.
/// </summary>
public class UserResourceLink : AuditableTenantEntity
{
    public Guid UserId { get; set; }

    public ResourceLinkKind Kind { get; set; }

    /// <summary>Closed catalog, validated by the service (Customer, TransportOrder, Trip, ...).</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>Display cache only (e.g. order number + customer); refreshed when touched.</summary>
    public string Label { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    /// <summary>SPA route to open the resource.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>Manual ordering for favorites/pins; recents order by <see cref="TouchedAt"/>.</summary>
    public int SortOrder { get; set; }

    public DateTime TouchedAt { get; set; }
}
