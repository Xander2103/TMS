using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tarification.Services;

public interface IPricingAdminService
{
    Task<IReadOnlyList<PricingZoneDto>> ListZonesAsync(CancellationToken cancellationToken);
    Task<PricingZoneDto> CreateZoneAsync(SavePricingZoneRequest request, CancellationToken cancellationToken);
    Task<PricingZoneDto?> UpdateZoneAsync(Guid id, SavePricingZoneRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>includeAll => every agreement of the tenant regardless of CustomerId (the "Tarieventabellen" overview); otherwise customerId filters as before (null = company-wide/shared only).</summary>
    Task<IReadOnlyList<PricingAgreementDto>> ListAgreementsAsync(Guid? customerId, CancellationToken cancellationToken, bool includeAll = false);
    Task<PricingAgreementDto?> GetAgreementAsync(Guid id, CancellationToken cancellationToken);
    Task<PricingAgreementDto> CreateAgreementAsync(SavePricingAgreementRequest request, CancellationToken cancellationToken);
    Task<PricingAgreementDto?> UpdateAgreementAsync(Guid id, SavePricingAgreementRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAgreementAsync(Guid id, CancellationToken cancellationToken);
    Task<PricingAgreementDto?> DuplicateAgreementAsync(Guid id, DuplicateAgreementRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// "Controle": configuration-health checks for one agreement (overlapping rule windows, staffel
    /// gaps, derivation-chain health, orphaned/mismatched assignments, inactive unit/zone
    /// references, drifted min/max data, ...). Null = the agreement does not exist for this tenant.
    /// Never throws for a "bad" configuration — every finding is reported, not blocked.
    /// </summary>
    Task<IReadOnlyList<PricingConfigCheckDto>?> ValidateAgreementConfigurationAsync(Guid agreementId, CancellationToken cancellationToken);

    /// <summary>
    /// Same duplication as <see cref="DuplicateAgreementAsync"/>, but prepares the copy WITHOUT
    /// saving or auditing — used by the Excel import "new version" mode, which must apply the
    /// file's changes to the copy's rules and persist everything (duplicate + import) in one
    /// SaveChanges/transaction so an invalid import never leaves a bare duplicate behind. Callers
    /// own the transaction, the SaveChangesAsync call and the audit entry. The returned map is
    /// source PriceRule.Id → copied PriceRule.Id, for translating the file's RegelId column.
    /// </summary>
    Task<(PricingAgreement NewAgreement, IReadOnlyDictionary<Guid, Guid> RuleIdMap)?> PrepareAgreementDuplicateAsync(
        Guid id, DuplicateAgreementRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PricingAgreementAssignmentDto>?> ListAssignmentsAsync(Guid agreementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PricingAgreementAssignmentDto>?> SaveAssignmentsAsync(
        Guid agreementId, IReadOnlyList<SavePricingAssignmentRequest> requests, CancellationToken cancellationToken);

    /// <summary>agreementId (when set) filters precisely to that agreement's own rules, taking priority over customerId.</summary>
    Task<IReadOnlyList<PriceRuleDto>> ListRulesAsync(Guid? customerId, CancellationToken cancellationToken, Guid? agreementId = null);
    Task<PriceRuleDto> CreateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken);
    Task<PriceRuleDto?> UpdateRuleAsync(Guid id, SavePriceRuleRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Row-level customer overrides ("klantafwijkingen") of one bracket rule. Null = rule unknown for this tenant. customerId filters when set.</summary>
    Task<IReadOnlyList<PriceRuleBracketOverrideDto>?> ListBracketOverridesAsync(Guid ruleId, Guid? customerId, CancellationToken cancellationToken);
    Task<PriceRuleBracketOverrideDto?> CreateBracketOverrideAsync(Guid ruleId, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken);
    Task<PriceRuleBracketOverrideDto?> UpdateBracketOverrideAsync(Guid id, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteBracketOverrideAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceOptionDto>> ListServiceOptionsAsync(bool includeInactive, bool forOrderEntry, CancellationToken cancellationToken);
    Task<ServiceOptionDto> CreateServiceOptionAsync(SaveServiceOptionRequest request, CancellationToken cancellationToken);
    Task<ServiceOptionDto?> UpdateServiceOptionAsync(Guid id, SaveServiceOptionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteServiceOptionAsync(Guid id, CancellationToken cancellationToken);

    // Wave 3 §4: tenant holidays driving Holiday time surcharges.
    Task<IReadOnlyList<TenantHolidayDto>> ListHolidaysAsync(CancellationToken cancellationToken);
    Task<TenantHolidayDto> CreateHolidayAsync(SaveTenantHolidayRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteHolidayAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerPricingConfigDto?> GetCustomerConfigAsync(Guid customerId, CancellationToken cancellationToken);
    Task<CustomerPricingConfigDto?> SaveCustomerConfigAsync(Guid customerId, SaveCustomerPricingConfigRequest request, CancellationToken cancellationToken);

    /// <summary>customerId/agreementId filter; both null lists every combined-unit discount of the tenant.</summary>
    Task<IReadOnlyList<CombinedUnitDiscountDto>> ListCombinedDiscountsAsync(Guid? customerId, Guid? agreementId, CancellationToken cancellationToken);
    Task<CombinedUnitDiscountDto> CreateCombinedDiscountAsync(SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken);
    Task<CombinedUnitDiscountDto?> UpdateCombinedDiscountAsync(Guid id, SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteCombinedDiscountAsync(Guid id, CancellationToken cancellationToken);
}

public class PricingAdminService : IPricingAdminService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public PricingAdminService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private Guid TenantId => _tenantContext.TenantId;

    // --- Zones ---

    public async Task<IReadOnlyList<PricingZoneDto>> ListZonesAsync(CancellationToken cancellationToken)
    {
        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Include(z => z.Areas)
            .Where(z => z.TenantId == TenantId)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .ToListAsync(cancellationToken);
        return zones.Select(MapZone).ToList();
    }

    public async Task<PricingZoneDto> CreateZoneAsync(SavePricingZoneRequest request, CancellationToken cancellationToken)
    {
        ValidateZone(request);
        await EnsureZoneCodeFreeAsync(request.Code, null, cancellationToken);

        var zone = new PricingZone { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyZone(zone, request);
        _dbContext.PricingZones.Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Created", null, new { zone.Code, zone.Name }, cancellationToken);
        return MapZone(zone);
    }

    public async Task<PricingZoneDto?> UpdateZoneAsync(Guid id, SavePricingZoneRequest request, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.PricingZones.Include(z => z.Areas)
            .FirstOrDefaultAsync(z => z.TenantId == TenantId && z.Id == id, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        ValidateZone(request);
        await EnsureZoneCodeFreeAsync(request.Code, id, cancellationToken);

        _dbContext.PricingZoneAreas.RemoveRange(zone.Areas);
        zone.Areas.Clear();
        ApplyZone(zone, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Updated", null, new { zone.Code, zone.Name }, cancellationToken);
        return MapZone(zone);
    }

    public async Task<bool> DeleteZoneAsync(Guid id, CancellationToken cancellationToken)
    {
        var zone = await _dbContext.PricingZones.FirstOrDefaultAsync(z => z.TenantId == TenantId && z.Id == id, cancellationToken);
        if (zone is null)
        {
            return false;
        }

        var inUse = await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.ZoneId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException("Deze zone wordt gebruikt door prijsregels. Verwijder of wijzig die eerst.");
        }

        _dbContext.Remove(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingZone", zone.Id.ToString(), "Deleted", new { zone.Code }, null, cancellationToken);
        return true;
    }

    // --- Pricing agreements (rate cards) ---

    public async Task<IReadOnlyList<PricingAgreementDto>> ListAgreementsAsync(
        Guid? customerId, CancellationToken cancellationToken, bool includeAll = false)
    {
        var query = _dbContext.PricingAgreements.AsNoTracking()
            .Include(a => a.Surcharges)
            .Include(a => a.Modifiers)
            .Where(a => a.TenantId == TenantId);
        if (!includeAll)
        {
            query = query.Where(a => customerId == null ? a.CustomerId == null : a.CustomerId == customerId);
        }

        var agreements = await query.OrderByDescending(a => a.EffectiveFrom).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        return await MapAgreementsAsync(agreements, cancellationToken);
    }

    public async Task<PricingAgreementDto?> GetAgreementAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .Include(a => a.Surcharges).Include(a => a.Modifiers)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        return agreement is null ? null : (await MapAgreementsAsync([agreement], cancellationToken))[0];
    }

    public async Task<PricingAgreementDto> CreateAgreementAsync(SavePricingAgreementRequest request, CancellationToken cancellationToken)
    {
        await ValidateAgreementAsync(request, null, cancellationToken);
        var agreement = new PricingAgreement { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyAgreement(agreement, request);
        _dbContext.PricingAgreements.Add(agreement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Created", null,
            new { agreement.Name, agreement.CustomerId, agreement.EffectiveFrom }, cancellationToken);
        return (await MapAgreementsAsync([agreement], cancellationToken))[0];
    }

    public async Task<PricingAgreementDto?> UpdateAgreementAsync(Guid id, SavePricingAgreementRequest request, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.Include(a => a.Surcharges).Include(a => a.Modifiers)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        await ValidateAgreementAsync(request, id, cancellationToken);
        // Surcharges/modifiers are replaced wholesale; the agreement is the aggregate root.
        _dbContext.RemoveRange(agreement.Surcharges);
        agreement.Surcharges = [];
        _dbContext.RemoveRange(agreement.Modifiers);
        agreement.Modifiers = [];
        ApplyAgreement(agreement, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Updated", null,
            new { agreement.Name, agreement.CustomerId, agreement.EffectiveFrom }, cancellationToken);
        return (await MapAgreementsAsync([agreement], cancellationToken))[0];
    }

    public async Task<bool> DeleteAgreementAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return false;
        }

        var inUse = await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.AgreementId == id, cancellationToken);
        if (inUse)
        {
            throw new DomainValidationException(
                "Deze prijsafspraak bevat nog tariefregels. Verwijder of verplaats die eerst.");
        }

        var dependentNames = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.BaseAgreementId == id)
            .Select(a => a.Name)
            .ToListAsync(cancellationToken);
        if (dependentNames.Count > 0)
        {
            throw new DomainValidationException(
                "Deze tabel is de basis voor " + string.Join(", ", dependentNames.Select(n => $"'{n}'"))
                + ". Verwijder eerst de afgeleide tabellen.");
        }

        _dbContext.Remove(agreement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", agreement.Id.ToString(), "Deleted", new { agreement.Name }, null, cancellationToken);
        return true;
    }

    /// <summary>
    /// Creates a new version of an agreement: copies its rules (incl. brackets), surcharges,
    /// modifiers and BaseAgreementId with a new effective window, optionally applying a percent
    /// or fixed-amount adjustment (same math as scheduled price adjustments) to the copied rules.
    /// Assignments are deliberately NOT copied — a shared table's new version must be linked to
    /// customers explicitly via the assignments endpoint, so nothing silently re-applies pricing.
    /// </summary>
    public async Task<PricingAgreementDto?> DuplicateAgreementAsync(
        Guid id, DuplicateAgreementRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAgreementDuplicateAsync(id, request, cancellationToken);
        if (prepared is null)
        {
            return null;
        }

        var (newAgreement, ruleIdMap) = prepared.Value;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreement", newAgreement.Id.ToString(), "duplicated", null,
            new { SourceAgreementId = id, newAgreement.Name, newAgreement.EffectiveFrom, request.CloseSource, RuleCount = ruleIdMap.Count },
            cancellationToken);
        return (await MapAgreementsAsync([newAgreement], cancellationToken))[0];
    }

    /// <summary>
    /// "Controle": reports configuration-health findings for one agreement without ever throwing —
    /// every problem (from a blocking overlap to a merely dead configuration) is a line in the
    /// returned list, tenant-filtered throughout. See <see cref="IPricingAdminService.ValidateAgreementConfigurationAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<PricingConfigCheckDto>?> ValidateAgreementConfigurationAsync(
        Guid agreementId, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        var checks = new List<PricingConfigCheckDto>();

        // Agreement MinimumAmount > MaximumAmount — re-check of save-time validation, since data
        // can drift (e.g. a base table's max lowered after this agreement's min was already saved).
        if (agreement.MinimumAmount is { } minAmount && agreement.MaximumAmount is { } maxAmount && minAmount > maxAmount)
        {
            checks.Add(new PricingConfigCheckDto("error",
                $"Het minimumbedrag ({minAmount:0.00}) is hoger dan het maximumbedrag ({maxAmount:0.00})."));
        }

        // Derived-chain health: base inactive/window mismatch (warning), cycle/depth drift (error).
        if (agreement.BaseAgreementId is { } baseId)
        {
            var baseAgreement = await _dbContext.PricingAgreements.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == baseId, cancellationToken);
            if (baseAgreement is null)
            {
                checks.Add(new PricingConfigCheckDto("warning", "De basistabel van deze afgeleide tabel bestaat niet meer."));
            }
            else
            {
                if (!baseAgreement.IsActive)
                {
                    checks.Add(new PricingConfigCheckDto("warning", $"Basistabel '{baseAgreement.Name}' is niet actief."));
                }

                var baseCoversWindow = baseAgreement.EffectiveFrom <= agreement.EffectiveFrom
                    && (baseAgreement.EffectiveUntil is null
                        || (agreement.EffectiveUntil is not null && baseAgreement.EffectiveUntil >= agreement.EffectiveUntil));
                if (!baseCoversWindow)
                {
                    checks.Add(new PricingConfigCheckDto("warning",
                        $"Basistabel '{baseAgreement.Name}' dekt de geldigheidsperiode van deze tabel niet volledig."));
                }
            }

            // Should be impossible via save-time validation (ValidateDerivationChainAsync) — report
            // if the data drifted (e.g. a chain rewired directly, bypassing normal saves).
            var (cycle, tooDeep) = await CheckBaseChainAsync(agreementId, baseId, cancellationToken);
            if (cycle)
            {
                checks.Add(new PricingConfigCheckDto("error", "Circulaire verwijzing tussen tarieventabellen."));
            }
            else if (tooDeep)
            {
                checks.Add(new PricingConfigCheckDto("error", "Maximale afleidingsdiepte (3) overschreden."));
            }
        }

        var assignments = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        if (agreement.IsShared && assignments.Count == 0)
        {
            checks.Add(new PricingConfigCheckDto("warning", "Deze gedeelde tabel is aan geen enkele klant gekoppeld."));
        }

        if (assignments.Count > 0)
        {
            var customerIds = assignments.Select(a => a.CustomerId).Distinct().ToList();
            var customerNames = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
            var agreementFrom = agreement.EffectiveFrom;
            var agreementTo = agreement.EffectiveUntil ?? DateOnly.MaxValue;
            foreach (var assignment in assignments)
            {
                var assignmentFrom = assignment.EffectiveFrom ?? DateOnly.MinValue;
                var assignmentTo = assignment.EffectiveUntil ?? DateOnly.MaxValue;
                var overlaps = assignmentFrom <= agreementTo && agreementFrom <= assignmentTo;
                if (!overlaps)
                {
                    var customerName = customerNames.GetValueOrDefault(assignment.CustomerId, "?");
                    checks.Add(new PricingConfigCheckDto("warning",
                        $"De klantkoppeling met '{customerName}' valt buiten de geldigheidsperiode van deze tabel."));
                }
            }
        }

        // Rules physically owned by this agreement — a derived table has none of its own (it
        // reuses its base-chain root's rules, checked there instead).
        var rules = await _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets)
            .Where(r => r.TenantId == tenantId && r.AgreementId == agreementId)
            .ToListAsync(cancellationToken);

        if (rules.Count > 0)
        {
            var unitIds = rules.Where(r => r.UnitTypeId is not null).Select(r => r.UnitTypeId!.Value).Distinct().ToList();
            var zoneIds = rules.Where(r => r.ZoneId is not null).Select(r => r.ZoneId!.Value).Distinct().ToList();
            var units = await _dbContext.UnitTypes.AsNoTracking()
                .Where(u => u.TenantId == tenantId && unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, cancellationToken);
            var zones = await _dbContext.PricingZones.AsNoTracking()
                .Where(z => z.TenantId == tenantId && zoneIds.Contains(z.Id))
                .ToDictionaryAsync(z => z.Id, cancellationToken);

            foreach (var rule in rules)
            {
                if (rule.UnitTypeId is { } unitId && units.TryGetValue(unitId, out var unit) && !unit.IsActive)
                {
                    checks.Add(new PricingConfigCheckDto("warning", $"Regel '{rule.Name}' gebruikt de inactieve eenheid '{unit.Name}'."));
                }

                if (rule.ZoneId is { } zoneId && zones.TryGetValue(zoneId, out var zone) && !zone.IsActive)
                {
                    checks.Add(new PricingConfigCheckDto("warning", $"Regel '{rule.Name}' gebruikt de inactieve zone '{zone.Name}'."));
                }
            }

            // Overlapping effective windows of two rules with identical specificity — an exact tie
            // the pricing engine itself would refuse to resolve (SelectRule blocks on this).
            foreach (var group in rules.GroupBy(r => (r.UnitTypeId, r.ZoneId, r.Basis, r.CustomerId, r.Priority)))
            {
                var candidates = group.ToList();
                for (var i = 0; i < candidates.Count; i++)
                {
                    for (var j = i + 1; j < candidates.Count; j++)
                    {
                        if (RuleWindowsOverlap(candidates[i], candidates[j]))
                        {
                            checks.Add(new PricingConfigCheckDto("error",
                                $"Regels '{candidates[i].Name}' en '{candidates[j].Name}' overlappen in geldigheid met gelijke "
                                + "specificiteit — dit blokkeert prijsberekening in die periode."));
                        }
                    }
                }
            }

            // Staffel gaps + brackets not starting at 0/1 (informational — the engine simply
            // returns "geen staffel" for a quantity that falls in a gap, never crashes).
            foreach (var rule in rules.Where(r => r.Basis is PriceRuleBasis.QuantityBracket or PriceRuleBasis.WeightBracket))
            {
                var ordered = rule.Brackets.OrderBy(b => b.FromQuantity).ToList();
                if (ordered.Count == 0)
                {
                    continue;
                }

                if (ordered[0].FromQuantity != 0 && ordered[0].FromQuantity != 1)
                {
                    checks.Add(new PricingConfigCheckDto("warning",
                        $"Staffel '{rule.Name}' begint niet bij 0 of 1 (begint bij {ordered[0].FromQuantity:0.##})."));
                }

                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i - 1].ToQuantity is { } previousTo)
                    {
                        if (ordered[i].FromQuantity > previousTo + 1)
                        {
                            checks.Add(new PricingConfigCheckDto("warning",
                                $"Staffel '{rule.Name}' heeft een gat tussen {previousTo:0.##} en {ordered[i].FromQuantity:0.##}."));
                        }
                    }
                    else
                    {
                        // An open-ended row (ToQuantity == null) before the last row silently skips
                        // the gap check for the following row — surface it instead of no-oping.
                        checks.Add(new PricingConfigCheckDto("warning",
                            $"Staffel '{rule.Name}' heeft een open einde vóór de laatste rij."));
                    }
                }
            }
        }

        return checks;
    }

    /// <summary>Null-open windows treated as -/+ infinity; overlap = the two windows intersect.</summary>
    private static bool RuleWindowsOverlap(PriceRule a, PriceRule b)
    {
        var aTo = a.EffectiveUntil ?? DateOnly.MaxValue;
        var bTo = b.EffectiveUntil ?? DateOnly.MaxValue;
        return a.EffectiveFrom <= bTo && b.EffectiveFrom <= aTo;
    }

    /// <summary>
    /// Non-throwing counterpart of <see cref="ValidateDerivationChainAsync"/>, for the "Controle"
    /// endpoint: walks the base-chain from <paramref name="baseAgreementId"/> upward (max 3 hops),
    /// reporting a cycle or excessive depth instead of throwing — this data should never exist
    /// (save-time validation prevents it) but is reported if it drifted in some other way.
    /// </summary>
    private async Task<(bool Cycle, bool TooDeep)> CheckBaseChainAsync(
        Guid agreementId, Guid baseAgreementId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { agreementId };
        Guid? current = baseAgreementId;
        var hops = 0;
        while (current is not null)
        {
            if (!visited.Add(current.Value))
            {
                return (true, false);
            }

            hops++;
            if (hops > 3)
            {
                return (false, true);
            }

            current = await _dbContext.PricingAgreements.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.Id == current.Value)
                .Select(a => (Guid?)a.BaseAgreementId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return (false, false);
    }

    public async Task<(PricingAgreement NewAgreement, IReadOnlyDictionary<Guid, Guid> RuleIdMap)?> PrepareAgreementDuplicateAsync(
        Guid id, DuplicateAgreementRequest request, CancellationToken cancellationToken)
    {
        var source = await _dbContext.PricingAgreements
            .Include(a => a.Surcharges).Include(a => a.Modifiers)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == id, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (request.Percent is not null && request.AmountDelta is not null)
        {
            throw new DomainValidationException("percent", "Kies precies één: een percentage of een vast bedrag.");
        }

        if (request.RoundingStep is { } step && step != 0.01m && step != 0.05m && step != 0.10m)
        {
            throw new DomainValidationException("roundingStep", "Kies geen afronding, of 0,01, 0,05 of 0,10.");
        }

        if (request.CloseSource && request.EffectiveFrom <= source.EffectiveFrom)
        {
            throw new DomainValidationException("effectiveFrom",
                "De ingangsdatum van de nieuwe versie moet na de startdatum van de huidige versie liggen.");
        }

        var tenantId = TenantId;
        var sourceRules = await _dbContext.PriceRules.Include(r => r.Brackets)
            .Where(r => r.TenantId == tenantId && r.AgreementId == id)
            .ToListAsync(cancellationToken);

        var newAgreement = new PricingAgreement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = source.CustomerId,
            Name = request.Name.Trim(),
            Currency = source.Currency,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveUntil = null,
            IsActive = true,
            MinimumAmount = source.MinimumAmount,
            MaximumAmount = source.MaximumAmount,
            IsShared = source.IsShared,
            Notes = source.Notes,
            BaseAgreementId = source.BaseAgreementId,
        };
        _dbContext.PricingAgreements.Add(newAgreement);

        foreach (var surcharge in source.Surcharges)
        {
            var copy = new PricingAgreementSurcharge
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AgreementId = newAgreement.Id,
                Name = surcharge.Name, Kind = surcharge.Kind, Value = surcharge.Value,
            };
            newAgreement.Surcharges.Add(copy);
            _dbContext.Entry(copy).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }

        foreach (var modifier in source.Modifiers)
        {
            var copy = new PricingAgreementModifier
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AgreementId = newAgreement.Id,
                Sequence = modifier.Sequence, Name = modifier.Name, CountryCode = modifier.CountryCode,
                ZoneId = modifier.ZoneId, Percent = modifier.Percent, FixedAmount = modifier.FixedAmount,
            };
            newAgreement.Modifiers.Add(copy);
            _dbContext.Entry(copy).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }

