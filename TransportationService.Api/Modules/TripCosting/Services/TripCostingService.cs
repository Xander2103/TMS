using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.TripCosting.Services;

public class TripCostingService : ITripCostingService
{
    private const string EntityType = "TripCosting";
    private const int FuelHistoryWindow = 50;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly ICostRateService _costRateService;
    private readonly TimeProvider _timeProvider;

    public TripCostingService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        ICostRateService costRateService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _costRateService = costRateService;
        _timeProvider = timeProvider;
    }

    private IQueryable<Trip> Trips() => _dbContext.Trips.Where(t => t.TenantId == _tenantContext.TenantId);

    // ------------------------------------------------------------------ read

    public async Task<TripCostingDto?> GetAsync(Guid tripId, bool includeProfitability, CancellationToken cancellationToken)
    {
        var trip = await Trips().AsNoTracking().Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var lines = await _dbContext.TripCostLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TripId == tripId)
            .OrderBy(l => l.Phase).ThenBy(l => l.CostType).ThenBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
        var summary = await _dbContext.TripCostSummaries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId && s.TripId == tripId, cancellationToken);

        return await MapAsync(trip, lines, summary, includeProfitability, cancellationToken);
    }

    // ------------------------------------------------------------- mutations

    public async Task<CostingOperationResult> RecalculateAsync(
        Guid tripId, TripCostPhase phase, CancellationToken cancellationToken)
    {
        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn definitief afgerond.");
        }

        if (phase == TripCostPhase.Estimated && trip.Status is not (TripStatus.Draft or TripStatus.Planned))
        {
            return CostingOperationResult.InvalidState(
                "De schatting kan alleen herberekend worden zolang de rit concept of gepland is.");
        }

        if (phase == TripCostPhase.Actual && trip.Status is TripStatus.Draft or TripStatus.Planned)
        {
            return CostingOperationResult.InvalidState(
                "Werkelijke kosten kunnen pas berekend worden zodra de rit gestart is.");
        }

        await StageRecalculationCoreAsync(trip, phase, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), $"Recalculated{phase}", null,
            new { trip.TripNumber, Phase = phase.ToString() }, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> AddManualLineAsync(
        Guid tripId, AddCostLineRequest request, CancellationToken cancellationToken)
    {
        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn definitief afgerond.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return CostingOperationResult.Invalid("Een omschrijving is verplicht.");
        }

        var amount = Math.Round(request.Quantity * request.UnitRate, 2);
        if (amount < 0 && request.CostType != TripCostType.Correction)
        {
            return CostingOperationResult.Invalid("Alleen correctieregels mogen negatief zijn.");
        }

        var line = new TripCostLine
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            Phase = request.Phase,
            CostType = request.CostType,
            Description = request.Description.Trim(),
            Quantity = request.Quantity,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "stuk" : request.Unit.Trim(),
            UnitRate = request.UnitRate,
            Amount = amount,
            Source = TripCostLine.SourceManual,
            CalculatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _dbContext.Add(line);
        await RefreshSummaryStagedAsync(trip, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("TripCostLine", line.Id.ToString(), "Created", null,
            new { line.TripId, line.Phase, line.CostType, line.Description, line.Amount }, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> OverrideLineAsync(
        Guid tripId, Guid lineId, OverrideCostLineRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return CostingOperationResult.Invalid("Een reden voor de correctie is verplicht.");
        }

        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn definitief afgerond.");
        }

        var line = await _dbContext.TripCostLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.TripId == tripId
                                      && l.TenantId == _tenantContext.TenantId, cancellationToken);
        if (line is null)
        {
            return CostingOperationResult.NotFound;
        }

        var before = new { line.Amount, line.IsManualOverride, line.OverrideReason };
        line.Amount = Math.Round(request.Amount, 2);
        line.IsManualOverride = true;
        line.OverrideReason = request.Reason.Trim();
        await RefreshSummaryStagedAsync(trip, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("TripCostLine", line.Id.ToString(), "Overridden", before,
            new { line.Amount, line.OverrideReason }, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> DeleteLineAsync(Guid tripId, Guid lineId, CancellationToken cancellationToken)
    {
        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn definitief afgerond.");
        }

        var line = await _dbContext.TripCostLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.TripId == tripId
                                      && l.TenantId == _tenantContext.TenantId, cancellationToken);
        if (line is null)
        {
            return CostingOperationResult.NotFound;
        }

        if (line.Source != TripCostLine.SourceManual)
        {
            return CostingOperationResult.InvalidState(
                "Alleen handmatige kostenregels kunnen worden verwijderd; overschrijf berekende regels.");
        }

        _dbContext.Remove(line); // soft delete via interceptor
        await RefreshSummaryStagedAsync(trip, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("TripCostLine", line.Id.ToString(), "Deleted",
            new { line.TripId, line.CostType, line.Amount }, null, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> UpdateActualsAsync(
        Guid tripId, UpdateTripActualsRequest request, CancellationToken cancellationToken)
    {
        if (request.ActualDistanceKm is < 0 || request.ActualEmptyKm is < 0)
        {
            return CostingOperationResult.Invalid("Afstanden kunnen niet negatief zijn.");
        }

        if (request.ActualDistanceKm is { } total && request.ActualEmptyKm is { } empty && empty > total)
        {
            return CostingOperationResult.Invalid("Lege kilometers kunnen niet groter zijn dan de totale afstand.");
        }

        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn definitief afgerond.");
        }

        var before = new { trip.ActualDistanceKm, trip.ActualEmptyKm };
        trip.ActualDistanceKm = request.ActualDistanceKm;
        trip.ActualEmptyKm = request.ActualEmptyKm;

        if (trip.Status is TripStatus.InProgress or TripStatus.Completed)
        {
            await StageRecalculationCoreAsync(trip, TripCostPhase.Actual, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "ActualsUpdated", before,
            new { trip.ActualDistanceKm, trip.ActualEmptyKm }, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> FinalizeAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await Trips().Include(t => t.Orders).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        if (trip.Status is not (TripStatus.Completed or TripStatus.Cancelled))
        {
            return CostingOperationResult.InvalidState("Alleen afgeronde of geannuleerde ritten kunnen worden afgesloten.");
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn al afgerond.");
        }

        // Freeze on the freshest numbers: completed trips get a final actual pass first.
        if (trip.Status == TripStatus.Completed)
        {
            await StageRecalculationCoreAsync(trip, TripCostPhase.Actual, cancellationToken);
        }

        summary = await RefreshSummaryStagedAsync(trip, cancellationToken);
        summary.IsFinalized = true;
        summary.FinalCost = summary.ProjectedTotal;
        summary.FinalRevenue = summary.Revenue;
        summary.FinalizedAt = _timeProvider.GetUtcNow().UtcDateTime;
        summary.FinalizedByUserId = _currentUserContext.CurrentUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "Finalized", null,
            new { trip.TripNumber, summary.FinalCost, summary.FinalRevenue }, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task<CostingOperationResult> ReopenAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await Trips().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return CostingOperationResult.NotFound;
        }

        var summary = await LoadSummaryAsync(tripId, cancellationToken);
        if (summary is not { IsFinalized: true })
        {
            return CostingOperationResult.InvalidState("De kosten van deze rit zijn niet afgerond.");
        }

        var before = new { summary.FinalCost, summary.FinalRevenue, summary.FinalizedAt };
        summary.IsFinalized = false;
        summary.FinalCost = null;
        summary.FinalRevenue = null;
        summary.FinalizedAt = null;
        summary.FinalizedByUserId = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, trip.Id.ToString(), "Reopened", before, null, cancellationToken);

        return CostingOperationResult.Success((await GetAsync(tripId, true, cancellationToken))!);
    }

    public async Task StageRecalculationAsync(Trip trip, TripCostPhase phase, CancellationToken cancellationToken)
    {
        var summary = await LoadSummaryAsync(trip.Id, cancellationToken);
        if (summary is { IsFinalized: true })
        {
            return;
        }

        if (phase == TripCostPhase.Estimated && trip.Status is not (TripStatus.Draft or TripStatus.Planned))
        {
            return;
        }

        if (phase == TripCostPhase.Actual && trip.Status is TripStatus.Draft or TripStatus.Planned)
        {
            return;
        }

        await StageRecalculationCoreAsync(trip, phase, cancellationToken);
    }

    // ------------------------------------------------------------ calculation

    private async Task StageRecalculationCoreAsync(Trip trip, TripCostPhase phase, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.TripCostLines
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TripId == trip.Id && l.Phase == phase)
            .ToListAsync(cancellationToken);

        // Manual lines and manual overrides survive every recalculation.
        var replaceable = existing.Where(l => l.Source != TripCostLine.SourceManual && !l.IsManualOverride).ToList();
        var overriddenTypes = existing
            .Where(l => l.IsManualOverride)
            .Select(l => l.CostType)
            .ToHashSet();
        _dbContext.RemoveRange(replaceable);

        var rates = await _costRateService.GetForDateAsync(trip.TripDate, cancellationToken);
        if (rates is not null)
        {
            var generated = phase == TripCostPhase.Estimated
                ? await BuildEstimatedLinesAsync(trip, rates, cancellationToken)
                : await BuildActualLinesAsync(trip, rates, cancellationToken);
            foreach (var line in generated.Where(l => !overriddenTypes.Contains(l.CostType)))
            {
                _dbContext.Add(line);
            }
        }

        await RefreshSummaryStagedAsync(trip, cancellationToken);
    }

    private async Task<List<TripCostLine>> BuildEstimatedLinesAsync(
        Trip trip, CostRateSet rates, CancellationToken cancellationToken)
    {
        var vehicle = await LoadVehicleAsync(trip.VehicleId, cancellationToken);
        var distance = trip.PlannedDistanceKm;
        var hours = DurationHours(trip.PlannedStart, trip.PlannedEnd);
        var consumption = vehicle?.ConsumptionLPer100Km ?? rates.DefaultConsumptionLPer100Km;

        var lines = new List<TripCostLine>();

        if (distance is { } km && km > 0 && consumption > 0 && rates.FuelPricePerLitre > 0)
        {
            var litres = Math.Round(km * consumption / 100m, 1);
            lines.Add(Line(trip, TripCostPhase.Estimated, TripCostType.Fuel,
                $"Brandstof: {km:0.#} km × {consumption:0.#} l/100km", litres, "l", rates.FuelPricePerLitre));
        }

        if (rates.DefaultTollPerTrip > 0)
        {
            lines.Add(Line(trip, TripCostPhase.Estimated, TripCostType.Toll,
                "Tol/Maut (standaardraming)", 1, "forfait", rates.DefaultTollPerTrip));
        }

        if (hours is { } h && h > 0 && rates.DriverCostPerHour > 0)
        {
            var rate = Math.Round(rates.DriverCostPerHour * rates.EmployerCostMultiplier, 4);
            lines.Add(Line(trip, TripCostPhase.Estimated, TripCostType.DriverLabour,
                $"Chauffeur: {h:0.##} u × werkgeverslastenfactor {rates.EmployerCostMultiplier:0.##}",
                Math.Round(h, 2), "u", rate));
        }

        AddVehicleAndAssetLines(lines, trip, TripCostPhase.Estimated, rates, vehicle, distance, hours);

        return lines;
    }

    private async Task<List<TripCostLine>> BuildActualLinesAsync(
        Trip trip, CostRateSet rates, CancellationToken cancellationToken)
    {
        var vehicle = await LoadVehicleAsync(trip.VehicleId, cancellationToken);
        var distance = trip.ActualDistanceKm ?? trip.PlannedDistanceKm;
        var execution = await LoadExecutionWindowAsync(trip.Id, cancellationToken);
        var hours = DurationHours(execution.Start, execution.End) ?? DurationHours(trip.PlannedStart, trip.PlannedEnd);

        var lines = new List<TripCostLine>();

        // Fuel: real fill-ups on the trip date beat any computation.
        var fuel = trip.VehicleId is { } vehicleId
            ? await _dbContext.FuelTransactions.AsNoTracking()
                .Where(f => f.TenantId == _tenantContext.TenantId && f.VehicleId == vehicleId
                            && f.TransactionDate == trip.TripDate)
                .Select(f => new { f.Litres, f.TotalAmount })
                .ToListAsync(cancellationToken)
            : [];
        if (fuel.Count > 0 && fuel.Sum(f => f.TotalAmount) > 0)
        {
            var litres = fuel.Sum(f => f.Litres);
            var amount = fuel.Sum(f => f.TotalAmount);
            var rate = litres > 0 ? Math.Round(amount / litres, 4) : 0m;
            lines.Add(new TripCostLine
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TripId = trip.Id,
                Phase = TripCostPhase.Actual,
                CostType = TripCostType.Fuel,
                Description = $"Brandstof: {fuel.Count} tankbeurt(en) op {trip.TripDate:dd-MM-yyyy}",
                Quantity = litres,
                Unit = "l",
                UnitRate = rate,
                Amount = Math.Round(amount, 2),
                Source = TripCostLine.SourceFuelRecords,
                CalculatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            });
        }
        else if (distance is { } km && km > 0 && rates.FuelPricePerLitre > 0)
        {
            var consumption = await MeasuredConsumptionAsync(trip.VehicleId, cancellationToken)
                              ?? vehicle?.ConsumptionLPer100Km
                              ?? rates.DefaultConsumptionLPer100Km;
            if (consumption > 0)
            {
                var litres = Math.Round(km * consumption / 100m, 1);
                lines.Add(Line(trip, TripCostPhase.Actual, TripCostType.Fuel,
                    $"Brandstof: {km:0.#} km × {consumption:0.#} l/100km", litres, "l", rates.FuelPricePerLitre));
            }
        }

        if (hours is { } h && h > 0 && rates.DriverCostPerHour > 0)
        {
            var rate = Math.Round(rates.DriverCostPerHour * rates.EmployerCostMultiplier, 4);
            lines.Add(Line(trip, TripCostPhase.Actual, TripCostType.DriverLabour,
                $"Chauffeur: {h:0.##} u × werkgeverslastenfactor {rates.EmployerCostMultiplier:0.##}",
                Math.Round(h, 2), "u", rate));

            var thresholdHours = rates.OvertimeThresholdMinutesPerDay / 60m;
            var overtime = h - thresholdHours;
            if (overtime > 0 && rates.OvertimeRateMultiplier > 1)
            {
                var overtimeRate = Math.Round(
                    rates.DriverCostPerHour * (rates.OvertimeRateMultiplier - 1m) * rates.EmployerCostMultiplier, 4);
                lines.Add(Line(trip, TripCostPhase.Actual, TripCostType.Overtime,
                    $"Overuren boven {thresholdHours:0.#} u (toeslag ×{rates.OvertimeRateMultiplier:0.##})",
                    Math.Round(overtime, 2), "u", overtimeRate));
            }
        }

        if (rates.WaitingTimeCostPerHour > 0)
        {
            var waitingHours = await WaitingHoursAsync(trip, cancellationToken);
            if (waitingHours > 0)
            {
                lines.Add(Line(trip, TripCostPhase.Actual, TripCostType.WaitingTime,
                    "Wachttijd bovenop de standaard laad-/lostijd", Math.Round(waitingHours, 2), "u",
                    rates.WaitingTimeCostPerHour));
            }
        }

        AddVehicleAndAssetLines(lines, trip, TripCostPhase.Actual, rates, vehicle, distance, hours);

        return lines;
    }

    /// <summary>Distance/time/asset components shared by both phases.</summary>
    private void AddVehicleAndAssetLines(
        List<TripCostLine> lines, Trip trip, TripCostPhase phase, CostRateSet rates,
        Vehicle? vehicle, decimal? distance, decimal? hours)
    {
        if (distance is { } km && km > 0)
        {
            if (rates.VehicleCostPerKm > 0)
            {
                lines.Add(Line(trip, phase, TripCostType.VehicleDistance,
                    "Voertuigkosten per kilometer", km, "km", rates.VehicleCostPerKm));
            }

            if (rates.MaintenanceCostPerKm > 0)
            {
                lines.Add(Line(trip, phase, TripCostType.Maintenance,
                    "Onderhoudstoerekening per kilometer", km, "km", rates.MaintenanceCostPerKm));
            }
        }

        if (hours is { } h && h > 0 && rates.VehicleCostPerHour > 0)
        {
            lines.Add(Line(trip, phase, TripCostType.VehicleTime,
                "Voertuigkosten per uur", Math.Round(h, 2), "u", rates.VehicleCostPerHour));
        }

        if (rates.DepreciationPerDay > 0 && trip.VehicleId is not null)
        {
            lines.Add(Line(trip, phase, TripCostType.Depreciation, "Afschrijving voertuig", 1, "dag", rates.DepreciationPerDay));
        }

        if (rates.TrailerCostPerDay > 0 && trip.TrailerId is not null)
        {
            lines.Add(Line(trip, phase, TripCostType.Trailer, "Opleggerkosten", 1, "dag", rates.TrailerCostPerDay));
        }

        if (rates.EquipmentCostPerDay > 0 && vehicle is not null && (vehicle.HasCrane || vehicle.HasRefrigeration))
        {
            var equipment = vehicle.HasCrane ? "kraan" : "koeling";
            lines.Add(Line(trip, phase, TripCostType.Equipment,
                $"Toeslag speciale uitrusting ({equipment})", 1, "dag", rates.EquipmentCostPerDay));
        }
    }

    private TripCostLine Line(
        Trip trip, TripCostPhase phase, TripCostType type, string description,
        decimal quantity, string unit, decimal rate) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantContext.TenantId,
        TripId = trip.Id,
        Phase = phase,
        CostType = type,
        Description = description,
        Quantity = quantity,
        Unit = unit,
        UnitRate = rate,
        Amount = Math.Round(quantity * rate, 2),
        Source = TripCostLine.SourceCalculated,
        CalculatedAt = _timeProvider.GetUtcNow().UtcDateTime,
    };

    // ---------------------------------------------------------------- helpers

    private Task<Vehicle?> LoadVehicleAsync(Guid? vehicleId, CancellationToken cancellationToken) =>
        vehicleId is { } id
            ? _dbContext.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == id && v.TenantId == _tenantContext.TenantId, cancellationToken)
            : Task.FromResult<Vehicle?>(null);

    private static decimal? DurationHours(DateTime? start, DateTime? end) =>
        start is { } s && end is { } e && e > s ? (decimal)(e - s).TotalHours : null;

    private async Task<(DateTime? Start, DateTime? End)> LoadExecutionWindowAsync(
        Guid tripId, CancellationToken cancellationToken)
    {
        var executions = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TripId == tripId && e.TenantId == _tenantContext.TenantId)
            .Select(e => new { e.ArrivedAt, e.CompletedAt, e.DepartedAt })
            .ToListAsync(cancellationToken);
        var start = executions.Where(e => e.ArrivedAt != null).Min(e => e.ArrivedAt);
        var end = executions.Select(e => e.DepartedAt ?? e.CompletedAt).Where(t => t != null).DefaultIfEmpty(null).Max();
        return (start, end);
    }

    /// <summary>Distance-weighted fleet-average consumption from recent fuel records, when derivable.</summary>
    private async Task<decimal?> MeasuredConsumptionAsync(Guid? vehicleId, CancellationToken cancellationToken)
    {
        if (vehicleId is not { } id)
        {
            return null;
        }

        var transactions = await _dbContext.FuelTransactions.AsNoTracking()
            .Where(f => f.TenantId == _tenantContext.TenantId && f.VehicleId == id)
            .OrderByDescending(f => f.TransactionDate).ThenByDescending(f => f.CreatedAt)
            .Take(FuelHistoryWindow)
            .ToListAsync(cancellationToken);
        return FuelService.ComputeDerived(transactions).Average;
    }

    private async Task<decimal> WaitingHoursAsync(Trip trip, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => new { s.DefaultLoadingMinutes, s.DefaultUnloadingMinutes })
            .FirstOrDefaultAsync(cancellationToken);
        var loadingMinutes = settings?.DefaultLoadingMinutes ?? 30;
        var unloadingMinutes = settings?.DefaultUnloadingMinutes ?? 30;

        var stops = await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TripId == trip.Id && e.TenantId == _tenantContext.TenantId
                        && e.ArrivedAt != null && e.CompletedAt != null)
            .Join(_dbContext.TransportOrderStops.AsNoTracking(),
                e => e.TransportOrderStopId, s => s.Id,
                (e, s) => new { e.ArrivedAt, e.CompletedAt, s.StopType })
            .ToListAsync(cancellationToken);

        var totalMinutes = 0m;
        foreach (var stop in stops)
        {
            var handling = stop.StopType == StopType.Loading ? loadingMinutes : unloadingMinutes;
            var spent = (decimal)(stop.CompletedAt!.Value - stop.ArrivedAt!.Value).TotalMinutes;
            totalMinutes += Math.Max(0m, spent - handling);
        }

        return totalMinutes / 60m;
    }

    private Task<TripCostSummary?> LoadSummaryAsync(Guid tripId, CancellationToken cancellationToken) =>
        _dbContext.TripCostSummaries
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId && s.TripId == tripId, cancellationToken);

    /// <summary>Recomputes the summary from the STAGED line set (tracked adds/removes included).</summary>
    private async Task<TripCostSummary> RefreshSummaryStagedAsync(Trip trip, CancellationToken cancellationToken)
    {
        var stored = await _dbContext.TripCostLines
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TripId == trip.Id)
            .ToListAsync(cancellationToken);
        // Merge with what this unit of work already staged: adds not yet saved, removes excluded.
        var staged = _dbContext.ChangeTracker.Entries<TripCostLine>()
            .Where(e => e.Entity.TripId == trip.Id)
            .ToList();
        var lines = stored
            .Concat(staged.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .Where(l => staged.FirstOrDefault(e => e.Entity.Id == l.Id)?.State != EntityState.Deleted && !l.IsDeleted)
            .DistinctBy(l => l.Id)
            .ToList();

        var summary = await LoadSummaryAsync(trip.Id, cancellationToken);
        if (summary is null)
        {
            summary = new TripCostSummary { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, TripId = trip.Id };
            _dbContext.Add(summary);
        }

        summary.EstimatedTotal = Total(lines, TripCostPhase.Estimated);
        summary.ActualTotal = Total(lines, TripCostPhase.Actual);
        summary.ProjectedTotal = ProjectedTotal(lines);
        summary.Revenue = await RevenueAsync(trip, cancellationToken);
        return summary;
    }

    private static decimal Total(IReadOnlyList<TripCostLine> lines, TripCostPhase phase) =>
        Math.Round(lines.Where(l => l.Phase == phase).Sum(l => l.Amount), 2);

    /// <summary>Per-cost-type merge: actual wins for types that have any actual line; estimate fills the rest.</summary>
    private static decimal ProjectedTotal(IReadOnlyList<TripCostLine> lines)
    {
        var actualTypes = lines.Where(l => l.Phase == TripCostPhase.Actual).Select(l => l.CostType).ToHashSet();
        var total = lines
            .Where(l => l.Phase == TripCostPhase.Actual || !actualTypes.Contains(l.CostType))
            .Sum(l => l.Amount);
        return Math.Round(total, 2);
    }

    private async Task<decimal> RevenueAsync(Trip trip, CancellationToken cancellationToken)
    {
        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        if (orderIds.Count == 0)
        {
            return 0m;
        }

        var revenue = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == _tenantContext.TenantId && orderIds.Contains(o.Id)
                        && o.Status != TransportOrderStatus.Cancelled)
            .SumAsync(o => o.AgreedPrice ?? 0m, cancellationToken);
        return Math.Round(revenue, 2);
    }

    // ---------------------------------------------------------------- mapping

    private async Task<TripCostingDto> MapAsync(
        Trip trip, IReadOnlyList<TripCostLine> lines, TripCostSummary? summary,
        bool includeProfitability, CancellationToken cancellationToken)
    {
        var estimated = summary?.EstimatedTotal ?? Total(lines, TripCostPhase.Estimated);
        var actual = summary?.ActualTotal ?? Total(lines, TripCostPhase.Actual);
        var projected = summary?.ProjectedTotal ?? ProjectedTotal(lines);

        TripProfitabilityDto? profitability = null;
        if (includeProfitability)
        {
            profitability = await BuildProfitabilityAsync(trip, summary, projected, estimated, cancellationToken);
        }

        return new TripCostingDto(
            trip.Id, trip.TripNumber, trip.Status,
            summary?.IsFinalized ?? false, summary?.FinalizedAt,
            estimated, actual, projected, summary?.FinalCost,
            trip.PlannedDistanceKm, trip.PlannedEmptyKm, trip.ActualDistanceKm, trip.ActualEmptyKm,
            lines.Select(l => new TripCostLineDto(
                l.Id, l.Phase, l.CostType, l.Description, l.Quantity, l.Unit, l.UnitRate, l.Amount,
                l.Source, l.IsManualOverride, l.OverrideReason, l.CalculatedAt)).ToList(),
            profitability);
    }

    private async Task<TripProfitabilityDto> BuildProfitabilityAsync(
        Trip trip, TripCostSummary? summary, decimal projected, decimal estimated, CancellationToken cancellationToken)
    {
        var cost = summary is { IsFinalized: true, FinalCost: { } finalCost }
            ? finalCost
            : projected > 0 ? projected : estimated;
        var revenue = summary is { IsFinalized: true, FinalRevenue: { } finalRevenue }
            ? finalRevenue
            : await RevenueAsync(trip, cancellationToken);

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var orders = orderIds.Count == 0
            ? []
            : await (from o in _dbContext.TransportOrders.AsNoTracking()
                         .Where(o => o.TenantId == _tenantContext.TenantId && orderIds.Contains(o.Id)
                                     && o.Status != TransportOrderStatus.Cancelled)
                     join c in _dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId)
                         on o.CustomerId equals c.Id
                     select new { o.Id, o.OrderNumber, o.CustomerId, CustomerName = c.Name, o.AgreedPrice })
                .ToListAsync(cancellationToken);

        // Allocation: proportional to each order's revenue share; equal split when revenue is zero.
        var allocations = new List<TripOrderAllocationDto>(orders.Count);
        foreach (var order in orders)
        {
            var orderRevenue = Math.Round(order.AgreedPrice ?? 0m, 2);
            var share = revenue > 0
                ? orderRevenue / revenue
                : orders.Count > 0 ? 1m / orders.Count : 0m;
            var allocatedCost = Math.Round(cost * share, 2);
            var profit = Math.Round(orderRevenue - allocatedCost, 2);
            allocations.Add(new TripOrderAllocationDto(
                order.Id, order.OrderNumber, order.CustomerName, order.CustomerId,
                orderRevenue, allocatedCost, profit,
                orderRevenue > 0 ? Math.Round(profit / orderRevenue * 100m, 1) : null));
        }

        var grossProfit = Math.Round(revenue - cost, 2);
        var distance = trip.ActualDistanceKm ?? trip.PlannedDistanceKm;
        var hours = DurationHours(trip.PlannedStart, trip.PlannedEnd);
        var executionWindow = await LoadExecutionWindowAsync(trip.Id, cancellationToken);
        hours = DurationHours(executionWindow.Start, executionWindow.End) ?? hours;

        return new TripProfitabilityDto(
            revenue, cost, grossProfit,
            revenue > 0 ? Math.Round(grossProfit / revenue * 100m, 1) : null,
            distance is { } km && km > 0 ? Math.Round(revenue / km, 2) : null,
            distance is { } km2 && km2 > 0 ? Math.Round(cost / km2, 2) : null,
            hours is { } h && h > 0 ? Math.Round(revenue / h, 2) : null,
            hours is { } h2 && h2 > 0 ? Math.Round(cost / h2, 2) : null,
            allocations);
    }
}
