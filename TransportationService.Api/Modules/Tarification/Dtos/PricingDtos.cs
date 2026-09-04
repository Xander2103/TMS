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
    IReadOnlyList<PricingAgreementModifierDto>? Modifiers = null,
    /// <summary>Included loading/unloading time (Phase 6, contract mode). Mutually exclusive with IncludedCombinedMinutes.</summary>
    int? IncludedLoadingMinutes = null,
    int? IncludedUnloadingMinutes = null,
    /// <summary>Included loading+unloading minutes combined. Mutually exclusive with the per-activity fields.</summary>
    int? IncludedCombinedMinutes = null,
    /// <summary>Hourly rate charged for time beyond the included allowance (proposal until confirmed).</summary>
    decimal? ExtraHourlyRate = null,
    /// <summary>Wave 2: default sales code for this agreement's rules (a rule's own code wins).</summary>
    Guid? SalesCategoryId = null,
    string? SalesCategoryName = null);

public record SavePricingAgreementSurchargeRequest(string Name, SurchargeKind Kind, decimal Value);

public record SavePricingAgreementModifierRequest(
    int Sequence, string Name, string? CountryCode, Guid? ZoneId, decimal? Percent, decimal? FixedAmount);

public record SavePricingAgreementRequest(
    Guid? CustomerId, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? MinimumAmount, string? Notes, IReadOnlyList<SavePricingAgreementSurchargeRequest>? Surcharges,
    bool IsShared = false, decimal? MaximumAmount = null,
    Guid? BaseAgreementId = null, IReadOnlyList<SavePricingAgreementModifierRequest>? Modifiers = null,
    int? IncludedLoadingMinutes = null, int? IncludedUnloadingMinutes = null,
    int? IncludedCombinedMinutes = null, decimal? ExtraHourlyRate = null,
    Guid? SalesCategoryId = null);

// --- Tenant holidays (Wave 3 §4: drive Holiday time surcharges) ---

public record TenantHolidayDto(Guid Id, DateOnly Date, string Name);

public record SaveTenantHolidayRequest(DateOnly Date, string Name);

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
    BracketSelectionMode BracketMode = BracketSelectionMode.Absolute,
    /// <summary>Wave 2: sales code for lines priced by this rule (wins over the agreement's).</summary>
    Guid? SalesCategoryId = null,
    string? SalesCategoryName = null,
    /// <summary>Wave 3 §2: origin-zone dimension — set: only matches when the first loading stop lands in this zone.</summary>
    Guid? OriginZoneId = null,
    string? OriginZoneName = null,
    /// <summary>P6: activity dimension — set: only matches orders of this dossier activity type.</summary>
    Guid? ActivityTypeId = null,
    string? ActivityTypeName = null);

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
    decimal? MaximumAmount = null, BracketSelectionMode BracketMode = BracketSelectionMode.Absolute,
    Guid? SalesCategoryId = null,
    Guid? OriginZoneId = null,
    /// <summary>P6: activity dimension (null = every activity).</summary>
    Guid? ActivityTypeId = null);

// --- Bracket-row customer overrides ("klantafwijkingen") ---

public record PriceRuleBracketOverrideDto(
    Guid Id, Guid PriceRuleId, Guid CustomerId, string CustomerName,
    decimal FromQuantity, decimal? ToQuantity,
    decimal? WeightToKg, decimal? VolumeToM3, decimal? LoadingMetersTo,
    decimal Price, decimal? PricePerExtraUnit,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, string? Notes,
    /// <summary>True when no bracket row with this exact identity exists on the rule anymore.</summary>
    bool Orphaned);

public record SavePriceRuleBracketOverrideRequest(
    Guid CustomerId, decimal FromQuantity, decimal? ToQuantity,
    decimal? WeightToKg = null, decimal? VolumeToM3 = null, decimal? LoadingMetersTo = null,
    decimal Price = 0m, decimal? PricePerExtraUnit = null,
    DateOnly? EffectiveFrom = null, DateOnly? EffectiveUntil = null, string? Notes = null);

// --- Service options ---

