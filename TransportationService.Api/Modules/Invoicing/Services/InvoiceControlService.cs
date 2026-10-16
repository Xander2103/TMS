using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public sealed record ControlOrderDto(
    Guid TransportOrderId, string OrderNumber, DateOnly OrderDate, decimal? AgreedPrice,
    string? DossierNumber, string? CustomerReference,
    string InvoiceReadiness, IReadOnlyList<string> Reasons,
    /// <summary>P12: postponed until this date (excluded from proposals, shown separately).</summary>
    DateOnly? SnoozedUntil = null, string? SnoozeReason = null);

/// <summary>
/// T-H8: one billable STANDALONE dossier activity (crane work, plateau, storage ...) on the control
/// page. <c>Reasons</c> (review only): <c>activity.price_missing</c> | <c>activity.price_zero_unconfirmed</c>.
/// </summary>
public sealed record ControlActivityDto(
    Guid DossierActivityId, Guid DossierId, string DossierNumber,
    string ActivityTypeName, string? Label,
    /// <summary>Completion date, else the planned date.</summary>
    DateOnly? Date,
    decimal? AgreedPrice, bool IsFree,
    /// <summary>Planned | InProgress | Completed.</summary>
    string ExecutionStatus,
    Guid CustomerId, string CustomerName,
    IReadOnlyList<string> Reasons);

public sealed record InvoiceProposalDto(
    Guid CustomerId, string CustomerName, string Grouping,
    /// <summary>"Dossier DOS-0012" | "Week 33" | "Maand augustus" | "Referentie X" | "Klaar".</summary>
    string GroupLabel,
    IReadOnlyList<ControlOrderDto> Orders,
    decimal TotalAmount,
    /// <summary>T-H8: ready standalone activities of the same group (invoiced via DossierActivityIds).</summary>
    IReadOnlyList<ControlActivityDto>? Activities = null);

public sealed record InvoiceControlDto(
    /// <summary>Proposals of READY orders, honoring each customer's grouping preference.</summary>
    IReadOnlyList<InvoiceProposalDto> Proposals,
    /// <summary>Completed but ReviewRequired orders with their readiness reasons.</summary>
    IReadOnlyList<ControlOrderDto> NeedsReview,
    /// <summary>Approved incident charges whose sales line could not land (locked/invoiced order).</summary>
    IReadOnlyList<string> PendingCharges,
    /// <summary>P12: postponed orders — out of the proposals until their date, never hidden.</summary>
    IReadOnlyList<ControlOrderDto> Snoozed,
    /// <summary>
    /// T-H8: executed billable standalone activities that cannot be invoiced yet (no sales price,
    /// or EUR 0 not confirmed as free) — never invisible to accounting.
    /// </summary>
    IReadOnlyList<ControlActivityDto>? ActivitiesNeedingReview = null);

