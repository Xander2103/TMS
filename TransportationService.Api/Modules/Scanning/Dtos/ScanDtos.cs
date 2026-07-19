using TransportationService.Api.Modules.Scanning.Entities;

namespace TransportationService.Api.Modules.Scanning.Dtos;

public record SubmitScanRequest(
    ScanType ScanType,
    string Barcode,
    decimal Quantity = 1,
    bool Damaged = false,
    string? DamageNote = null,
    string? DeviceInfo = null);

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
    StopScanSummaryDto Summary);

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
    DateTime OccurredAt);

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