public record ServiceOptionDto(
    Guid Id, string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true,
    /// <summary>Kind == PerUnit only: which managed unit this service counts.</summary>
    Guid? UnitTypeId = null, string? UnitTypeName = null,
    /// <summary>The engine adds this service automatically (contract service), quantified from the order.</summary>
    bool AutoApply = false,
    /// <summary>Only charged/auto-applied when the order requires ADR.</summary>
    bool OnlyForAdr = false,
    /// <summary>Warehouse condition: only charged/auto-applied when the order touches one of these warehouses. Empty = all orders.</summary>
    IReadOnlyList<Guid>? WarehouseIds = null,
    IReadOnlyList<string>? WarehouseNames = null,
    /// <summary>Wave 2026-08-04 §16: time-based stop conditions (before/after/appointment/weekend).</summary>
    IReadOnlyList<ServiceTimeConditionDto>? TimeConditions = null,
    /// <summary>Wave 2: sales code for service lines of this option (wins over rule/agreement).</summary>
    Guid? SalesCategoryId = null,
    string? SalesCategoryName = null,
    /// <summary>P7: Ordered | ScannedIn | ScannedOut | Picked | PalletDays.</summary>
    string QuantitySource = "Ordered");

/// <summary>One time-based condition row of a service option (wave 2026-08-04 §16/§17).</summary>
public record ServiceTimeConditionDto(
    ServiceConditionKind Kind,
    ServiceConditionStopScope StopScope = ServiceConditionStopScope.Any,
    /// <summary>StopTimeBefore/StopTimeAfter only: the configured threshold.</summary>
    TimeOnly? TimeOfDay = null,
    /// <summary>Competition priority among matched Before (resp. After) conditions; higher wins.</summary>
    int Priority = 0,
    /// <summary>Opt-in stacking: never competes, always applies when matched.</summary>
    bool AllowStacking = false,
    /// <summary>Kind == ActivityType only: the dossier activity type to match (P6).</summary>
    Guid? ActivityTypeId = null);

public record SaveServiceOptionRequest(
    string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder,
    string? Description = null, string? InvoiceDescription = null, bool SelectableInOrders = true,
    Guid? UnitTypeId = null, bool AutoApply = false, bool OnlyForAdr = false,
    /// <summary>Warehouse condition (OR within the list, AND with the ADR flag); null/empty = all orders.</summary>
    IReadOnlyList<Guid>? WarehouseIds = null,
    /// <summary>Wave 2026-08-04 §16: time-based stop conditions; null = leave unchanged is NOT supported — the list replaces.</summary>
    IReadOnlyList<ServiceTimeConditionDto>? TimeConditions = null,
    Guid? SalesCategoryId = null,
    /// <summary>P7: Ordered (default) | ScannedIn | ScannedOut | Picked | PalletDays.</summary>
    string QuantitySource = "Ordered");

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

/// <summary>
/// Partial by design: <see cref="Units"/> null = leave the customer's unit mapping untouched
/// (a present list is a full replace); option rows absent from <see cref="OptionPrices"/> are
/// left untouched. Independent panels save their own slice without echoing the other's state.
/// </summary>
public record SaveCustomerPricingConfigRequest(
    IReadOnlyList<SaveCustomerUnitRequest>? Units,
    IReadOnlyList<SaveCustomerOptionPriceRequest> OptionPrices);

public record SaveCustomerOptionPriceRequest(
    Guid ServiceOptionId, decimal? Value,
    bool Disabled = false, decimal? MinimumAmount = null, string? InvoiceDescription = null,
    DateOnly? EffectiveFrom = null, DateOnly? EffectiveUntil = null,
    bool? AutoApplyOverride = null);

// --- Customer tariff base (customer detail "Tarieven & toeslagen") ---

/// <summary>
/// One rate table that prices (or will price) a specific customer: the customer's own private
/// tables plus every shared table that has an assignment for that customer. Read model for the
/// customer detail's "Tariefbasis" section — replaces the client-side fan-out over
/// GET api/pricing/agreements/{id}/assignments (docs/pricing.md §11.4).
/// </summary>
public record CustomerAgreementLinkDto(
    Guid AgreementId, string Name, bool IsShared,
    DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? MinimumAmount, decimal? MaximumAmount,
    /// <summary>Set => derived table ("NL = BE +30%"); rules come from the base-chain root.</summary>
    Guid? BaseAgreementId = null, string? BaseAgreementName = null,
    /// <summary>Shared tables only: this customer's assignment (adjustment + own validity window).</summary>
    Guid? AssignmentId = null,
    decimal? AssignmentPercentAdjustment = null, decimal? AssignmentFixedAdjustment = null,
    DateOnly? AssignmentEffectiveFrom = null, DateOnly? AssignmentEffectiveUntil = null,
    /// <summary>Earliest agreement-scoped scheduled adjustment that is still planned (not yet effective).</summary>
    DateOnly? PlannedAdjustmentDate = null,
    decimal? PlannedAdjustmentPercent = null, decimal? PlannedAdjustmentAmountDelta = null);

