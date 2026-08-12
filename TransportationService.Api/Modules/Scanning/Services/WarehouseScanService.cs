using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Scanning.Services;

public sealed record WarehouseScanRequest(
    string Barcode,
    ScanType ScanType,
    /// <summary>Target location; REQUIRED for Moved, optional (location stamp) otherwise.</summary>
    Guid? WarehouseLocationId = null,
    /// <summary>Client idempotency key: replaying the same key returns the original outcome.</summary>
    Guid? ClientEventId = null,
    string? DeviceInfo = null);

public sealed record WarehouseScanFeedbackDto(
    string Outcome,
    ScanFeedbackLevel Level,
    string Message,
    Guid? PackageId,
    string? PackageNumber,
    string? OrderNumber,
    string? LifecycleStatus,
    Guid? WarehouseLocationId,
    string? LocationCode);

public interface IWarehouseScanService
{
    Task<WarehouseScanFeedbackDto> SubmitAsync(WarehouseScanRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 4 §3: the trip-less warehouse entry point of the ONE scan pipeline. Reuses the same
/// barcode registry, the same append-only custody events and the same ScanEvent ledger as the
/// trip-scoped flow — never a fork. Received registers arrival (Created/Labelled →
/// AwaitingLoading), Moved/Staged are custody+location markers without lifecycle changes, and
/// Return checks a failed/refused/pending package in as ReturnedToDepot without a return trip.
/// Unknown barcodes and unexpected statuses are recorded as warning rows, never dropped.
/// </summary>
public class WarehouseScanService : IWarehouseScanService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPackageBarcodeService _barcodes;
    private readonly IPackageEventWriter _eventWriter;
    private readonly TimeProvider _timeProvider;

    public WarehouseScanService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUser,
        IPackageBarcodeService barcodes,
        IPackageEventWriter eventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _barcodes = barcodes;
        _eventWriter = eventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<WarehouseScanFeedbackDto> SubmitAsync(
        WarehouseScanRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (request.ScanType is not (ScanType.Received or ScanType.Moved or ScanType.Staged or ScanType.Return))
        {
            return new WarehouseScanFeedbackDto(
                "InvalidScanType", ScanFeedbackLevel.Error,
                "Alleen Ontvangst-, Verplaats-, Klaarzet- en Retourscans kunnen zonder rit.",
                null, null, null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return new WarehouseScanFeedbackDto(
                "EmptyBarcode", ScanFeedbackLevel.Error, "Geen barcode ontvangen.",
                null, null, null, null, null, null);
        }

        // Idempotent replay — same contract as the trip-scoped pipeline.
        if (request.ClientEventId is { } clientEventId)
        {
            var existing = await _dbContext.ScanEvents.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.ClientEventId == clientEventId, cancellationToken);
            if (existing is not null)
            {
                return await BuildReplayFeedbackAsync(existing, cancellationToken);
            }
        }

        // Location: required for a move, validated whenever supplied.
        Modules.Warehousing.Entities.WarehouseLocation? location = null;
        if (request.WarehouseLocationId is { } locationId)
        {
            location = await _dbContext.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == locationId && l.IsActive, cancellationToken);
            if (location is null)
            {
                return new WarehouseScanFeedbackDto(
                    "InvalidLocation", ScanFeedbackLevel.Error,
                    "De gekozen magazijnlocatie bestaat niet of is inactief.",
                    null, null, null, null, null, null);
            }
        }

        if (request.ScanType == ScanType.Moved && location is null)
        {
            return new WarehouseScanFeedbackDto(
                "LocationRequired", ScanFeedbackLevel.Error,
                "Kies de doellocatie voor een verplaatsing.",
                null, null, null, null, null, null);
        }

        var resolution = await _barcodes.ResolveAsync(request.Barcode.Trim(), cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Unknown barcode: warning ROW (never silently dropped), no package/custody mutation.
        if (resolution is null)
        {
            _dbContext.ScanEvents.Add(new ScanEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                TripId = null, TransportOrderId = null, TransportOrderStopId = null,
                WarehouseLocationId = location?.Id,
                ClientEventId = request.ClientEventId, UserId = _currentUser.CurrentUserId,
                OccurredAt = now, ScanType = request.ScanType, Result = ScanResult.UnexpectedItem,
                Barcode = request.Barcode.Trim(), Quantity = 1m, DeviceInfo = request.DeviceInfo,
                PackageOutcome = "UnknownBarcode",
            });
            await SaveWithReplayGuardAsync(request.ClientEventId, cancellationToken);
            return new WarehouseScanFeedbackDto(
                "UnknownBarcode", ScanFeedbackLevel.Warning,
                "Onbekende barcode — geregistreerd als afwijking.",
                null, null, null, null, location?.Id, location?.Code);
        }

        var package = await _dbContext.Packages
            .FirstAsync(p => p.TenantId == tenantId && p.Id == resolution.Package.Id, cancellationToken);
        var status = package.CurrentLifecycleStatus;

        var (outcome, level, message, newStatus, eventType) = request.ScanType switch
        {
            ScanType.Received => status switch
            {
                PackageLifecycleStatus.Created or PackageLifecycleStatus.Labelled =>
                    ("Received", ScanFeedbackLevel.Success, "Ontvangst geregistreerd.",
                        (PackageLifecycleStatus?)PackageLifecycleStatus.AwaitingLoading, PackageEventType.Received),
                PackageLifecycleStatus.AwaitingLoading =>
                    ("Received", ScanFeedbackLevel.Success, "Al aanwezig — locatie bijgewerkt.",
                        null, PackageEventType.Received),
                _ => ("UnexpectedStatus", ScanFeedbackLevel.Warning,
                        $"Onverwachte status ({status}) voor een ontvangstscan — geregistreerd.",
                        null, PackageEventType.Received),
            },
            ScanType.Moved =>
                ("Moved", ScanFeedbackLevel.Success, $"Verplaatst naar {location!.Code}.",
                    null, PackageEventType.MovedLocation),
            ScanType.Staged =>
                ("Staged", ScanFeedbackLevel.Success, "Klaargezet voor vertrek.",
                    null, PackageEventType.Staged),
            _ => status switch // ScanType.Return
            {
                PackageLifecycleStatus.ReturnPending or PackageLifecycleStatus.ReturnLoaded
                    or PackageLifecycleStatus.DeliveryFailed or PackageLifecycleStatus.Refused =>
                    ("ReturnedToDepot", ScanFeedbackLevel.Success, "Retour ingeboekt in het depot.",
                        (PackageLifecycleStatus?)PackageLifecycleStatus.ReturnedToDepot, PackageEventType.ReturnedToDepot),
                PackageLifecycleStatus.ReturnedToDepot =>
                    ("ReturnedToDepot", ScanFeedbackLevel.Warning, "Deze collo is al retour ingeboekt.",
                        null, PackageEventType.ReturnedToDepot),
                _ => ("UnexpectedStatus", ScanFeedbackLevel.Warning,
                        $"Onverwachte status ({status}) voor een retourscan — geregistreerd.",
                        null, PackageEventType.ReturnedToDepot),
            },
        };

        var scanEvent = new ScanEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            TripId = null, TransportOrderId = package.TransportOrderId, TransportOrderStopId = null,
            PackageId = package.Id, WarehouseLocationId = location?.Id,
            ClientEventId = request.ClientEventId, UserId = _currentUser.CurrentUserId,
            OccurredAt = now, ScanType = request.ScanType,
            Result = level == ScanFeedbackLevel.Warning ? ScanResult.UnexpectedItem : ScanResult.Expected,
            Barcode = request.Barcode.Trim(), Quantity = 1m, DeviceInfo = request.DeviceInfo,
            PackageOutcome = outcome,
        };
        _dbContext.ScanEvents.Add(scanEvent);

