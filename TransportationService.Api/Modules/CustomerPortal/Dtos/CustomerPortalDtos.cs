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

/// <summary>
/// H-14: the timeline shows WHEN a status was reached, never WHY. The history's Reason column
/// carries planner-typed cancel/correction motivations ("dispatch boekte verkeerd", "klant
/// betaalt niet") and is internal by classification.
/// </summary>
public record PortalTimelineEventDto(TransportOrderStatus Status, DateTime ChangedAt);

public record PortalExceptionDto(string Type, string Description, string Status, DateTime OccurredAt);

/// <summary>
/// Customer-facing order view: deliberately NO prices or internal planning data.
/// H-14: <c>Notes</c> (the planners' free-text field on the order) and <c>CancellationReason</c>
/// are staff-only and are NOT projected here. The portal's intake still writes the customer's own
/// remarks into <c>TransportOrder.Notes</c>, but a planner edits that same field afterwards, so the
/// column cannot be echoed back safely; a customer-facing exception or portal message is the
/// supported way to tell the customer something.
/// </summary>
public record PortalOrderDetailDto(
    Guid Id,
    string OrderNumber,
    DateOnly OrderDate,
    TransportOrderStatus Status,
    string? CustomerReference,
    string? GoodsDescription,
    IReadOnlyList<PortalStopDto> Stops,
    IReadOnlyList<PortalCargoDto> CargoItems,
    IReadOnlyList<PortalTimelineEventDto> Timeline,
    IReadOnlyList<PortalExceptionDto> Exceptions,
    /// <summary>Wave 8: expected arrival at the (last) delivery stop — the tracking answer.</summary>
    DateTime? ExpectedDeliveryEta = null,
    /// <summary>Wave 11: delivery proof summary once a current POD exists.</summary>
    PortalPodSummaryDto? Pod = null);

/// <summary>Wave 11: what the customer may see of the proof of delivery (data, not files).</summary>
public record PortalPodSummaryDto(DateTime DeliveredAt, string RecipientName, string? Outcome);

/// <summary>Wave 11: the customer's own notification preferences (MessagingProfile surface).</summary>
public record PortalNotificationPreferencesDto(
    bool EmailEnabled,
    bool SmsEnabled,
    string? PreferredLanguage,
    /// <summary>Enabled customer-facing kinds; null = all.</summary>
    IReadOnlyList<string>? EnabledKinds,
    IReadOnlyList<string> AvailableKinds);

public record SavePortalNotificationPreferencesRequest(
    bool EmailEnabled,
    bool SmsEnabled,
    string? PreferredLanguage,
    IReadOnlyList<string>? EnabledKinds);

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
