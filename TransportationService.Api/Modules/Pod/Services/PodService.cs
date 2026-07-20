using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Pod.Dtos;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Pod.Services;

public class PodService : IPodService
{
    private const string EntityType = "ProofOfDelivery";
    private const int MaxSignatureBytes = 1024 * 1024;

    private static readonly string[] AllowedPhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly IScanService _scanService;
    private readonly ITripPackageService _tripPackageService;
    private readonly IFileStorageService _fileStorageService;
    private readonly INotificationService _notificationService;
    private readonly IMessageOutboxService _messageOutbox;
    private readonly TimeProvider _timeProvider;

    public PodService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        IScanService scanService,
        ITripPackageService tripPackageService,
        IFileStorageService fileStorageService,
        INotificationService notificationService,
        IMessageOutboxService messageOutbox,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _scanService = scanService;
        _tripPackageService = tripPackageService;
        _fileStorageService = fileStorageService;
        _notificationService = notificationService;
        _messageOutbox = messageOutbox;
        _timeProvider = timeProvider;
    }

    public async Task<PodOperationResult> FinalizeAsync(
        Guid tripId, Guid stopId, FinalizePodRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientName))
        {
            return PodOperationResult.Invalid("De naam van de ontvanger is verplicht.");
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId, cancellationToken);
        if (trip is null)
        {
            return PodOperationResult.NotFound;
        }

        var driverId = await CurrentDriverIdAsync(cancellationToken);
        if (restrictToOwnDriver && (driverId is null || trip.DriverId != driverId))
        {
            return PodOperationResult.NotYourTrip;
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).ToList();
        var stop = await _dbContext.TransportOrderStops.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stopId && s.TenantId == _tenantContext.TenantId
                                      && orderIds.Contains(s.TransportOrderId), cancellationToken);
        if (stop is null)
        {
            return PodOperationResult.NotFound;
        }

        var existing = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TripId == tripId && p.TransportOrderStopId == stopId
                        && p.IsCurrent && p.TenantId == _tenantContext.TenantId)
            .Select(p => new { p.Id, p.ClientRequestId })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Offline replay of the exact same finalisation returns the stored proof as
            // success; a genuinely different attempt still gets the correction hint.
            if (request.ClientRequestId is { } key && existing.ClientRequestId == key)
            {
                return PodOperationResult.Success((await MapDetailAsync(existing.Id, cancellationToken))!);
            }

            return PodOperationResult.InvalidState(
                "Voor deze stop bestaat al een afgeronde POD; gebruik een correctie om iets recht te zetten.");
        }

        var signatureResult = await StoreSignatureAsync(request.SignatureBase64, cancellationToken);
        if (signatureResult.Error is not null)
        {
            return PodOperationResult.Invalid(signatureResult.Error);
        }

        // Freeze the scan tallies as evidence inside the proof.
        var scanSummary = await _scanService.GetStopSummaryAsync(tripId, stopId, restrictToOwnDriver: false, cancellationToken);
        var snapshot = (scanSummary.Summary?.Items ?? [])
            .Select(i => new PodScanLineDto(i.Description, i.Barcode, i.ExpectedQuantity, i.ScannedQuantity, i.DamagedQuantity, i.State.ToString()))
            .ToList();

        // Package gate: every mandatory package needs a delivery outcome before proof can be
        // signed, and the recipient must confirm the package list when packages exist.
        var unresolved = await _tripPackageService.GetUnresolvedAtStopAsync(tripId, stopId, cancellationToken);
        if (unresolved.Count > 0)
        {
            var numbers = string.Join(", ", unresolved.Select(p => p.PackageNumber).Take(5));
            return PodOperationResult.Invalid(
                $"Er zijn nog {unresolved.Count} verplichte colli zonder uitkomst ({numbers}); scan of registreer ze eerst.");
        }

        var packageLines = await BuildPackageSnapshotAsync(tripId, stopId, cancellationToken);
        if (packageLines.Count > 0 && !request.PackagesAcknowledged)
        {
            return PodOperationResult.Invalid("Bevestig de collilijst met de ontvanger voordat de POD wordt afgerond.");
        }

        var pod = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = tripId,
            TransportOrderId = stop.TransportOrderId,
            TransportOrderStopId = stopId,
            Version = 1,
            IsCurrent = true,
            RecipientName = request.RecipientName.Trim(),
            RecipientRole = Trim(request.RecipientRole),
            Outcome = request.Outcome,
            DamageReported = request.DamageReported,
            MissingReported = request.MissingReported,
            Notes = Trim(request.Notes),
            DeliveredAt = _timeProvider.GetUtcNow().UtcDateTime,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            SignaturePath = signatureResult.StoragePath,
            ScannedSummaryJson = JsonSerializer.Serialize(snapshot, SnapshotJson),
            PackageSummaryJson = JsonSerializer.Serialize(packageLines, SnapshotJson),
            PackagesAcknowledged = packageLines.Count > 0 && request.PackagesAcknowledged,
            FinalisedByUserId = _currentUserContext.CurrentUserId,
            DriverId = driverId,
            ClientRequestId = request.ClientRequestId,
        };
        _dbContext.Add(pod);
        // Every package at the stop gets a PodFinalized custody event in the same save.
        await _tripPackageService.StagePodFinalizedAsync(tripId, stopId, pod.Version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, pod.Id.ToString(), "Finalised", null,
            new { pod.TripId, pod.TransportOrderStopId, pod.RecipientName, pod.Outcome, pod.Version }, cancellationToken);

        // Dispatch hears that proof landed (refused/partial outcomes stand out by message).
        await _notificationService.NotifyPermissionHoldersAsync(
            PermissionCodes.PlanningEdit, "pod_completed", "POD vastgelegd",
            $"{trip.TripNumber}: POD voor {stop.LocationName ?? stop.City} — {pod.Outcome}.",
            $"/pods/{pod.Id}", cancellationToken);

        // The customer's "POD available" mail rides the provider-neutral outbox.
        var orderInfo = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.Id == pod.TransportOrderId && o.TenantId == _tenantContext.TenantId)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                o => o.CustomerId, c => c.Id, (o, c) => new { o.OrderNumber, CustomerId = c.Id, CustomerName = c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (orderInfo is not null)
        {
            await _messageOutbox.QueueAsync(new MessageRequest(
                MessageKinds.PodAvailable, MessageOwnerType.Customer, orderInfo.CustomerId,
                new Dictionary<string, string>
                {
                    ["orderNumber"] = orderInfo.OrderNumber,
                    ["customerName"] = orderInfo.CustomerName,
                },
                EntityType, pod.Id.ToString(),
                $"pod_available:{pod.Id}"), cancellationToken);
        }

        return PodOperationResult.Success((await MapDetailAsync(pod.Id, cancellationToken))!);
    }

    /// <summary>Leaf packages expected at the stop, mapped to frozen proof lines.</summary>
    private async Task<List<PodPackageLineDto>> BuildPackageSnapshotAsync(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var result = await _tripPackageService.GetChecklistAsync(tripId, stopId, restrictToOwnDriver: false, cancellationToken);
        var checklist = result.Payload as TripPackageChecklistDto;
        var items = checklist?.Stops.FirstOrDefault()?.Packages ?? [];
        return items
            .Where(i => !i.IsGroup)
            .Select(i => new PodPackageLineDto(
                i.PackageNumber, i.Description, i.Quantity, i.UnitTypeLabel ?? i.UnitType,
                i.Status.ToString(), i.ExceptionState == PackageExceptionState.Open))
            .ToList();
    }

    public async Task<PodOperationResult> CorrectAsync(Guid podId, CorrectPodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return PodOperationResult.Invalid("Een reden is verplicht bij een POD-correctie.");
        }

        if (string.IsNullOrWhiteSpace(request.RecipientName))
        {
            return PodOperationResult.Invalid("De naam van de ontvanger is verplicht.");
        }

        var original = await _dbContext.ProofsOfDelivery
            .FirstOrDefaultAsync(p => p.Id == podId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (original is null)
        {
            return PodOperationResult.NotFound;
        }

        if (!original.IsCurrent)
        {
            return PodOperationResult.InvalidState(
                "Alleen de actuele POD-versie kan worden gecorrigeerd; deze versie is al vervangen.");
        }

        var signatureResult = await StoreSignatureAsync(request.SignatureBase64, cancellationToken);
        if (signatureResult.Error is not null)
        {
            return PodOperationResult.Invalid(signatureResult.Error);
        }

        var replacement = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = original.TripId,
            TransportOrderId = original.TransportOrderId,
            TransportOrderStopId = original.TransportOrderStopId,
            Version = original.Version + 1,
            IsCurrent = true,
            RecipientName = request.RecipientName.Trim(),
            RecipientRole = Trim(request.RecipientRole),
            Outcome = request.Outcome,
            DamageReported = request.DamageReported,
            MissingReported = request.MissingReported,
            Notes = Trim(request.Notes),
            // The delivery moment is historical fact — the correction keeps it.
            DeliveredAt = original.DeliveredAt,
            Latitude = request.Latitude ?? original.Latitude,
            Longitude = request.Longitude ?? original.Longitude,
            SignaturePath = signatureResult.StoragePath ?? original.SignaturePath,
            ScannedSummaryJson = original.ScannedSummaryJson,
            PackageSummaryJson = original.PackageSummaryJson,
            PackagesAcknowledged = original.PackagesAcknowledged,
            FinalisedByUserId = _currentUserContext.CurrentUserId,
            DriverId = original.DriverId,
            CorrectionReason = request.Reason.Trim(),
            CorrectedFromPodId = original.Id,
        };

        original.IsCurrent = false;
        _dbContext.Add(replacement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, replacement.Id.ToString(), "Corrected",
            new { CorrectedPodId = original.Id, original.Version },
            new { replacement.Version, replacement.RecipientName, replacement.Outcome, Reason = replacement.CorrectionReason },
            cancellationToken);

        return PodOperationResult.Success((await MapDetailAsync(replacement.Id, cancellationToken))!);
    }

    public Task<PodDetailDto?> GetByIdAsync(Guid podId, CancellationToken cancellationToken) =>
        MapDetailAsync(podId, cancellationToken);

    public async Task<PodDetailDto?> GetForStopAsync(
        Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        if (restrictToOwnDriver)
        {
            var driverId = await CurrentDriverIdAsync(cancellationToken);
            var tripDriverId = await _dbContext.Trips.AsNoTracking()
                .Where(t => t.Id == tripId && t.TenantId == _tenantContext.TenantId)
                .Select(t => t.DriverId)
                .FirstOrDefaultAsync(cancellationToken);
            if (driverId is null || tripDriverId != driverId)
            {
                return null;
            }
        }

        var currentId = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TripId == tripId && p.TransportOrderStopId == stopId
                        && p.IsCurrent && p.TenantId == _tenantContext.TenantId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return currentId is { } id ? await MapDetailAsync(id, cancellationToken) : null;
    }

    public async Task<PodOperationResult> AttachPhotoAsync(
        Guid podId, PodPhotoCategory category, string fileName, string contentType, Stream content,
        bool restrictToOwnDriver, CancellationToken cancellationToken)
    {
        var pod = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == podId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (pod is null)
        {
            return PodOperationResult.NotFound;
        }

        if (!pod.IsCurrent)
        {
            return PodOperationResult.InvalidState("Foto's kunnen alleen aan de actuele POD-versie worden toegevoegd.");
        }

        if (restrictToOwnDriver)
        {
            var driverId = await CurrentDriverIdAsync(cancellationToken);
            var tripDriverId = await _dbContext.Trips.AsNoTracking()
                .Where(t => t.Id == pod.TripId && t.TenantId == _tenantContext.TenantId)
                .Select(t => t.DriverId)
                .FirstOrDefaultAsync(cancellationToken);
            if (driverId is null || tripDriverId != driverId)
            {
                return PodOperationResult.NotYourTrip;
            }
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedPhotoExtensions.Contains(extension))
        {
            return PodOperationResult.Invalid("Alleen foto's (.jpg, .jpeg, .png, .webp) zijn toegestaan.");
        }

        var storagePath = await _fileStorageService.SaveAsync(
            _tenantContext.TenantId, "pod-photos", fileName, content, cancellationToken);

        _dbContext.Add(new PodPhoto
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            ProofOfDeliveryId = pod.Id,
            Category = category,
            FileName = fileName,
            ContentType = contentType,
            StoragePath = storagePath,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, pod.Id.ToString(), "PhotoAttached", null,
            new { fileName, Category = category }, cancellationToken);

        return PodOperationResult.Success((await MapDetailAsync(pod.Id, cancellationToken))!);
    }

    public async Task<(Stream Content, string ContentType, string FileName)?> OpenPhotoAsync(
        Guid podId, Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await _dbContext.PodPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProofOfDeliveryId == podId
                                      && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await _fileStorageService.OpenReadAsync(photo.StoragePath, cancellationToken);
        return (stream, photo.ContentType, photo.FileName);
    }

    public async Task<(Stream Content, string ContentType)?> OpenSignatureAsync(Guid podId, CancellationToken cancellationToken)
    {
        var signaturePath = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.Id == podId && p.TenantId == _tenantContext.TenantId)
            .Select(p => p.SignaturePath)
            .FirstOrDefaultAsync(cancellationToken);
        if (signaturePath is null)
        {
            return null;
        }

        var stream = await _fileStorageService.OpenReadAsync(signaturePath, cancellationToken);
        return (stream, "image/png");
    }

    private sealed record SignatureStoreResult(string? StoragePath, string? Error);

    /// <summary>Decodes and stores a canvas data URL; only PNG signatures up to 1 MB are accepted.</summary>
    private async Task<SignatureStoreResult> StoreSignatureAsync(string? signatureBase64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64))
        {
            return new SignatureStoreResult(null, null);
        }

        const string prefix = "data:image/png;base64,";
        if (!signatureBase64.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureStoreResult(null, "De handtekening moet een PNG-afbeelding zijn (data-URL).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(signatureBase64[prefix.Length..]);
        }
        catch (FormatException)
        {
            return new SignatureStoreResult(null, "De handtekening kon niet worden gelezen.");
        }

        if (bytes.Length == 0 || bytes.Length > MaxSignatureBytes)
        {
            return new SignatureStoreResult(null, "De handtekening moet tussen 1 byte en 1 MB groot zijn.");
        }

        using var stream = new MemoryStream(bytes);
        var path = await _fileStorageService.SaveAsync(
            _tenantContext.TenantId, "pod-signatures", "signature.png", stream, cancellationToken);
        return new SignatureStoreResult(path, null);
    }

    private async Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.EmployeeId != null)
            .Join(_dbContext.Drivers.AsNoTracking().Where(d => d.TenantId == _tenantContext.TenantId),
                u => u.EmployeeId, d => d.EmployeeId, (u, d) => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PodDetailDto?> MapDetailAsync(Guid podId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var pod = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == podId && p.TenantId == tenantId, cancellationToken);
        if (pod is null)
        {
            return null;
        }

        var tripNumber = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.Id == pod.TripId && t.TenantId == tenantId)
            .Select(t => t.TripNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var stopLabel = await _dbContext.TransportOrderStops.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.Id == pod.TransportOrderStopId && s.TenantId == tenantId)
            .Select(s => s.LocationName ?? s.City)
            .FirstOrDefaultAsync(cancellationToken);

        var orderInfo = await _dbContext.TransportOrders.AsNoTracking().IgnoreQueryFilters()
            .Where(o => o.Id == pod.TransportOrderId && o.TenantId == tenantId)
            .Join(_dbContext.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => c.TenantId == tenantId),
                o => o.CustomerId, c => c.Id,
                (o, c) => new { o.OrderNumber, CustomerName = c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var finalisedByName = pod.FinalisedByUserId is { } uid
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == uid && u.TenantId == tenantId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        string? driverName = null;
        if (pod.DriverId is { } driverId)
        {
            driverName = await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.Id == driverId && d.TenantId == tenantId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var photos = await _dbContext.PodPhotos.AsNoTracking()
            .Where(p => p.ProofOfDeliveryId == pod.Id && p.TenantId == tenantId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PodPhotoDto(p.Id, p.Category, p.FileName, p.ContentType, p.CreatedAt))
            .ToListAsync(cancellationToken);

        var versions = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TripId == pod.TripId && p.TransportOrderStopId == pod.TransportOrderStopId
                        && p.TenantId == tenantId)
            .OrderBy(p => p.Version)
            .Select(p => new PodVersionDto(p.Id, p.Version, p.IsCurrent, p.DeliveredAt, p.Outcome, p.CorrectionReason))
            .ToListAsync(cancellationToken);

        List<PodScanLineDto> snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<List<PodScanLineDto>>(pod.ScannedSummaryJson, SnapshotJson) ?? [];
        }
        catch (JsonException)
        {
            snapshot = [];
        }

        List<PodPackageLineDto> packageSnapshot;
        try
        {
            packageSnapshot = JsonSerializer.Deserialize<List<PodPackageLineDto>>(pod.PackageSummaryJson, SnapshotJson) ?? [];
        }
        catch (JsonException)
        {
            packageSnapshot = [];
        }

        return new PodDetailDto(
            pod.Id, pod.Version, pod.IsCurrent,
            pod.TripId, tripNumber,
            pod.TransportOrderStopId, stopLabel,
            pod.TransportOrderId, orderInfo?.OrderNumber, orderInfo?.CustomerName,
            pod.RecipientName, pod.RecipientRole,
            pod.Outcome, pod.DamageReported, pod.MissingReported, pod.Notes,
            pod.DeliveredAt, pod.Latitude, pod.Longitude,
            pod.SignaturePath is not null,
            snapshot,
            packageSnapshot,
            pod.PackagesAcknowledged,
            finalisedByName, driverName,
            pod.CorrectionReason, pod.CorrectedFromPodId,
            pod.CustomerVisible,
            photos, versions);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
