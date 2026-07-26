using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Dtos;

// --- Zones ---

public record PricingZoneAreaDto(Guid Id, string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record PricingZoneDto(Guid Id, string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<PricingZoneAreaDto> Areas);

public record SavePricingZoneAreaRequest(string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record SavePricingZoneRequest(string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<SavePricingZoneAreaRequest> Areas);

// --- Pricing agreements (rate cards) ---

public record PricingAgreementSurchargeDto(Guid Id, string Name, SurchargeKind Kind, decimal Value);

/// <summary>One stacking step of a derived agreement (spec §9: "NL = BE +30%").</summary>
public record PricingAgreementModifierDto(
    Guid Id, int Sequence, string Name, string? CountryCode, Guid? ZoneId, string? ZoneName,
    decimal? Percent, decimal? FixedAmount);

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
    IReadOnlyList<string>? CustomerNames = null,
    /// <summary>Set => this is a derived table; it reuses the base-chain root's rules.</summary>
    Guid? BaseAgreementId = null,
    string? BaseAgreementName = null,
    IReadOnlyList<PricingAgreementModifierDto>? Modifiers = null);

public record SavePricingAgreementSurchargeRequest(string Name, SurchargeKind Kind, decimal Value);

public record SavePricingAgreementModifierRequest(
    int Sequence, string Name, string? CountryCode, Guid? ZoneId, decimal? Percent, decimal? FixedAmount);

public record SavePricingAgreementRequest(
    Guid? CustomerId, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? MinimumAmount, string? Notes, IReadOnlyList<SavePricingAgreementSurchargeRequest>? Surcharges,
    bool IsShared = false, decimal? MaximumAmount = null,
    Guid? BaseAgreementId = null, IReadOnlyList<SavePricingAgreementModifierRequest>? Modifiers = null);

// --- Pricing agreement assignments (shared tables → customers) ---

public record PricingAgreementAssignmentDto(
    Guid Id, Guid CustomerId, string CustomerName,
    decimal? PercentAdjustment, decimal? FixedAdjustment,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, string? Notes);

public record SavePricingAssignmentRequest(
    Guid CustomerId, decimal? PercentAdjustment, decimal? FixedAdjustment,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, string? Notes);

// --- Price rules ---

public record PriceRuleBracketDto(
    Guid Id, decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit,
    /// <summary>Row matches only when the order's own weight/volume/loading-meters is known and within the cap.</summary>
    decimal? WeightToKg = null, decimal? VolumeToM3 = null, decimal? LoadingMetersTo = null);

public record PriceRuleDto(
    Guid Id, Guid? CustomerId, string? CustomerName, Guid? UnitTypeId, string? UnitTypeName,
    PriceRuleBasis Basis, Guid? ZoneId, string? ZoneName,
    string Name, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<PriceRuleBracketDto> Brackets,
    Guid? AgreementId = null, string? AgreementName = null, int Priority = 0, decimal? BaseAmount = null,
    decimal? OversizeLengthCm = null, decimal? OversizeWidthCm = null, decimal? OversizeBillableFactor = null,
    decimal? MinimumQuantity = null, decimal? QuantityRoundingStep = null,
    /// <summary>Cap on the rule amount, applied after MinimumAmount.</summary>
    decimal? MaximumAmount = null,
    /// <summary>QuantityBracket only: Absolute (bracket price) or PerNextUnit (sum per piece).</summary>
    BracketSelectionMode BracketMode = BracketSelectionMode.Absolute);

public record SavePriceRuleBracketRequest(
    decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit,
    decimal? WeightToKg = null, decimal? VolumeToM3 = null, decimal? LoadingMetersTo = null);

public record SavePriceRuleRequest(
    Guid? CustomerId, Guid? UnitTypeId, PriceRuleBasis Basis, Guid? ZoneId,
    string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<SavePriceRuleBracketRequest>? Brackets,
    Guid? AgreementId = null, int Priority = 0, decimal? BaseAmount = null,
    decimal? OversizeLengthCm = null, decimal? OversizeWidthCm = null, decimal? OversizeBillableFactor = null,
    decimal? MinimumQuantity = null, decimal? QuantityRoundingStep = null,
    decimal? MaximumAmount = null, BracketSelectionMode BracketMode = BracketSelectionMode.Absolute);

// --- Service options ---

public record ServiceOptionDto(
    Guid Id, string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true,
    /// <summary>Kind == PerUnit only: which managed unit this service counts.</summary>
    Guid? UnitTypeId = null, string? UnitTypeName = null,
    /// <summary>The engine adds this service automatically (contract service), quantified from the order.</summary>
    bool AutoApply = false,
    /// <summary>Only charged/auto-applied when the order requires ADR.</summary>
    bool OnlyForAdr = false);

public record SaveServiceOptionRequest(
    string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true,
    Guid? UnitTypeId = null, bool AutoApply = false, bool OnlyForAdr = false);

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
    decimal EffectiveValue = 0, string Source = "Algemene standaard",
    /// <summary>Override of the global AutoApply for this customer; null = inherit.</summary>
    bool? AutoApplyOverride = null,
    /// <summary>The effective auto-apply behaviour today, after applying the override.</summary>
    bool EffectiveAutoApply = false);

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
    DateOnly? EffectiveFrom = null, DateOnly? EffectiveUntil = null,
    bool? AutoApplyOverride = null);

