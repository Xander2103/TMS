using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// Hourly tenant-aware inventory sweep: reconciles stock-status alerts (safety net on top of
/// the mutation-driven sync), announces return deadlines, flags inactive employees holding
/// material, and raises the inventory escalations. All notifications ride
/// Notification.DedupeKey, so repeated runs never spam.
/// </summary>
public sealed class InventorySweepHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventorySweepHostedService> _logger;

    public InventorySweepHostedService(IServiceScopeFactory scopeFactory, ILogger<InventorySweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<InventorySweepWorker>();
                await worker.SweepAllTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Inventory sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class InventorySweepWorker
{
    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InventorySweepWorker> _logger;

    public InventorySweepWorker(TransportationDbContext dbContext, TimeProvider timeProvider, ILogger<InventorySweepWorker> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task SweepAllTenantsAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await SweepTenantAsync(tenantId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One bad tenant never blocks the rest of the sweep.
                _logger.LogError(exception, "Inventory sweep failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    public async Task SweepTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Manual composition with a fixed tenant context: the request-scoped accessors are
        // fail-closed outside a request (established job pattern, see ExpiryNotificationProducer).
        var tenant = new DevTenantContext(tenantId);
        var notifications = new NotificationService(_dbContext, tenant, new DevCurrentUserContext(null), _timeProvider);
        var alerts = new InventoryAlertService(_dbContext, tenant, notifications, _timeProvider);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        await ReconcileStatusAlertsAsync(tenantId, alerts, cancellationToken);
        await AnnounceReturnsAsync(tenantId, notifications, now, today, cancellationToken);
        await EscalateAsync(tenantId, notifications, now, today, cancellationToken);
    }

    private async Task ReconcileStatusAlertsAsync(
        Guid tenantId, InventoryAlertService alerts, CancellationToken cancellationToken)
    {
        var templates = await _dbContext.IssuedItemTemplates
            .Where(t => t.TenantId == tenantId && t.IsActive && t.StockTrackingEnabled)
            .ToListAsync(cancellationToken);
        var templateIds = templates.Select(t => t.Id).ToList();
        var variants = await _dbContext.IssuedItemVariants
            .Where(v => v.TenantId == tenantId && templateIds.Contains(v.TemplateId) && v.IsActive)
            .ToListAsync(cancellationToken);
        var byTemplate = variants.ToLookup(v => v.TemplateId);
        foreach (var template in templates)
        {
            if (template.VariantsEnabled)
            {
                foreach (var variant in byTemplate[template.Id])
                {
                    await alerts.SyncAsync(template, variant, cancellationToken);
                }
            }
            else
            {
                await alerts.SyncAsync(template, null, cancellationToken);
            }
        }
    }

    private async Task AnnounceReturnsAsync(
        Guid tenantId, NotificationService notifications, DateTime now, DateOnly today, CancellationToken cancellationToken)
    {
        var soonThreshold = today.AddDays(2);
        var loans = await _dbContext.EmployeeIssuedItems
            .Where(i => i.TenantId == tenantId && i.Status == IssuedItemStatus.Issued && i.ExpectedReturnDate != null)
            .Join(_dbContext.Employees.Where(e => e.TenantId == tenantId),
                i => i.EmployeeId, e => e.Id,
                (i, e) => new { Item = i, EmployeeName = e.FirstName + " " + e.LastName, e.IsActive })
            .ToListAsync(cancellationToken);

        foreach (var loan in loans)
        {
            var due = loan.Item.ExpectedReturnDate!.Value;
            if (due >= today && due <= soonThreshold)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    PermissionCodes.IssuedItemsManage, "inventory_return_due", "Retour binnenkort",
                    $"{loan.Item.NameSnapshot} van {loan.EmployeeName} moet terug op {due:dd/MM/yyyy}.",
                    $"/inventory", cancellationToken,
                    new NotificationOptions(DedupeKey: $"return_due:{loan.Item.Id}"));
            }
            else if (due < today && loan.Item.OverdueNotifiedAt is null)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    PermissionCodes.IssuedItemsManage, "inventory_return_overdue", "Retour te laat",
                    $"{loan.Item.NameSnapshot} van {loan.EmployeeName} had terug gemoeten op {due:dd/MM/yyyy}.",
                    $"/inventory", cancellationToken,
                    new NotificationOptions(DedupeKey: $"return_overdue:{loan.Item.Id}"));
                loan.Item.OverdueNotifiedAt = now;
            }
        }

        // Employees who left with material still out.
        var allInactiveHolders = await _dbContext.EmployeeIssuedItems
            .Where(i => i.TenantId == tenantId && i.Status == IssuedItemStatus.Issued)
            .Join(_dbContext.Employees.Where(e => e.TenantId == tenantId && !e.IsActive),
                i => i.EmployeeId, e => e.Id,
                (i, e) => new { i.EmployeeId, EmployeeName = e.FirstName + " " + e.LastName })
            .ToListAsync(cancellationToken);
        foreach (var group in allInactiveHolders.GroupBy(h => h.EmployeeId))
        {
            await notifications.NotifyPermissionHoldersAsync(
                PermissionCodes.IssuedItemsManage, "inventory_return_overdue", "Materiaal bij vertrokken medewerker",
                $"{group.First().EmployeeName} is niet langer actief maar heeft nog {group.Count()} uitgereikte item(s).",
                $"/employees/{group.Key}?tab=bedrijfsmiddelen", cancellationToken,
                new NotificationOptions(DedupeKey: $"loan_inactive:{group.Key}"));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EscalateAsync(
        Guid tenantId, NotificationService notifications, DateTime now, DateOnly today, CancellationToken cancellationToken)
    {
        var policies = await ReadPoliciesAsync(tenantId, cancellationToken);

        if (policies.TryGetValue(EscalationKind.NegativeStockUnresolved, out var negative) && negative.IsActive)
        {
            var cutoff = now.AddHours(-negative.DelayHours);
            var stale = await _dbContext.InventoryAlerts.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Status == InventoryAlertStatus.Active
                            && a.Kind == InventoryStatus.NegativeStock && a.ActivatedAt <= cutoff)
                .ToListAsync(cancellationToken);
            foreach (var alert in stale)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    negative.TargetPermissionCode, "escalation_raised", "Escalatie: negatieve voorraad",
                    $"Een negatieve voorraad (stand {alert.StockSnapshot}) staat al langer dan {negative.DelayHours} uur open.",
                    $"/settings/issued-item-templates/{alert.TemplateId}?tab=voorraad", cancellationToken,
                    new NotificationOptions(DedupeKey: $"escalation:negative_stock:{alert.Id}"));
            }
        }

        if (policies.TryGetValue(EscalationKind.CriticalStockUnhandled, out var critical) && critical.IsActive)
        {
            var cutoff = now.AddHours(-critical.DelayHours);
            var openProposalTargets = await _dbContext.ReorderProposals.AsNoTracking()
                .Where(p => p.TenantId == tenantId
                            && (p.Status == ReorderProposalStatus.Proposed || p.Status == ReorderProposalStatus.Reviewed
                                || p.Status == ReorderProposalStatus.Approved || p.Status == ReorderProposalStatus.Ordered))
                .Select(p => new { p.TemplateId, p.VariantId })
                .ToListAsync(cancellationToken);
            var handled = openProposalTargets.Select(p => (p.TemplateId, p.VariantId)).ToHashSet();
            var stale = await _dbContext.InventoryAlerts.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Status == InventoryAlertStatus.Active
                            && a.Kind == InventoryStatus.CriticalStock && a.ActivatedAt <= cutoff)
                .ToListAsync(cancellationToken);
            foreach (var alert in stale.Where(a => !handled.Contains((a.TemplateId, a.VariantId))))
            {
                await notifications.NotifyPermissionHoldersAsync(
                    critical.TargetPermissionCode, "escalation_raised", "Escalatie: kritieke voorraad onbehandeld",
                    $"Een kritieke voorraad (stand {alert.StockSnapshot}) is na {critical.DelayHours} uur nog niet behandeld en heeft geen bestelvoorstel.",
                    $"/settings/issued-item-templates/{alert.TemplateId}?tab=voorraad", cancellationToken,
                    new NotificationOptions(DedupeKey: $"escalation:critical_stock:{alert.Id}"));
            }
        }

        if (policies.TryGetValue(EscalationKind.ReturnOverdue, out var returns) && returns.IsActive)
        {
            var cutoffDate = today.AddDays(-Math.Max(1, returns.DelayHours / 24));
            var stale = await _dbContext.EmployeeIssuedItems.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Status == IssuedItemStatus.Issued
                            && i.ExpectedReturnDate != null && i.ExpectedReturnDate <= cutoffDate)
                .ToListAsync(cancellationToken);
            foreach (var item in stale)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    returns.TargetPermissionCode, "escalation_raised", "Escalatie: retour te laat",
                    $"{item.NameSnapshot} is meer dan {returns.DelayHours} uur over de retourdatum ({item.ExpectedReturnDate:dd/MM/yyyy}).",
                    "/inventory", cancellationToken,
                    new NotificationOptions(DedupeKey: $"escalation:return:{item.Id}"));
            }
        }
    }

    private async Task<Dictionary<EscalationKind, EscalationPolicy>> ReadPoliciesAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Set<EscalationPolicy>().AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var result = rows.ToDictionary(p => p.Kind);
        foreach (var (kind, defaults) in EscalationPolicyService.Defaults)
        {
            if (!result.ContainsKey(kind))
            {
                result[kind] = new EscalationPolicy
                {
                    TenantId = tenantId, Kind = kind, DelayHours = defaults.DelayHours,
                    TargetPermissionCode = defaults.Target, IsActive = defaults.Active,
                };
            }
        }

        return result;
    }
}