public interface IInvoiceControlService
{
    Task<InvoiceControlDto> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Wave 10: the invoice-control workspace. Reads the Wave-2 readiness projection (never
/// recomputes it), groups READY uninvoiced orders per customer according to the customer's
/// InvoiceGrouping preference (PerDossier / Weekly / Monthly / ByReference; Manual customers
/// get one plain "Klaar" bucket) and surfaces ReviewRequired orders with their reasons plus
/// approved-but-unapplied incident charges. Creating the invoice = the EXISTING invoice
/// create endpoint with the proposal's order ids.
/// </summary>
public class InvoiceControlService : IInvoiceControlService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InvoiceControlService(
        TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private readonly TimeProvider _timeProvider;

    public async Task<InvoiceControlDto> GetAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var invoicedOrderIds = _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId != null)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != Entities.InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => l.TransportOrderId!.Value);

        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Status == TransportOrderStatus.Completed
                        && !invoicedOrderIds.Contains(o.Id))
            .Join(_dbContext.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new
                {
                    o.Id, o.OrderNumber, o.OrderDate, o.AgreedPrice, o.CustomerReference,
                    o.InvoiceReadiness, o.InvoiceReadinessReasons,
                    o.InvoiceSnoozeUntil, o.InvoiceSnoozeReason,
                    CustomerId = c.Id, CustomerName = c.Name, c.InvoiceGrouping,
                })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var dossierByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && orderIds.Contains(l.TransportOrderId))
                .Join(_dbContext.TransportDossiers.AsNoTracking(), l => l.DossierId, d => d.Id,
                    (l, d) => new { l.TransportOrderId, d.DossierNumber })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.First().DossierNumber);

        var dtoById = orders.ToDictionary(
            o => o.Id,
            o => new ControlOrderDto(
                o.Id, o.OrderNumber, o.OrderDate, o.AgreedPrice,
                dossierByOrder.GetValueOrDefault(o.Id), o.CustomerReference,
                o.InvoiceReadiness,
                o.InvoiceReadinessReasons?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [],
                o.InvoiceSnoozeUntil, o.InvoiceSnoozeReason));

        // P12: an active snooze parks the order — out of proposals AND review, but always
        // visible in its own section. An expired snooze simply stops applying.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        bool IsSnoozed(DateOnly? until) => until is { } u && u > today;
        var snoozed = orders
            .Where(o => IsSnoozed(o.InvoiceSnoozeUntil))
            .OrderBy(o => o.InvoiceSnoozeUntil)
            .Select(o => dtoById[o.Id])
            .ToList();

        var needsReview = orders
            .Where(o => o.InvoiceReadiness == "ReviewRequired" && !IsSnoozed(o.InvoiceSnoozeUntil))
            .OrderBy(o => o.OrderDate)
            .Select(o => dtoById[o.Id])
            .ToList();

        static string GroupKey(string grouping, DateOnly date, string? dossier, string? reference) => grouping switch
        {
            "PerDossier" => dossier ?? "Zonder dossier",
            "Weekly" => $"Week {System.Globalization.ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue))}",
            "Monthly" => $"Maand {date:MM-yyyy}",
            "ByReference" => reference is { Length: > 0 } ? $"Referentie {reference}" : "Zonder referentie",
            _ => "Klaar voor facturatie",
        };

        var (readyActivities, activitiesNeedingReview) = await LoadStandaloneActivitiesAsync(today, cancellationToken);

        // Orders and ready standalone activities share the customer's grouping: one proposal per
        // (customer, group) carrying both, invoiced together through the existing create endpoint.
        var orderGroups = orders
            .Where(o => o.InvoiceReadiness == "ReadyForInvoice" && !IsSnoozed(o.InvoiceSnoozeUntil))
            .Select(o => new ProposalItem(
                new ProposalKey(o.CustomerId, o.CustomerName, o.InvoiceGrouping,
                    GroupKey(o.InvoiceGrouping, o.OrderDate, dossierByOrder.GetValueOrDefault(o.Id), o.CustomerReference)),
                dtoById[o.Id], null));
        var activityGroups = readyActivities.Select(a => new ProposalItem(
            new ProposalKey(a.Dto.CustomerId, a.Dto.CustomerName, a.Grouping,
                GroupKey(a.Grouping, a.Dto.Date ?? today, a.Dto.DossierNumber, a.CustomerReference)),
            null, a.Dto));
        var proposals = orderGroups.Concat(activityGroups)
            .GroupBy(x => x.Key)
            .Select(g =>
            {
                var groupOrders = g.Where(x => x.Order is not null).Select(x => x.Order!).OrderBy(o => o.OrderDate).ToList();
                var groupActivities = g.Where(x => x.Activity is not null).Select(x => x.Activity!)
                    .OrderBy(a => a.DossierNumber).ThenBy(a => a.Date).ToList();
                return new InvoiceProposalDto(
                    g.Key.CustomerId, g.Key.CustomerName, g.Key.Grouping, g.Key.Label,
                    groupOrders,
                    groupOrders.Sum(o => o.AgreedPrice ?? 0m) + groupActivities.Sum(a => a.AgreedPrice ?? 0m),
                    groupActivities);
            })
            .OrderBy(p => p.CustomerName).ThenBy(p => p.GroupLabel)
            .ToList();

        // Approved incident charges that could not land on the order (locked/invoiced price).
        var pendingCharges = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ChargeDecision == "Approved" && i.TransportOrderId != null)
            .Join(_dbContext.TransportOrderPricingSnapshots.AsNoTracking()
                    .Where(s => s.Status == OrderPricingStatus.Locked || s.Status == OrderPricingStatus.Invoiced),
                i => i.TransportOrderId, s => s.TransportOrderId,
                (i, s) => new { i.Title, i.ChargeAmount, i.TransportOrderId })
            .Join(_dbContext.TransportOrders.AsNoTracking(), x => x.TransportOrderId, o => o.Id,
                (x, o) => $"{o.OrderNumber}: € {x.ChargeAmount:0.00} — {x.Title} (prijs vergrendeld; voeg de lijn handmatig toe bij facturatie)")
            .ToListAsync(cancellationToken);

        return new InvoiceControlDto(proposals, needsReview, pendingCharges, snoozed, activitiesNeedingReview);
    }

    private sealed record ProposalKey(Guid CustomerId, string CustomerName, string Grouping, string Label);

    private sealed record ProposalItem(ProposalKey Key, ControlOrderDto? Order, ControlActivityDto? Activity);

    private sealed record ReadyActivity(ControlActivityDto Dto, string Grouping, string? CustomerReference);

    /// <summary>
    /// T-H8 — every billable standalone activity counts for accounting. Scope: billable type
    /// without stops, not cancelled, not on a live invoice, in a non-cancelled dossier with a
    /// customer. Then, with the SAME rule as <c>InvoiceService.ListUninvoicedActivitiesAsync</c>:
    /// <list type="bullet">
    ///   <item><b>ready</b>: priced by provenance and (amount != 0 or confirmed free), and — for
    ///   planning-relevant (operational) types — Completed;</item>
    ///   <item><b>review</b>: executed (Completed, or a non-operational unit of a confirmed dossier)
    ///   but without a sales price, or at EUR 0 without the "free" confirmation.</item>
    /// </list>
    /// A planned, not yet executed operational activity is neither: like an order before completion.
    /// </summary>
    private async Task<(List<ReadyActivity> Ready, List<ControlActivityDto> Review)> LoadStandaloneActivitiesAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var invoicedActivityIds = _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DossierActivityId != null)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != Entities.InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => l.DossierActivityId!.Value);

        var rows = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == null
                        && a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled
                        && !invoicedActivityIds.Contains(a.Id))
            .Join(_dbContext.ActivityTypes.AsNoTracking().Where(t => t.TenantId == tenantId && t.IsBillable && !t.HasStops),
                a => a.ActivityTypeId, t => t.Id, (a, t) => new { Activity = a, Type = t })
            .Join(_dbContext.TransportDossiers.AsNoTracking()
                    .Where(d => d.TenantId == tenantId && d.CustomerId != null && d.Status != DossierStatus.Cancelled),
                x => x.Activity.DossierId, d => d.Id, (x, d) => new { x.Activity, x.Type, Dossier = d })
            .Join(_dbContext.Customers.AsNoTracking(), x => x.Dossier.CustomerId, c => c.Id,
                (x, c) => new { x.Activity, x.Type, x.Dossier, Customer = c })
            // Only what is executed, or a commercial unit that bills on its price alone.
            .Where(x => x.Activity.ExecutionStatus == DossierActivityExecutionStatus.Completed
                        || !x.Type.PlanningRelevant)
            .Select(x => new
            {
                x.Activity.Id, x.Activity.DossierId, x.Dossier.DossierNumber, DossierStatus = x.Dossier.Status,
                x.Dossier.CustomerReference, TypeName = x.Type.Name, x.Type.PlanningRelevant,
                x.Activity.Label, x.Activity.PlannedDate, x.Activity.CompletedAt, x.Activity.ExecutionStatus,
                CustomerId = x.Customer.Id, CustomerName = x.Customer.Name, x.Customer.InvoiceGrouping,
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var pricings = ids.Count == 0
            ? new Dictionary<Guid, DossierActivityPricing>()
            : await _dbContext.DossierActivityPricings.AsNoTracking()
                .Where(p => p.TenantId == tenantId && ids.Contains(p.DossierActivityId))
                .ToDictionaryAsync(p => p.DossierActivityId, cancellationToken);

        var ready = new List<ReadyActivity>();
        var review = new List<ControlActivityDto>();
        foreach (var r in rows)
        {
            var pricing = pricings.GetValueOrDefault(r.Id);
            var priced = pricing is not null && ActivityPricingState.IsPriced(pricing);
            var unconfirmedZero = priced && ActivityPricingState.IsUnconfirmedZero(pricing!);
            // The business day of the work (like an order's date), not the UTC date of the completion
            // click — completing just after midnight showed yesterday.
            var date = r.PlannedDate ?? (r.CompletedAt is { } done ? DateOnly.FromDateTime(done) : null);
            ControlActivityDto Dto(IReadOnlyList<string> reasons) => new(
                r.Id, r.DossierId, r.DossierNumber, r.TypeName, r.Label, date,
                priced ? pricing!.AgreedPrice : null, priced && ActivityPricingState.IsFree(pricing!),
                r.ExecutionStatus.ToString(), r.CustomerId, r.CustomerName, reasons);

            if (priced && !unconfirmedZero)
            {
                // An Invoiced price record without a live invoice line is not offered again.
                if (pricing!.Status != OrderPricingStatus.Invoiced)
                {
                    ready.Add(new ReadyActivity(Dto([]), r.InvoiceGrouping, r.CustomerReference));
                }

                continue;
            }

            var executed = r.ExecutionStatus == DossierActivityExecutionStatus.Completed
                           || (!r.PlanningRelevant && r.DossierStatus == DossierStatus.Closed);
            if (executed)
            {
                review.Add(Dto([unconfirmedZero ? "activity.price_zero_unconfirmed" : "activity.price_missing"]));
            }
        }

        return (ready, review.OrderBy(a => a.Date ?? today).ThenBy(a => a.DossierNumber).ToList());
    }
}
