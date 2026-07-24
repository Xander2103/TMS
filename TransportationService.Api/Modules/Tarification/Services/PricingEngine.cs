using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IPricingEngine
{
    /// <summary>
    /// Calculates an explainable price for an order: unit price rules (customer-specific
    /// preferred over company defaults, zone-specific over zone-less) per line, service
    /// options, and an informational diesel line. Falls back to the customer's rate card
    /// when no unit rules matched at all.
    /// </summary>
    Task<PriceCalculationResult> CalculateAsync(PriceCalculationRequest request, CancellationToken cancellationToken);
}

public class PricingEngine : IPricingEngine
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IRateCardService _rateCardService;

    public PricingEngine(TransportationDbContext dbContext, ITenantContext tenantContext, IRateCardService rateCardService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _rateCardService = rateCardService;
    }

    public async Task<PriceCalculationResult> CalculateAsync(PriceCalculationRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var lines = new List<PriceBreakdownLine>();
        var requiresManual = false;

        var zone = await ResolveZoneAsync(request.DeliveryCountryCode, request.DeliveryPostalCode, cancellationToken);

        var unitTypeIds = request.Lines.Select(l => l.UnitTypeId).Distinct().ToList();
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitTypeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var candidateRules = await _dbContext.PriceRules.AsNoTracking()
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == tenantId && r.IsActive
                        && (r.CustomerId == null || r.CustomerId == request.CustomerId)
                        && r.EffectiveFrom <= request.Date
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);

        var anyRuleMatched = false;
        foreach (var line in request.Lines)
        {
            var unitName = unitNames.GetValueOrDefault(line.UnitTypeId, "eenheid");
            var rule = PickRule(candidateRules, request.CustomerId, line.UnitTypeId, zone?.Id);
            if (rule is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen tarief geconfigureerd voor {unitName}", 0m, "Ontbrekend"));
                requiresManual = true;
                continue;
            }

            anyRuleMatched = true;
            var amount = ComputeRuleAmount(rule, line.Quantity, request.WeightKg);
            if (amount is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen staffel voor {line.Quantity:0.##} × {unitName}", 0m, rule.Name));
                requiresManual = true;
                continue;
            }

            var value = amount.Value;
            if (rule.MinimumAmount is { } minimum && value < minimum)
            {
                value = minimum;
            }

            var zoneSuffix = rule.ZoneId is not null && zone is not null ? $" (zone {zone.Code})" : "";
            lines.Add(new PriceBreakdownLine(
                $"{line.Quantity:0.##} × {unitName}{zoneSuffix}", decimal.Round(value, 2), rule.Name));
        }

        // Fallback: no unit rules at all → the legacy rate-card engine remains authoritative.
        if (!anyRuleMatched && request.Lines.Count > 0)
        {
            try
            {
                var quote = await _rateCardService.QuoteAsync(
                    new QuoteRequest(request.CustomerId, request.Date, request.DistanceKm, request.PalletCount, request.WeightKg),
                    cancellationToken);
                lines.Clear();
                requiresManual = false;
                lines.AddRange(quote.Lines.Select(l => new PriceBreakdownLine(l.Label, l.Amount, $"Tarievenkaart: {quote.RateCardName}")));
            }
            catch (DomainValidationException)
            {
                // No rate card either — the "Ontbrekend" lines above stay and manual pricing is required.
            }
        }

        var subtotal = lines.Where(l => !l.Informational).Sum(l => l.Amount);

        // Service options: customer price wins over the default; Percent applies to the base subtotal.
        if (request.ServiceOptionIds.Count > 0)
        {
            var optionIds = request.ServiceOptionIds.Distinct().ToList();
            var options = await _dbContext.ServiceOptions.AsNoTracking()
                .Where(o => o.TenantId == tenantId && optionIds.Contains(o.Id) && o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
                .ToListAsync(cancellationToken);
            var customerPrices = await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.CustomerId == request.CustomerId && optionIds.Contains(p.ServiceOptionId))
                .ToDictionaryAsync(p => p.ServiceOptionId, p => p.Value, cancellationToken);

            foreach (var option in options)
            {
                var value = customerPrices.GetValueOrDefault(option.Id, option.DefaultValue);
                var amount = option.Kind == SurchargeKind.Percent
                    ? decimal.Round(subtotal * value / 100m, 2)
                    : decimal.Round(value, 2);
                lines.Add(new PriceBreakdownLine(option.Name, amount,
                    customerPrices.ContainsKey(option.Id) ? "Klantprijs" : "Standaardtarief"));
            }
        }

        var total = lines.Where(l => !l.Informational).Sum(l => l.Amount);

        // Diesel surcharge is owned by invoicing (separate invoice lines); shown here as an
        // informational line so the calculation stays explainable without double-charging.
        var diesel = await _dbContext.Set<CustomerDieselSurcharge>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.CustomerId == request.CustomerId && d.Enabled, cancellationToken);
        if (diesel is not null
            && (diesel.EffectiveFrom is null || diesel.EffectiveFrom <= request.Date)
            && (diesel.EffectiveUntil is null || diesel.EffectiveUntil >= request.Date))
        {
            lines.Add(new PriceBreakdownLine(
                $"Dieseltoeslag {diesel.Percent:0.##}% (wordt bij facturatie toegevoegd)",
                decimal.Round(total * diesel.Percent / 100m, 2), "Dieseltoeslag", Informational: true));
        }

        var totalWithInformational = total + lines.Where(l => l.Informational).Sum(l => l.Amount);
        return new PriceCalculationResult(
            lines, decimal.Round(total, 2), decimal.Round(totalWithInformational, 2), "EUR",
            zone?.Code, zone?.Name, requiresManual);
    }

    /// <summary>Customer+zone → customer → company+zone → company default.</summary>
    private static PriceRule? PickRule(IReadOnlyList<PriceRule> candidates, Guid customerId, Guid unitTypeId, Guid? zoneId)
    {
        var forUnit = candidates.Where(r => r.UnitTypeId == unitTypeId).ToList();
        return forUnit.FirstOrDefault(r => r.CustomerId == customerId && zoneId is not null && r.ZoneId == zoneId)
            ?? forUnit.FirstOrDefault(r => r.CustomerId == customerId && r.ZoneId == null)
            ?? forUnit.FirstOrDefault(r => r.CustomerId == null && zoneId is not null && r.ZoneId == zoneId)
            ?? forUnit.FirstOrDefault(r => r.CustomerId == null && r.ZoneId == null);
    }

    private static decimal? ComputeRuleAmount(PriceRule rule, decimal quantity, decimal? weightKg)
    {
        switch (rule.Basis)
        {
            case PriceRuleBasis.PerUnit:
            case PriceRuleBasis.Hourly:
                return rule.UnitPrice is { } rate ? rate * quantity : null;
            case PriceRuleBasis.Fixed:
                return rule.UnitPrice;
            case PriceRuleBasis.QuantityBracket:
                return BracketAmount(rule, quantity);
            case PriceRuleBasis.WeightBracket:
                return weightKg is { } weight ? BracketAmount(rule, weight) : null;
            default:
                return null;
        }
    }

    private static decimal? BracketAmount(PriceRule rule, decimal value)
    {
        var bracket = rule.Brackets
            .Where(b => value >= b.FromQuantity && (b.ToQuantity is null || value <= b.ToQuantity))
            .OrderBy(b => b.FromQuantity)
            .LastOrDefault();
        if (bracket is null)
        {
            return null;
        }

        var amount = bracket.Price;
        if (bracket.ToQuantity is null && bracket.PricePerExtraUnit is { } extra && value > bracket.FromQuantity)
        {
            amount += extra * (value - bracket.FromQuantity);
        }

        return amount;
    }

    private async Task<PricingZone?> ResolveZoneAsync(string? countryCode, string? postalCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        var country = string.IsNullOrWhiteSpace(countryCode) ? "BE" : countryCode.Trim().ToUpperInvariant();
        var code = postalCode.Trim();
        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Include(z => z.Areas)
            .Where(z => z.TenantId == _tenantContext.TenantId && z.IsActive)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .ToListAsync(cancellationToken);

        foreach (var zone in zones)
        {
            foreach (var area in zone.Areas)
            {
                if (!string.Equals(area.CountryCode, country, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Numeric ranges when both bounds and the code parse; ordinal otherwise (e.g. NL "1234 AB").
                if (int.TryParse(code, out var numeric)
                    && int.TryParse(area.PostalCodeFrom, out var from) && int.TryParse(area.PostalCodeTo, out var to))
                {
                    if (numeric >= from && numeric <= to)
                    {
                        return zone;
                    }
                }
                else if (string.CompareOrdinal(code, area.PostalCodeFrom) >= 0 && string.CompareOrdinal(code, area.PostalCodeTo) <= 0)
                {
                    return zone;
                }
            }
        }

        return null;
    }
}
