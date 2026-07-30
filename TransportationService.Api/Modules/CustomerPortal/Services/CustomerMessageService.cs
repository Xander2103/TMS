using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface ICustomerMessageService
{
    // --- Portal side: customer scope is ALWAYS the caller's own linked customer ---
    Task<PortalResult<IReadOnlyList<CustomerMessageDto>>> ListPortalAsync(Guid? orderId, CancellationToken cancellationToken);
    Task<PortalResult<CustomerMessageDto>> SendPortalAsync(SendCustomerMessageRequest request, CancellationToken cancellationToken);
    Task<PortalResult<PortalUnreadCountDto>> GetPortalUnreadCountAsync(CancellationToken cancellationToken);
    Task<PortalResult<PortalMessageAckDto>> MarkPortalReadAsync(Guid? orderId, CancellationToken cancellationToken);

    // --- Internal side: staff supplies the customer id explicitly ---
    Task<IReadOnlyList<CustomerMessageDto>?> ListForCustomerAsync(Guid customerId, Guid? orderId, CancellationToken cancellationToken);
    /// <summary>
    /// publishNotification=false is used by the order portal-review "request info" action,
    /// which stores its note through this SAME mechanism but publishes its own richer
    /// order_info_requested event (order tokens + reason) instead of the generic
    /// customer_message_reply — avoids sending the customer two separate e-mails for one action.
    /// </summary>
    Task<CustomerMessageDto?> SendToCustomerAsync(
        Guid customerId, SendCustomerMessageRequest request, CancellationToken cancellationToken, bool publishNotification = true);
    Task<int?> GetCustomerUnreadCountAsync(Guid customerId, CancellationToken cancellationToken);
    Task<bool> MarkCustomerReadAsync(Guid customerId, Guid? orderId, CancellationToken cancellationToken);
}

/// <summary>
/// The customer/staff messaging thread behind the portal's Berichten module and the internal
/// customer-detail Berichten tab. A thread is (CustomerId, TransportOrderId?): TransportOrderId
/// null is the customer's general thread, set scopes the thread to one order. No attachments in
/// v1 (see task report). Unread counts are computed from CustomerMessageRead markers, not stored
/// counters, so they self-correct with no background job.
/// </summary>
public class CustomerMessageService : ICustomerMessageService
{
    private const string EntityType = "CustomerMessage";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<CustomerMessageService>? _logger;