        var dayBeforeNew = request.EffectiveFrom.AddDays(-1);
        var ruleIdMap = new Dictionary<Guid, Guid>();
        foreach (var sourceRule in sourceRules)
        {
            decimal? AdjustValue(decimal? value) =>
                request.Percent is null && request.AmountDelta is null
                    ? value
                    : PriceAdjustmentMath.Adjust(value, request.Percent, request.AmountDelta, request.RoundingStep, sourceRule.Name);

            var clone = new PriceRule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = sourceRule.CustomerId,
                UnitTypeId = sourceRule.UnitTypeId,
                Basis = sourceRule.Basis,
                ZoneId = sourceRule.ZoneId,
                Name = sourceRule.Name,
                Currency = sourceRule.Currency,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveUntil = null,
                IsActive = true,
                UnitPrice = AdjustValue(sourceRule.UnitPrice),
                MinimumAmount = AdjustValue(sourceRule.MinimumAmount),
                MaximumAmount = AdjustValue(sourceRule.MaximumAmount),
                AgreementId = newAgreement.Id,
                Priority = sourceRule.Priority,
                BaseAmount = AdjustValue(sourceRule.BaseAmount),
                MinimumQuantity = sourceRule.MinimumQuantity,
                QuantityRoundingStep = sourceRule.QuantityRoundingStep,
                BracketMode = sourceRule.BracketMode,
                OversizeLengthCm = sourceRule.OversizeLengthCm,
                OversizeWidthCm = sourceRule.OversizeWidthCm,
                OversizeBillableFactor = sourceRule.OversizeBillableFactor,
            };
            _dbContext.PriceRules.Add(clone);
            ruleIdMap[sourceRule.Id] = clone.Id;

