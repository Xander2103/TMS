using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// Tenant-managed operational activity kind (distribution, crane work, storage, ... — or a
/// completely different set for another transport company). Behaviour is driven ONLY by the
/// capability flags below; domain logic never matches on <see cref="Code"/> outside the
/// per-tenant seeder, so tenants reshape their activity model without source changes.
/// </summary>
public class ActivityType : AuditableTenantEntity
{
    /// <summary>Stable identifier within the tenant (e.g. "KRAANWERK"); immutable after creation.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Key into the curated frontend icon map; unknown/null renders the default icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Free grouping label for KPI reporting (revenue/count per activity family).</summary>
    public string? KpiCategory { get; set; }

    /// <summary>True → the activity is executed by a linked TransportOrder (stops, cargo, planning, scanning, POD).</summary>
    public bool HasStops { get; set; }

    /// <summary>True → the dossier page shows the goods section when this activity is present.</summary>
    public bool SupportsGoods { get; set; }

    public bool PlanningRelevant { get; set; }

    public bool WarehouseRelevant { get; set; }

    /// <summary>True → DossierActivity.DurationHours is shown/editable (crane hours etc.).</summary>
    public bool AllowsDuration { get; set; }

    /// <summary>True → offered as a quick-start tile on the New Dossier screen.</summary>
    public bool IsQuickStart { get; set; }

    public int QuickStartOrder { get; set; }

    /// <summary>
    /// Exactly one active type per tenant carries this flag; used when an order is created
    /// without a dossier (EDI, portal, legacy API) to type the auto-created wrapper activity.
    /// </summary>
    public bool IsSystemDefaultTransport { get; set; }
}
