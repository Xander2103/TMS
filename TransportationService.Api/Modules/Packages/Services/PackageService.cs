using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

public interface IPackageService
{
    Task<IReadOnlyList<PackageDto>> ListForOrderAsync(Guid transportOrderId, CancellationToken cancellationToken);
    Task<PackageDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PackageBarcodeDto>> ListBarcodesAsync(Guid id, CancellationToken cancellationToken);
    Task<PackageOperationResult> CreateAsync(Guid transportOrderId, CreatePackageRequest request, CancellationToken cancellationToken);
    Task<(BulkCreateResultDto? Result, string? Error)> BulkCreateAsync(
        Guid transportOrderId, BulkCreatePackagesRequest request, CancellationToken cancellationToken);
    Task<PackageOperationResult> UpdateAsync(Guid id, UpdatePackageRequest request, CancellationToken cancellationToken);
    Task<PackageOperationResult> CancelAsync(Guid id, CancelPackageRequest request, CancellationToken cancellationToken);
    Task<PackageOperationResult> RelabelAsync(Guid id, RelabelPackageRequest request, CancellationToken cancellationToken);
}

public class PackageService : IPackageService
{
    private const string EntityType = "Package";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IPackageBarcodeService _barcodeService;
    private readonly IPackageEventWriter _eventWriter;

    public PackageService(
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

    private IQueryable<Package> Scoped() => _dbContext.Packages.Where(p => p.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<PackageDto>> ListForOrderAsync(
        Guid transportOrderId, CancellationToken cancellationToken)
    {
        var packages = await Scoped().AsNoTracking()
            .Where(p => p.TransportOrderId == transportOrderId)
            .OrderBy(p => p.PackageNumber)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return packages.Select(Map).ToList();
    }

    public async Task<PackageDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var package = await Scoped().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return package is null ? null : Map(package);
    }

    public async Task<IReadOnlyList<PackageBarcodeDto>> ListBarcodesAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.PackageBarcodes.AsNoTracking()
            .Where(b => b.TenantId == _tenantContext.TenantId && b.PackageId == id)
            .OrderByDescending(b => b.IsActive).ThenByDescending(b => b.CreatedAt)
            .Select(b => new PackageBarcodeDto(b.Id, b.Value, b.Type, b.IsActive, b.CreatedAt, b.RetiredAt, b.RetireReason))
            .ToListAsync(cancellationToken);
    }

    public async Task<PackageOperationResult> CreateAsync(
        Guid transportOrderId, CreatePackageRequest request, CancellationToken cancellationToken)
    {
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == transportOrderId && o.TenantId == _tenantContext.TenantId, cancellationToken);
        if (order is null)
        {
            return PackageOperationResult.NotFound;
        }

        if (order.Status is TransportOrderStatus.Cancelled)
        {
            return PackageOperationResult.InvalidState("Op een geannuleerde opdracht kunnen geen colli worden aangemaakt.");
        }

        if (await ValidateRequestAsync(transportOrderId, request.Description, request.Quantity,
                request.LoadingStopId, request.DeliveryStopId, request.ParentPackageId,
                request.ExternalBarcode, cancellationToken) is { } error)
        {
            return error;
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var package = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TransportOrderId = transportOrderId,
            Description = request.Description.Trim(),
            Quantity = request.Quantity,
            UnitType = request.UnitType,
            UnitTypeLabel = request.UnitType == PackageUnitType.Other ? Trim(request.UnitTypeLabel) : null,
            ExternalBarcode = Trim(request.ExternalBarcode),
            ExternalPackageReference = Trim(request.ExternalPackageReference),
            CustomerReference = Trim(request.CustomerReference),
            WeightKg = request.WeightKg,
            VolumeM3 = request.VolumeM3,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            LoadingStopId = request.LoadingStopId,
            DeliveryStopId = request.DeliveryStopId,
            ParentPackageId = request.ParentPackageId,
            IsMandatory = request.IsMandatory,
            IsFragile = request.IsFragile,
            RequiresTemperatureControl = request.RequiresTemperatureControl,
            RequiresSignature = request.RequiresSignature,
            Notes = Trim(request.Notes),
        };
        _dbContext.Add(package);

