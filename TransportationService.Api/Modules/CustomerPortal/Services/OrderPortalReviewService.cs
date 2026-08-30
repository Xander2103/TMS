using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public enum PortalReviewAction
{
    Accept,
    Reject,
    RequestInfo,
}

public record PortalReviewRequest(PortalReviewAction Action, string? Reason);

public interface IOrderPortalReviewService
{
    /// <summary>
    /// Staff decision on a customer-submitted (Submitted-status) order: Accept confirms it,
    /// Reject cancels it with a mandatory reason, RequestInfo leaves it Submitted and posts a
    /// staff message on the order's portal thread asking for more information. All three are
    /// audited (via the underlying order/message operations) and notify the customer.
    /// </summary>
    Task<TransportOrderOperationResult> ReviewAsync(Guid orderId, PortalReviewRequest request, CancellationToken cancellationToken);
}

public class OrderPortalReviewService : IOrderPortalReviewService
{
    private const string EntityType = "TransportOrder";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ITransportOrderService _orderService;
    private readonly ICustomerMessageService _messageService;
    private readonly IAuditService _auditService;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<OrderPortalReviewService>? _logger;

    public OrderPortalReviewService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ITransportOrderService orderService,
        ICustomerMessageService messageService,
        IAuditService auditService,
        INotificationEventService? notificationEvents = null,
        ILogger<OrderPortalReviewService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _orderService = orderService;
        _messageService = messageService;
        _auditService = auditService;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    public async Task<TransportOrderOperationResult> ReviewAsync(
        Guid orderId, PortalReviewRequest request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == _tenantContext.TenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (order.Status != TransportOrderStatus.Submitted)
        {
            return TransportOrderOperationResult.InvalidState(
                $"Alleen ingediende opdrachten kunnen worden beoordeeld (huidige status: '{order.Status}').");
        }

        switch (request.Action)
        {
            case PortalReviewAction.Accept:
                return await _orderService.ChangeStatusAsync(orderId, TransportOrderStatus.Confirmed, cancellationToken);

            case PortalReviewAction.Reject:
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return TransportOrderOperationResult.Invalid("Een reden is verplicht bij het weigeren van een opdracht.");
                }

                return await RejectAsync(order, request.Reason, cancellationToken);

            case PortalReviewAction.RequestInfo:
                return await RequestInfoAsync(order, request.Reason, cancellationToken);

            default:
                return TransportOrderOperationResult.Invalid("Onbekende actie.");
        }
    }

    /// <summary>
    /// Rejects a customer-submitted order AND tells the customer why. `CancellationReason` is a
    /// staff field (planners also type internal motivations into it on the ordinary cancel action)
    /// and H-14 removed it from the portal DTO, so the explanation now travels through the same
    /// customer-facing order thread the RequestInfo branch uses. The message is posted with
    /// publishNotification: false because the cancel already published the richer order_rejected
    /// e-mail — one action, one mail.
    /// </summary>
    private async Task<TransportOrderOperationResult> RejectAsync(
        Orders.Entities.TransportOrder order, string reason, CancellationToken cancellationToken)
    {
        var cancelled = await _orderService.CancelAsync(order.Id, reason, cancellationToken);
        if (cancelled.Outcome != TransportOrderOperationOutcome.Success)
        {
            return cancelled;
        }

        // The cancellation is committed; a failing note must not undo it, but it must be visible
        // in the log because the customer would otherwise be left without an explanation.
        try
        {
            await _messageService.SendToCustomerAsync(
                order.CustomerId,
                new SendCustomerMessageRequest(order.Id,
                    $"Uw opdracht {order.OrderNumber} is geweigerd. Reden: {reason.Trim()}"),
                cancellationToken, publishNotification: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception,
                "Rejection note for order {OrderId} could not be posted; the cancellation itself is already committed.",
                order.Id);
        }

        return cancelled;
    }

    private async Task<TransportOrderOperationResult> RequestInfoAsync(
        Orders.Entities.TransportOrder order, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TransportOrderOperationResult.Invalid("Vermeld welke informatie u nodig heeft van de klant.");
        }

        // Single mechanism: the info request IS a staff message on the order's portal thread —
        // no separate info-request entity. The generic customer_message_reply notification is
        // suppressed here (publishNotification: false) so the customer gets exactly one e-mail,
        // built from the richer order_info_requested template below.
        var sent = await _messageService.SendToCustomerAsync(
            order.CustomerId, new SendCustomerMessageRequest(order.Id, reason), cancellationToken, publishNotification: false);
        if (sent is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "PortalInfoRequested", null,
            new { OrderId = order.Id, order.OrderNumber, Reason = reason.Trim() }, cancellationToken);

        if (_notificationEvents is not null)
        {
            try
            {
                var customerName = await _dbContext.Customers.AsNoTracking()
                    .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
                    .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
                await _notificationEvents.PublishAsync(MessageKinds.OrderInfoRequested, new NotificationEventContext(
                    EntityType, order.Id.ToString(),
                    new Dictionary<string, string>
                    {
                        ["orderNumber"] = order.OrderNumber,
                        ["customerName"] = customerName,
                        ["goodsDescription"] = order.GoodsDescription ?? string.Empty,
                        ["reason"] = reason.Trim(),
                    })
                {
                    CustomerId = order.CustomerId,
                    LinkPath = $"/portal/orders/{order.Id}",
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.",
                    MessageKinds.OrderInfoRequested);
            }
        }

        var detail = await _orderService.GetByIdAsync(order.Id, cancellationToken);
        return detail is null ? TransportOrderOperationResult.NotFound : TransportOrderOperationResult.Success(detail);
    }
}
