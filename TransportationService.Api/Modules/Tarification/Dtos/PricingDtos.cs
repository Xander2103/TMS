using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Dtos;

// --- Zones ---

public record PricingZoneAreaDto(Guid Id, string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record PricingZoneDto(Guid Id, string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<PricingZoneAreaDto> Areas);

public record SavePricingZoneAreaRequest(string CountryCode, string PostalCodeFrom, string PostalCodeTo);

public record SavePricingZoneRequest(string Code, string Name, bool IsActive, int SortOrder, IReadOnlyList<SavePricingZoneAreaRequest> Areas);

// --- Price rules ---

public record PriceRuleBracketDto(Guid Id, decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit);

public record PriceRuleDto(
    Guid Id, Guid? CustomerId, string? CustomerName, Guid? UnitTypeId, string? UnitTypeName,
    PriceRuleBasis Basis, Guid? ZoneId, string? ZoneName,
    string Name, string Currency, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<PriceRuleBracketDto> Brackets);

public record SavePriceRuleBracketRequest(decimal FromQuantity, decimal? ToQuantity, decimal Price, decimal? PricePerExtraUnit);

public record SavePriceRuleRequest(
    Guid? CustomerId, Guid? UnitTypeId, PriceRuleBasis Basis, Guid? ZoneId,
    string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsActive,
    decimal? UnitPrice, decimal? MinimumAmount, IReadOnlyList<SavePriceRuleBracketRequest>? Brackets);

// --- Service options ---

public record ServiceOptionDto(Guid Id, string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder);

public record SaveServiceOptionRequest(string Code, string Name, SurchargeKind Kind, decimal DefaultValue, bool IsActive, int SortOrder);

// --- Customer pricing configuration ---

public record CustomerPreferredUnitDto(Guid UnitTypeId, string Code, string Name, int SortOrder);

public record CustomerServiceOptionPriceDto(Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal DefaultValue, decimal? CustomerValue);

public record CustomerPricingConfigDto(
    IReadOnlyList<CustomerPreferredUnitDto> PreferredUnits,
    IReadOnlyList<CustomerServiceOptionPriceDto> ServiceOptions);

public record SaveCustomerPricingConfigRequest(
    IReadOnlyList<Guid> PreferredUnitTypeIds,
    IReadOnlyList<SaveCustomerOptionPriceRequest> OptionPrices);

public record SaveCustomerOptionPriceRequest(Guid ServiceOptionId, decimal? Value);

// --- Calculation ---

public record PriceCalculationLineInput(Guid UnitTypeId, decimal Quantity);

public record PriceCalculationRequest(
    Guid CustomerId,
    DateOnly Date,
    IReadOnlyList<PriceCalculationLineInput> Lines,
    string? DeliveryCountryCode,
    string? DeliveryPostalCode,
    decimal? WeightKg,
    decimal? DistanceKm,
    int? PalletCount,
    IReadOnlyList<Guid> ServiceOptionIds);

public record PriceBreakdownLine(string Label, decimal Amount, string Source, bool Informational = false);

/// <summary>A selected service option with its resolved (customer or default) price.</summary>
public record PriceServiceLine(Guid ServiceOptionId, string Name, SurchargeKind Kind, decimal Value, decimal Amount);

public record PriceCalculationResult(
    IReadOnlyList<PriceBreakdownLine> Lines,
    /// <summary>Calculated total EXCLUDING informational lines (diesel is added at invoicing).</summary>
    decimal Total,
    decimal TotalWithInformational,
    string Currency,
    string? ZoneCode,
    string? ZoneName,
    bool RequiresManualPrice,
    IReadOnlyList<PriceServiceLine> ServiceLines);
