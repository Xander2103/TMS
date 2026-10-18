using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface IPortalDashboardService
{
    Task<PortalResult<PortalDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Portal landing-page summary: active orders, upcoming deliveries (next 7 days), open
/// customer-visible exceptions, unread messages, recent invoices and active announcements.
/// Pure read model — composes the other portal services rather than duplicating their logic.
/// </summary>
public class PortalDashboardService : IPortalDashboardService
{
    /// <summary>Internal statuses that can still project to an "active" customer status (a Draft only
    /// when it is a portal request sent back for corrections — the projection decides).</summary>
    private static readonly TransportOrderStatus[] OpenStatuses =
        [TransportOrderStatus.Draft, TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned, TransportOrderStatus.InProgress];
    /// <summary>A stop whose latest execution reached one of these is no longer "upcoming", POD or not.</summary>
    private static readonly StopExecutionStatus[] FinishedStopStatuses =
        [StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted, StopExecutionStatus.Failed, StopExecutionStatus.Skipped];
    private static readonly ExecutionExceptionStatus[] ClosedExceptionStatuses =
        [ExecutionExceptionStatus.Resolved, ExecutionExceptionStatus.Rejected];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ICustomerMessageService _messageService;
    private readonly IInvoiceService _invoiceService;
    private readonly IPortalAnnouncementService _announcementService;
    private readonly TimeProvider _timeProvider;
    private readonly IPermissionAuthorizationService? _authorization;

    public PortalDashboardService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        ICustomerMessageService messageService, IInvoiceService invoiceService,
        IPortalAnnouncementService announcementService, TimeProvider timeProvider,
        IPermissionAuthorizationService? authorization = null)
    {
        _authorization = authorization;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _messageService = messageService;
        _invoiceService = invoiceService;
        _announcementService = announcementService;
        _timeProvider = timeProvider;
    }

    public async Task<PortalResult<PortalDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return PortalResult<PortalDashboardDto>.NoCustomerLink();
        }

        var customerId = await PortalCustomerResolver.ResolveCustomerIdAsync(
            _dbContext, _tenantContext.TenantId, userId, cancellationToken);
        if (customerId is null)
        {
            return PortalResult<PortalDashboardDto>.NoCustomerLink();
        }

        var tenantId = _tenantContext.TenantId;
        var id = customerId.Value;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var horizon = now.AddDays(7);

        // One projection for every count on this page: internal drafts never count, a portal
        // request sent back for corrections still does ("Wordt beoordeeld").
        var openOrders = (await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.CustomerId == id && OpenStatuses.Contains(o.Status))
                .Select(o => new { o.Id, o.Status, o.OrderNumber })
                .ToListAsync(cancellationToken));
        var projection = await PortalOrderStatusRules.ProjectAsync(
            _dbContext, tenantId, openOrders.Select(o => (o.Id, o.Status)).ToList(), cancellationToken);
        var activeOrders = projection.Count(p => PortalCustomerStatuses.Active.Contains(p.Value.CustomerStatus));
        var awaitingReply = openOrders
            .Where(o => projection.TryGetValue(o.Id, out var p) && p.CustomerStatus == PortalCustomerStatuses.InfoRequested)
            .Select(o => new PortalActionItemDto(o.Id, o.OrderNumber))
            .ToList();

        // "Aankomende leveringen": only ACCEPTED work (an unanswered request is not a promise) whose
        // delivery stop has not happened yet — no POD and no finished execution (delivered, failed or
        // skipped). One row per order: its next delivery. A stop still pending on a running trip
        // stays listed even if its window already started.
        var upcomingIds = projection
            .Where(p => PortalCustomerStatuses.UpcomingDelivery.Contains(p.Value.CustomerStatus))
            .Select(p => p.Key)
            .ToList();
        var inTransit = projection.Where(p => p.Value.CustomerStatus == PortalCustomerStatuses.InTransit).Select(p => p.Key).ToHashSet();
        var candidateStops = upcomingIds.Count == 0
            ? []
            : await (
                from s in _dbContext.TransportOrderStops.AsNoTracking()
                join o in _dbContext.TransportOrders.AsNoTracking() on s.TransportOrderId equals o.Id
                where s.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == id
                    && upcomingIds.Contains(o.Id)
                    && s.StopType == StopType.Unloading
                    && !_dbContext.ProofsOfDelivery.Any(p => p.TenantId == tenantId && p.TransportOrderStopId == s.Id && p.IsCurrent)
                select new
                {
                    StopId = s.Id, o.Id, o.OrderNumber, s.City,
                    BestFrom = s.ConfirmedFrom ?? s.PlannedFrom ?? s.RequestedFrom,
                    BestTo = s.ConfirmedTo ?? s.PlannedTo ?? s.RequestedTo,
                })
                .ToListAsync(cancellationToken);
        var stopIds = candidateStops.Select(c => c.StopId).ToList();
        var finishedStops = stopIds.Count == 0
            ? []
            : (await _dbContext.StopExecutions.AsNoTracking()
                    .Where(e => e.TenantId == tenantId && stopIds.Contains(e.TransportOrderStopId))
                    .Select(e => new { e.TransportOrderStopId, e.Status, At = e.CompletedAt ?? e.UpdatedAt })
                    .ToListAsync(cancellationToken))
                .GroupBy(e => e.TransportOrderStopId)
                .Where(g => FinishedStopStatuses.Contains(g.OrderByDescending(e => e.At).First().Status))
                .Select(g => g.Key)
                .ToHashSet();
        var upcomingDeliveries = candidateStops
            .Where(c => !finishedStops.Contains(c.StopId) && c.BestFrom != null && c.BestFrom <= horizon)
            .Where(c => (c.BestTo ?? c.BestFrom) >= now || inTransit.Contains(c.Id))
            .GroupBy(c => c.Id)
            .Select(g => g.OrderBy(c => c.BestFrom).First())
            .OrderBy(c => c.BestFrom)
            .Take(20)
            .ToList();

        var problemOrderIds = await (
            from e in _dbContext.ExecutionExceptions.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on e.TransportOrderId equals o.Id
            where e.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == id
                && e.CustomerVisible && !ClosedExceptionStatuses.Contains(e.Status)
            select o.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        var problemOrders = (await PortalOrderStatusRules.VisibleOrderIdsAsync(_dbContext, tenantId, id, problemOrderIds, cancellationToken)).Count;

        // KP-04: what a portal page hides must not travel in its payload either — invoice and message
        // facts are filled only for a user who holds the matching portal right.
        var unreadMessages = 0;
        if (await HasAsync(userId, Identity.PermissionCodes.CustomerPortalMessages, cancellationToken))
        {
            var unreadResult = await _messageService.GetPortalUnreadCountAsync(cancellationToken);
            unreadMessages = unreadResult.Outcome == PortalOutcomeKind.Success ? unreadResult.Value!.Count : 0;
        }

        IReadOnlyList<PortalRecentInvoiceDto> recentInvoices = [];
        if (await HasAsync(userId, Identity.PermissionCodes.CustomerPortalViewInvoices, cancellationToken))
        {
            var invoicePage = await _invoiceService.SearchAsync(null, null, id, PageRequest.Of(1, 50), cancellationToken);
            recentInvoices = invoicePage.Items
                // ACC-H2: the same allow-list as the invoice pages — issued documents only.
                .Where(i => PortalInvoiceService.IsCustomerVisible(i.Status))
                .OrderByDescending(i => i.InvoiceDate)
                .Take(5)
                .Select(i => new PortalRecentInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.Status, i.Total)
                {
                    Kind = i.Kind,
                    Currency = i.Currency,
                })
                .ToList();
        }

        var announcements = await _announcementService.ListActiveAsync(cancellationToken);

        return PortalResult<PortalDashboardDto>.Success(new PortalDashboardDto(
            activeOrders,
            upcomingDeliveries.Select(x => new PortalUpcomingDeliveryDto(x.Id, x.OrderNumber, x.BestFrom!.Value, x.City)).ToList(),
            problemOrders,
            unreadMessages,
            recentInvoices,
            announcements)
        {
            AwaitingYourReply = awaitingReply,
        });
    }

    private async Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        _authorization is not null && await _authorization.UserHasPermissionAsync(userId, permission, cancellationToken);
}
