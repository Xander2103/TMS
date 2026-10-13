using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Writes an immutable <see cref="TransportOrderStatusHistory"/> row whenever a tracked
/// order's Status changes — regardless of which module performed the change (manual
/// transition, cancel, correction, planning engine, invoicing). The acting service can
/// attach a reason/correction marker via the order's transient Pending* properties.
/// </summary>
public sealed class OrderStatusHistoryInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public OrderStatusHistoryInterceptor(IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var userId = ResolveCurrentUserId();

        // Materialise first: adding history rows while iterating would mutate the tracker.
        var changed = context.ChangeTracker.Entries<TransportOrder>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => new
            {
                Entry = e,
                From = e.OriginalValues.GetValue<TransportOrderStatus>(nameof(TransportOrder.Status)),
                To = e.CurrentValues.GetValue<TransportOrderStatus>(nameof(TransportOrder.Status)),
            })
            .Where(x => x.From != x.To)
            .ToList();

        foreach (var change in changed)
        {
            var order = change.Entry.Entity;
            context.Add(new TransportOrderStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = order.TenantId,
                TransportOrderId = order.Id,
                FromStatus = change.From,
                ToStatus = change.To,
                Reason = order.PendingStatusChangeReason,
                IsCorrection = order.PendingStatusChangeIsCorrection,
                ChangedByUserId = userId,
                ChangedAt = now,
            });
            order.PendingStatusChangeReason = null;
            order.PendingStatusChangeIsCorrection = false;
        }
    }

    private Guid? ResolveCurrentUserId()
    {
        var items = _httpContextAccessor.HttpContext?.Items;
        if (items is not null && items.TryGetValue(nameof(ICurrentUserContext), out var value)
            && value is ICurrentUserContext currentUser)
        {
            return currentUser.CurrentUserId;
        }

        return null;
    }
}
