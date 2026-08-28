using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

public interface ITransportOrderService
{
    Task<PagedResult<TransportOrderListItemDto>> SearchAsync(
        string? search, TransportOrderStatus? status, Guid? customerId,
        DateOnly? fromDate, DateOnly? toDate, PageRequest page, CancellationToken cancellationToken);

    Task<TransportOrderDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TransportOrderOperationResult> CreateAsync(CreateTransportOrderRequest request, CancellationToken cancellationToken);

    /// <summary>Full update including wholesale stop replacement; only Draft and Confirmed orders are editable.</summary>
    Task<TransportOrderOperationResult> UpdateAsync(Guid id, UpdateTransportOrderRequest request, CancellationToken cancellationToken);

    /// <summary>Guarded workflow transition (e.g. Draft -> Confirmed requires loading + unloading stops).
    /// Cancelling is deliberately NOT reachable here — it is a separate action with its own permission.</summary>
    Task<TransportOrderOperationResult> ChangeStatusAsync(Guid id, TransportOrderStatus target, CancellationToken cancellationToken);

    /// <summary>Cancels the order with a mandatory reason. Allowed from every non-final status.</summary>
    Task<TransportOrderOperationResult> CancelAsync(Guid id, string reason, CancellationToken cancellationToken);

    /// <summary>Sprint 6 (completion): preview of an invoicing-entity change; null when the order is unknown.</summary>
    Task<OrderLegalEntityChangeImpactDto?> PreviewLegalEntityChangeAsync(Guid id, Guid legalEntityId, CancellationToken cancellationToken);

    /// <summary>
    /// Sprint 6 (completion): moves the order to another invoicing entity. Inside the customer's
    /// allowed set always; away from the customer default only with dossiers.override_entity AND
    /// a reason (audited). Refused once the order sits on a sent/booked invoice; draft invoice
    /// lines on a concept of another entity are released so no concept stays incoherent.
    /// </summary>
    Task<TransportOrderOperationResult> ChangeLegalEntityAsync(Guid id, ChangeOrderLegalEntityRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Applies an entity change for one order inside a transaction the CALLER (the dossier)
    /// owns. The dossier already enforced the override right + reason; the financial guards
    /// (sent invoice, policy, draft release) still apply per order. Returns the blocking
    /// reason when refused, null when applied.
    /// </summary>
    Task<string?> ChangeLegalEntityWithinDossierAsync(Guid id, Guid legalEntityId, string? reason, CancellationToken cancellationToken);

    Task<TransportOrderOperationResult> CorrectStatusAsync(Guid id, TransportOrderStatus target, string reason, CancellationToken cancellationToken);

    /// <summary>All orders matching the filters (bounded), for CSV export.</summary>
    Task<IReadOnlyList<TransportOrderListItemDto>> ListForExportAsync(
        string? search, TransportOrderStatus? status, Guid? customerId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the execution plan (confirmed window, bounds, appointment, instructions) of one
    /// stop. Unlike <see cref="UpdateAsync"/> this stays possible while the order is Planned or
    /// InProgress; only final statuses refuse it.
    /// </summary>
    Task<TransportOrderOperationResult> UpdateStopExecutionPlanAsync(
        Guid orderId, Guid stopId, UpdateStopExecutionPlanRequest request, CancellationToken cancellationToken);

    /// <summary>Only Draft and Cancelled orders can be deleted (soft).</summary>
    Task<TransportOrderOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Inline priority change; allowed in every non-final status, audited.</summary>
    Task<TransportOrderOperationResult> ChangePriorityAsync(
        Guid id, OrderPriority priority, CancellationToken cancellationToken);

    /// <summary>
    /// Applies manual corrections/removals/free additions to the persisted pricing lines (spec
    /// ch. 24-26). Blocked while the pricing status is Locked/Invoiced.
    /// </summary>
    Task<TransportOrderOperationResult> SaveOrderPriceLinesAsync(
        Guid orderId, IReadOnlyList<SaveOrderPriceLineRequest> requests, CancellationToken cancellationToken);

    /// <summary>Explicit re-run of the pricing engine (merge-on-recalc). Blocked while Locked/Invoiced.</summary>
    Task<TransportOrderOperationResult> RecalculateOrderPricingAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Pricing status transition (Draft/Reviewed/Locked); Invoiced is set only by invoicing.</summary>
    Task<TransportOrderOperationResult> SetOrderPricingStatusAsync(
        Guid orderId, OrderPricingStatus target, CancellationToken cancellationToken);

    /// <summary>Confirms an unconfirmed (Proposed) extra-time line so it counts in LinesTotal/AgreedPrice.</summary>
    Task<TransportOrderOperationResult> ConfirmOrderPriceLineAsync(
        Guid orderId, Guid lineId, CancellationToken cancellationToken);

    /// <summary>
    /// Wave 2026-08-04 §8/§10: "Prijs bevestigen" — locks the snapshot and stamps who/when.
    /// Refused with unpriced goods unless the dedicated override permission + reason is given.
    /// </summary>
    Task<TransportOrderOperationResult> ConfirmOrderPricingAsync(
        Guid orderId, string? unpricedGoodsReason, CancellationToken cancellationToken);

    /// <summary>Wave 2026-08-04 §8: "Prijs aanpassen" — reopens a confirmed price with a mandatory reason.</summary>
    Task<TransportOrderOperationResult> ReopenOrderPricingAsync(
        Guid orderId, string? reason, CancellationToken cancellationToken);
}
