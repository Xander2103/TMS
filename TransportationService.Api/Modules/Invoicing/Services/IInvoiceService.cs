using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;

namespace TransportationService.Api.Modules.Invoicing.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceListItemDto>> SearchAsync(
        string? search, InvoiceStatus? status, Guid? customerId, PageRequest page, CancellationToken cancellationToken);

    Task<InvoiceDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Completed orders of a customer not yet on a non-cancelled invoice.</summary>
    Task<IReadOnlyList<UninvoicedOrderDto>> ListUninvoicedOrdersAsync(Guid customerId, CancellationToken cancellationToken);

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
    Task<InvoiceOperationResult> CreateCreditNoteAsync(Guid id, CancellationToken cancellationToken);

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
