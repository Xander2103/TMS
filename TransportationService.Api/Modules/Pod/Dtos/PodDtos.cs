using TransportationService.Api.Modules.Pod.Entities;

namespace TransportationService.Api.Modules.Pod.Dtos;

public record FinalizePodRequest(
    string RecipientName,
    string? RecipientRole,
    PodOutcome Outcome,
    bool DamageReported,
    bool MissingReported,
    string? Notes,
    /// <summary>Signature as a data URL (data:image/png;base64,...), captured from a canvas.</summary>
    string? SignatureBase64,
    decimal? Latitude,
    decimal? Longitude,
    /// <summary>Recipient confirmed the per-package outcome list; required when packages exist.</summary>
    bool PackagesAcknowledged = false);

public record CorrectPodRequest(
    string RecipientName,
    string? RecipientRole,
    PodOutcome Outcome,
    bool DamageReported,
    bool MissingReported,
    string? Notes,
    string? SignatureBase64,
    decimal? Latitude,
    decimal? Longitude,
    string Reason);

/// <summary>One frozen scan line inside the proof.</summary>
public record PodScanLineDto(
    string Description,
    string? Barcode,
    decimal ExpectedQuantity,
    decimal ScannedQuantity,
    decimal DamagedQuantity,
    string State);

/// <summary>One tracked package frozen into the proof: its outcome at finalisation time.</summary>
public record PodPackageLineDto(
    string PackageNumber,
    string Description,
    decimal Quantity,
    string UnitType,
    string Outcome,
    bool ExceptionOpen);

public record PodPhotoDto(Guid Id, PodPhotoCategory Category, string FileName, string ContentType, DateTime CreatedAt);

public record PodVersionDto(Guid Id, int Version, bool IsCurrent, DateTime DeliveredAt, PodOutcome Outcome, string? CorrectionReason);

public record PodDetailDto(
    Guid Id,
    int Version,
    bool IsCurrent,
    Guid TripId,
    string TripNumber,
    Guid TransportOrderStopId,
    string? StopLabel,
    Guid TransportOrderId,
    string? OrderNumber,
    string? CustomerName,
    string RecipientName,
    string? RecipientRole,
    PodOutcome Outcome,
    bool DamageReported,
    bool MissingReported,
    string? Notes,
    DateTime DeliveredAt,
    decimal? Latitude,
    decimal? Longitude,
    bool HasSignature,
    IReadOnlyList<PodScanLineDto> ScannedSummary,
    IReadOnlyList<PodPackageLineDto> PackageSummary,
    bool PackagesAcknowledged,
    string? FinalisedByName,
    string? DriverName,
    string? CorrectionReason,
    Guid? CorrectedFromPodId,
    bool CustomerVisible,
    IReadOnlyList<PodPhotoDto> Photos,
    IReadOnlyList<PodVersionDto> Versions);

public enum PodOutcomeResult
{
    Success,
    NotFound,
    NotYourTrip,
    InvalidState,
    ValidationFailed,
}

public record PodOperationResult(PodOutcomeResult Outcome, PodDetailDto? Pod, string? Error = null)
{
    public static PodOperationResult Success(PodDetailDto pod) => new(PodOutcomeResult.Success, pod);
    public static readonly PodOperationResult NotFound = new(PodOutcomeResult.NotFound, null);
    public static readonly PodOperationResult NotYourTrip = new(PodOutcomeResult.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
    public static PodOperationResult InvalidState(string error) => new(PodOutcomeResult.InvalidState, null, error);
    public static PodOperationResult Invalid(string error) => new(PodOutcomeResult.ValidationFailed, null, error);
}
