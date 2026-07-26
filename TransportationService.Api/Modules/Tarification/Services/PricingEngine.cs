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

        var candidateRules = await _dbContext.PriceRules.AsNoTracking()
            .Include(r => r.Brackets)
            .Include(r => r.Agreement!.Surcharges)
            .Where(r => r.TenantId == tenantId && r.IsActive
                        && (r.CustomerId == null || r.CustomerId == request.CustomerId)
                        && r.EffectiveFrom <= request.Date
                        && (r.EffectiveUntil == null || r.EffectiveUntil >= request.Date))
            .ToListAsync(cancellationToken);

        // A rule inside an agreement only applies while its agreement applies. A shared
        // agreement additionally requires an active assignment for this customer.
        candidateRules = candidateRules
            .Where(r => r.AgreementId is null || (r.Agreement is { IsActive: true } agreement
                        && agreement.EffectiveFrom <= request.Date
                        && (agreement.EffectiveUntil is null || agreement.EffectiveUntil >= request.Date)
                        && (agreement.IsShared
                            ? assignmentByAgreementId.ContainsKey(agreement.Id)
                            : (agreement.CustomerId is null || agreement.CustomerId == request.CustomerId))))
            .ToList();

        // --- Unit lines: pick the most specific rule per line ---------------------------------
        var anyRuleMatched = false;
        var engagedAgreements = new Dictionary<Guid, PricingAgreement>();
        foreach (var line in request.Lines)
        {
            var unitName = unitNames.GetValueOrDefault(line.UnitTypeId, "eenheid");
            var forUnit = candidateRules
                .Where(r => r.UnitTypeId == line.UnitTypeId && (r.ZoneId == null || (zone is not null && r.ZoneId == zone.Id)))
                .ToList();
            var (rule, conflicts) = SelectRule(forUnit, RuleTier);
            if (conflicts is not null)
            {
                configurationError = $"Conflicterende tariefregels voor {unitName}: "
                    + string.Join(" én ", conflicts.Select(c => $"'{c.Name}'"))
                    + ". Corrigeer de tarieven (geldigheid, zone of prioriteit).";
                lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                requiresManual = true;
                continue;
            }

            if (rule is null)
            {
                lines.Add(new PriceBreakdownLine($"Geen tarief geconfigureerd voor {unitName}", 0m, "Ontbrekend"));
                requiresManual = true;
                continue;
            }

            anyRuleMatched = true;
            if (rule.Agreement is { } lineAgreement)
            {
                engagedAgreements.TryAdd(lineAgreement.Id, lineAgreement);
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
                AgreementId: rule.AgreementId, AgreementName: rule.Agreement?.Name,
                ActualQuantity: line.Quantity, BillableQuantity: billable));
        }

        // --- Order-level rules (no unit): forfaits, km/pallet/ton components ------------------
        var orderLevelRules = candidateRules
            .Where(r => r.UnitTypeId == null
                        && r.Basis is PriceRuleBasis.Fixed or PriceRuleBasis.PerKm or PriceRuleBasis.PerPallet
                            or PriceRuleBasis.PerTon or PriceRuleBasis.WeightBracket
                            or PriceRuleBasis.PerLoadingMeter or PriceRuleBasis.PerVolume or PriceRuleBasis.PerStop
                        && (r.ZoneId == null || (zone is not null && r.ZoneId == zone.Id)))
            .ToList();

        if (anyRuleMatched)
        {
            // Component model: an agreement engaged by a matched unit rule also contributes
            // its order-level components (base cost, km price, ...).
            foreach (var rule in orderLevelRules.Where(r => r.AgreementId is { } aid && engagedAgreements.ContainsKey(aid)))
            {
                AddOrderLevelLine(lines, rule, request);
            }
        }
        else if (orderLevelRules.Count > 0)
        {
            // No unit line priced: an order-level tariff (converted rate card or forfait) is
            // the price. The most specific applicable agreement wins; standalone rules only
            // apply when no agreement-grouped order tariff exists.
            var producedAmount = false;
            var agreements = orderLevelRules
                .Where(r => r.Agreement is not null)
                .Select(r => r.Agreement!)
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
                    foreach (var rule in orderLevelRules.Where(r => r.AgreementId == best[0].Id))
                    {
                        producedAmount |= AddOrderLevelLine(lines, rule, request);
                    }
                }
            }
            else
            {
                // One primary pricing basis (spec §10/18): standalone order-level rules never
                // sum across bases — the single most specific rule wins, an exact tie blocks.
                var (rule, conflicts) = SelectRule(orderLevelRules.Where(r => r.AgreementId is null).ToList(), RuleTier);
                if (conflicts is not null)
                {
                    configurationError = "Conflicterende tariefregels: "
                        + string.Join(" én ", conflicts.Select(c => $"'{c.Name}'"))
                        + ". Kies één prijsbasis of onderscheid ze met prioriteit.";
                    lines.Add(new PriceBreakdownLine(configurationError, 0m, "Configuratiefout"));
                    requiresManual = true;
                }
                else if (rule is not null)
                {
                    producedAmount |= AddOrderLevelLine(lines, rule, request);
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

        // --- Agreement post-processing: minimum + automatic surcharges ------------------------
        foreach (var agreement in lines
                     .Where(l => !l.Informational && l.AgreementId is not null)
                     .Select(l => l.AgreementId!.Value)
                     .Distinct()
                     .Select(id => candidateRules.Select(r => r.Agreement).First(a => a?.Id == id)!)
                     .ToList())
        {
            var subtotal = lines.Where(l => !l.Informational && l.AgreementId == agreement.Id).Sum(l => l.Amount);

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

        // Service options: order-level override > customer override > global default (§19);
        // Percent applies to the base subtotal. Customer overrides are date-aware and can
        // disable a globally available service for this customer.
        var serviceLines = new List<PriceServiceLine>();
        var selections = request.Services is { Count: > 0 }
            ? request.Services
            : request.ServiceOptionIds.Select(id => new PriceServiceInput(id)).ToList();
        if (selections.Count > 0)
        {
            var quantities = selections
                .GroupBy(s => s.ServiceOptionId)
                .ToDictionary(g => g.Key, g => g.First().Quantity);
            var optionIds = quantities.Keys.ToList();
            var options = await _dbContext.ServiceOptions.AsNoTracking()
                .Where(o => o.TenantId == tenantId && optionIds.Contains(o.Id) && o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
                .ToListAsync(cancellationToken);
            var overrides = (await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
                    .Where(p => p.TenantId == tenantId && p.CustomerId == request.CustomerId && optionIds.Contains(p.ServiceOptionId))
                    .ToListAsync(cancellationToken))
                .Where(p => (p.EffectiveFrom is null || p.EffectiveFrom <= request.Date)
                            && (p.EffectiveUntil is null || p.EffectiveUntil >= request.Date))
                .ToDictionary(p => p.ServiceOptionId);

            foreach (var option in options)
            {
                var over = overrides.GetValueOrDefault(option.Id);
                if (over?.Disabled == true)
                {
                    lines.Add(new PriceBreakdownLine(
                        $"{option.Name}: uitgeschakeld voor deze klant", 0m, "Klanttarief", Informational: true));
                    continue;
                }

                var value = over?.Value ?? option.DefaultValue;
                var source = over?.Value is not null ? "Klanttarief" : "Algemene standaard";
                var invoiceLabel = over?.InvoiceDescription ?? option.InvoiceDescription;
                decimal amount;
                decimal? quantity = null;
                string label = option.Name;
                if (option.Kind is SurchargeKind.PerHour or SurchargeKind.PerStop)
                {
                    var unitLabel = option.Kind == SurchargeKind.PerHour ? "uur" : "stops";
                    if (quantities.GetValueOrDefault(option.Id) is not { } enteredQuantity || enteredQuantity <= 0)
                    {
                        // Quantity-based services need an entered quantity (hours / stops).
                        lines.Add(new PriceBreakdownLine(
                            $"{option.Name}: geef het aantal {(option.Kind == SurchargeKind.PerHour ? "uur" : "stop")} op",
                            0m, source, Informational: true));
                        continue;
                    }

                    quantity = enteredQuantity;
                    amount = decimal.Round(value * enteredQuantity, 2);
                    label = $"{option.Name} ({enteredQuantity:0.##} {unitLabel})";
                }
                else if (option.Kind == SurchargeKind.Percent)
                {
                    amount = decimal.Round(subtotalBeforeServices * value / 100m, 2);
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
                serviceLines.Add(new PriceServiceLine(option.Id, option.Name, option.Kind, value, amount, quantity, invoiceLabel, source));
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
    private static bool AddOrderLevelLine(List<PriceBreakdownLine> lines, PriceRule rule, PriceCalculationRequest request)
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
            PriceRuleBasis.WeightBracket => request.WeightKg is { } w && BracketAmount(rule, w) is { } bracketAmount
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
                    ? BracketAmount(rule, stops) is { } stopAmount
                        ? ((rule.BaseAmount ?? 0m) + stopAmount, $"{rule.Name} ({stops} stops)", null)
                        : (0m, rule.Name, "geen staffel voor dit aantal stops")
                    : ((rule.BaseAmount ?? 0m) + (rule.UnitPrice ?? 0m) * stops, $"{rule.Name} ({stops} stops)", null)
                : (0m, rule.Name, "geen stops gekend"),
            _ => (0m, rule.Name, "niet ondersteund"),
        };

        if (missing is not null)
        {
            lines.Add(new PriceBreakdownLine($"{rule.Name}: overgeslagen ({missing})", 0m,
                rule.Agreement?.Name ?? rule.Name, Informational: true,
                RuleId: rule.Id, RuleName: rule.Name,
                AgreementId: rule.AgreementId, AgreementName: rule.Agreement?.Name));
            return false;
        }

        if (rule.MinimumAmount is { } minimum && amount < minimum)
        {
            amount = minimum;
        }

        lines.Add(new PriceBreakdownLine(label, decimal.Round(amount, 2),
            rule.Agreement?.Name ?? rule.Name,
            RuleId: rule.Id, RuleName: rule.Name,
            AgreementId: rule.AgreementId, AgreementName: rule.Agreement?.Name));
        return true;
    }

    /// <summary>
    /// Deterministic precedence: tier (private beats assigned beats company default, weight 4),
    /// zone-bound beats zone-less (weight 2), then explicit Priority. Two rules left in an exact
    /// tie are a configuration error — never an arbitrary pick.
    /// </summary>
    private static (PriceRule? Rule, List<PriceRule>? Conflicts) SelectRule(
        IReadOnlyList<PriceRule> candidates, Func<PriceRule, int> tier)
    {
        if (candidates.Count == 0)
        {
            return (null, null);
        }

        int Score(PriceRule rule) => tier(rule) * 4 + (rule.ZoneId is not null ? 2 : 0);

        var ordered = candidates.OrderByDescending(Score).ThenByDescending(r => r.Priority).ToList();
        var top = ordered.Where(r => Score(r) == Score(ordered[0]) && r.Priority == ordered[0].Priority).ToList();
        return top.Count > 1 ? (null, top) : (ordered[0], null);
    }

    /// <summary>
    /// Specificity tier of a rule: private (own customer rule, or grouped under a customer-owned
    /// agreement) = 2; grouped under a shared agreement (candidateRules already guarantees an
    /// active assignment exists whenever such a rule is present) = 1; company default = 0.
    /// </summary>
    private static int RuleTier(PriceRule rule) =>
        rule.CustomerId is not null || rule.Agreement?.CustomerId is not null ? 2
        : rule.Agreement is { IsShared: true } ? 1
        : 0;

    /// <summary>Same tier semantics as <see cref="RuleTier"/>, applied to an agreement directly.</summary>
    private static int AgreementTier(PricingAgreement agreement) =>
        agreement.CustomerId is not null ? 2
        : agreement.IsShared ? 1
        : 0;

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
            PriceRuleBasis.QuantityBracket => BracketAmount(rule, billableQuantity),
            PriceRuleBasis.WeightBracket => request.WeightKg is { } weight ? BracketAmount(rule, weight) : null,
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

        return amount;
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
