using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Dtos;

// --- Zones ---

public record PricingZoneAreaDto(Guid Id, string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record PricingZoneDto(Guid Id, string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<PricingZoneAreaDto> Areas);

public record SavePricingZoneAreaRequest(string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record SavePricingZoneRequest(string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<SavePricingZoneAreaRequest> Areas);

// --- Pricing agreements (rate cards) ---

public record PricingAgreementSurchargeDto(Guid Id, string Name, SurchargeKind Kind, decimal Value);

public record PricingAgreementDto(
    Guid Id, Guid? CustomerId, string? CustomerName, string Name, string Currency,
    DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? MinimumAmount, string? Notes, IReadOnlyList<PricingAgreementSurchargeDto> Surcharges,
    /// <summary>True = reusable rate table (never applies directly; see assignments).</summary>
    bool IsShared = false,
    /// <summary>Cap on the agreement subtotal per order, applied after the minimum.</summary>
    decimal? MaximumAmount = null,
    /// <summary>Count of customer assignments active today (0 for non-shared agreements).</summary>
    int CustomerCount = 0,
    /// <summary>Names of the customers currently assigned; populated on the list endpoint.</summary>
    IReadOnlyList<string>? CustomerNames = null);

public record SavePricingAgreementSurchargeRequest(string Name, SurchargeKind Kind, decimal Value);

public record SavePricingAgreementRequest(
    Guid? CustomerId, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? MinimumAmount, string? Notes, IReadOnlyList<SavePricingAgreementSurchargeRequest>? Surcharges,
    bool IsShared = false, decimal? MaximumAmount = null);

// --- Pricing agreement assignments (shared tables → customers) ---

public record PricingAgreementAssignmentDto(
    Guid Id, Guid CustomerId, string CustomerName,
    decimal? PercentAdjustment, decimal? FixedAdjustment,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, string? Notes);

public record SavePricingAssignmentRequest(
    Guid CustomerId, decimal? PercentAdjustment, decimal? FixedAdjustment,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, string? Notes);

// --- Price rules ---

public record PriceRuleBracketDto(Guid Id, decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit);

public record PriceRuleDto(
    Guid Id, Guid? CustomerId, string? CustomerName, Guid? UnitTypeId, string? UnitTypeName,
    PriceRuleBasis Basis, Guid? ZoneId, string? ZoneName,
    string Name, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<PriceRuleBracketDto> Brackets,
    Guid? AgreementId = null, string? AgreementName = null, int Priority = 0, decimal? BaseAmount = null,
    decimal? OversizeLengthCm = null, decimal? OversizeWidthCm = null, decimal? OversizeBillableFactor = null,
    decimal? MinimumQuantity = null, decimal? QuantityRoundingStep = null);

public record SavePriceRuleBracketRequest(decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit);

public record SavePriceRuleRequest(
    Guid? CustomerId, Guid? UnitTypeId, PriceRuleBasis Basis, Guid? ZoneId,
    string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<SavePriceRuleBracketRequest>? Brackets,
    Guid? AgreementId = null, int Priority = 0, decimal? BaseAmount = null,
    decimal? OversizeLengthCm = null, decimal? OversizeWidthCm = null, decimal? OversizeBillableFactor = null,
    decimal? MinimumQuantity = null, decimal? QuantityRoundingStep = null);

// --- Service options ---

public record ServiceOptionDto(
    Guid Id, string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true);

public record SaveServiceOptionRequest(
    string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true);

// --- Customer pricing configuration ---

public record CustomerPreferredUnitDto(
    Guid UnitTypeId, string Code, string Name, int SortOrder,
    string? CustomerLabel, string? EdiCode, string? ExcelCode, bool IsFavourite);

/// <summary>
/// One service as the customer sees it: the global default, the optional override, the
/// resulting effective value and where it came from ("Klanttarief" / "Algemene standaard").
/// </summary>
public record CustomerServiceOptionPriceDto(
    Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal DefaultValue, decimal? CustomerValue,
    bool Disabled = false, decimal? MinimumAmount = null, string? InvoiceDescription = null,
    DateOnly? EffectiveFrom = null, DateOnly? EffectiveUntil = null,
    decimal EffectiveValue = 0, string Source = "Algemene standaard");

public record CustomerPricingConfigDto(
    IReadOnlyList<CustomerPreferredUnitDto> PreferredUnits,
    IReadOnlyList<CustomerServiceOptionPriceDto> ServiceOptions);

/// <summary>One configured customer unit; refers to the global unit, never copies it.</summary>
public record SaveCustomerUnitRequest(
    Guid UnitTypeId, int SortOrder, string? CustomerLabel, string? EdiCode, string? ExcelCode, bool IsFavourite);

public record SaveCustomerPricingConfigRequest(
    IReadOnlyList<SaveCustomerUnitRequest> Units,
    IReadOnlyList<SaveCustomerOptionPriceRequest> OptionPrices);

public record SaveCustomerOptionPriceRequest(
    Guid ServiceOptionId, decimal? Value,
    bool Disabled = false, decimal? MinimumAmount = null, string? InvoiceDescription = null,
    DateOnly? EffectiveFrom = null, DateOnly? EffectiveUntil = null);

// --- Scheduled price adjustments ---

public record PriceAdjustmentValueChange(string Field, decimal OldValue, decimal NewValue);

public record PriceAdjustmentRulePreview(
    Guid PriceRuleId, string RuleName, DateOnly EffectiveFrom, DateOnly? EffectiveUntil,
    IReadOnlyList<PriceAdjustmentValueChange> Changes);

/// <summary>Null RuleIds = all adjustable active rules of the customer.</summary>
public record PreviewPriceAdjustmentRequest(DateOnly EffectiveDate, decimal Percent, IReadOnlyList<Guid>? RuleIds);

public record CreatePriceAdjustmentRequest(DateOnly EffectiveDate, decimal Percent, IReadOnlyList<Guid>? RuleIds, string? Reason);

public record ScheduledPriceAdjustmentDto(
    Guid Id, Guid CustomerId, DateOnly EffectiveDate, decimal Percent,
    string Status, string? Reason, int RuleCount, DateTime CreatedAt);

// --- Calculation ---

/// <summary>Physical detail of part of a line (from cargo items) used for billable-quantity rules.</summary>
public record PriceCalculationLineDetail(decimal Quantity, decimal? LengthCm, decimal? WidthCm);

public record PriceCalculationLineInput(
    Guid UnitTypeId, decimal Quantity, IReadOnlyList<PriceCalculationLineDetail>? Details = null);

/// <summary>A selected service with its entered quantity (hours / stops) where applicable.</summary>
public record PriceServiceInput(Guid ServiceOptionId, decimal? Quantity = null);

public record PriceCalculationRequest(
    Guid CustomerId,
    DateOnly Date,
    IReadOnlyList<PriceCalculationLineInput> Lines,
    string? DeliveryCountryCode,
    string? DeliveryPostalCode,
    decimal? WeightKg,
    decimal? DistanceKm,
    int? PalletCount,
    IReadOnlyList<Guid> ServiceOptionIds,
    /// <summary>Preferred over ServiceOptionIds when present: selections incl. quantities.</summary>
    IReadOnlyList<PriceServiceInput>? Services = null,
    decimal? VolumeM3 = null,
    decimal? LoadingMeters = null,
    int? StopCount = null);

public record PriceBreakdownLine(
    string Label, decimal Amount, string Source, bool Informational = false,
    Guid? RuleId = null, string? RuleName = null,
    Guid? AgreementId = null, string? AgreementName = null,
    decimal? ActualQuantity = null, decimal? BillableQuantity = null);

/// <summary>A selected service option with its resolved (customer or default) price.</summary>
public record PriceServiceLine(
    Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal Value, decimal Amount,
    decimal? Quantity = null, string? InvoiceLabel = null, string Source = "Algemene standaard");

public record PriceCalculationResult(
    IReadOnlyList<PriceBreakdownLine> Lines,
    /// <summary>Calculated total EXCLUDING informational lines (diesel is added at invoicing).</summary>
    decimal Total,
    decimal TotalWithInformational,
    string Currency,
    string? ZoneCode,
    string? ZoneName,
    bool RequiresManualPrice,
    IReadOnlyList<PriceServiceLine> ServiceLines,
    /// <summary>The date the tariff was resolved for (normally the order date).</summary>
    DateOnly? TariffDate = null,
    /// <summary>Blocking configuration problem (e.g. two equally specific rules). Fix the tariffs.</summary>
    string? ConfigurationError = null,
    /// <summary>Diagnostic context shown when no valid tariff was found.</summary>
    IReadOnlyList<string>? Diagnostics = null);
