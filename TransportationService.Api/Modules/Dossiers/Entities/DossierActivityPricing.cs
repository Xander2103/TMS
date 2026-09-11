using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// Provenance of a standalone activity's sales price. <see cref="None"/> = no price agreed
/// (the activity is unpriced); <see cref="OneOff"/> = an explicit agreed amount — any amount,
/// € 0 included. There is no engine behind standalone activities, so no "positive amount
/// without provenance" path exists (see <c>ActivityPricingState</c>).
/// </summary>
public enum ActivityPricingSource
{
    None,
    OneOff,
}

/// <summary>
/// Commercial record of ONE standalone, billable dossier activity (Opslag, Kraanwerk, …):
/// the activity-side counterpart of the order's one-off price agreement. Transport-shaped
/// activities never get one — their linked <c>TransportOrder</c> stays the price carrier.
/// Lives 1:1 with its activity (cascade), carries its own concurrency token (a price edit
/// must not be invalidated by an unrelated dossier mutation) and the shared pricing-status
/// vocabulary so the invoicing wave can mark it Invoiced/Locked without a schema change.
/// <see cref="AgreedPrice"/> equals <see cref="FixedAmount"/> today and is where a future
/// lines total lands (docs/ux-sprint/2026-09-11-activity-pricing-design.md §2.1, §7).
/// </summary>
public class DossierActivityPricing : AuditableTenantEntity, IVersionedEntity
{
    public Guid DossierActivityId { get; set; }

    public ActivityPricingSource PricingSource { get; set; } = ActivityPricingSource.None;

    /// <summary>The agreed amount; 0 is a deliberate price, null means none agreed.</summary>
    public decimal? FixedAmount { get; set; }

    /// <summary>Effective sales price of the activity (= FixedAmount until lines exist).</summary>
    public decimal? AgreedPrice { get; set; }

    /// <summary>Draft by default; Locked/Invoiced refuse further price changes (set by invoicing later).</summary>
    public OrderPricingStatus Status { get; set; } = OrderPricingStatus.Draft;

    public string? Notes { get; set; }

    /// <summary>Optimistic-concurrency token, echoed by the client; a mismatch yields 409 with the current dossier.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