        var oldStatus = package.CurrentLifecycleStatus;
        if (newStatus is { } target && PackageLifecycleMachine.IsAllowed(oldStatus, target))
        {
            package.CurrentLifecycleStatus = target;
        }
        else
        {
            newStatus = null; // recorded as a status-less custody event
        }

        // Physical reality wins: a scanned location always updates the projection.
        if (location is not null)
        {
            package.CurrentWarehouseLocationId = location.Id;
        }

        var custodyEvent = _eventWriter.Stage(package, eventType, newStatus is null ? null : oldStatus, newStatus,
            new PackageEventContext(
                BarcodeUsed: request.Barcode.Trim(), Result: outcome, ScanEventId: scanEvent.Id,
                DeviceInfo: request.DeviceInfo, ClientEventId: request.ClientEventId));
        // The custody event's location stamp (the writer has no location parameter yet).
        custodyEvent.WarehouseLocationId = location?.Id;

        await SaveWithReplayGuardAsync(request.ClientEventId, cancellationToken);

        var orderNumber = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.Id == package.TransportOrderId)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return new WarehouseScanFeedbackDto(
            outcome, level, message, package.Id, package.PackageNumber, orderNumber,
            package.CurrentLifecycleStatus.ToString(), location?.Id, location?.Code);
    }

    private async Task SaveWithReplayGuardAsync(Guid? clientEventId, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (clientEventId is not null)
        {
            // A concurrent replay won the unique-key race; the stored row is authoritative.
        }
    }

    private async Task<WarehouseScanFeedbackDto> BuildReplayFeedbackAsync(
        ScanEvent existing, CancellationToken cancellationToken)
    {
        var package = existing.PackageId is { } packageId
            ? await _dbContext.Packages.AsNoTracking()
                .Where(p => p.TenantId == _tenantContext.TenantId && p.Id == packageId)
                .Select(p => new { p.Id, p.PackageNumber, p.CurrentLifecycleStatus, p.TransportOrderId })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var orderNumber = package is null
            ? null
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.Id == package.TransportOrderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken);
        return new WarehouseScanFeedbackDto(
            existing.PackageOutcome ?? "Replayed",
            existing.Result == ScanResult.Expected ? ScanFeedbackLevel.Success : ScanFeedbackLevel.Warning,
            "Deze scan was al verwerkt (herhaalde aanbieding).",
            package?.Id, package?.PackageNumber, orderNumber,
            package?.CurrentLifecycleStatus.ToString(), existing.WarehouseLocationId, null);
    }
}
