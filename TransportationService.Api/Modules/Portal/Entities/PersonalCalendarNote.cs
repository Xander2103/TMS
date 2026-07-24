using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Portal.Entities;

/// <summary>
/// A personal calendar note of one employee (Tandarts, Privé afspraak, ...). Not a shift,
/// not an absence: purely self-service, visible only in the employee's own planning.
/// </summary>
public class PersonalCalendarNote : AuditableTenantEntity
{
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool AllDay { get; set; } = true;

    /// <summary>Hex colour from <see cref="CalendarNotePalette"/>; validated server-side.</summary>
    public string Colour { get; set; } = CalendarNotePalette.Default;
}

/// <summary>Safe predefined colour palette for personal notes (no arbitrary CSS input).</summary>
public static class CalendarNotePalette
{
    public const string Default = "#2563eb";

    public static readonly IReadOnlyList<string> Colours =
    [
        "#2563eb", // blauw
        "#16a34a", // groen
        "#ea580c", // oranje
        "#9333ea", // paars
        "#0891b2", // cyaan
        "#db2777", // roze
        "#ca8a04", // oker
        "#64748b", // grijs
    ];

    public static bool IsValid(string? colour) =>
        colour is not null && Colours.Contains(colour, StringComparer.OrdinalIgnoreCase);
}
