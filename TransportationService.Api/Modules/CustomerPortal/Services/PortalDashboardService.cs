using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;
using TransportationService.Api.Modules.Orders.Entities;
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
    private static readonly TransportOrderStatus[] ActiveStatuses =
        [TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned, TransportOrderStatus.InProgress];
    private static readonly ExecutionExceptionStatus[] ClosedExceptionStatuses =
        [ExecutionExceptionStatus.Resolved, ExecutionExceptionStatus.Rejected];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ICustomerMessageService _messageService;
    private readonly IInvoiceService _invoiceService;
    private readonly IPortalAnnouncementService _announcementService;
    private readonly TimeProvider _timeProvider;

    public PortalDashboardService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        ICustomerMessageService messageService, IInvoiceService invoiceService,
        IPortalAnnouncementService announcementService, TimeProvider timeProvider)
    {
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

        var activeOrders = await _dbContext.TransportOrders.AsNoTracking()
            .CountAsync(o => o.TenantId == tenantId && o.CustomerId == id && ActiveStatuses.Contains(o.Status), cancellationToken);

        var upcomingDeliveries = await (
            from s in _dbContext.TransportOrderStops.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on s.TransportOrderId equals o.Id
            where s.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == id
                && s.StopType == StopType.Unloading && o.Status != TransportOrderStatus.Cancelled
            select new
            {
                o.Id, o.OrderNumber, s.City,
                BestTime = s.ConfirmedFrom ?? s.PlannedFrom ?? s.RequestedFrom,
            })
            .Where(x => x.BestTime != null && x.BestTime >= now && x.BestTime <= horizon)
            .OrderBy(x => x.BestTime)
            .Take(20)
            .ToListAsync(cancellationToken);

        var problemOrders = await (
            from e in _dbContext.ExecutionExceptions.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on e.TransportOrderId equals o.Id
            where e.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == id
                && e.CustomerVisible && !ClosedExceptionStatuses.Contains(e.Status)
            select o.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var unreadResult = await _messageService.GetPortalUnreadCountAsync(cancellationToken);
        var unreadMessages = unreadResult.Outcome == PortalOutcomeKind.Success ? unreadResult.Value!.Count : 0;

        var invoicePage = await _invoiceService.SearchAsync(null, null, id, PageRequest.Of(1, 50), cancellationToken);
        var recentInvoices = invoicePage.Items
            .Where(i => i.Status != InvoiceStatus.Draft)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(5)
            .Select(i => new PortalRecentInvoiceDto(i.Id, i.InvoiceNumber, i.InvoiceDate, i.Status, i.Total))
            .ToList();

        var announcements = await _announcementService.ListActiveAsync(cancellationToken);

        return PortalResult<PortalDashboardDto>.Success(new PortalDashboardDto(
            activeOrders,
            upcomingDeliveries.Select(x => new PortalUpcomingDeliveryDto(x.Id, x.OrderNumber, x.BestTime!.Value, x.City)).ToList(),
            problemOrders,
            unreadMessages,
            recentInvoices,
            announcements));
    }
}
