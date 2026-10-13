using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Tarification.Services;

/// <summary>
/// One resulting group after applying a <see cref="DegressionScope"/> merge (spec §29-31): a
/// key/label for the discount line's LineKey/label, and the summed quantity per unit type across
/// every original <see cref="PriceCalculationGroup"/> that merged into it.
/// </summary>
public sealed record MergedUnitGroup(string Key, string Label, IReadOnlyDictionary<Guid, decimal> Quantities);

/// <summary>
/// Pure, dependency-free math for combined-unit degression discounts — kept out of
/// <see cref="PricingEngine"/> so grouping, equivalent-count and apportioning logic can be unit
/// tested directly without a database. Never rounds internally except the final eligible-base
/// caller (PricingEngine), which uses <c>decimal.Round(x, 2)</c> on the discount amount itself.
/// </summary>
public static class CombinedUnitDiscountMath
{
    /// <summary>
    /// Applies the discount's <see cref="DegressionScope"/> to the order's (already per-stop)
    /// groups: Order merges everything into one; Stop keeps every incoming group separate;
    /// DeliveryAddress merges groups that share a non-null <see cref="PriceCalculationGroup.AddressKey"/>
    /// (a null AddressKey never merges with anything — it stays its own group).
    /// </summary>
    public static IReadOnlyList<MergedUnitGroup> MergeGroups(IReadOnlyList<PriceCalculationGroup> groups, DegressionScope scope)
    {
        if (groups.Count == 0)
        {
            return [];
        }

        if (scope == DegressionScope.Order)
        {
            var merged = new Dictionary<Guid, decimal>();
            foreach (var group in groups)
            {
                foreach (var unit in group.Units)
                {
                    merged[unit.UnitTypeId] = merged.GetValueOrDefault(unit.UnitTypeId) + unit.Quantity;
                }
            }

            return [new MergedUnitGroup("order", "Hele order", merged)];
        }

        if (scope == DegressionScope.Stop)
        {
            return groups.Select(g => new MergedUnitGroup(g.GroupKey, g.GroupLabel, SumQuantities(g))).ToList();
        }

        // DeliveryAddress: groups sharing a non-null AddressKey merge; null AddressKey groups stand alone.
        var result = new List<MergedUnitGroup>();
        foreach (var byAddress in groups.GroupBy(g => g.AddressKey))
        {
            if (byAddress.Key is null)
            {
                foreach (var group in byAddress)
                {
                    result.Add(new MergedUnitGroup(group.GroupKey, group.GroupLabel, SumQuantities(group)));
                }

                continue;
            }

            var merged = new Dictionary<Guid, decimal>();
            foreach (var group in byAddress)
            {
                foreach (var unit in group.Units)
                {
                    merged[unit.UnitTypeId] = merged.GetValueOrDefault(unit.UnitTypeId) + unit.Quantity;
                }
            }

            result.Add(new MergedUnitGroup(byAddress.Key, byAddress.First().GroupLabel, merged));
        }

        return result;
    }

    private static Dictionary<Guid, decimal> SumQuantities(PriceCalculationGroup group) =>
        group.Units
            .GroupBy(u => u.UnitTypeId)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Quantity));

    /// <summary>
    /// The group's weighted equivalent count: Σ (group quantity of unit × its configured factor)
    /// over the discount's configured units only — a unit type not configured on the discount
    /// never contributes, even if it appears in the group.
    /// </summary>
    public static decimal ComputeEquivalent(
        IReadOnlyDictionary<Guid, decimal> groupQuantities, IReadOnlyList<(Guid UnitTypeId, decimal Factor)> discountUnits)
    {
        var total = 0m;
        foreach (var (unitTypeId, factor) in discountUnits)
        {
            total += groupQuantities.GetValueOrDefault(unitTypeId) * factor;
        }

        return total;
    }

    /// <summary>First tier whose [FromCount, ToCount] range holds the equivalent (ToCount null = open-ended); null when none match.</summary>
    public static CombinedUnitDiscountTier? FindTier(IReadOnlyList<CombinedUnitDiscountTier> tiers, decimal equivalent) =>
        tiers.FirstOrDefault(t => equivalent >= t.FromCount && (t.ToCount is null || equivalent <= t.ToCount));

    /// <summary>
    /// The group's eligible base: for each unit type configured on the discount that has a priced
    /// breakdown line, the group's SHARE of that line's amount — its own quantity of the unit
    /// divided by the unit's total quantity across every (scope-merged) group — summed across the
    /// discount's units. A unit with zero total quantity across groups, or no breakdown line,
    /// contributes nothing (never divides by zero).
    /// </summary>
    public static decimal ComputeEligibleBase(
        IReadOnlyDictionary<Guid, decimal> groupQuantities,
        IReadOnlyDictionary<Guid, decimal> totalQuantitiesAcrossGroups,
        IReadOnlyDictionary<Guid, decimal> lineAmountsByUnitType,
        IEnumerable<Guid> discountUnitTypeIds)
    {
        var eligible = 0m;
        foreach (var unitTypeId in discountUnitTypeIds)
        {
            if (!lineAmountsByUnitType.TryGetValue(unitTypeId, out var lineAmount))
            {
                continue;
            }

            var total = totalQuantitiesAcrossGroups.GetValueOrDefault(unitTypeId);
            if (total <= 0)
            {
                continue;
            }

            var groupQty = groupQuantities.GetValueOrDefault(unitTypeId);
            eligible += lineAmount * (groupQty / total);
        }

        return eligible;
    }
}
