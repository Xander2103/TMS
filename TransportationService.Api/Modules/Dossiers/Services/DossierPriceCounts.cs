using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// M11 (2026-10-16): the ONE server-side computation of a dossier's commercial completeness —
/// used by the dossier list column, the list's <c>priceStatus</c> filter, the detail financials
/// ("x van y onderdelen afgehandeld") and the confirmation dialog, so they can never disagree.
/// <para>
/// A <b>relevant component</b> is a billable unit that is not cancelled:
/// <list type="bullet">
///   <item>a transport activity (type HasStops + IsBillable) whose linked order is not Cancelled
///   (no order yet = relevant and unpriced);</item>
///   <item>a standalone activity (no stops, IsBillable) whose <see cref="DossierActivity.ExecutionStatus"/>
///   is not Cancelled;</item>
///   <item>a legacy linked order no activity represents, unless Cancelled.</item>
/// </list>
/// Non-billable types and cancelled work are excluded. Each relevant component falls in exactly
/// one bucket — the per-activity <see cref="ActivityPriceStatus"/> vocabulary: <c>Priced</c>,
/// <c>Free</c> (confirmed standalone € 0 / intentional order € 0), <c>ZeroUnconfirmed</c>,
/// <c>PartiallyPriced</c> (order priced but snapshot coverage Partial/None or stale) or
/// <c>NotPriced</c>. Settled = Priced + Free.
/// </para>
/// Every count is a correlated scalar subquery (one statement for any number of dossiers).
/// </summary>
public sealed class DossierPriceCounts
{
    public Guid DossierId { get; init; }

    // Raw per-kind counts (SQL). T = transport activities, S = standalone, L = legacy links.
    public int TransportUnits { get; init; }
    public int TransportPriced { get; init; }
    public int TransportFree { get; init; }
    public int TransportPartial { get; init; }
    public decimal? TransportTotal { get; init; }
    public int StandaloneUnits { get; init; }
    public int StandalonePriced { get; init; }
    public int StandaloneFree { get; init; }
    public int StandaloneZero { get; init; }
    public decimal? StandaloneTotal { get; init; }
    public int LegacyUnits { get; init; }
    public int LegacyPriced { get; init; }
    public int LegacyFree { get; init; }
    public int LegacyPartial { get; init; }
    public decimal? LegacyTotal { get; init; }

    /// <summary>Relevant (billable, not cancelled) components.</summary>
    public int Relevant => TransportUnits + StandaloneUnits + LegacyUnits;

    /// <summary>Components with a price by provenance (any bucket except NotPriced).</summary>
    public int WithPrice => TransportPriced + StandalonePriced + LegacyPriced;

    public int Free => TransportFree + StandaloneFree + LegacyFree;

    public int ZeroUnconfirmed => StandaloneZero;

    public int Partial => TransportPartial + LegacyPartial;

    public int Unpriced => Relevant - WithPrice;

    /// <summary>Priced and complete, not free.</summary>
    public int Priced => WithPrice - Free - ZeroUnconfirmed - Partial;

    public int Settled => Priced + Free;

    /// <summary>Sum over the components with a price; null when none has one (never € 0 for "no price").</summary>
    public decimal? Total => WithPrice > 0 ? (TransportTotal ?? 0m) + (StandaloneTotal ?? 0m) + (LegacyTotal ?? 0m) : null;

    /// <summary>The dossier-level status in the <see cref="ActivityPriceStatus"/> vocabulary; null = nothing relevant.</summary>
    public string? Status
    {
        get
        {
            if (Relevant == 0) return null;
            if (Settled == Relevant) return Priced == 0 ? ActivityPriceStatus.Free : ActivityPriceStatus.Priced;
            if (Unpriced == Relevant) return ActivityPriceStatus.NotPriced;
            if (Unpriced == 0 && Partial == 0) return ActivityPriceStatus.ZeroUnconfirmed;
            return ActivityPriceStatus.PartiallyPriced;
        }
    }

