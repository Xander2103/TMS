namespace TransportationService.Api.Modules.Tarification.Dtos;

public record RateSurchargeDto(Guid Id, string Name, string Kind, decimal Value);

public record RateCardDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string Name,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveUntil,
    decimal BaseAmount,
    decimal? PerKmRate,
    decimal? PerPalletRate,
    decimal? PerTonRate,
    decimal? MinimumAmount,
    string? Notes,
    IReadOnlyList<RateSurchargeDto> Surcharges);

public record SaveRateSurchargeRequest(string Name, string Kind, decimal Value);

public record SaveRateCardRequest(
    Guid CustomerId,
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveUntil = null,
    decimal BaseAmount = 0,
    decimal? PerKmRate = null,
    decimal? PerPalletRate = null,
    decimal? PerTonRate = null,
    decimal? MinimumAmount = null,
    string? Notes = null,
    IReadOnlyList<SaveRateSurchargeRequest>? Surcharges = null);

/// <summary>One human-readable line of the price build-up shown to the user.</summary>
public record QuoteLineDto(string Label, decimal Amount);

public record QuoteDto(
    Guid RateCardId,
    string RateCardName,
    string Currency,
    IReadOnlyList<QuoteLineDto> Lines,
    decimal Total);

public record QuoteRequest(
    Guid CustomerId,
    DateOnly Date,
    decimal? DistanceKm = null,
    int? PalletCount = null,
    decimal? WeightKg = null);
