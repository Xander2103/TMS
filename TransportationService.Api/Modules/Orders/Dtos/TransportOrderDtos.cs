using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Dtos;

public record TransportOrderListItemDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerReference,
    TransportOrderStatus Status,
    string? GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    int StopCount,
    bool AdrRequired,
    bool CraneRequired,
    OrderPriority Priority = OrderPriority.Normal);

public record TransportOrderStopDto(
    Guid Id,
    int Sequence,
    StopType StopType,
    Guid? LocationId,
    string? LocationCode,
    string LocationName,
    string? Address,
    string? PostalCode,
    string? City,
    string? CountryCode,
    DateTime? PlannedFrom,
    DateTime? PlannedTo,
    string? Reference,
    string? Instructions,
    DateTime? RequestedFrom = null,
    DateTime? RequestedTo = null,
    DateTime? ConfirmedFrom = null,
    DateTime? ConfirmedTo = null,
    DateTime? EarliestAllowed = null,
    DateTime? LatestAllowed = null,
    bool AppointmentRequired = false,
    string? AppointmentReference = null,
    string? AccessInstructions = null,
    string? LoadingInstructions = null,
    string? UnloadingInstructions = null);

public record TransportOrderDetailDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerReference,
    TransportOrderStatus Status,
    string? GoodsDescription,
    decimal? Quantity,
    string? QuantityUnit,
    decimal? WeightKg,
    decimal? VolumeM3,
    int? PalletCount,
    bool AdrRequired,
    bool CraneRequired,
    decimal? AgreedPrice,
    string? Notes,
    string? CancellationReason,
    IReadOnlyList<TransportOrderStopDto> Stops,
    IReadOnlyList<CargoItemDto> CargoItems,
    IReadOnlyList<TransportOrderStatus> AllowedTransitions,
    /// <summary>Whether the order can currently be cancelled (separate action, orders.cancel).</summary>
    bool CanCancel,
    /// <summary>Backward corrections available via the controlled correction flow (orders.correct_status).</summary>
    IReadOnlyList<TransportOrderStatus> AllowedCorrections,
    OrderPriority Priority = OrderPriority.Normal);

/// <summary>Body for the dedicated cancel action; the reason is mandatory and audited.</summary>
public record CancelTransportOrderRequest(string Reason);

/// <summary>Body for the controlled status-correction action; the reason is mandatory and audited.</summary>
public record CorrectTransportOrderStatusRequest(TransportOrderStatus TargetStatus, string Reason);

/// <summary>One entry of the chronological order timeline (audit + status + packages + stops + invoicing).</summary>
public record OrderTimelineEventDto(
    DateTime Timestamp,
    string Category,
    string Title,
    string? Detail,
    string? UserName);

public record CargoItemDto(
    Guid Id,
    int Sequence,
    string Description,
    string? Barcode,
    decimal ExpectedQuantity,
    string? QuantityUnit,
    string? Notes,
    TransportationService.Api.Modules.Packages.Entities.PackageUnitType? UnitType = null,
    string? UnitTypeLabel = null,
    decimal? TotalWeightKg = null,
    decimal? WeightPerUnitKg = null,
    decimal? LengthMeters = null,
    decimal? WidthMeters = null,
    decimal? HeightMeters = null,
    decimal? VolumeM3 = null,
    bool VolumeIsManual = false,
    bool AdrRequired = false,
    string? AdrDetails = null,
    bool Stackable = true,
    string? Reference = null,
    Guid? LoadingStopId = null,
    Guid? UnloadingStopId = null);

/// <summary>
/// Stop links use INDEXES into the request's stop list (stops receive fresh ids on every
/// save). Omitted indexes auto-link when the order has exactly one loading and one
/// unloading stop; otherwise the line stays unlinked until assigned.
/// </summary>
public record CargoItemInput(
    string Description,
    string? Barcode,
    decimal ExpectedQuantity,
    string? QuantityUnit,
    string? Notes,
    TransportationService.Api.Modules.Packages.Entities.PackageUnitType? UnitType = null,
    string? UnitTypeLabel = null,
    decimal? TotalWeightKg = null,
    decimal? WeightPerUnitKg = null,
    decimal? LengthMeters = null,
    decimal? WidthMeters = null,
    decimal? HeightMeters = null,
    decimal? VolumeM3 = null,
    bool VolumeIsManual = false,
    bool AdrRequired = false,
    string? AdrDetails = null,
    bool Stackable = true,
    string? Reference = null,
    int? LoadingStopIndex = null,
    int? UnloadingStopIndex = null);