        // Number + internal barcode are claimed inside the concurrency-safe numbering save;
        // the barcode embeds the number, so both are (re)assigned together on retry.
        PackageBarcode? internalRow = null;
        try
        {
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () =>
                {
                    package.PackageNumber = GeneratePackageNumber(settings);
                    package.BarcodeValue = _barcodeService.GenerateInternalValue(package.PackageNumber);
                    if (internalRow is null)
                    {
                        _barcodeService.StageInitialBarcodes(package, request.ExternalBarcode);
                        internalRow = _dbContext.ChangeTracker.Entries<PackageBarcode>()
                            .Select(e => e.Entity)
                            .First(b => b.PackageId == package.Id && b.Type != PackageBarcodeType.External);
                    }
                    else
                    {
                        internalRow.Value = package.BarcodeValue;
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            // Unique (TenantId, Value) on the registry: the external barcode already exists.
            return PackageOperationResult.Duplicate("Deze (externe) barcode is al in gebruik binnen dit bedrijf.");
        }

        _eventWriter.Stage(package, PackageEventType.Created, null, PackageLifecycleStatus.Created,
            new PackageEventContext(OrderId: transportOrderId, BarcodeUsed: package.BarcodeValue));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, package.Id.ToString(), "Created", null,
            new { package.PackageNumber, package.TransportOrderId, package.Description, package.UnitType }, cancellationToken);

        return PackageOperationResult.Success(Map(package));
    }

