using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>What actually reached the consignee — derived, never a second order status.</summary>
public static class DeliveryOutcomes
{
    public const string Delivered = "Delivered";
    public const string PartiallyDelivered = "PartiallyDelivered";
    public const string NotDelivered = "NotDelivered";
    public const string RedeliveryPlanned = "RedeliveryPlanned";
    public const string Redelivered = "Redelivered";
}

public sealed record DeliveryOutcomeDto(string Outcome, Guid? RedeliveryOrderId = null, string? RedeliveryOrderNumber = null);

/// <summary>
/// EX-08: the order's lifecycle may legitimately end "Completed" (the trip is done, the order is
/// closed operationally) while the goods did NOT (fully) arrive. This rule derives the delivery
/// outcome from the facts — the latest execution of every delivery stop, the current POD outcome,
/// not-delivered quantities reported as afwijkingen, and the redelivery order — so the customer
/// portal never says "Afgerond" for a failed delivery and the office sees a partial one.
/// Null = no execution facts at all (legacy/manual completion): nothing to contradict.
/// </summary>
public static class DeliveryOutcomeRules
{
    private static readonly ExecutionExceptionType[] NotDeliveredTypes =
    [
        ExecutionExceptionType.MissingPackage, ExecutionExceptionType.IncompleteQuantity,
        ExecutionExceptionType.PartialDelivery, ExecutionExceptionType.RejectedDelivery,
    ];

    public static async Task<Dictionary<Guid, DeliveryOutcomeDto>> ResolveAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, DeliveryOutcomeDto>();
        if (orderIds.Count == 0)
        {
            return result;
        }

        var ids = orderIds.Distinct().ToList();
        var stops = await db.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.TransportOrderId) && s.StopType == StopType.Unloading)
            .Select(s => new { s.Id, s.TransportOrderId })
            .ToListAsync(cancellationToken);
        var stopIds = stops.Select(s => s.Id).ToList();
        var executions = stopIds.Count == 0
            ? []
            : await db.StopExecutions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && stopIds.Contains(e.TransportOrderStopId))
                .Select(e => new { e.TransportOrderStopId, e.Status, At = e.CompletedAt ?? e.UpdatedAt })
                .ToListAsync(cancellationToken);
        var pods = stopIds.Count == 0
            ? []
            : await db.ProofsOfDelivery.AsNoTracking()
                .Where(p => p.TenantId == tenantId && stopIds.Contains(p.TransportOrderStopId) && p.IsCurrent)
                .Select(p => new { p.TransportOrderStopId, p.Outcome })
                .ToListAsync(cancellationToken);
        var notDeliveredTypes = NotDeliveredTypes;
        var shortfalls = await db.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TransportOrderId != null && ids.Contains(e.TransportOrderId.Value)
                        && notDeliveredTypes.Contains(e.Type) && e.Status != ExecutionExceptionStatus.Rejected)
            .Select(e => e.TransportOrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var redeliveries = await db.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.RedeliveryOfOrderId != null && ids.Contains(o.RedeliveryOfOrderId.Value)
                        && o.Status != TransportOrderStatus.Cancelled)
            .Select(o => new { SourceId = o.RedeliveryOfOrderId!.Value, o.Id, o.OrderNumber, o.Status, o.CreatedAt })
            .ToListAsync(cancellationToken);

        foreach (var orderId in ids)
        {
            var mine = stops.Where(s => s.TransportOrderId == orderId).Select(s => s.Id).ToList();
            var delivered = 0;
            var partial = 0;
            var failed = 0;
            foreach (var stopId in mine)
            {
                var latest = executions.Where(e => e.TransportOrderStopId == stopId).OrderByDescending(e => e.At).FirstOrDefault();
                var pod = pods.FirstOrDefault(p => p.TransportOrderStopId == stopId);
                if (pod?.Outcome == PodOutcome.Refused || latest?.Status is StopExecutionStatus.Failed or StopExecutionStatus.Skipped)
                {
                    failed++;
                }
                else if (pod?.Outcome == PodOutcome.Partial || latest?.Status == StopExecutionStatus.PartiallyCompleted)
                {
                    partial++;
                }
                else if (pod is not null || latest?.Status == StopExecutionStatus.Completed)
                {
                    delivered++;
                }
            }

            if (delivered + partial + failed == 0)
            {
                continue; // no execution facts: the lifecycle status stands on its own
            }

            var outcome = failed > 0 && delivered + partial == 0 ? DeliveryOutcomes.NotDelivered
                : failed > 0 || partial > 0 || shortfalls.Contains(orderId) ? DeliveryOutcomes.PartiallyDelivered
                : DeliveryOutcomes.Delivered;

            if (outcome != DeliveryOutcomes.Delivered
                && redeliveries.Where(r => r.SourceId == orderId).OrderByDescending(r => r.CreatedAt).FirstOrDefault() is { } redelivery)
            {
                result[orderId] = new DeliveryOutcomeDto(
                    redelivery.Status is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced
                        ? DeliveryOutcomes.Redelivered
                        : DeliveryOutcomes.RedeliveryPlanned,
                    redelivery.Id, redelivery.OrderNumber);
                continue;
            }

            result[orderId] = new DeliveryOutcomeDto(outcome);
        }

        return result;
    }
}
