using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.Packages.Dtos;

/// <summary>How incomplete loading affects departure; mirrors TenantSettings.PackageDepartureRule.</summary>
public enum PackageDepartureRule
{
    AllowWithWarning,
    RequireOverride,
    Block,
}

/// <summary>One expected package on a stop checklist, with its live custody state.</summary>
public record TripPackageChecklistItemDto(
    Guid PackageId,
    string PackageNumber,
    string Description,
    decimal Quantity,
    string UnitType,
    string? UnitTypeLabel,
    PackageLifecycleStatus Status,
    PackageExceptionState ExceptionState,
    bool IsMandatory,
    bool IsGroup,
    Guid? ParentPackageId,
    string BarcodeValue,
    Guid TransportOrderId,
    string OrderNumber);

public record TripPackageStopChecklistDto(
    Guid StopId,
    Guid TransportOrderId,
    string StopType,
    int Sequence,
    string? LocationName,
    string? City,
    IReadOnlyList<TripPackageChecklistItemDto> Packages);

public record TripPackageChecklistDto(
    Guid TripId,
    IReadOnlyList<TripPackageStopChecklistDto> Stops);

/// <summary>
/// Load completeness for departure: only mandatory, non-cancelled packages gate the trip.
/// MissingPackages lists every mandatory package that is not on the vehicle, by state.
/// </summary>
public record TripPackageReadinessDto(
    Guid TripId,
    PackageDepartureRule Rule,
    int TotalPackages,
    int MandatoryPackages,
    int LoadedCount,
    int NotLoadedCount,
    int MissingCount,
    int DamagedCount,
    int OpenExceptionCount,
    bool IsComplete,
    bool RequiresOverride,
    bool IsBlocked,
    IReadOnlyList<TripPackageChecklistItemDto> OutstandingPackages);

public record MarkPackageMissingRequest(Guid StopId, string? Note);

public enum PackageIncidentAction
{
    Found,
    ReleaseToLoad,
    Return,
    Quarantine,
    Cancel,
    Redeliver,
    ReturnToSender,
}

public record ResolvePackageIncidentRequest(PackageIncidentAction Action, string Note);

public enum TripPackageOutcome
{
    Success,
    NotFound,
    NotYourTrip,
    InvalidState,
    ValidationFailed,
}

public record TripPackageOperationResult(
    TripPackageOutcome Outcome, object? Payload = null, string? Error = null)
{
    public static TripPackageOperationResult Success(object? payload = null) => new(TripPackageOutcome.Success, payload);
    public static readonly TripPackageOperationResult NotFound = new(TripPackageOutcome.NotFound);
    public static readonly TripPackageOperationResult NotYourTrip = new(TripPackageOutcome.NotYourTrip, null,
        "Deze rit is niet aan jou toegewezen.");
    public static TripPackageOperationResult InvalidState(string error) => new(TripPackageOutcome.InvalidState, null, error);
    public static TripPackageOperationResult Invalid(string error) => new(TripPackageOutcome.ValidationFailed, null, error);
}
