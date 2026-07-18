using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public class FuelService : IFuelService
{
    /// <summary>A consumption is flagged when it deviates more than this factor from the vehicle average.</summary>
    public const decimal OutlierFactor = 1.5m;

    /// <summary>Outlier detection needs at least this many computed consumptions to be meaningful.</summary>
    public const int MinSamplesForOutliers = 3;

    /// <summary>Tenant-wide scan window for the dashboard warnings feed.</summary>
    private const int WarningScanWindow = 500;

    private const string EntityType = "FuelTransaction";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public FuelService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public sealed record FuelDerived(decimal? ConsumptionLPer100Km, IReadOnlyList<FuelWarningCode> Warnings);

    /// <summary>
    /// Walks the chronological odometer trail of one vehicle. A full-tank fill with a higher odometer than
    /// the previous reading yields litres/100km; a lower odometer flags a registration error. Once at least
    /// <see cref="MinSamplesForOutliers"/> consumptions exist, values deviating more than
    /// <see cref="OutlierFactor"/> from the distance-weighted average are flagged as anomalies.
    /// </summary>
    public static (IReadOnlyDictionary<Guid, FuelDerived> PerTransaction, decimal? Average) ComputeDerived(
        IEnumerable<FuelTransaction> transactions)
    {
        var ordered = transactions
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

        var consumptions = new Dictionary<Guid, decimal>();
        var warnings = ordered.ToDictionary(t => t.Id, _ => new List<FuelWarningCode>());
        int? lastOdometer = null;
        decimal totalLitres = 0;
        decimal totalKm = 0;

        foreach (var transaction in ordered)
        {
            if (transaction.OdometerKm is not { } odometer)
            {
                continue;
            }

            if (lastOdometer is { } previous)
            {
                if (odometer < previous)
                {
                    warnings[transaction.Id].Add(FuelWarningCode.OdometerLowerThanPrevious);
                }
                else if (odometer > previous && transaction.FullTank && transaction.Litres > 0)
                {
                    var distance = odometer - previous;
                    consumptions[transaction.Id] = Math.Round(transaction.Litres / distance * 100m, 1);
                    totalLitres += transaction.Litres;
                    totalKm += distance;
                }
            }

            lastOdometer = odometer;
        }

        decimal? average = totalKm > 0 ? Math.Round(totalLitres / totalKm * 100m, 1) : null;

        if (consumptions.Count >= MinSamplesForOutliers && average is { } avg && avg > 0)
        {
            foreach (var (id, consumption) in consumptions)
            {
                if (consumption > avg * OutlierFactor)
                {
                    warnings[id].Add(FuelWarningCode.ConsumptionAboveAverage);
                }
                else if (consumption < avg / OutlierFactor)
                {
                    warnings[id].Add(FuelWarningCode.ConsumptionBelowAverage);
                }
            }
        }

        var perTransaction = ordered.ToDictionary(
            t => t.Id,
            t => new FuelDerived(
                consumptions.TryGetValue(t.Id, out var c) ? c : null,
                warnings[t.Id]));

        return (perTransaction, average);
    }

    private IQueryable<FuelTransaction> TenantScoped() =>
        _dbContext.FuelTransactions.Where(f => f.TenantId == _tenantContext.TenantId);

    public async Task<FuelOverviewDto?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Vehicles.AnyAsync(
                v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        var transactions = await TenantScoped().AsNoTracking()
            .Where(f => f.VehicleId == vehicleId)
            .ToListAsync(cancellationToken);

        var (derived, average) = ComputeDerived(transactions);
        var names = await ResolveNamesAsync(transactions, cancellationToken);

        var items = transactions
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => Map(t, derived[t.Id], names))
            .ToList();

        return new FuelOverviewDto(
            items,
            average,
            transactions.Sum(t => t.Litres),
            transactions.Sum(t => t.TotalAmount));
    }

    public async Task<IReadOnlyList<FuelWarningDto>> ListRecentWarningsAsync(int take, CancellationToken cancellationToken)
    {
        // Bounded scan: derive warnings per vehicle inside a recent window instead of the full history.
        // The oldest row of each vehicle group has no baseline and yields no consumption, which is acceptable
        // for a dashboard feed.
        var window = await TenantScoped().AsNoTracking()
            .OrderByDescending(f => f.TransactionDate).ThenByDescending(f => f.CreatedAt)
            .Take(WarningScanWindow)
            .ToListAsync(cancellationToken);

        var flagged = new List<(FuelTransaction Transaction, FuelDerived Derived)>();
        foreach (var group in window.GroupBy(f => f.VehicleId))
        {
            var (derived, _) = ComputeDerived(group);
            flagged.AddRange(group
                .Where(t => derived[t.Id].Warnings.Count > 0)
                .Select(t => (t, derived[t.Id])));
        }

        var vehicleIds = flagged.Select(f => f.Transaction.VehicleId).Distinct().ToList();
        var vehicles = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && vehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        return flagged
            .OrderByDescending(f => f.Transaction.TransactionDate)
            .ThenByDescending(f => f.Transaction.CreatedAt)
            .Take(Math.Clamp(take, 1, 50))
            .Select(f => new FuelWarningDto(
                f.Transaction.Id,
                f.Transaction.VehicleId,
                vehicles.TryGetValue(f.Transaction.VehicleId, out var v) ? v.InternalNumber : string.Empty,
                vehicles.TryGetValue(f.Transaction.VehicleId, out var v2) ? v2.LicensePlate : string.Empty,
                f.Transaction.TransactionDate,
                f.Transaction.Litres,
                f.Derived.ConsumptionLPer100Km,
                f.Derived.Warnings))
            .ToList();
    }

    public async Task<FuelOperationResult> CreateForVehicleAsync(
        Guid vehicleId, CreateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(request.Litres, request.TotalAmount, request.OdometerKm);
        if (validationError is not null)
        {
            return FuelOperationResult.Invalid(validationError);
        }

        var vehicle = await _dbContext.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken);
        if (vehicle is null)
        {
            return FuelOperationResult.OwnerNotFound;
        }

        if (!await ReferencesInTenantAsync(request.DriverId, request.TankCardId, cancellationToken))
        {
            return FuelOperationResult.InvalidReference;
        }

        var transaction = new FuelTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            VehicleId = vehicleId,
            DriverId = request.DriverId,
            TankCardId = request.TankCardId,
            TransactionDate = request.TransactionDate,
            Litres = request.Litres,
            TotalAmount = request.TotalAmount,
            OdometerKm = request.OdometerKm,
            Station = Trim(request.Station),
            FullTank = request.FullTank,
            Notes = Trim(request.Notes),
        };

        // A refuelling with a higher odometer than the vehicle record advances the vehicle,
        // mirroring maintenance completion.
        if (request.OdometerKm is { } odometer && odometer > vehicle.OdometerKm)
        {
            vehicle.OdometerKm = odometer;
        }

        _dbContext.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, transaction.Id.ToString(), "Created", null,
            new { transaction.VehicleId, transaction.TransactionDate, transaction.Litres, transaction.TotalAmount }, cancellationToken);

        return FuelOperationResult.Success(await MapWithHistoryAsync(transaction, cancellationToken));
    }

    public async Task<FuelOperationResult> UpdateAsync(
        Guid id, UpdateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        var validationError = Validate(request.Litres, request.TotalAmount, request.OdometerKm);
        if (validationError is not null)
        {
            return FuelOperationResult.Invalid(validationError);
        }

        var transaction = await TenantScoped().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (transaction is null)
        {
            return FuelOperationResult.NotFound;
        }

        if (!await ReferencesInTenantAsync(request.DriverId, request.TankCardId, cancellationToken))
        {
            return FuelOperationResult.InvalidReference;
        }

        var before = new { transaction.TransactionDate, transaction.Litres, transaction.TotalAmount, transaction.OdometerKm };

        transaction.DriverId = request.DriverId;
        transaction.TankCardId = request.TankCardId;
        transaction.TransactionDate = request.TransactionDate;
        transaction.Litres = request.Litres;
        transaction.TotalAmount = request.TotalAmount;
        transaction.OdometerKm = request.OdometerKm;
        transaction.Station = Trim(request.Station);
        transaction.FullTank = request.FullTank;
        transaction.Notes = Trim(request.Notes);

        var vehicle = await _dbContext.Vehicles
            .FirstOrDefaultAsync(v => v.Id == transaction.VehicleId && v.TenantId == _tenantContext.TenantId, cancellationToken);
        if (vehicle is not null && request.OdometerKm is { } odometer && odometer > vehicle.OdometerKm)
        {
            vehicle.OdometerKm = odometer;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, transaction.Id.ToString(), "Updated", before,
            new { transaction.TransactionDate, transaction.Litres, transaction.TotalAmount, transaction.OdometerKm }, cancellationToken);

        return FuelOperationResult.Success(await MapWithHistoryAsync(transaction, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await TenantScoped().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (transaction is null)
        {
            return false;
        }

        _dbContext.Remove(transaction); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, transaction.Id.ToString(), "Deleted",
            new { transaction.VehicleId, transaction.TransactionDate, transaction.Litres }, null, cancellationToken);

        return true;
    }

    private static string? Validate(decimal litres, decimal totalAmount, int? odometerKm)
    {
        if (litres <= 0)
        {
            return "Het aantal liter moet groter zijn dan nul.";
        }

        if (totalAmount < 0)
        {
            return "Het bedrag kan niet negatief zijn.";
        }

        if (odometerKm is < 0)
        {
            return "De kilometerstand kan niet negatief zijn.";
        }

        return null;
    }

    private async Task<bool> ReferencesInTenantAsync(Guid? driverId, Guid? tankCardId, CancellationToken cancellationToken)
    {
        if (driverId is { } d && !await _dbContext.Drivers.AnyAsync(
                x => x.Id == d && x.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return false;
        }

        if (tankCardId is { } c && !await _dbContext.TankCards.AnyAsync(
                x => x.Id == c && x.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return false;
        }

        return true;
    }

    /// <summary>Consumption of a single row depends on the vehicle's trail, so recompute against full history.</summary>
    private async Task<FuelTransactionDto> MapWithHistoryAsync(FuelTransaction transaction, CancellationToken cancellationToken)
    {
        var history = await TenantScoped().AsNoTracking()
            .Where(f => f.VehicleId == transaction.VehicleId)
            .ToListAsync(cancellationToken);

        var (derived, _) = ComputeDerived(history);
        var names = await ResolveNamesAsync([transaction], cancellationToken);

        return Map(transaction, derived[transaction.Id], names);
    }

    private async Task<(Dictionary<Guid, string> Drivers, Dictionary<Guid, string> Cards)> ResolveNamesAsync(
        IReadOnlyCollection<FuelTransaction> transactions, CancellationToken cancellationToken)
    {
        var driverIds = transactions.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var cardIds = transactions.Where(t => t.TankCardId is not null).Select(t => t.TankCardId!.Value).Distinct().ToList();

        var drivers = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var cards = cardIds.Count == 0
            ? []
            : await _dbContext.TankCards.AsNoTracking()
                .Where(c => c.TenantId == _tenantContext.TenantId && cardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.CardNumber, cancellationToken);

        return (drivers, cards);
    }

    private static FuelTransactionDto Map(
        FuelTransaction t,
        FuelDerived derived,
        (Dictionary<Guid, string> Drivers, Dictionary<Guid, string> Cards) names) => new(
        t.Id, t.VehicleId,
        t.DriverId,
        t.DriverId is { } d && names.Drivers.TryGetValue(d, out var driverName) ? driverName : null,
        t.TankCardId,
        t.TankCardId is { } c && names.Cards.TryGetValue(c, out var cardNumber) ? cardNumber : null,
        t.TransactionDate, t.Litres, t.TotalAmount,
        t.Litres > 0 ? Math.Round(t.TotalAmount / t.Litres, 3) : null,
        t.OdometerKm, t.Station, t.FullTank,
        derived.ConsumptionLPer100Km,
        derived.Warnings,
        t.Notes);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
