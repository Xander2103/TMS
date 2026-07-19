using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.TripCosting.Entities;

/// <summary>
/// Per-trip costing rollup (the KPI read model). Recomputed by TripCostingService on every
/// costing change. Finalizing freezes FinalCost/FinalRevenue; afterwards every costing
/// mutation is refused until an explicit reopen (trip_costs.override).
/// </summary>
public class TripCostSummary : AuditableTenantEntity
{
    public Guid TripId { get; set; }

    public decimal EstimatedTotal { get; set; }
    public decimal ActualTotal { get; set; }

    /// <summary>Per-cost-type merge: actual where any actual line of that type exists, else estimated.</summary>
    public decimal ProjectedTotal { get; set; }

    /// <summary>Σ AgreedPrice of the trip's non-cancelled orders (live until finalized).</summary>
    public decimal Revenue { get; set; }

    public bool IsFinalized { get; set; }
    public decimal? FinalCost { get; set; }
    public decimal? FinalRevenue { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public Guid? FinalizedByUserId { get; set; }
}
