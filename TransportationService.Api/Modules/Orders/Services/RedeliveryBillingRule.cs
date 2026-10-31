using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Money;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// R-N2: a failed delivery and its redelivery are ONE transport for the customer. The original
/// order carries the transport price (docs/problems.md: the redelivery starts without a price,
/// "nooit een stille kopie van de facturatie", or explicitly free). A price on the redelivery is
/// therefore never billed next to a charging original; the decisions that resolve it are the
/// existing ones:
/// <list type="bullet">
///   <item>the redelivery is confirmed free (the normal outcome);</item>
///   <item>the original is confirmed free, or was credited in full: the redelivery then carries the charge;</item>
///   <item>an extra charge goes through the approved incident charge, a separate line on the original.</item>
/// </list>
/// The rule reads live facts rather than the readiness projection, so the invoice builder, the
/// control workspace, invoice creation and the issue preflight cannot disagree or go stale.
/// </summary>
public static class RedeliveryBillingRule
{
    /// <summary>This redelivery (chain) has an ancestor order that carries the transport price.</summary>
    public const string OriginalCharged = "redelivery.original_charged";

    /// <summary>This original's redelivery was already issued with a charge.</summary>
    public const string RedeliveryCharged = "redelivery.already_charged";

    public sealed record Hold(string Reason, Guid RelatedOrderId, string RelatedOrderNumber);

    private const int MaxChainDepth = 16;

    private sealed record Facts(
        Guid Id, string OrderNumber, Guid? ParentId, TransportOrderStatus Status,
        bool PriceIsManual, OrderPricingSource PricingSource, decimal? OneOffFixedAmount, decimal? AgreedPrice, bool FreeConfirmed)
    {
        public bool IsFree => FreeConfirmed || OrderPricingState.IsIntentionalZero(PriceIsManual, PricingSource, OneOffFixedAmount, AgreedPrice);

        public bool HasCharge => Status != TransportOrderStatus.Cancelled && !IsFree
                                 && (OrderPricingState.EffectiveAgreedPrice(PricingSource, OneOffFixedAmount, AgreedPrice) ?? 0m) > 0m;
    }

    /// <summary>Appends a hold reason to the readiness reasons of a read model.</summary>
    public static string WithReason(string? reasons, Hold hold) =>
        string.IsNullOrEmpty(reasons) ? hold.Reason : $"{reasons};{hold.Reason}";

    /// <summary>The Dutch explanation shared by invoice creation and the issue preflight.</summary>
    public static string Describe(string orderNumber, Hold hold) => hold.Reason == OriginalCharged
        ? $"Opdracht {orderNumber} is de herlevering van {hold.RelatedOrderNumber}, dat de transportprijs al draagt. Bevestig de herlevering als gratis, of reken een extra kost door via het incident."
        : $"Opdracht {orderNumber} werd herleverd via {hold.RelatedOrderNumber}, dat al met een prijs gefactureerd is. Bevestig deze opdracht als gratis, of crediteer eerst de factuur van de herlevering.";

    /// <summary>Which of these orders may not be billed now, and because of which related order.</summary>
    public static async Task<IReadOnlyDictionary<Guid, Hold>> ResolveAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var holds = new Dictionary<Guid, Hold>();
        if (orderIds.Count == 0)
        {
            return holds;
        }

        var family = await LoadFamilyAsync(db, tenantId, orderIds, cancellationToken);
        if (family.Values.All(o => o.ParentId is null || !family.ContainsKey(o.ParentId.Value)))
        {
            return holds; // no failed-delivery pair among these orders: the usual case, one query
        }

        var childrenOf = family.Values.Where(o => o.ParentId is not null).ToLookup(o => o.ParentId!.Value);
        var charged = family.Values.Where(o => o.HasCharge).Select(o => o.Id).ToList();
        var invoicing = await LoadInvoicingAsync(db, tenantId, charged, cancellationToken);

        bool StillCharges(Facts order) =>
            order.HasCharge && (!invoicing.TryGetValue(order.Id, out var state) || !state.FullyCredited);

        foreach (var id in orderIds.Distinct())
        {
            if (!family.TryGetValue(id, out var order) || order.IsFree || order.Status == TransportOrderStatus.Cancelled)
            {
                continue;
            }

            if (ChargingAncestor(order, family, StillCharges) is { } ancestor)
            {
                holds[id] = new Hold(OriginalCharged, ancestor.Id, ancestor.OrderNumber);
            }
            else if (IssuedDescendant(order, childrenOf, candidate =>
                         StillCharges(candidate) && invoicing.TryGetValue(candidate.Id, out var state) && state.Issued) is { } descendant)
            {
                holds[id] = new Hold(RedeliveryCharged, descendant.Id, descendant.OrderNumber);
            }
        }

