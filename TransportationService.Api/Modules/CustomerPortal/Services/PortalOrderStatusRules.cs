using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

/// <summary>
/// M5: the customer-facing order status and visibility, decided server-side so every portal
/// surface (list, detail, dashboard) tells the same story. Internal statuses are NOT changed.
/// <list type="bullet">
/// <item>Visible: every order the customer submitted via the portal (any status), and every
/// internal order once it reached Confirmed or later. A Cancelled order is only visible when it
/// was visible before (submitted, or Confirmed+ at some point); internal Drafts — including
/// redelivery drafts and historical drafts — never reach the portal.</item>
/// <item>Draft of a portal-submitted order (sent back internally) → "Submitted": from the
/// customer's view it is still being reviewed.</item>
/// <item>Invoiced → "Completed" until an invoice containing the order is actually issued
/// (Sent/Paid); a draft invoice is internal bookkeeping. Then → "Invoiced".</item>
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

    /// <summary>Resolves the customer-facing status of each order; hidden orders are absent from the result.</summary>
    public static async Task<Dictionary<Guid, TransportOrderStatus>> ResolveAsync(
        TransportationDbContext dbContext, Guid tenantId,
        IReadOnlyCollection<(Guid Id, TransportOrderStatus Status)> orders, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TransportOrderStatus>();
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

        var invoicedIds = orders.Where(o => o.Status == TransportOrderStatus.Invoiced).Select(o => o.Id).ToList();
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
                result[id] = facing;
            }
        }

        return result;
    }
}
