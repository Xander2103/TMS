using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Reporting.Services;

/// <summary>
/// P11: activity-based KPI read model. Groups DossierActivities by ActivityType so management
/// sees counts and revenue per activity family (crane, plateau, distribution, storage, ...).
///
/// Documented conventions:
/// - Period filter uses the activity's effective date = PlannedDate ?? date(CreatedAt).
///   Standalone activities carry their own PlannedDate; transport-shaped activities usually do
///   not (their dates live on the order) and fall back to the creation date.
/// - Rows count ACTIVITIES: one dossier with a crane and a plateau activity contributes to
///   both rows independently.
/// - Revenue = AgreedPrice ?? 0 of the linked, non-cancelled orders; counted once per distinct
///   order within a row, and once overall in the totals (cross-row dedupe).
/// - RedeliveryCount per row = incidents whose LinkedRedeliveryOrderId points to one of the
///   row's linked orders and whose redelivery order's OrderDate falls in the period. The
///   totals carry the tenant-wide count for the period (also covers unlinked redeliveries).
/// - PalletDays mirrors StorageBillingService's started-days over the whole tenant; null when
///   no storage stay overlaps the period.
/// </summary>
public class ActivityKpiService : IActivityKpiService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ActivityKpiService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<ActivityKpiReportDto> GetActivityKpisAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var periodStart = from.ToDateTime(TimeOnly.MinValue);
        var periodEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue); // exclusive

        // Activities in the period by effective date (PlannedDate ?? CreatedAt date).
        var activities = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId
                        && ((a.PlannedDate != null && a.PlannedDate >= from && a.PlannedDate <= to)
                            || (a.PlannedDate == null && a.CreatedAt >= periodStart && a.CreatedAt < periodEnd)))
            .Select(a => new { a.Id, a.ActivityTypeId, a.LinkedTransportOrderId })
            .ToListAsync(cancellationToken);

        var typeIds = activities.Select(a => a.ActivityTypeId).Distinct().ToList();
        var types = typeIds.Count == 0
            ? []
            : await _dbContext.ActivityTypes.AsNoTracking()
                .Where(t => t.TenantId == tenantId && typeIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Code, t.Name, t.KpiCategory, t.SortOrder })
                .ToListAsync(cancellationToken);
        var typeById = types.ToDictionary(t => t.Id);

        // Linked orders (revenue basis): non-cancelled orders only, AgreedPrice ?? 0.
        var linkedOrderIds = activities
            .Where(a => a.LinkedTransportOrderId is not null)
            .Select(a => a.LinkedTransportOrderId!.Value)
            .Distinct()
            .ToList();
        var orderPrices = linkedOrderIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && linkedOrderIds.Contains(o.Id)
                            && o.Status != TransportOrderStatus.Cancelled)
                .ToDictionaryAsync(o => o.Id, o => o.AgreedPrice ?? 0m, cancellationToken);

        // Redeliveries: incidents whose redelivery order's OrderDate falls in the period.
        var redeliveries = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.LinkedRedeliveryOrderId != null)
            .Join(_dbContext.TransportOrders.AsNoTracking()
                    .Where(o => o.TenantId == tenantId && o.OrderDate >= from && o.OrderDate <= to),
                i => i.LinkedRedeliveryOrderId, o => o.Id,
                (i, o) => new { IncidentId = i.Id, RedeliveryOrderId = o.Id })
            .ToListAsync(cancellationToken);
        var redeliveriesByOrder = redeliveries.ToLookup(r => r.RedeliveryOrderId);

        var rows = activities
            .GroupBy(a => a.ActivityTypeId)
            .Select(g =>
            {
                typeById.TryGetValue(g.Key, out var type);
                // Distinct linked orders that still exist and are not cancelled.
                var orderIds = g
                    .Where(a => a.LinkedTransportOrderId is not null)
                    .Select(a => a.LinkedTransportOrderId!.Value)
                    .Distinct()
                    .Where(orderPrices.ContainsKey)
                    .ToList();
                return new
                {
                    SortOrder = type?.SortOrder ?? int.MaxValue,
                    OrderIds = orderIds,
                    Row = new ActivityKpiRowDto(
                        g.Key, type?.Code ?? "?", type?.Name ?? "?", type?.KpiCategory,
                        g.Count(), orderIds.Count,
                        Round2(orderIds.Sum(id => orderPrices[id])),
                        orderIds.Sum(id => redeliveriesByOrder[id].Count())),
                };
            })
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Row.Name).ThenBy(x => x.Row.Code)
            .ToList();

        // Totals: activities are summed; orders/revenue are deduped across rows.
        var allOrderIds = rows.SelectMany(x => x.OrderIds).Distinct().ToList();
        var totals = new ActivityKpiTotalsDto(
            activities.Count,
            allOrderIds.Count,
            Round2(allOrderIds.Sum(id => orderPrices[id])),
            redeliveries.Count);

        // Per-category rollup (same dedupe rules within each category).
        var perCategory = rows
            .GroupBy(x => x.Row.KpiCategory)
            .Select(g =>
            {
                var orderIds = g.SelectMany(x => x.OrderIds).Distinct().ToList();
                return new ActivityKpiCategoryRowDto(
                    g.Key,
                    g.Sum(x => x.Row.ActivityCount),
                    orderIds.Count,
                    Round2(orderIds.Sum(id => orderPrices[id])),
                    orderIds.Sum(id => redeliveriesByOrder[id].Count()));
            })
            .OrderBy(c => c.KpiCategory is null) // named categories first
            .ThenBy(c => c.KpiCategory)
            .ToList();

        var palletDays = await PalletDaysAsync(periodStart, periodEnd, cancellationToken);

        return new ActivityKpiReportDto(from, to, rows.Select(x => x.Row).ToList(), totals, palletDays, perCategory);
    }

    /// <summary>Started-days total of all storage stays overlapping the period (the
    /// StorageBillingService convention: 0.5 day counts as 1; open stays run to period end).
    /// Null when no stay overlaps the period.</summary>
    private async Task<decimal?> PalletDaysAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var stays = await _dbContext.StorageStays.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.InAt < periodEnd && (s.OutAt == null || s.OutAt > periodStart))
            .Select(s => new { s.InAt, s.OutAt })
            .ToListAsync(cancellationToken);
        if (stays.Count == 0)
        {
            return null;
        }

        return stays.Sum(s =>
        {
            var start = s.InAt < periodStart ? periodStart : s.InAt;
            var end = s.OutAt is { } o && o < periodEnd ? o : periodEnd;
            return end <= start ? 0m : Math.Ceiling((decimal)(end - start).TotalDays);
        });
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2);
}
