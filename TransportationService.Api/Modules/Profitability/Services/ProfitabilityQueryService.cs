using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Profitability.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.Profitability.Services;

public interface IProfitabilityQueryService
{
    Task<ProfitabilityOverviewDto> GetOverviewAsync(
        DateOnly? from, DateOnly? to, Guid? customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfitabilityGroupDto>> GetGroupedAsync(
        ProfitabilityDimension dimension, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<TripProfitabilityExplanationDto?> GetExplanationAsync(Guid tripId, CancellationToken cancellationToken);
}

/// <summary>
/// Read models over the EXISTING financial data: TripCosting snapshots (Estimated/Actual
/// phases), agreed order prices and invoice lines. No second accounting system — revenue
/// natures stay separate (agreed / invoiced / paid), estimates are never presented as
/// actuals, and core cost types without any line are reported as explicitly missing.
/// </summary>
public class ProfitabilityQueryService : IProfitabilityQueryService
{
    private const int MaxRangeDays = 366;

    /// <summary>Cost types a trip calculation is incomplete without.</summary>
    private static readonly TripCostType[] CoreCostTypes =
        [TripCostType.Fuel, TripCostType.DriverLabour, TripCostType.VehicleDistance, TripCostType.Toll];

    private static readonly TripStatus[] RelevantStatuses =
        [TripStatus.Planned, TripStatus.InProgress, TripStatus.Completed];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ProfitabilityQueryService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    private (DateOnly From, DateOnly To) NormalizeRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var start = from ?? today.AddDays(-30);
        var end = to ?? today;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            throw new DomainValidationException($"De periode mag maximaal {MaxRangeDays} dagen beslaan.");
        }

        return (start, end);
    }

    private sealed record TripContext(
        List<TripRow> Trips,
        Dictionary<Guid, TripCostSummary> Summaries,
        ILookup<Guid, TripCostLine> LinesByTrip,
        ILookup<Guid, OrderRow> OrdersByTrip,
        Dictionary<Guid, decimal> InvoicedByOrder,
        Dictionary<Guid, decimal> PaidByOrder,
        Dictionary<Guid, string> DriverNames,
        Dictionary<Guid, string> VehicleNumbers,
        Dictionary<Guid, int> StopCounts,
        Dictionary<Guid, int> PackageCounts);

    private sealed record TripRow(
        Guid Id, string TripNumber, DateOnly TripDate, TripStatus Status,
        Guid? DriverId, Guid? VehicleId, decimal? PlannedDistanceKm, decimal? ActualDistanceKm);

    private sealed record OrderRow(Guid TripId, Guid OrderId, Guid CustomerId, string CustomerName, decimal? AgreedPrice);

    private async Task<TripContext> LoadAsync(
        DateOnly from, DateOnly to, Guid? customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var trips = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TripDate >= from && t.TripDate <= to
                        && RelevantStatuses.Contains(t.Status))
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .Select(t => new TripRow(t.Id, t.TripNumber, t.TripDate, t.Status,
                t.DriverId, t.VehicleId, t.PlannedDistanceKm, t.ActualDistanceKm))
            .ToListAsync(cancellationToken);
        var tripIds = trips.Select(t => t.Id).ToList();

