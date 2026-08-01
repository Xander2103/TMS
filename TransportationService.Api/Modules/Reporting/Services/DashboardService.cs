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
        var monthInvoices = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Cancelled
                        && i.InvoiceDate >= monthStart && i.InvoiceDate <= today)
            .Select(i => new { i.Id, i.Kind })
            .ToListAsync(cancellationToken);
        var outstandingInvoices = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == InvoiceStatus.Sent)
            .Select(i => new { i.Id, i.DueDate, i.Kind })
            .ToListAsync(cancellationToken);

        var relevantInvoiceIds = monthInvoices.Select(i => i.Id).Concat(outstandingInvoices.Select(i => i.Id)).Distinct().ToList();
        var lines = relevantInvoiceIds.Count == 0
            ? []
            : await _dbContext.InvoiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && relevantInvoiceIds.Contains(l.InvoiceId))
                .Select(l => new { l.InvoiceId, l.Quantity, l.UnitPrice, l.VatRatePercent })
                .ToListAsync(cancellationToken);
        var totalsByInvoice = lines
            .GroupBy(l => l.InvoiceId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(l => l.Quantity * l.UnitPrice * (1 + l.VatRatePercent / 100m)), 2));

        // Credit notes carry positive line amounts; their commercial sign is the Kind.
        static decimal Sign(InvoiceKind kind) => kind == InvoiceKind.CreditNote ? -1m : 1m;
        var revenueThisMonth = monthInvoices.Sum(i => Sign(i.Kind) * totalsByInvoice.GetValueOrDefault(i.Id));
        var outstanding = outstandingInvoices.Sum(i => Sign(i.Kind) * totalsByInvoice.GetValueOrDefault(i.Id));
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
            pinnedEmployeeNotes,
            await GetInventorySectionAsync(tenantId, today, cancellationToken),
            await GetTaskSectionAsync(tenantId, cancellationToken),
            await GetUnreadInternalMessagesAsync(tenantId, cancellationToken));
    }

    /// <summary>Inventory tiles, only for holders of inventory.view/manage (frontend also gates).</summary>
    private async Task<InventoryDashboardSectionDto?> GetInventorySectionAsync(
        Guid tenantId, DateOnly today, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId
            || (!await _authorization.UserHasPermissionAsync(userId, PermissionCodes.InventoryView, cancellationToken)
                && !await _authorization.UserHasPermissionAsync(userId, PermissionCodes.InventoryManage, cancellationToken)))
        {
            return null;
        }

        var templates = await _dbContext.IssuedItemTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive && t.StockTrackingEnabled)
            .Select(t => new { t.Id, t.VariantsEnabled, t.CurrentStock, t.LowStockThreshold, t.MinimumStock })
            .ToListAsync(cancellationToken);
        var templateIds = templates.Select(t => t.Id).ToList();
        var variants = await _dbContext.IssuedItemVariants.AsNoTracking()
            .Where(v => v.TenantId == tenantId && templateIds.Contains(v.TemplateId) && v.IsActive)
            .Select(v => new { v.TemplateId, v.CurrentStock, v.LowStockThreshold })
            .ToListAsync(cancellationToken);
        var variantsByTemplate = variants.ToLookup(v => v.TemplateId);

        int low = 0, critical = 0, outOf = 0, negative = 0;
        void Tally(Employees.Entities.InventoryStatus status)
        {
            switch (status)
            {
                case Employees.Entities.InventoryStatus.LowStock: low++; break;
                case Employees.Entities.InventoryStatus.CriticalStock: critical++; break;
                case Employees.Entities.InventoryStatus.OutOfStock: outOf++; break;
                case Employees.Entities.InventoryStatus.NegativeStock: negative++; break;
            }
        }

        foreach (var template in templates)
        {
            if (template.VariantsEnabled)
            {
                foreach (var variant in variantsByTemplate[template.Id])
                {
                    Tally(Employees.Services.InventoryStatusCalculator.Compute(
                        variant.CurrentStock, variant.LowStockThreshold ?? template.LowStockThreshold, template.MinimumStock));
                }
            }
            else
            {
                Tally(Employees.Services.InventoryStatusCalculator.Compute(
                    template.CurrentStock, template.LowStockThreshold, template.MinimumStock));
            }
        }

        var openProposals = await _dbContext.ReorderProposals.AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId
                             && (p.Status == Employees.Entities.ReorderProposalStatus.Proposed
                                 || p.Status == Employees.Entities.ReorderProposalStatus.Reviewed
                                 || p.Status == Employees.Entities.ReorderProposalStatus.Approved
                                 || p.Status == Employees.Entities.ReorderProposalStatus.Ordered), cancellationToken);
        var overdueReturns = await _dbContext.EmployeeIssuedItems.AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId && i.Status == Employees.Entities.IssuedItemStatus.Issued
                             && i.ExpectedReturnDate != null && i.ExpectedReturnDate < today, cancellationToken);
        return new InventoryDashboardSectionDto(low, critical, outOf, negative, openProposals, overdueReturns);
    }

    /// <summary>Personal task tiles for anyone with a task-view permission; team tiles need view_team/all.</summary>
    private async Task<TaskDashboardSectionDto?> GetTaskSectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId)
        {
            return null;
        }

        var viewOwn = await _authorization.UserHasPermissionAsync(userId, PermissionCodes.TasksViewOwn, cancellationToken);
        var viewTeam = await _authorization.UserHasPermissionAsync(userId, PermissionCodes.TasksViewTeam, cancellationToken);
        var viewAll = await _authorization.UserHasPermissionAsync(userId, PermissionCodes.TasksViewAll, cancellationToken);
        if (!viewOwn && !viewTeam && !viewAll)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var todayEnd = now.Date.AddDays(1);
        var myEmployeeId = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == tenantId)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        var open = Tasks.Services.TaskStatusMachine.OpenStatuses;
        int myOpen = 0, myDueToday = 0, myOverdue = 0;
        if (myEmployeeId is { } mine)
        {
            var myTasks = await _dbContext.EmployeeTasks.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.AssignedEmployeeId == mine && open.Contains(t.Status))
                .Select(t => new { t.DueAt })
                .ToListAsync(cancellationToken);
            myOpen = myTasks.Count;
            myDueToday = myTasks.Count(t => t.DueAt >= now && t.DueAt < todayEnd);
            myOverdue = myTasks.Count(t => t.DueAt is { } due && due < now);
        }

        var myToAcknowledge = await _dbContext.Set<Notifications.Entities.InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId && r.AcknowledgedAt == null)
            .Join(_dbContext.Set<Notifications.Entities.InternalMessage>().AsNoTracking()
                    .Where(m => m.RequiresAcknowledgement && m.CancelledAt == null
                                && (m.VisibleFrom == null || m.VisibleFrom <= now)
                                && (m.ExpiresAt == null || m.ExpiresAt > now)),
                r => r.MessageId, m => m.Id, (r, m) => r.Id)
            .CountAsync(cancellationToken);

        int? teamOpen = null, teamOverdue = null, teamBlocked = null, teamWaitingReview = null;
        if (viewAll || viewTeam)
        {
            IQueryable<Tasks.Entities.EmployeeTask> teamTasks = _dbContext.EmployeeTasks.AsNoTracking()
                .Where(t => t.TenantId == tenantId && open.Contains(t.Status));
            if (!viewAll)
            {
                var myDepartment = myEmployeeId is { } employeeId
                    ? await _dbContext.Employees.AsNoTracking()
                        .Where(e => e.TenantId == tenantId && e.Id == employeeId)
                        .Select(e => e.DepartmentId)
                        .FirstOrDefaultAsync(cancellationToken)
                    : null;
                if (myDepartment is { } department)
                {
                    var teamIds = _dbContext.Employees.AsNoTracking()
                        .Where(e => e.TenantId == tenantId && e.DepartmentId == department)
                        .Select(e => e.Id);
                    teamTasks = teamTasks.Where(t => teamIds.Contains(t.AssignedEmployeeId));
                }
                else
                {
                    teamTasks = teamTasks.Where(t => false);
                }
            }

            var rows = await teamTasks.Select(t => new { t.Status, t.DueAt }).ToListAsync(cancellationToken);
            teamOpen = rows.Count;
            teamOverdue = rows.Count(t => t.DueAt is { } due && due < now);
            teamBlocked = rows.Count(t => t.Status == Tasks.Entities.EmployeeTaskStatus.Blocked);
            teamWaitingReview = rows.Count(t => t.Status == Tasks.Entities.EmployeeTaskStatus.WaitingForReview);
        }

        return new TaskDashboardSectionDto(
            myOpen, myDueToday, myOverdue, myToAcknowledge,
            teamOpen, teamOverdue, teamBlocked, teamWaitingReview);
    }

    private async Task<int> GetUnreadInternalMessagesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await _dbContext.Set<Notifications.Entities.InternalMessageRecipient>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId && r.ReadAt == null)
            .Join(_dbContext.Set<Notifications.Entities.InternalMessage>().AsNoTracking()
                    .Where(m => m.CancelledAt == null
                                && (m.VisibleFrom == null || m.VisibleFrom <= now)
                                && (m.ExpiresAt == null || m.ExpiresAt > now)),
                r => r.MessageId, m => m.Id, (r, m) => r.Id)
            .CountAsync(cancellationToken);
    }

    private const int PinnedNoteExcerptLength = 160;

    private async Task<IReadOnlyList<PinnedEmployeeNoteDto>> GetPinnedEmployeeNotesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } userId
            || !await _authorization.UserHasPermissionAsync(userId, PermissionCodes.EmployeeNotesView, cancellationToken))
        {
            return [];
        }

        // PinnedAt/PinnedByUserId (not CreatedAt/CreatedByUserId) drive both the sort order and
        // the displayed attribution: an old note pinned just now must rise to the top and show
        // who pinned it, not who originally wrote it.
        var rows = await (
                from n in _dbContext.EmployeeNotes.AsNoTracking()
                where n.TenantId == tenantId && n.IsPinnedToDashboard && n.PinnedAt != null
                join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId)
                    on n.EmployeeId equals e.Id
                orderby n.PinnedAt descending
                select new { n.Id, n.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName, n.Text, n.PinnedAt, n.PinnedByUserId })
            .ToListAsync(cancellationToken);

        var authorIds = rows.Where(r => r.PinnedByUserId is not null).Select(r => r.PinnedByUserId!.Value).Distinct().ToList();
        var authorNames = authorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        return rows.Select(r => new PinnedEmployeeNoteDto(
                r.Id, r.EmployeeId, r.EmployeeName,
                r.Text.Length > PinnedNoteExcerptLength ? r.Text[..PinnedNoteExcerptLength] + "…" : r.Text,
                r.PinnedAt!.Value,
                r.PinnedByUserId is { } authorId ? authorNames.GetValueOrDefault(authorId) : null))
            .ToList();
    }
}
