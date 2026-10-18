using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

/// <summary>
/// The customer-facing status codes — the ONLY order status a portal response carries. Internal
/// lifecycle keys (Draft, Submitted, Completed, Invoiced …) never go over the portal wire; the
/// frontend only translates these codes.
/// </summary>
public static class PortalCustomerStatuses
{
    /// <summary>Submitted via the portal, nobody has acted on it yet.</summary>
    public const string Received = "Received";
    /// <summary>A planner is handling it: sent back to draft for corrections, or the customer answered a question.</summary>
    public const string InReview = "InReview";
    /// <summary>A planner asked a question ("Info opvragen") the customer has not answered yet.</summary>
    public const string InfoRequested = "InfoRequested";
    public const string Confirmed = "Confirmed";
    public const string Planned = "Planned";
    /// <summary>The trip is under way.</summary>
    public const string InTransit = "InTransit";
    public const string Delivered = "Delivered";
    public const string PartiallyDelivered = DeliveryOutcomes.PartiallyDelivered;
    public const string NotDelivered = DeliveryOutcomes.NotDelivered;
    public const string RedeliveryPlanned = DeliveryOutcomes.RedeliveryPlanned;
    public const string Redelivered = DeliveryOutcomes.Redelivered;
    /// <summary>A portal request the carrier declined (cancelled before it was ever confirmed).</summary>
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    /// <summary>Timeline only: the operational close of an order that was not (fully) delivered.</summary>
    public const string ExecutionClosed = "ExecutionClosed";

    /// <summary>Orders still in the customer's "active" bucket (requests and work not yet finished).</summary>
    public static readonly string[] Active = [Received, InReview, InfoRequested, Confirmed, Planned, InTransit];

    /// <summary>Accepted delivery work that has not happened yet — the only candidates for "Aankomende leveringen".</summary>
    public static readonly string[] UpcomingDelivery = [Confirmed, Planned, InTransit];
}

/// <summary>The one customer-facing projection of an order (see <see cref="PortalOrderStatusRules"/>).</summary>
public sealed record PortalOrderProjection(
    string CustomerStatus,
    /// <summary>The internal lifecycle as the customer may know it (drafts already folded away);
    /// server-side use only — never serialised to the portal.</summary>
    TransportOrderStatus Lifecycle,
    string? DeliveryOutcome,
    Guid? RedeliveryOrderId,
    string? RedeliveryOrderNumber,
    /// <summary>An invoice containing the order has been issued (Sent/Paid). Shown separately, never as the status.</summary>
    bool InvoiceIssued,
    PortalInfoRequestState? InfoRequest);

/// <summary>The latest "Info opvragen" question on the order thread and whether the customer answered it.</summary>
public sealed record PortalInfoRequestState(string Question, DateTime AskedAt, DateTime? AnsweredAt);

/// <summary>
/// M5 + portal final remediation: the customer-facing order status and visibility, decided
/// server-side so every portal surface (list, detail, dashboard, messages, documents) tells the
/// same story. Internal statuses are NOT changed; this is a read projection over them, the EX-08
/// delivery outcome and the order's portal thread.
/// <list type="bullet">
/// <item>Visible: every order the customer submitted via the portal (any status), and every
/// internal order once it reached Confirmed or later. A Cancelled order is only visible when it
/// was visible before (submitted, or Confirmed+ at some point); internal Drafts — including
/// redelivery drafts and historical drafts — never reach the portal.</item>
/// <item>Submitted → "Aanvraag ontvangen", or "Informatie gevraagd" while a planner's question
/// is open, or "Wordt beoordeeld" once the customer answered it. A portal request sent back to
/// Draft internally → "Wordt beoordeeld" (no e-mail: nothing was decided).</item>
/// <item>Finished orders show what reached the consignee (Geleverd / Gedeeltelijk geleverd / Niet
/// geleverd / Herlevering gepland / Herleverd). A redelivery counts only once the customer may see
/// it; while it is an internal draft the first attempt's truth stands.</item>
/// <item>Invoicing never replaces the delivery truth: "Factuur beschikbaar" is a separate flag.</item>
/// </list>
/// </summary>
public static class PortalOrderStatusRules
{
    /// <summary>Statuses that make an internal order visible to the customer.</summary>
    private static readonly TransportOrderStatus[] CustomerVisibleStatuses =
    [
        TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned,
        TransportOrderStatus.InProgress, TransportOrderStatus.Completed, TransportOrderStatus.Invoiced,
    ];

