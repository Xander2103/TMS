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
    OrderPriority Priority = OrderPriority.Normal,
    bool DieselSurchargeOverride = false,
    decimal? DieselSurchargePercentOverride = null,
    string? DieselSurchargeOverrideReason = null,
    Guid? LegalEntityId = null,
    string? QuantityUnitCode = null,
    decimal? CalculatedPrice = null,
    bool PriceIsManual = false,
    string? PriceOverrideReason = null,
    IReadOnlyList<OrderPricingLineDto>? PricingLines = null,
    IReadOnlyList<OrderServiceLineDto>? ServiceLines = null,
    OrderPricingSnapshotDto? PricingSnapshot = null,
    /// <summary>Contract (default) or OneOff (spec Phase 6): this order carries its own price agreement.</summary>
    OrderPricingSource PricingSource = OrderPricingSource.Contract,
    decimal? OneOffFixedAmount = null,
    int? OneOffIncludedLoadingMinutes = null,
    int? OneOffIncludedUnloadingMinutes = null,
    int? OneOffIncludedCombinedMinutes = null,
    decimal? OneOffExtraHourlyRate = null,
    string? OneOffNotes = null,
    /// <summary>CalculatedPrice + proposed (unconfirmed) extra-time charges; null when nothing could be calculated.</summary>
    decimal? TotalWithProposed = null,
    /// <summary>
    /// Task 10: order-level overrides of the engaged contract agreement's included loading/
    /// unloading time and extra-time rate/rounding/minimum. Contract pricing only.
    /// </summary>
    int? IncludedLoadingMinutesOverride = null,
    int? IncludedUnloadingMinutesOverride = null,
    decimal? ExtraTimeHourlyRateOverride = null,
    int? ExtraTimeRoundingStepMinutes = null,
    int? ExtraTimeMinimumBillableMinutes = null);

/// <summary>Snapshot line of the price calculation stored on the order.</summary>
public record OrderPricingLineDto(
    string Label, decimal Amount, string Source, bool Informational,
    string? RuleName = null, string? AgreementName = null,
    decimal? ActualQuantity = null, decimal? BillableQuantity = null,
    /// <summary>An unconfirmed extra-time charge (spec Phase 6): excluded from AgreedPrice, shown as a proposal.</summary>
    bool Proposed = false,
    /// <summary>Persisted line id; needed to target the confirm endpoint on a VOORSTEL line.</summary>
    Guid? Id = null,
    /// <summary>Manual-editing lifecycle (spec ch. 24-26): Auto/AutoAdjusted/Manual/Proposed.</summary>
    OrderPriceLineKind Kind = OrderPriceLineKind.Auto,
    decimal? Quantity = null, decimal? UnitPrice = null,
    decimal? OriginalQuantity = null, decimal? OriginalUnitPrice = null, decimal? OriginalAmount = null,
    string? AdjustReason = null,
    string? LineKey = null,
    /// <summary>Managed unit code for Quantity (e.g. "COLLI"), editable on manual lines (spec Task 5).</summary>
    string? Unit = null,
    /// <summary>Frozen identity of the service option, for merge-matching (see LineKey too).</summary>
    Guid? ServiceOptionId = null);

/// <summary>Frozen header of the order's pricing snapshot (spec ch. 21).</summary>
public record OrderPricingSnapshotDto(
    DateOnly TariffDate, string Currency, string? ZoneCode, string? ZoneName,
    string? AgreementNames, string? UnitSummary, decimal? CalculatedTotal,
    decimal? OverrideAmount, string? OverrideReason, Guid? OverriddenByUserId, DateTime? OverriddenAtUtc,
    string? Explanation,
    /// <summary>Draft → Reviewed → Locked → Invoiced (spec ch. 24-26); preserved across recalculations.</summary>
    OrderPricingStatus Status = OrderPricingStatus.Draft,
    /// <summary>Sum of Auto/AutoAdjusted/Manual non-informational line amounts.</summary>
    decimal? LinesTotal = null,
    /// <summary>Wave 2026-08-04 §7: per-goods-line pricing coverage frozen with the calculation.</summary>
    IReadOnlyList<OrderPricingCoverageDto>? Coverage = null,
    /// <summary>Wave 2026-08-04 §8: confirmation metadata ("Bevestigd op … door …").</summary>
    DateTime? ConfirmedAtUtc = null,
    Guid? ConfirmedByUserId = null,
    string? ConfirmedByName = null,
    /// <summary>Non-null: the price was confirmed DESPITE unpriced goods, with this reason (visible warning).</summary>
    string? ConfirmedWithUnpricedGoodsReason = null);

/// <summary>
/// Pricing coverage of one commercial goods line/unit (wave 2026-08-04 §7). Status: "Full"
/// (base tariff prices it), "Partial" (services only — never counts as transport pricing),
/// "None" (nothing prices it; see Reason).
/// </summary>
public record OrderPricingCoverageDto(
    Guid? UnitTypeId, string UnitLabel, decimal Quantity, string Status,
    decimal BaseAmount = 0m, string? BaseRuleName = null,
    decimal ServicesAmount = 0m, string? Reason = null);