public record TransportOrderStopInput(
    StopType StopType,
    Guid? LocationId,
    string? LocationName,
    string? Address,
    string? PostalCode,
    string? City,
    string? CountryCode,
    DateTime? PlannedFrom,
    DateTime? PlannedTo,
    string? Reference,
    string? Instructions,
    DateTime? RequestedFrom = null,
    DateTime? RequestedTo = null,
    DateTime? ConfirmedFrom = null,
    DateTime? ConfirmedTo = null,
    DateTime? EarliestAllowed = null,
    DateTime? LatestAllowed = null,
    bool AppointmentRequired = false,
    string? AppointmentReference = null,
    string? AccessInstructions = null,
    string? LoadingInstructions = null,
    string? UnloadingInstructions = null);

/// <summary>
/// Dispatcher-side execution planning of one stop: the confirmed window, hard bounds,
/// appointment and instructions. Editable while the order is not in a final status, so the
/// window can still be confirmed after planning locked the rest of the order.
/// </summary>
public record UpdateStopExecutionPlanRequest(
    DateTime? ConfirmedFrom,
    DateTime? ConfirmedTo,
    DateTime? EarliestAllowed,
    DateTime? LatestAllowed,
    bool AppointmentRequired,
    string? AppointmentReference,
    string? AccessInstructions,
    string? LoadingInstructions,
    string? UnloadingInstructions);

public record CreateTransportOrderRequest(
    Guid CustomerId,
    string? CustomerReference,
    DateOnly? OrderDate,
    string? GoodsDescription,
    decimal? Quantity,
    string? QuantityUnit,
    decimal? WeightKg,
    decimal? VolumeM3,
    int? PalletCount,
    bool AdrRequired,
    bool CraneRequired,
    decimal? AgreedPrice,
    string? Notes,
    IReadOnlyList<TransportOrderStopInput> Stops,
    IReadOnlyList<CargoItemInput>? CargoItems = null,
    OrderPriority? Priority = null);

public record UpdateTransportOrderRequest(
    Guid CustomerId,
    string? CustomerReference,
    DateOnly? OrderDate,
    string? GoodsDescription,
    decimal? Quantity,
    string? QuantityUnit,
    decimal? WeightKg,
    decimal? VolumeM3,
    int? PalletCount,
    bool AdrRequired,
    bool CraneRequired,
    decimal? AgreedPrice,
    string? Notes,
    IReadOnlyList<TransportOrderStopInput> Stops,
    IReadOnlyList<CargoItemInput>? CargoItems = null,
    OrderPriority? Priority = null);

public record ChangeTransportOrderStatusRequest(TransportOrderStatus Status);

/// <summary>Inline edit of the operational priority; validated and audited server-side.</summary>
public record ChangeOrderPriorityRequest(OrderPriority Priority);

public enum TransportOrderOperationOutcome
{
    Success,
    NotFound,
    InvalidReference,
    InvalidState,
    ValidationFailed,
}

public record TransportOrderOperationResult(
    TransportOrderOperationOutcome Outcome, TransportOrderDetailDto? Order, string? Error = null)
{
    public static TransportOrderOperationResult Success(TransportOrderDetailDto order) => new(TransportOrderOperationOutcome.Success, order);
    public static readonly TransportOrderOperationResult NotFound = new(TransportOrderOperationOutcome.NotFound, null);
    public static TransportOrderOperationResult InvalidReference(string error) => new(TransportOrderOperationOutcome.InvalidReference, null, error);
    public static TransportOrderOperationResult InvalidState(string error) => new(TransportOrderOperationOutcome.InvalidState, null, error);
    public static TransportOrderOperationResult Invalid(string error) => new(TransportOrderOperationOutcome.ValidationFailed, null, error);
}
