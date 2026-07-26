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

        var unitTypeIds = request.Lines.Select(l => l.UnitTypeId).Distinct().ToList();
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && unitTypeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

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

        var candidateRules = await _dbContext.PriceRules.AsNoTracking()
            .Include(r => r.Brackets)
            .Where(r => r.TenantId == tenantId && r.IsActive
                        && (r.CustomerId == null || r.CustomerId == request.CustomerId)
                        && r.EffectiveFrom <= request.Date
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);
        var rulesByAgreementId = candidateRules.Where(r => r.AgreementId is not null)
            .ToLookup(r => r.AgreementId!.Value);
        var standaloneRules = candidateRules.Where(r => r.AgreementId is null).ToList();

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
        foreach (var line in request.Lines)
        {
            var unitName = unitNames.GetValueOrDefault(line.UnitTypeId, "eenheid");
            var forUnit = allCandidates
                .Where(c => c.Rule.UnitTypeId == line.UnitTypeId && (c.Rule.ZoneId == null || (zone is not null && c.Rule.ZoneId == zone.Id)))
                .ToList();
            var (candidate, conflicts) = SelectRule(forUnit);
            if (conflicts is not null)
            {
                configurationError = $"Conflicterende tariefregels voor {unitName}: "
                    + string.Join(" én ", conflicts.Select(c => $"'{c.Rule.Name}'"))
                    + ". Corrigeer de tarieven (geldigheid, zone of prioriteit).";
                lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
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
            var amount = ComputeRuleAmount(rule, billable, request);
            if (amount is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen staffel voor {billable:0.##} × {unitName}", 0m, rule.Name,
                    RuleId: rule.Id, RuleName: rule.Name));
                requiresManual = true;
                continue;
            }

            var zoneSuffix = rule.ZoneId is not null && zone is not null ? $" (zone {zone.Code})" : "";
            var billableSuffix = billable != line.Quantity ? $" — factureerbaar: {billable:0.##}" : "";
            lines.Add(new PriceBreakdownLine(
                $"{line.Quantity:0.##} × {unitName}{zoneSuffix}{billableSuffix}",
                decimal.Round(amount.Value, 2), rule.Name,
                RuleId: rule.Id, RuleName: rule.Name,
                AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name,
                ActualQuantity: line.Quantity, BillableQuantity: billable));
        }

        // --- Order-level rules (no unit): forfaits, km/pallet/ton components ------------------
        var orderLevelCandidates = allCandidates
            .Where(c => c.Rule.UnitTypeId == null
                        && c.Rule.Basis is PriceRuleBasis.Fixed or PriceRuleBasis.PerKm or PriceRuleBasis.PerPallet
                            or PriceRuleBasis.PerTon or PriceRuleBasis.WeightBracket
                            or PriceRuleBasis.PerLoadingMeter or PriceRuleBasis.PerVolume or PriceRuleBasis.PerStop
                        && (c.Rule.ZoneId == null || (zone is not null && c.Rule.ZoneId == zone.Id)))
            .ToList();

        if (anyRuleMatched)
        {
            // Component model: an agreement engaged by a matched unit rule also contributes
            // its order-level components (base cost, km price, ...).
            foreach (var candidate in orderLevelCandidates.Where(c => c.EngagedAgreement is { } a && engagedAgreements.ContainsKey(a.Id)))
            {
                AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement);
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
                        producedAmount |= AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement);
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
                    producedAmount |= AddOrderLevelLine(lines, candidate.Rule, request, candidate.EngagedAgreement);
                }
            }

            if (producedAmount)
            {
                // The order-wide tariff replaces the per-unit "Ontbrekend" lines.
                lines.RemoveAll(l => l.Source == "Ontbrekend" && !l.Informational);
                requiresManual = configurationError is not null;
                anyRuleMatched = true;
            }
        }

        // --- Agreement post-processing: derivation modifiers, adjustment, minimum/maximum, surcharges ---
        // Driven by which agreements actually produced a breakdown line — not just `engagedAgreements`
        // (unit-line engagement only), since an order-level-only tariff (no unit rule matched) also
        // engages its agreement's minimum/maximum/surcharges/modifiers.
        foreach (var agreement in lines
                     .Where(l => !l.Informational && l.AgreementId is not null)
                     .Select(l => l.AgreementId!.Value)
                     .Distinct()
                     .Select(id => agreementsById.GetValueOrDefault(id))
                     .Where(a => a is not null)
                     .Select(a => a!)
                     .ToList())
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
                        AgreementId: agreement.Id, AgreementName: agreement.Name));
                    subtotal += modifierAmount;
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
                        agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name));
                    subtotal += percentAmount;
                }

                if (assignment.FixedAdjustment is { } fixedAdjustment)
                {
                    var fixedAmount = decimal.Round(fixedAdjustment, 2);
                    lines.Add(new PriceBreakdownLine("Klantafspraak vast bedrag", fixedAmount,
                        agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name));
                    subtotal += fixedAmount;
                }
            }

            if (agreement.MinimumAmount is { } minimum && subtotal < minimum)
            {
                lines.Add(new PriceBreakdownLine($"Minimumtarief {agreement.Name}", decimal.Round(minimum - subtotal, 2),
                    agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name));
                subtotal = minimum;
            }

            if (agreement.MaximumAmount is { } maximum && subtotal > maximum)
            {
                lines.Add(new PriceBreakdownLine($"Maximumtarief {agreement.Name}", decimal.Round(maximum - subtotal, 2),
                    agreement.Name, AgreementId: agreement.Id, AgreementName: agreement.Name));
                subtotal = maximum;
            }

            foreach (var surcharge in agreement.Surcharges.OrderBy(s => s.Name))
            {
                var amount = surcharge.Kind == SurchargeKind.Percent
                    ? decimal.Round(subtotal * surcharge.Value / 100m, 2)
                    : decimal.Round(surcharge.Value, 2);
                lines.Add(new PriceBreakdownLine(surcharge.Name, amount, agreement.Name,
                    AgreementId: agreement.Id, AgreementName: agreement.Name));
            }
        }

        var subtotalBeforeServices = lines.Where(l => !l.Informational).Sum(l => l.Amount);

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

        foreach (var option in allOptions)
        {
            var over = overrides.GetValueOrDefault(option.Id);
            var isSelected = selectedIds.Contains(option.Id);

            // Auto-apply: a contract service the engine adds without the user selecting it —
            // active, effectively auto (customer override wins), not disabled for this customer,
            // and (when ADR-only) the order is actually ADR.
            var effectiveAutoApply = over?.AutoApplyOverride ?? option.AutoApply;
            var autoEligible = effectiveAutoApply && over?.Disabled != true
                                && (!option.OnlyForAdr || request.AdrRequired == true);
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
                // Entered quantity always wins; otherwise derived from the matching order line(s).
                var derived = option.UnitTypeId is { } unitTypeId
                    ? request.Lines.Where(l => l.UnitTypeId == unitTypeId).Sum(l => l.Quantity)
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
                if (qty is null)
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
                if (qty is null)
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
                if (qty is null)
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
                if (qty is null)
                {
                    lines.Add(new PriceBreakdownLine($"{option.Name}: geen laadmeters gekend", 0m, source, Informational: true));
                    continue;
                }

                quantity = qty;
                amount = decimal.Round(value * qty.Value, 2);
                label = $"{option.Name} ({qty.Value:0.##} ldm)";
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

            lines.Add(new PriceBreakdownLine(label, amount, source));
            serviceLines.Add(new PriceServiceLine(
                option.Id, option.Name, option.Kind, value, amount, quantity, invoiceLabel, source, isAutoApplied));
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
        return new PriceCalculationResult(
            lines, decimal.Round(total, 2), decimal.Round(totalWithInformational, 2), "EUR",
            zone?.Code, zone?.Name, requiresManual, serviceLines,
            TariffDate: request.Date, ConfigurationError: configurationError, Diagnostics: diagnostics);
    }

    /// <summary>Returns true when the rule produced an amount line (false = informational skip).</summary>
    private static bool AddOrderLevelLine(
        List<PriceBreakdownLine> lines, PriceRule rule, PriceCalculationRequest request, PricingAgreement? engagedAgreement)
    {
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
            PriceRuleBasis.WeightBracket => request.WeightKg is { } w && BracketAmount(rule, w, request) is { } bracketAmount
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
                    ? BracketAmount(rule, stops, request) is { } stopAmount
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
                AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name));
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

        lines.Add(new PriceBreakdownLine(label, decimal.Round(amount, 2),
            engagedAgreement?.Name ?? rule.Name,
            RuleId: rule.Id, RuleName: rule.Name,
            AgreementId: engagedAgreement?.Id, AgreementName: engagedAgreement?.Name));
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

        int Score(RuleCandidate candidate) => candidate.Tier * 4 + (candidate.Rule.ZoneId is not null ? 2 : 0);

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

    private static decimal? ComputeRuleAmount(PriceRule rule, decimal billableQuantity, PriceCalculationRequest request)
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
            PriceRuleBasis.QuantityBracket => BracketAmount(rule, billableQuantity, request),
            PriceRuleBasis.WeightBracket => request.WeightKg is { } weight ? BracketAmount(rule, weight, request) : null,
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

    private static decimal? BracketAmount(PriceRule rule, decimal value, PriceCalculationRequest request)
    {
        if (rule.BracketMode == BracketSelectionMode.PerNextUnit)
        {
            return PerNextUnitBracketAmount(rule, value, request);
        }

        var bracket = FindMatchingBracket(rule, value, request);
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

    /// <summary>
    /// Progressive per-piece pricing (e.g. "1e stuk €60, 2e €55, 3e €50, 4e en verder €45"): the
    /// amount is the sum of the bracket price containing each whole unit index 1..floor(qty), plus
    /// the fractional remainder billed at the bracket containing ceil(qty). A unit index with no
    /// matching bracket blocks the whole calculation (null → "Geen staffel voor …").
    /// </summary>
    private static decimal? PerNextUnitBracketAmount(PriceRule rule, decimal quantity, PriceCalculationRequest request)
    {
        var wholeUnits = (int)Math.Floor(quantity);
        var fraction = quantity - wholeUnits;
        var total = 0m;

        for (var i = 1; i <= wholeUnits; i++)
        {
            var bracket = FindMatchingBracket(rule, i, request);
            if (bracket is null)
            {
                return null;
            }

            total += bracket.Price;
        }

        if (fraction > 0)
        {
            var bracket = FindMatchingBracket(rule, Math.Ceiling(quantity), request);
            if (bracket is null)
            {
                return null;
            }

            total += fraction * bracket.Price;
        }

        return total;
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
