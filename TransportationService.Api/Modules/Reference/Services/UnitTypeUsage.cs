using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Reference.Services;

/// <summary>Where a unit is being chosen — each context has its own master-data switch.</summary>
public enum UnitUsage
{
    /// <summary>Order / goods-line entry: <c>AllowForOrderEntry</c>.</summary>
    OrderEntry,

    /// <summary>Tariffs, service options, customer unit mapping, price lines: <c>AllowForPricing</c>.</summary>
    Pricing,
}

/// <summary>
/// MD-H8 (final follow-up): the ONE validation for a unit chosen on a record — order entry and
/// every pricing / customer configuration endpoint. A NEWLY chosen unit must exist in the tenant's
/// master, be active and be allowed for that context; units the record ALREADY carries stay valid
/// (deactivating a unit later never blocks editing an existing tariff or order). An unknown unit is
/// never stored silently.
/// </summary>
public static class UnitTypeUsage
{
    /// <summary>Uppercases a managed unit code; blank → null.</summary>
    public static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    public static async Task EnsureCodesAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyList<(string Code, string Field)> requested, UnitUsage usage,
        IReadOnlySet<string> alreadyUsed, CancellationToken cancellationToken)
    {
        var toCheck = requested.Where(r => !alreadyUsed.Contains(r.Code)).ToList();
        if (toCheck.Count == 0)
        {
            return;
        }

        var codes = toCheck.Select(r => r.Code).Distinct().ToList();
        var units = await db.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && codes.Contains(u.Code))
            .Select(u => new UnitState(u.Code, u.Name, u.IsActive, u.AllowForOrderEntry, u.AllowForPricing))
            .ToListAsync(cancellationToken);
        var byCode = units.ToDictionary(u => u.Code, StringComparer.Ordinal);
        foreach (var (code, field) in toCheck)
        {
            if (Refusal(byCode.GetValueOrDefault(code), $"'{code}'", usage) is { } message)
            {
                throw new DomainValidationException(field, message);
            }
        }
    }

    public static async Task EnsureIdsAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> requested, UnitUsage usage,
        IReadOnlyCollection<Guid> alreadyUsed, string field, CancellationToken cancellationToken)
    {
        var ids = requested.Where(id => !alreadyUsed.Contains(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var units = await db.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .Select(u => new { u.Id, State = new UnitState(u.Code, u.Name, u.IsActive, u.AllowForOrderEntry, u.AllowForPricing) })
            .ToDictionaryAsync(u => u.Id, u => u.State, cancellationToken);
        foreach (var id in ids)
        {
            if (Refusal(units.GetValueOrDefault(id), "die je koos", usage) is { } message)
            {
                throw new DomainValidationException(field, message);
            }
        }
    }

    private sealed record UnitState(string Code, string Name, bool IsActive, bool AllowForOrderEntry, bool AllowForPricing);

    private static string? Refusal(UnitState? unit, string unknownLabel, UnitUsage usage) => unit switch
    {
        null => $"Onbekende eenheid {unknownLabel}. Kies een eenheid uit de stamgegevens.",
        { IsActive: false } => usage == UnitUsage.OrderEntry
            ? $"De eenheid '{unit.Name}' is inactief en kan niet meer gekozen worden voor nieuwe opdrachten."
            : $"De eenheid '{unit.Name}' is inactief en kan niet meer gekozen worden voor nieuwe tarieven.",
        { AllowForOrderEntry: false } when usage == UnitUsage.OrderEntry => $"De eenheid '{unit.Name}' is niet toegelaten voor orderinvoer.",
        { AllowForPricing: false } when usage == UnitUsage.Pricing => $"De eenheid '{unit.Name}' is niet toegelaten voor prijzen en tarieven.",
        _ => null,
    };
}
