using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Reporting.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Modules.Reporting.Services;

/// <summary>
/// Backend read model for the management KPI dashboard. Every number is computed here (never
/// in the browser); the exact formulas live in docs/kpi-definitions.md and in the tests.
/// </summary>
public class KpiQueryService : IKpiQueryService
{
    /// <summary>Hard bound on trips per query; ranges are already capped at 366 days.</summary>
    private const int MaxTrips = 5000;
    private const decimal AvailableHoursPerVehiclePerWorkday = 8m;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICostRateService _costRateService;
    private readonly TimeProvider _timeProvider;

    public KpiQueryService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICostRateService costRateService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _costRateService = costRateService;
        _timeProvider = timeProvider;
    }

    private sealed record TripFacts(
        Guid Id, string TripNumber, DateOnly TripDate, TripStatus Status,
        Guid? DriverId, Guid? VehicleId,
        decimal Revenue, decimal MatchedRevenue, decimal CostShare,
        decimal Cost, decimal EstimatedCost, decimal ProjectedCost, decimal? FinalCost, bool IsFinalized,
        decimal Km, decimal EmptyKm, decimal? Hours,
        IReadOnlyList<OrderFacts> Orders);

    private sealed record OrderFacts(Guid OrderId, string OrderNumber, Guid CustomerId, decimal Revenue, bool Matched);

    public async Task<KpiDashboardDto> GetDashboardAsync(KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var facts = await LoadTripFactsAsync(filter, cancellationToken);
        var active = facts.Where(f => f.Status != TripStatus.Cancelled).ToList();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // Financieel — revenue/cost honour the customer filter via MatchedRevenue/CostShare.
        var revenuePeriod = Round2(active.Sum(f => f.MatchedRevenue));
        var costPeriod = Round2(active.Sum(f => f.Cost * f.CostShare));
        var profitPeriod = Round2(revenuePeriod - costPeriod);
        var todayTrips = active.Where(f => f.TripDate == today).ToList();
        var revenueToday = Round2(todayTrips.Sum(f => f.MatchedRevenue));
        var profitToday = Round2(revenueToday - todayTrips.Sum(f => f.Cost * f.CostShare));

        // Vloot & kilometers.
        var totalKm = active.Sum(f => f.Km);
        var emptyKm = active.Sum(f => f.EmptyKm);
        var activeHours = active.Sum(f => f.Hours ?? 0m);
        var availableHours = await AvailableVehicleHoursAsync(filter, cancellationToken);

        var fuelQuery = _dbContext.FuelTransactions.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.TransactionDate >= filter.From && f.TransactionDate <= filter.To);
        if (filter.VehicleId is { } fuelVehicle)
        {
            fuelQuery = fuelQuery.Where(f => f.VehicleId == fuelVehicle);
        }

        if (filter.DriverId is { } fuelDriver)
        {
            fuelQuery = fuelQuery.Where(f => f.DriverId == fuelDriver);
        }

        var fuel = await fuelQuery
            .Join(_dbContext.Vehicles.AsNoTracking().Where(v => v.TenantId == tenantId),
                f => f.VehicleId, v => v.Id,
                (f, v) => new { f.Litres, f.TotalAmount, v.FuelType })
            .ToListAsync(cancellationToken);
        var rates = await _costRateService.GetForDateAsync(filter.To, cancellationToken);
        var co2Diesel = rates?.Co2KgPerLitreDiesel ?? 2.68m;
        var co2Other = rates?.Co2KgPerLitreOther ?? 2.31m;
        var co2 = fuel.Sum(f => f.FuelType switch
        {
            FuelType.Electric or FuelType.Hydrogen => 0m,
            FuelType.Diesel => f.Litres * co2Diesel,
            _ => f.Litres * co2Other,
        });

        var damageQuery = _dbContext.DamageReports.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && d.Status != DamageStatus.Repaired && d.Status != DamageStatus.Closed);
        if (filter.VehicleId is { } damageVehicle)
        {
            damageQuery = damageQuery.Where(d => d.VehicleId == damageVehicle);
        }

        var openDamage = await damageQuery.CountAsync(cancellationToken);

        // Uitvoering — stops/executions of the loaded (non-cancelled) trips.
        var activeTripIds = active.Select(f => f.Id).ToList();
        var execution = await LoadExecutionKpisAsync(activeTripIds, cancellationToken);

        var openExceptions = activeTripIds.Count == 0
            ? 0
            : await _dbContext.ExecutionExceptions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && activeTripIds.Contains(e.TripId)
                            && e.Status != ExecutionExceptionStatus.Resolved
                            && e.Status != ExecutionExceptionStatus.Rejected)
                .CountAsync(cancellationToken);

        // Kostenafwijking: afgeronde ritten met zowel schatting als definitieve kost.
        var overrunEligible = active
            .Where(f => f.Status == TripStatus.Completed && f.FinalCost is { } && f.EstimatedCost > 0)
            .Select(f => (Final: f.FinalCost!.Value, f.EstimatedCost))
            .ToList();
        var overrunCount = overrunEligible.Count(o => o.Final > o.EstimatedCost);
        var avgOverrunPct = overrunEligible.Count > 0
            ? Round1(overrunEligible.Average(o => (o.Final - o.EstimatedCost) / o.EstimatedCost * 100m))
            : (decimal?)null;

        // Verdiepingen (dashboard shows the top 10 of the full aggregations).
        var topCustomers = (await AggregateCustomersAsync(active, cancellationToken)).Take(10).ToList();
        var kmPerDriver = (await AggregateDriversAsync(active, cancellationToken)).Take(10).ToList();

        return new KpiDashboardDto(
            revenueToday, revenuePeriod, profitToday, profitPeriod,
            revenuePeriod > 0 ? Round1(profitPeriod / revenuePeriod * 100m) : null,
            active.Count > 0 ? Round2(profitPeriod / active.Count) : null,
            active.Count,
            availableHours > 0 ? Round1(activeHours / availableHours * 100m) : null,
            Round1(totalKm), Round1(emptyKm),
            totalKm > 0 ? Round1(emptyKm / totalKm * 100m) : null,
            Round1(fuel.Sum(f => f.Litres)), Round2(fuel.Sum(f => f.TotalAmount)), Round1(co2),
            openDamage,
            execution.ReliabilityPct, execution.OnTimePct, execution.AvgEtaDeviationMinutes,
            execution.Failed, execution.Partial,
            openExceptions, overrunCount, avgOverrunPct,
            topCustomers, kmPerDriver);
    }

    public async Task<IReadOnlyList<TripProfitabilityRowDto>> GetTripProfitabilityAsync(
        KpiFilter filter, CancellationToken cancellationToken)
    {
        var facts = await LoadTripFactsAsync(filter, cancellationToken);
        var customerNames = await CustomerNamesAsync(
            facts.SelectMany(f => f.Orders).Select(o => o.CustomerId).Distinct().ToList(), cancellationToken);
        var driverNames = await DriverNamesAsync(
            facts.Where(f => f.DriverId is not null).Select(f => f.DriverId!.Value).Distinct().ToList(), cancellationToken);
        var vehicleNumbers = await VehicleNumbersAsync(
            facts.Where(f => f.VehicleId is not null).Select(f => f.VehicleId!.Value).Distinct().ToList(), cancellationToken);

        return facts
            .Where(f => f.Status != TripStatus.Cancelled)
            .OrderBy(f => f.TripDate).ThenBy(f => f.TripNumber)
            .Select(f =>
            {
                var revenue = Round2(f.MatchedRevenue);
                var cost = Round2(f.Cost * f.CostShare);
                var profit = Round2(revenue - cost);
                return new TripProfitabilityRowDto(
                    f.Id, f.TripNumber, f.TripDate,
                    f.DriverId is { } d ? driverNames.GetValueOrDefault(d) : null,
                    f.VehicleId is { } v ? vehicleNumbers.GetValueOrDefault(v) : null,
                    string.Join(", ", f.Orders.Where(o => o.Matched)
                        .Select(o => customerNames.GetValueOrDefault(o.CustomerId, "?")).Distinct().OrderBy(n => n)),
                    revenue, Round2(f.EstimatedCost), Round2(f.ProjectedCost), f.FinalCost,
                    profit, revenue > 0 ? Round1(profit / revenue * 100m) : null,
                    Round1(f.Km), Round1(f.EmptyKm), f.Status.ToString(), f.IsFinalized);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<KpiCustomerRowDto>> GetCustomerProfitabilityAsync(
        KpiFilter filter, CancellationToken cancellationToken)
    {
        var facts = await LoadTripFactsAsync(filter, cancellationToken);
        return await AggregateCustomersAsync(
            facts.Where(f => f.Status != TripStatus.Cancelled).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<KpiDriverKmRowDto>> GetDriverEffortAsync(
        KpiFilter filter, CancellationToken cancellationToken)
    {
        var facts = await LoadTripFactsAsync(filter, cancellationToken);
        return await AggregateDriversAsync(
            facts.Where(f => f.Status != TripStatus.Cancelled).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<KpiVehicleUtilisationRowDto>> GetVehicleUtilisationAsync(
        KpiFilter filter, CancellationToken cancellationToken)
    {
        var facts = await LoadTripFactsAsync(filter, cancellationToken);
        var active = facts.Where(f => f.Status != TripStatus.Cancelled && f.VehicleId is not null).ToList();
        var vehicleIds = active.Select(f => f.VehicleId!.Value).Distinct().ToList();
        var vehicles = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == _tenantContext.TenantId && vehicleIds.Contains(v.Id))
                .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                .ToListAsync(cancellationToken);
        var vehicleById = vehicles.ToDictionary(v => v.Id);
        var availablePerVehicle = Workdays(filter.From, filter.To) * AvailableHoursPerVehiclePerWorkday;

        return active
            .GroupBy(f => f.VehicleId!.Value)
            .Select(g =>
            {
                vehicleById.TryGetValue(g.Key, out var vehicle);
                var hours = Round1(g.Sum(f => f.Hours ?? 0m));
                return new KpiVehicleUtilisationRowDto(
                    g.Key, vehicle?.InternalNumber ?? "?", vehicle?.LicensePlate ?? "?",
                    g.Count(), hours, Round1(g.Sum(f => f.Km)),
                    availablePerVehicle > 0 ? Round1(hours / availablePerVehicle * 100m) : null);
            })
            .OrderByDescending(v => v.Km)
            .ToList();
    }

    // ---------------------------------------------------------- aggregation

    private async Task<List<KpiCustomerRowDto>> AggregateCustomersAsync(
        IReadOnlyList<TripFacts> active, CancellationToken cancellationToken)
    {
        var customerNames = await CustomerNamesAsync(
            active.SelectMany(f => f.Orders).Select(o => o.CustomerId).Distinct().ToList(), cancellationToken);
        return active
            .SelectMany(f => f.Orders.Where(o => o.Matched).Select(o => new
            {
                o.CustomerId,
                o.Revenue,
                Cost = f.Revenue > 0
                    ? f.Cost * (o.Revenue / f.Revenue)
                    : f.Orders.Count > 0 ? f.Cost / f.Orders.Count : 0m,
            }))
            .GroupBy(x => x.CustomerId)
            .Select(g =>
            {
                var revenue = Round2(g.Sum(x => x.Revenue));
                var cost = Round2(g.Sum(x => x.Cost));
                var profit = Round2(revenue - cost);
                return new KpiCustomerRowDto(
                    g.Key, customerNames.GetValueOrDefault(g.Key, "?"), revenue, cost, profit,
                    revenue > 0 ? Round1(profit / revenue * 100m) : null);
            })
            .OrderByDescending(c => c.Revenue)
            .ToList();
    }

    private async Task<List<KpiDriverKmRowDto>> AggregateDriversAsync(
        IReadOnlyList<TripFacts> active, CancellationToken cancellationToken)
    {
        var driverNames = await DriverNamesAsync(
            active.Where(f => f.DriverId is not null).Select(f => f.DriverId!.Value).Distinct().ToList(), cancellationToken);
        return active
            .Where(f => f.DriverId is not null)
            .GroupBy(f => f.DriverId!.Value)
            .Select(g => new KpiDriverKmRowDto(
                g.Key, driverNames.GetValueOrDefault(g.Key, "?"),
                Round1(g.Sum(f => f.Km)), Round1(g.Sum(f => f.Hours ?? 0m))))
            .OrderByDescending(d => d.Km)
            .ToList();
    }

    // -------------------------------------------------------------- loading

    private async Task<List<TripFacts>> LoadTripFactsAsync(KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tripQuery = _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.TripDate >= filter.From && t.TripDate <= filter.To);
        if (filter.DriverId is { } driverId)
        {
            tripQuery = tripQuery.Where(t => t.DriverId == driverId);
        }

        if (filter.VehicleId is { } vehicleId)
        {
            tripQuery = tripQuery.Where(t => t.VehicleId == vehicleId);
        }

        var trips = await tripQuery
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .Take(MaxTrips)
            .Select(t => new
            {
                t.Id, t.TripNumber, t.TripDate, t.Status, t.DriverId, t.VehicleId,
                t.PlannedStart, t.PlannedEnd,
                t.PlannedDistanceKm, t.PlannedEmptyKm, t.ActualDistanceKm, t.ActualEmptyKm,
            })
            .ToListAsync(cancellationToken);
        var tripIds = trips.Select(t => t.Id).ToList();

        var tripOrders = tripIds.Count == 0
            ? []
            : await _dbContext.TripOrders.AsNoTracking()
                .Where(to => to.TenantId == tenantId && tripIds.Contains(to.TripId))
                .Join(_dbContext.TransportOrders.AsNoTracking()
                        .Where(o => o.TenantId == tenantId && o.Status != TransportOrderStatus.Cancelled),
                    to => to.TransportOrderId, o => o.Id,
                    (to, o) => new { to.TripId, o.Id, o.OrderNumber, o.CustomerId, o.AgreedPrice })
                .ToListAsync(cancellationToken);
        var ordersByTrip = tripOrders.ToLookup(o => o.TripId);

        var summaries = tripIds.Count == 0
            ? []
            : await _dbContext.TripCostSummaries.AsNoTracking()
                .Where(s => s.TenantId == tenantId && tripIds.Contains(s.TripId))
                .ToListAsync(cancellationToken);
        var summaryByTrip = summaries.ToDictionary(s => s.TripId);

        var actuals = tripIds.Count == 0
            ? []
            : await _dbContext.TripPlanningEntries.AsNoTracking()
                .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId))
                .Select(e => new { e.TripId, e.ActualStart, e.ActualEnd })
                .ToListAsync(cancellationToken);
        var actualsByTrip = actuals.ToDictionary(a => a.TripId);

        var facts = new List<TripFacts>(trips.Count);
        foreach (var trip in trips)
        {
            var orders = ordersByTrip[trip.Id]
                .Select(o => new OrderFacts(
                    o.Id, o.OrderNumber, o.CustomerId, Round2(o.AgreedPrice ?? 0m),
                    filter.CustomerId is not { } customer || o.CustomerId == customer))
                .ToList();
            if (filter.CustomerId is not null && !orders.Any(o => o.Matched))
            {
                continue; // customer filter: only trips carrying at least one matching order
            }

            var revenue = orders.Sum(o => o.Revenue);
            var matchedRevenue = orders.Where(o => o.Matched).Sum(o => o.Revenue);
            // Cost share follows the matched revenue share; equal split when the trip has no revenue.
            var costShare = filter.CustomerId is null
                ? 1m
                : revenue > 0
                    ? matchedRevenue / revenue
                    : orders.Count > 0 ? (decimal)orders.Count(o => o.Matched) / orders.Count : 0m;

            summaryByTrip.TryGetValue(trip.Id, out var summary);
            var estimated = summary?.EstimatedTotal ?? 0m;
            var projected = summary?.ProjectedTotal ?? 0m;
            var final = summary is { IsFinalized: true } ? summary.FinalCost : null;
            var cost = final ?? (projected > 0 ? projected : estimated);

            actualsByTrip.TryGetValue(trip.Id, out var actual);
            var hours = DurationHours(actual?.ActualStart, actual?.ActualEnd)
                        ?? DurationHours(trip.PlannedStart, trip.PlannedEnd);

            facts.Add(new TripFacts(
                trip.Id, trip.TripNumber, trip.TripDate, trip.Status, trip.DriverId, trip.VehicleId,
                revenue, matchedRevenue, costShare,
                cost, estimated, projected, final, summary?.IsFinalized ?? false,
                trip.ActualDistanceKm ?? trip.PlannedDistanceKm ?? 0m,
                trip.ActualEmptyKm ?? trip.PlannedEmptyKm ?? 0m,
                hours, orders));
        }

        return facts;
    }

    private sealed record ExecutionKpis(
        decimal? ReliabilityPct, decimal? OnTimePct, decimal? AvgEtaDeviationMinutes, int Failed, int Partial);

    private async Task<ExecutionKpis> LoadExecutionKpisAsync(
        IReadOnlyList<Guid> tripIds, CancellationToken cancellationToken)
    {
        if (tripIds.Count == 0)
        {
            return new ExecutionKpis(null, null, null, 0, 0);
        }

        var tenantId = _tenantContext.TenantId;

        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId))
            .Select(e => new { e.TripId, e.TransportOrderStopId, e.Status, e.ArrivedAt })
            .ToListAsync(cancellationToken);

        var orderIds = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && tripIds.Contains(to.TripId))
            .Select(to => to.TransportOrderId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var stops = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .Select(s => new { s.Id, s.StopType, s.ConfirmedTo, s.RequestedTo, s.PlannedTo })
                .ToListAsync(cancellationToken);
        var stopById = stops.ToDictionary(s => s.Id);

        // Leverbetrouwbaarheid: succesvol afgeronde losstops / geplande losstops.
        var unloadingStopIds = stops.Where(s => s.StopType == StopType.Unloading).Select(s => s.Id).ToHashSet();
        var plannedDeliveries = unloadingStopIds.Count;
        var successfulDeliveries = executions.Count(e =>
            unloadingStopIds.Contains(e.TransportOrderStopId) && e.Status == StopExecutionStatus.Completed);

        // Stiptheid: aankomst binnen het geldende venster (bevestigd > gevraagd > gepland).
        var windowed = executions
            .Where(e => e.ArrivedAt is not null && stopById.ContainsKey(e.TransportOrderStopId))
            .Select(e => new
            {
                e.ArrivedAt,
                Window = stopById[e.TransportOrderStopId] is { } s ? s.ConfirmedTo ?? s.RequestedTo ?? s.PlannedTo : null,
            })
            .Where(x => x.Window is not null)
            .ToList();
        var onTime = windowed.Count(x => x.ArrivedAt <= x.Window);

        // ETA-afwijking: aankomst − recentste ETA-snapshot vóór aankomst.
        var arrived = executions.Where(e => e.ArrivedAt is not null).ToList();
        decimal? avgDeviation = null;
        if (arrived.Count > 0)
        {
            var etas = await _dbContext.StopEtas.AsNoTracking()
                .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId))
                .Select(e => new { e.Id, e.TripId, e.TransportOrderStopId })
                .ToListAsync(cancellationToken);
            var etaByStop = etas.ToDictionary(e => (e.TripId, e.TransportOrderStopId), e => e.Id);
            var etaIds = etas.Select(e => e.Id).ToList();
            var history = etaIds.Count == 0
                ? []
                : await _dbContext.StopEtaHistories.AsNoTracking()
                    .Where(h => h.TenantId == tenantId && etaIds.Contains(h.StopEtaId))
                    .Select(h => new { h.StopEtaId, h.Eta, h.RecordedAt })
                    .ToListAsync(cancellationToken);
            var historyByEta = history.ToLookup(h => h.StopEtaId);

            var deviations = new List<decimal>();
            foreach (var execution in arrived)
            {
                if (!etaByStop.TryGetValue((execution.TripId, execution.TransportOrderStopId), out var etaId))
                {
                    continue;
                }

                var snapshot = historyByEta[etaId]
                    .Where(h => h.RecordedAt <= execution.ArrivedAt)
                    .OrderByDescending(h => h.RecordedAt)
                    .FirstOrDefault();
                if (snapshot is null)
                {
                    continue;
                }

                deviations.Add((decimal)(execution.ArrivedAt!.Value - snapshot.Eta).TotalMinutes);
            }

            if (deviations.Count > 0)
            {
                avgDeviation = Round1(deviations.Average());
            }
        }

        return new ExecutionKpis(
            plannedDeliveries > 0 ? Round1((decimal)successfulDeliveries / plannedDeliveries * 100m) : null,
            windowed.Count > 0 ? Round1((decimal)onTime / windowed.Count * 100m) : null,
            avgDeviation,
            executions.Count(e => e.Status == StopExecutionStatus.Failed),
            executions.Count(e => e.Status == StopExecutionStatus.PartiallyCompleted));
    }

    /// <summary>Available hours = active vehicles × Mon–Fri workdays in range × 8h (documented convention).</summary>
    private async Task<decimal> AvailableVehicleHoursAsync(KpiFilter filter, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var vehicleCount = filter.VehicleId is { } vehicleId
            ? await _dbContext.Vehicles.AsNoTracking()
                .CountAsync(v => v.TenantId == tenantId && v.Id == vehicleId && v.IsActive, cancellationToken)
            : await _dbContext.Vehicles.AsNoTracking()
                .CountAsync(v => v.TenantId == tenantId && v.IsActive, cancellationToken);

        return vehicleCount * Workdays(filter.From, filter.To) * AvailableHoursPerVehiclePerWorkday;
    }

    internal static int Workdays(DateOnly from, DateOnly to)
    {
        var workdays = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                workdays += 1;
            }
        }

        return workdays;
    }

    private Task<Dictionary<Guid, string>> CustomerNamesAsync(
        IReadOnlyList<Guid> customerIds, CancellationToken cancellationToken) =>
        customerIds.Count == 0
            ? Task.FromResult(new Dictionary<Guid, string>())
            : _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == _tenantContext.TenantId && customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

    private Task<Dictionary<Guid, string>> DriverNamesAsync(
        IReadOnlyList<Guid> driverIds, CancellationToken cancellationToken) =>
        driverIds.Count == 0
            ? Task.FromResult(new Dictionary<Guid, string>())
            : _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    private Task<Dictionary<Guid, string>> VehicleNumbersAsync(
        IReadOnlyList<Guid> vehicleIds, CancellationToken cancellationToken) =>
        vehicleIds.Count == 0
            ? Task.FromResult(new Dictionary<Guid, string>())
            : _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == _tenantContext.TenantId && vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.InternalNumber, cancellationToken);

    private static decimal? DurationHours(DateTime? start, DateTime? end) =>
        start is { } s && end is { } e && e > s ? (decimal)(e - s).TotalHours : null;

    private static decimal Round2(decimal value) => Math.Round(value, 2);
    private static decimal Round1(decimal value) => Math.Round(value, 1);
}