    public static readonly DossierPriceCounts Empty = new();

    /// <summary>Intentional € 0 on an order (one-off at 0 or override at 0) — <see cref="OrderPricingState.IsIntentionalZero"/> as a tree.</summary>
    private static readonly Expression<Func<TransportOrder, bool>> IntentionalZero = o =>
        o.FreeConfirmed || (o.PricingSource == OrderPricingSource.OneOff && o.OneOffFixedAmount == 0m) || (o.PriceIsManual && o.AgreedPrice == 0m);

    private static readonly Expression<Func<TransportOrder, bool>> NotIntentionalZero =
        Expression.Lambda<Func<TransportOrder, bool>>(Expression.Not(IntentionalZero.Body), IntentionalZero.Parameters);

    private static readonly Expression<Func<DossierActivityPricing, bool>> StandaloneFreeExpression = p =>
        ((p.PricingSource == ActivityPricingSource.OneOff && p.FixedAmount != null)
         || (p.PricingSource == ActivityPricingSource.Lines && p.AgreedPrice != null))
        && p.AgreedPrice == 0m && p.FreeConfirmed;

    /// <summary>The counts for every dossier of <paramref name="dossiers"/> — composable (the search filter narrows on it).</summary>
    public static IQueryable<DossierPriceCounts> Project(TransportationDbContext db, IQueryable<TransportDossier> dossiers) =>
        dossiers.Select(d => new DossierPriceCounts
        {
            DossierId = d.Id,
            TransportUnits = db.DossierActivities
                .Where(a => a.DossierId == d.Id)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && x.t.HasStops)
                .Count(x => !db.TransportOrders.Any(o => o.Id == x.a.LinkedTransportOrderId && o.Status == TransportOrderStatus.Cancelled)),
            TransportPriced = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && x.t.HasStops)
                .Join(db.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Count(),
            TransportFree = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && x.t.HasStops)
                .Join(db.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Where(IntentionalZero)
                .Count(),
            TransportPartial = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && x.t.HasStops)
                .Join(db.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Where(NotIntentionalZero)
                .Count(o => db.TransportOrderPricingSnapshots.Any(s => s.TransportOrderId == o.Id
                    && (s.CoverageStatus == "Partial" || s.CoverageStatus == "None" || s.IsStale))),
            TransportTotal = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && x.t.HasStops)
                .Join(db.TransportOrders, x => x.a.LinkedTransportOrderId, o => o.Id, (x, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                // OrderPricingState.EffectiveAgreedPrice — the same tree, inlined for SQL.
                .Sum(o => o.AgreedPrice ?? (o.PricingSource == OrderPricingSource.OneOff ? o.OneOffFixedAmount : null)),
            StandaloneUnits = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => t)
                .Count(t => t.IsBillable && !t.HasStops),
            StandalonePriced = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && !x.t.HasStops)
                .Join(db.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p)
                .Where(ActivityPricingState.IsPricedExpression)
                .Count(),
            StandaloneFree = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && !x.t.HasStops)
                .Join(db.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p)
                .Where(StandaloneFreeExpression)
                .Count(),
            StandaloneZero = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && !x.t.HasStops)
                .Join(db.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p)
                .Where(ActivityPricingState.IsUnconfirmedZeroExpression)
                .Count(),
            StandaloneTotal = db.DossierActivities
                .Where(a => a.DossierId == d.Id && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled)
                .Join(db.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => new { a, t })
                .Where(x => x.t.IsBillable && !x.t.HasStops)
                .Join(db.DossierActivityPricings, x => x.a.Id, p => p.DossierActivityId, (x, p) => p)
                .Where(ActivityPricingState.IsPricedExpression)
                .Sum(p => p.AgreedPrice),
            LegacyUnits = db.DossierOrders
                .Where(l => l.DossierId == d.Id
                            && !db.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == l.TransportOrderId))
                .Join(db.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                .Count(o => o.Status != TransportOrderStatus.Cancelled),
            LegacyPriced = db.DossierOrders
                .Where(l => l.DossierId == d.Id
                            && !db.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == l.TransportOrderId))
                .Join(db.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Count(),
            LegacyFree = db.DossierOrders
                .Where(l => l.DossierId == d.Id
                            && !db.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == l.TransportOrderId))
                .Join(db.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Where(IntentionalZero)
                .Count(),
            LegacyPartial = db.DossierOrders
                .Where(l => l.DossierId == d.Id
                            && !db.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == l.TransportOrderId))
                .Join(db.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Where(NotIntentionalZero)
                .Count(o => db.TransportOrderPricingSnapshots.Any(s => s.TransportOrderId == o.Id
                    && (s.CoverageStatus == "Partial" || s.CoverageStatus == "None" || s.IsStale))),
            LegacyTotal = db.DossierOrders
                .Where(l => l.DossierId == d.Id
                            && !db.DossierActivities.Any(a => a.DossierId == d.Id && a.LinkedTransportOrderId == l.TransportOrderId))
                .Join(db.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                .Where(o => o.Status != TransportOrderStatus.Cancelled)
                .Where(OrderPricingState.IsPricedExpression)
                .Sum(o => o.AgreedPrice ?? (o.PricingSource == OrderPricingSource.OneOff ? o.OneOffFixedAmount : null)),
        });

    /// <summary>Counts per dossier id (tenant scope comes from the caller's id set + global query filters).</summary>
    public static async Task<Dictionary<Guid, DossierPriceCounts>> LoadAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> dossierIds, CancellationToken cancellationToken)
    {
        if (dossierIds.Count == 0)
        {
            return [];
        }

        var ids = dossierIds.ToList();
        return await Project(db, db.TransportDossiers.AsNoTracking().Where(d => d.TenantId == tenantId && ids.Contains(d.Id)))
            .ToDictionaryAsync(c => c.DossierId, cancellationToken);
    }

    /// <summary>
    /// Narrows a dossier query to a list price-status filter value: <c>priced</c> (every relevant
    /// component settled), <c>partial</c> (some price, not all settled), <c>unpriced</c> (no price at all).
    /// Exactly the buckets of <see cref="Status"/>: Priced/Free → priced, PartiallyPriced/ZeroUnconfirmed → partial,
    /// NotPriced → unpriced.
    /// </summary>
    public static IQueryable<TransportDossier> Filter(TransportationDbContext db, IQueryable<TransportDossier> dossiers, string priceStatus)
    {
        var counts = Project(db, dossiers);
        IQueryable<DossierPriceCounts> matching = priceStatus switch
        {
            "priced" => counts.Where(c => c.TransportUnits + c.StandaloneUnits + c.LegacyUnits > 0
                && (c.TransportPriced + c.StandalonePriced + c.LegacyPriced) - c.StandaloneZero - c.TransportPartial - c.LegacyPartial
                   == c.TransportUnits + c.StandaloneUnits + c.LegacyUnits),
            "partial" => counts.Where(c => c.TransportPriced + c.StandalonePriced + c.LegacyPriced > 0
                && (c.TransportPriced + c.StandalonePriced + c.LegacyPriced) - c.StandaloneZero - c.TransportPartial - c.LegacyPartial
                   < c.TransportUnits + c.StandaloneUnits + c.LegacyUnits),
            "unpriced" => counts.Where(c => c.TransportUnits + c.StandaloneUnits + c.LegacyUnits > 0
                && c.TransportPriced + c.StandalonePriced + c.LegacyPriced == 0),
            _ => throw new Common.DomainValidationException("priceStatus", "Onbekende prijsstatus."),
        };
        var ids = matching.Select(c => c.DossierId);
        return dossiers.Where(d => ids.Contains(d.Id));
    }
}
