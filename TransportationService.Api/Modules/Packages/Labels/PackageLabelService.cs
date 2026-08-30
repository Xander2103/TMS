using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Labels;

public sealed record PackageLabelVersionDto(
    Guid Id, int Version, LabelFormat Format, DateTime PrintedAt, string? ReprintReason);

public interface IPackageLabelService
{
    /// <summary>Prints labels for packages (creating a new snapshot version each) → one PDF.</summary>
    Task<(byte[]? Pdf, string? Error)> PrintAsync(
        IReadOnlyList<Guid> packageIds, LabelFormat format, string? reprintReason, CancellationToken cancellationToken);

    /// <summary>Regenerates a HISTORICAL label verbatim from its stored snapshot.</summary>
    Task<byte[]?> RenderVersionAsync(Guid packageId, int version, CancellationToken cancellationToken);

    Task<IReadOnlyList<PackageLabelVersionDto>> ListVersionsAsync(Guid packageId, CancellationToken cancellationToken);
}

public class PackageLabelService : IPackageLabelService
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly ILabelRenderService _renderService;
    private readonly IPackageEventWriter _eventWriter;
    private readonly TimeProvider _timeProvider;

    public PackageLabelService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        ILabelRenderService renderService,
        IPackageEventWriter eventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _renderService = renderService;
        _eventWriter = eventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<(byte[]? Pdf, string? Error)> PrintAsync(
        IReadOnlyList<Guid> packageIds, LabelFormat format, string? reprintReason, CancellationToken cancellationToken)
    {
        if (packageIds.Count is 0 or > 500)
        {
            return (null, "Kies 1 tot 500 colli.");
        }

        var tenantId = _tenantContext.TenantId;
        var packages = await _dbContext.Packages
            .Where(p => p.TenantId == tenantId && packageIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        if (packages.Count != packageIds.Distinct().Count())
        {
            return (null, "Eén of meer colli bestaan niet.");
        }

        if (packages.Any(p => p.CurrentLifecycleStatus == PackageLifecycleStatus.Cancelled))
        {
            return (null, "Geannuleerde colli krijgen geen etiket.");
        }

        var snapshots = await BuildSnapshotsAsync(packages, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existingVersions = await _dbContext.PackageLabels
            .Where(l => l.TenantId == tenantId && packageIds.Contains(l.PackageId))
            .GroupBy(l => l.PackageId)
            .Select(g => new { PackageId = g.Key, Max = g.Max(l => l.Version) })
            .ToDictionaryAsync(g => g.PackageId, g => g.Max, cancellationToken);

        var ordered = new List<LabelSnapshot>(packages.Count);
        foreach (var package in packages.OrderBy(p => p.PackageNumber))
        {
            var snapshot = snapshots[package.Id];
            ordered.Add(snapshot);
            var isReprint = existingVersions.ContainsKey(package.Id);
            _dbContext.Add(new PackageLabel
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PackageId = package.Id,
                Version = existingVersions.GetValueOrDefault(package.Id) + 1,
                Format = format,
                SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJson),
                PrintedAt = now,
                PrintedByUserId = _currentUserContext.CurrentUserId,
                ReprintReason = isReprint ? reprintReason ?? "Herafdruk" : null,
            });

            if (!isReprint
                && PackageLifecycleMachine.IsAllowed(package.CurrentLifecycleStatus, PackageLifecycleStatus.Labelled))
            {
                var old = package.CurrentLifecycleStatus;
                package.CurrentLifecycleStatus = PackageLifecycleStatus.Labelled;
                _eventWriter.Stage(package, PackageEventType.Labelled, old, PackageLifecycleStatus.Labelled,
                    new PackageEventContext(BarcodeUsed: package.BarcodeValue));
            }
            else if (isReprint)
            {
                _eventWriter.Stage(package, PackageEventType.LabelReprinted,
                    package.CurrentLifecycleStatus, package.CurrentLifecycleStatus,
                    new PackageEventContext(Notes: reprintReason ?? "Herafdruk"));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PackageLabel", string.Join(",", packageIds.Take(5)), "Printed", null,
            new { Count = packages.Count, Format = format.ToString(), ReprintReason = reprintReason }, cancellationToken);

        return (_renderService.Render(ordered, format), null);
    }

    public async Task<byte[]?> RenderVersionAsync(Guid packageId, int version, CancellationToken cancellationToken)
    {
        var label = await _dbContext.PackageLabels.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TenantId == _tenantContext.TenantId
                                      && l.PackageId == packageId && l.Version == version, cancellationToken);
        if (label is null)
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<LabelSnapshot>(label.SnapshotJson, SnapshotJson);
        return snapshot is null ? null : _renderService.Render([snapshot], label.Format);
    }

    public async Task<IReadOnlyList<PackageLabelVersionDto>> ListVersionsAsync(
        Guid packageId, CancellationToken cancellationToken)
    {
        return await _dbContext.PackageLabels.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.PackageId == packageId)
            .OrderByDescending(l => l.Version)
            .Select(l => new PackageLabelVersionDto(l.Id, l.Version, l.Format, l.PrintedAt, l.ReprintReason))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, LabelSnapshot>> BuildSnapshotsAsync(
        IReadOnlyList<Package> packages, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tenantName = await _dbContext.TenantSettings.AsNoTracking()
                             .Where(s => s.TenantId == tenantId)
                             .Select(s => s.TradingName ?? s.CompanyLegalName)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? await _dbContext.Tenants.AsNoTracking()
                             .Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync(cancellationToken);

        var orderIds = packages.Select(p => p.TransportOrderId).Distinct().ToList();
        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId),
                o => o.CustomerId, c => c.Id,
                (o, c) => new { o.Id, o.OrderNumber, CustomerName = c.Name, o.AdrRequired, o.CustomerReference })
            .ToDictionaryAsync(o => o.Id, cancellationToken);
        var stops = await _dbContext.TransportOrderStops.AsNoTracking()
            .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
            .ToListAsync(cancellationToken);

        // Resolve linked master locations for full street/city/country on the label.
        var locationIds = stops.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        var locations = locationIds.Count == 0
            ? new Dictionary<Guid, Locations.Entities.Location>()
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == tenantId && locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);

        // Sequence "Collo n van m": within the cargo line for generated packages, within the
        // order for manually created ones. Ordered by package number (stable, immutable).
        var siblings = await _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == tenantId && orderIds.Contains(p.TransportOrderId)
                && p.CurrentLifecycleStatus != PackageLifecycleStatus.Cancelled)
            .Select(p => new { p.Id, p.TransportOrderId, p.CargoItemId, p.PackageNumber })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, LabelSnapshot>(packages.Count);
        foreach (var package in packages)
        {
            orders.TryGetValue(package.TransportOrderId, out var order);
            var orderStops = stops.Where(s => s.TransportOrderId == package.TransportOrderId).ToList();
            // Wave 1 fix A (A5): a pin that no longer resolves — a DANGLING pin, which every order
            // edited before blocker C-01 landed can carry — gets the same fallback as a null pin
            // (first loading / last unloading stop). Without it the label printed a blank sender
            // and recipient instead of the order's own addresses.
            var loading = (package.LoadingStopId is { } loadId ? orderStops.FirstOrDefault(s => s.Id == loadId) : null)
                ?? orderStops.Where(s => s.StopType == StopType.Loading).OrderBy(s => s.Sequence).FirstOrDefault();
            var delivery = (package.DeliveryStopId is { } deliveryId ? orderStops.FirstOrDefault(s => s.Id == deliveryId) : null)
                ?? orderStops.Where(s => s.StopType == StopType.Unloading).OrderBy(s => s.Sequence).LastOrDefault();

            var group = siblings
                .Where(s => s.TransportOrderId == package.TransportOrderId
                    && (package.CargoItemId is null || s.CargoItemId == package.CargoItemId))
                .OrderBy(s => s.PackageNumber, StringComparer.Ordinal)
                .ToList();
            var position = group.FindIndex(s => s.Id == package.Id) + 1;

            var loadingAddress = ResolveAddress(loading, locations);
            var deliveryAddress = ResolveAddress(delivery, locations);

            result[package.Id] = new LabelSnapshot(
                tenantName,
                package.PackageNumber,
                package.BarcodeValue,
                IncludeQr: true,
                order?.OrderNumber ?? "?",
                order?.CustomerName ?? "?",
                LocationLabel(loading),
                LocationLabel(delivery),
                delivery?.Sequence,
                package.CustomerReference ?? package.ExternalPackageReference,
                package.WeightKg,
                UnitTypeDutch(package.UnitType, package.UnitTypeLabel),
                delivery?.UnloadingInstructions ?? delivery?.Instructions,
                package.IsFragile,
                order?.AdrRequired ?? false,
                package.RequiresTemperatureControl,
                package.RequiresSignature,
                position > 0 ? $"Collo {position} van {group.Count}" : null,
                // Redesigned horizontal-label fields.
                SenderName: loadingAddress.Name,
                SenderStreet: loadingAddress.Street,
                SenderPostalCodeCity: loadingAddress.PostalCity,
                SenderCountry: loadingAddress.Country,
                PickupDate: loading?.PlannedFrom?.ToString("dd-MM-yyyy"),
                PickupTime: loading?.PlannedFrom?.ToString("HH:mm"),
                RecipientName: deliveryAddress.Name,
                RecipientStreet: deliveryAddress.Street,
                RecipientPostalCodeCity: deliveryAddress.PostalCity,
                RecipientCountry: deliveryAddress.Country,
                DeliveryDate: delivery?.PlannedFrom?.ToString("dd-MM-yyyy"),
                DeliveryTime: delivery?.PlannedFrom?.ToString("HH:mm"),
                SequenceNumber: position > 0 ? position : null,
                SequenceTotal: position > 0 ? group.Count : null,
                VolumeM3: package.VolumeM3,
                PurchaseOrderNumber: order?.CustomerReference);
        }

        return result;
    }

    private static string? LocationLabel(TransportOrderStop? stop) =>
        stop is null ? null : string.Join(", ", new[] { stop.LocationName, stop.City }.Where(v => !string.IsNullOrWhiteSpace(v)));

    /// <summary>Full address for a stop: prefers the inline stop fields, falls back to the linked location.</summary>
    private static (string? Name, string? Street, string? PostalCity, string? Country) ResolveAddress(
        TransportOrderStop? stop, IReadOnlyDictionary<Guid, Locations.Entities.Location> locations)
    {
        if (stop is null)
        {
            return (null, null, null, null);
        }

        var location = stop.LocationId is { } id && locations.TryGetValue(id, out var loc) ? loc : null;
        var name = FirstNonEmpty(stop.LocationName, location?.Name);
        var street = FirstNonEmpty(
            stop.Address,
            location is not null ? string.Join(" ", new[] { location.Street, location.HouseNumber }.Where(v => !string.IsNullOrWhiteSpace(v))) : null);
        var postalCity = FirstNonEmpty(
            string.Join(" ", new[] { stop.PostalCode, stop.City }.Where(v => !string.IsNullOrWhiteSpace(v))),
            location is not null ? string.Join(" ", new[] { location.PostalCode, location.City }.Where(v => !string.IsNullOrWhiteSpace(v))) : null);
        var country = FirstNonEmpty(stop.CountryCode, location?.CountryCode);
        return (name, street, string.IsNullOrWhiteSpace(postalCity) ? null : postalCity, country);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string UnitTypeDutch(PackageUnitType type, string? customLabel) => type switch
    {
        PackageUnitType.Other => customLabel ?? "Anders",
        PackageUnitType.Package => "Pakket",
        PackageUnitType.Parcel => "Pakje",
        PackageUnitType.Colli => "Colli",
        PackageUnitType.Pallet => "Pallet",
        PackageUnitType.EuroPallet => "Europallet",
        PackageUnitType.BlockPallet => "Blokpallet",
        PackageUnitType.Box => "Doos",
        PackageUnitType.Crate => "Krat",
        PackageUnitType.RollContainer => "Rolcontainer",
        PackageUnitType.Drum => "Vat",
        PackageUnitType.Container => "Container",
        PackageUnitType.Document => "Document",
        _ => type.ToString(),
    };
}