    /// <summary>Pure mapping; null = the customer must not see the order at all.</summary>
    public static TransportOrderStatus? CustomerFacing(
        TransportOrderStatus status, bool everSubmitted, bool everVisible, bool invoiceIssued) => status switch
    {
        TransportOrderStatus.Draft => everSubmitted ? TransportOrderStatus.Submitted : null,
        TransportOrderStatus.Cancelled => everSubmitted || everVisible ? TransportOrderStatus.Cancelled : null,
        TransportOrderStatus.Invoiced => invoiceIssued ? TransportOrderStatus.Invoiced : TransportOrderStatus.Completed,
        _ => status,
    };

    /// <summary>True once the customer should no longer see an expected-arrival time.</summary>
    public static bool IsDelivered(TransportOrderStatus customerFacing) =>
        customerFacing is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced or TransportOrderStatus.Cancelled;

    /// <summary>
    /// Pure status decision over the facts. <paramref name="internalStatus"/> is the real lifecycle,
    /// <paramref name="facing"/> the visibility-folded one (<see cref="CustomerFacing"/>).
    /// </summary>
    public static string CustomerStatus(
        TransportOrderStatus internalStatus, TransportOrderStatus facing, bool everConfirmed,
        string? deliveryOutcome, PortalInfoRequestState? infoRequest) => facing switch
    {
        TransportOrderStatus.Submitted when internalStatus == TransportOrderStatus.Draft => PortalCustomerStatuses.InReview,
        TransportOrderStatus.Submitted when infoRequest is { AnsweredAt: null } => PortalCustomerStatuses.InfoRequested,
        TransportOrderStatus.Submitted when infoRequest is not null => PortalCustomerStatuses.InReview,
        TransportOrderStatus.Submitted => PortalCustomerStatuses.Received,
        TransportOrderStatus.Confirmed => PortalCustomerStatuses.Confirmed,
        TransportOrderStatus.Planned => PortalCustomerStatuses.Planned,
        TransportOrderStatus.InProgress => PortalCustomerStatuses.InTransit,
        TransportOrderStatus.Cancelled => everConfirmed ? PortalCustomerStatuses.Cancelled : PortalCustomerStatuses.Rejected,
        TransportOrderStatus.Completed or TransportOrderStatus.Invoiced => deliveryOutcome switch
        {
            DeliveryOutcomes.PartiallyDelivered or DeliveryOutcomes.NotDelivered
                or DeliveryOutcomes.RedeliveryPlanned or DeliveryOutcomes.Redelivered => deliveryOutcome,
            _ => PortalCustomerStatuses.Delivered,
        },
        _ => PortalCustomerStatuses.Received,
    };

    /// <summary>Resolves the customer-facing lifecycle of each order; hidden orders are absent from the result.</summary>
    public static async Task<Dictionary<Guid, TransportOrderStatus>> ResolveAsync(
        TransportationDbContext dbContext, Guid tenantId,
        IReadOnlyCollection<(Guid Id, TransportOrderStatus Status)> orders, CancellationToken cancellationToken) =>
        (await ResolveFactsAsync(dbContext, tenantId, orders, cancellationToken))
            .ToDictionary(f => f.Key, f => f.Value.Facing);

