using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>A refused crane-job rule; <see cref="Field"/> (camelCase request path) is set when the message belongs to one input.</summary>
public sealed record CraneJobRuleError(string? Field, string Message);

/// <summary>
/// Master sprint 2026-09-21 (D2) — the shape rules that separate on-site lifting work from every
/// other order. Pure: the caller resolves whether the order's activity type carries
/// <c>SupportsOnSiteWork</c> (a capability FLAG — never the type code) and passes the answer in.
///
/// OnSiteLifting: site stop(s) with a place, a work description, no loading/unloading route and
/// no goods requirement. Every other kind: no site stop — and otherwise exactly the rules the
/// order always had, which is why this class adds nothing for them.
/// </summary>
public static class CraneJobRules
{
    public const int WorkDescriptionMaxLength = 2000;

    /// <summary>True → the minimal-goods rule and the loading/unloading route rules do not apply.</summary>
    public static bool IsOnSiteWork(CraneJobKind kind) => kind == CraneJobKind.OnSiteLifting;

    /// <summary>A site stop is locatable with a master location, a city or an address.</summary>
    public static bool HasSitePlace(TransportOrderStopInput stop) =>
        stop.LocationId is not null || !string.IsNullOrWhiteSpace(stop.City) || !string.IsNullOrWhiteSpace(stop.Address);

    /// <summary>Save-time rules. Returns null when satisfied.</summary>
    public static CraneJobRuleError? Validate(
        CraneJobKind kind, string? workDescription, IReadOnlyList<TransportOrderStopInput> stops,
        bool activitySupportsOnSiteWork,
        decimal? liftLoadWeightKg, string? liftLoadDimensions, decimal? liftRadiusMeters, decimal? liftHeightMeters,
        string? liftConditions, string? liftEquipment)
    {
        if (!IsOnSiteWork(kind))
        {
            // A site stop has no loading/unloading role, so an ordinary transport can neither
            // plan, scan nor deliver against it.
            return stops.Any(s => s.StopType == StopType.Site)
                ? new CraneJobRuleError(null, "Een werfstop is alleen mogelijk op een opdracht voor kraanwerk ter plaatse.")
                : LiftDataError(workDescription, liftLoadWeightKg, liftLoadDimensions, liftRadiusMeters, liftHeightMeters,
                    liftConditions, liftEquipment);
        }

        if (!activitySupportsOnSiteWork)
        {
            return new CraneJobRuleError("craneJobKind", "Dit activiteitstype ondersteunt geen kraanwerk ter plaatse.");
        }

        if (stops.Any(s => s.StopType != StopType.Site))
        {
            return new CraneJobRuleError(null,
                "Een opdracht voor kraanwerk ter plaatse heeft geen laad- of losstops; gebruik een werfstop.");
        }

        if (!stops.Any(HasSitePlace))
        {
            return new CraneJobRuleError("stops",
                "Een opdracht voor kraanwerk ter plaatse heeft minstens één werfstop met een plaats of adres nodig.");
        }

        if (string.IsNullOrWhiteSpace(workDescription))
        {
            return new CraneJobRuleError("workDescription", "Een werkomschrijving is verplicht voor kraanwerk ter plaatse.");
        }

        return LiftDataError(workDescription, liftLoadWeightKg, liftLoadDimensions, liftRadiusMeters, liftHeightMeters,
            liftConditions, liftEquipment);
    }

    /// <summary>
    /// Rules an order must satisfy to be (or stay) confirmed, per kind: on-site work needs a
    /// site stop and its work description; everything else keeps the loading + unloading rule.
    /// </summary>
    public static string? ConfirmationError(CraneJobKind kind, string? workDescription, IReadOnlyList<TransportOrderStopInput> stops)
    {
        if (IsOnSiteWork(kind))
        {
            return !stops.Any(s => s.StopType == StopType.Site) || string.IsNullOrWhiteSpace(workDescription)
                ? "Een bevestigde opdracht voor kraanwerk ter plaatse heeft minstens één werfstop en een werkomschrijving nodig."
                : null;
        }

        return !stops.Any(s => s.StopType == StopType.Loading) || !stops.Any(s => s.StopType == StopType.Unloading)
            ? "Een bevestigde opdracht heeft minstens één laad- en één losstop nodig."
            : null;
    }

    private static CraneJobRuleError? LiftDataError(
        string? workDescription,
        decimal? liftLoadWeightKg, string? liftLoadDimensions, decimal? liftRadiusMeters, decimal? liftHeightMeters,
        string? liftConditions, string? liftEquipment)
    {
        if (workDescription is { } description && description.Trim().Length > WorkDescriptionMaxLength)
        {
            return new CraneJobRuleError("workDescription",
                $"De werkomschrijving mag maximaal {WorkDescriptionMaxLength} tekens lang zijn.");
        }

        if (liftLoadWeightKg is < 0)
        {
            return new CraneJobRuleError("liftLoadWeightKg", "Het gewicht van de last mag niet negatief zijn.");
        }

        if (liftRadiusMeters is < 0)
        {
            return new CraneJobRuleError("liftRadiusMeters", "De vlucht (radius) mag niet negatief zijn.");
        }

        if (liftHeightMeters is < 0)
        {
            return new CraneJobRuleError("liftHeightMeters", "De hijshoogte mag niet negatief zijn.");
        }

        if (liftLoadDimensions is { } dimensions && dimensions.Trim().Length > 200)
        {
            return new CraneJobRuleError("liftLoadDimensions", "De afmetingen van de last mogen maximaal 200 tekens lang zijn.");
        }

        if (liftConditions is { } conditions && conditions.Trim().Length > 1000)
        {
            return new CraneJobRuleError("liftConditions", "De omstandigheden mogen maximaal 1000 tekens lang zijn.");
        }

        if (liftEquipment is { } equipment && equipment.Trim().Length > 500)
        {
            return new CraneJobRuleError("liftEquipment", "Het hijsmateriaal mag maximaal 500 tekens lang zijn.");
        }

        return null;
    }
}
