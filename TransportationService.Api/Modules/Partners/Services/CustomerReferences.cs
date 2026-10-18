using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>
/// MD-B2 (master-data acceptance 2026-10-18, BLOCKER): a customer with orders, dossiers, invoices,
/// credit notes and CMRs could be deleted — the order showed a blank customer, the dossier "Geen
/// klant", Peppol validation of an issued invoice failed and the CMR lost its party. Everything
/// below is history or live operational work; a customer that any of it points at is never
/// destructively deleted — it is deactivated. Contacts, address links, communication rules,
/// allowed legal entities, PO registry and pricing configuration are the customer's own
/// configuration and do not block deleting an UNUSED customer.
/// </summary>
public static class CustomerReferences
{
    public const string DeactivateHint =
        "Gebruik \"Deactiveren\" om de klant niet langer beschikbaar te maken voor nieuwe opdrachten; de historiek blijft bewaard.";

    public static async Task<ReferenceReport> ForCustomerAsync(
        TransportationDbContext db, Guid tenantId, Guid customerId, CancellationToken cancellationToken)
    {
        var orderIds = db.TransportOrders.Where(o => o.TenantId == tenantId && o.CustomerId == customerId).Select(o => o.Id);
        var invoices = db.Invoices.Where(i => i.TenantId == tenantId && i.CustomerId == customerId);
        return new ReferenceReport()
            .Add("orders", await orderIds.CountAsync(cancellationToken), "opdracht", "opdrachten")
            .Add("dossiers", await db.TransportDossiers.CountAsync(d => d.TenantId == tenantId && d.CustomerId == customerId, cancellationToken),
                "dossier", "dossiers")
            .Add("invoices", await invoices.CountAsync(i => i.Kind == InvoiceKind.Invoice, cancellationToken), "factuur", "facturen")
            .Add("creditNotes", await invoices.CountAsync(i => i.Kind == InvoiceKind.CreditNote, cancellationToken), "creditnota", "creditnota's")
            .Add("transportDocuments", await db.IssuedTransportDocuments.CountAsync(
                    d => d.TenantId == tenantId && orderIds.Contains(d.TransportOrderId), cancellationToken),
                "transportdocument", "transportdocumenten")
            .Add("incidents", await db.Incidents.CountAsync(i => i.TenantId == tenantId && i.CustomerId == customerId, cancellationToken),
                "afwijking", "afwijkingen")
            .Add("portalUsers", await db.Users.CountAsync(u => u.TenantId == tenantId && u.CustomerId == customerId, cancellationToken),
                "portaalgebruiker", "portaalgebruikers")
            .Add("messages", await db.CustomerMessages.CountAsync(m => m.TenantId == tenantId && m.CustomerId == customerId, cancellationToken),
                "klantbericht", "klantberichten")
            .Add("imports", await db.OrderImportBatches.CountAsync(b => b.TenantId == tenantId && b.CustomerId == customerId, cancellationToken),
                "orderimport", "orderimports");
    }
}