/// <summary>
/// One bracket-row deviation ("klantafwijking") of one customer, with the rule/table context and
/// the CURRENT standard price of the targeted bracket row — a read model for the customer detail
/// ("welke staffelprijzen wijken af voor deze klant?"). Standard values are null and
/// <see cref="Orphaned"/> is true when the targeted row no longer exists on the rule (same
/// value-identity matching as the rule-scoped listing). Never a second pricing engine: the
/// effective application stays in <see cref="PricingEngine"/>.
/// </summary>
public record CustomerBracketOverrideRowDto(
    Guid Id, Guid PriceRuleId, string RuleName,
    Guid? AgreementId, string? AgreementName, string? UnitTypeName,
    decimal FromQuantity, decimal? ToQuantity,
    decimal? WeightToKg, decimal? VolumeToM3, decimal? LoadingMetersTo,
    decimal? StandardPrice, decimal? StandardPricePerExtraUnit,
    decimal Price, decimal? PricePerExtraUnit,
    DateOnly? EffectiveFrom, DateOnly? EffectiveUntil, bool Orphaned);

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
    /// <summary>LEGACY weergavetekst (Nederlands). Logica hoort op StatusCode; dit veld
    /// verdwijnt zodra alle clients zijn overgestapt (i18n-wave).</summary>
    string Status, string? Reason, int RuleCount, DateTime CreatedAt,
    Guid? AgreementId = null, decimal? AmountDelta = null, decimal? RoundingStep = null,
    string? BasisFilter = null, Guid? UnitTypeIdFilter = null,
    /// <summary>Stabiele statuscode: Planned | Active | Cancelled.</summary>
    string StatusCode = "Planned");

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

// --- Combined-unit degression discounts (spec §29-31) ---

public record CombinedUnitDiscountUnitDto(Guid Id, Guid UnitTypeId, string? UnitTypeName, decimal EquivalentFactor);

public record CombinedUnitDiscountTierDto(Guid Id, decimal FromCount, decimal? ToCount, decimal Percent);

public record CombinedUnitDiscountDto(
    Guid Id, Guid? CustomerId, string? CustomerName, Guid? AgreementId, string? AgreementName,
    string Name, DegressionScope Scope, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    IReadOnlyList<CombinedUnitDiscountUnitDto> Units, IReadOnlyList<CombinedUnitDiscountTierDto> Tiers);

public record SaveCombinedUnitDiscountUnitRequest(Guid UnitTypeId, decimal EquivalentFactor);

public record SaveCombinedUnitDiscountTierRequest(decimal FromCount, decimal? ToCount, decimal Percent);

public record SaveCombinedUnitDiscountRequest(
    Guid? CustomerId, Guid? AgreementId, string Name, DegressionScope Scope,
    DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    IReadOnlyList<SaveCombinedUnitDiscountUnitRequest> Units, IReadOnlyList<SaveCombinedUnitDiscountTierRequest> Tiers);

// --- Agreement configuration validation ("Controle") ---

/// <summary>One configuration-health finding for an agreement ("Controle"). Severity is "error"
/// (blocks/would block price calculation) or "warning" (dead/surprising configuration, still prices).</summary>
public record PricingConfigCheckDto(string Severity, string Message);

// --- Calculation ---

/// <summary>Physical detail of part of a line (from cargo items) used for billable-quantity rules.</summary>
public record PriceCalculationLineDetail(decimal Quantity, decimal? LengthCm, decimal? WidthCm);

/// <summary>One unit type's quantity within a <see cref="PriceCalculationGroup"/> (spec §29-31).</summary>
public record PriceCalculationGroupUnit(Guid UnitTypeId, decimal Quantity);

