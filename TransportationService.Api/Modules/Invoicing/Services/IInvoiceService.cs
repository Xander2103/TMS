using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Invoicing.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceListItemDto>> SearchAsync(
        string? search, InvoiceStatus? status, Guid? customerId, PageRequest page, CancellationToken cancellationToken, string? sort = null, string? dir = null);

    Task<InvoiceDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Completed orders of a customer not yet on a non-cancelled invoice.</summary>
    Task<IReadOnlyList<UninvoicedOrderDto>> ListUninvoicedOrdersAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Priced standalone dossier activities of the customer that are not yet on a live invoice.</summary>
    Task<IReadOnlyList<UninvoicedActivityDto>> ListUninvoicedActivitiesAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Builds a draft invoice from completed orders (+ manual lines); orders move to Invoiced.</summary>
    Task<InvoiceOperationResult> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Draft-only edit; dropping an order-backed line releases that order back to Completed.</summary>
    Task<InvoiceOperationResult> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Draft→Sent→Paid with Cancelled branches; cancelling releases the orders.</summary>
    Task<InvoiceOperationResult> ChangeStatusAsync(Guid id, InvoiceStatus target, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a Draft credit note for a Sent/Paid invoice: lines copied with POSITIVE
    /// amounts (the sign lives in the document Kind), snapshots copied from the credited
    /// document, numbered in the same monthly series with the credit-note prefix.
    /// </summary>
    Task<InvoiceOperationResult> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken);

    /// <summary>ACC-M4: the issue rules, evaluated without issuing; null when the invoice is not this tenant's.</summary>
    Task<InvoiceIssuePreflightDto?> PreflightAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>ACC-H1: the rate a manual line defaults to for this customer; null when the customer is unknown.</summary>
    Task<decimal?> DefaultVatRateForCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    Task<InvoiceOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Draft-only manual number correction (permission + mandatory reason, audited).</summary>
    Task<InvoiceOperationResult> OverrideNumberAsync(Guid id, OverrideInvoiceNumberRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Fills ONLY the missing ledger snapshots of a Sent/Paid invoice from the current
    /// category mapping (never overwrites frozen values) — the remediation for invoices sent
    /// while a mapping was still missing. Audited.
    /// </summary>
    Task<InvoiceOperationResult> CompleteLedgerSnapshotsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Next expected number for an entity + period without claiming it; null when no entity exists.</summary>
    Task<InvoiceNumberPreviewDto?> PreviewNextNumberAsync(Guid? legalEntityId, int? year, int? month, CancellationToken cancellationToken);
}
