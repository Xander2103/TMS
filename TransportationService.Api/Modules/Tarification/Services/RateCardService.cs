using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IRateCardService
{
    Task<IReadOnlyList<RateCardDto>> ListAsync(Guid? customerId, CancellationToken cancellationToken);

    Task<RateCardDto> CreateAsync(SaveRateCardRequest request, CancellationToken cancellationToken);

    Task<RateCardDto?> UpdateAsync(Guid id, SaveRateCardRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Computes a price quote with explanation lines from the card effective on the date.</summary>
    Task<QuoteDto> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken);
}

public class RateCardService : IRateCardService
{
    private const string EntityType = "RateCard";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public RateCardService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<RateCardDto>> ListAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.RateCards.AsNoTracking()
            .Include(r => r.Surcharges)
            .Where(r => r.TenantId == tenantId);
        if (customerId is { } cid)
        {
            query = query.Where(r => r.CustomerId == cid);
        }

        var cards = await query
            .OrderByDescending(r => r.EffectiveFrom)
            .Take(500)
            .ToListAsync(cancellationToken);
        var customerIds = cards.Select(c => c.CustomerId).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return cards.Select(c => Map(c, customers.GetValueOrDefault(c.CustomerId, "?"))).ToList();
    }

    public async Task<RateCardDto> CreateAsync(SaveRateCardRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var customerName = await ValidateAsync(request, tenantId, excludeCardId: null, cancellationToken);

        var card = new RateCard
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
        };
        Apply(card, request, tenantId);
        _dbContext.Add(card);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, card.Id.ToString(), "Created", null,
            new { card.Name, card.CustomerId, card.EffectiveFrom }, cancellationToken);

        return Map(card, customerName);
    }

    public async Task<RateCardDto?> UpdateAsync(Guid id, SaveRateCardRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var card = await _dbContext.RateCards
            .Include(r => r.Surcharges)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken);
        if (card is null)
        {
            return null;
        }

        var customerName = await ValidateAsync(request, tenantId, excludeCardId: id, cancellationToken);

        // Surcharges are replaced wholesale; the card is the aggregate root.
        _dbContext.RemoveRange(card.Surcharges);
        card.Surcharges = [];
        Apply(card, request, tenantId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, card.Id.ToString(), "Updated", null,
            new { card.Name, card.CustomerId, card.EffectiveFrom }, cancellationToken);

        return Map(card, customerName);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var card = await _dbContext.RateCards
            .Include(r => r.Surcharges)
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.Id == id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        _dbContext.RemoveRange(card.Surcharges);
        _dbContext.Remove(card);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, id.ToString(), "Deleted", null,
            new { card.Name, card.CustomerId }, cancellationToken);

        return true;
    }

    public async Task<QuoteDto> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var card = await _dbContext.RateCards.AsNoTracking()
            .Include(r => r.Surcharges)
            .Where(r => r.TenantId == tenantId && r.CustomerId == request.CustomerId
                        && r.EffectiveFrom <= request.Date
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= request.Date))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (card is null)
        {
            throw new DomainValidationException("customerId",
                "Geen geldige tarievenkaart voor deze klant op de gekozen datum.");
        }

        var lines = new List<QuoteLineDto>();
        if (card.BaseAmount != 0)
        {
            lines.Add(new QuoteLineDto("Basisbedrag", Round(card.BaseAmount)));
        }

        if (card.PerKmRate is { } perKm && request.DistanceKm is { } km && km > 0)
        {
            lines.Add(new QuoteLineDto($"Afstand: {km:0.#} km × {perKm:0.####} {card.Currency}/km", Round(km * perKm)));
        }

        if (card.PerPalletRate is { } perPallet && request.PalletCount is { } pallets && pallets > 0)
        {
            lines.Add(new QuoteLineDto($"Pallets: {pallets} × {perPallet:0.##} {card.Currency}", Round(pallets * perPallet)));
        }

        if (card.PerTonRate is { } perTon && request.WeightKg is { } kg && kg > 0)
        {
            var tons = kg / 1000m;
            lines.Add(new QuoteLineDto($"Gewicht: {tons:0.###} ton × {perTon:0.##} {card.Currency}/ton", Round(tons * perTon)));
        }

        var subtotal = lines.Sum(l => l.Amount);
        foreach (var surcharge in card.Surcharges.OrderBy(s => s.Name))
        {
            var amount = surcharge.Kind == SurchargeKind.Percent
                ? Round(subtotal * surcharge.Value / 100m)
                : Round(surcharge.Value);
            var label = surcharge.Kind == SurchargeKind.Percent
                ? $"Toeslag: {surcharge.Name} ({surcharge.Value:0.##}%)"
                : $"Toeslag: {surcharge.Name}";
            lines.Add(new QuoteLineDto(label, amount));
        }

        var total = lines.Sum(l => l.Amount);
        if (card.MinimumAmount is { } minimum && total < minimum)
        {
            lines.Add(new QuoteLineDto($"Minimumtarief toegepast ({minimum:0.##} {card.Currency})", Round(minimum - total)));
            total = minimum;
        }

        return new QuoteDto(card.Id, card.Name, card.Currency, lines, Round(total));
    }

    private async Task<string> ValidateAsync(
        SaveRateCardRequest request, Guid tenantId, Guid? excludeCardId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "Een naam is verplicht.");
        }

        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == request.CustomerId)
            .Select(c => (string?)c.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerName is null)
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de startdatum.");
        }

        if (request.BaseAmount < 0 || request.PerKmRate is < 0 || request.PerPalletRate is < 0
            || request.PerTonRate is < 0 || request.MinimumAmount is < 0)
        {
            throw new DomainValidationException("baseAmount", "Tarieven kunnen niet negatief zijn.");
        }

        foreach (var surcharge in request.Surcharges ?? [])
        {
            if (string.IsNullOrWhiteSpace(surcharge.Name))
            {
                throw new DomainValidationException("surcharges", "Elke toeslag heeft een naam nodig.");
            }

            if (!Enum.TryParse<SurchargeKind>(surcharge.Kind, ignoreCase: true, out _))
            {
                throw new DomainValidationException("surcharges", $"Onbekend toeslagtype '{surcharge.Kind}'.");
            }
        }

        // Overlapping windows would make the effective-card lookup ambiguous.
        var overlaps = await _dbContext.RateCards
            .Where(r => r.TenantId == tenantId && r.CustomerId == request.CustomerId && r.Id != excludeCardId)
            .Where(r => r.EffectiveFrom <= (request.EffectiveUntil ?? DateOnly.MaxValue)
                        && (r.EffectiveUntil ?? DateOnly.MaxValue) >= request.EffectiveFrom)
            .AnyAsync(cancellationToken);
        if (overlaps)
        {
            throw new DomainValidationException("effectiveFrom",
                "Er bestaat al een tarievenkaart voor deze klant in (een deel van) deze periode.");
        }

        return customerName;
    }

    private void Apply(RateCard card, SaveRateCardRequest request, Guid tenantId)
    {
        card.CustomerId = request.CustomerId;
        card.Name = request.Name.Trim();
        card.EffectiveFrom = request.EffectiveFrom;
        card.EffectiveUntil = request.EffectiveUntil;
        card.BaseAmount = request.BaseAmount;
        card.PerKmRate = request.PerKmRate;
        card.PerPalletRate = request.PerPalletRate;
        card.PerTonRate = request.PerTonRate;
        card.MinimumAmount = request.MinimumAmount;
        card.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        foreach (var surcharge in request.Surcharges ?? [])
        {
            var entity = new RateSurcharge
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RateCardId = card.Id,
                Name = surcharge.Name.Trim(),
                Kind = Enum.Parse<SurchargeKind>(surcharge.Kind, ignoreCase: true),
                Value = surcharge.Value,
            };
            card.Surcharges.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private static RateCardDto Map(RateCard card, string customerName) => new(
        card.Id, card.CustomerId, customerName, card.Name, card.Currency,
        card.EffectiveFrom, card.EffectiveUntil,
        card.BaseAmount, card.PerKmRate, card.PerPalletRate, card.PerTonRate, card.MinimumAmount,
        card.Notes,
        card.Surcharges.OrderBy(s => s.Name)
            .Select(s => new RateSurchargeDto(s.Id, s.Name, s.Kind.ToString(), s.Value))
            .ToList());

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
