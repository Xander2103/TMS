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
        Guid customerId, SendCustomerMessageRequest request, CancellationToken cancellationToken, bool publishNotification = true,
        bool isInfoRequest = false);
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
    /// <summary>Mirrors CustomerMessageConfiguration's HasMaxLength(4000) — validated before the
    /// insert so an over-length body fails as a clean 400, never an unhandled DB error.</summary>
    private const int MaxBodyLength = 4000;

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

        var link = await PortalCustomerResolver.ResolveAsync(
            _dbContext, _tenantContext.TenantId, userId, cancellationToken);
        return link is null ? null : (link.CustomerId, link.CustomerName);
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

        if (orderId is { } id && !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, _tenantContext.TenantId, customer.Value.CustomerId, id, cancellationToken))
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

        if (request.Body.Length > MaxBodyLength)
        {
            return PortalResult<CustomerMessageDto>.Invalid($"Een bericht mag maximaal {MaxBodyLength} tekens bevatten.");
        }

        if (request.OrderId is { } id && !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, _tenantContext.TenantId, customer.Value.CustomerId, id, cancellationToken))
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

        // An answer to an open "Info opvragen" question tells the planner so explicitly: the request
        // is back on their desk (the customer-facing status turns "Wordt beoordeeld").
        var answeredOrderNumber = request.OrderId is { } answeredId
            ? await OpenInfoRequestOrderNumberAsync(customer.Value.CustomerId, answeredId, message.CreatedAt, cancellationToken)
            : null;
        await PublishEventAsync(MessageKinds.CustomerMessageReceived, customer.Value.CustomerId, customer.Value.CustomerName,
            message, linkPath: request.OrderId is { } orderId ? $"/orders/{orderId}" : $"/customers/{customer.Value.CustomerId}",
            cancellationToken,
            inAppPrefix: answeredOrderNumber is null ? null : $"antwoordde op de informatievraag voor {answeredOrderNumber}");

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
        var count = await CountUnreadAsync(customer.Value.CustomerId, userId, unreadFromStaff: true, cancellationToken,
            portalVisibleOnly: true);
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

        if (orderId is { } id && !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, _tenantContext.TenantId, customer.Value.CustomerId, id, cancellationToken))
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
        Guid customerId, SendCustomerMessageRequest request, CancellationToken cancellationToken, bool publishNotification = true,
        bool isInfoRequest = false)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new Common.DomainValidationException("body", "Een bericht mag niet leeg zijn.");
        }

        if (request.Body.Length > MaxBodyLength)
        {
            throw new Common.DomainValidationException("body", $"Een bericht mag maximaal {MaxBodyLength} tekens bevatten.");
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
            IsInfoRequest = isInfoRequest && request.OrderId is not null,
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
                m.Body, m.CreatedAt) { IsInfoRequest = m.IsInfoRequest })
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
            string.IsNullOrWhiteSpace(authorName) ? "Onbekend" : authorName, message.Body, message.CreatedAt)
        {
            IsInfoRequest = message.IsInfoRequest,
        };
    }

    /// <summary>
    /// Unread = messages authored by "the other side" newer than this user's marker for that
    /// exact thread (no marker yet = every such message counts). Aggregated across every thread
    /// (general + every order) of the customer, matching the nav badge's "anything new" intent.
    /// </summary>
    private async Task<int> CountUnreadAsync(
        Guid customerId, Guid userId, bool unreadFromStaff, CancellationToken cancellationToken, bool portalVisibleOnly = false)
    {
        var tenantId = _tenantContext.TenantId;
        var messages = await _dbContext.CustomerMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId && m.AuthorIsStaff == unreadFromStaff)
            .Select(m => new { m.TransportOrderId, m.CreatedAt })
            .ToListAsync(cancellationToken);
        if (portalVisibleOnly && messages.Any(m => m.TransportOrderId is not null))
        {
            // The customer cannot open the thread of an order they may not see (an internal draft),
            // so it must not count towards their badge either.
            var visible = await PortalOrderStatusRules.VisibleOrderIdsAsync(_dbContext, tenantId, customerId,
                messages.Where(m => m.TransportOrderId is not null).Select(m => m.TransportOrderId!.Value).Distinct().ToList(),
                cancellationToken);
            messages = messages.Where(m => m.TransportOrderId is null || visible.Contains(m.TransportOrderId.Value)).ToList();
        }

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
        if (marker is not null)
        {
            marker.LastReadAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var created = new CustomerMessageRead
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, CustomerId = customerId,
            TransportOrderId = orderId, LastReadAt = now,
        };
        _dbContext.Add(created);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (Common.Persistence.TenantNumbering.IsUniqueViolation(exception))
        {
            // KP-10: two first-time "mark read" calls for the same thread raced (the page fires one
            // per mount); the other one created the marker. Reading is idempotent: move that marker.
            _dbContext.Entry(created).State = EntityState.Detached;
            var existing = await _dbContext.CustomerMessageReads
                .FirstAsync(r => r.TenantId == tenantId && r.UserId == userId && r.CustomerId == customerId
                    && r.TransportOrderId == orderId, cancellationToken);
            existing.LastReadAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The order number when this order thread has an info request newer than every
    /// customer message before <paramref name="before"/> (i.e. the message being sent answers it).</summary>
    private async Task<string?> OpenInfoRequestOrderNumberAsync(
        Guid customerId, Guid orderId, DateTime before, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var thread = await _dbContext.CustomerMessages.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.CustomerId == customerId && m.TransportOrderId == orderId && m.CreatedAt < before)
            .Select(m => new { m.AuthorIsStaff, m.IsInfoRequest, m.CreatedAt })
            .ToListAsync(cancellationToken);
        var lastQuestion = thread.Where(m => m.IsInfoRequest).Select(m => (DateTime?)m.CreatedAt).Max();
        if (lastQuestion is null || thread.Any(m => !m.AuthorIsStaff && m.CreatedAt > lastQuestion))
        {
            return null;
        }

        return await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task PublishEventAsync(
        string eventKey, Guid customerId, string customerName, CustomerMessage message, string? linkPath,
        CancellationToken cancellationToken, string? inAppPrefix = null)
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
                InAppMessage = inAppPrefix is null ? $"{customerName}: {preview}" : $"{customerName} {inAppPrefix}: {preview}",
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.", eventKey);
        }
    }
}