/// <summary>
/// One combinable group of units for combined-unit degression discounts — normally one per
/// unloading stop. <see cref="AddressKey"/> is a normalized identity of the delivery address
/// (location id, or "address|postalcode|city" lowercased); null means "no known address" and the
/// group never merges with another under <see cref="DegressionScope.DeliveryAddress"/>.
/// </summary>
public record PriceCalculationGroup(
    string GroupKey, string GroupLabel, IReadOnlyList<PriceCalculationGroupUnit> Units, string? AddressKey = null);

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
    int? CargoLineCount = null,
    /// <summary>
    /// Set => this order carries its own one-off price agreement (spec Phase 6): the engine
    /// skips all rule/agreement resolution and prices the fixed amount + extra-time proposals.
    /// </summary>
    OneOffPricingInput? OneOff = null,
    /// <summary>Measured loading/unloading minutes from stop executions, for included-time extra-time proposals.</summary>
    decimal? ActualLoadingMinutes = null,
    decimal? ActualUnloadingMinutes = null,
    /// <summary>Wave 3 §2: FIRST LOADING stop — resolves the origin zone for O/D-dimension rules.</summary>
    string? OriginCountryCode = null,
    string? OriginPostalCode = null,
    /// <summary>
    /// Per-stop (or otherwise combinable) unit groups for combined-unit degression discounts
    /// (spec §29-31). Null/empty => the engine falls back to one "order" group built from
    /// <see cref="Lines"/> so a caller that doesn't build groups still gets Order-scope discounts.
    /// </summary>
    IReadOnlyList<PriceCalculationGroup>? Groups = null,
    /// <summary>
    /// Warehouses the order touches (a stop at the warehouse's master location), for
    /// warehouse-conditioned service options. Null/empty = the order touches no known warehouse.
    /// </summary>
    IReadOnlyList<Guid>? WarehouseIds = null,
    /// <summary>
    /// Task 10: order-level overrides of the engaged contract agreement's included loading/
    /// unloading time and extra-time rate/rounding/minimum. Contract mode only — never set for a
    /// one-off order (see <see cref="OneOff"/>, which carries its own included-time fields).
    /// </summary>
    IncludedTimeOverrideInput? IncludedTimeOverrides = null,
    /// <summary>
    /// Wave 2026-08-04 §16: per-stop time requirements + appointment flag + planned date,
    /// feeding time-based service conditions. Null/empty = no time conditions can match.
    /// </summary>
    IReadOnlyList<StopTimeInput>? StopTimes = null,
    // P6: equipment/movement/activity pricing dimensions (null = unknown, conditions don't match).
    bool? CraneRequired = null,
    bool? PlateauRequired = null,
    bool? MoffettRequired = null,
    bool? IsReturnMovement = null,
    /// <summary>The order's linked dossier activity type; drives ActivityType-bound rules/conditions.</summary>
    Guid? ActivityTypeId = null,
    // P7: ACTUAL warehouse activity of the order's packages (null = no scans known yet) —
    // feeds services whose QuantitySource is ScannedIn/ScannedOut/Picked/PalletDays.
    decimal? ScannedInCount = null,
    decimal? ScannedOutCount = null,
    decimal? PickedCount = null,
    decimal? PalletDays = null);

/// <summary>
/// One stop's time facts for time-based service conditions (wave 2026-08-04 §16).
/// RequirementKind mirrors the order-side StopTimeRequirementKind as a string
/// ("None"/"Before"/"After"/"Window") to keep the modules decoupled.
/// </summary>
public record StopTimeInput(
    bool IsUnloading,
    string RequirementKind,
    TimeOnly? RequirementFrom,
    TimeOnly? RequirementTo,
    bool AppointmentRequired,
    DateOnly? PlannedDate);

/// <summary>A one-off order's own price agreement: no contract is consulted (spec Phase 6).</summary>
public record OneOffPricingInput(
    decimal FixedAmount, int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, int? IncludedCombinedMinutes,
    decimal? ExtraHourlyRate, string? Notes);

