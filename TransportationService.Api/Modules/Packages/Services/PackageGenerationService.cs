using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public interface IPackageGenerationService
{
    /// <summary>
    /// Generates the packages an order's cargo lines call for. Business rule: one package
    /// per whole unit (ceiling of the expected quantity) per cargo line. Idempotent: lines
    /// that already carry enough non-cancelled packages are untouched, retries create no
    /// duplicates. When a quantity DROPPED after generation, surplus packages without scan
    /// history are cancelled (with reason); scanned surplus is only reported — resolving it
    /// runs through the existing incident/disposition workflow, never a silent delete.
    /// Returns null when the order does not exist (tenant-scoped).
    /// </summary>
    Task<PackageGenerationResultDto?> GenerateForOrderAsync(Guid transportOrderId, CancellationToken cancellationToken);
}

public class PackageGenerationService : IPackageGenerationService
{
    private const string EntityType = "Package";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IPackageBarcodeService _barcodeService;
    private readonly IPackageEventWriter _eventWriter;

    public PackageGenerationService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IPackageBarcodeService barcodeService,
        IPackageEventWriter eventWriter)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _barcodeService = barcodeService;
        _eventWriter = eventWriter;
    }

    public async Task<PackageGenerationResultDto?> GenerateForOrderAsync(
        Guid transportOrderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == transportOrderId && o.TenantId == tenantId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.Status is TransportOrderStatus.Cancelled)
        {
            return new PackageGenerationResultDto(0, 0, 0, 0, [], "Voor een geannuleerde opdracht worden geen colli gegenereerd.");
        }

        var cargoItems = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TransportOrderId == transportOrderId)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);
        if (cargoItems.Count == 0)
        {
            return new PackageGenerationResultDto(0, 0, 0, 0, [], "Deze opdracht heeft geen goederenlijnen om colli uit te genereren.");
        }

        var linkedPackages = await _dbContext.Packages
            .Where(p => p.TenantId == tenantId && p.TransportOrderId == transportOrderId && p.CargoItemId != null)
            .ToListAsync(cancellationToken);
        var linkedByCargo = linkedPackages.ToLookup(p => p.CargoItemId!.Value);

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var newPackages = new List<Package>();
        var cancelled = 0;
        var unchanged = 0;
        var requiresAttention = 0;

        foreach (var line in cargoItems)
        {
            var target = (int)Math.Ceiling(line.ExpectedQuantity);
            var existing = linkedByCargo[line.Id]
                .Where(p => p.CurrentLifecycleStatus != PackageLifecycleStatus.Cancelled)
                .ToList();

            if (existing.Count == target)
            {
                unchanged += existing.Count;
                continue;
            }

            if (existing.Count < target)
            {
                unchanged += existing.Count;
                var perUnitWeight = line.WeightPerUnitKg
                    ?? (line.TotalWeightKg is { } total && target > 0 ? Math.Round(total / target, 2) : (decimal?)null);
                for (var i = existing.Count; i < target; i += 1)
                {
                    newPackages.Add(new Package
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        TransportOrderId = transportOrderId,
                        CargoItemId = line.Id,
                        Description = LineDescription(line),
                        Quantity = 1m,
                        UnitType = line.UnitType ?? PackageUnitType.Colli,
                        UnitTypeLabel = line.UnitTypeLabel,
                        WeightKg = perUnitWeight,
                        VolumeM3 = line.VolumeM3,
                        LengthCm = line.LengthMeters is { } l ? Math.Round(l * 100, 1) : null,
                        WidthCm = line.WidthMeters is { } w ? Math.Round(w * 100, 1) : null,
                        HeightCm = line.HeightMeters is { } h ? Math.Round(h * 100, 1) : null,
                        LoadingStopId = line.LoadingStopId,
                        DeliveryStopId = line.UnloadingStopId,
                        CustomerReference = line.Reference,
                        Notes = $"Gegenereerd uit goederenlijn {line.Sequence}",
                    });
                }

                continue;
            }

            // Quantity dropped below the already generated count: cancel untouched surplus
            // (newest first), keep and report anything that already has scan history.
            var surplus = existing.Count - target;
            var cancellable = existing
                .Where(p => p.CurrentLifecycleStatus is PackageLifecycleStatus.Created or PackageLifecycleStatus.Labelled)
                .OrderByDescending(p => p.PackageNumber)
                .Take(surplus)
                .ToList();
            foreach (var package in cancellable)
            {
                var old = package.CurrentLifecycleStatus;
                package.CurrentLifecycleStatus = PackageLifecycleStatus.Cancelled;
                _eventWriter.Stage(package, PackageEventType.Cancelled, old, PackageLifecycleStatus.Cancelled,
                    new PackageEventContext(OrderId: transportOrderId,
                        Notes: $"Overtollig na aanpassing goederenlijn {line.Sequence} (nog niet gescand)"));
                cancelled += 1;
            }

            requiresAttention += surplus - cancellable.Count;
            unchanged += target;
        }

        if (newPackages.Count > 0)
        {
            foreach (var package in newPackages)
            {
                _dbContext.Add(package);
            }

            // One numbering save claims a contiguous block; on a concurrency retry the callback
            // reassigns every number+barcode consistently from the reloaded counter.
            var barcodeRows = new Dictionary<Guid, PackageBarcode>();
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () =>
                {
                    foreach (var package in newPackages)
                    {
                        package.PackageNumber = GeneratePackageNumber(settings);
                        package.BarcodeValue = _barcodeService.GenerateInternalValue(package.PackageNumber);
                        if (barcodeRows.TryGetValue(package.Id, out var row))
                        {
                            row.Value = package.BarcodeValue;
                        }
                        else
                        {
                            _barcodeService.StageInitialBarcodes(package, externalBarcode: null);
                            barcodeRows[package.Id] = _dbContext.ChangeTracker.Entries<PackageBarcode>()
                                .Select(e => e.Entity)
                                .First(b => b.PackageId == package.Id);
                        }
                    }
                },
                cancellationToken);

            foreach (var package in newPackages)
            {
                _eventWriter.Stage(package, PackageEventType.Created, null, PackageLifecycleStatus.Created,
                    new PackageEventContext(OrderId: transportOrderId, BarcodeUsed: package.BarcodeValue,
                        Notes: package.Notes));
            }
        }

        if (newPackages.Count > 0 || cancelled > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, transportOrderId.ToString(), "Generated", null,
                new { Created = newPackages.Count, Cancelled = cancelled, Unchanged = unchanged, RequiresAttention = requiresAttention },
                cancellationToken);
        }

        return new PackageGenerationResultDto(
            newPackages.Count, cancelled, unchanged, requiresAttention,
            newPackages.Select(PackageService.Map).ToList(),
            requiresAttention > 0
                ? "Eén of meer overtollige colli hebben al scanhistoriek; los ze op via de afwijkingenflow."
                : null);
    }

    /// <summary>Falls back to a generated label ("2 × EUROPALLET") when the line has no description.</summary>
    private static string LineDescription(CargoItem line) =>
        string.IsNullOrWhiteSpace(line.Description)
            ? $"{line.ExpectedQuantity:0.##} × {line.QuantityUnitCode ?? line.UnitTypeLabel ?? "stuks"}"
            : line.Description;

    private static string GeneratePackageNumber(Modules.Tenancy.Entities.TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"PKG-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        }

        var number = $"{settings.PackageNumberPrefix}{settings.PackageNumberNextValue:00000}";
        settings.PackageNumberNextValue += 1;
        return number;
    }
}
