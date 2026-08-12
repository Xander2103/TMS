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
    /// Calculates an explainable price for an order from the single coherent tariff model:
    /// deterministic most-specific rule selection per unit line (customer beats company,
    /// zone beats zone-less, explicit priority breaks remaining ties, an exact tie is a
    /// blocking configuration error), billable-quantity contracts, agreement minimums and
    /// surcharges, service options and an informational diesel line. Never silently prices
    /// zero: a missing tariff yields RequiresManualPrice plus diagnostic context.
    /// </summary>
    Task<PriceCalculationResult> CalculateAsync(PriceCalculationRequest request, CancellationToken cancellationToken);
}

public class PricingEngine : IPricingEngine
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public PricingEngine(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<PriceCalculationResult> CalculateAsync(PriceCalculationRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var lines = new List<PriceBreakdownLine>();
        var requiresManual = false;
        string? configurationError = null;

        var zone = await ResolveZoneAsync(request.DeliveryCountryCode, request.DeliveryPostalCode, cancellationToken);
        // Wave 3 §2: the origin (first loading stop) resolves through the same zone mechanism;
        // a rule with OriginZoneId only matches when the origin lands in that zone.
        var originZone = await ResolveZoneAsync(request.OriginCountryCode, request.OriginPostalCode, cancellationToken);
        bool OriginMatches(PriceRule rule) =>
            rule.OriginZoneId == null || (originZone is not null && rule.OriginZoneId == originZone.Id);

        var unitTypeIds = request.Lines.Select(l => l.UnitTypeId).Distinct().ToList();
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitTypeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        // Wave 2026-08-04 §7: one coverage entry per unit line the engine received; the status is
        // finalized in FinalizeAsync once per-unit services are known.
        var coverage = request.Lines
            .Select(l => new CoverageWork
            {
                UnitTypeId = l.UnitTypeId,
                UnitLabel = unitNames.GetValueOrDefault(l.UnitTypeId, "eenheid"),
                Quantity = l.Quantity,
                Reason = "Geen passend basistarief",
            })
            .ToList();

        // One-off order pricing (spec Phase 6): the order carries its own price agreement — no
        // contract is consulted at all (no candidate loading, no agreement post-processing, no
        // "missing tariff" diagnostics). Services + diesel still run as usual, below.
        if (request.OneOff is { } oneOff)
        {
            var source = "Eenmalig";
            if (!string.IsNullOrWhiteSpace(oneOff.Notes))
            {
                var notes = oneOff.Notes.Trim();
                if (notes.Length > 200)
                {
                    notes = notes[..200];
                }

                source = $"Eenmalig — {notes}";
            }

            lines.Add(new PriceBreakdownLine("Eenmalige prijsafspraak", decimal.Round(oneOff.FixedAmount, 2), source, LineKey: "oneoff"));
            lines.AddRange(ComputeExtraTimeLines(
                oneOff.IncludedLoadingMinutes, oneOff.IncludedUnloadingMinutes, oneOff.IncludedCombinedMinutes,
                oneOff.ExtraHourlyRate, request.ActualLoadingMinutes, request.ActualUnloadingMinutes));

            // The one-off amount prices the whole order — every unit line is covered by it.
            foreach (var item in coverage)
            {
                item.BasePriced = true;
                item.BaseRuleName = "Eenmalige prijsafspraak";
                item.Reason = null;
            }

            return await FinalizeAsync(
                request, lines, requiresManual: false, configurationError: null,
                zone, unitNames, cancellationToken, coverage: coverage);
        }

        // This customer's assignments to shared rate tables, active on the tariff date. A shared
        // agreement (IsShared) never applies on its own — only through a matching assignment here.
        var assignmentByAgreementId = (await _dbContext.PricingAgreementAssignments.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.CustomerId == request.CustomerId
                            && (a.EffectiveFrom == null || a.EffectiveFrom <= request.Date)
                            && (a.EffectiveUntil == null || a.EffectiveUntil >= request.Date))
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.AgreementId)
            .ToDictionary(g => g.Key, g => g.First());

        // All agreements active + within their window on the tariff date (both plain and derived
        // tables) — loaded once, with their own Surcharges/Modifiers, so derived tables carry
        // their OWN post-processing config even though their rules live on their base-chain root.
        var allAgreements = await _dbContext.PricingAgreements.AsNoTracking()
            .Include(a => a.Surcharges)
            .Include(a => a.Modifiers)
            .Where(a => a.TenantId == tenantId && a.IsActive
                        && a.EffectiveFrom <= request.Date
                        && (a.EffectiveUntil == null || a.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);
        var agreementsById = allAgreements.ToDictionary(a => a.Id);

        bool IsApplicable(PricingAgreement agreement) =>
            agreement.IsShared
                ? assignmentByAgreementId.ContainsKey(agreement.Id)
                : (agreement.CustomerId is null || agreement.CustomerId == request.CustomerId);

        var applicableAgreements = allAgreements.Where(IsApplicable).ToList();

        // Resolve each applicable agreement's rule source: itself, or — for a derived table
        // (spec §9) — its base-chain root, following BaseAgreementId up to 3 hops with a
        // visited-set cycle guard. A cycle in already-saved data (validation normally prevents
        // it) is a blocking configuration error, never a hang.
        var rootIdByAgreementId = new Dictionary<Guid, Guid>();
        foreach (var agreement in applicableAgreements)
        {
            if (agreement.BaseAgreementId is null)
            {
                rootIdByAgreementId[agreement.Id] = agreement.Id;
                continue;
            }

            var (rootId, cycle, chainNames) = ResolveBaseChainRoot(agreement, agreementsById);
            if (cycle)
            {
                configurationError ??= "Circulaire verwijzing tussen tarieventabellen: " + string.Join(" → ", chainNames);
                requiresManual = true;
                continue;
            }

            if (rootId is not null)
            {
                rootIdByAgreementId[agreement.Id] = rootId.Value;
            }
        }

        // P6: an activity-bound rule only matches orders OF that activity; a rule without the
        // dimension keeps matching everything (legacy tariffs stay byte-stable).
        var activityTypeId = request.ActivityTypeId;
        var candidateRules = await _dbContext.PriceRules.AsNoTracking()
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == tenantId && r.IsActive
                        && (r.CustomerId == null || r.CustomerId == request.CustomerId)
                        && (r.ActivityTypeId == null || r.ActivityTypeId == activityTypeId)
                        && r.EffectiveFrom <= request.Date
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);
        var rulesByAgreementId = candidateRules.Where(r => r.AgreementId is not null)
            .ToLookup(r => r.AgreementId!.Value);
        var standaloneRules = candidateRules.Where(r => r.AgreementId is null).ToList();

        // Row-level customer overrides ("klantafwijkingen", spec wave 2026-07-27 §2.2): a
        // customer-specific price for ONE bracket row of a shared/company rule. Loaded once,
        // date-filtered; only rows of rules that are actually candidates matter. Two overrides
        // valid on the same date for the same row never silently win — blocking config error.
        var candidateRuleIds = candidateRules.Select(r => r.Id).ToHashSet();
        var overrideRows = await _dbContext.PriceRuleBracketOverrides.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.CustomerId == request.CustomerId
                        && (o.EffectiveFrom == null || o.EffectiveFrom <= request.Date)
                        && (o.EffectiveUntil == null || o.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);
        overrideRows.RemoveAll(o => !candidateRuleIds.Contains(o.PriceRuleId));
        var conflictingOverride = overrideRows
            .GroupBy(o => new { o.PriceRuleId, o.FromQuantity, o.ToQuantity, o.WeightToKg, o.VolumeToM3, o.LoadingMetersTo })
            .FirstOrDefault(g => g.Count() > 1);
        if (conflictingOverride is not null)
        {
            var conflictRuleName = candidateRules.First(r => r.Id == conflictingOverride.Key.PriceRuleId).Name;
            var rowLabel = conflictingOverride.Key.ToQuantity is { } to
                ? $"{conflictingOverride.Key.FromQuantity:0.##}–{to:0.##}"
                : $"{conflictingOverride.Key.FromQuantity:0.##}+";
            configurationError ??= $"Conflicterende klantafwijkingen voor regel '{conflictRuleName}' (staffel {rowLabel}). "
                + "Corrigeer de klantafwijkingen (geldigheid of rij).";
            requiresManual = true;
        }
        var bracketOverrides = new BracketOverrideSet(overrideRows);

        // One candidate per (rule, engaging agreement) pair — the same physical rule can be
        // reachable through more than one applicable agreement (e.g. its own root table AND a
        // derived table assigned to this customer); each engagement scores/attributes separately.
        var allCandidates = new List<RuleCandidate>();
        allCandidates.AddRange(standaloneRules.Select(r => new RuleCandidate(r, r.CustomerId is not null ? 2 : 0, null)));
        foreach (var agreement in applicableAgreements)
        {
            if (!rootIdByAgreementId.TryGetValue(agreement.Id, out var rootId))
            {
                continue;
            }

            var tier = AgreementTier(agreement);
            allCandidates.AddRange(rulesByAgreementId[rootId].Select(r => new RuleCandidate(r, tier, agreement)));
        }

        // --- Unit lines: pick the most specific rule per line ---------------------------------
        var anyRuleMatched = false;
        var engagedAgreements = new Dictionary<Guid, PricingAgreement>();
        // Captures each unit type's own priced breakdown line (spec §29-31, combined-unit
        // degression): the eligible base for a discount is apportioned from these RAW base
        // amounts, never from a post-modifier/assignment subtotal.
        var unitLineByUnitTypeId = new Dictionary<Guid, PriceBreakdownLine>();
        foreach (var (line, coverageItem) in request.Lines.Zip(coverage))
        {
            var unitName = unitNames.GetValueOrDefault(line.UnitTypeId, "eenheid");
            var forUnit = allCandidates
                .Where(c => c.Rule.UnitTypeId == line.UnitTypeId && (c.Rule.ZoneId == null || (zone is not null && c.Rule.ZoneId == zone.Id)))
                .Where(c => OriginMatches(c.Rule))
                .ToList();
            var (candidate, conflicts) = SelectRule(forUnit);
            if (conflicts is not null)
            {
                configurationError = $"Conflicterende tariefregels voor {unitName}: "
                    + string.Join(" én ", conflicts.Select(c => $"'{c.Rule.Name}'"))
                    + ". Corrigeer de tarieven (geldigheid, zone of prioriteit).";
                lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                coverageItem.Reason = "Conflicterende tariefregels";
                requiresManual = true;
                continue;
            }

            if (candidate is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen tarief geconfigureerd voor {unitName}", 0m, "Ontbrekend"));
                requiresManual = true;
                continue;
            }

            anyRuleMatched = true;
            var rule = candidate.Rule;
            var engagedAgreement = candidate.EngagedAgreement;
            if (engagedAgreement is not null)
            {
                engagedAgreements.TryAdd(engagedAgreement.Id, engagedAgreement);
            }

            var billable = BillableQuantity(rule, line);
            var appliedBefore = bracketOverrides.AppliedCount;
            var amount = ComputeRuleAmount(rule, billable, request, bracketOverrides);
            var usedOverride = bracketOverrides.AppliedCount > appliedBefore;
            if (amount is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen staffel voor {billable:0.##} × {unitName}", 0m, rule.Name,
                    RuleId: rule.Id, RuleName: rule.Name, LineKey: RuleLineKey(rule.Id)));
                coverageItem.Reason = $"Geen staffel voor {billable:0.##} × {unitName}";
                requiresManual = true;
                continue;
            }

            var zoneSuffix = rule.ZoneId is not null && zone is not null ? $" (zone {zone.Code})" : "";
            var billableSuffix = billable != line.Quantity ? $" — factureerbaar: {billable:0.##}" : "";
            var unitLine = new PriceBreakdownLine(
                $"{line.Quantity:0.##} × {unitName}{zoneSuffix}{billableSuffix}",
                decimal.Round(amount.Value, 2), usedOverride ? $"{rule.Name} — klantafwijking" : rule.Name,
                RuleId: rule.Id, RuleName: rule.Name,
                AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name,
                ActualQuantity: line.Quantity, BillableQuantity: billable, LineKey: RuleLineKey(rule.Id));
            lines.Add(unitLine);
            unitLineByUnitTypeId[line.UnitTypeId] = unitLine;
            coverageItem.BasePriced = true;
            coverageItem.BaseAmount = unitLine.Amount;
            coverageItem.BaseRuleName = rule.Name;
            coverageItem.Reason = null;
        }

        // --- Order-level rules (no unit): forfaits, km/pallet/ton components ------------------
        var orderLevelCandidates = allCandidates
            .Where(c => c.Rule.UnitTypeId == null
                        && c.Rule.Basis is PriceRuleBasis.Fixed or PriceRuleBasis.PerKm or PriceRuleBasis.PerPallet
                            or PriceRuleBasis.PerTon or PriceRuleBasis.WeightBracket
                            or PriceRuleBasis.PerLoadingMeter or PriceRuleBasis.PerVolume or PriceRuleBasis.PerStop
                        && (c.Rule.ZoneId == null || (zone is not null && c.Rule.ZoneId == zone.Id)))
            .Where(c => OriginMatches(c.Rule))
            .ToList();

        if (anyRuleMatched)
        {
            // Component model: an agreement engaged by a matched unit rule also contributes
            // its order-level components (base cost, km price, ...).
            foreach (var candidate in orderLevelCandidates.Where(c => c.EngagedAgreement is { } a && engagedAgreements.ContainsKey(a.Id)))
            {
                AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement, bracketOverrides);
            }
        }
        else if (orderLevelCandidates.Count > 0)
        {
            // No unit line priced: an order-level tariff (converted rate card or forfait) is
            // the price. The most specific applicable agreement wins; standalone rules only
            // apply when no agreement-grouped order tariff exists.
            var producedAmount = false;
            var agreements = orderLevelCandidates
                .Where(c => c.EngagedAgreement is not null)
                .Select(c => c.EngagedAgreement!)
                .DistinctBy(a => a.Id)
                .ToList();
            if (agreements.Count > 0)
            {
                var bestSpecificity = agreements.Max(AgreementTier);
                var best = agreements.Where(a => AgreementTier(a) == bestSpecificity).ToList();
                if (best.Count > 1)
                {
                    configurationError = "Conflicterende prijsafspraken: "
                        + string.Join(" én ", best.Select(a => $"'{a.Name}'"))
                        + ". Corrigeer de geldigheidsperiodes.";
                    lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                    requiresManual = true;
                }
                else
                {
                    foreach (var candidate in orderLevelCandidates.Where(c => c.EngagedAgreement?.Id == best[0].Id))
                    {
                        producedAmount |= AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement, bracketOverrides);
                    }
                }
            }
            else
            {
                // One primary pricing basis (spec §10/18): standalone order-level rules never
                // sum across bases — the single most specific rule wins, an exact tie blocks.
                var (candidate, conflicts) = SelectRule(orderLevelCandidates.Where(c => c.EngagedAgreement is null).ToList());
                if (conflicts is not null)
                {
                    configurationError = "Conflicterende tariefregels: "
                        + string.Join(" én ", conflicts.Select(c => $"'{c.Rule.Name}'"))
                        + ". Kies één prijsbasis of onderscheid ze met prioriteit.";
                    lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                    requiresManual = true;
                }
                else if (candidate is not null)
                {
                    producedAmount |= AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement, bracketOverrides);
                }
            }

            if (producedAmount)
            {
                // The order-wide tariff replaces the per-unit "Ontbrekend" lines.
                lines.RemoveAll(l => l.Source == "Ontbrekend" && !l.Informational);
                requiresManual = configurationError is not null;
                anyRuleMatched = true;
                // The order-level tariff covers every unit line that had no own rule.
                foreach (var item in coverage.Where(c => !c.BasePriced))
                {
                    item.BasePriced = true;
                    item.BaseRuleName = "Ordertarief";
                    item.Reason = null;
                }
            }
        }

        // --- Agreement post-processing: derivation modifiers, adjustment, minimum/maximum, surcharges ---
        // Driven by which agreements actually produced a breakdown line — not just `engagedAgreements`
        // (unit-line engagement only), since an order-level-only tariff (no unit rule matched) also
        // engages its agreement's minimum/maximum/surcharges/modifiers.
        var engagedAgreementsWithLines = lines
            .Where(l => !l.Informational && l.AgreementId is not null)
            .Select(l => l.AgreementId!.Value)
            .Distinct()
            .Select(id => agreementsById.GetValueOrDefault(id))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        // --- Combined-unit degression discounts (spec §29-31) --------------------------------
        // Computed from the RAW base unit-line amounts captured above — never from a post-
        // modifier/assignment subtotal — but ATTACHED to `lines` inside the per-agreement loop
        // below, right after that agreement's own modifiers stack and BEFORE its assignment
        // adjustment, so: base + modifier lines → degression discount → assignment %/fixed →
        // minimum/maximum → surcharges. A discount line whose eligible unit lines span more than
        // one engaged agreement (ambiguous attribution) never joins any agreement subtotal — it is
        // added directly to `lines` as a plain line instead (still counted in Total).
        var discountLinesByAgreementId = new Dictionary<Guid, List<PriceBreakdownLine>>();
        if (unitLineByUnitTypeId.Count > 0)
        {
            var engagedAgreementIds = engagedAgreementsWithLines.Select(a => a.Id).ToHashSet();
            var discountConfigs = await _dbContext.CombinedUnitDiscounts.AsNoTracking()
                .Include(d => d.Units).Include(d => d.Tiers)
                .Where(d => d.TenantId == tenantId && d.IsActive
                            && d.EffectiveFrom <= request.Date && (d.EffectiveUntil == null || d.EffectiveUntil >= request.Date)
                            && (d.CustomerId == null || d.CustomerId == request.CustomerId))
                .ToListAsync(cancellationToken);
            var applicableDiscounts = discountConfigs
                .Where(d => d.AgreementId is null || engagedAgreementIds.Contains(d.AgreementId.Value))
                .ToList();

            CombinedUnitDiscount? winningDiscount = null;
            if (applicableDiscounts.Count > 0)
            {
                int Specificity(CombinedUnitDiscount d) => (d.CustomerId is not null ? 2 : 0) + (d.AgreementId is not null ? 1 : 0);
                var bestSpecificity = applicableDiscounts.Max(Specificity);
                var best = applicableDiscounts.Where(d => Specificity(d) == bestSpecificity).ToList();
                if (best.Count > 1)
                {
                    configurationError ??= "Conflicterende combinatiekortingen: "
                        + string.Join(" én ", best.Select(d => $"'{d.Name}'")) + ".";
                    lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                    requiresManual = true;
                }
                else
                {
                    winningDiscount = best[0];
                }
            }

            if (winningDiscount is not null)
            {
                var groups = request.Groups is { Count: > 0 } explicitGroups
                    ? explicitGroups
                    : [new PriceCalculationGroup(
                        "order", "Order", request.Lines.Select(l => new PriceCalculationGroupUnit(l.UnitTypeId, l.Quantity)).ToList())];

                var mergedGroups = CombinedUnitDiscountMath.MergeGroups(groups, winningDiscount.Scope);
                var discountUnitTypeIds = winningDiscount.Units.Select(u => u.UnitTypeId).ToList();
                var discountUnitFactors = winningDiscount.Units.Select(u => (u.UnitTypeId, u.EquivalentFactor)).ToList();

                var lineAmountsByUnitType = discountUnitTypeIds
                    .Where(unitLineByUnitTypeId.ContainsKey)
                    .ToDictionary(id => id, id => unitLineByUnitTypeId[id].Amount);
                var lineAgreementByUnitType = discountUnitTypeIds
                    .Where(unitLineByUnitTypeId.ContainsKey)
                    .ToDictionary(id => id, id => unitLineByUnitTypeId[id].AgreementId);

                var totalQuantities = new Dictionary<Guid, decimal>();
                foreach (var mergedGroup in mergedGroups)
                {
                    foreach (var (unitTypeId, quantity) in mergedGroup.Quantities)
                    {
                        totalQuantities[unitTypeId] = totalQuantities.GetValueOrDefault(unitTypeId) + quantity;
                    }
                }

                foreach (var mergedGroup in mergedGroups)
                {
                    var equivalent = CombinedUnitDiscountMath.ComputeEquivalent(mergedGroup.Quantities, discountUnitFactors);
                    var tier = CombinedUnitDiscountMath.FindTier(winningDiscount.Tiers, equivalent);
                    if (tier is null)
                    {
                        continue;
                    }

                    var eligibleBase = CombinedUnitDiscountMath.ComputeEligibleBase(
                        mergedGroup.Quantities, totalQuantities, lineAmountsByUnitType, discountUnitTypeIds);

                    // Unambiguous attribution: every unit type that actually contributed a nonzero
                    // share to this group's eligible base must point at the SAME engaged agreement
                    // (or all at "no agreement") for the discount line to join that agreement's
                    // subtotal — otherwise it stays a plain line (see comment above).
                    var contributingAgreementIds = discountUnitTypeIds
                        .Where(id => lineAmountsByUnitType.ContainsKey(id)
                                     && totalQuantities.GetValueOrDefault(id) > 0
                                     && mergedGroup.Quantities.GetValueOrDefault(id) > 0)
                        .Select(id => lineAgreementByUnitType[id])
                        .Distinct()
                        .ToList();
                    var targetAgreementId = contributingAgreementIds.Count == 1 ? contributingAgreementIds[0] : null;

                    var discountLine = new PriceBreakdownLine(
                        $"{winningDiscount.Name} ({mergedGroup.Label}): {equivalent:0.##} eenheden → -{tier.Percent:0.##}%",
                        -decimal.Round(eligibleBase * tier.Percent / 100m, 2),
                        winningDiscount.Name,
                        AgreementId: targetAgreementId,
                        LineKey: $"combineddiscount:{winningDiscount.Id}:{mergedGroup.Key}");

                    if (targetAgreementId is { } aid)
                    {
                        if (!discountLinesByAgreementId.TryGetValue(aid, out var pending))
                        {
                            pending = [];
                            discountLinesByAgreementId[aid] = pending;
                        }

                        pending.Add(discountLine);
                    }
                    else
                    {
                        lines.Add(discountLine);
                    }
                }
            }
        }

        // Multi-agreement included time (carried-over review item): at most ONE engaged agreement
        // may apply its included-time allowance against the same order actuals — otherwise two
        // configured agreements would double-charge/double-propose the same measured minutes.
        // The most specific tier wins; an exact tie is a blocking configuration error.
        var includedTimeAgreements = engagedAgreementsWithLines
            .Where(a => a.IncludedLoadingMinutes is not null || a.IncludedUnloadingMinutes is not null
                        || a.IncludedCombinedMinutes is not null)
            .ToList();
        Guid? extraTimeAgreementId = null;
        if (includedTimeAgreements.Count == 1)
        {
            extraTimeAgreementId = includedTimeAgreements[0].Id;
        }
        else if (includedTimeAgreements.Count > 1)
        {
            var bestTier = includedTimeAgreements.Max(AgreementTier);
            var best = includedTimeAgreements.Where(a => AgreementTier(a) == bestTier).ToList();
            if (best.Count == 1)
            {
                extraTimeAgreementId = best[0].Id;
            }
            else
            {
                configurationError ??= "Meerdere prijsafspraken met inbegrepen tijd op hetzelfde niveau: "
                    + string.Join(" én ", best.Select(a => $"'{a.Name}'"))
                    + ". Ken inbegrepen tijd slechts op één afspraak toe.";
                lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                requiresManual = true;
            }
        }

        // Task 11: effective included-time info for the order form, filled below alongside the
        // winning agreement's extra-time proposal so it can never disagree with the actual charge.
        IncludedTimeInfoDto? includedTimeInfo = null;

        foreach (var agreement in engagedAgreementsWithLines)
        {
            var subtotal = lines.Where(l => !l.Informational && l.AgreementId == agreement.Id).Sum(l => l.Amount);

            // Derived table (spec §9): stack its own modifiers, ascending Sequence, on the running
            // subtotal (base lines + previously applied modifiers) BEFORE the assignment adjustment.
            if (agreement.BaseAgreementId is not null)
            {
                var deliveryCountry = NormalizeCountryCode(request.DeliveryCountryCode);
                foreach (var modifier in agreement.Modifiers.OrderBy(m => m.Sequence))
                {
                    var countryMatches = modifier.CountryCode is null || modifier.CountryCode == deliveryCountry;
                    var zoneMatches = modifier.ZoneId is null || (zone is not null && modifier.ZoneId == zone.Id);
                    if (!countryMatches || !zoneMatches)
                    {
                        continue;
                    }

                    var modifierAmount = modifier.Percent is { } percent
                        ? decimal.Round(subtotal * percent / 100m, 2)
                        : decimal.Round(modifier.FixedAmount ?? 0m, 2);
                    lines.Add(new PriceBreakdownLine(modifier.Name, modifierAmount, agreement.Name,
                        AgreementId: agreement.Id, AgreementName: agreement.Name,
                        LineKey: AgreementLineKey(agreement.Id, $"modifier:{modifier.Id}")));
                    subtotal += modifierAmount;
                }
            }

            // Combined-unit degression discounts (spec §29-31): attached HERE — after base +
            // modifier lines, before the assignment adjustment below — so assignment %/fixed,
            // minimum, maximum and surcharges all see the discounted subtotal.
            if (discountLinesByAgreementId.TryGetValue(agreement.Id, out var pendingDiscountLines))
            {
                foreach (var discountLine in pendingDiscountLines)
                {
                    lines.Add(discountLine);
                    subtotal += discountLine.Amount;
                }
            }

            // Per-customer adjustment on a shared table (spec: reusable rate tables), before the
            // minimum top-up: a percentage discount/markup on the table's own lines, then a fixed
            // amount on top of the order.
            if (agreement.IsShared && assignmentByAgreementId.TryGetValue(agreement.Id, out var assignment))
            {
                if (assignment.PercentAdjustment is { } percent)
                {
                    var percentAmount = decimal.Round(subtotal * percent / 100m, 2);
                    lines.Add(new PriceBreakdownLine($"Klantafspraak {percent:+0.##;-0.##}%", percentAmount,
                        agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name,
                        LineKey: AgreementLineKey(agreement.Id, "assignpct")));
                    subtotal += percentAmount;
                }

                if (assignment.FixedAdjustment is { } fixedAdjustment)
                {
                    var fixedAmount = decimal.Round(fixedAdjustment, 2);
                    lines.Add(new PriceBreakdownLine("Klantafspraak vast bedrag", fixedAmount,
                        agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name,
                        LineKey: AgreementLineKey(agreement.Id, "assignfix")));
                    subtotal += fixedAmount;
                }
            }

            if (agreement.MinimumAmount is { } minimum && subtotal < minimum)
            {
                lines.Add(new PriceBreakdownLine($"Minimumtarief {agreement.Name}", decimal.Round(minimum - subtotal, 2),
                    agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name,
                    LineKey: AgreementLineKey(agreement.Id, "min")));
                subtotal = minimum;
            }

            if (agreement.MaximumAmount is { } maximum && subtotal > maximum)
            {
                lines.Add(new PriceBreakdownLine($"Maximumtarief {agreement.Name}", decimal.Round(maximum - subtotal, 2),
                    agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name,
                    LineKey: AgreementLineKey(agreement.Id, "max")));
                subtotal = maximum;
            }

            foreach (var surcharge in agreement.Surcharges.OrderBy(s => s.Name))
            {
                var amount = surcharge.Kind == SurchargeKind.Percent
                    ? decimal.Round(subtotal * surcharge.Value / 100m, 2)
                    : decimal.Round(surcharge.Value, 2);
                lines.Add(new PriceBreakdownLine(surcharge.Name, amount, agreement.Name,
                    AgreementId: agreement.Id, AgreementName: agreement.Name,
                    LineKey: AgreementLineKey(agreement.Id, $"surcharge:{surcharge.Name}")));
            }

            // Included loading/unloading time (spec Phase 6): only the single winning agreement
            // (see extraTimeAgreementId above) gets the extra-time proposal — never more than one,
            // to avoid double-counting the same measured minutes.
            if (agreement.Id == extraTimeAgreementId)
            {
                lines.AddRange(ComputeExtraTimeLines(
                    agreement.IncludedLoadingMinutes, agreement.IncludedUnloadingMinutes, agreement.IncludedCombinedMinutes,
                    agreement.ExtraHourlyRate, request.ActualLoadingMinutes, request.ActualUnloadingMinutes,
                    request.IncludedTimeOverrides));
                includedTimeInfo = BuildIncludedTimeInfo(
                    agreement.IncludedLoadingMinutes, agreement.IncludedUnloadingMinutes, agreement.IncludedCombinedMinutes,
                    agreement.ExtraHourlyRate, request.IncludedTimeOverrides);
            }
        }

        // No engaged agreement carries included time. An order override with nothing to override
        // is still a corner case worth reflecting as "Order" (rather than silently no-op'ing to
        // "Geen") — but the common case is genuinely nothing configured anywhere.
        var hasOrderOverride = request.IncludedTimeOverrides is { } overridesOnly
            && (overridesOnly.IncludedLoadingMinutes is not null || overridesOnly.IncludedUnloadingMinutes is not null
                || overridesOnly.ExtraHourlyRate is not null || overridesOnly.RoundingStepMinutes is not null
                || overridesOnly.MinimumBillableMinutes is not null);
        includedTimeInfo ??= hasOrderOverride
            ? BuildIncludedTimeInfo(null, null, null, null, request.IncludedTimeOverrides)
            : new IncludedTimeInfoDto(null, null, null, null, "Geen");

        return await FinalizeAsync(request, lines, requiresManual, configurationError, zone, unitNames, cancellationToken, includedTimeInfo, coverage);
    }

    private static string RuleLineKey(Guid ruleId) => $"rule:{ruleId}";

    private static string AgreementLineKey(Guid agreementId, string discriminator) => $"agreement:{agreementId}:{discriminator}";

    /// <summary>
    /// Shared tail of the calculation: service options, informational diesel line, no-tariff
    /// diagnostics and the final totals. Used by both contract-mode pricing and one-off pricing
    /// (spec Phase 6), which skips straight here after producing its own header line(s).
    /// </summary>
    /// <summary>
    /// Working state for one unit line's pricing coverage (wave 2026-08-04 §7) — finalized into
    /// <see cref="PricingCoverageLine"/> records in <see cref="FinalizeAsync"/> once the per-unit
    /// service amounts are known.
    /// </summary>
    private sealed class CoverageWork
    {
        public Guid UnitTypeId { get; init; }
        public string UnitLabel { get; init; } = "eenheid";
        public decimal Quantity { get; init; }
        public bool BasePriced { get; set; }
        public decimal BaseAmount { get; set; }
        public string? BaseRuleName { get; set; }
        public string? Reason { get; set; }
        public decimal ServicesAmount { get; set; }
    }

    private async Task<PriceCalculationResult> FinalizeAsync(
        PriceCalculationRequest request, List<PriceBreakdownLine> lines, bool requiresManual, string? configurationError,
        PricingZone? zone, IReadOnlyDictionary<Guid, string> unitNames, CancellationToken cancellationToken,
        IncludedTimeInfoDto? includedTimeInfo = null, List<CoverageWork>? coverage = null)
    {
        var tenantId = _tenantContext.TenantId;
        var subtotalBeforeServices = lines.Where(l => !l.Informational && !l.Proposed).Sum(l => l.Amount);

        // Service options: explicitly selected ∪ auto-applied (contract) options. Percent applies
        // to the base subtotal. Customer overrides are date-aware and can disable a globally
        // available service, or override its auto-apply behaviour, for this customer.
        var serviceLines = new List<PriceServiceLine>();
        var selections = request.Services is { Count: > 0 }
            ? request.Services
            : request.ServiceOptionIds.Select(id => new PriceServiceInput(id)).ToList();
        var quantities = selections
            .GroupBy(s => s.ServiceOptionId)
            .ToDictionary(g => g.Key, g => g.First().Quantity);
        var selectedIds = quantities.Keys.ToHashSet();

        var allOptions = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.IsActive)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .ToListAsync(cancellationToken);
        var allOptionIds = allOptions.Select(o => o.Id).ToList();
        var overrides = (await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.CustomerId == request.CustomerId && allOptionIds.Contains(p.ServiceOptionId))
                .ToListAsync(cancellationToken))
            .Where(p => (p.EffectiveFrom is null || p.EffectiveFrom <= request.Date)
                        && (p.EffectiveUntil is null || p.EffectiveUntil >= request.Date))
            .ToDictionary(p => p.ServiceOptionId);

        var serviceUnitTypeIds = allOptions.Where(o => o.UnitTypeId is not null).Select(o => o.UnitTypeId!.Value).Distinct().ToList();
        var serviceUnitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && serviceUnitTypeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        // Extra conditions beyond ADR (wave 2026-07-27 §2.4): rows of the same kind OR together,
        // different kinds (incl. OnlyForAdr) AND together. No rows = applies to all orders.
        var allOptionIdSet = allOptions.Select(o => o.Id).ToHashSet();
        var conditionsByOption = (await _dbContext.ServiceOptionConditions.AsNoTracking()
                .Where(c => c.TenantId == tenantId && allOptionIds.Contains(c.ServiceOptionId))
                .ToListAsync(cancellationToken))
            .Where(c => allOptionIdSet.Contains(c.ServiceOptionId))
            .ToLookup(c => c.ServiceOptionId);

        // --- Wave 2026-08-04 §16/§17: time-based stop conditions -------------------------------
        var stopTimes = request.StopTimes ?? [];

        // Wave 3 §4: tenant holidays feed the Holiday condition — loaded once per calculation,
        // only for the dates the stops actually mention.
        var stopDates = stopTimes.Where(s => s.PlannedDate is not null).Select(s => s.PlannedDate!.Value).Distinct().ToList();
        var holidayDates = stopDates.Count == 0
            ? new HashSet<DateOnly>()
            : (await _dbContext.TenantHolidays.AsNoTracking()
                .Where(hd => hd.TenantId == tenantId && stopDates.Contains(hd.Date))
                .Select(hd => hd.Date)
                .ToListAsync(cancellationToken)).ToHashSet();

        bool ScopeMatches(ServiceConditionStopScope scope, StopTimeInput stop) =>
            scope == ServiceConditionStopScope.Any
            || (scope == ServiceConditionStopScope.Unloading) == stop.IsUnloading;

        // A stop's PROMISE matches the condition: "vóór 08:00" satisfies a "vóór 10:00" surcharge
        // (08 ≤ 10); "na 19:00" satisfies "na 18:00" (19 ≥ 18). Windows expose both bounds.
        bool RowMatches(ServiceOptionCondition row) => row.Kind switch
        {
            ServiceConditionKind.StopTimeBefore => stopTimes.Any(s => ScopeMatches(row.StopScope, s)
                && s.RequirementKind is "Before" or "Window"
                && s.RequirementTo is { } to && row.TimeOfDay is { } before && to <= before),
            ServiceConditionKind.StopTimeAfter => stopTimes.Any(s => ScopeMatches(row.StopScope, s)
                && s.RequirementKind is "After" or "Window"
                && s.RequirementFrom is { } from && row.TimeOfDay is { } after && from >= after),
            ServiceConditionKind.AppointmentRequired => stopTimes.Any(s =>
                ScopeMatches(row.StopScope, s) && s.AppointmentRequired),
            ServiceConditionKind.Weekend => stopTimes.Any(s => ScopeMatches(row.StopScope, s)
                && s.PlannedDate is { } day && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            ServiceConditionKind.Holiday => stopTimes.Any(s => ScopeMatches(row.StopScope, s)
                && s.PlannedDate is { } holidayDay && holidayDates.Contains(holidayDay)),
            // P6: order-level equipment/movement/activity conditions — null (unknown) never matches.
            ServiceConditionKind.Crane => request.CraneRequired == true,
            ServiceConditionKind.Plateau => request.PlateauRequired == true,
            ServiceConditionKind.Moffett => request.MoffettRequired == true,
            ServiceConditionKind.ReturnMovement => request.IsReturnMovement == true,
            ServiceConditionKind.ActivityType =>
                row.ActivityTypeId is { } conditionActivity && request.ActivityTypeId == conditionActivity,
            _ => true,
        };

        var timeConditionHoldsByOption = new Dictionary<Guid, bool>();
        foreach (var option in allOptions)
        {
            var rows = conditionsByOption[option.Id].Where(c => c.Kind != ServiceConditionKind.Warehouse).ToList();
            timeConditionHoldsByOption[option.Id] = rows
                .GroupBy(c => c.Kind)
                .All(kindGroup => kindGroup.Any(RowMatches));
        }

        bool WouldAutoApply(ServiceOption option)
        {
            var over = overrides.GetValueOrDefault(option.Id);
            var warehouseRows = conditionsByOption[option.Id]
                .Where(c => c.Kind == ServiceConditionKind.Warehouse).Select(c => c.ReferenceId).ToList();
            var warehouseHolds = warehouseRows.Count == 0
                || warehouseRows.Any(id => request.WarehouseIds?.Contains(id) == true);
            return (over?.AutoApplyOverride ?? option.AutoApply) && over?.Disabled != true
                   && (!option.OnlyForAdr || request.AdrRequired == true)
                   && warehouseHolds && timeConditionHoldsByOption[option.Id];
        }

        // §17 competition: among auto-applied options matched via a non-stacking Before (resp.
        // After) condition only ONE applies — highest priority wins, then the most specific time
        // (Before: earliest, After: latest). An exact tie is a blocking configuration error.
        var suppressedByCompetition = new Dictionary<Guid, string>();
        foreach (var competitionKind in new[] { ServiceConditionKind.StopTimeBefore, ServiceConditionKind.StopTimeAfter })
        {
            var contenders = allOptions
                .Where(o => !selectedIds.Contains(o.Id) && WouldAutoApply(o))
                .Select(o => (Option: o, Row: conditionsByOption[o.Id]
                    .Where(c => c.Kind == competitionKind && !c.AllowStacking && RowMatches(c))
                    .OrderByDescending(c => c.Priority)
                    .FirstOrDefault()))
                .Where(x => x.Row is not null)
                .ToList();
            if (contenders.Count < 2)
            {
                continue;
            }

            var ordered = (competitionKind == ServiceConditionKind.StopTimeBefore
                    ? contenders.OrderByDescending(x => x.Row!.Priority).ThenBy(x => x.Row!.TimeOfDay)
                    : contenders.OrderByDescending(x => x.Row!.Priority).ThenByDescending(x => x.Row!.TimeOfDay))
                .ToList();
            var winner = ordered[0];
            var runnerUp = ordered[1];
            if (winner.Row!.Priority == runnerUp.Row!.Priority && winner.Row.TimeOfDay == runnerUp.Row.TimeOfDay)
            {
                configurationError ??= "Conflicterende tijdsvoorwaarden: "
                    + $"'{winner.Option.Name}' én '{runnerUp.Option.Name}' matchen even specifiek. "
                    + "Onderscheid ze met prioriteit of tijd.";
                requiresManual = true;
                foreach (var contender in ordered)
                {
                    suppressedByCompetition[contender.Option.Id] = "conflict";
                }

                lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                continue;
            }

            foreach (var loser in ordered.Skip(1))
            {
                suppressedByCompetition[loser.Option.Id] = winner.Option.Name;
            }
        }

        foreach (var option in allOptions)
        {
            var over = overrides.GetValueOrDefault(option.Id);
            var isSelected = selectedIds.Contains(option.Id);

            var warehouseConditionIds = conditionsByOption[option.Id]
                .Where(c => c.Kind == ServiceConditionKind.Warehouse)
                .Select(c => c.ReferenceId)
                .ToList();
            var warehouseConditionHolds = warehouseConditionIds.Count == 0
                || warehouseConditionIds.Any(id => request.WarehouseIds?.Contains(id) == true);
            var timeConditionHolds = timeConditionHoldsByOption[option.Id];

            // Auto-apply: a contract service the engine adds without the user selecting it —
            // active, effectively auto (customer override wins), not disabled for this customer,
            // (when ADR-only) the order is actually ADR, and every configured condition holds.
            var effectiveAutoApply = over?.AutoApplyOverride ?? option.AutoApply;
            var autoEligible = effectiveAutoApply && over?.Disabled != true
                                && (!option.OnlyForAdr || request.AdrRequired == true)
                                && warehouseConditionHolds
                                && timeConditionHolds;
            var isAutoApplied = !isSelected && autoEligible;
            if (!isSelected && !isAutoApplied)
            {
                continue;
            }

            if (over?.Disabled == true)
            {
                lines.Add(new PriceBreakdownLine(
                    $"{option.Name}: uitgeschakeld voor deze klant", 0m, "Klanttarief", Informational: true));
                continue;
            }

            if (option.OnlyForAdr && request.AdrRequired != true)
            {
                // Never reached via the auto-apply path (excluded from autoEligible above) — this
                // is an explicit selection that simply doesn't apply to a non-ADR order.
                lines.Add(new PriceBreakdownLine(
                    $"{option.Name}: alleen van toepassing bij ADR", 0m, "Voorwaarde", Informational: true));
                continue;
            }

            if (!warehouseConditionHolds)
            {
                // Explicit selection of a warehouse-conditioned service on an order that never
                // touches one of the configured warehouses — informational, never charged.
                lines.Add(new PriceBreakdownLine(
                    $"{option.Name}: alleen van toepassing voor het gekoppelde magazijn", 0m, "Voorwaarde", Informational: true));
                continue;
            }

            if (!timeConditionHolds)
            {
                // Explicit selection of a time-conditioned service whose stop-time requirement
                // does not match — informational, never charged (§16).
                lines.Add(new PriceBreakdownLine(
                    $"{option.Name}: alleen van toepassing bij de ingestelde tijdseis", 0m, "Voorwaarde", Informational: true));
                continue;
            }

            if (isAutoApplied && suppressedByCompetition.TryGetValue(option.Id, out var winnerName))
            {
                // §17: never a silent double charge — the loser explains why it did not apply.
                lines.Add(new PriceBreakdownLine(
                    winnerName == "conflict"
                        ? $"{option.Name}: niet toegepast — conflicterende tijdsvoorwaarden"
                        : $"{option.Name}: niet toegepast — '{winnerName}' heeft voorrang",
                    0m, "Voorwaarde", Informational: true));
                continue;
            }

            var value = over?.Value ?? option.DefaultValue;
            var source = isAutoApplied
                ? (over?.Value is not null ? "Automatisch (klanttarief)" : "Automatisch (contract)")
                : (over?.Value is not null ? "Klanttarief" : "Algemene standaard");
            var invoiceLabel = over?.InvoiceDescription ?? option.InvoiceDescription;
            var enteredQuantity = quantities.GetValueOrDefault(option.Id);
            decimal amount;
            decimal? quantity = null;
            string label = option.Name;

            if (option.Kind is SurchargeKind.PerHour or SurchargeKind.PerStop)
            {
                var unitLabel = option.Kind == SurchargeKind.PerHour ? "uur" : "stops";
                if (enteredQuantity is not { } hourStopQty || hourStopQty <= 0)
                {
                    // Quantity-based services need an entered quantity (hours / stops).
                    lines.Add(new PriceBreakdownLine(
                        $"{option.Name}: geef het aantal {(option.Kind == SurchargeKind.PerHour ? "uur" : "stop")} op",
                        0m, source, Informational: true));
                    continue;
                }

                quantity = hourStopQty;
                amount = decimal.Round(value * hourStopQty, 2);
                label = $"{option.Name} ({hourStopQty:0.##} {unitLabel})";
            }
            else if (option.Kind == SurchargeKind.Percent)
            {
                amount = decimal.Round(subtotalBeforeServices * value / 100m, 2);
            }
            else if (option.Kind == SurchargeKind.PerUnit)
            {
                // Entered quantity always wins; otherwise derived from the matching order line(s) —
                // per unit type, Lines wins when it has that unit (the order's own primary unit,
                // driven by order.Quantity — the authoritative source for that unit even when cargo
                // detail for it is partial/absent), else fall back to the cargo-detail Groups
                // (combined-unit degression input, which reflects every OTHER unit type actually
                // present across the order's cargo lines, e.g. a Colli-bound service on a Pallet
                // order). This is a per-unit-type choice, not a wholesale Groups-vs-Lines
                // preference: Groups is only ever built from cargo items carrying a
                // QuantityUnitCode, so it never contains the order's own primary unit unless a
                // cargo line happens to share that same code — preferring Groups wholesale would
                // silently drop a primary-unit service back to zero whenever cargo detail exists
                // only for a different unit.
                var derived = option.UnitTypeId is { } unitTypeId
                    ? (request.Lines.Any(l => l.UnitTypeId == unitTypeId)
                        ? request.Lines.Where(l => l.UnitTypeId == unitTypeId).Sum(l => l.Quantity)
                        : (request.Groups ?? []).SelectMany(g => g.Units).Where(u => u.UnitTypeId == unitTypeId).Sum(u => u.Quantity))
                    : 0m;
                var qty = enteredQuantity is { } q1 && q1 > 0 ? q1 : derived;
                var unitName = option.UnitTypeId is { } uid ? serviceUnitNames.GetValueOrDefault(uid, "eenheid") : "eenheid";
                if (qty <= 0)
                {
                    lines.Add(new PriceBreakdownLine(
                        $"{option.Name}: geen {unitName} op deze order", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty, 2);
                label = $"{option.Name} ({qty:0.##} {unitName})";
            }
            else if (option.Kind == SurchargeKind.PerOrderLine)
            {
                var qty = enteredQuantity is { } q2 && q2 > 0 ? q2 : (decimal?)request.CargoLineCount;
                // An explicit 0 (no cargo lines known) is treated as "missing" — same as null —
                // so this never produces a silent €0 charge line, aligning with PerHour/PerStop/PerDay.
                if (qty is null or <= 0)
                {
                    lines.Add(new PriceBreakdownLine(
                        $"{option.Name}: aantal orderlijnen onbekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} orderlijnen)";
            }
            else if (option.Kind == SurchargeKind.PerKg)
            {
                var qty = enteredQuantity is { } q3 && q3 > 0 ? q3 : request.WeightKg;
                // Explicit 0 kg treated the same as unknown — no silent €0 charge line.
                if (qty is null or <= 0)
                {
                    lines.Add(new PriceBreakdownLine($"{option.Name}: geen gewicht gekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} kg)";
            }
            else if (option.Kind == SurchargeKind.PerM3)
            {
                var qty = enteredQuantity is { } q4 && q4 > 0 ? q4 : request.VolumeM3;
                // Explicit 0 m³ treated the same as unknown — no silent €0 charge line.
                if (qty is null or <= 0)
                {
                    lines.Add(new PriceBreakdownLine($"{option.Name}: geen volume gekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} m³)";
            }
            else if (option.Kind == SurchargeKind.PerLdm)
            {
                var qty = enteredQuantity is { } q5 && q5 > 0 ? q5 : request.LoadingMeters;
                // Explicit 0 ldm treated the same as unknown — no silent €0 charge line.
                if (qty is null or <= 0)
                {
                    lines.Add(new PriceBreakdownLine($"{option.Name}: geen laadmeters gekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} ldm)";
            }
            else if (option.Kind == SurchargeKind.PerKm)
            {
                // Wave 3 §3 (Maut as sales concept): amount = tarief × orderafstand.
                var qty = enteredQuantity is { } q6 && q6 > 0 ? q6 : request.DistanceKm;
                // Explicit 0 km treated the same as unknown — no silent €0 charge line.
                if (qty is null or <= 0)
                {
                    lines.Add(new PriceBreakdownLine($"{option.Name}: geen afstand gekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} km)";
            }
            else if (option.Kind is SurchargeKind.PerDay or SurchargeKind.PerPalletDay)
            {
                var dayWord = option.Kind == SurchargeKind.PerDay ? "dagen" : "pallet-dagen";
                if (enteredQuantity is not { } dayQty || dayQty <= 0)
                {
                    lines.Add(new PriceBreakdownLine(
                        $"{option.Name}: geef het aantal {dayWord} op", 0m, source, Informational: true));
                    continue;
                }

                quantity = dayQty;
                amount = decimal.Round(value * dayQty, 2);
                label = $"{option.Name} ({dayQty:0.##} {dayWord})";
            }
            else
            {
                amount = decimal.Round(value, 2);
            }

            if (over?.MinimumAmount is { } serviceMinimum && amount < serviceMinimum)
            {
                amount = serviceMinimum;
            }

            // Quantity round-trips as both Actual/BillableQuantity so the order-side merge can
            // derive a stable per-unit price (amount / quantity) for a simple qty × rate service.
            lines.Add(new PriceBreakdownLine(label, amount, source,
                ActualQuantity: quantity, BillableQuantity: quantity, LineKey: $"service:{option.Id}", ServiceOptionId: option.Id));
            serviceLines.Add(new PriceServiceLine(
                option.Id, option.Name, option.Kind, value, amount, quantity, invoiceLabel, source, isAutoApplied));

            // Coverage (wave 2026-08-04 §7): a per-unit service bills a specific unit line —
            // recorded so an otherwise unpriced unit shows "Gedeeltelijk geprijsd", never "Volledig".
            if (option.Kind == SurchargeKind.PerUnit && option.UnitTypeId is { } coveredUnitId
                && coverage?.FirstOrDefault(c => c.UnitTypeId == coveredUnitId) is { } coveredItem)
            {
                coveredItem.ServicesAmount += amount;
            }
        }

        var total = lines.Where(l => !l.Informational && !l.Proposed).Sum(l => l.Amount);

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
                decimal.Round(total * diesel.Percent / 100m, 2), "Dieseltoeslag", Informational: true, LineKey: "diesel"));
        }

        // Never a silent €0: explain what was searched for when no valid tariff exists.
        List<string>? diagnostics = null;
        if (requiresManual)
        {
            diagnostics = await BuildDiagnosticsAsync(request, zone, unitNames, cancellationToken);
            if (configurationError is null && !lines.Any(l => l.Source == "Ontbrekend" || l.Source == "Configuratiefout"))
            {
                lines.Insert(0, new PriceBreakdownLine("Geen geldig tarief gevonden voor deze order.", 0m, "Ontbrekend"));
            }
            else if (lines.Any(l => l.Source == "Ontbrekend"))
            {
                lines.Insert(0, new PriceBreakdownLine("Geen geldig tarief gevonden voor deze order.", 0m, "Ontbrekend", Informational: true));
            }
        }

        var totalWithInformational = total + lines.Where(l => l.Informational).Sum(l => l.Amount);
        // Proposed lines (unconfirmed extra-time charges) are excluded from Total/TotalWithInformational
        // — never silently invoiceable — but surfaced here so the UI can show them as a "what if" total.
        var totalWithProposed = total + lines.Where(l => l.Proposed && !l.Informational).Sum(l => l.Amount);
        return new PriceCalculationResult(
            lines, decimal.Round(total, 2), decimal.Round(totalWithInformational, 2), "EUR",
            zone?.Code, zone?.Name, requiresManual, serviceLines,
            TariffDate: request.Date, ConfigurationError: configurationError, Diagnostics: diagnostics,
            TotalWithProposed: decimal.Round(totalWithProposed, 2), IncludedTimeInfo: includedTimeInfo,
            Coverage: coverage?
                .Select(c => new PricingCoverageLine(
                    c.UnitTypeId, c.UnitLabel, c.Quantity,
                    c.BasePriced ? "Full" : c.ServicesAmount > 0m ? "Partial" : "None",
                    c.BaseAmount, c.BaseRuleName, c.ServicesAmount,
                    c.BasePriced ? null : c.Reason))
                .ToList());
    }

    /// <summary>
    /// Task 11: resolves the effective included loading/unloading/combined minutes and extra-time
    /// rate (order override ?? agreement value) plus whether any of the 5 order overrides is set —
    /// the single source of truth shared by <see cref="ComputeExtraTimeLines"/> (the pricing math)
    /// and <see cref="BuildIncludedTimeInfo"/> (the order form's read-only summary), so the two can
    /// never disagree on what "effective" means. See <see cref="ComputeExtraTimeLines"/> for the
    /// exact combined-vs-per-activity switching rule.
    /// </summary>
    private static (int? Loading, int? Unloading, int? Combined, decimal? Rate, bool AnyOverrideSet) ResolveIncludedTime(
        int? includedLoadingMinutes, int? includedUnloadingMinutes, int? includedCombinedMinutes,
        decimal? extraHourlyRate, IncludedTimeOverrideInput? overrides)
    {
        var loadingOverrideSet = overrides?.IncludedLoadingMinutes is not null;
        var unloadingOverrideSet = overrides?.IncludedUnloadingMinutes is not null;
        var anyOverrideSet = loadingOverrideSet || unloadingOverrideSet
            || overrides?.ExtraHourlyRate is not null || overrides?.RoundingStepMinutes is not null
            || overrides?.MinimumBillableMinutes is not null;
        var effectiveRate = overrides?.ExtraHourlyRate ?? extraHourlyRate;

        if (includedCombinedMinutes is { } combined && !loadingOverrideSet && !unloadingOverrideSet)
        {
            return (null, null, combined, effectiveRate, anyOverrideSet);
        }

        // Either the agreement is per-activity already, or it is combined but got switched to
        // per-activity by one of the overrides above — the non-overridden side then falls back to
        // 0 included minutes rather than the (no longer applicable) combined figure.
        var agreementIsCombined = includedCombinedMinutes is not null;
        var effectiveLoading = overrides?.IncludedLoadingMinutes ?? (agreementIsCombined ? 0 : includedLoadingMinutes);
        var effectiveUnloading = overrides?.IncludedUnloadingMinutes ?? (agreementIsCombined ? 0 : includedUnloadingMinutes);
        return (effectiveLoading, effectiveUnloading, null, effectiveRate, anyOverrideSet);
    }

    /// <summary>
    /// Task 11: the effective included-time info shown on the order form ("Laad- en lostijd"
    /// section) — same resolution as <see cref="ComputeExtraTimeLines"/>, without needing actual
    /// measured minutes. Source is "Order" as soon as any of the 5 order overrides is set,
    /// otherwise "Contract".
    /// </summary>
    private static IncludedTimeInfoDto BuildIncludedTimeInfo(
        int? includedLoadingMinutes, int? includedUnloadingMinutes, int? includedCombinedMinutes,
        decimal? extraHourlyRate, IncludedTimeOverrideInput? overrides)
    {
        var (loading, unloading, combined, rate, anyOverrideSet) = ResolveIncludedTime(
            includedLoadingMinutes, includedUnloadingMinutes, includedCombinedMinutes, extraHourlyRate, overrides);
        // §18: stop-level overrides beat order-level ones — the source reflects the winner.
        var source = !anyOverrideSet ? "Contract" : overrides?.FromStopOverride == true ? "Stop" : "Order";
        return new IncludedTimeInfoDto(loading, unloading, combined, rate, source);
    }

    /// <summary>
    /// Extra-time proposal lines for one included-time agreement (spec Phase 6, one-off order or
    /// contract agreement): compares the actual measured loading/unloading minutes (from stop
    /// executions) against the included allowance and charges the excess at the hourly rate — as
    /// a PROPOSAL (never silently invoiceable) until confirmed. Combined mode and per-activity mode
    /// are mutually exclusive; combined wins when both are somehow set. A configured excess with no
    /// rate yields an informational "geef het uurtarief op" line instead of a charge.
    /// </summary>
    /// <param name="overrides">
    /// Task 10 (contract mode only — never passed for a one-off order): order-level overrides of
    /// the agreement's included minutes/rate/rounding/minimum. Resolution rule: per-activity
    /// included = override ?? agreement value. The agreement's COMBINED allowance survives only
    /// when NEITHER per-activity override is set; as soon as either per-activity override is
    /// present, the calculation switches from combined to per-activity mode, and the activity that
    /// was NOT overridden falls back to 0 included minutes (the combined allowance was never split
    /// per activity, so there is no partial figure to inherit for it). Rate = override ?? agreement
    /// rate. Rounding step and minimum billable minutes have no agreement-level equivalent — they
    /// only ever come from the order override.
    /// </param>
    public static List<PriceBreakdownLine> ComputeExtraTimeLines(
        int? includedLoadingMinutes, int? includedUnloadingMinutes, int? includedCombinedMinutes,
        decimal? extraHourlyRate, decimal? actualLoadingMinutes, decimal? actualUnloadingMinutes,
        IncludedTimeOverrideInput? overrides = null)
    {
        var lines = new List<PriceBreakdownLine>();
        var roundingStep = overrides?.RoundingStepMinutes;
        var minimumBillable = overrides?.MinimumBillableMinutes;
        var (effectiveLoading, effectiveUnloading, effectiveCombined, effectiveRate, _) = ResolveIncludedTime(
            includedLoadingMinutes, includedUnloadingMinutes, includedCombinedMinutes, extraHourlyRate, overrides);

        if (effectiveCombined is { } combined)
        {
            if (actualLoadingMinutes is null && actualUnloadingMinutes is null)
            {
                return lines;
            }

            var actualTotal = (actualLoadingMinutes ?? 0m) + (actualUnloadingMinutes ?? 0m);
            var extra = Math.Max(0m, actualTotal - combined);
            if (extra > 0)
            {
                AddExtraTimeLine(lines,
                    $"Extra laad-/lostijd: {actualTotal:0.##} min (inbegrepen {combined} min)", extra, effectiveRate,
                    "combined", roundingStep, minimumBillable);
            }

            return lines;
        }

        if (effectiveLoading is { } includedLoading && actualLoadingMinutes is { } actualLoading)
        {
            var extra = Math.Max(0m, actualLoading - includedLoading);
            if (extra > 0)
            {
                AddExtraTimeLine(lines,
                    $"Extra laadtijd: {actualLoading:0.##} min (inbegrepen {includedLoading} min)", extra, effectiveRate,
                    "loading", roundingStep, minimumBillable);
            }
        }

        if (effectiveUnloading is { } includedUnloading && actualUnloadingMinutes is { } actualUnloading)
        {
            var extra = Math.Max(0m, actualUnloading - includedUnloading);
            if (extra > 0)
            {
                AddExtraTimeLine(lines,
                    $"Extra lostijd: {actualUnloading:0.##} min (inbegrepen {includedUnloading} min)", extra, effectiveRate,
                    "unloading", roundingStep, minimumBillable);
            }
        }

        return lines;
    }

    private const string MissingExtraTimeRateLabel = "Extra tijd: geef het uurtarief voor extra tijd op";

    private static void AddExtraTimeLine(
        List<PriceBreakdownLine> lines, string label, decimal extraMinutes, decimal? rate, string discriminator,
        int? roundingStepMinutes = null, int? minimumBillableMinutes = null)
    {
        if (rate is null)
        {
            // Carried-over review item: loading AND unloading can both be missing a rate in the
            // same calculation — emit the informational prompt once, not once per activity.
            if (lines.Any(l => l.Label == MissingExtraTimeRateLabel))
            {
                return;
            }

            lines.Add(new PriceBreakdownLine(MissingExtraTimeRateLabel, 0m, "Extra tijd", Informational: true));
            return;
        }

        // Task 10: raw excess minutes (extraMinutes, always > 0 here) round UP to the configured
        // step (e.g. 17 min @ step 15 -> 30 min; no step = no rounding), then the configured
        // minimum billable minutes applies as a floor on top of that.
        var rounded = roundingStepMinutes is { } step && step > 0
            ? Math.Ceiling(extraMinutes / step) * step
            : extraMinutes;
        var billedMinutes = Math.Max(rounded, minimumBillableMinutes ?? 0);

        lines.Add(new PriceBreakdownLine(label, decimal.Round(billedMinutes / 60m * rate.Value, 2), "Extra tijd",
            Proposed: true, LineKey: $"extratime:{discriminator}"));
    }

    /// <summary>Returns true when the rule produced an amount line (false = informational skip).</summary>
    private static bool AddOrderLevelLine(
        List<PriceBreakdownLine> lines, PriceRule rule, PriceCalculationRequest request, PricingAgreement? engagedAgreement,
        BracketOverrideSet overrides)
    {
        var appliedBefore = overrides.AppliedCount;
        var (amount, label, missing) = rule.Basis switch
        {
            PriceRuleBasis.Fixed => (
                (rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m), rule.Name, (string?)null),
            PriceRuleBasis.PerKm => request.DistanceKm is { } km
                ? ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * km, $"{rule.Name} ({km:0.#} km)", null)
                : (0m, rule.Name, "geen afstand gekend"),
            PriceRuleBasis.PerPallet => request.PalletCount is { } pallets
                ? ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * pallets, $"{rule.Name} ({pallets} pallets)", null)
                : (0m, rule.Name, "geen palletaantal gekend"),
            PriceRuleBasis.PerTon => request.WeightKg is { } weight
                ? ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * (weight / 1000m), $"{rule.Name} ({weight / 1000m:0.##} ton)", null)
                : (0m, rule.Name, "geen gewicht gekend"),
            PriceRuleBasis.WeightBracket => request.WeightKg is { } w && BracketAmount(rule, w, request, overrides) is { } bracketAmount
                ? ((rule.BaseAmount ?? 0m) + bracketAmount, $"{rule.Name} ({w:0.#} kg)", null)
                : (0m, rule.Name, "geen gewicht of staffel"),
            PriceRuleBasis.PerLoadingMeter => request.LoadingMeters is { } ldm
                ? ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * ldm, $"{rule.Name} ({ldm:0.##} ldm)", null)
                : (0m, rule.Name, "geen laadmeters gekend"),
            PriceRuleBasis.PerVolume => request.VolumeM3 is { } volume
                ? ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * volume, $"{rule.Name} ({volume:0.##} m³)", null)
                : (0m, rule.Name, "geen volume gekend"),
            PriceRuleBasis.PerStop => request.StopCount is { } stops
                ? rule.Brackets.Count > 0
                    ? BracketAmount(rule, stops, request, overrides) is { } stopAmount
                        ? ((rule.BaseAmount ?? 0m) + stopAmount, $"{rule.Name} ({stops} stops)", null)
                        : (0m, rule.Name, "geen staffel voor dit aantal stops")
                    : ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * stops, $"{rule.Name} ({stops} stops)", null)
                : (0m, rule.Name, "geen stops gekend"),
            _ => (0m, rule.Name, "niet ondersteund"),
        };

        if (missing is not null)
        {
            lines.Add(new PriceBreakdownLine($"{rule.Name}: overgeslagen ({missing})", 0m,
                engagedAgreement?.Name ?? rule.Name, Informational: true,
                RuleId: rule.Id, RuleName: rule.Name,
                AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name, LineKey: RuleLineKey(rule.Id)));
            return false;
        }

        if (rule.MinimumAmount is { } minimum && amount < minimum)
        {
            amount = minimum;
        }

        if (rule.MaximumAmount is { } maximum && amount > maximum)
        {
            amount = maximum;
        }

        var usedOverride = overrides.AppliedCount > appliedBefore;
        lines.Add(new PriceBreakdownLine(usedOverride ? $"{label} — klantafwijking" : label, decimal.Round(amount, 2),
            engagedAgreement?.Name ?? rule.Name,
            RuleId: rule.Id, RuleName: rule.Name,
            AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name, LineKey: RuleLineKey(rule.Id)));
        return true;
    }

    /// <summary>
    /// A rule as reachable through one engaging context: either a standalone rule (no agreement)
    /// or a rule grouped under an applicable agreement — which, for a derived agreement (spec §9),
    /// is the agreement the customer is actually engaged with even though the rule physically
    /// lives on its base-chain root. Tier is the ENGAGING context's specificity, so the same rule
    /// can appear more than once (e.g. via its own root table AND via a derived table) with a
    /// different tier/attribution each time.
    /// </summary>
    private sealed record RuleCandidate(PriceRule Rule, int Tier, PricingAgreement? EngagedAgreement);

    /// <summary>
    /// Deterministic precedence: tier (private beats assigned beats company default, weight 4),
    /// zone-bound beats zone-less (weight 2), then explicit Priority. Two candidates left in an
    /// exact tie are a configuration error — never an arbitrary pick.
    /// </summary>
    private static (RuleCandidate? Candidate, List<RuleCandidate>? Conflicts) SelectRule(IReadOnlyList<RuleCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return (null, null);
        }

        // Specificity: tier dominates; activity (P6) beats both zone dimensions; destination
        // zone beats origin zone (Wave 3 §2). Max within-tier bonus 4+2+1=7 stays below one
        // tier step of 8, and rules without the new dimension keep their exact relative order.
        int Score(RuleCandidate candidate) => candidate.Tier * 8
            + (candidate.Rule.ActivityTypeId is not null ? 4 : 0)
            + (candidate.Rule.ZoneId is not null ? 2 : 0)
            + (candidate.Rule.OriginZoneId is not null ? 1 : 0);

        var ordered = candidates.OrderByDescending(Score).ThenByDescending(c => c.Rule.Priority).ToList();
        var top = ordered.Where(c => Score(c) == Score(ordered[0]) && c.Rule.Priority == ordered[0].Rule.Priority).ToList();
        return top.Count > 1 ? (null, top) : (ordered[0], null);
    }

    /// <summary>Specificity tier of an agreement: private = 2, shared (assigned) = 1, company default = 0.</summary>
    private static int AgreementTier(PricingAgreement agreement) =>
        agreement.CustomerId is not null ? 2
        : agreement.IsShared ? 1
        : 0;

    /// <summary>
    /// Follows a derived agreement's BaseAgreementId chain up to its root (max 3 hops, tenant-
    /// filtered via the pre-loaded, active+windowed <paramref name="agreementsById"/> map). Returns
    /// the root id, or Cycle=true with the visited chain's names when the chain revisits a node
    /// (bad data that bypassed save-time validation) — never loops indefinitely.
    /// </summary>
    private static (Guid? RootId, bool Cycle, List<string> ChainNames) ResolveBaseChainRoot(
        PricingAgreement agreement, IReadOnlyDictionary<Guid, PricingAgreement> agreementsById)
    {
        var chainNames = new List<string> { agreement.Name };
        var visited = new HashSet<Guid> { agreement.Id };
        var current = agreement;
        var depth = 0;
        while (current.BaseAgreementId is { } baseId)
        {
            if (!agreementsById.TryGetValue(baseId, out var next))
            {
                // Base not active/in window at this date (or missing) — no rules resolved, not a cycle.
                return (null, false, chainNames);
            }

            chainNames.Add(next.Name);
            if (!visited.Add(baseId) || ++depth > 3)
            {
                return (null, true, chainNames);
            }

            current = next;
        }

        return (current.Id, false, chainNames);
    }

    private static string? NormalizeCountryCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>
    /// Spec ch. 11: the billable quantity may differ from the physical quantity (an oversized
    /// pallet can count as two pallet places) — the physical order is never altered.
    /// </summary>
    private static decimal BillableQuantity(PriceRule rule, PriceCalculationLineInput line)
    {
        if (rule.OversizeBillableFactor is not { } factor || line.Details is not { Count: > 0 } details)
        {
            return line.Quantity;
        }

        var covered = 0m;
        var billable = 0m;
        foreach (var detail in details)
        {
            var oversized = (rule.OversizeLengthCm is { } maxLength && detail.LengthCm is { } length && length > maxLength)
                            || (rule.OversizeWidthCm is { } maxWidth && detail.WidthCm is { } width && width > maxWidth);
            billable += detail.Quantity * (oversized ? factor : 1m);
            covered += detail.Quantity;
        }

        // Units not described by cargo details bill 1:1.
        if (line.Quantity > covered)
        {
            billable += line.Quantity - covered;
        }

        return billable;
    }

    private static decimal? ComputeRuleAmount(
        PriceRule rule, decimal billableQuantity, PriceCalculationRequest request, BracketOverrideSet overrides)
    {
        // Hourly quantity pipeline (spec §13): round UP to the configured step (per started
        // interval), then apply the minimum billable duration.
        if (rule.Basis == PriceRuleBasis.Hourly)
        {
            if (rule.QuantityRoundingStep is { } step && step > 0)
            {
                billableQuantity = Math.Ceiling(billableQuantity / step) * step;
            }

            if (rule.MinimumQuantity is { } minimumQuantity && billableQuantity < minimumQuantity)
            {
                billableQuantity = minimumQuantity;
            }
        }

        decimal? computed = rule.Basis switch
        {
            PriceRuleBasis.PerUnit or PriceRuleBasis.Hourly =>
                rule.UnitPrice is { } rate ? rate * billableQuantity : null,
            PriceRuleBasis.Fixed => rule.UnitPrice,
            PriceRuleBasis.QuantityBracket => BracketAmount(rule, billableQuantity, request, overrides),
            PriceRuleBasis.WeightBracket => request.WeightKg is { } weight ? BracketAmount(rule, weight, request, overrides) : null,
            PriceRuleBasis.PerKm => request.DistanceKm is { } km && rule.UnitPrice is { } kmRate ? kmRate * km : null,
            PriceRuleBasis.PerPallet => request.PalletCount is { } pallets && rule.UnitPrice is { } palletRate ? palletRate * pallets : null,
            PriceRuleBasis.PerTon => request.WeightKg is { } kg && rule.UnitPrice is { } tonRate ? tonRate * (kg / 1000m) : null,
            _ => null,
        };
        if (computed is null)
        {
            return null;
        }

        var amount = computed.Value + (rule.BaseAmount ?? 0m);
        if (rule.MinimumAmount is { } minimum && amount < minimum)
        {
            amount = minimum;
        }

        if (rule.MaximumAmount is { } maximum && amount > maximum)
        {
            amount = maximum;
        }

        return amount;
    }

    /// <summary>
    /// A bracket row matches when the quantity range holds AND every cap the row fills (weight/
    /// volume/loading-meters) holds too — a filled cap with a MISSING request measure never
    /// matches (the row demands a dimension the order doesn't know). Among matches, the TIGHTEST
    /// wins: highest FromQuantity first (existing "last bracket" behaviour), then the smallest
    /// filled caps (nulls sort last) — so a carrier's "kg tot / cbm tot / ldm tot" table picks the
    /// narrowest row whose caps still hold.
    /// </summary>
    private static PriceRuleBracket? FindMatchingBracket(PriceRule rule, decimal value, PriceCalculationRequest request)
    {
        bool CapHolds(decimal? cap, decimal? measure) => cap is null || (measure is not null && measure <= cap);

        return rule.Brackets
            .Where(b => value >= b.FromQuantity && (b.ToQuantity is null || value <= b.ToQuantity)
                        && CapHolds(b.WeightToKg, request.WeightKg)
                        && CapHolds(b.VolumeToM3, request.VolumeM3)
                        && CapHolds(b.LoadingMetersTo, request.LoadingMeters))
            .OrderByDescending(b => b.FromQuantity)
            .ThenBy(b => b.WeightToKg ?? decimal.MaxValue)
            .ThenBy(b => b.VolumeToM3 ?? decimal.MaxValue)
            .ThenBy(b => b.LoadingMetersTo ?? decimal.MaxValue)
            .FirstOrDefault();
    }

    private static decimal? BracketAmount(PriceRule rule, decimal value, PriceCalculationRequest request, BracketOverrideSet overrides)
    {
        if (rule.BracketMode == BracketSelectionMode.PerNextUnit)
        {
            return PerNextUnitBracketAmount(rule, value, request, overrides);
        }

        var bracket = FindMatchingBracket(rule, value, request);
        if (bracket is null)
        {
            return null;
        }

        // Row-level customer override: replaces exactly this row's price; PricePerExtraUnit only
        // when the override provides one (most overrides change just the base row price).
        var rowOverride = overrides.Find(rule, bracket);
        var amount = rowOverride?.Price ?? bracket.Price;
        var perExtra = rowOverride?.PricePerExtraUnit ?? bracket.PricePerExtraUnit;
        if (bracket.ToQuantity is null && perExtra is { } extra && value > bracket.FromQuantity)
        {
            amount += extra * (value - bracket.FromQuantity);
        }

        return amount;
    }

    /// <summary>
    /// Progressive per-piece pricing (e.g. "1e stuk €60, 2e €55, 3e €50, 4e en verder €45"): the
    /// amount is the sum of the bracket price containing each whole unit index 1..floor(qty), plus
    /// the fractional remainder billed at the bracket containing ceil(qty). A unit index with no
    /// matching bracket blocks the whole calculation (null → "Geen staffel voor …").
    /// </summary>
    private static decimal? PerNextUnitBracketAmount(PriceRule rule, decimal quantity, PriceCalculationRequest request, BracketOverrideSet overrides)
    {
        var wholeUnits = (int)Math.Floor(quantity);
        var fraction = quantity - wholeUnits;
        var total = 0m;
        decimal RowPrice(PriceRuleBracket bracket) => overrides.Find(rule, bracket)?.Price ?? bracket.Price;

        for (var i = 1; i <= wholeUnits; i++)
        {
            var bracket = FindMatchingBracket(rule, i, request);
            if (bracket is null)
            {
                return null;
            }

            total += RowPrice(bracket);
        }

        if (fraction > 0)
        {
            var bracket = FindMatchingBracket(rule, Math.Ceiling(quantity), request);
            if (bracket is null)
            {
                return null;
            }

            total += fraction * RowPrice(bracket);
        }

        return total;
    }

    /// <summary>
    /// The order customer's date-valid row-level overrides, keyed by rule. Find() only ever
    /// substitutes on rules that are NOT customer-private (a private rule already IS the
    /// customer's own price) and counts every hit so callers can attribute "klantafwijking"
    /// on the produced line. Duplicate rows for one identity were already flagged as a
    /// blocking configuration error before any computation ran.
    /// </summary>
    private sealed class BracketOverrideSet
    {
        private readonly ILookup<Guid, PriceRuleBracketOverride> _byRuleId;

        public BracketOverrideSet(IReadOnlyList<PriceRuleBracketOverride> rows)
            => _byRuleId = rows.ToLookup(o => o.PriceRuleId);

        public int AppliedCount { get; private set; }

        public PriceRuleBracketOverride? Find(PriceRule rule, PriceRuleBracket bracket)
        {
            if (rule.CustomerId is not null)
            {
                return null;
            }

            var match = _byRuleId[rule.Id].FirstOrDefault(o =>
                o.FromQuantity == bracket.FromQuantity && o.ToQuantity == bracket.ToQuantity
                && o.WeightToKg == bracket.WeightToKg && o.VolumeToM3 == bracket.VolumeToM3
                && o.LoadingMetersTo == bracket.LoadingMetersTo);
            if (match is not null)
            {
                AppliedCount++;
            }

            return match;
        }
    }

    private async Task<List<string>> BuildDiagnosticsAsync(
        PriceCalculationRequest request, PricingZone? zone,
        IReadOnlyDictionary<Guid, string> unitNames, CancellationToken cancellationToken)
    {
        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == request.CustomerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var diagnostics = new List<string>
        {
            $"Klant: {customerName ?? request.CustomerId.ToString()}",
            $"Tariefdatum: {request.Date:dd/MM/yyyy}",
        };
        foreach (var line in request.Lines)
        {
            diagnostics.Add($"Eenheid: {line.Quantity:0.##} × {unitNames.GetValueOrDefault(line.UnitTypeId, "eenheid")}");
        }

        if (request.WeightKg is { } weight)
        {
            diagnostics.Add($"Gewicht: {weight:0.#} kg");
        }

        if (request.PalletCount is { } pallets)
        {
            diagnostics.Add($"Palletplaatsen: {pallets}");
        }

        if (!string.IsNullOrWhiteSpace(request.DeliveryPostalCode))
        {
            diagnostics.Add($"Leverpostcode: {request.DeliveryPostalCode}"
                            + (zone is null ? " (geen zone gevonden)" : $" — zone {zone.Code}"));
        }

        return diagnostics;
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
