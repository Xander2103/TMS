using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.CustomerPortal.Dtos;

public record PortalContextDto(Guid CustomerId, string CustomerName, string? PreferredLanguage = null);

public record PortalOrderListItemDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    TransportOrderStatus Status,
    string? CustomerReference,
    string? GoodsDescription,
    string? FirstLoadingCity,
    string? LastUnloadingCity);

public record PortalStopDto(
    int Sequence,
    StopType StopType,
    string LocationName,
    string? City,
    DateTime? RequestedFrom,
    DateTime? RequestedTo,
    string? Reference,
    string? Instructions);

public record PortalCargoDto(
    int Sequence,
    string Description,
    decimal ExpectedQuantity,
    string? QuantityUnit,
    PackageUnitType? UnitType,
    bool AdrRequired);

public record PortalTimelineEventDto(TransportOrderStatus Status, DateTime ChangedAt, string? Reason);

public record PortalExceptionDto(string Type, string Description, string Status, DateTime OccurredAt);

/// <summary>Customer-facing order view: deliberately NO prices or internal planning data.</summary>
public record PortalOrderDetailDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    TransportOrderStatus Status,
    string? CustomerReference,
    string? GoodsDescription,
    string? Notes,
    string? CancellationReason,
    IReadOnlyList<PortalStopDto> Stops,
    IReadOnlyList<PortalCargoDto> CargoItems,
    IReadOnlyList<PortalTimelineEventDto> Timeline,
    IReadOnlyList<PortalExceptionDto> Exceptions);

public record PortalStopInput(
    StopType StopType,
    Guid? LocationId,
    string? LocationName,
    string? Address,
    string? PostalCode,
    string? City,
    string? CountryCode,
    DateTime? RequestedFrom,
    DateTime? RequestedTo,
    string? Reference,
    string? Instructions);

public record PortalCargoInput(
    string Description,
    decimal ExpectedQuantity,
    string? QuantityUnit,
    PackageUnitType? UnitType = null,
    decimal? TotalWeightKg = null,
    bool AdrRequired = false,
    string? AdrDetails = null);

public record PortalCreateOrderRequest(
    string? CustomerReference,
    DateOnly? OrderDate,
    string? GoodsDescription,
    string? Remarks,
    IReadOnlyList<PortalStopInput> Stops,
    IReadOnlyList<PortalCargoInput>? CargoItems = null);

public record PortalLocationDto(
    Guid Id,
    string Name,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    bool IsDefaultLoadingLocation,
    bool IsDefaultUnloadingLocation);

public record PortalCreateLocationRequest(
    string Name,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode);

public enum PortalOutcomeKind
{
    Success,
    NoCustomerLink,
    NotFound,
    ValidationFailed,
}

public record PortalResult<T>(PortalOutcomeKind Outcome, T? Value, string? Error = null) where T : class
{
    public static PortalResult<T> Success(T value) => new(PortalOutcomeKind.Success, value);
    public static PortalResult<T> NoCustomerLink() => new(PortalOutcomeKind.NoCustomerLink, null,
        "Deze gebruiker is niet aan een klant gekoppeld; neem contact op met de beheerder.");
    public static PortalResult<T> NotFound() => new(PortalOutcomeKind.NotFound, null);
    public static PortalResult<T> Invalid(string error) => new(PortalOutcomeKind.ValidationFailed, null, error);
}
