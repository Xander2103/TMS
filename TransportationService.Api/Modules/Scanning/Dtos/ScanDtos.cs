using TransportationService.Api.Modules.Scanning.Entities;

namespace TransportationService.Api.Modules.Scanning.Dtos;

public record SubmitScanRequest(
    ScanType ScanType,
    string Barcode,
    decimal Quantity = 1,
    bool Damaged = false,
    string? DamageNote = null,
    string? DeviceInfo = null,
    Guid? ClientEventId = null,
    bool Refused = false,
    bool Partial = false,
    string? Note = null);

/// <summary>Sets the absolute scanned quantity for one item/action; the reason is mandatory and audited.</summary>
public record ScanCorrectionRequest(Guid CargoItemId, ScanType ScanType, decimal Quantity, string Reason);

/// <summary>Scan state of one expected cargo item at a stop (under-delivery shows as Missing/Partial).</summary>
public enum CargoScanState
{
    Missing,
    Partial,
    Complete,
    Over,
}

public record CargoItemScanSummaryDto(
    Guid CargoItemId,
    int Sequence,
    string Description,
    string? Barcode,
    decimal ExpectedQuantity,
    string? QuantityUnit,
    decimal ScannedQuantity,
    decimal DamagedQuantity,
    CargoScanState State);

public record StopScanSummaryDto(
    Guid TransportOrderStopId,
    ScanType ScanType,
    IReadOnlyList<CargoItemScanSummaryDto> Items,
    int UnexpectedScanCount,
    int TotalScanCount);

/// <summary>Per-child result of a group/pallet scan; failed children are itemized, never hidden.</summary>
public record PackageChildScanResultDto(
    Guid PackageId,
    string PackageNumber,
    string Description,
    string Outcome,
    bool Succeeded,
    string Message);

/// <summary>Package block on scan feedback, present when the barcode resolved to a tracked package.</summary>
public record PackageScanFeedbackDto(
    Guid PackageId,
    string PackageNumber,
    string Description,
    string Outcome,
    string LifecycleStatus,
    Guid? ExceptionId,
    IReadOnlyList<PackageChildScanResultDto> Children);

/// <summary>Immediate feedback for the scanning UI: classification plus the updated tallies.</summary>
public record ScanFeedbackDto(
    Guid ScanEventId,
    ScanResult Result,
    ScanFeedbackLevel Level,
    string Message,
    Guid? CargoItemId,
    string? CargoDescription,
    decimal AcceptedQuantity,
    decimal ExpectedQuantity,
    StopScanSummaryDto Summary,
    PackageScanFeedbackDto? Package = null,
    bool Replayed = false);

public enum ScanFeedbackLevel
{
    Success,
    Warning,
}

public record ScanEventDto(
    Guid Id,
    Guid TransportOrderStopId,
    Guid? CargoItemId,
    string? CargoDescription,
    ScanType ScanType,
    ScanResult Result,
    string? Barcode,
    decimal Quantity,
    bool Damaged,
    string? DamageNote,
    string? CorrectionReason,
    string? DeviceInfo,
    string? UserName,
    DateTime OccurredAt,
    Guid? PackageId = null,
    string? PackageNumber = null,
    string? PackageOutcome = null);

public enum ScanOutcome
{
    Success,
    NotFound,
    NotYourTrip,
    InvalidState,
    ValidationFailed,
}

public record ScanOperationResult(ScanOutcome Outcome, ScanFeedbackDto? Feedback, string? Error = null)
{
    public static ScanOperationResult Success(ScanFeedbackDto feedback) => new(ScanOutcome.Success, feedback);
    public static readonly ScanOperationResult NotFound = new(ScanOutcome.NotFound, null);
    public static readonly ScanOperationResult NotYourTrip = new(ScanOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
    public static ScanOperationResult InvalidState(string error) => new(ScanOutcome.InvalidState, null, error);
    public static ScanOperationResult Invalid(string error) => new(ScanOutcome.ValidationFailed, null, error);
}

public record ScanHistoryResult(ScanOutcome Outcome, IReadOnlyList<ScanEventDto>? Events, string? Error = null)
{
    public static ScanHistoryResult Success(IReadOnlyList<ScanEventDto> events) => new(ScanOutcome.Success, events);
    public static readonly ScanHistoryResult NotFound = new(ScanOutcome.NotFound, null);
    public static readonly ScanHistoryResult NotYourTrip = new(ScanOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
}

public record ScanSummaryResult(ScanOutcome Outcome, StopScanSummaryDto? Summary, string? Error = null)
{
    public static ScanSummaryResult Success(StopScanSummaryDto summary) => new(ScanOutcome.Success, summary);
    public static readonly ScanSummaryResult NotFound = new(ScanOutcome.NotFound, null);
    public static readonly ScanSummaryResult NotYourTrip = new(ScanOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
}
