using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public interface ILowStockNotifier
{
    /// <summary>
    /// Emits an inventory_low_stock notification to holders of inventory.low_stock_alerts when
    /// the stock of the template (or variant) crossed its low-stock threshold downward. Call
    /// AFTER SaveChanges: the notification pipeline saves independently, and notifying inside
    /// the stock transaction would commit it prematurely.
    /// </summary>
    Task NotifyIfCrossedAsync(IssuedItemTemplate template, IssuedItemVariant? variant, int previousQuantity, CancellationToken cancellationToken);
}

public class LowStockNotifier : ILowStockNotifier
{
    public const string NotificationType = "inventory_low_stock";
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromDays(7);

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notificationService;

    public LowStockNotifier(TransportationDbContext dbContext, ITenantContext tenantContext, INotificationService notificationService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notificationService = notificationService;
    }

    public async Task NotifyIfCrossedAsync(
        IssuedItemTemplate template, IssuedItemVariant? variant, int previousQuantity, CancellationToken cancellationToken)
    {
        if (!template.StockTrackingEnabled || template.LowStockThreshold is not { } threshold)
        {
            return;
        }

        var current = variant?.CurrentStock ?? template.CurrentStock;
        var crossed = previousQuantity > threshold && current <= threshold;
        if (!crossed)
        {
            return; // fire on the crossing only — reads and further mutations below the line stay silent
        }

        // One notification per template/variant per window, no matter how often the level
        // bounces around the threshold (same LinkPath-#id marker idea as the expiry producer).
        var markerId = variant?.Id ?? template.Id;
        var marker = $"#{markerId}";
        var since = DateTime.UtcNow - DedupeWindow;
        var alreadyNotified = await _dbContext.Notifications.AsNoTracking()
            .AnyAsync(n => n.TenantId == _tenantContext.TenantId && n.Type == NotificationType
                           && n.CreatedAt >= since
                           && n.LinkPath != null && n.LinkPath.EndsWith(marker), cancellationToken);
        if (alreadyNotified)
        {
            return;
        }

        var label = variant is null ? template.Name : $"{template.Name} — {variant.Label}";
        var tab = template.VariantsEnabled ? "varianten" : "voorraad";
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.InventoryLowStockAlerts,
            NotificationType,
            "Lage voorraad",
            $"{label}: voorraad is {current} (grens: {threshold}). Vul de voorraad aan.",
            $"/settings/issued-item-templates/{template.Id}?tab={tab}{marker}",
            cancellationToken);
    }
}
