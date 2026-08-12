using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Warehousing.Services;

public sealed record StorageOrderBreakdownDto(
    Guid TransportOrderId, string OrderNumber, int PackageCount, decimal PalletDays);

public sealed record StorageWarehouseBreakdownDto(Guid WarehouseId, string WarehouseName, decimal PalletDays);

public sealed record StorageBillingDto(
    Guid CustomerId, DateOnly From, DateOnly To,
    /// <summary>Total pallet-days in the period (per started day per handling unit).</summary>
    decimal TotalPalletDays,
    /// <summary>Handling units with an OPEN stay at the end of the period.</summary>
    int OpenStays,
    IReadOnlyList<StorageOrderBreakdownDto> PerOrder,
    IReadOnlyList<StorageWarehouseBreakdownDto> PerWarehouse);

public interface IStorageBillingService
{
    Task<StorageBillingDto> ComputeAsync(Guid customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 5 §2: pallet-day derivation from the movement-based storage clock. Per handling unit
/// the overlap of its stay(s) with the period counts in STARTED days (the same semantics the
/// manual PerPalletDay entries used) — an open stay counts until the period end. Read-only:
/// the numbers feed the operator (and later the Wave-10 proposal engine); explicit day counts
/// entered on an order always win.
/// </summary>
public class StorageBillingService : IStorageBillingService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public StorageBillingService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<StorageBillingDto> ComputeAsync(
        Guid customerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var periodStart = from.ToDateTime(TimeOnly.MinValue);
        var periodEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue); // exclusive

        var stays = await _dbContext.StorageStays.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.InAt < periodEnd && (s.OutAt == null || s.OutAt > periodStart))
            .Join(_dbContext.Packages.AsNoTracking(), s => s.PackageId, p => p.Id,
                (s, p) => new { Stay = s, p.TransportOrderId })
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.CustomerId == customerId),
                x => x.TransportOrderId, o => o.Id,
                (x, o) => new { x.Stay, x.TransportOrderId, o.OrderNumber })
            .ToListAsync(cancellationToken);

        decimal DaysOf(DateTime inAt, DateTime? outAt)
        {
            var start = inAt < periodStart ? periodStart : inAt;
            var end = outAt is { } o && o < periodEnd ? o : periodEnd;
            if (end <= start)
            {
                return 0m;
            }

            // Started days: 0.5 day counts as 1 — mirrors the manual pallet-day convention.
            return Math.Ceiling((decimal)(end - start).TotalDays);
        }

        var perStay = stays
            .Select(s => new
            {
                s.Stay, s.TransportOrderId, s.OrderNumber,
                Days = DaysOf(s.Stay.InAt, s.Stay.OutAt),
            })
            .Where(s => s.Days > 0)
            .ToList();

        var perOrder = perStay
            .GroupBy(s => new { s.TransportOrderId, s.OrderNumber })
            .Select(g => new StorageOrderBreakdownDto(
                g.Key.TransportOrderId, g.Key.OrderNumber,
                g.Select(s => s.Stay.PackageId).Distinct().Count(),
                g.Sum(s => s.Days)))
            .OrderBy(o => o.OrderNumber)
            .ToList();

        var warehouseIds = perStay.Select(s => s.Stay.WarehouseId).Distinct().ToList();
        var warehouseNames = warehouseIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Warehouses.AsNoTracking()
                .Where(w => w.TenantId == tenantId && warehouseIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var perWarehouse = perStay
            .GroupBy(s => s.Stay.WarehouseId)
            .Select(g => new StorageWarehouseBreakdownDto(
                g.Key, warehouseNames.GetValueOrDefault(g.Key, "?"), g.Sum(s => s.Days)))
            .OrderBy(w => w.WarehouseName)
            .ToList();

        return new StorageBillingDto(
            customerId, from, to,
            perStay.Sum(s => s.Days),
            stays.Count(s => s.Stay.OutAt is null || s.Stay.OutAt >= periodEnd),
            perOrder, perWarehouse);
    }
}
