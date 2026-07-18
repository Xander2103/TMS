using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Reporting.Services;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Company dashboard aggregate. Fleet numbers come from the fleet dashboard service and
/// today's trips from the planning board so no domain rule is duplicated here.
/// </summary>
public class DashboardService : IDashboardService
{
    private const int RecentOrderCount = 5;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFleetDashboardService _fleetDashboardService;
    private readonly ITripService _tripService;
    private readonly TimeProvider _timeProvider;

    public DashboardService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IFleetDashboardService fleetDashboardService,
        ITripService tripService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fleetDashboardService = fleetDashboardService;
        _tripService = tripService;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Orders
        var orderCounts = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int CountOf(params TransportOrderStatus[] statuses) =>
            orderCounts.Where(c => statuses.Contains(c.Status)).Sum(c => c.Count);

        var completedThisMonth = await _dbContext.TransportOrders.AsNoTracking()
            .CountAsync(o => o.TenantId == tenantId
                             && (o.Status == TransportOrderStatus.Completed || o.Status == TransportOrderStatus.Invoiced)
                             && o.OrderDate >= monthStart && o.OrderDate <= today, cancellationToken);

        // Invoices: bounded in-memory sums over the relevant lines.
        var monthInvoiceIds = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Cancelled
                        && i.InvoiceDate >= monthStart && i.InvoiceDate <= today)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var outstandingInvoices = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == InvoiceStatus.Sent)
            .Select(i => new { i.Id, i.DueDate })
            .ToListAsync(cancellationToken);

        var relevantInvoiceIds = monthInvoiceIds.Concat(outstandingInvoices.Select(i => i.Id)).Distinct().ToList();
        var lines = relevantInvoiceIds.Count == 0
            ? []
            : await _dbContext.InvoiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && relevantInvoiceIds.Contains(l.InvoiceId))
                .Select(l => new { l.InvoiceId, l.Quantity, l.UnitPrice, l.VatRatePercent })
                .ToListAsync(cancellationToken);
        var totalsByInvoice = lines
            .GroupBy(l => l.InvoiceId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(l => l.Quantity * l.UnitPrice * (1 + l.VatRatePercent / 100m)), 2));

        var revenueThisMonth = monthInvoiceIds.Sum(id => totalsByInvoice.GetValueOrDefault(id));
        var outstanding = outstandingInvoices.Sum(i => totalsByInvoice.GetValueOrDefault(i.Id));
        var overdueCount = outstandingInvoices.Count(i => i.DueDate < today);

        // Availability & fleet
        var driversAbsentToday = await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Status == AbsenceStatus.Approved
                        && a.StartDate <= today && a.EndDate >= today)
            .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == tenantId),
                a => a.EmployeeId, d => d.EmployeeId, (a, d) => d.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var fleet = await _fleetDashboardService.GetAsync(cancellationToken);
        var vehiclesAvailable = await _dbContext.Vehicles.AsNoTracking()
            .CountAsync(v => v.TenantId == tenantId && v.IsActive
                             && v.OperationalStatus == VehicleOperationalStatus.Active, cancellationToken);

        // Today's planning board (conflicts computed live by the trip service).
        var tripsToday = await _tripService.ListAsync(today, today, null, null, cancellationToken);

        var recentOrders = await (from o in _dbContext.TransportOrders.AsNoTracking()
                                      .Where(o => o.TenantId == tenantId)
                                  join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                                      on o.CustomerId equals c.Id
                                  orderby o.OrderDate descending, o.OrderNumber descending
                                  select new RecentOrderDto(o.Id, o.OrderNumber, o.OrderDate, c.Name, o.Status, o.GoodsDescription))
            .Take(RecentOrderCount)
            .ToListAsync(cancellationToken);

        return new DashboardDto(
            CountOf(TransportOrderStatus.Draft, TransportOrderStatus.Confirmed),
            CountOf(TransportOrderStatus.Planned, TransportOrderStatus.InProgress),
            completedThisMonth,
            tripsToday.Count,
            tripsToday.Count(t => t.Status == Planning.Entities.TripStatus.InProgress),
            tripsToday.Count(t => t.BlockingConflictCount > 0),
            revenueThisMonth,
            outstanding,
            overdueCount,
            driversAbsentToday,
            vehiclesAvailable,
            fleet.MaintenanceDueCount,
            fleet.InspectionsDueCount,
            fleet.DocumentsExpiringCount,
            fleet.OpenDamageCount,
            recentOrders,
            tripsToday);
    }
}