/// <summary>
/// Task 10: order-level overrides applied on top of the engaged contract agreement's included-time
/// configuration. Any field left null falls back to the agreement's own value (or, for
/// RoundingStepMinutes/MinimumBillableMinutes, to "no rounding"/"no minimum"). See
/// PricingEngine.ComputeExtraTimeLines for the exact resolution/rounding/minimum rules.
/// </summary>
public record IncludedTimeOverrideInput(
    int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, decimal? ExtraHourlyRate,
    int? RoundingStepMinutes, int? MinimumBillableMinutes,
    /// <summary>
    /// Wave 2026-08-04 §18: true when a STOP-level override produced the minutes (resolution
    /// stop → order → contract) — the included-time info then reports Source "Stop".
    /// </summary>
    bool FromStopOverride = false);

public record PriceBreakdownLine(
    string Label, decimal Amount, string Source, bool Informational = false,
    Guid? RuleId = null, string? RuleName = null,
    Guid? AgreementId = null, string? AgreementName = null,
    decimal? ActualQuantity = null, decimal? BillableQuantity = null,
    /// <summary>An unconfirmed extra-time charge (spec Phase 6): excluded from Total, included in TotalWithProposed.</summary>
    bool Proposed = false,
    /// <summary>
    /// Stable merge key stamped by the engine (spec ch. 24-26, single source of truth): lets the
    /// order-side merge-on-recalc match this line against a previously adjusted/persisted one
    /// instead of delete-all-rewrite. See TransportOrderService.ApplyPricingAsync.
    /// </summary>
    string? LineKey = null,
    /// <summary>Service option identity, for the "service:{id}" LineKey scheme and merge-matching.</summary>
    Guid? ServiceOptionId = null);

/// <summary>A selected (or auto-applied) service option with its resolved (customer or default) price.</summary>
public record PriceServiceLine(
    Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal Value, decimal Amount,
    decimal? Quantity = null, string? InvoiceLabel = null, string Source = "Algemene standaard",
    /// <summary>True when the engine added this line automatically (contract service) — not selected by the user.</summary>
    bool AutoApplied = false);

public record PriceCalculationResult(
    IReadOnlyList<PriceBreakdownLine> Lines,
    /// <summary>Calculated total EXCLUDING informational AND proposed lines (diesel is added at invoicing).</summary>
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
    IReadOnlyList<string>? Diagnostics = null,
    /// <summary>Total + proposed (unconfirmed) extra-time charges — never silently invoiceable on its own.</summary>
    decimal TotalWithProposed = 0m,
    /// <summary>
    /// Task 11: the effective included loading/unloading time and extra-time rate actually applied
    /// (order override ?? winning agreement value), plus where it came from — drives the order
    /// form's "Laad- en lostijd" section. Null for a one-off order (see <see cref="OneOffPricingInput"/>,
    /// which has its own included-time fields).
    /// </summary>
    IncludedTimeInfoDto? IncludedTimeInfo = null,
    /// <summary>
    /// Wave 2026-08-04 §7: per-unit-line pricing coverage — whether each commercial unit the
    /// engine received has a base transport tariff, which per-unit services bill it, and the
    /// resulting status. The order side appends entries for cargo lines that never reached the
    /// engine (missing/unknown unit code).
    /// </summary>
    IReadOnlyList<PricingCoverageLine>? Coverage = null);

/// <summary>
/// Pricing coverage of one commercial unit line (wave 2026-08-04 §7). Status codes: "Full" — a
/// base transport tariff prices it; "Partial" — no base tariff, but at least one per-unit service
/// bills it (a service never masquerades as transport pricing); "None" — nothing prices it.
/// Reason is set whenever the status is not Full (e.g. "Geen passend basistarief").
/// </summary>
public record PricingCoverageLine(
    Guid? UnitTypeId, string UnitLabel, decimal Quantity, string Status,
    decimal BaseAmount = 0m, string? BaseRuleName = null,
    decimal ServicesAmount = 0m, string? Reason = null);

/// <summary>
/// Task 11: effective included-time info for the order form (spec 2026-08-02 §11). Source is
/// "Contract" (agreement value, no order override), "Order" (any of the 5 order overrides set) or
/// "Geen" (no engaged agreement with included time and no overrides — nothing to show).
/// </summary>
public record IncludedTimeInfoDto(
    int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, int? IncludedCombinedMinutes,
    decimal? ExtraHourlyRate, string Source);
