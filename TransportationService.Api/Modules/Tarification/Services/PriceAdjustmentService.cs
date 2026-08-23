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

    Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListForAgreementAsync(Guid agreementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewForAgreementAsync(Guid agreementId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken);
    Task<ScheduledPriceAdjustmentDto> CreateForAgreementAsync(Guid agreementId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken);
    Task<ScheduledPriceAdjustmentDto?> CancelForAgreementAsync(Guid agreementId, Guid adjustmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Bulk future price changes (spec §12/14/15/16, v2: agreement scope + fixed-amount delta +
/// rounding + basis/unit filters): preview per rule, confirm to materialize future effective
/// versions (source windows close the day before), cancel while still scheduled. Old versions
/// are never deleted or overwritten — price history stays complete.
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

    // --- Customer scope (existing surface) ---

    public Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListAsync(Guid customerId, CancellationToken cancellationToken) =>
        ListInternalAsync(customerId, null, cancellationToken);

    public Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewAsync(
        Guid customerId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        PreviewInternalAsync(customerId, null, request, cancellationToken);

    public Task<ScheduledPriceAdjustmentDto> CreateAsync(
        Guid customerId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        CreateInternalAsync(customerId, null, request, cancellationToken);

    public Task<ScheduledPriceAdjustmentDto?> CancelAsync(Guid customerId, Guid adjustmentId, CancellationToken cancellationToken) =>
        CancelInternalAsync(customerId, null, adjustmentId, cancellationToken);

    // --- Agreement scope ---

    public Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListForAgreementAsync(Guid agreementId, CancellationToken cancellationToken) =>
        ListInternalAsync(null, agreementId, cancellationToken);

    public Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewForAgreementAsync(
        Guid agreementId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        PreviewInternalAsync(null, agreementId, request, cancellationToken);

    public Task<ScheduledPriceAdjustmentDto> CreateForAgreementAsync(
        Guid agreementId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken) =>
        CreateInternalAsync(null, agreementId, request, cancellationToken);

    public Task<ScheduledPriceAdjustmentDto?> CancelForAgreementAsync(Guid agreementId, Guid adjustmentId, CancellationToken cancellationToken) =>
        CancelInternalAsync(null, agreementId, adjustmentId, cancellationToken);

    // --- Shared implementation ---

    private async Task<IReadOnlyList<ScheduledPriceAdjustmentDto>> ListInternalAsync(
        Guid? customerId, Guid? agreementId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ScheduledPriceAdjustments.AsNoTracking()
            .Include(a => a.Rules)
            .Where(a => a.TenantId == TenantId);
        query = customerId is { } c ? query.Where(a => a.CustomerId == c) : query.Where(a => a.AgreementId == agreementId);
        var adjustments = await query.OrderByDescending(a => a.EffectiveDate).ToListAsync(cancellationToken);
        var today = Today;
        return adjustments.Select(a => Map(a, today)).ToList();
    }

    private async Task<IReadOnlyList<PriceAdjustmentRulePreview>> PreviewInternalAsync(
        Guid? customerId, Guid? agreementId, PreviewPriceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.EffectiveDate, request.Percent, request.AmountDelta, request.RoundingStep, customerId, agreementId);
        var rules = await LoadAdjustableRulesAsync(
            customerId, agreementId, request.EffectiveDate, request.RuleIds,
            request.BasisFilter, request.UnitTypeIdFilter, cancellationToken);
        return rules.Select(rule => BuildPreview(rule, request.Percent, request.AmountDelta, request.RoundingStep)).ToList();
    }

    private async Task<ScheduledPriceAdjustmentDto> CreateInternalAsync(
        Guid? customerId, Guid? agreementId, CreatePriceAdjustmentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.EffectiveDate, request.Percent, request.AmountDelta, request.RoundingStep, customerId, agreementId);
        var rules = await LoadAdjustableRulesAsync(
            customerId, agreementId, request.EffectiveDate, request.RuleIds,
            request.BasisFilter, request.UnitTypeIdFilter, cancellationToken);
        if (rules.Count == 0)
        {
            throw new DomainValidationException("rules",
                customerId is not null
                    ? "Geen aanpasbare tariefregels gevonden voor deze klant."
                    : "Geen aanpasbare tariefregels gevonden voor deze prijsafspraak.");
        }

        var adjustment = new ScheduledPriceAdjustment
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            CustomerId = customerId,
            AgreementId = agreementId,
            EffectiveDate = request.EffectiveDate,
            Percent = request.Percent,
            AmountDelta = request.AmountDelta,
            RoundingStep = request.RoundingStep,
            BasisFilter = request.BasisFilter,
            UnitTypeIdFilter = request.UnitTypeIdFilter,
            Status = ScheduledAdjustmentStatus.Scheduled,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
        };
        _dbContext.ScheduledPriceAdjustments.Add(adjustment);

        foreach (var source in rules)
        {
            var future = CloneAdjusted(source, request.EffectiveDate, request.Percent, request.AmountDelta, request.RoundingStep);
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
            new
            {
                adjustment.CustomerId, adjustment.AgreementId, adjustment.EffectiveDate, adjustment.Percent,
                adjustment.AmountDelta, adjustment.RoundingStep, RuleCount = rules.Count, adjustment.Reason,
            },
            cancellationToken);
        return Map(adjustment, Today);
    }

    private async Task<ScheduledPriceAdjustmentDto?> CancelInternalAsync(
        Guid? customerId, Guid? agreementId, Guid adjustmentId, CancellationToken cancellationToken)
    {
        var query = _dbContext.ScheduledPriceAdjustments.Include(a => a.Rules)
            .Where(a => a.TenantId == TenantId && a.Id == adjustmentId);
        query = customerId is { } c ? query.Where(a => a.CustomerId == c) : query.Where(a => a.AgreementId == agreementId);
        var adjustment = await query.FirstOrDefaultAsync(cancellationToken);
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
            new { adjustment.EffectiveDate, adjustment.Percent, adjustment.AmountDelta }, null, cancellationToken);
        return Map(adjustment, Today);
    }

    /// <summary>
    /// Runtime guard (defense in depth, currently structurally unreachable via the public API —
    /// every caller passes exactly one of customerId/agreementId): a scope with both set would be
    /// ambiguous (which scope's rules should the adjustment target?), so it is rejected outright
    /// rather than silently picking one.
    /// </summary>
    private void Validate(
        DateOnly effectiveDate, decimal? percent, decimal? amountDelta, decimal? roundingStep,
        Guid? customerId = null, Guid? agreementId = null)
    {
        if (customerId is not null && agreementId is not null)
        {
            throw new DomainValidationException("Kies precies één toepassingsgebied.");
        }

        if (effectiveDate <= Today)
        {
            throw new DomainValidationException("effectiveDate", "De ingangsdatum moet in de toekomst liggen.");
        }

        if ((percent is null) == (amountDelta is null))
        {
            throw new DomainValidationException("percent", "Kies precies één: een percentage of een vast bedrag.");
        }

        if (percent is { } p)
        {
            if (p == 0)
            {
                throw new DomainValidationException("percent", "Geef een aanpassingspercentage op (bv. +4 of -2,5).");
            }

            if (Math.Abs(p) > 100)
            {
                throw new DomainValidationException("percent", "Het percentage moet tussen -100 en +100 liggen.");
            }
        }

        if (amountDelta == 0m)
        {
            throw new DomainValidationException("amountDelta", "Geef een aanpassingsbedrag op (bv. +5 of -2,50).");
        }

        if (roundingStep is { } step && step != 0.01m && step != 0.05m && step != 0.10m)
        {
            throw new DomainValidationException("roundingStep", "Kies geen afronding, of 0,01, 0,05 of 0,10.");
        }
    }

    /// <summary>
    /// Rules that can carry a future version: active rules in scope (customer or agreement) still
    /// in force the day before the effective date (the current version the change builds on),
    /// narrowed by the optional basis/unit filters. Explicitly selected rules outside that set are
    /// refused rather than silently skipped.
    /// </summary>
    private async Task<List<PriceRule>> LoadAdjustableRulesAsync(
        Guid? customerId, Guid? agreementId, DateOnly effectiveDate, IReadOnlyList<Guid>? ruleIds,
        string? basisFilter, Guid? unitTypeIdFilter, CancellationToken cancellationToken)
    {
        if (customerId is { } cid
            && !await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == cid, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        if (agreementId is { } aid
            && !await _dbContext.PricingAgreements.AnyAsync(a => a.TenantId == TenantId && a.Id == aid, cancellationToken))
        {
            throw new InvalidTenantReferenceException("prijsafspraak");
        }

        var dayBefore = effectiveDate.AddDays(-1);
        var query = _dbContext.PriceRules
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == TenantId && r.IsActive
                        && r.EffectiveFrom <= dayBefore
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= dayBefore));
        query = customerId is { } c ? query.Where(r => r.CustomerId == c) : query.Where(r => r.AgreementId == agreementId);

        var rules = await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(basisFilter))
        {
            var bases = basisFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            rules = rules.Where(r => bases.Contains(r.Basis.ToString())).ToList();
        }

        if (unitTypeIdFilter is { } unitId)
        {
            rules = rules.Where(r => r.UnitTypeId == unitId).ToList();
        }

        if (ruleIds is null)
        {
            return rules;
        }

        var selected = ruleIds.Distinct().ToHashSet();
        var adjustable = rules.Where(r => selected.Contains(r.Id)).ToList();
        if (adjustable.Count != selected.Count)
        {
            throw new DomainValidationException("ruleIds",
                "Eén of meer geselecteerde regels bestaan niet, horen niet bij deze scope of lopen al af vóór de ingangsdatum.");
        }

        return adjustable;
    }

    private PriceRule CloneAdjusted(PriceRule source, DateOnly effectiveDate, decimal? percent, decimal? amountDelta, decimal? roundingStep)
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
            UnitPrice = Adjust(source.UnitPrice, percent, amountDelta, roundingStep, source.Name),
            MinimumAmount = Adjust(source.MinimumAmount, percent, amountDelta, roundingStep, source.Name),
            MaximumAmount = Adjust(source.MaximumAmount, percent, amountDelta, roundingStep, source.Name),
            AgreementId = source.AgreementId,
            Priority = source.Priority,
            BaseAmount = Adjust(source.BaseAmount, percent, amountDelta, roundingStep, source.Name),
            MinimumQuantity = source.MinimumQuantity,
            QuantityRoundingStep = source.QuantityRoundingStep,
            BracketMode = source.BracketMode,
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
                Price = Adjust(bracket.Price, percent, amountDelta, roundingStep, source.Name)!.Value,
                PricePerExtraUnit = Adjust(bracket.PricePerExtraUnit, percent, amountDelta, roundingStep, source.Name),
                WeightToKg = bracket.WeightToKg,
                VolumeToM3 = bracket.VolumeToM3,
                LoadingMetersTo = bracket.LoadingMetersTo,
            };
            clone.Brackets.Add(futureBracket);
            _dbContext.Entry(futureBracket).State = EntityState.Added;
        }

        return clone;
    }

    private static PriceAdjustmentRulePreview BuildPreview(PriceRule rule, decimal? percent, decimal? amountDelta, decimal? roundingStep)
    {
        var changes = new List<PriceAdjustmentValueChange>();

        void AddChange(string field, decimal? oldValue)
        {
            if (oldValue is { } value)
            {
                changes.Add(new PriceAdjustmentValueChange(field, value, Adjust(value, percent, amountDelta, roundingStep, rule.Name)!.Value));
            }
        }

        AddChange("Prijs", rule.UnitPrice);
        AddChange("Minimumbedrag", rule.MinimumAmount);
        AddChange("Maximumbedrag", rule.MaximumAmount);
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

    private static decimal? Adjust(decimal? value, decimal? percent, decimal? amountDelta, decimal? roundingStep, string ruleName) =>
        PriceAdjustmentMath.Adjust(value, percent, amountDelta, roundingStep, ruleName);

    private static ScheduledPriceAdjustmentDto Map(ScheduledPriceAdjustment adjustment, DateOnly today)
    {
        // StatusCode = stabiel contract; het Nederlandse Status-veld blijft één release
        // als legacy weergave mee (frontend vertaalt intussen op StatusCode).
        var statusCode = adjustment.Status == ScheduledAdjustmentStatus.Cancelled
            ? "Cancelled"
            : adjustment.EffectiveDate <= today ? "Active" : "Planned";
        var status = statusCode switch
        {
            "Cancelled" => "Geannuleerd",
            "Active" => "Actief",
            _ => "Gepland",
        };
        return new ScheduledPriceAdjustmentDto(
            adjustment.Id, adjustment.CustomerId, adjustment.EffectiveDate, adjustment.Percent,
            status, adjustment.Reason, adjustment.Rules.Count, adjustment.CreatedAt,
            adjustment.AgreementId, adjustment.AmountDelta, adjustment.RoundingStep,
            adjustment.BasisFilter, adjustment.UnitTypeIdFilter, statusCode);
    }
}
