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

    /// <summary>Guarded workflow transition (e.g. Draft -> Confirmed requires loading + unloading stops).</summary>
    Task<TransportOrderOperationResult> ChangeStatusAsync(Guid id, TransportOrderStatus target, CancellationToken cancellationToken);

    /// <summary>Only Draft and Cancelled orders can be deleted (soft).</summary>
    Task<TransportOrderOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
