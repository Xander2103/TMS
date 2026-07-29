using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Scanning.Entities;
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
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public DashboardService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IFleetDashboardService fleetDashboardService,
        ITripService tripService,
        TimeProvider timeProvider,
        ICurrentUserContext currentUser,
        IPermissionAuthorizationService authorization)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fleetDashboardService = fleetDashboardService;
        _tripService = tripService;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _authorization = authorization;
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
                             && v.OperationalStatus == VehicleOperationalStatus.Available, cancellationToken);

        // Qualification expiry alerts (30-day window; suspended/rejected excluded by expiry logic).
        var expiryLimit = today.AddDays(30);
        var qualificationsExpiring = await _dbContext.EmployeeQualifications.AsNoTracking()
            .CountAsync(q => q.TenantId == tenantId && q.ExpiryDate != null
                             && q.ExpiryDate >= today && q.ExpiryDate <= expiryLimit, cancellationToken);
        var qualificationsExpired = await _dbContext.EmployeeQualifications.AsNoTracking()
            .CountAsync(q => q.TenantId == tenantId && q.ExpiryDate != null && q.ExpiryDate < today, cancellationToken);

        // Alert cards: incidents, missing PODs, failed scans and overdue maintenance.
        var openIncidentCount = await _dbContext.Incidents.AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId
                             && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress),
                cancellationToken);

        // Completed (not yet invoiced) orders without a current proof of delivery need
        // follow-up before they can be billed.
        var missingPodCount = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Status == TransportOrderStatus.Completed)
            .CountAsync(o => !_dbContext.ProofsOfDelivery
                .Any(p => p.TenantId == tenantId && p.TransportOrderId == o.Id && p.IsCurrent), cancellationToken);

        var weekAgo = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-7);
        var failedScanCount = await _dbContext.ScanEvents.AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId && s.OccurredAt >= weekAgo
                             && (s.Result == ScanResult.UnexpectedItem
                                 || s.Result == ScanResult.WrongItem
                                 || s.Result == ScanResult.DamagedItem), cancellationToken);

        // Overdue = strictly past its date/odometer trigger (the fleet card counts due-soon
        // as well); the shared MaintenanceService rule decides, so no logic is duplicated.
        var openMaintenanceJobs = await (
                from m in _dbContext.MaintenanceRecords.AsNoTracking()
                where m.TenantId == tenantId
                      && (m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress)
                      && (m.ScheduledDate != null || m.OdometerTriggerKm != null)
                join v in _dbContext.Vehicles.AsNoTracking() on m.VehicleId equals v.Id into vehicleGroup
                from v in vehicleGroup.DefaultIfEmpty()
                select new { m.ScheduledDate, m.OdometerTriggerKm, CurrentOdometer = v != null ? (int?)v.OdometerKm : null })
            .ToListAsync(cancellationToken);
        var overdueMaintenanceCount = openMaintenanceJobs.Count(j =>
            MaintenanceService.IsOverdue(j.ScheduledDate, j.OdometerTriggerKm, j.CurrentOdometer, today));

        // Today's planning board (conflicts computed live by the trip service).
        var tripsToday = await _tripService.ListAsync(today, today, null, null, cancellationToken);

        // Personnel notes pinned to the dashboard — only ever surfaced to a caller holding
        // employee_notes.view (defence in depth: the endpoint is also gated by dashboard.view,
        // a distinct, broader permission).
        var pinnedEmployeeNotes = await GetPinnedEmployeeNotesAsync(tenantId, cancellationToken);

        var recentOrders = await (from o in _dbContext.TransportOrders.AsNoTracking()
                                      .Where(o => o.TenantId == tenantId)
                                  join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                                      on o.CustomerId equals c.Id
                                  orderby o.OrderDate descending, o.OrderNumber descending
                                  select new RecentOrderDto(o.Id, o.OrderNumber, o.OrderDate, c.Name, o.Status, o.GoodsDescription ?? string.Empty))
            .Take(RecentOrderCount)
            .ToListAsync(cancellationToken);

        return new DashboardDto(
            CountOf(TransportOrderStatus.Draft, TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed),
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
            qualificationsExpiring,
            qualificationsExpired,
            openIncidentCount,
            missingPodCount,
            failedScanCount,
            overdueMaintenanceCount,
            recentOrders,
            tripsToday,
            pinnedEmployeeNotes);
    }

    private const int PinnedNoteExcerptLength = 160;

    private async Task<IReadOnlyList<PinnedEmployeeNoteDto>> GetPinnedEmployeeNotesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId
            || !await _authorization.UserHasPermissionAsync(userId, PermissionCodes.EmployeeNotesView, cancellationToken))
        {
            return [];
        }

        var rows = await (
                from n in _dbContext.EmployeeNotes.AsNoTracking()
                where n.TenantId == tenantId && n.IsPinnedToDashboard
                join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId)
                    on n.EmployeeId equals e.Id
                orderby n.CreatedAt descending
                select new { n.Id, n.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName, n.Text, n.CreatedAt, n.CreatedByUserId })
            .ToListAsync(cancellationToken);

        var authorIds = rows.Where(r => r.CreatedByUserId is not null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var authorNames = authorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        return rows.Select(r => new PinnedEmployeeNoteDto(
                r.Id, r.EmployeeId, r.EmployeeName,
                r.Text.Length > PinnedNoteExcerptLength ? r.Text[..PinnedNoteExcerptLength] + "…" : r.Text,
                r.CreatedAt,
                r.CreatedByUserId is { } authorId ? authorNames.GetValueOrDefault(authorId) : null))
            .ToList();
    }
}