    /// <summary>THE customer visibility rule for one order id of this customer (other customers' and
    /// hidden orders alike answer false, so callers return the same 404).</summary>
    public static async Task<bool> IsVisibleToCustomerAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid customerId, Guid orderId, CancellationToken cancellationToken) =>
        (await VisibleOrderIdsAsync(dbContext, tenantId, customerId, [orderId], cancellationToken)).Contains(orderId);

    /// <summary>The subset of <paramref name="orderIds"/> that belongs to the customer AND is visible to them.</summary>
    public static async Task<HashSet<Guid>> VisibleOrderIdsAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid customerId, IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var ids = orderIds.Distinct().ToList();
        var orders = (await dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.CustomerId == customerId && ids.Contains(o.Id))
                .Select(o => new { o.Id, o.Status })
                .ToListAsync(cancellationToken))
            .Select(o => (o.Id, o.Status))
            .ToList();
        return (await ResolveAsync(dbContext, tenantId, orders, cancellationToken)).Keys.ToHashSet();
    }

    /// <summary>
    /// The full customer-facing projection of each visible order (hidden orders are absent). One
    /// pass over status history, invoices, delivery outcome and the portal thread.
    /// </summary>
    public static async Task<Dictionary<Guid, PortalOrderProjection>> ProjectAsync(
        TransportationDbContext dbContext, Guid tenantId,
        IReadOnlyCollection<(Guid Id, TransportOrderStatus Status)> orders, CancellationToken cancellationToken)
    {
        var facts = await ResolveFactsAsync(dbContext, tenantId, orders, cancellationToken);
        var result = new Dictionary<Guid, PortalOrderProjection>();
        if (facts.Count == 0)
        {
            return result;
        }

        var finishedIds = facts
            .Where(f => f.Value.Facing is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced)
            .Select(f => f.Key)
            .ToList();
        var outcomes = await DeliveryOutcomeRules.ResolveAsync(dbContext, tenantId, finishedIds, cancellationToken);

        // A redelivery only counts once the customer may see that order itself (Confirmed or later);
        // a redelivery that came back undelivered again tells that, not "Herleverd".
        var redeliveryIds = outcomes.Values.Where(o => o.RedeliveryOrderId is not null).Select(o => o.RedeliveryOrderId!.Value).Distinct().ToList();
        var redeliveryStatuses = redeliveryIds.Count == 0
            ? []
            : (await dbContext.TransportOrders.AsNoTracking()
                    .Where(o => o.TenantId == tenantId && redeliveryIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.Status })
                    .ToListAsync(cancellationToken))
                .Select(o => (o.Id, o.Status))
                .ToList();
        var visibleRedeliveries = (await ResolveAsync(dbContext, tenantId, redeliveryStatuses, cancellationToken)).Keys.ToHashSet();
        var redeliveryOutcomes = await DeliveryOutcomeRules.ResolveAsync(
            dbContext, tenantId, redeliveryStatuses.Where(r => r.Status is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced)
                .Select(r => r.Id).ToList(), cancellationToken);

        var submittedIds = facts.Where(f => f.Value.Facing == TransportOrderStatus.Submitted).Select(f => f.Key).ToList();
        var infoRequests = await InfoRequestsAsync(dbContext, tenantId, submittedIds, cancellationToken);

        foreach (var (id, fact) in facts)
        {
            string? outcome = null;
            Guid? redeliveryId = null;
            string? redeliveryNumber = null;
            if (outcomes.TryGetValue(id, out var resolved))
            {
                outcome = resolved.Outcome;
                if (resolved.RedeliveryOrderId is { } rid)
                {
                    if (!visibleRedeliveries.Contains(rid))
                    {
                        outcome = resolved.ShortfallOutcome ?? DeliveryOutcomes.NotDelivered;
                    }
                    else
                    {
                        redeliveryId = rid;
                        redeliveryNumber = resolved.RedeliveryOrderNumber;
                        if (outcome == DeliveryOutcomes.Redelivered
                            && redeliveryOutcomes.TryGetValue(rid, out var second)
                            && second.Outcome is DeliveryOutcomes.NotDelivered or DeliveryOutcomes.PartiallyDelivered)
                        {
                            outcome = second.Outcome;
                        }
                    }
                }
            }

            var infoRequest = infoRequests.GetValueOrDefault(id);
            result[id] = new PortalOrderProjection(
                CustomerStatus(fact.Internal, fact.Facing, fact.EverConfirmed, outcome, infoRequest),
                fact.Facing, outcome, redeliveryId, redeliveryNumber, fact.InvoiceIssued, infoRequest);
        }

        return result;
    }

    /// <summary>Customer-facing label code of one status-history step (the order's timeline).</summary>
    public static string? TimelineStatus(TransportOrderStatus toStatus, PortalOrderProjection projection) => toStatus switch
    {
        TransportOrderStatus.Submitted => PortalCustomerStatuses.Received,
        TransportOrderStatus.Confirmed => PortalCustomerStatuses.Confirmed,
        TransportOrderStatus.Planned => PortalCustomerStatuses.Planned,
        TransportOrderStatus.InProgress => PortalCustomerStatuses.InTransit,
        TransportOrderStatus.Completed => projection.DeliveryOutcome is null or DeliveryOutcomes.Delivered
            ? PortalCustomerStatuses.Delivered
            : PortalCustomerStatuses.ExecutionClosed,
        TransportOrderStatus.Cancelled => projection.CustomerStatus == PortalCustomerStatuses.Rejected
            ? PortalCustomerStatuses.Rejected
            : PortalCustomerStatuses.Cancelled,
        _ => null, // Draft and Invoiced are internal steps — billing is shown separately
    };

    private sealed record OrderFacts(TransportOrderStatus Internal, TransportOrderStatus Facing, bool EverConfirmed, bool InvoiceIssued);

    private static async Task<Dictionary<Guid, OrderFacts>> ResolveFactsAsync(
        TransportationDbContext dbContext, Guid tenantId,
        IReadOnlyCollection<(Guid Id, TransportOrderStatus Status)> orders, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, OrderFacts>();
        if (orders.Count == 0)
        {
            return result;
        }

        var ids = orders.Select(o => o.Id).Distinct().ToList();
        var history = await dbContext.TransportOrderStatusHistories.AsNoTracking()
            .Where(h => h.TenantId == tenantId && ids.Contains(h.TransportOrderId))
            .Select(h => new { h.TransportOrderId, h.FromStatus, h.ToStatus })
            .ToListAsync(cancellationToken);
        var submitted = history
            .Where(h => h.ToStatus == TransportOrderStatus.Submitted)
            .Select(h => h.TransportOrderId)
            .ToHashSet();
        var everVisible = history
            .Where(h => CustomerVisibleStatuses.Contains(h.ToStatus) || CustomerVisibleStatuses.Contains(h.FromStatus))
            .Select(h => h.TransportOrderId)
            .ToHashSet();
        var everConfirmed = history
            .Where(h => h.ToStatus is TransportOrderStatus.Confirmed or TransportOrderStatus.Planned or TransportOrderStatus.InProgress
                or TransportOrderStatus.Completed or TransportOrderStatus.Invoiced)
            .Select(h => h.TransportOrderId)
            .ToHashSet();

        var invoicedIds = orders
            .Where(o => o.Status is TransportOrderStatus.Invoiced or TransportOrderStatus.Completed)
            .Select(o => o.Id)
            .ToList();
        var issued = new HashSet<Guid>();
        if (invoicedIds.Count > 0)
        {
            var issuedIds = await (
                    from line in dbContext.InvoiceLines.AsNoTracking()
                    join invoice in dbContext.Invoices.AsNoTracking() on line.InvoiceId equals invoice.Id
                    where line.TenantId == tenantId && invoice.TenantId == tenantId
                        && line.TransportOrderId != null && invoicedIds.Contains(line.TransportOrderId.Value)
                        && invoice.Kind == InvoiceKind.Invoice
                        && (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Paid)
                    select line.TransportOrderId!.Value)
                .ToListAsync(cancellationToken);
            issued.UnionWith(issuedIds);
        }

        foreach (var (id, status) in orders)
        {
            if (CustomerFacing(status, submitted.Contains(id), everVisible.Contains(id), issued.Contains(id)) is { } facing)
            {
                result[id] = new OrderFacts(status, facing, everConfirmed.Contains(id), issued.Contains(id));
            }
        }

        return result;
    }

    private static async Task<Dictionary<Guid, PortalInfoRequestState>> InfoRequestsAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PortalInfoRequestState>();
        if (orderIds.Count == 0)
        {
            return result;
        }

        var ids = orderIds.ToList();
        var thread = await dbContext.CustomerMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.TransportOrderId != null && ids.Contains(m.TransportOrderId.Value)
                        && (m.IsInfoRequest || !m.AuthorIsStaff))
            .Select(m => new { OrderId = m.TransportOrderId!.Value, m.IsInfoRequest, m.AuthorIsStaff, m.Body, m.CreatedAt })
            .ToListAsync(cancellationToken);
        foreach (var group in thread.GroupBy(m => m.OrderId))
        {
            var question = group.Where(m => m.IsInfoRequest).OrderByDescending(m => m.CreatedAt).FirstOrDefault();
            if (question is null)
            {
                continue;
            }

            var answer = group.Where(m => !m.AuthorIsStaff && m.CreatedAt > question.CreatedAt).OrderBy(m => m.CreatedAt).FirstOrDefault();
            result[group.Key] = new PortalInfoRequestState(question.Body, question.CreatedAt, answer?.CreatedAt);
        }

        return result;
    }
}
