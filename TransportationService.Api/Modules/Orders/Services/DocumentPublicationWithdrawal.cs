using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Closure sprint 2026-09-23 (P0 security) — a document was published (CustomerVisible) for ONE
/// customer. When the order or dossier it hangs on moves to another customer, that publication
/// must not silently carry over: the flag goes back to internal, the file stays where it is,
/// internal users keep their normal access, and every withdrawal is audited per document. An
/// internal user republishes deliberately for the new customer.
///
/// Scoped explicitly on TenantId (the callers run inside a request scope, but the withdrawal
/// never relies on the global filter alone).
/// </summary>
public static class DocumentPublicationWithdrawal
{
    private const string EntityType = "TransportOrderDocument";
    public const string AuditAction = "CustomerVisibilityWithdrawn";

    /// <summary>Published documents of ONE order (what a per-order customer change withdraws).</summary>
    public static IQueryable<TransportOrderDocument> PublishedForOrder(TransportationDbContext db, Guid tenantId, Guid orderId) =>
        db.TransportOrderDocuments
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId && d.CustomerVisible);

    /// <summary>
    /// Published DOSSIER-level documents (TransportOrderId null). Order-level documents of the
    /// linked orders are withdrawn by the per-order change that the dossier change runs for them.
    /// </summary>
    public static IQueryable<TransportOrderDocument> PublishedForDossier(TransportationDbContext db, Guid tenantId, Guid dossierId) =>
        db.TransportOrderDocuments
            .Where(d => d.TenantId == tenantId && d.DossierId == dossierId && d.TransportOrderId == null && d.CustomerVisible);

    /// <summary>Sets every document in <paramref name="published"/> back to internal and audits each one. Does not save.</summary>
    public static async Task<int> WithdrawAsync(
        IQueryable<TransportOrderDocument> published, IAuditService auditService,
        Guid? previousCustomerId, Guid newCustomerId, CancellationToken cancellationToken)
    {
        var documents = await published.ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            document.CustomerVisible = false;
            await auditService.RecordAsync(EntityType, document.Id.ToString(), AuditAction,
                new { CustomerVisible = true },
                new
                {
                    CustomerVisible = false,
                    Reason = "CustomerChanged",
                    PreviousCustomerId = previousCustomerId,
                    NewCustomerId = newCustomerId,
                    document.TransportOrderId,
                    document.DossierId,
                },
                cancellationToken);
        }

        return documents.Count;
    }
}