        return holds;
    }

    private static Facts? ChargingAncestor(Facts order, Dictionary<Guid, Facts> family, Func<Facts, bool> charges)
    {
        var current = order;
        for (var depth = 0; depth < MaxChainDepth && current.ParentId is { } parentId && family.TryGetValue(parentId, out var parent); depth++)
        {
            if (charges(parent))
            {
                return parent;
            }

            current = parent;
        }

        return null;
    }

    private static Facts? IssuedDescendant(Facts order, ILookup<Guid, Facts> childrenOf, Func<Facts, bool> matches)
    {
        var level = childrenOf[order.Id].ToList();
        for (var depth = 0; depth < MaxChainDepth && level.Count > 0; depth++)
        {
            if (level.FirstOrDefault(matches) is { } match)
            {
                return match;
            }

            level = level.SelectMany(o => childrenOf[o.Id]).ToList();
        }

        return null;
    }

    /// <summary>The orders themselves plus every order linked to them, up and down the redelivery chain.</summary>
    private static async Task<Dictionary<Guid, Facts>> LoadFamilyAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var family = new Dictionary<Guid, Facts>();
        var expanded = new HashSet<Guid>();
        var frontier = orderIds.Distinct().ToList();
        for (var depth = 0; depth < MaxChainDepth && frontier.Count > 0; depth++)
        {
            var ids = frontier;
            expanded.UnionWith(ids);
            var rows = await db.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId
                            && (ids.Contains(o.Id) || (o.RedeliveryOfOrderId != null && ids.Contains(o.RedeliveryOfOrderId.Value))))
                .Select(o => new Facts(o.Id, o.OrderNumber, o.RedeliveryOfOrderId, o.Status,
                    o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.AgreedPrice, o.FreeConfirmed))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                family[row.Id] = row;
            }

            frontier = rows.Select(r => r.Id)
                .Concat(rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value))
                .Where(id => !expanded.Contains(id))
                .Distinct()
                .ToList();
        }

        return family;
    }

    private sealed record InvoicingState(bool Issued, bool FullyCredited);

    /// <summary>
    /// Per order: is it on an issued invoice, and were ALL its live invoices credited in full? Only
    /// an issued credit note counts; a partial one leaves the charge standing.
    /// </summary>
    private static async Task<Dictionary<Guid, InvoicingState>> LoadInvoicingAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var placements = await db.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId != null && orderIds.Contains(l.TransportOrderId.Value))
            .Join(db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled && i.Kind != InvoiceKind.CreditNote),
                l => l.InvoiceId, i => i.Id,
                (l, i) => new { OrderId = l.TransportOrderId!.Value, InvoiceId = i.Id, i.Status })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (placements.Count == 0)
        {
            return [];
        }

        var invoiceIds = placements.Select(p => p.InvoiceId).Distinct().ToList();
        var creditNotes = await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.CreditedInvoiceId != null && invoiceIds.Contains(i.CreditedInvoiceId.Value)
                        && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid))
            .Select(i => new { i.Id, CreditedInvoiceId = i.CreditedInvoiceId!.Value })
            .ToListAsync(cancellationToken);
        var fullyCredited = new HashSet<Guid>();
        if (creditNotes.Count > 0)
        {
            var creditNoteIds = creditNotes.Select(c => c.Id).ToList();
            var creditedIds = creditNotes.Select(c => c.CreditedInvoiceId).Distinct().ToList();
            var lines = await db.InvoiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && (creditNoteIds.Contains(l.InvoiceId) || creditedIds.Contains(l.InvoiceId)))
                .Select(l => new { l.InvoiceId, l.Quantity, l.UnitPrice })
                .ToListAsync(cancellationToken);
            decimal TotalOf(IEnumerable<Guid> ids)
            {
                var set = ids.ToHashSet();
                return lines.Where(l => set.Contains(l.InvoiceId)).Sum(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice));
            }

            foreach (var credited in creditNotes.GroupBy(c => c.CreditedInvoiceId))
            {
                if (TotalOf(credited.Select(c => c.Id)) >= TotalOf([credited.Key]))
                {
                    fullyCredited.Add(credited.Key);
                }
            }
        }

        return placements.GroupBy(p => p.OrderId).ToDictionary(
            g => g.Key,
            g => new InvoicingState(
                g.Any(p => p.Status is InvoiceStatus.Sent or InvoiceStatus.Paid && !fullyCredited.Contains(p.InvoiceId)),
                g.All(p => fullyCredited.Contains(p.InvoiceId))));
    }
}
