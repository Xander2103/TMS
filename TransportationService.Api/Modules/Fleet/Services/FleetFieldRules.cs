using TransportationService.Api.Common;

namespace TransportationService.Api.Modules.Fleet.Services;

/// <summary>
/// Shared field rules for vehicles and trailers (and reusable for cargo): construction-year
/// bounds against the CURRENT calendar year (never a hardcoded maximum) and volume
/// resolution with an explicit manual-override flag.
/// </summary>
public static class FleetFieldRules
{
    /// <summary>Throws a field-level validation error when the year is in the future (or absurd).</summary>
    public static void ValidateConstructionYear(int? year, int currentYear, string field = "year")
    {
        if (year is not { } y)
        {
            return;
        }

        if (y > currentYear)
        {
            throw new DomainValidationException(field, $"Bouwjaar mag niet in de toekomst liggen (maximaal {currentYear}).");
        }

        if (y < 1900)
        {
            throw new DomainValidationException(field, "Bouwjaar is ongeldig (minimaal 1900).");
        }
    }

    /// <summary>
    /// Resolves the stored volume. A manual override keeps the supplied value verbatim;
    /// otherwise the volume is computed from complete positive dimensions (m³, 3 decimals),
    /// falling back to the supplied value when dimensions are incomplete/ambiguous —
    /// volumes are never invented.
    /// </summary>
    public static (decimal? Volume, bool IsManual) ResolveVolume(
        decimal? lengthMeters, decimal? widthMeters, decimal? heightMeters,
        decimal? requestedVolume, bool requestedManual, string field = "volumeM3")
    {
        if (requestedVolume is < 0)
        {
            throw new DomainValidationException(field, "Volume mag niet negatief zijn.");
        }

        if (requestedManual)
        {
            return (requestedVolume, true);
        }

        if (lengthMeters is { } l and > 0 && widthMeters is { } w and > 0 && heightMeters is { } h and > 0)
        {
            return (Math.Round(l * w * h, 3), false);
        }

        return (requestedVolume, false);
    }
}
