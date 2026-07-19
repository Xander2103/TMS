using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Packages.Services;

/// <summary>
/// Precise, append-only outcome of one package scan. Stored as text on the scan ledger
/// (ScanEvent.PackageOutcome) next to the coarse ScanResult, and returned verbatim in feedback.
/// </summary>
public enum PackageScanOutcome
{
    Success,
    Delivered,
    ReplacedBarcode,
    WrongTrip,
    WrongOrder,
    WrongLoadingStop,
    WrongDeliveryStop,
    AlreadyLoaded,
    AlreadyDelivered,
    NotLoaded,
    CancelledPackage,
    MissingPackage,
    DamagedPackage,
    Refused,
    PartialDelivery,
    NotScannable,
    GroupProcessed,
}

/// <summary>Everything the processor needs about the scan context; the scan event is pre-created so custody events can reference it.</summary>
public sealed record PackageScanInput(
    BarcodeResolution Resolution,
    Guid TripId,
    TransportOrderStop Stop,
    IReadOnlyList<Guid> TripOrderIds,
    Guid? DriverId,
    SubmitScanRequest Request,
    Guid ScanEventId);

public sealed record PackageChildScanResult(
    Guid PackageId, string PackageNumber, string Description,
    PackageScanOutcome Outcome, bool Succeeded, string Message);

public sealed record PackageScanProcessResult(
    PackageScanOutcome Outcome,
    ScanResult LedgerResult,
    ScanFeedbackLevel Level,
    string Message,
    Package Package,
    Guid? ExceptionId,
    IReadOnlyList<PackageChildScanResult> Children);

public interface IPackageScanProcessor
{
    /// <summary>
    /// Classifies one package scan, mutates lifecycle state on the tracked entity and STAGES
    /// custody events and exceptions on the shared DbContext — the caller owns the single save.
    /// Every outcome is returned for the ledger; nothing is silently dropped.
    /// </summary>
    Task<PackageScanProcessResult> ProcessAsync(PackageScanInput input, CancellationToken cancellationToken);
}

