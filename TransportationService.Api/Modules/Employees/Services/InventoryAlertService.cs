using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public interface IInventoryAlertService
{
    /// <summary>
    /// Recomputes the stock status of the target and reconciles its <see cref="InventoryAlert"/>
    /// row + notifications: a transition into a (different) non-normal status notifies the
    /// inventory.low_stock_alerts holders once; recovery resolves alert and notifications so a
    /// later new drop notifies again. Call AFTER SaveChanges of the stock mutation — this
    /// service saves independently and must never sit inside the stock transaction.
    /// </summary>
    Task SyncAsync(IssuedItemTemplate template, IssuedItemVariant? variant, CancellationToken cancellationToken);
}

public class InventoryAlertService : IInventoryAlertService
{
    public static string TypeFor(InventoryStatus status) => status switch
    {
        InventoryStatus.LowStock => "inventory_status_low",
        InventoryStatus.CriticalStock => "inventory_status_critical",
        InventoryStatus.OutOfStock => "inventory_status_out",
        _ => "inventory_status_negative",
    };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    public InventoryAlertService(TransportationDbContext dbContext, ITenantContext tenantContext,
        INotificationService notificationService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    public async Task SyncAsync(IssuedItemTemplate template, IssuedItemVariant? variant, CancellationToken cancellationToken)
    {
        var variantId = variant?.Id;
        var alert = await _dbContext.InventoryAlerts.FirstOrDefaultAsync(
            a => a.TenantId == _tenantContext.TenantId && a.TemplateId == template.Id && a.VariantId == variantId,
            cancellationToken);

        var status = template.StockTrackingEnabled
            ? InventoryStatusCalculator.ComputeFor(template, variant)
            : InventoryStatus.Normal;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var dedupeKey = DedupeKeyFor(template.Id, variant?.Id);

        if (status == InventoryStatus.Normal)
        {
            if (alert is { Status: InventoryAlertStatus.Active })
            {
                alert.Status = InventoryAlertStatus.Resolved;
                alert.ResolvedAt = now;
                alert.LastSeenAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _notificationService.ResolveByDedupeKeyAsync(dedupeKey, cancellationToken);
                // Re-arm escalations for the next episode too.
                await _notificationService.ResolveByDedupeKeyAsync($"escalation:negative_stock:{alert.Id}", cancellationToken);
                await _notificationService.ResolveByDedupeKeyAsync($"escalation:critical_stock:{alert.Id}", cancellationToken);
            }

            return;
        }

        var isTransition = alert is null || alert.Status == InventoryAlertStatus.Resolved || alert.Kind != status;
        var isReactivation = alert is null || alert.Status == InventoryAlertStatus.Resolved;
        var previousKind = alert?.Status == InventoryAlertStatus.Active ? alert.Kind : (InventoryStatus?)null;

        var current = variant?.CurrentStock ?? template.CurrentStock;
        var warning = variant?.LowStockThreshold ?? template.LowStockThreshold;
        if (alert is null)
        {
            alert = new InventoryAlert
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TemplateId = template.Id,
                VariantId = variant?.Id,
            };
            _dbContext.InventoryAlerts.Add(alert);
        }

        alert.Kind = status;
        alert.Status = InventoryAlertStatus.Active;
        if (isReactivation)
        {
            alert.ActivatedAt = now;
        }

        alert.ResolvedAt = null;
        alert.StockSnapshot = current;
        alert.WarningSnapshot = warning;
        alert.MinimumSnapshot = template.MinimumStock;
        alert.LastSeenAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!isTransition || !ShouldNotify(status, warning, template.MinimumStock))
        {
            return;
        }

        // A kind change (e.g. LowStock → NegativeStock) resolves the previous notification
        // first so the dedupe key is re-armed for the new, more specific message.
        if (previousKind is not null && previousKind != status)
        {
            await _notificationService.ResolveByDedupeKeyAsync(dedupeKey, cancellationToken);
        }

        var label = variant is null ? template.Name : $"{template.Name} — {variant.Label}";
        var tab = template.VariantsEnabled ? "varianten" : "voorraad";
        var (title, message) = status switch
        {
            InventoryStatus.LowStock => ("Lage voorraad", $"{label}: voorraad is {current} (waarschuwingsgrens: {warning})."),
            InventoryStatus.CriticalStock => ("Kritieke voorraad", $"{label}: voorraad is {current} (minimum: {template.MinimumStock})."),
            InventoryStatus.OutOfStock => ("Niet op voorraad", $"{label}: voorraad is 0."),
            _ => ("Negatieve voorraad", $"{label}: voorraad is {current}. Controleer en corrigeer."),
        };
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.InventoryLowStockAlerts, TypeFor(status), title, message,
            $"/settings/issued-item-templates/{template.Id}?tab={tab}", cancellationToken,
            new NotificationOptions(DedupeKey: dedupeKey));
    }

    public static string DedupeKeyFor(Guid templateId, Guid? variantId) =>
        $"inventory_status:{templateId}:{variantId?.ToString() ?? "-"}";

    /// <summary>
    /// Negative stock always warrants a notification; the other statuses only when thresholds
    /// were actually configured — an untracked-by-thresholds item hitting zero (e.g. all
    /// units issued) is dashboard information, not an alarm.
    /// </summary>
    private static bool ShouldNotify(InventoryStatus status, int? warning, int? minimum) =>
        status == InventoryStatus.NegativeStock || warning is not null || minimum is not null;
}