    public CustomerMessageService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        INotificationEventService? notificationEvents = null,
        ILogger<CustomerMessageService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    private async Task<(Guid CustomerId, string CustomerName)?> MyCustomerAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        var link = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.CustomerId != null)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                u => u.CustomerId, c => c.Id, (u, c) => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return link is null ? null : (link.Id, link.Name);
    }

    // --- Portal side ---

    public async Task<PortalResult<IReadOnlyList<CustomerMessageDto>>> ListPortalAsync(
        Guid? orderId, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<IReadOnlyList<CustomerMessageDto>>.NoCustomerLink();
        }

        if (orderId is { } id && !await OrderBelongsToCustomerAsync(id, customer.Value.CustomerId, cancellationToken))
        {
            return PortalResult<IReadOnlyList<CustomerMessageDto>>.NotFound();
        }

        var messages = await LoadThreadAsync(customer.Value.CustomerId, orderId, cancellationToken);
        return PortalResult<IReadOnlyList<CustomerMessageDto>>.Success(messages);
    }

    public async Task<PortalResult<CustomerMessageDto>> SendPortalAsync(
        SendCustomerMessageRequest request, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<CustomerMessageDto>.NoCustomerLink();
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return PortalResult<CustomerMessageDto>.Invalid("Een bericht mag niet leeg zijn.");
        }

        if (request.OrderId is { } id && !await OrderBelongsToCustomerAsync(id, customer.Value.CustomerId, cancellationToken))
        {
            return PortalResult<CustomerMessageDto>.NotFound();
        }

        var message = new CustomerMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customer.Value.CustomerId,
            TransportOrderId = request.OrderId,
            AuthorUserId = _currentUserContext.CurrentUserId!.Value,
            AuthorIsStaff = false,
            Body = request.Body.Trim(),
        };
        _dbContext.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, message.Id.ToString(), "SentFromPortal", null,
            new { message.CustomerId, message.TransportOrderId }, cancellationToken);

        await PublishEventAsync(MessageKinds.CustomerMessageReceived, customer.Value.CustomerId, customer.Value.CustomerName,
            message, linkPath: request.OrderId is { } orderId ? $"/orders/{orderId}" : $"/customers/{customer.Value.CustomerId}",
            cancellationToken);

        return PortalResult<CustomerMessageDto>.Success(await MapAsync(message, cancellationToken));
    }

    public async Task<PortalResult<PortalUnreadCountDto>> GetPortalUnreadCountAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalUnreadCountDto>.NoCustomerLink();
        }

        var userId = _currentUserContext.CurrentUserId!.Value;
        var count = await CountUnreadAsync(customer.Value.CustomerId, userId, unreadFromStaff: true, cancellationToken);
        return PortalResult<PortalUnreadCountDto>.Success(new PortalUnreadCountDto(count));
    }

    public async Task<PortalResult<PortalMessageAckDto>> MarkPortalReadAsync(
        Guid? orderId, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalMessageAckDto>.NoCustomerLink();
        }

        if (orderId is { } id && !await OrderBelongsToCustomerAsync(id, customer.Value.CustomerId, cancellationToken))
        {
            return PortalResult<PortalMessageAckDto>.NotFound();
        }

        await MarkReadAsync(customer.Value.CustomerId, orderId, _currentUserContext.CurrentUserId!.Value, cancellationToken);
        return PortalResult<PortalMessageAckDto>.Success(new PortalMessageAckDto());
    }

    // --- Internal side ---

    public async Task<IReadOnlyList<CustomerMessageDto>?> ListForCustomerAsync(
        Guid customerId, Guid? orderId, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        if (orderId is { } id && !await OrderBelongsToCustomerAsync(id, customerId, cancellationToken))
        {
            return null;
        }

        return await LoadThreadAsync(customerId, orderId, cancellationToken);
    }

    public async Task<CustomerMessageDto?> SendToCustomerAsync(
        Guid customerId, SendCustomerMessageRequest request, CancellationToken cancellationToken, bool publishNotification = true)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new Common.DomainValidationException("body", "Een bericht mag niet leeg zijn.");
        }

        if (request.OrderId is { } id && !await OrderBelongsToCustomerAsync(id, customerId, cancellationToken))
        {
            return null;
        }

        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId)
            .Select(c => c.Name).FirstAsync(cancellationToken);

        var message = new CustomerMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
            TransportOrderId = request.OrderId,
            AuthorUserId = _currentUserContext.CurrentUserId!.Value,
            AuthorIsStaff = true,
            Body = request.Body.Trim(),
        };
        _dbContext.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, message.Id.ToString(), "SentFromStaff", null,
            new { message.CustomerId, message.TransportOrderId }, cancellationToken);

        if (publishNotification)
        {
            await PublishEventAsync(MessageKinds.CustomerMessageReply, customerId, customerName, message,
                linkPath: null, cancellationToken);
        }

        return await MapAsync(message, cancellationToken);
    }

    public async Task<int?> GetCustomerUnreadCountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken) || _currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await CountUnreadAsync(customerId, userId, unreadFromStaff: false, cancellationToken);
    }

    public async Task<bool> MarkCustomerReadAsync(Guid customerId, Guid? orderId, CancellationToken cancellationToken)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken) || _currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }

        if (orderId is { } id && !await OrderBelongsToCustomerAsync(id, customerId, cancellationToken))
        {
            return false;
        }

        await MarkReadAsync(customerId, orderId, userId, cancellationToken);
        return true;
    }

    // --- shared helpers ---

    private async Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken) =>
        await _dbContext.Customers.AsNoTracking()
            .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId, cancellationToken);

    private async Task<bool> OrderBelongsToCustomerAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
        await _dbContext.TransportOrders.AsNoTracking()
            .AnyAsync(o => o.TenantId == _tenantContext.TenantId && o.Id == orderId && o.CustomerId == customerId, cancellationToken);

    private async Task<IReadOnlyList<CustomerMessageDto>> LoadThreadAsync(
        Guid customerId, Guid? orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var rows = await _dbContext.CustomerMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId && m.TransportOrderId == orderId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var authorIds = rows.Select(m => m.AuthorUserId).Distinct().ToList();
        var authorNames = authorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var orderNumber = orderId is { } id
            ? await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.Id == id)
                .Select(o => (string?)o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return rows.Select(m => new CustomerMessageDto(
                m.Id, m.TransportOrderId, orderNumber, m.AuthorIsStaff,
                authorNames.TryGetValue(m.AuthorUserId, out var name) && name.Length > 0 ? name : "Onbekend",
                m.Body, m.CreatedAt))
            .ToList();
    }

    private async Task<CustomerMessageDto> MapAsync(CustomerMessage message, CancellationToken cancellationToken)
    {
        var authorName = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.Id == message.AuthorUserId)
            .Select(u => (u.FirstName + " " + u.LastName).Trim())
            .FirstOrDefaultAsync(cancellationToken);
        var orderNumber = message.TransportOrderId is { } id
            ? await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == _tenantContext.TenantId && o.Id == id)
                .Select(o => (string?)o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return new CustomerMessageDto(
            message.Id, message.TransportOrderId, orderNumber, message.AuthorIsStaff,
            string.IsNullOrWhiteSpace(authorName) ? "Onbekend" : authorName, message.Body, message.CreatedAt);
    }

    /// <summary>
    /// Unread = messages authored by "the other side" newer than this user's marker for that
    /// exact thread (no marker yet = every such message counts). Aggregated across every thread
    /// (general + every order) of the customer, matching the nav badge's "anything new" intent.
    /// </summary>
    private async Task<int> CountUnreadAsync(Guid customerId, Guid userId, bool unreadFromStaff, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var messages = await _dbContext.CustomerMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId && m.AuthorIsStaff == unreadFromStaff)
            .Select(m => new { m.TransportOrderId, m.CreatedAt })
            .ToListAsync(cancellationToken);
        if (messages.Count == 0)
        {
            return 0;
        }

        var markers = await _dbContext.CustomerMessageReads.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId && r.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        // Dictionary<TKey> requires a non-null key; Guid.Empty stands in for the general thread
        // (TransportOrderId == null) — no real order ever has an empty id.
        var markerByThread = markers.ToDictionary(m => m.TransportOrderId ?? Guid.Empty, m => m.LastReadAt);

        return messages.Count(m =>
            !markerByThread.TryGetValue(m.TransportOrderId ?? Guid.Empty, out var lastRead) || m.CreatedAt > lastRead);
    }

    private async Task MarkReadAsync(Guid customerId, Guid? orderId, Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var marker = await _dbContext.CustomerMessageReads
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.UserId == userId && r.CustomerId == customerId
                && r.TransportOrderId == orderId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (marker is null)
        {
            _dbContext.Add(new CustomerMessageRead
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, CustomerId = customerId,
                TransportOrderId = orderId, LastReadAt = now,
            });
        }
        else
        {
            marker.LastReadAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishEventAsync(
        string eventKey, Guid customerId, string customerName, CustomerMessage message, string? linkPath,
        CancellationToken cancellationToken)
    {
        if (_notificationEvents is null)
        {
            return;
        }

        try
        {
            var preview = message.Body.Length > 120 ? message.Body[..120] + "…" : message.Body;
            await _notificationEvents.PublishAsync(eventKey, new NotificationEventContext(
                EntityType, message.Id.ToString(),
                new Dictionary<string, string> { ["customerName"] = customerName, ["preview"] = preview })
            {
                CustomerId = customerId,
                LinkPath = linkPath,
                InAppMessage = $"{customerName}: {preview}",
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.", eventKey);
        }
    }
}