/// <summary>
/// One line-level manual correction/addition (spec ch. 24-26). LineKey null = free manual line;
/// otherwise targets an existing Auto/AutoAdjusted/Manual line by its stable merge key.
/// Remove keeps the row for audit (Auto/AutoAdjusted → Amount 0) except a Manual line, which is
/// hard-deleted.
/// </summary>
public record SaveOrderPriceLineRequest(
    string? LineKey, string Label, decimal? Quantity, decimal? UnitPrice, decimal? Amount,
    string? AdjustReason, bool Remove = false,
    /// <summary>Managed unit code for Quantity (e.g. "COLLI"); normalized like <c>QuantityUnitCode</c>.</summary>
    string? Unit = null);

/// <summary>Body for the pricing status transition endpoint (spec ch. 24-26).</summary>
public record SetOrderPricingStatusRequest(OrderPricingStatus Status);

/// <summary>Selected delivery service/supplement snapshotted on the order.</summary>
public record OrderServiceLineDto(
    Guid? ServiceOptionId, string Name,
    TransportationService.Api.Modules.Tarification.Entities.SurchargeKind Kind,
    decimal Value, decimal Amount, decimal? Quantity = null,
    decimal? PalletCount = null, decimal? DayCount = null, string? Note = null);

/// <summary>
/// A selected service with the entered quantity where applicable (hours / stops / days /
/// pallet-days). For per-pallet-day services the pallet and day inputs are persisted and the
/// billable quantity defaults to pallets × days; an explicitly sent Quantity is a manual
/// correction and always wins. For per-day services a lone DayCount doubles as the quantity.
/// </summary>
public record OrderServiceInput(
    Guid ServiceOptionId, decimal? Quantity = null, decimal? PalletCount = null, decimal? DayCount = null,
    string? Note = null);

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
    string? Description,
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
    Guid? UnloadingStopId = null,
    string? QuantityUnitCode = null,
    decimal? PalletCount = null);

/// <summary>
/// Stop links use INDEXES into the request's stop list (stops receive fresh ids on every
/// save). Omitted indexes auto-link when the order has exactly one loading and one
/// unloading stop; otherwise the line stays unlinked until assigned.
/// </summary>
public record CargoItemInput(
    string? Description,
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
    int? UnloadingStopIndex = null,
    string? QuantityUnitCode = null,
    // Id-preserving update sync: null/unmatched Id => treated as a new line. Ignored on create.
    Guid? Id = null,
    decimal? PalletCount = null);

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
    OrderPriority? Priority = null,
    bool DieselSurchargeOverride = false,
    decimal? DieselSurchargePercentOverride = null,
    string? DieselSurchargeOverrideReason = null,
    Guid? LegalEntityId = null,
    string? QuantityUnitCode = null,
    IReadOnlyList<Guid>? ServiceOptionIds = null,
    bool PriceIsManual = false,
    string? PriceOverrideReason = null,
    /// <summary>Preferred over ServiceOptionIds when present: selections incl. quantities.</summary>
    IReadOnlyList<OrderServiceInput>? Services = null,
    /// <summary>Contract (default) or OneOff (spec Phase 6): this order carries its own price agreement.</summary>
    OrderPricingSource PricingSource = OrderPricingSource.Contract,
    decimal? OneOffFixedAmount = null,
    int? OneOffIncludedLoadingMinutes = null,
    int? OneOffIncludedUnloadingMinutes = null,
    int? OneOffIncludedCombinedMinutes = null,
    decimal? OneOffExtraHourlyRate = null,
    string? OneOffNotes = null,
    /// <summary>
    /// Task 10: order-level overrides of the engaged contract agreement's included loading/
    /// unloading time and extra-time rate/rounding/minimum. Contract pricing only — rejected in
    /// combination with PricingSource == OneOff.
    /// </summary>
    int? IncludedLoadingMinutesOverride = null,
    int? IncludedUnloadingMinutesOverride = null,
    decimal? ExtraTimeHourlyRateOverride = null,
    int? ExtraTimeRoundingStepMinutes = null,
    int? ExtraTimeMinimumBillableMinutes = null);

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
    OrderPriority? Priority = null,
    bool DieselSurchargeOverride = false,
    decimal? DieselSurchargePercentOverride = null,
    string? DieselSurchargeOverrideReason = null,
    Guid? LegalEntityId = null,
    string? QuantityUnitCode = null,
    IReadOnlyList<Guid>? ServiceOptionIds = null,
    bool PriceIsManual = false,
    string? PriceOverrideReason = null,
    /// <summary>Preferred over ServiceOptionIds when present: selections incl. quantities.</summary>
    IReadOnlyList<OrderServiceInput>? Services = null,
    /// <summary>Contract (default) or OneOff (spec Phase 6): this order carries its own price agreement.</summary>
    OrderPricingSource PricingSource = OrderPricingSource.Contract,
    decimal? OneOffFixedAmount = null,
    int? OneOffIncludedLoadingMinutes = null,
    int? OneOffIncludedUnloadingMinutes = null,
    int? OneOffIncludedCombinedMinutes = null,
    decimal? OneOffExtraHourlyRate = null,
    string? OneOffNotes = null,
    /// <summary>
    /// Task 10: order-level overrides of the engaged contract agreement's included loading/
    /// unloading time and extra-time rate/rounding/minimum. Contract pricing only — rejected in
    /// combination with PricingSource == OneOff.
    /// </summary>
    int? IncludedLoadingMinutesOverride = null,
    int? IncludedUnloadingMinutesOverride = null,
    decimal? ExtraTimeHourlyRateOverride = null,
    int? ExtraTimeRoundingStepMinutes = null,
    int? ExtraTimeMinimumBillableMinutes = null);

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