public class PackageScanProcessor : IPackageScanProcessor
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IPackageEventWriter _eventWriter;
    private readonly TimeProvider _timeProvider;

    public PackageScanProcessor(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IPackageEventWriter eventWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _eventWriter = eventWriter;
        _timeProvider = timeProvider;
    }

    public async Task<PackageScanProcessResult> ProcessAsync(PackageScanInput input, CancellationToken cancellationToken)
    {
        var package = input.Resolution.Package;

        // A retired barcode still identifies the package, but the physical label is stale:
        // no custody movement — the active label must be used (or the package relabelled again).
        if (input.Resolution.IsRetired)
        {
            return Blocked(package, PackageScanOutcome.ReplacedBarcode, ScanResult.WrongItem,
                $"Deze barcode is vervangen voor {package.PackageNumber}; gebruik het actuele etiket ({package.BarcodeValue}).");
        }

        var children = await _dbContext.Packages
            .Where(p => p.TenantId == _tenantContext.TenantId && p.ParentPackageId == package.Id)
            .OrderBy(p => p.PackageNumber)
            .ToListAsync(cancellationToken);

        if (children.Count > 0)
        {
            return await ProcessGroupAsync(input, package, children, cancellationToken);
        }

        return await ProcessSingleAsync(input, package, isChild: false, cancellationToken);
    }

    private async Task<PackageScanProcessResult> ProcessGroupAsync(
        PackageScanInput input, Package parent, List<Package> children, CancellationToken cancellationToken)
    {
        // Every child is processed and itemized — a cancelled child on the pallet is a
        // failure the driver must see, never a row that silently disappears.
        var childResults = new List<PackageChildScanResult>();
        foreach (var child in children)
        {
            var childOutcome = await ProcessSingleAsync(input, child, isChild: true, cancellationToken);
            childResults.Add(new PackageChildScanResult(
                child.Id, child.PackageNumber, child.Description,
                childOutcome.Outcome, IsMovement(childOutcome.Outcome), childOutcome.Message));
        }

        var succeeded = childResults.Count(c => c.Succeeded);
        var failed = childResults.Count - succeeded;

        // The parent only moves when the whole group moved; a partial group is stated, never hidden.
        var scanType = input.Request.ScanType;
        if (failed == 0)
        {
            var parentTarget = scanType == ScanType.Load ? PackageLifecycleStatus.Loaded : PackageLifecycleStatus.Delivered;
            if (PackageLifecycleMachine.IsAllowed(parent.CurrentLifecycleStatus, parentTarget))
            {
                var old = parent.CurrentLifecycleStatus;
                parent.CurrentLifecycleStatus = parentTarget;
                _eventWriter.Stage(parent,
                    scanType == ScanType.Load ? PackageEventType.LoadScan : PackageEventType.Delivered,
                    old, parentTarget, BuildContext(input, PackageScanOutcome.GroupProcessed.ToString()));
            }
        }

        var message = failed == 0
            ? $"Groep {parent.PackageNumber}: alle {succeeded} colli geregistreerd."
            : $"Groep {parent.PackageNumber}: {succeeded} van {childResults.Count} colli geregistreerd — {failed} mislukt, zie details.";

        return new PackageScanProcessResult(
            PackageScanOutcome.GroupProcessed, ScanResult.Expected,
            failed == 0 ? ScanFeedbackLevel.Success : ScanFeedbackLevel.Warning,
            message, parent, null, childResults);
    }

    private async Task<PackageScanProcessResult> ProcessSingleAsync(
        PackageScanInput input, Package package, bool isChild, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var stop = input.Stop;

        // Wrong vehicle/route: the package's order is not on this trip at all.
        if (!input.TripOrderIds.Contains(package.TransportOrderId))
        {
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.WrongRoutePackage,
                ExceptionSeverity.High,
                $"Colli {package.PackageNumber} gescand op een rit waar de opdracht niet op gepland staat.",
                cancellationToken);
            return Blocked(package, PackageScanOutcome.WrongTrip, ScanResult.WrongItem,
                $"{package.PackageNumber} hoort niet bij deze rit — melding aangemaakt voor planning.",
                exceptionId, stageWrongScanEvent: true, input);
        }

        // Return/depot scans are not stop-directional; only load/unload must match the stop's order.
        if (request.ScanType is (ScanType.Load or ScanType.Unload) && package.TransportOrderId != stop.TransportOrderId)
        {
            return Blocked(package, PackageScanOutcome.WrongOrder, ScanResult.WrongItem,
                $"{package.PackageNumber} hoort bij een andere opdracht op deze rit; scan bij de juiste stop.",
                stageWrongScanEvent: true, input: input);
        }

        return request.ScanType switch
        {
            ScanType.Load => await ProcessLoadAsync(input, package, cancellationToken),
            ScanType.Unload => await ProcessUnloadAsync(input, package, cancellationToken),
            ScanType.Return => ProcessReturn(input, package),
            _ => ProcessDepot(input, package),
        };
    }

    /// <summary>
    /// Return scan: the driver takes a refused/failed/return-pending package back on the
    /// vehicle. No stop-direction check — returns happen wherever the driver stands.
    /// </summary>
    private PackageScanProcessResult ProcessReturn(PackageScanInput input, Package package)
    {
        var old = package.CurrentLifecycleStatus;
        if (old == PackageLifecycleStatus.ReturnLoaded)
        {
            return Blocked(package, PackageScanOutcome.AlreadyLoaded, ScanResult.DuplicateScan,
                $"{package.PackageNumber} is al retour geladen; duplicaat geregistreerd.");
        }

        if (old is not (PackageLifecycleStatus.ReturnPending or PackageLifecycleStatus.Refused
            or PackageLifecycleStatus.DeliveryFailed))
        {
            return Blocked(package, PackageScanOutcome.NotScannable, ScanResult.WrongItem,
                $"{package.PackageNumber} kan in status '{old}' niet retour geladen worden.");
        }

        package.CurrentLifecycleStatus = PackageLifecycleStatus.ReturnLoaded;
        _eventWriter.Stage(package, PackageEventType.ReturnLoaded, old, PackageLifecycleStatus.ReturnLoaded,
            BuildContext(input, PackageScanOutcome.Success.ToString()));
        return new PackageScanProcessResult(PackageScanOutcome.Success, ScanResult.Expected,
            ScanFeedbackLevel.Success, $"{package.PackageNumber} retour geladen.", package, null, []);
    }

    /// <summary>Depot scan: a return-loaded package is checked back into the depot.</summary>
    private PackageScanProcessResult ProcessDepot(PackageScanInput input, Package package)
    {
        var old = package.CurrentLifecycleStatus;
        if (old == PackageLifecycleStatus.ReturnedToDepot)
        {
            return Blocked(package, PackageScanOutcome.AlreadyDelivered, ScanResult.DuplicateScan,
                $"{package.PackageNumber} is al in het depot ontvangen; duplicaat geregistreerd.");
        }

        if (old != PackageLifecycleStatus.ReturnLoaded)
        {
            return Blocked(package, PackageScanOutcome.NotScannable, ScanResult.WrongItem,
                $"{package.PackageNumber} kan in status '{old}' niet in het depot worden ontvangen.");
        }

        package.CurrentLifecycleStatus = PackageLifecycleStatus.ReturnedToDepot;
        _eventWriter.Stage(package, PackageEventType.ReturnedToDepot, old, PackageLifecycleStatus.ReturnedToDepot,
            BuildContext(input, PackageScanOutcome.Success.ToString()));
        return new PackageScanProcessResult(PackageScanOutcome.Success, ScanResult.Expected,
            ScanFeedbackLevel.Success, $"{package.PackageNumber} ontvangen in depot.", package, null, []);
    }

    private async Task<PackageScanProcessResult> ProcessLoadAsync(
        PackageScanInput input, Package package, CancellationToken cancellationToken)
    {
        var stop = input.Stop;

        if (stop.StopType != StopType.Loading)
        {
            return Blocked(package, PackageScanOutcome.WrongLoadingStop, ScanResult.WrongItem,
                $"{package.PackageNumber} kan alleen bij een laadstop worden geladen.");
        }

        if (package.LoadingStopId is { } pinned && pinned != stop.Id)
        {
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.WrongStopPackage,
                ExceptionSeverity.Medium,
                $"Colli {package.PackageNumber} gescand bij een andere laadstop dan gepland.",
                cancellationToken);
            return Blocked(package, PackageScanOutcome.WrongLoadingStop, ScanResult.WrongItem,
                $"{package.PackageNumber} staat gepland voor een andere laadstop — melding aangemaakt.",
                exceptionId, stageWrongScanEvent: true, input);
        }

        switch (package.CurrentLifecycleStatus)
        {
            case PackageLifecycleStatus.Cancelled:
                return Blocked(package, PackageScanOutcome.CancelledPackage, ScanResult.WrongItem,
                    $"{package.PackageNumber} is geannuleerd en mag niet geladen worden.");
            case PackageLifecycleStatus.Missing:
                return Blocked(package, PackageScanOutcome.MissingPackage, ScanResult.WrongItem,
                    $"{package.PackageNumber} is als vermist gemeld; los eerst de uitzondering op (gevonden/verwijderd).");
            case PackageLifecycleStatus.Damaged:
                return Blocked(package, PackageScanOutcome.DamagedPackage, ScanResult.WrongItem,
                    $"{package.PackageNumber} is als beschadigd gemeld; los eerst de uitzondering op voordat het geladen wordt.");
            case PackageLifecycleStatus.Loaded:
            case PackageLifecycleStatus.InTransit:
            case PackageLifecycleStatus.AtStop:
                return Blocked(package, PackageScanOutcome.AlreadyLoaded, ScanResult.DuplicateScan,
                    $"{package.PackageNumber} is al geladen; duplicaat geregistreerd.");
            case PackageLifecycleStatus.Delivered:
            case PackageLifecycleStatus.PartiallyDelivered:
            case PackageLifecycleStatus.DeliveryFailed:
            case PackageLifecycleStatus.Refused:
                return Blocked(package, PackageScanOutcome.AlreadyDelivered, ScanResult.DuplicateScan,
                    $"{package.PackageNumber} is al afgeleverd; laden is niet meer mogelijk.");
        }

        // A planned redelivery loads again through the normal load scan.
        if (package.CurrentLifecycleStatus == PackageLifecycleStatus.RedeliveryPlanned)
        {
            var previous = package.CurrentLifecycleStatus;
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
            _eventWriter.Stage(package, PackageEventType.RedeliveryLoaded, previous, PackageLifecycleStatus.Loaded,
                BuildContext(input, PackageScanOutcome.Success.ToString()));
            return new PackageScanProcessResult(PackageScanOutcome.Success, ScanResult.Expected,
                ScanFeedbackLevel.Success, $"{package.PackageNumber} geladen voor herlevering.", package, null, []);
        }

        if (!PackageLifecycleMachine.IsPreTransit(package.CurrentLifecycleStatus))
        {
            return Blocked(package, PackageScanOutcome.NotScannable, ScanResult.WrongItem,
                $"{package.PackageNumber} kan in status '{package.CurrentLifecycleStatus}' niet geladen worden.");
        }

        var old = package.CurrentLifecycleStatus;

        // Damage discovered at the dock: custody moves to Damaged (not Loaded) and an
        // exception opens — the package never silently rides along.
        if (input.Request.Damaged)
        {
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Damaged;
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.DamagedPackage,
                ExceptionSeverity.High,
                $"Colli {package.PackageNumber} beschadigd aangetroffen bij het laden."
                + (string.IsNullOrWhiteSpace(input.Request.DamageNote) ? string.Empty : $" {input.Request.DamageNote!.Trim()}"),
                cancellationToken);
            _eventWriter.Stage(package, PackageEventType.DamageReported, old, PackageLifecycleStatus.Damaged,
                BuildContext(input, PackageScanOutcome.DamagedPackage.ToString(), exceptionId, input.Request.DamageNote));
            return new PackageScanProcessResult(PackageScanOutcome.DamagedPackage, ScanResult.DamagedItem,
                ScanFeedbackLevel.Warning,
                $"Schade geregistreerd voor {package.PackageNumber}; het colli is niet geladen.",
                package, exceptionId, []);
        }

        package.CurrentLifecycleStatus = PackageLifecycleStatus.Loaded;
        _eventWriter.Stage(package, PackageEventType.LoadScan, old, PackageLifecycleStatus.Loaded,
            BuildContext(input, PackageScanOutcome.Success.ToString()));
        return new PackageScanProcessResult(PackageScanOutcome.Success, ScanResult.Expected,
            ScanFeedbackLevel.Success, $"{package.PackageNumber} geladen.", package, null, []);
    }

    private async Task<PackageScanProcessResult> ProcessUnloadAsync(
        PackageScanInput input, Package package, CancellationToken cancellationToken)
    {
        var stop = input.Stop;

        if (stop.StopType != StopType.Unloading)
        {
            return Blocked(package, PackageScanOutcome.WrongDeliveryStop, ScanResult.WrongItem,
                $"{package.PackageNumber} kan alleen bij een losstop worden afgeleverd.");
        }

        if (package.DeliveryStopId is { } pinned && pinned != stop.Id)
        {
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.WrongStopPackage,
                ExceptionSeverity.High,
                $"Colli {package.PackageNumber} gescand bij een andere losstop dan gepland.",
                cancellationToken);
            return Blocked(package, PackageScanOutcome.WrongDeliveryStop, ScanResult.WrongItem,
                $"{package.PackageNumber} hoort bij een andere losstop — melding aangemaakt.",
                exceptionId, stageWrongScanEvent: true, input);
        }

        switch (package.CurrentLifecycleStatus)
        {
            case PackageLifecycleStatus.Cancelled:
                return Blocked(package, PackageScanOutcome.CancelledPackage, ScanResult.WrongItem,
                    $"{package.PackageNumber} is geannuleerd; er valt niets af te leveren.");
            case PackageLifecycleStatus.Missing:
                return Blocked(package, PackageScanOutcome.MissingPackage, ScanResult.WrongItem,
                    $"{package.PackageNumber} is als vermist gemeld; los eerst de uitzondering op.");
            case PackageLifecycleStatus.Delivered:
                return Blocked(package, PackageScanOutcome.AlreadyDelivered, ScanResult.DuplicateScan,
                    $"{package.PackageNumber} is al afgeleverd; duplicaat geregistreerd.");
        }

        if (PackageLifecycleMachine.IsPreTransit(package.CurrentLifecycleStatus))
        {
            return Blocked(package, PackageScanOutcome.NotLoaded, ScanResult.WrongItem,
                $"{package.PackageNumber} is nooit geladen; controleer het voertuig en meld dit bij planning.");
        }

        var old = package.CurrentLifecycleStatus;
        var request = input.Request;

        if (old == PackageLifecycleStatus.Damaged)
        {
            // Damage was already recorded; delivering it is allowed but stays flagged.
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Delivered;
            _eventWriter.Stage(package, PackageEventType.Delivered, old, PackageLifecycleStatus.Delivered,
                BuildContext(input, PackageScanOutcome.Delivered.ToString()));
            return new PackageScanProcessResult(PackageScanOutcome.Delivered, ScanResult.Expected,
                ScanFeedbackLevel.Warning,
                $"{package.PackageNumber} afgeleverd met bestaande schademelding.", package, null, []);
        }

        if (old is not (PackageLifecycleStatus.Loaded or PackageLifecycleStatus.InTransit or PackageLifecycleStatus.AtStop))
        {
            return Blocked(package, PackageScanOutcome.NotScannable, ScanResult.WrongItem,
                $"{package.PackageNumber} kan in status '{old}' niet afgeleverd worden.");
        }

        if (request.Refused)
        {
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Refused;
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.RejectedDelivery,
                ExceptionSeverity.High,
                $"Colli {package.PackageNumber} geweigerd door de ontvanger."
                + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" {request.Note!.Trim()}"),
                cancellationToken);
            _eventWriter.Stage(package, PackageEventType.Refused, old, PackageLifecycleStatus.Refused,
                BuildContext(input, PackageScanOutcome.Refused.ToString(), exceptionId, request.Note));
            return new PackageScanProcessResult(PackageScanOutcome.Refused, ScanResult.Expected,
                ScanFeedbackLevel.Warning,
                $"Weigering geregistreerd voor {package.PackageNumber}; kies een vervolgactie (retour/herlevering).",
                package, exceptionId, []);
        }

        if (request.Partial)
        {
            package.CurrentLifecycleStatus = PackageLifecycleStatus.PartiallyDelivered;
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.PartialDelivery,
                ExceptionSeverity.Medium,
                $"Colli {package.PackageNumber} gedeeltelijk afgeleverd."
                + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" {request.Note!.Trim()}"),
                cancellationToken);
            _eventWriter.Stage(package, PackageEventType.PartialDelivery, old, PackageLifecycleStatus.PartiallyDelivered,
                BuildContext(input, PackageScanOutcome.PartialDelivery.ToString(), exceptionId, request.Note));
            return new PackageScanProcessResult(PackageScanOutcome.PartialDelivery, ScanResult.Expected,
                ScanFeedbackLevel.Warning,
                $"Gedeeltelijke levering geregistreerd voor {package.PackageNumber}.", package, exceptionId, []);
        }

        if (request.Damaged)
        {
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Damaged;
            var exceptionId = await EnsureExceptionAsync(input, package, ExecutionExceptionType.DamagedPackage,
                ExceptionSeverity.High,
                $"Colli {package.PackageNumber} beschadigd aangetroffen bij aflevering."
                + (string.IsNullOrWhiteSpace(request.DamageNote) ? string.Empty : $" {request.DamageNote!.Trim()}"),
                cancellationToken);
            _eventWriter.Stage(package, PackageEventType.DamageReported, old, PackageLifecycleStatus.Damaged,
                BuildContext(input, PackageScanOutcome.DamagedPackage.ToString(), exceptionId, request.DamageNote));
            return new PackageScanProcessResult(PackageScanOutcome.DamagedPackage, ScanResult.DamagedItem,
                ScanFeedbackLevel.Warning,
                $"Schade geregistreerd voor {package.PackageNumber}; kies een vervolgactie voordat het wordt afgeleverd.",
                package, exceptionId, []);
        }

        package.CurrentLifecycleStatus = PackageLifecycleStatus.Delivered;
        _eventWriter.Stage(package, PackageEventType.Delivered, old, PackageLifecycleStatus.Delivered,
            BuildContext(input, PackageScanOutcome.Delivered.ToString()));
        return new PackageScanProcessResult(PackageScanOutcome.Delivered, ScanResult.Expected,
            ScanFeedbackLevel.Success, $"{package.PackageNumber} afgeleverd.", package, null, []);
    }

    /// <summary>Blocked outcomes never move custody, but wrong-context ones still leave a custody trace.</summary>
    private PackageScanProcessResult Blocked(
        Package package, PackageScanOutcome outcome, ScanResult ledgerResult, string message,
        Guid? exceptionId = null, bool stageWrongScanEvent = false, PackageScanInput? input = null)
    {
        if (stageWrongScanEvent && input is not null)
        {
            _eventWriter.Stage(package, PackageEventType.WrongPackageScan, package.CurrentLifecycleStatus,
                package.CurrentLifecycleStatus, BuildContext(input, outcome.ToString(), exceptionId));
        }
        return new PackageScanProcessResult(outcome, ledgerResult, ScanFeedbackLevel.Warning, message,
            package, exceptionId, []);
    }

    /// <summary>
    /// Opens an execution exception unless the same open exception already exists for this
    /// package and trip — repeated scanning of the same problem must not flood dispatch.
    /// </summary>
    private async Task<Guid> EnsureExceptionAsync(
        PackageScanInput input, Package package, ExecutionExceptionType type, ExceptionSeverity severity,
        string description, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ExecutionExceptions
            .Where(e => e.TenantId == _tenantContext.TenantId && e.PackageId == package.Id
                        && e.TripId == input.TripId && e.Type == type
                        && (e.Status == ExecutionExceptionStatus.Open || e.Status == ExecutionExceptionStatus.Investigating))
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        var exception = new ExecutionException
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TripId = input.TripId,
            TransportOrderId = package.TransportOrderId,
            TransportOrderStopId = input.Stop.Id,
            PackageId = package.Id,
            Type = type,
            Severity = severity,
            Status = ExecutionExceptionStatus.Open,
            Description = description,
            ReportedByUserId = _currentUserContext.CurrentUserId,
            DriverId = input.DriverId,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _dbContext.Add(exception);
        package.CurrentExceptionStatus = PackageExceptionState.Open;
        return exception.Id;
    }

    private static PackageEventContext BuildContext(
        PackageScanInput input, string result, Guid? exceptionId = null, string? notes = null) => new(
        TripId: input.TripId,
        StopId: input.Stop.Id,
        OrderId: null,
        DriverId: input.DriverId,
        DeviceInfo: string.IsNullOrWhiteSpace(input.Request.DeviceInfo) ? null : input.Request.DeviceInfo.Trim(),
        BarcodeUsed: input.Request.Barcode?.Trim(),
        Result: result,
        ScanEventId: input.ScanEventId,
        ExceptionId: exceptionId,
        Notes: string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        ClientEventId: input.Request.ClientEventId);

    private static bool IsMovement(PackageScanOutcome outcome) =>
        outcome is PackageScanOutcome.Success or PackageScanOutcome.Delivered
            or PackageScanOutcome.Refused or PackageScanOutcome.PartialDelivery
            or PackageScanOutcome.DamagedPackage;
}
