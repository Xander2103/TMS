using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Services;

/// <summary>
/// One-time (idempotent) conversion of legacy rate cards into the coherent pricing model:
/// each card becomes a PricingAgreement with order-level component rules (fixed base,
/// per-km, per-pallet, per-ton), the card's minimum and its surcharges. The legacy
/// rate_cards rows are preserved untouched for history; LegacyRateCardId marks a card as
/// converted so re-running at every startup adds nothing.
/// </summary>
public static class RateCardConversionService
{
    public static async Task ConvertAsync(TransportationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var cards = await dbContext.RateCards
            .Include(c => c.Surcharges)
            .ToListAsync(cancellationToken);
        if (cards.Count == 0)
        {
            return;
        }

        var convertedIds = (await dbContext.PricingAgreements
                .IgnoreQueryFilters()
                .Where(a => a.LegacyRateCardId != null)
                .Select(a => a.LegacyRateCardId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var converted = false;
        foreach (var card in cards.Where(c => !convertedIds.Contains(c.Id)))
        {
            var agreement = new PricingAgreement
            {
                Id = Guid.NewGuid(),
                TenantId = card.TenantId,
                CustomerId = card.CustomerId,
                Name = card.Name,
                Currency = card.Currency,
                EffectiveFrom = card.EffectiveFrom,
                EffectiveUntil = card.EffectiveUntil,
                IsActive = true,
                MinimumAmount = card.MinimumAmount,
                Notes = card.Notes,
                LegacyRateCardId = card.Id,
            };
            dbContext.PricingAgreements.Add(agreement);

            foreach (var surcharge in card.Surcharges)
            {
                dbContext.PricingAgreementSurcharges.Add(new PricingAgreementSurcharge
                {
                    Id = Guid.NewGuid(),
                    TenantId = card.TenantId,
                    AgreementId = agreement.Id,
                    Name = surcharge.Name,
                    Kind = surcharge.Kind,
                    Value = surcharge.Value,
                });
            }

            void AddRule(string name, PriceRuleBasis basis, decimal unitPrice) =>
                dbContext.PriceRules.Add(new PriceRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = card.TenantId,
                    CustomerId = card.CustomerId,
                    UnitTypeId = null,
                    Basis = basis,
                    Name = name,
                    Currency = card.Currency,
                    EffectiveFrom = card.EffectiveFrom,
                    EffectiveUntil = card.EffectiveUntil,
                    IsActive = true,
                    UnitPrice = unitPrice,
                    AgreementId = agreement.Id,
                });

            if (card.BaseAmount > 0)
            {
                AddRule("Basisbedrag", PriceRuleBasis.Fixed, card.BaseAmount);
            }

            if (card.PerKmRate is { } perKm)
            {
                AddRule("Kilometertarief", PriceRuleBasis.PerKm, perKm);
            }

            if (card.PerPalletRate is { } perPallet)
            {
                AddRule("Palletprijs", PriceRuleBasis.PerPallet, perPallet);
            }

            if (card.PerTonRate is { } perTon)
            {
                AddRule("Tonprijs", PriceRuleBasis.PerTon, perTon);
            }

            converted = true;
        }

        if (converted)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