// --- Scheduled price adjustments ---

public record PriceAdjustmentValueChange(string Field, decimal OldValue, decimal NewValue);

public record PriceAdjustmentRulePreview(
    Guid PriceRuleId, string RuleName, DateOnly EffectiveFrom, DateOnly? EffectiveUntil,
    IReadOnlyList<PriceAdjustmentValueChange> Changes);

/// <summary>
/// Null RuleIds = all adjustable active rules in scope (customer or agreement). Exactly one of
/// Percent/AmountDelta must be set. BasisFilter/UnitTypeIdFilter narrow the affected rules further.
/// </summary>
public record PreviewPriceAdjustmentRequest(
    DateOnly EffectiveDate, decimal? Percent, IReadOnlyList<Guid>? RuleIds,
    decimal? AmountDelta = null, decimal? RoundingStep = null,
    string? BasisFilter = null, Guid? UnitTypeIdFilter = null);

public record CreatePriceAdjustmentRequest(
    DateOnly EffectiveDate, decimal? Percent, IReadOnlyList<Guid>? RuleIds, string? Reason,
    decimal? AmountDelta = null, decimal? RoundingStep = null,
    string? BasisFilter = null, Guid? UnitTypeIdFilter = null);

public record ScheduledPriceAdjustmentDto(
    Guid Id, Guid? CustomerId, DateOnly EffectiveDate, decimal? Percent,
    string Status, string? Reason, int RuleCount, DateTime CreatedAt,
    Guid? AgreementId = null, decimal? AmountDelta = null, decimal? RoundingStep = null,
    string? BasisFilter = null, Guid? UnitTypeIdFilter = null);

// --- Agreement duplication (new version) ---

/// <summary>
/// Copies an agreement's rules (incl. brackets/surcharges/modifiers/BaseAgreementId) into a new
/// version with its own effective window. Assignments are deliberately NOT copied — the new
/// version must be linked to customers explicitly (see PricingAdminService.DuplicateAgreementAsync).
/// Percent XOR AmountDelta (+ optional RoundingStep) is applied to the copied rules using the same
/// math as the scheduled price adjustments.
/// </summary>
public record DuplicateAgreementRequest(
    string Name, DateOnly EffectiveFrom, bool CloseSource,
    decimal? Percent = null, decimal? AmountDelta = null, decimal? RoundingStep = null);

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
    int? StopCount = null,
    /// <summary>Whether the order requires ADR handling; drives OnlyForAdr service options.</summary>
    bool? AdrRequired = null,
    /// <summary>Number of (non-deleted) cargo/order lines; drives PerOrderLine service options. Null = unknown.</summary>
    int? CargoLineCount = null);

public record PriceBreakdownLine(
    string Label, decimal Amount, string Source, bool Informational = false,
    Guid? RuleId = null, string? RuleName = null,
    Guid? AgreementId = null, string? AgreementName = null,
    decimal? ActualQuantity = null, decimal? BillableQuantity = null);

/// <summary>A selected (or auto-applied) service option with its resolved (customer or default) price.</summary>
public record PriceServiceLine(
    Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal Value, decimal Amount,
    decimal? Quantity = null, string? InvoiceLabel = null, string Source = "Algemene standaard",
    /// <summary>True when the engine added this line automatically (contract service) — not selected by the user.</summary>
    bool AutoApplied = false);

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