        var orders = tripIds.Count == 0
            ? []
            : await (from link in _dbContext.TripOrders.AsNoTracking()
                         .Where(l => l.TenantId == tenantId && tripIds.Contains(l.TripId))
                     join o in _dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId)
                         on link.TransportOrderId equals o.Id
                     join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId)
                         on o.CustomerId equals c.Id
                     select new OrderRow(link.TripId, o.Id, c.Id, c.Name, o.AgreedPrice))
                .ToListAsync(cancellationToken);

        if (customerId is { } filterCustomer)
        {
            var matchingTripIds = orders.Where(o => o.CustomerId == filterCustomer)
                .Select(o => o.TripId).ToHashSet();
            trips = trips.Where(t => matchingTripIds.Contains(t.Id)).ToList();
            tripIds = trips.Select(t => t.Id).ToList();
            orders = orders.Where(o => tripIds.Contains(o.TripId)).ToList();
        }

        var orderIds = orders.Select(o => o.OrderId).Distinct().ToList();

        var summaries = tripIds.Count == 0
            ? []
            : await _dbContext.TripCostSummaries.AsNoTracking()
                .Where(s => s.TenantId == tenantId && tripIds.Contains(s.TripId))
                .ToDictionaryAsync(s => s.TripId, cancellationToken);
        var lines = tripIds.Count == 0
            ? Array.Empty<TripCostLine>().ToLookup(l => l.TripId)
            : (await _dbContext.TripCostLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && tripIds.Contains(l.TripId))
                .ToListAsync(cancellationToken)).ToLookup(l => l.TripId);

        // Invoiced/paid revenue per order (all non-cancelled invoices; paid subset separate).
        var invoiceLines = orderIds.Count == 0
            ? []
            : await (from line in _dbContext.InvoiceLines.AsNoTracking()
                         .Where(l => l.TenantId == tenantId && l.TransportOrderId != null
                                     && orderIds.Contains(l.TransportOrderId.Value))
                     join invoice in _dbContext.Invoices.AsNoTracking()
                             .Where(i => i.TenantId == tenantId && i.Status != InvoiceStatus.Cancelled)
                         on line.InvoiceId equals invoice.Id
                     select new { OrderId = line.TransportOrderId!.Value, Amount = line.Quantity * line.UnitPrice, invoice.Status })
                .ToListAsync(cancellationToken);
        var invoicedByOrder = invoiceLines
            .GroupBy(l => l.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var paidByOrder = invoiceLines
            .Where(l => l.Status == InvoiceStatus.Paid)
            .GroupBy(l => l.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        var driverIds = trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var driverNames = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var vehicleIds = trips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var vehicleNumbers = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.InternalNumber, cancellationToken);

        var stopCounts = orderIds.Count == 0
            ? new Dictionary<Guid, int>()
            : (await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .GroupBy(s => s.TransportOrderId)
                .Select(g => new { OrderId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.OrderId, x => x.Count);
        var packageCounts = orderIds.Count == 0
            ? new Dictionary<Guid, int>()
            : (await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && orderIds.Contains(p.TransportOrderId))
                .GroupBy(p => p.TransportOrderId)
                .Select(g => new { OrderId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.OrderId, x => x.Count);

        return new TripContext(trips, summaries, lines, orders.ToLookup(o => o.TripId),
            invoicedByOrder, paidByOrder, driverNames, vehicleNumbers, stopCounts, packageCounts);
    }

    private TripProfitabilityDto Project(TripRow trip, TripContext ctx)
    {
        var orders = ctx.OrdersByTrip[trip.Id].ToList();
        var summary = ctx.Summaries.GetValueOrDefault(trip.Id);
        var lines = ctx.LinesByTrip[trip.Id].ToList();

        var agreed = orders.Sum(o => o.AgreedPrice ?? 0m);
        var invoiced = orders.Sum(o => ctx.InvoicedByOrder.GetValueOrDefault(o.OrderId));
        var paid = orders.Sum(o => ctx.PaidByOrder.GetValueOrDefault(o.OrderId));
        var (used, source) = invoiced > 0m ? (invoiced, RevenueSource.Invoiced)
            : agreed > 0m ? (agreed, RevenueSource.Agreed)
            : (0m, RevenueSource.None);

        // Finalized trips freeze their numbers; live trips use the projected merge.
        var actualCost = summary?.ActualTotal ?? 0m;
        var estimatedCost = summary?.EstimatedTotal ?? 0m;
        var projectedCost = summary?.IsFinalized == true && summary.FinalCost is { } finalCost
            ? finalCost
            : summary?.ProjectedTotal ?? 0m;

        var presentTypes = lines.Select(l => l.CostType).ToHashSet();
        var missing = CoreCostTypes.Where(t => !presentTypes.Contains(t)).ToList();

        var distance = trip.ActualDistanceKm ?? trip.PlannedDistanceKm;
        var stopCount = orders.Sum(o => ctx.StopCounts.GetValueOrDefault(o.OrderId));
        var packageCount = orders.Sum(o => ctx.PackageCounts.GetValueOrDefault(o.OrderId));

        var margin = used - projectedCost;
        var customerNames = orders.Select(o => o.CustomerName).Distinct().ToList();

        return new TripProfitabilityDto(
            trip.Id, trip.TripNumber, trip.TripDate, trip.Status,
            trip.DriverId is { } d ? ctx.DriverNames.GetValueOrDefault(d) : null,
            trip.VehicleId is { } v ? ctx.VehicleNumbers.GetValueOrDefault(v) : null,
            customerNames.Count == 0 ? null
                : customerNames.Count == 1 ? customerNames[0]
                : $"{customerNames[0]} +{customerNames.Count - 1}",
            orders.Count, stopCount, packageCount,
            distance, trip.ActualDistanceKm is not null,
            agreed, invoiced, paid, used, source,
            actualCost, estimatedCost, projectedCost,
            missing,
            margin,
            used > 0m ? Math.Round(margin / used * 100m, 1) : null,
            distance is > 0m ? Math.Round(used / distance.Value, 2) : null,
            distance is > 0m ? Math.Round(projectedCost / distance.Value, 2) : null,
            stopCount > 0 ? Math.Round(projectedCost / stopCount, 2) : null,
            packageCount > 0 ? Math.Round(projectedCost / packageCount, 2) : null,
            summary?.IsFinalized ?? false);
    }

    public async Task<ProfitabilityOverviewDto> GetOverviewAsync(
        DateOnly? from, DateOnly? to, Guid? customerId, CancellationToken cancellationToken)
    {
        var (start, end) = NormalizeRange(from, to);
        var ctx = await LoadAsync(start, end, customerId, cancellationToken);
        var trips = ctx.Trips.Select(t => Project(t, ctx)).ToList();

        var revenue = trips.Sum(t => t.RevenueUsed);
        var projected = trips.Sum(t => t.ProjectedCost);
        var summary = new ProfitabilitySummaryDto(
            trips.Count,
            revenue,
            trips.Sum(t => t.ActualCost),
            trips.Sum(t => t.EstimatedCost),
            projected,
            revenue - projected,
            revenue > 0m ? Math.Round((revenue - projected) / revenue * 100m, 1) : null,
            trips.Count(t => t.MissingCostTypes.Count > 0),
            trips.Count(t => t.Margin < 0m));

        return new ProfitabilityOverviewDto(start, end, summary, trips);
    }

    public async Task<IReadOnlyList<ProfitabilityGroupDto>> GetGroupedAsync(
        ProfitabilityDimension dimension, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = NormalizeRange(from, to);
        var ctx = await LoadAsync(start, end, customerId: null, cancellationToken);
        var trips = ctx.Trips.Select(t => (Row: t, Dto: Project(t, ctx))).ToList();

        if (dimension == ProfitabilityDimension.Customer)
        {
            // Cost is booked per trip, not per customer: shared trips split their projected
            // cost evenly over their orders. Explicitly marked as allocation.
            var buckets = new Dictionary<Guid, (string Label, int Trips, decimal Revenue, decimal Cost, bool Allocated)>();
            foreach (var (row, dto) in trips)
            {
                var orders = ctx.OrdersByTrip[row.Id].ToList();
                if (orders.Count == 0)
                {
                    continue;
                }

                var shared = orders.Select(o => o.CustomerId).Distinct().Count() > 1;
                var costPerOrder = dto.ProjectedCost / orders.Count;
                foreach (var order in orders)
                {
                    var invoiced = ctx.InvoicedByOrder.GetValueOrDefault(order.OrderId);
                    var orderRevenue = invoiced > 0m ? invoiced : order.AgreedPrice ?? 0m;
                    var bucket = buckets.GetValueOrDefault(order.CustomerId,
                        (Label: order.CustomerName, Trips: 0, Revenue: 0m, Cost: 0m, Allocated: false));
                    buckets[order.CustomerId] = (order.CustomerName, bucket.Trips + 1,
                        bucket.Revenue + orderRevenue, bucket.Cost + costPerOrder, bucket.Allocated || shared);
                }
            }

            return Finish(buckets.Select(kv => (kv.Key.ToString(), kv.Value.Label, kv.Value.Trips,
                kv.Value.Revenue, kv.Value.Cost, kv.Value.Allocated)));
        }

        Func<(TripRow Row, TripProfitabilityDto Dto), (string Key, string Label)> selector = dimension switch
        {
            ProfitabilityDimension.Driver => t =>
                (t.Row.DriverId?.ToString() ?? "none", t.Dto.DriverName ?? "Zonder chauffeur"),
            ProfitabilityDimension.Vehicle => t =>
                (t.Row.VehicleId?.ToString() ?? "none", t.Dto.VehicleNumber ?? "Zonder voertuig"),
            _ => t =>
            {
                var calendar = System.Globalization.ISOWeek.GetWeekOfYear(
                    t.Row.TripDate.ToDateTime(TimeOnly.MinValue));
                var year = System.Globalization.ISOWeek.GetYear(t.Row.TripDate.ToDateTime(TimeOnly.MinValue));
                return ($"{year}-W{calendar:00}", $"Week {calendar} {year}");
            },
        };

        return Finish(trips
            .GroupBy(selector)
            .Select(g => (g.Key.Key, g.Key.Label, g.Count(),
                g.Sum(t => t.Dto.RevenueUsed), g.Sum(t => t.Dto.ProjectedCost), false)));

        static IReadOnlyList<ProfitabilityGroupDto> Finish(
            IEnumerable<(string Key, string Label, int Trips, decimal Revenue, decimal Cost, bool Allocated)> rows) =>
            rows.Select(r => new ProfitabilityGroupDto(
                    r.Key, r.Label, r.Trips, r.Revenue, r.Cost, r.Revenue - r.Cost,
                    r.Revenue > 0m ? Math.Round((r.Revenue - r.Cost) / r.Revenue * 100m, 1) : null,
                    r.Allocated))
                .OrderByDescending(g => g.Margin)
                .ToList();
    }

    public async Task<TripProfitabilityExplanationDto?> GetExplanationAsync(
        Guid tripId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var trip = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.Id == tripId && t.TenantId == tenantId)
            .Select(t => new { t.Id, t.TripNumber, t.TripDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var ctx = await LoadAsync(trip.TripDate, trip.TripDate, null, cancellationToken);
        var orders = ctx.OrdersByTrip[tripId].ToList();

        var revenueLines = new List<ProfitabilityExplanationLineDto>();
        foreach (var order in orders)
        {
            if (order.AgreedPrice is { } agreed)
            {
                revenueLines.Add(new("Revenue", $"Afgesproken prijs — {order.CustomerName}", agreed,
                    null, "Opdracht", false, null));
            }

            if (ctx.InvoicedByOrder.GetValueOrDefault(order.OrderId) is var invoiced and > 0m)
            {
                revenueLines.Add(new("Revenue", $"Gefactureerd — {order.CustomerName}", invoiced,
                    null, "Factuur", false, null));
            }
        }

        var costLines = ctx.LinesByTrip[tripId]
            .OrderBy(l => l.Phase).ThenBy(l => l.CostType)
            .Select(l => new ProfitabilityExplanationLineDto(
                "Cost", l.Description, l.Amount, l.Phase.ToString(), l.Source, l.IsManualOverride, l.OverrideReason))
            .ToList();

        var presentTypes = ctx.LinesByTrip[tripId].Select(l => l.CostType).ToHashSet();
        return new TripProfitabilityExplanationDto(
            tripId, trip.TripNumber, revenueLines, costLines,
            CoreCostTypes.Where(t => !presentTypes.Contains(t)).ToList(),
            "Marge = gebruikte omzet (gefactureerd indien aanwezig, anders afgesproken) minus geprojecteerde kosten "
            + "(werkelijk per kostensoort waar beschikbaar, anders raming). Correcties lopen via ritkosten (audit).");
    }
}