    public async Task<(BulkCreateResultDto? Result, string? Error)> BulkCreateAsync(
        Guid transportOrderId, BulkCreatePackagesRequest request, CancellationToken cancellationToken)
    {
        if (request.Count is < 1 or > 500)
        {
            return (null, "Het aantal colli moet tussen 1 en 500 liggen.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return (null, "Een omschrijving is verplicht.");
        }

        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == transportOrderId && o.TenantId == _tenantContext.TenantId, cancellationToken);
        if (order is null)
        {
            return (null, "De opdracht bestaat niet.");
        }

        if (order.Status is TransportOrderStatus.Cancelled)
        {
            return (null, "Op een geannuleerde opdracht kunnen geen colli worden aangemaakt.");
        }

        if (await ValidateRequestAsync(transportOrderId, request.Description, 1m,
                request.LoadingStopId, request.DeliveryStopId, null, null, cancellationToken) is { } error)
        {
            return (null, error.Error);
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        Package? pallet = null;
        if (request.GroupOnPallet)
        {
            pallet = NewPackage(transportOrderId, request, PackageUnitType.Pallet,
                Trim(request.PalletDescription) ?? $"Pallet — {request.Description.Trim()}", reference: null);
            _dbContext.Add(pallet);
        }

        var packages = new List<Package>(request.Count);
        for (var index = 1; index <= request.Count; index += 1)
        {
            var package = NewPackage(transportOrderId, request, request.UnitType, request.Description.Trim(),
                request.ReferencePrefix is { Length: > 0 } prefix ? $"{prefix.Trim()}-{index}" : null);
            package.ParentPackageId = pallet?.Id;
            packages.Add(package);
            _dbContext.Add(package);
        }

        // One numbering save claims a contiguous block; on a concurrency retry the callback
        // reassigns every number+barcode consistently from the reloaded counter.
        List<Package> all = pallet is null ? packages : [pallet, .. packages];
        var barcodeRows = new Dictionary<Guid, PackageBarcode>();
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () =>
            {
                foreach (var package in all)
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

        foreach (var package in all)
        {
            _eventWriter.Stage(package, PackageEventType.Created, null, PackageLifecycleStatus.Created,
                new PackageEventContext(OrderId: transportOrderId, BarcodeUsed: package.BarcodeValue,
                    Notes: pallet is not null && package != pallet ? $"Op pallet {pallet.PackageNumber}" : null));
            if (pallet is not null && package != pallet)
            {
                _eventWriter.Stage(package, PackageEventType.GroupAssigned,
                    PackageLifecycleStatus.Created, PackageLifecycleStatus.Created,
                    new PackageEventContext(Notes: $"Pallet {pallet.PackageNumber}"));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, transportOrderId.ToString(), "BulkCreated", null,
            new { Count = request.Count, request.GroupOnPallet, First = all[0].PackageNumber, Last = all[^1].PackageNumber },
            cancellationToken);

        return (new BulkCreateResultDto(packages.Select(Map).ToList(), pallet is null ? null : Map(pallet)), null);
    }

    private Package NewPackage(
        Guid transportOrderId, BulkCreatePackagesRequest request, PackageUnitType unitType,
        string description, string? reference) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantContext.TenantId,
        TransportOrderId = transportOrderId,
        Description = description,
        Quantity = 1m,
        UnitType = unitType,
        WeightKg = request.WeightKg,
        VolumeM3 = request.VolumeM3,
        LoadingStopId = request.LoadingStopId,
        DeliveryStopId = request.DeliveryStopId,
        CustomerReference = reference,
        IsMandatory = request.IsMandatory,
        IsFragile = request.IsFragile,
        RequiresTemperatureControl = request.RequiresTemperatureControl,
        RequiresSignature = request.RequiresSignature,
    };

    public async Task<PackageOperationResult> UpdateAsync(
        Guid id, UpdatePackageRequest request, CancellationToken cancellationToken)
    {
        var package = await Scoped().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package is null)
        {
            return PackageOperationResult.NotFound;
        }

        if (PackageLifecycleMachine.IsTerminal(package.CurrentLifecycleStatus))
        {
            return PackageOperationResult.InvalidState("Een afgesloten collo kan niet meer worden bewerkt.");
        }

        if (await ValidateRequestAsync(package.TransportOrderId, request.Description, request.Quantity,
                request.LoadingStopId, request.DeliveryStopId, parentPackageId: null,
                externalBarcode: null, cancellationToken) is { } error)
        {
            return error;
        }

        var before = new { package.Description, package.Quantity, package.UnitType, package.DeliveryStopId };
        package.Description = request.Description.Trim();
        package.Quantity = request.Quantity;
        package.UnitType = request.UnitType;
        package.UnitTypeLabel = request.UnitType == PackageUnitType.Other ? Trim(request.UnitTypeLabel) : null;
        package.ExternalPackageReference = Trim(request.ExternalPackageReference);
        package.CustomerReference = Trim(request.CustomerReference);
        package.WeightKg = request.WeightKg;
        package.VolumeM3 = request.VolumeM3;
        package.LengthCm = request.LengthCm;
        package.WidthCm = request.WidthCm;
        package.HeightCm = request.HeightCm;
        package.LoadingStopId = request.LoadingStopId;
        package.DeliveryStopId = request.DeliveryStopId;
        package.IsMandatory = request.IsMandatory;
        package.IsFragile = request.IsFragile;
        package.RequiresTemperatureControl = request.RequiresTemperatureControl;
        package.RequiresSignature = request.RequiresSignature;
        package.Notes = Trim(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, package.Id.ToString(), "Updated", before,
            new { package.Description, package.Quantity, package.UnitType, package.DeliveryStopId }, cancellationToken);

        return PackageOperationResult.Success(Map(package));
    }

    public async Task<PackageOperationResult> CancelAsync(
        Guid id, CancelPackageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return PackageOperationResult.Invalid("Een reden is verplicht bij annulering.");
        }

        var package = await Scoped().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package is null)
        {
            return PackageOperationResult.NotFound;
        }

        if (!PackageLifecycleMachine.IsAllowed(package.CurrentLifecycleStatus, PackageLifecycleStatus.Cancelled))
        {
            return PackageOperationResult.InvalidState(
                $"Een collo met status '{package.CurrentLifecycleStatus}' kan niet worden geannuleerd.");
        }