            foreach (var bracket in sourceRule.Brackets)
            {
                _dbContext.PriceRuleBrackets.Add(new PriceRuleBracket
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PriceRuleId = clone.Id,
                    FromQuantity = bracket.FromQuantity,
                    ToQuantity = bracket.ToQuantity,
                    Price = AdjustValue(bracket.Price)!.Value,
                    PricePerExtraUnit = AdjustValue(bracket.PricePerExtraUnit),
                    WeightToKg = bracket.WeightToKg,
                    VolumeToM3 = bracket.VolumeToM3,
                    LoadingMetersTo = bracket.LoadingMetersTo,
                });
            }

            if (request.CloseSource && (sourceRule.EffectiveUntil is null || sourceRule.EffectiveUntil > dayBeforeNew))
            {
                sourceRule.EffectiveUntil = dayBeforeNew;
            }
        }

        if (request.CloseSource)
        {
            source.EffectiveUntil = dayBeforeNew;
        }

        return (newAgreement, ruleIdMap);
    }

    /// <summary>
    /// Walks the proposed base-chain from <paramref name="baseAgreementId"/> upward: revisiting the
    /// agreement being saved (<paramref name="agreementId"/>, null for a new agreement) or any node
    /// already seen is a cycle; a chain longer than 3 hops from the saved agreement is too deep.
    /// </summary>
    private async Task ValidateDerivationChainAsync(Guid? agreementId, Guid baseAgreementId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        Guid? current = baseAgreementId;
        var hops = 1;
        while (current is not null)
        {
            if (current == agreementId || !visited.Add(current.Value))
            {
                throw new DomainValidationException("baseAgreementId", "Circulaire verwijzing tussen tarieventabellen.");
            }

            if (hops > 3)
            {
                throw new DomainValidationException("baseAgreementId", "Maximale afleidingsdiepte (3) overschreden.");
            }

            var next = await _dbContext.PricingAgreements.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.Id == current.Value)
                .Select(a => (Guid?)a.BaseAgreementId)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is null)
            {
                break;
            }

            hops++;
            current = next;
        }
    }

    /// <summary>Wave 2: a sales code on a pricing object must be an own-tenant category.</summary>
    private async Task ValidateSalesCategoryAsync(Guid? salesCategoryId, CancellationToken cancellationToken)
    {
        if (salesCategoryId is { } id
            && !await _dbContext.SalesCategories.AnyAsync(c => c.TenantId == TenantId && c.Id == id, cancellationToken))
        {
            throw new InvalidTenantReferenceException("verkoopcategorie");
        }
    }

    private async Task ValidateAgreementAsync(SavePricingAgreementRequest request, Guid? agreementId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        await ValidateSalesCategoryAsync(request.SalesCategoryId, cancellationToken);

        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
        }

        if (request.MinimumAmount is < 0)
        {
            throw new DomainValidationException("minimumAmount", "Het minimumbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is < 0)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is { } maximum && request.MinimumAmount is { } minimum && maximum < minimum)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumbedrag moet minstens het minimumbedrag zijn.");
        }

        if (request.IsShared && request.CustomerId is not null)
        {
            throw new DomainValidationException("isShared",
                "Een herbruikbare tabel is niet gekoppeld aan één klant; koppel klanten via de klantkoppelingen.");
        }

        if (request.IncludedLoadingMinutes is < 0 || request.IncludedUnloadingMinutes is < 0 || request.IncludedCombinedMinutes is < 0)
        {
            throw new DomainValidationException("includedLoadingMinutes", "Inbegrepen tijd mag niet negatief zijn.");
        }

        if (request.ExtraHourlyRate is < 0)
        {
            throw new DomainValidationException("extraHourlyRate", "Het uurtarief voor extra tijd mag niet negatief zijn.");
        }

        if (request.IncludedCombinedMinutes is not null
            && (request.IncludedLoadingMinutes is not null || request.IncludedUnloadingMinutes is not null))
        {
            throw new DomainValidationException("includedCombinedMinutes",
                "Kies inbegrepen tijd per activiteit óf gecombineerd, niet beide.");
        }

        foreach (var surcharge in request.Surcharges ?? [])
        {
            if (string.IsNullOrWhiteSpace(surcharge.Name))
            {
                throw new DomainValidationException("surcharges", "Elke toeslag heeft een naam nodig.");
            }

            if (surcharge.Kind is not (SurchargeKind.Percent or SurchargeKind.Fixed))
            {
                throw new DomainValidationException("surcharges",
                    "Een automatische toeslag op een prijsafspraak is een percentage of een vast bedrag.");
            }
        }

        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == TenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            if (string.IsNullOrWhiteSpace(modifier.Name))
            {
                throw new DomainValidationException("modifiers", "Elke aanpassing heeft een naam nodig.");
            }

            if ((modifier.Percent is null) == (modifier.FixedAmount is null))
            {
                throw new DomainValidationException("modifiers", "Kies per regel een percentage óf een vast bedrag.");
            }
        }

        var modifierZoneIds = (request.Modifiers ?? [])
            .Where(m => m.ZoneId.HasValue).Select(m => m.ZoneId!.Value).Distinct().ToList();
        if (modifierZoneIds.Count > 0)
        {
            var knownModifierZones = await _dbContext.PricingZones
                .CountAsync(z => z.TenantId == TenantId && modifierZoneIds.Contains(z.Id), cancellationToken);
            if (knownModifierZones != modifierZoneIds.Count)
            {
                throw new InvalidTenantReferenceException("zone");
            }
        }

        var surchargeNames = (request.Surcharges ?? [])
            .Select(s => s.Name.Trim().ToLowerInvariant())
            .ToList();
        if (surchargeNames.Distinct().Count() != surchargeNames.Count)
        {
            throw new DomainValidationException("surcharges",
                "Toeslagen op één tabel moeten een unieke naam hebben.");
        }

        if (request.BaseAgreementId is { } baseAgreementId)
        {
            // A derived table has no rules of its own — reject converting a table that already
            // carries rules, and reject targeting one that is itself already derived's own rules
            // (that half is enforced in ValidateRuleAsync, on rule creation).
            var hasOwnRules = agreementId is { } existingId
                && await _dbContext.PriceRules.AnyAsync(r => r.TenantId == TenantId && r.AgreementId == existingId, cancellationToken);
            if (hasOwnRules)
            {
                throw new DomainValidationException("baseAgreementId",
                    "Deze tabel heeft eigen prijsregels en kan niet afgeleid worden.");
            }

            var baseAgreement = await _dbContext.PricingAgreements.AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == baseAgreementId, cancellationToken);
            if (baseAgreement is null)
            {
                throw new InvalidTenantReferenceException("basistabel");
            }

            // A shared/company-default derived table on a private base would leak that customer's
            // prices to everyone the derived table applies to.
            if (request.CustomerId is null && baseAgreement.CustomerId is not null)
            {
                throw new DomainValidationException("baseAgreementId", "Basistabel moet een gedeelde of algemene tabel zijn.");
            }

            await ValidateDerivationChainAsync(agreementId, baseAgreementId, cancellationToken);
        }
        else if (request.Modifiers is { Count: > 0 })
        {
            throw new DomainValidationException("modifiers", "Aanpassingen zijn alleen mogelijk op een afgeleide tabel.");
        }
    }

    private void ApplyAgreement(PricingAgreement agreement, SavePricingAgreementRequest request)
    {
        agreement.CustomerId = request.CustomerId;
        agreement.Name = request.Name.Trim();
        agreement.EffectiveFrom = request.EffectiveFrom;
        agreement.EffectiveUntil = request.EffectiveUntil;
        agreement.IsActive = request.IsActive;
        agreement.MinimumAmount = request.MinimumAmount;
        agreement.MaximumAmount = request.MaximumAmount;
        agreement.IsShared = request.IsShared;
        agreement.Notes = Clean(request.Notes);
        agreement.BaseAgreementId = request.BaseAgreementId;
        agreement.IncludedLoadingMinutes = request.IncludedLoadingMinutes;
        agreement.IncludedUnloadingMinutes = request.IncludedUnloadingMinutes;
        agreement.IncludedCombinedMinutes = request.IncludedCombinedMinutes;
        agreement.ExtraHourlyRate = request.ExtraHourlyRate;
        agreement.SalesCategoryId = request.SalesCategoryId;
        foreach (var surcharge in request.Surcharges ?? [])
        {
            var entity = new PricingAgreementSurcharge
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                AgreementId = agreement.Id,
                Name = surcharge.Name.Trim(),
                Kind = surcharge.Kind,
                Value = surcharge.Value,
            };
            agreement.Surcharges.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            var entity = new PricingAgreementModifier
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                AgreementId = agreement.Id,
                Sequence = modifier.Sequence,
                Name = modifier.Name.Trim(),
                CountryCode = string.IsNullOrWhiteSpace(modifier.CountryCode) ? null : modifier.CountryCode.Trim().ToUpperInvariant(),
                ZoneId = modifier.ZoneId,
                Percent = modifier.Percent,
                FixedAmount = modifier.FixedAmount,
            };
            agreement.Modifiers.Add(entity);
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private async Task<IReadOnlyList<PricingAgreementDto>> MapAgreementsAsync(
        IReadOnlyList<PricingAgreement> agreements, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = agreements.Where(a => a.CustomerId.HasValue).Select(a => a.CustomerId!.Value).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Two extra, batched queries for the whole list — never per-agreement — to attach the
        // "assigned today" customer count/names to shared tables without N+1.
        var agreementIds = agreements.Select(a => a.Id).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activeAssignments = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && agreementIds.Contains(x.AgreementId)
                        && (x.EffectiveFrom == null || x.EffectiveFrom <= today)
                        && (x.EffectiveUntil == null || x.EffectiveUntil >= today))
            .ToListAsync(cancellationToken);
        var assignedCustomerIds = activeAssignments.Select(x => x.CustomerId).Distinct().ToList();
        var assignedCustomerNames = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && assignedCustomerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var namesByAgreement = activeAssignments
            .GroupBy(x => x.AgreementId)
            .ToDictionary(g => g.Key, g => g.Select(x => assignedCustomerNames.GetValueOrDefault(x.CustomerId, "?")).OrderBy(n => n).ToList());

        var baseAgreementIds = agreements.Where(a => a.BaseAgreementId.HasValue).Select(a => a.BaseAgreementId!.Value).Distinct().ToList();
        var baseAgreementNames = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == tenantId && baseAgreementIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var modifierZoneIds = agreements.SelectMany(a => a.Modifiers)
            .Where(m => m.ZoneId.HasValue).Select(m => m.ZoneId!.Value).Distinct().ToList();
        var zoneNames = await _dbContext.PricingZones.AsNoTracking()
            .Where(z => z.TenantId == tenantId && modifierZoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);
        var salesCategoryIds = agreements.Where(a => a.SalesCategoryId.HasValue)
            .Select(a => a.SalesCategoryId!.Value).Distinct().ToList();
        var salesCategoryNames = await _dbContext.SalesCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && salesCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return agreements.Select(a =>
        {
            var names = namesByAgreement.GetValueOrDefault(a.Id);
            return new PricingAgreementDto(
                a.Id, a.CustomerId,
                a.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null,
                a.Name, a.Currency, a.EffectiveFrom, a.EffectiveUntil, a.IsActive,
                a.MinimumAmount, a.Notes,
                a.Surcharges.OrderBy(s => s.Name)
                    .Select(s => new PricingAgreementSurchargeDto(s.Id, s.Name, s.Kind, s.Value))
                    .ToList(),
                a.IsShared, a.MaximumAmount, names?.Count ?? 0, names,
                a.BaseAgreementId,
                a.BaseAgreementId is { } bid ? baseAgreementNames.GetValueOrDefault(bid) : null,
                a.Modifiers.OrderBy(m => m.Sequence)
                    .Select(m => new PricingAgreementModifierDto(
                        m.Id, m.Sequence, m.Name, m.CountryCode, m.ZoneId,
                        m.ZoneId is { } zid ? zoneNames.GetValueOrDefault(zid) : null,
                        m.Percent, m.FixedAmount))
                    .ToList(),
                a.IncludedLoadingMinutes, a.IncludedUnloadingMinutes, a.IncludedCombinedMinutes, a.ExtraHourlyRate,
                a.SalesCategoryId,
                a.SalesCategoryId is { } scid ? salesCategoryNames.GetValueOrDefault(scid) : null);
        }).ToList();
    }

    // --- Pricing agreement assignments (shared tables → customers) ---

    public async Task<IReadOnlyList<PricingAgreementAssignmentDto>?> ListAssignmentsAsync(
        Guid agreementId, CancellationToken cancellationToken)
    {
        var agreement = await _dbContext.PricingAgreements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        var assignments = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        return await MapAssignmentsAsync(assignments, cancellationToken);
    }

    public async Task<IReadOnlyList<PricingAgreementAssignmentDto>?> SaveAssignmentsAsync(
        Guid agreementId, IReadOnlyList<SavePricingAssignmentRequest> requests, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var agreement = await _dbContext.PricingAgreements
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == agreementId, cancellationToken);
        if (agreement is null)
        {
            return null;
        }

        if (!agreement.IsShared)
        {
            throw new DomainValidationException("agreementId",
                "Klantkoppelingen zijn alleen mogelijk op herbruikbare tabellen.");
        }

        foreach (var request in requests)
        {
            if (request.PercentAdjustment is < -100 or > 100)
            {
                throw new DomainValidationException("percentAdjustment", "De aanpassing moet tussen -100% en 100% liggen.");
            }

            if (request.EffectiveFrom is { } from && request.EffectiveUntil is { } until && until < from)
            {
                throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
            }

            if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == tenantId && c.Id == request.CustomerId, cancellationToken))
            {
                throw new InvalidTenantReferenceException("klant");
            }
        }

        foreach (var group in requests.GroupBy(r => r.CustomerId))
        {
            var windows = group.ToList();
            for (var i = 0; i < windows.Count; i++)
            {
                for (var j = i + 1; j < windows.Count; j++)
                {
                    if (WindowsOverlap(windows[i], windows[j]))
                    {
                        throw new DomainValidationException("assignments",
                            "Deze klant heeft al een actieve koppeling in deze periode.");
                    }
                }
            }
        }

        var existing = await _dbContext.PricingAgreementAssignments
            .Where(x => x.TenantId == tenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        var oldSnapshot = existing
            .Select(x => new { x.CustomerId, x.PercentAdjustment, x.FixedAdjustment, x.EffectiveFrom, x.EffectiveUntil })
            .ToList();
        _dbContext.PricingAgreementAssignments.RemoveRange(existing);
        foreach (var request in requests)
        {
            _dbContext.PricingAgreementAssignments.Add(new PricingAgreementAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgreementId = agreementId,
                CustomerId = request.CustomerId,
                PercentAdjustment = request.PercentAdjustment,
                FixedAdjustment = request.FixedAdjustment,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveUntil = request.EffectiveUntil,
                Notes = Clean(request.Notes),
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PricingAgreementAssignment", agreementId.ToString(), "saved",
            oldSnapshot, requests, cancellationToken);

        var saved = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AgreementId == agreementId)
            .ToListAsync(cancellationToken);
        return await MapAssignmentsAsync(saved, cancellationToken);
    }

    /// <summary>Null-open windows treated as -/+ infinity; overlap = the two ranges intersect.</summary>
    private static bool WindowsOverlap(SavePricingAssignmentRequest a, SavePricingAssignmentRequest b)
    {
        var aFrom = a.EffectiveFrom ?? DateOnly.MinValue;
        var aTo = a.EffectiveUntil ?? DateOnly.MaxValue;
        var bFrom = b.EffectiveFrom ?? DateOnly.MinValue;
        var bTo = b.EffectiveUntil ?? DateOnly.MaxValue;
        return aFrom <= bTo && bFrom <= aTo;
    }

    private async Task<IReadOnlyList<PricingAgreementAssignmentDto>> MapAssignmentsAsync(
        IReadOnlyList<PricingAgreementAssignment> assignments, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = assignments.Select(x => x.CustomerId).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return assignments
            .Select(x => new PricingAgreementAssignmentDto(
                x.Id, x.CustomerId, customers.GetValueOrDefault(x.CustomerId, "?"),
                x.PercentAdjustment, x.FixedAdjustment, x.EffectiveFrom, x.EffectiveUntil, x.Notes))
            .OrderBy(x => x.CustomerName)
            .ToList();
    }

    // --- Price rules ---

    public async Task<IReadOnlyList<PriceRuleDto>> ListRulesAsync(Guid? customerId, CancellationToken cancellationToken, Guid? agreementId = null)
    {
        var query = _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets).Where(r => r.TenantId == TenantId);
        query = agreementId is { } aid
            ? query.Where(r => r.AgreementId == aid)
            : query.Where(r => customerId == null ? r.CustomerId == null : r.CustomerId == customerId);
        var rules = await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return await MapRulesAsync(rules, cancellationToken);
    }

    public async Task<PriceRuleDto> CreateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        await ValidateRuleAsync(request, cancellationToken);
        var rule = new PriceRule { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyRule(rule, request);
        _dbContext.PriceRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Created", null,
            new { rule.Name, rule.Basis, rule.CustomerId, rule.UnitTypeId }, cancellationToken);
        return (await MapRulesAsync([rule], cancellationToken))[0];
    }

    public async Task<PriceRuleDto?> UpdateRuleAsync(Guid id, SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.Include(r => r.Brackets)
            .FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == id, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        await ValidateRuleAsync(request, cancellationToken);
        _dbContext.PriceRuleBrackets.RemoveRange(rule.Brackets);
        rule.Brackets.Clear();
        ApplyRule(rule, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Updated", null, new { rule.Name, rule.Basis }, cancellationToken);
        return (await MapRulesAsync([rule], cancellationToken))[0];
    }

    public async Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == id, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        _dbContext.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRule", rule.Id.ToString(), "Deleted", new { rule.Name }, null, cancellationToken);
        return true;
    }

    // --- Bracket-row customer overrides ("klantafwijkingen") ---

    public async Task<IReadOnlyList<PriceRuleBracketOverrideDto>?> ListBracketOverridesAsync(
        Guid ruleId, Guid? customerId, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets)
            .FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        var overrides = await _dbContext.PriceRuleBracketOverrides.AsNoTracking()
            .Where(o => o.TenantId == TenantId && o.PriceRuleId == ruleId
                        && (customerId == null || o.CustomerId == customerId))
            .OrderBy(o => o.FromQuantity).ThenBy(o => o.EffectiveFrom)
            .ToListAsync(cancellationToken);
        return await MapBracketOverridesAsync(rule, overrides, cancellationToken);
    }

    public async Task<PriceRuleBracketOverrideDto?> CreateBracketOverrideAsync(
        Guid ruleId, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken)
    {
        var rule = await _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets)
            .FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        await ValidateBracketOverrideAsync(rule, request, existingId: null, cancellationToken);
        var entity = new PriceRuleBracketOverride { Id = Guid.NewGuid(), TenantId = TenantId, PriceRuleId = ruleId };
        ApplyBracketOverride(entity, request);
        _dbContext.PriceRuleBracketOverrides.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRuleBracketOverride", entity.Id.ToString(), "Created", null,
            new { RuleName = rule.Name, entity.CustomerId, entity.FromQuantity, entity.ToQuantity, entity.Price }, cancellationToken);
        return (await MapBracketOverridesAsync(rule, [entity], cancellationToken))[0];
    }

    public async Task<PriceRuleBracketOverrideDto?> UpdateBracketOverrideAsync(
        Guid id, SavePriceRuleBracketOverrideRequest request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.PriceRuleBracketOverrides
            .FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var rule = await _dbContext.PriceRules.AsNoTracking().Include(r => r.Brackets)
            .FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == entity.PriceRuleId, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        await ValidateBracketOverrideAsync(rule, request, existingId: id, cancellationToken);
        var oldValues = new { entity.CustomerId, entity.FromQuantity, entity.ToQuantity, entity.Price, entity.PricePerExtraUnit };
        ApplyBracketOverride(entity, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRuleBracketOverride", entity.Id.ToString(), "Updated", oldValues,
            new { entity.CustomerId, entity.FromQuantity, entity.ToQuantity, entity.Price, entity.PricePerExtraUnit }, cancellationToken);
        return (await MapBracketOverridesAsync(rule, [entity], cancellationToken))[0];
    }

    public async Task<bool> DeleteBracketOverrideAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.PriceRuleBracketOverrides
            .FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("PriceRuleBracketOverride", entity.Id.ToString(), "Deleted",
            new { entity.CustomerId, entity.FromQuantity, entity.ToQuantity, entity.Price }, null, cancellationToken);
        return true;
    }

    private async Task ValidateBracketOverrideAsync(
        PriceRule rule, SavePriceRuleBracketOverrideRequest request, Guid? existingId, CancellationToken cancellationToken)
    {
        if (rule.CustomerId is not null)
        {
            throw new DomainValidationException(
                "Klantafwijkingen zijn enkel mogelijk op algemene of gedeelde tariefregels, niet op klantspecifieke regels.");
        }

        if (rule.Basis is not (PriceRuleBasis.QuantityBracket or PriceRuleBasis.WeightBracket))
        {
            throw new DomainValidationException("Klantafwijkingen zijn enkel mogelijk op staffelregels.");
        }

        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == request.CustomerId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        if (request.Price < 0)
        {
            throw new DomainValidationException("price", "De prijs mag niet negatief zijn.");
        }

        if (request.PricePerExtraUnit is < 0)
        {
            throw new DomainValidationException("pricePerExtraUnit", "De prijs per extra eenheid mag niet negatief zijn.");
        }

        if (request.EffectiveFrom is { } from && request.EffectiveUntil is { } until && until < from)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
        }

        // The override must target an EXISTING bracket row by exact value identity.
        var rowExists = rule.Brackets.Any(b => BracketRowMatches(b, request));
        if (!rowExists)
        {
            throw new DomainValidationException(
                "fromQuantity", "Deze staffelrij bestaat niet (meer) in de regel. Kies een bestaande rij.");
        }

        // No two overrides for the same customer + row with overlapping validity (never let an
        // ambiguous override silently win).
        var siblings = await _dbContext.PriceRuleBracketOverrides.AsNoTracking()
            .Where(o => o.TenantId == TenantId && o.PriceRuleId == rule.Id && o.CustomerId == request.CustomerId
                        && (existingId == null || o.Id != existingId))
            .ToListAsync(cancellationToken);
        var overlapping = siblings.Any(o =>
            o.FromQuantity == request.FromQuantity && o.ToQuantity == request.ToQuantity
            && o.WeightToKg == request.WeightToKg && o.VolumeToM3 == request.VolumeToM3
            && o.LoadingMetersTo == request.LoadingMetersTo
            && WindowsOverlap(o.EffectiveFrom, o.EffectiveUntil, request.EffectiveFrom, request.EffectiveUntil));
        if (overlapping)
        {
            throw new DomainValidationException(
                "Er bestaat al een klantafwijking voor deze staffelrij in (een deel van) deze periode.");
        }
    }

    private static bool WindowsOverlap(DateOnly? fromA, DateOnly? untilA, DateOnly? fromB, DateOnly? untilB)
        => (fromA ?? DateOnly.MinValue) <= (untilB ?? DateOnly.MaxValue)
           && (fromB ?? DateOnly.MinValue) <= (untilA ?? DateOnly.MaxValue);

    private static bool BracketRowMatches(PriceRuleBracket bracket, SavePriceRuleBracketOverrideRequest request)
        => bracket.FromQuantity == request.FromQuantity && bracket.ToQuantity == request.ToQuantity
           && bracket.WeightToKg == request.WeightToKg && bracket.VolumeToM3 == request.VolumeToM3
           && bracket.LoadingMetersTo == request.LoadingMetersTo;

    private static void ApplyBracketOverride(PriceRuleBracketOverride entity, SavePriceRuleBracketOverrideRequest request)
    {
        entity.CustomerId = request.CustomerId;
        entity.FromQuantity = request.FromQuantity;
        entity.ToQuantity = request.ToQuantity;
        entity.WeightToKg = request.WeightToKg;
        entity.VolumeToM3 = request.VolumeToM3;
        entity.LoadingMetersTo = request.LoadingMetersTo;
        entity.Price = request.Price;
        entity.PricePerExtraUnit = request.PricePerExtraUnit;
        entity.EffectiveFrom = request.EffectiveFrom;
        entity.EffectiveUntil = request.EffectiveUntil;
        entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private async Task<IReadOnlyList<PriceRuleBracketOverrideDto>> MapBracketOverridesAsync(
        PriceRule rule, IReadOnlyList<PriceRuleBracketOverride> overrides, CancellationToken cancellationToken)
    {
        var customerIds = overrides.Select(o => o.CustomerId).Distinct().ToList();
        var customerNames = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == TenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return overrides.Select(o => new PriceRuleBracketOverrideDto(
            o.Id, o.PriceRuleId, o.CustomerId, customerNames.GetValueOrDefault(o.CustomerId, "?"),
            o.FromQuantity, o.ToQuantity, o.WeightToKg, o.VolumeToM3, o.LoadingMetersTo,
            o.Price, o.PricePerExtraUnit, o.EffectiveFrom, o.EffectiveUntil, o.Notes,
            Orphaned: !rule.Brackets.Any(b =>
                b.FromQuantity == o.FromQuantity && b.ToQuantity == o.ToQuantity
                && b.WeightToKg == o.WeightToKg && b.VolumeToM3 == o.VolumeToM3
                && b.LoadingMetersTo == o.LoadingMetersTo))).ToList();
    }

    // --- Service options ---

    public async Task<IReadOnlyList<ServiceOptionDto>> ListServiceOptionsAsync(
        bool includeInactive, bool forOrderEntry, CancellationToken cancellationToken)
    {
        var options = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == TenantId && (includeInactive || o.IsActive))
            .Where(o => !forOrderEntry || o.SelectableInOrders)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(cancellationToken);
        return await MapOptionsAsync(options, cancellationToken);
    }

    public async Task<ServiceOptionDto> CreateServiceOptionAsync(SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        await ValidateOptionAsync(request, cancellationToken);
        var duplicate = await _dbContext.ServiceOptions.AnyAsync(
            o => o.TenantId == TenantId && o.Code == request.Code.Trim().ToUpperInvariant(), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een dienst met code '{request.Code}'.");
        }

        var option = new ServiceOption { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyOption(option, request);
        _dbContext.ServiceOptions.Add(option);
        await ApplyWarehouseConditionsAsync(option, request.WarehouseIds, cancellationToken);
        await ApplyTimeConditionsAsync(option, request.TimeConditions, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Created", null,
            new
            {
                option.Code, option.Name, option.Kind, WarehouseIds = request.WarehouseIds ?? [],
                TimeConditions = request.TimeConditions ?? [],
            }, cancellationToken);
        return await MapOptionAsync(option, cancellationToken);
    }

    public async Task<ServiceOptionDto?> UpdateServiceOptionAsync(Guid id, SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        var option = await _dbContext.ServiceOptions.FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (option is null)
        {
            return null;
        }

        await ValidateOptionAsync(request, cancellationToken);
        ApplyOption(option, request);
        await ApplyWarehouseConditionsAsync(option, request.WarehouseIds, cancellationToken);
        await ApplyTimeConditionsAsync(option, request.TimeConditions, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Updated", null,
            new
            {
                option.Code, option.Name, option.Kind, WarehouseIds = request.WarehouseIds ?? [],
                TimeConditions = request.TimeConditions ?? [],
            }, cancellationToken);
        return await MapOptionAsync(option, cancellationToken);
    }

    public async Task<bool> DeleteServiceOptionAsync(Guid id, CancellationToken cancellationToken)
    {
        var option = await _dbContext.ServiceOptions.FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == id, cancellationToken);
        if (option is null)
        {
            return false;
        }

        _dbContext.Remove(option);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("ServiceOption", option.Id.ToString(), "Deleted", new { option.Code }, null, cancellationToken);
        return true;
    }

    // --- Tenant holidays (Wave 3 §4) ---

    public async Task<IReadOnlyList<TenantHolidayDto>> ListHolidaysAsync(CancellationToken cancellationToken) =>
        await _dbContext.TenantHolidays.AsNoTracking()
            .Where(h => h.TenantId == TenantId)
            .OrderBy(h => h.Date)
            .Select(h => new TenantHolidayDto(h.Id, h.Date, h.Name))
            .ToListAsync(cancellationToken);

    public async Task<TenantHolidayDto> CreateHolidayAsync(SaveTenantHolidayRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam van de feestdag is verplicht.");
        }

        if (await _dbContext.TenantHolidays.AnyAsync(
                h => h.TenantId == TenantId && h.Date == request.Date, cancellationToken))
        {
            throw new DomainValidationException("date", "Voor deze datum bestaat al een feestdag.");
        }

        var holiday = new TenantHoliday
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Date = request.Date, Name = request.Name.Trim(),
        };
        _dbContext.TenantHolidays.Add(holiday);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TenantHoliday", holiday.Id.ToString(), "Created", null,
            new { holiday.Date, holiday.Name }, cancellationToken);
        return new TenantHolidayDto(holiday.Id, holiday.Date, holiday.Name);
    }

    public async Task<bool> DeleteHolidayAsync(Guid id, CancellationToken cancellationToken)
    {
        var holiday = await _dbContext.TenantHolidays.FirstOrDefaultAsync(
            h => h.TenantId == TenantId && h.Id == id, cancellationToken);
        if (holiday is null)
        {
            return false;
        }

        _dbContext.Remove(holiday);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TenantHoliday", holiday.Id.ToString(), "Deleted",
            new { holiday.Date, holiday.Name }, null, cancellationToken);
        return true;
    }

    // --- Customer pricing configuration ---

    public async Task<CustomerPricingConfigDto?> GetCustomerConfigAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            return null;
        }

        var preferred = await _dbContext.CustomerPreferredUnits.AsNoTracking()
            .Where(u => u.TenantId == TenantId && u.CustomerId == customerId)
            .OrderByDescending(u => u.IsFavourite).ThenBy(u => u.SortOrder)
            .Join(_dbContext.UnitTypes.Where(t => t.TenantId == TenantId),
                pu => pu.UnitTypeId, ut => ut.Id,
                (pu, ut) => new CustomerPreferredUnitDto(
                    ut.Id, ut.Code, ut.Name, pu.SortOrder,
                    pu.CustomerLabel, pu.EdiCode, pu.ExcelCode, pu.IsFavourite))
            .ToListAsync(cancellationToken);

        var options = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == TenantId && o.IsActive)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(cancellationToken);
        var overrides = await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
            .Where(p => p.TenantId == TenantId && p.CustomerId == customerId)
            .ToDictionaryAsync(p => p.ServiceOptionId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var optionDtos = options
            .Select(o =>
            {
                var over = overrides.GetValueOrDefault(o.Id);
                var overrideActiveToday = over is not null
                    && (over.EffectiveFrom is null || over.EffectiveFrom <= today)
                    && (over.EffectiveUntil is null || over.EffectiveUntil >= today);
                var effectiveValue = overrideActiveToday && over!.Value is { } v ? v : o.DefaultValue;
                var source = overrideActiveToday && (over!.Value is not null || over.Disabled)
                    ? "Klanttarief"
                    : "Algemene standaard";
                var effectiveAutoApply = (overrideActiveToday ? over!.AutoApplyOverride : null) ?? o.AutoApply;
                return new CustomerServiceOptionPriceDto(
                    o.Id, o.Name, o.Kind, o.DefaultValue, over?.Value,
                    over?.Disabled ?? false, over?.MinimumAmount, over?.InvoiceDescription,
                    over?.EffectiveFrom, over?.EffectiveUntil,
                    effectiveValue, source, over?.AutoApplyOverride, effectiveAutoApply);
            })
            .ToList();

        return new CustomerPricingConfigDto(preferred, optionDtos);
    }

    public async Task<CustomerPricingConfigDto?> SaveCustomerConfigAsync(
        Guid customerId, SaveCustomerPricingConfigRequest request, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            return null;
        }

        var units = request.Units;
        var unitIds = units.Select(u => u.UnitTypeId).Distinct().ToList();
        if (unitIds.Count != units.Count)
        {
            throw new DomainValidationException("units", "Eén eenheid mag maar één keer geconfigureerd worden.");
        }

        var knownUnits = await _dbContext.UnitTypes
            .CountAsync(u => u.TenantId == TenantId && unitIds.Contains(u.Id), cancellationToken);
        if (knownUnits != unitIds.Count)
        {
            throw new DomainValidationException("units", "Eén of meer eenheden bestaan niet.");
        }

        foreach (var unit in units)
        {
            if (unit.CustomerLabel is { Length: > 150 } || unit.EdiCode is { Length: > 50 } || unit.ExcelCode is { Length: > 50 })
            {
                throw new DomainValidationException("units", "Klantbenaming of externe code is te lang.");
            }
        }

        var optionIds = request.OptionPrices.Select(p => p.ServiceOptionId).Distinct().ToList();
        if (optionIds.Count > 0)
        {
            var knownOptions = await _dbContext.ServiceOptions
                .CountAsync(o => o.TenantId == TenantId && optionIds.Contains(o.Id), cancellationToken);
            if (knownOptions != optionIds.Count)
            {
                throw new DomainValidationException("optionPrices", "Eén of meer diensten bestaan niet.");
            }
        }

        var existingPreferred = await _dbContext.CustomerPreferredUnits
            .Where(u => u.TenantId == TenantId && u.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        _dbContext.CustomerPreferredUnits.RemoveRange(existingPreferred.Where(u => !unitIds.Contains(u.UnitTypeId)));
        foreach (var unit in units)
        {
            var row = existingPreferred.FirstOrDefault(u => u.UnitTypeId == unit.UnitTypeId);
            if (row is null)
            {
                row = new CustomerPreferredUnit
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, CustomerId = customerId,
                    UnitTypeId = unit.UnitTypeId,
                };
                _dbContext.CustomerPreferredUnits.Add(row);
            }

            row.SortOrder = unit.SortOrder;
            row.CustomerLabel = Clean(unit.CustomerLabel);
            row.EdiCode = Clean(unit.EdiCode);
            row.ExcelCode = Clean(unit.ExcelCode);
            row.IsFavourite = unit.IsFavourite;
        }

        var existingPrices = await _dbContext.CustomerServiceOptionPrices
            .Where(p => p.TenantId == TenantId && p.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        foreach (var priceRequest in request.OptionPrices)
        {
            var row = existingPrices.FirstOrDefault(p => p.ServiceOptionId == priceRequest.ServiceOptionId);
            var isOverride = priceRequest.Value is not null || priceRequest.Disabled
                             || priceRequest.MinimumAmount is not null
                             || !string.IsNullOrWhiteSpace(priceRequest.InvoiceDescription)
                             || priceRequest.EffectiveFrom is not null || priceRequest.EffectiveUntil is not null
                             || priceRequest.AutoApplyOverride is not null;
            if (!isOverride)
            {
                if (row is not null)
                {
                    _dbContext.Remove(row); // "Algemene waarde opnieuw gebruiken"
                }

                continue;
            }

            if (row is null)
            {
                row = new CustomerServiceOptionPrice
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, CustomerId = customerId,
                    ServiceOptionId = priceRequest.ServiceOptionId,
                };
                _dbContext.CustomerServiceOptionPrices.Add(row);
            }

            row.Value = priceRequest.Value;
            row.Disabled = priceRequest.Disabled;
            row.MinimumAmount = priceRequest.MinimumAmount;
            row.InvoiceDescription = Clean(priceRequest.InvoiceDescription);
            row.EffectiveFrom = priceRequest.EffectiveFrom;
            row.EffectiveUntil = priceRequest.EffectiveUntil;
            row.AutoApplyOverride = priceRequest.AutoApplyOverride;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("CustomerPricingConfig", customerId.ToString(), "Updated", null,
            new { PreferredUnits = unitIds.Count, OptionPrices = request.OptionPrices.Count }, cancellationToken);
        return await GetCustomerConfigAsync(customerId, cancellationToken);
    }

    // --- Combined-unit degression discounts (spec §29-31) ---

    public async Task<IReadOnlyList<CombinedUnitDiscountDto>> ListCombinedDiscountsAsync(
        Guid? customerId, Guid? agreementId, CancellationToken cancellationToken)
    {
        var query = _dbContext.CombinedUnitDiscounts.AsNoTracking()
            .Include(d => d.Units).Include(d => d.Tiers)
            .Where(d => d.TenantId == TenantId);
        if (customerId is { } cid)
        {
            query = query.Where(d => d.CustomerId == cid);
        }

        if (agreementId is { } aid)
        {
            query = query.Where(d => d.AgreementId == aid);
        }

        var discounts = await query.OrderBy(d => d.Name).ToListAsync(cancellationToken);
        return await MapCombinedDiscountsAsync(discounts, cancellationToken);
    }

    public async Task<CombinedUnitDiscountDto> CreateCombinedDiscountAsync(
        SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken)
    {
        await ValidateCombinedDiscountAsync(request, null, cancellationToken);
        var discount = new CombinedUnitDiscount { Id = Guid.NewGuid(), TenantId = TenantId };
        ApplyCombinedDiscount(discount, request);
        _dbContext.CombinedUnitDiscounts.Add(discount);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("CombinedUnitDiscount", discount.Id.ToString(), "Created", null,
            new { discount.Name, discount.CustomerId, discount.AgreementId, discount.Scope }, cancellationToken);
        return (await MapCombinedDiscountsAsync([discount], cancellationToken))[0];
    }

    public async Task<CombinedUnitDiscountDto?> UpdateCombinedDiscountAsync(
        Guid id, SaveCombinedUnitDiscountRequest request, CancellationToken cancellationToken)
    {
        var discount = await _dbContext.CombinedUnitDiscounts.Include(d => d.Units).Include(d => d.Tiers)
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == id, cancellationToken);
        if (discount is null)
        {
            return null;
        }

        await ValidateCombinedDiscountAsync(request, id, cancellationToken);
        // Full-graph replace: units/tiers are always rewritten wholesale, same pattern as agreement surcharges/modifiers.
        _dbContext.RemoveRange(discount.Units);
        discount.Units = [];
        _dbContext.RemoveRange(discount.Tiers);
        discount.Tiers = [];
        ApplyCombinedDiscount(discount, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("CombinedUnitDiscount", discount.Id.ToString(), "Updated", null,
            new { discount.Name, discount.CustomerId, discount.AgreementId, discount.Scope }, cancellationToken);
        return (await MapCombinedDiscountsAsync([discount], cancellationToken))[0];
    }

    public async Task<bool> DeleteCombinedDiscountAsync(Guid id, CancellationToken cancellationToken)
    {
        var discount = await _dbContext.CombinedUnitDiscounts.FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == id, cancellationToken);
        if (discount is null)
        {
            return false;
        }

        _dbContext.Remove(discount);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("CombinedUnitDiscount", discount.Id.ToString(), "Deleted", new { discount.Name }, null, cancellationToken);
        return true;
    }

    private async Task ValidateCombinedDiscountAsync(
        SaveCombinedUnitDiscountRequest request, Guid? discountId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (request.EffectiveUntil is { } until && until < request.EffectiveFrom)
        {
            throw new DomainValidationException("effectiveUntil", "De einddatum ligt vóór de begindatum.");
        }

        if (request.Units is null || request.Units.Count == 0)
        {
            throw new DomainValidationException("units", "Kies minstens één eenheid.");
        }

        if (request.Tiers is null || request.Tiers.Count == 0)
        {
            throw new DomainValidationException("tiers", "Voeg minstens één staffel toe.");
        }

        var unitTypeIds = request.Units.Select(u => u.UnitTypeId).ToList();
        if (unitTypeIds.Distinct().Count() != unitTypeIds.Count)
        {
            throw new DomainValidationException("units", "Elke eenheid mag maar één keer voorkomen.");
        }

        foreach (var unit in request.Units)
        {
            if (unit.EquivalentFactor <= 0)
            {
                throw new DomainValidationException("units", "De factor moet groter zijn dan 0.");
            }
        }

        var knownUnits = await _dbContext.UnitTypes.CountAsync(
            u => u.TenantId == TenantId && unitTypeIds.Contains(u.Id), cancellationToken);
        if (knownUnits != unitTypeIds.Distinct().Count())
        {
            throw new InvalidTenantReferenceException("eenheid");
        }

        foreach (var tier in request.Tiers)
        {
            if (tier.FromCount < 0)
            {
                throw new DomainValidationException("tiers", "De van-waarde mag niet negatief zijn.");
            }

            if (tier.ToCount is { } to && to < tier.FromCount)
            {
                throw new DomainValidationException("tiers", "De van-waarde moet vóór de tot-waarde liggen.");
            }

            if (tier.Percent <= 0 || tier.Percent > 100)
            {
                throw new DomainValidationException("tiers", "De korting moet tussen 0 en 100% liggen.");
            }
        }

        for (var i = 0; i < request.Tiers.Count; i++)
        {
            for (var j = i + 1; j < request.Tiers.Count; j++)
            {
                var aTo = request.Tiers[i].ToCount ?? decimal.MaxValue;
                var bTo = request.Tiers[j].ToCount ?? decimal.MaxValue;
                if (request.Tiers[i].FromCount <= bTo && request.Tiers[j].FromCount <= aTo)
                {
                    throw new DomainValidationException("tiers", "De staffels mogen elkaar niet overlappen.");
                }
            }
        }

        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.TenantId == TenantId && c.Id == customerId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        if (request.AgreementId is { } agreementId
            && !await _dbContext.PricingAgreements.AnyAsync(a => a.TenantId == TenantId && a.Id == agreementId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("prijsafspraak");
        }
    }

    private void ApplyCombinedDiscount(CombinedUnitDiscount discount, SaveCombinedUnitDiscountRequest request)
    {
        discount.CustomerId = request.CustomerId;
        discount.AgreementId = request.AgreementId;
        discount.Name = request.Name.Trim();
        discount.Scope = request.Scope;
        discount.EffectiveFrom = request.EffectiveFrom;
        discount.EffectiveUntil = request.EffectiveUntil;
        discount.IsActive = request.IsActive;

        foreach (var unit in request.Units)
        {
            var entity = new CombinedUnitDiscountUnit
            {
                Id = Guid.NewGuid(), TenantId = TenantId, DiscountId = discount.Id,
                UnitTypeId = unit.UnitTypeId, EquivalentFactor = unit.EquivalentFactor,
            };
            discount.Units.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }

        foreach (var tier in request.Tiers)
        {
            var entity = new CombinedUnitDiscountTier
            {
                Id = Guid.NewGuid(), TenantId = TenantId, DiscountId = discount.Id,
                FromCount = tier.FromCount, ToCount = tier.ToCount, Percent = tier.Percent,
            };
            discount.Tiers.Add(entity);
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private async Task<IReadOnlyList<CombinedUnitDiscountDto>> MapCombinedDiscountsAsync(
        IReadOnlyList<CombinedUnitDiscount> discounts, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = discounts.Where(d => d.CustomerId.HasValue).Select(d => d.CustomerId!.Value).Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var agreementIds = discounts.Where(d => d.AgreementId.HasValue).Select(d => d.AgreementId!.Value).Distinct().ToList();
        var agreements = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == tenantId && agreementIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var unitTypeIds = discounts.SelectMany(d => d.Units).Select(u => u.UnitTypeId).Distinct().ToList();
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitTypeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        return discounts.Select(d => new CombinedUnitDiscountDto(
            d.Id, d.CustomerId, d.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null,
            d.AgreementId, d.AgreementId is { } aid ? agreements.GetValueOrDefault(aid) : null,
            d.Name, d.Scope, d.EffectiveFrom, d.EffectiveUntil, d.IsActive,
            d.Units.OrderBy(u => u.Id)
                .Select(u => new CombinedUnitDiscountUnitDto(u.Id, u.UnitTypeId, unitNames.GetValueOrDefault(u.UnitTypeId), u.EquivalentFactor))
                .ToList(),
            d.Tiers.OrderBy(t => t.FromCount)
                .Select(t => new CombinedUnitDiscountTierDto(t.Id, t.FromCount, t.ToCount, t.Percent))
                .ToList()))
            .ToList();
    }

    // --- Helpers ---

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateZone(SavePricingZoneRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("code", "Code en naam zijn verplicht.");
        }

        foreach (var area in request.Areas)
        {
            if (string.IsNullOrWhiteSpace(area.PostalCodeFrom) || string.IsNullOrWhiteSpace(area.PostalCodeTo))
            {
                throw new DomainValidationException("areas", "Elke postcodereeks heeft een van- en tot-waarde nodig.");
            }

            if (int.TryParse(area.PostalCodeFrom, out var from) && int.TryParse(area.PostalCodeTo, out var to) && from > to)
            {
                throw new DomainValidationException("areas", "De van-postcode moet vóór de tot-postcode liggen.");
            }
        }
    }

    private async Task EnsureZoneCodeFreeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var duplicate = await _dbContext.PricingZones.AnyAsync(
            z => z.TenantId == TenantId && z.Code == normalized && z.Id != exceptId, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("code", $"Er bestaat al een zone met code '{normalized}'.");
        }
    }

    private void ApplyZone(PricingZone zone, SavePricingZoneRequest request)
    {
        zone.Code = request.Code.Trim().ToUpperInvariant();
        zone.Name = request.Name.Trim();
        zone.IsActive = request.IsActive;
        zone.SortOrder = request.SortOrder;
        foreach (var area in request.Areas)
        {
            var entity = new PricingZoneArea
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ZoneId = zone.Id,
                CountryCode = area.CountryCode.Trim().ToUpperInvariant(),
                PostalCodeFrom = area.PostalCodeFrom.Trim(),
                PostalCodeTo = area.PostalCodeTo.Trim(),
            };
            zone.Areas.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private async Task ValidateRuleAsync(SavePriceRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        await ValidateSalesCategoryAsync(request.SalesCategoryId, cancellationToken);

        if (request.OriginZoneId is { } originZoneId
            && !await _dbContext.PricingZones.AnyAsync(
                z => z.TenantId == TenantId && z.Id == originZoneId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("zone van herkomst");
        }

        var orderMeasureBasis = request.Basis
            is PriceRuleBasis.Fixed or PriceRuleBasis.PerKm or PriceRuleBasis.PerPallet or PriceRuleBasis.PerTon
            or PriceRuleBasis.PerLoadingMeter or PriceRuleBasis.PerVolume or PriceRuleBasis.PerStop;
        if (request.UnitTypeId is null && !orderMeasureBasis && request.Basis != PriceRuleBasis.WeightBracket)
        {
            throw new DomainValidationException("unitTypeId", "Kies een eenheid (alleen order-brede regels kunnen zonder).");
        }

        if (request.MinimumQuantity is < 0 || request.QuantityRoundingStep is < 0)
        {
            throw new DomainValidationException("minimumQuantity", "Minimumduur en afrondingsstap mogen niet negatief zijn.");
        }

        if (request.Priority is < -1000 or > 1000)
        {
            throw new DomainValidationException("priority", "Prioriteit moet tussen -1000 en 1000 liggen.");
        }

        if (request.BaseAmount is < 0)
        {
            throw new DomainValidationException("baseAmount", "Het basisbedrag mag niet negatief zijn.");
        }

        if (request.MaximumAmount is <= 0)
        {
            throw new DomainValidationException("maximumAmount", "Het maximumtarief moet groter zijn dan nul.");
        }

        if (request.MinimumAmount is { } ruleMinimum && request.MaximumAmount is { } ruleMaximum && ruleMinimum > ruleMaximum)
        {
            throw new DomainValidationException("maximumAmount", "Minimumtarief kan niet hoger zijn dan het maximumtarief.");
        }

        var hasOversize = request.OversizeLengthCm is not null || request.OversizeWidthCm is not null
                          || request.OversizeBillableFactor is not null;
        if (hasOversize)
        {
            if (request.OversizeBillableFactor is null or < 1)
            {
                throw new DomainValidationException("oversizeBillableFactor",
                    "Geef aan voor hoeveel factureerbare eenheden een buitenmaat telt (minstens 1).");
            }

            if (request.OversizeLengthCm is null && request.OversizeWidthCm is null)
            {
                throw new DomainValidationException("oversizeLengthCm",
                    "Geef minstens één buitenmaat-drempel (lengte of breedte) op.");
            }

            if (request.OversizeLengthCm is < 0 || request.OversizeWidthCm is < 0)
            {
                throw new DomainValidationException("oversizeLengthCm", "Een buitenmaat-drempel mag niet negatief zijn.");
            }
        }

        var usesBrackets = request.Basis is PriceRuleBasis.QuantityBracket or PriceRuleBasis.WeightBracket;
        var providedBrackets = request.Brackets ?? [];
        if (usesBrackets && providedBrackets.Count == 0)
        {
            throw new DomainValidationException("brackets", "Een staffelregel heeft minstens één staffel nodig.");
        }

        if (providedBrackets.Count > 0)
        {
            foreach (var bracket in providedBrackets)
            {
                if (bracket.ToQuantity is { } to && to < bracket.FromQuantity)
                {
                    throw new DomainValidationException("brackets", "Een staffel eindigt niet vóór hij begint.");
                }

                if (bracket.WeightToKg is <= 0 || bracket.VolumeToM3 is <= 0 || bracket.LoadingMetersTo is <= 0)
                {
                    throw new DomainValidationException("brackets", "Een staffelgrens (kg/m³/ldm) moet groter zijn dan nul.");
                }
            }

            // Two rows conflict only when BOTH their quantity ranges overlap AND they carry
            // identical dimension caps — a carrier table legitimately has several rows sharing a
            // quantity band (even "0 tot oneindig"), distinguished only by weight/volume/ldm caps.
            static bool QuantityOverlaps(SavePriceRuleBracketRequest a, SavePriceRuleBracketRequest b)
            {
                var aTo = a.ToQuantity ?? decimal.MaxValue;
                var bTo = b.ToQuantity ?? decimal.MaxValue;
                return a.FromQuantity <= bTo && b.FromQuantity <= aTo;
            }

            static bool SameCaps(SavePriceRuleBracketRequest a, SavePriceRuleBracketRequest b) =>
                a.WeightToKg == b.WeightToKg && a.VolumeToM3 == b.VolumeToM3 && a.LoadingMetersTo == b.LoadingMetersTo;

            for (var i = 0; i < providedBrackets.Count; i++)
            {
                for (var j = i + 1; j < providedBrackets.Count; j++)
                {
                    if (QuantityOverlaps(providedBrackets[i], providedBrackets[j]) && SameCaps(providedBrackets[i], providedBrackets[j]))
                    {
                        throw new DomainValidationException("brackets", "Staffels mogen elkaar niet overlappen.");
                    }
                }
            }

            if (request.BracketMode == BracketSelectionMode.PerNextUnit)
            {
                if (request.Basis != PriceRuleBasis.QuantityBracket)
                {
                    throw new DomainValidationException("bracketMode",
                        "Prijs per volgende eenheid is alleen mogelijk bij een staffel op aantal.");
                }

                var ordered = providedBrackets.OrderBy(b => b.FromQuantity).ToList();
                var gapless = ordered[0].FromQuantity == 1;
                for (var i = 1; gapless && i < ordered.Count; i++)
                {
                    gapless = ordered[i - 1].ToQuantity is { } previousTo && ordered[i].FromQuantity == previousTo + 1;
                }

                if (!gapless)
                {
                    throw new DomainValidationException("brackets",
                        "Bij prijs per volgende eenheid moeten de staffels aansluiten vanaf 1.");
                }
            }
        }
        else if (!usesBrackets && request.UnitPrice is null)
        {
            throw new DomainValidationException("unitPrice", "Geef een prijs op.");
        }

        var tenantId = TenantId;
        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }

        if (request.UnitTypeId is { } unitTypeId
            && !await _dbContext.UnitTypes.AnyAsync(u => u.Id == unitTypeId && u.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("eenheid");
        }

        if (request.ZoneId is { } zoneId
            && !await _dbContext.PricingZones.AnyAsync(z => z.Id == zoneId && z.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("zone");
        }

        if (request.AgreementId is { } agreementId)
        {
            var agreement = await _dbContext.PricingAgreements.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == agreementId && a.TenantId == tenantId, cancellationToken);
            if (agreement is null)
            {
                throw new InvalidTenantReferenceException("prijsafspraak");
            }

            // A customer rule cannot live inside another customer's agreement.
            if (agreement.CustomerId is { } agreementCustomer && request.CustomerId != agreementCustomer)
            {
                throw new DomainValidationException("agreementId",
                    "De regel hoort bij een andere klant dan de prijsafspraak.");
            }

            // A derived table has no rules of its own — it reuses its base-chain root's rules.
            if (agreement.BaseAgreementId is not null)
            {
                throw new DomainValidationException("agreementId", "Een afgeleide tabel kan geen eigen prijsregels hebben.");
            }
        }
    }

    private void ApplyRule(PriceRule rule, SavePriceRuleRequest request)
    {
        rule.CustomerId = request.CustomerId;
        rule.UnitTypeId = request.UnitTypeId;
        rule.Basis = request.Basis;
        rule.ZoneId = request.ZoneId;
        rule.Name = request.Name.Trim();
        rule.EffectiveFrom = request.EffectiveFrom;
        rule.EffectiveUntil = request.EffectiveUntil;
        rule.IsActive = request.IsActive;
        rule.UnitPrice = request.UnitPrice;
        rule.MinimumAmount = request.MinimumAmount;
        rule.MaximumAmount = request.MaximumAmount;
        rule.BracketMode = request.BracketMode;
        rule.AgreementId = request.AgreementId;
        rule.Priority = request.Priority;
        rule.BaseAmount = request.BaseAmount;
        rule.MinimumQuantity = request.MinimumQuantity;
        rule.QuantityRoundingStep = request.QuantityRoundingStep;
        rule.OversizeLengthCm = request.OversizeLengthCm;
        rule.OversizeWidthCm = request.OversizeWidthCm;
        rule.OversizeBillableFactor = request.OversizeBillableFactor;
        rule.SalesCategoryId = request.SalesCategoryId;
        rule.OriginZoneId = request.OriginZoneId;
        rule.ActivityTypeId = request.ActivityTypeId;
        foreach (var bracket in request.Brackets ?? [])
        {
            var entity = new PriceRuleBracket
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PriceRuleId = rule.Id,
                FromQuantity = bracket.FromQuantity,
                ToQuantity = bracket.ToQuantity,
                Price = bracket.Price,
                PricePerExtraUnit = bracket.PricePerExtraUnit,
                WeightToKg = bracket.WeightToKg,
                VolumeToM3 = bracket.VolumeToM3,
                LoadingMetersTo = bracket.LoadingMetersTo,
            };
            rule.Brackets.Add(entity);
            // Client-set Guid keys reached via a navigation are otherwise tracked as
            // existing (Modified) — mark them Added explicitly.
            _dbContext.Entry(entity).State = Microsoft.EntityFrameworkCore.EntityState.Added;
        }
    }

    private async Task ValidateOptionAsync(SaveServiceOptionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("code", "Code en naam zijn verplicht.");
        }

        await ValidateSalesCategoryAsync(request.SalesCategoryId, cancellationToken);

        if (request.DefaultValue < 0)
        {
            throw new DomainValidationException("defaultValue", "De standaardprijs mag niet negatief zijn.");
        }

        if (request.Kind == SurchargeKind.PerUnit)
        {
            if (request.UnitTypeId is null)
            {
                throw new DomainValidationException("unitTypeId", "Kies de eenheid waarop deze service telt.");
            }

            if (!await _dbContext.UnitTypes.AnyAsync(
                    u => u.TenantId == TenantId && u.Id == request.UnitTypeId && u.IsActive, cancellationToken))
            {
                throw new InvalidTenantReferenceException("eenheid");
            }
        }
        else if (request.UnitTypeId is not null)
        {
            throw new DomainValidationException("unitTypeId", "Een eenheid is alleen van toepassing bij 'per eenheid'.");
        }

        if (request.WarehouseIds is { Count: > 0 } warehouseIds)
        {
            var known = await _dbContext.Warehouses
                .CountAsync(w => w.TenantId == TenantId && warehouseIds.Contains(w.Id), cancellationToken);
            if (known != warehouseIds.Distinct().Count())
            {
                throw new InvalidTenantReferenceException("magazijn");
            }
        }

        // Wave 2026-08-04 §16: time conditions need their threshold; nothing is ever hardcoded.
        foreach (var condition in request.TimeConditions ?? [])
        {
            if (condition.Kind is ServiceConditionKind.StopTimeBefore or ServiceConditionKind.StopTimeAfter
                && condition.TimeOfDay is null)
            {
                throw new DomainValidationException("timeConditions", "Geef het uur van de tijdsvoorwaarde op.");
            }

            if (condition.Kind is ServiceConditionKind.Warehouse)
            {
                throw new DomainValidationException("timeConditions",
                    "Magazijnvoorwaarden worden via de magazijnselectie beheerd.");
            }

            // P6: an activity condition must say WHICH activity.
            if (condition.Kind is ServiceConditionKind.ActivityType && condition.ActivityTypeId is null)
            {
                throw new DomainValidationException("timeConditions",
                    "Kies het activiteitstype van de voorwaarde.");
            }
        }
    }

    /// <summary>
    /// Syncs the warehouse condition rows to exactly <paramref name="warehouseIds"/> (null/empty
    /// = no condition, applies to all orders). Add/remove per row — never delete-all-rewrite.
    /// </summary>
    private async Task ApplyWarehouseConditionsAsync(
        ServiceOption option, IReadOnlyList<Guid>? warehouseIds, CancellationToken cancellationToken)
    {
        var wanted = (warehouseIds ?? []).Distinct().ToHashSet();
        var existing = await _dbContext.ServiceOptionConditions
            .Where(c => c.TenantId == TenantId && c.ServiceOptionId == option.Id && c.Kind == ServiceConditionKind.Warehouse)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(existing.Where(c => !wanted.Contains(c.ReferenceId)));
        foreach (var id in wanted.Except(existing.Select(c => c.ReferenceId)))
        {
            _dbContext.ServiceOptionConditions.Add(new ServiceOptionCondition
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ServiceOptionId = option.Id,
                Kind = ServiceConditionKind.Warehouse, ReferenceId = id,
            });
        }
    }

    /// <summary>
    /// Wave 2026-08-04 §16: replaces the option's time-based condition rows with the requested
    /// list (value-matched add/remove — never delete-all-rewrite). Warehouse rows are untouched.
    /// </summary>
    private async Task ApplyTimeConditionsAsync(
        ServiceOption option, IReadOnlyList<ServiceTimeConditionDto>? timeConditions, CancellationToken cancellationToken)
    {
        var wanted = (timeConditions ?? [])
            .Select(c => (c.Kind, c.StopScope, c.TimeOfDay, c.Priority, c.AllowStacking, c.ActivityTypeId))
            .Distinct()
            .ToHashSet();
        var existing = await _dbContext.ServiceOptionConditions
            .Where(c => c.TenantId == TenantId && c.ServiceOptionId == option.Id && c.Kind != ServiceConditionKind.Warehouse)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(existing.Where(c =>
            !wanted.Contains((c.Kind, c.StopScope, c.TimeOfDay, c.Priority, c.AllowStacking, c.ActivityTypeId))));
        var kept = existing
            .Select(c => (c.Kind, c.StopScope, c.TimeOfDay, c.Priority, c.AllowStacking, c.ActivityTypeId))
            .ToHashSet();
        foreach (var row in wanted.Except(kept))
        {
            _dbContext.ServiceOptionConditions.Add(new ServiceOptionCondition
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ServiceOptionId = option.Id,
                Kind = row.Kind, StopScope = row.StopScope, TimeOfDay = row.TimeOfDay,
                Priority = row.Priority, AllowStacking = row.AllowStacking,
                ActivityTypeId = row.ActivityTypeId,
            });
        }
    }

    private void ApplyOption(ServiceOption option, SaveServiceOptionRequest request)
    {
        option.Code = request.Code.Trim().ToUpperInvariant();
        option.Name = request.Name.Trim();
        option.Kind = request.Kind;
        option.DefaultValue = request.DefaultValue;
        option.IsActive = request.IsActive;
        option.SortOrder = request.SortOrder;
        option.Description = Clean(request.Description);
        option.InvoiceDescription = Clean(request.InvoiceDescription);
        option.SelectableInOrders = request.SelectableInOrders;
        option.UnitTypeId = request.Kind == SurchargeKind.PerUnit ? request.UnitTypeId : null;
        option.AutoApply = request.AutoApply;
        option.OnlyForAdr = request.OnlyForAdr;
        option.SalesCategoryId = request.SalesCategoryId;
    }

    private async Task<ServiceOptionDto> MapOptionAsync(ServiceOption option, CancellationToken cancellationToken) =>
        (await MapOptionsAsync([option], cancellationToken))[0];

    private async Task<IReadOnlyList<ServiceOptionDto>> MapOptionsAsync(
        IReadOnlyList<ServiceOption> options, CancellationToken cancellationToken)
    {
        var unitIds = options.Where(o => o.UnitTypeId.HasValue).Select(o => o.UnitTypeId!.Value).Distinct().ToList();
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == TenantId && unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var optionIds = options.Select(o => o.Id).ToList();
        var allConditions = await _dbContext.ServiceOptionConditions.AsNoTracking()
            .Where(c => c.TenantId == TenantId && optionIds.Contains(c.ServiceOptionId))
            .ToListAsync(cancellationToken);
        var conditions = allConditions
            .Where(c => c.Kind == ServiceConditionKind.Warehouse)
            .ToLookup(c => c.ServiceOptionId, c => c.ReferenceId);
        var timeConditions = allConditions
            .Where(c => c.Kind != ServiceConditionKind.Warehouse)
            .ToLookup(c => c.ServiceOptionId,
                c => new ServiceTimeConditionDto(c.Kind, c.StopScope, c.TimeOfDay, c.Priority, c.AllowStacking, c.ActivityTypeId));
        var warehouseIds = conditions.SelectMany(g => g).Distinct().ToList();
        var warehouseNames = await _dbContext.Warehouses.AsNoTracking()
            .Where(w => w.TenantId == TenantId && warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);
        var salesCategoryIds = options.Where(o => o.SalesCategoryId.HasValue)
            .Select(o => o.SalesCategoryId!.Value).Distinct().ToList();
        var salesCategoryNames = await _dbContext.SalesCategories.AsNoTracking()
            .Where(c => c.TenantId == TenantId && salesCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return options.Select(o => new ServiceOptionDto(
            o.Id, o.Code, o.Name, o.Kind, o.DefaultValue, o.IsActive, o.SortOrder,
            o.Description, o.InvoiceDescription, o.SelectableInOrders,
            o.UnitTypeId, o.UnitTypeId is { } uid ? unitNames.GetValueOrDefault(uid) : null,
            o.AutoApply, o.OnlyForAdr,
            conditions[o.Id].ToList(),
            conditions[o.Id].Select(id => warehouseNames.GetValueOrDefault(id, "?")).ToList(),
            timeConditions[o.Id].ToList(),
            o.SalesCategoryId,
            o.SalesCategoryId is { } scid ? salesCategoryNames.GetValueOrDefault(scid) : null)).ToList();
    }

    private async Task<IReadOnlyList<PriceRuleDto>> MapRulesAsync(IReadOnlyList<PriceRule> rules, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var customerIds = rules.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId!.Value).Distinct().ToList();
        var unitIds = rules.Where(r => r.UnitTypeId.HasValue).Select(r => r.UnitTypeId!.Value).Distinct().ToList();
        var zoneIds = rules.Where(r => r.ZoneId.HasValue).Select(r => r.ZoneId!.Value)
            .Concat(rules.Where(r => r.OriginZoneId.HasValue).Select(r => r.OriginZoneId!.Value))
            .Distinct().ToList();
        var agreementIds = rules.Where(r => r.AgreementId.HasValue).Select(r => r.AgreementId!.Value).Distinct().ToList();

        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var units = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Where(z => z.TenantId == tenantId && zoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, z => z.Name, cancellationToken);
        var agreements = await _dbContext.PricingAgreements.AsNoTracking()
            .Where(a => a.TenantId == tenantId && agreementIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var salesCategoryIds = rules.Where(r => r.SalesCategoryId.HasValue)
            .Select(r => r.SalesCategoryId!.Value).Distinct().ToList();
        var salesCategoryNames = await _dbContext.SalesCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && salesCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var activityTypeIds = rules.Where(r => r.ActivityTypeId.HasValue)
            .Select(r => r.ActivityTypeId!.Value).Distinct().ToList();
        var activityTypeNames = await _dbContext.ActivityTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && activityTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        return rules.Select(rule => new PriceRuleDto(
            rule.Id, rule.CustomerId,
            rule.CustomerId is { } cid ? customers.GetValueOrDefault(cid) : null,
            rule.UnitTypeId,
            rule.UnitTypeId is { } uid ? units.GetValueOrDefault(uid) : null,
            rule.Basis, rule.ZoneId,
            rule.ZoneId is { } zid ? zones.GetValueOrDefault(zid) : null,
            rule.Name, rule.Currency, rule.EffectiveFrom, rule.EffectiveUntil, rule.IsActive,
            rule.UnitPrice, rule.MinimumAmount,
            rule.Brackets.OrderBy(b => b.FromQuantity)
                .Select(b => new PriceRuleBracketDto(
                    b.Id, b.FromQuantity, b.ToQuantity, b.Price, b.PricePerExtraUnit,
                    b.WeightToKg, b.VolumeToM3, b.LoadingMetersTo))
                .ToList(),
            rule.AgreementId,
            rule.AgreementId is { } aid ? agreements.GetValueOrDefault(aid) : null,
            rule.Priority, rule.BaseAmount,
            rule.OversizeLengthCm, rule.OversizeWidthCm, rule.OversizeBillableFactor,
            rule.MinimumQuantity, rule.QuantityRoundingStep,
            rule.MaximumAmount, rule.BracketMode,
            rule.SalesCategoryId,
            rule.SalesCategoryId is { } rscid ? salesCategoryNames.GetValueOrDefault(rscid) : null,
            rule.OriginZoneId,
            rule.OriginZoneId is { } ozid ? zones.GetValueOrDefault(ozid) : null,
            rule.ActivityTypeId,
            rule.ActivityTypeId is { } atid ? activityTypeNames.GetValueOrDefault(atid) : null))
            .ToList();
    }

    private static PricingZoneDto MapZone(PricingZone zone) => new(
        zone.Id, zone.Code, zone.Name, zone.IsActive, zone.SortOrder,
        zone.Areas.Select(a => new PricingZoneAreaDto(a.Id, a.CountryCode, a.PostalCodeFrom, a.PostalCodeTo)).ToList());
}
