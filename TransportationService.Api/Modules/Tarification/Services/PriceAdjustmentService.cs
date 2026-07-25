using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IPriceAdjustmentService
{
    Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListAsync(Guid customerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewAsync(Guid customerId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken);
    Task<ScheduledPriceAdjustmentDto> CreateAsync(Guid customerId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken);
    Task<ScheduledPriceAdjustmentDto?> CancelAsync(Guid customerId, Guid adjustmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Bulk future price changes (spec §12/14/15/16): preview per rule, confirm to materialize
/// future effective versions (source windows close the day before), cancel while still
/// scheduled. Old versions are never deleted or overwritten — price history stays complete.
/// </summary>
public class PriceAdjustmentService : IPriceAdjustmentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public PriceAdjustmentService(
        TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    private Guid TenantId => _tenantContext.TenantId;

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var adjustments = await _dbContext.ScheduledPriceAdjustments.AsNoTracking()
            .Include(a => a.Rules)
            .Where(a => a.TenantId == TenantId && a.CustomerId == customerId)
            .OrderByDescending(a => a.EffectiveDate)
            .ToListAsync(cancellationToken);
        var today = Today;
        return adjustments.Select(a => Map(a, today)).ToList();
    }

    public async Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewAsync(
        Guid customerId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.EffectiveDate, request.Percent);
        var rules = await LoadAdjustableRulesAsync(customerId, request.EffectiveDate, request.RuleIds, cancellationToken);
        return rules.Select(rule => BuildPreview(rule, request.Percent)).ToList();
    }

    public async Task<ScheduledPriceAdjustmentDto> CreateAsync(
        Guid customerId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.EffectiveDate, request.Percent);
        var rules = await LoadAdjustableRulesAsync(customerId, request.EffectiveDate, request.RuleIds, cancellationToken);
        if (rules.Count == 0)
        {
            throw new DomainValidationException("rules", "Geen aanpasbare tariefregels gevonden voor deze klant.");
        }

        var adjustment = new ScheduledPriceAdjustment
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerId = customerId,
            EffectiveDate = request.EffectiveDate,
            Percent = request.Percent,
            Status = ScheduledAdjustmentStatus.Scheduled,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        };
        _dbContext.ScheduledPriceAdjustments.Add(adjustment);

        foreach (var source in rules)
        {
            var future = CloneAdjusted(source, request.EffectiveDate, request.Percent);
            _dbContext.PriceRules.Add(future);

            var link = new ScheduledPriceAdjustmentRule
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                AdjustmentId = adjustment.Id,
                SourcePriceRuleId = source.Id,
                CreatedPriceRuleId = future.Id,
                SourceOriginalEffectiveUntil = source.EffectiveUntil,
            };
            adjustment.Rules.Add(link);
            _dbContext.Entry(link).State = EntityState.Added;

            // The current version stays valid up to (at most) the day before the increase;
            // a version already ending earlier keeps its own end date.
            var dayBefore = request.EffectiveDate.AddDays(-1);
            if (source.EffectiveUntil is null || source.EffectiveUntil > dayBefore)
            {
                source.EffectiveUntil = dayBefore;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ScheduledPriceAdjustment", adjustment.Id.ToString(), "Created", null,
            new { adjustment.CustomerId, adjustment.EffectiveDate, adjustment.Percent, RuleCount = rules.Count, adjustment.Reason },
            cancellationToken);
        return Map(adjustment, Today);
    }

    public async Task<ScheduledPriceAdjustmentDto?> CancelAsync(Guid customerId, Guid adjustmentId, CancellationToken cancellationToken)
    {
        var adjustment = await _dbContext.ScheduledPriceAdjustments
            .Include(a => a.Rules)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.CustomerId == customerId && a.Id == adjustmentId, cancellationToken);
        if (adjustment is null)
        {
            return null;
        }

        if (adjustment.Status == ScheduledAdjustmentStatus.Cancelled)
        {
            throw new DomainValidationException("status", "Deze prijsaanpassing is al geannuleerd.");
        }

        if (adjustment.EffectiveDate <= Today)
        {
            throw new DomainValidationException("status",
                "Deze prijsaanpassing is al actief en kan niet meer worden geannuleerd.");
        }

        var createdIds = adjustment.Rules.Select(r => r.CreatedPriceRuleId).ToList();
        var sourceIds = adjustment.Rules.Select(r => r.SourcePriceRuleId).ToList();
        var createdRules = await _dbContext.PriceRules.Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId && createdIds.Contains(r.Id))
            .ToListAsync(cancellationToken);
        var sourceRules = await _dbContext.PriceRules
            .Where(r => r.TenantId == TenantId && sourceIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var created in createdRules)
        {
            _dbContext.RemoveRange(created.Brackets);
            _dbContext.Remove(created);
        }

        foreach (var link in adjustment.Rules)
        {
            if (sourceRules.TryGetValue(link.SourcePriceRuleId, out var source))
            {
                source.EffectiveUntil = link.SourceOriginalEffectiveUntil;
            }
        }

        adjustment.Status = ScheduledAdjustmentStatus.Cancelled;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ScheduledPriceAdjustment", adjustment.Id.ToString(), "Cancelled",
            new { adjustment.EffectiveDate, adjustment.Percent }, null, cancellationToken);
        return Map(adjustment, Today);
    }

    private void Validate(DateOnly effectiveDate, decimal percent)
    {
        if (effectiveDate <= Today)
        {
            throw new DomainValidationException("effectiveDate", "De ingangsdatum moet in de toekomst liggen.");
        }

        if (percent == 0)
        {
            throw new DomainValidationException("percent", "Geef een aanpassingspercentage op (bv. +4 of -2,5).");
        }

        if (Math.Abs(percent) > 100)
        {
            throw new DomainValidationException("percent", "Het percentage moet tussen -100 en +100 liggen.");
        }
    }

    /// <summary>
    /// Rules that can carry a future version: active customer rules still in force the day
    /// before the effective date (the current version the increase builds on). Explicitly
    /// selected rules outside that set are refused rather than silently skipped.
    /// </summary>
    private async Task<List<PriceRule>> LoadAdjustableRulesAsync(
        Guid customerId, DateOnly effectiveDate, IReadOnlyList<Guid>? ruleIds, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        var dayBefore = effectiveDate.AddDays(-1);
        var rules = await _dbContext.PriceRules
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId && r.CustomerId == customerId && r.IsActive
                        && r.EffectiveFrom <= dayBefore
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= dayBefore))
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        if (ruleIds is null)
        {
            return rules;
        }

        var selected = ruleIds.Distinct().ToHashSet();
        var adjustable = rules.Where(r => selected.Contains(r.Id)).ToList();
        if (adjustable.Count != selected.Count)
        {
            throw new DomainValidationException("ruleIds",
                "Eén of meer geselecteerde regels bestaan niet, horen niet bij deze klant of lopen al af vóór de ingangsdatum.");
        }

        return adjustable;
    }

    private PriceRule CloneAdjusted(PriceRule source, DateOnly effectiveDate, decimal percent)
    {
        var clone = new PriceRule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerId = source.CustomerId,
            UnitTypeId = source.UnitTypeId,
            Basis = source.Basis,
            ZoneId = source.ZoneId,
            Name = source.Name,
            Currency = source.Currency,
            EffectiveFrom = effectiveDate,
            // Keep a later end date; a source ending right before the increase leaves the
            // future version open-ended (the new prices simply run "vanaf" the date).
            EffectiveUntil = source.EffectiveUntil is { } until && until >= effectiveDate ? until : null,
            IsActive = true,
            UnitPrice = Adjust(source.UnitPrice, percent),
            MinimumAmount = Adjust(source.MinimumAmount, percent),
            AgreementId = source.AgreementId,
            Priority = source.Priority,
            BaseAmount = Adjust(source.BaseAmount, percent),
            MinimumQuantity = source.MinimumQuantity,
            QuantityRoundingStep = source.QuantityRoundingStep,
            OversizeLengthCm = source.OversizeLengthCm,
            OversizeWidthCm = source.OversizeWidthCm,
            OversizeBillableFactor = source.OversizeBillableFactor,
        };
        foreach (var bracket in source.Brackets.OrderBy(b => b.FromQuantity))
        {
            var futureBracket = new PriceRuleBracket
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PriceRuleId = clone.Id,
                FromQuantity = bracket.FromQuantity,
                ToQuantity = bracket.ToQuantity,
                Price = Adjust(bracket.Price, percent)!.Value,
                PricePerExtraUnit = Adjust(bracket.PricePerExtraUnit, percent),
            };
            clone.Brackets.Add(futureBracket);
            _dbContext.Entry(futureBracket).State = EntityState.Added;
        }

        return clone;
    }

    private static PriceAdjustmentRulePreview BuildPreview(PriceRule rule, decimal percent)
    {
        var changes = new List<PriceAdjustmentValueChange>();

        void AddChange(string field, decimal? oldValue)
        {
            if (oldValue is { } value)
            {
                changes.Add(new PriceAdjustmentValueChange(field, value, Adjust(value, percent)!.Value));
            }
        }

        AddChange("Prijs", rule.UnitPrice);
        AddChange("Minimumbedrag", rule.MinimumAmount);
        AddChange("Basisbedrag", rule.BaseAmount);
        foreach (var bracket in rule.Brackets.OrderBy(b => b.FromQuantity))
        {
            var range = bracket.ToQuantity is { } to ? $"{bracket.FromQuantity:0.##}-{to:0.##}" : $"{bracket.FromQuantity:0.##}+";
            AddChange($"Staffel {range}", bracket.Price);
            if (bracket.PricePerExtraUnit is not null)
            {
                AddChange($"Staffel {range} extra/eenheid", bracket.PricePerExtraUnit);
            }
        }

        return new PriceAdjustmentRulePreview(rule.Id, rule.Name, rule.EffectiveFrom, rule.EffectiveUntil, changes);
    }

    /// <summary>Monetary rounding policy: 2 decimals, away from zero (45 × 1.04 → 46.80).</summary>
    private static decimal? Adjust(decimal? value, decimal percent) =>
        value is { } v ? decimal.Round(v * (1 + percent / 100m), 2, MidpointRounding.AwayFromZero) : null;

    private static ScheduledPriceAdjustmentDto Map(ScheduledPriceAdjustment adjustment, DateOnly today)
    {
        var status = adjustment.Status == ScheduledAdjustmentStatus.Cancelled
            ? "Geannuleerd"
            : adjustment.EffectiveDate <= today ? "Actief" : "Gepland";
        return new ScheduledPriceAdjustmentDto(
            adjustment.Id, adjustment.CustomerId, adjustment.EffectiveDate, adjustment.Percent,
            status, adjustment.Reason, adjustment.Rules.Count, adjustment.CreatedAt);
    }
}
