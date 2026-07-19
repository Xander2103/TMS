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
    string GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity,
    int StopCount,
    bool AdrRequired,
    bool CraneRequired);

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
    string? Instructions);

public record TransportOrderDetailDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    Guid CustomerId,
    string CustomerName,
    string? CustomerReference,
    TransportOrderStatus Status,
    string GoodsDescription,
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
    IReadOnlyList<TransportOrderStatus> AllowedTransitions,
    /// <summary>Whether the order can currently be cancelled (separate action, orders.cancel).</summary>
    bool CanCancel);

/// <summary>Body for the dedicated cancel action; the reason is mandatory and audited.</summary>
public record CancelTransportOrderRequest(string Reason);

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
    string? Instructions);

public record CreateTransportOrderRequest(
    Guid CustomerId,
    string? CustomerReference,
    DateOnly? OrderDate,
    string GoodsDescription,
    decimal? Quantity,
    string? QuantityUnit,
    decimal? WeightKg,
    decimal? VolumeM3,
    int? PalletCount,
    bool AdrRequired,
    bool CraneRequired,
    decimal? AgreedPrice,
    string? Notes,
    IReadOnlyList<TransportOrderStopInput> Stops);

public record UpdateTransportOrderRequest(
    Guid CustomerId,
    string? CustomerReference,
    DateOnly? OrderDate,
    string GoodsDescription,
    decimal? Quantity,
    string? QuantityUnit,
    decimal? WeightKg,
    decimal? VolumeM3,
    int? PalletCount,
    bool AdrRequired,
    bool CraneRequired,
    decimal? AgreedPrice,
    string? Notes,
    IReadOnlyList<TransportOrderStopInput> Stops);

public record ChangeTransportOrderStatusRequest(TransportOrderStatus Status);

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