        var old = package.CurrentLifecycleStatus;
        package.CurrentLifecycleStatus = PackageLifecycleStatus.Cancelled;
        _eventWriter.Stage(package, PackageEventType.Cancelled, old, PackageLifecycleStatus.Cancelled,
            new PackageEventContext(Notes: request.Reason.Trim()));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, package.Id.ToString(), "Cancelled",
            new { Status = old }, new { package.CurrentLifecycleStatus, request.Reason }, cancellationToken);

        return PackageOperationResult.Success(Map(package));
    }

    public async Task<PackageOperationResult> RelabelAsync(
        Guid id, RelabelPackageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return PackageOperationResult.Invalid("Een reden is verplicht bij heretikettering.");
        }

        var package = await Scoped().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (package is null)
        {
            return PackageOperationResult.NotFound;
        }

        if (PackageLifecycleMachine.IsTerminal(package.CurrentLifecycleStatus))
        {
            return PackageOperationResult.InvalidState("Een afgesloten collo kan geen nieuw etiket krijgen.");
        }

        var (oldValue, newValue) = await _barcodeService.RelabelAsync(package, request.Reason.Trim(), cancellationToken);
        _eventWriter.Stage(package, PackageEventType.Relabelled,
            package.CurrentLifecycleStatus, package.CurrentLifecycleStatus,
            new PackageEventContext(BarcodeUsed: newValue, Notes: $"Vervangt {oldValue}: {request.Reason.Trim()}"));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, package.Id.ToString(), "Relabelled",
            new { OldBarcode = oldValue }, new { NewBarcode = newValue, request.Reason }, cancellationToken);

        return PackageOperationResult.Success(Map(package));
    }

    private async Task<PackageOperationResult?> ValidateRequestAsync(
        Guid transportOrderId, string description, decimal quantity,
        Guid? loadingStopId, Guid? deliveryStopId, Guid? parentPackageId,
        string? externalBarcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return PackageOperationResult.Invalid("Een omschrijving is verplicht.");
        }

        if (quantity <= 0)
        {
            return PackageOperationResult.Invalid("Het aantal moet groter dan nul zijn.");
        }

        if (!string.IsNullOrWhiteSpace(externalBarcode)
            && _barcodeService.ValidateExternalValue(externalBarcode) is { } barcodeError)
        {
            return PackageOperationResult.Invalid(barcodeError);
        }

        foreach (var (stopId, expectedType, label) in new[]
                 {
                     (loadingStopId, StopType.Loading, "laadstop"),
                     (deliveryStopId, StopType.Unloading, "losstop"),
                 })
        {
            if (stopId is not { } sid)
            {
                continue;
            }

            var stop = await _dbContext.TransportOrderStops.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sid && s.TenantId == _tenantContext.TenantId, cancellationToken);
            if (stop is null || stop.TransportOrderId != transportOrderId || stop.StopType != expectedType)
            {
                return PackageOperationResult.Invalid($"De gekozen {label} hoort niet bij deze opdracht.");
            }
        }

        if (parentPackageId is { } parentId)
        {
            var parent = await Scoped().AsNoTracking().FirstOrDefaultAsync(p => p.Id == parentId, cancellationToken);
            if (parent is null || parent.TransportOrderId != transportOrderId)
            {
                return PackageOperationResult.Invalid("De gekozen groep/pallet hoort niet bij deze opdracht.");
            }

            if (parent.ParentPackageId is not null)
            {
                return PackageOperationResult.Invalid("Groepen kunnen niet genest worden.");
            }
        }

        return null;
    }

    private static string GeneratePackageNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"PKG-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        }

        var number = $"{settings.PackageNumberPrefix}{settings.PackageNumberNextValue:00000}";
        settings.PackageNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static PackageDto Map(Package p) => new(
        p.Id, p.TransportOrderId, p.PackageNumber, p.BarcodeValue, p.BarcodeType,
        p.ExternalBarcode, p.ExternalPackageReference, p.CustomerReference,
        p.Description, p.Quantity, p.UnitType, p.UnitTypeLabel,
        p.WeightKg, p.VolumeM3, p.LengthCm, p.WidthCm, p.HeightCm,
        p.ParentPackageId, p.LoadingStopId, p.DeliveryStopId,
        p.IsMandatory, p.IsFragile, p.RequiresTemperatureControl, p.RequiresSignature,
        p.CurrentLifecycleStatus, p.CurrentExceptionStatus, p.Notes, p.CreatedAt);
}
