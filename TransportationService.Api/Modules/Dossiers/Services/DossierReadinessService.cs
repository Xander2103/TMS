using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierReadinessService
{
    /// <summary>Actionable attention items for one dossier; empty = nothing needs attention.</summary>
    Task<IReadOnlyList<ReadinessIssueDto>> EvaluateAsync(Guid dossierId, CancellationToken cancellationToken);

    /// <summary>Dashboard tile: open dossiers with structural attention (cheap approximation, see remarks).</summary>
    Task<int> CountDossiersWithAttentionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Additive readiness projections (spec Part C): computed on read, never persisted, never a
/// new order-status value. Draft dossiers with missing information are VALID — these issues
/// tell the user exactly what needs attention for the NEXT step, each with the page section
/// to jump to. Wave 1 produces Planning + Commercial rules; later waves add Warehouse /
/// Execution / Invoice producers to the same vocabulary without schema change.
/// </summary>
public class DossierReadinessService : IDossierReadinessService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public DossierReadinessService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    private sealed record CoverageEntry(string? Status);

    public async Task<IReadOnlyList<ReadinessIssueDto>> EvaluateAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var issues = new List<ReadinessIssueDto>();

        var activities = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId)
            .Join(_dbContext.ActivityTypes.AsNoTracking(), a => a.ActivityTypeId, t => t.Id,
                (a, t) => new { a.Id, a.LinkedTransportOrderId, TypeName = t.Name, t.HasStops })
            .ToListAsync(cancellationToken);

        if (activities.Count == 0)
        {
            issues.Add(new ReadinessIssueDto(
                "activity.none", "Info", "Nog geen activiteit toegevoegd.",
                "activiteiten", "activity.add", "Planning"));
        }

        // Lives in the ROUTE section since the UX sprint: the dossier route section offers inline
        // stop entry that creates the order on save, so the jump lands where the fix happens.
        foreach (var activity in activities.Where(a => a.HasStops && a.LinkedTransportOrderId is null))
        {
            issues.Add(new ReadinessIssueDto(
                "route.order_missing", "Warning",
                $"{activity.TypeName}: route en goederen zijn nog niet ingevuld.",
                "route", "stops.loading", "Planning", ActivityId: activity.Id));
        }

        var orderIds = activities
            .Where(a => a.LinkedTransportOrderId is not null)
            .Select(a => a.LinkedTransportOrderId!.Value)
            .Distinct()
            .ToList();
        if (orderIds.Count == 0)
        {
            // Only a transport-shaped activity can carry a price (the linked order is the price
            // carrier — docs/dossiers.md). A dossier with only standalone activities (opslag,
            // kraanwerk) has no actionable pricing step in this release, so it gets no issue:
            // the attention panel never suggests creating a transport order to price storage.
            if (activities.Any(a => a.HasStops))
            {
                issues.Add(new ReadinessIssueDto(
                    "pricing.none", "Info", "Nog geen verkooplijnen of prijs.",
                    "prijs", "price", "Commercial"));
            }

            return issues;
        }

        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Status, o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.AgreedPrice,
                HasLoading = o.Stops.Any(s => !s.IsDeleted && s.StopType == StopType.Loading),
                HasUnloading = o.Stops.Any(s => !s.IsDeleted && s.StopType == StopType.Unloading),
                HasDate = o.Stops.Any(s => !s.IsDeleted && s.PlannedFrom != null),
            })
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            // Blocking exactly where the existing Confirm gate blocks — shown BEFORE the user
            // presses Bevestigen, with the same rule, so the error is never a surprise.
            if (order.Status is TransportOrderStatus.Draft or TransportOrderStatus.Submitted
                && (!order.HasLoading || !order.HasUnloading))
            {
                var missing = (!order.HasLoading, !order.HasUnloading) switch
                {
                    (true, true) => "laad- en loslocatie zijn nog onbekend",
                    (true, false) => "laadlocatie is nog onbekend",
                    _ => "loslocatie is nog onbekend",
                };
                issues.Add(new ReadinessIssueDto(
                    "order.confirm.stops", "Blocking",
                    $"{order.OrderNumber}: {missing} (nodig om te bevestigen).",
                    "route", order.HasLoading ? "stops.unloading" : "stops.loading", "Planning",
                    TransportOrderId: order.Id));
            }

            // Parenthesised on purpose: `!HasDate && Draft || Submitted || Confirmed` (the pre-sprint
            // text) fired for EVERY Submitted/Confirmed order, date or not.
            if (!order.HasDate && IsOpenCommercial(order.Status))
            {
                issues.Add(new ReadinessIssueDto(
                    "route.date_missing", "Warning",
                    $"{order.OrderNumber}: nog geen planningsdatum.",
                    "route", "stops.plannedFrom", "Planning", TransportOrderId: order.Id));
            }
        }

        // Commercial completeness from the typed coverage column (Wave 2 §5) — no JSON parse;
        // the backfill seeder derived the column for pre-wave snapshots at startup.
        var snapshots = await _dbContext.TransportOrderPricingSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId)
                        && (s.CoverageStatus == "Partial" || s.CoverageStatus == "None" || s.IsStale))
            .Join(_dbContext.TransportOrders.AsNoTracking(), s => s.TransportOrderId, o => o.Id,
                (s, o) => new { s.TransportOrderId, s.CoverageStatus, s.IsStale, o.OrderNumber })
            .ToListAsync(cancellationToken);
        var incompleteOrderIds = new HashSet<Guid>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot.CoverageStatus is "Partial" or "None")
            {
                incompleteOrderIds.Add(snapshot.TransportOrderId);
                issues.Add(new ReadinessIssueDto(
                    "pricing.incomplete", "Warning",
                    $"{snapshot.OrderNumber}: niet alle onderdelen hebben een volledige prijs.",
                    "prijs", "price", "Commercial", TransportOrderId: snapshot.TransportOrderId));
            }

            if (snapshot.IsStale)
            {
                issues.Add(new ReadinessIssueDto(
                    "pricing.stale", "Warning",
                    $"{snapshot.OrderNumber}: prijs verouderd — herbereken.",
                    "prijs", "price", "Commercial", TransportOrderId: snapshot.TransportOrderId));
            }
        }

        // UX sprint 2026-09-09 §2.5: a linked order in its commercial phase without a sales price
        // (OrderPricingState.IsPriced — the same definition the list and the financials use).
        // Suppressed when pricing.incomplete already names that order: both point at the same
        // field, and "niet alle onderdelen hebben een prijs" is the more specific message. The
        // two conditions can coincide (coverage Partial with a 0 total) — one line suffices.
        foreach (var order in orders.Where(o =>
                     IsOpenCommercial(o.Status)
                     && !OrderPricingState.IsPriced(o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.AgreedPrice)
                     && !incompleteOrderIds.Contains(o.Id)))
        {
            issues.Add(new ReadinessIssueDto(
                "pricing.missing", "Warning",
                $"{order.OrderNumber}: nog geen verkoopprijs.",
                "prijs", "price", "Commercial", TransportOrderId: order.Id));
        }

        return issues;
    }

    /// <summary>Statuses in which route and price are still being completed (Draft, Submitted, Confirmed).</summary>
    private static bool IsOpenCommercial(TransportOrderStatus status) =>
        status is TransportOrderStatus.Draft or TransportOrderStatus.Submitted or TransportOrderStatus.Confirmed;

    /// <remarks>
    /// Structural attention (no activities yet, or a transport activity without its order)
    /// plus, since Wave 2 §5, the pricing dimension via the typed coverage column: an open
    /// dossier with a linked order whose coverage is Partial/None or whose price went stale
    /// counts as needing attention — the Wave 1 gap closes. Since the UX sprint 2026-09-09 a
    /// linked order in its commercial phase that is not priced (<c>pricing.missing</c>) counts
    /// too. Still a single SQL statement; the date/stop rules remain out of scope here.
    /// </remarks>
    public async Task<int> CountDossiersWithAttentionAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        return await _dbContext.TransportDossiers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Status == Entities.DossierStatus.Open)
            .Where(d =>
                !_dbContext.DossierActivities.Any(a => a.DossierId == d.Id)
                || _dbContext.DossierActivities
                    .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId == null)
                    .Join(_dbContext.ActivityTypes, a => a.ActivityTypeId, t => t.Id, (a, t) => t.HasStops)
                    .Any(hasStops => hasStops)
                || _dbContext.DossierActivities
                    .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                    .Join(_dbContext.TransportOrderPricingSnapshots,
                        a => a.LinkedTransportOrderId, s => s.TransportOrderId,
                        (a, s) => new { s.CoverageStatus, s.IsStale })
                    .Any(s => s.CoverageStatus == "Partial" || s.CoverageStatus == "None" || s.IsStale)
                // OrderPricingState.IsUnpricedExpression: the same tree as the C# definition, so
                // SQL NULL (AgreedPrice unknown) counts as unpriced exactly like the helper does.
                || _dbContext.DossierActivities
                    .Where(a => a.DossierId == d.Id && a.LinkedTransportOrderId != null)
                    .Join(_dbContext.TransportOrders, a => a.LinkedTransportOrderId, o => o.Id, (a, o) => o)
                    .Where(OrderPricingState.IsUnpricedExpression)
                    .Any(o => o.Status == TransportOrderStatus.Draft
                              || o.Status == TransportOrderStatus.Submitted
                              || o.Status == TransportOrderStatus.Confirmed))
            .CountAsync(cancellationToken);
    }
}
