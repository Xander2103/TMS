using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// Follow-up wave P2: one tenant-configurable document-decision rule. Rules are evaluated in
/// Priority order (lowest first); the first rule whose non-null conditions ALL match the order
/// decides the document kind. When no rule matches, the built-in reference defaults apply
/// (ADR → CMR, cross-border → CMR, otherwise delivery note) — see DocumentStrategyResolver.
/// Conditions are nullable so a rule can match on any subset (null = don't care).
/// </summary>
public class TenantDocumentRule : AuditableTenantEntity
{
    public int Priority { get; set; }

    /// <summary>Match orders whose stops span more than one country (null = don't care).</summary>
    public bool? MatchCrossBorder { get; set; }

    /// <summary>Match orders flagged ADR (null = don't care).</summary>
    public bool? MatchAdr { get; set; }

    /// <summary>Match orders linked to a dossier activity of this type (null = don't care).</summary>
    public Guid? MatchActivityTypeId { get; set; }

    /// <summary>Resulting document kind: DeliveryNote | Cmr | None.</summary>
    public string DocumentKind { get; set; } = "DeliveryNote";
}
