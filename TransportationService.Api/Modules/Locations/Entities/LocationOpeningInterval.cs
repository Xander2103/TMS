using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Locations.Entities;

/// <summary>
/// One opening window of a location on a fixed weekday. A day may carry multiple intervals
/// (e.g. 07:00–12:00 and 13:00–17:00); a day with no intervals is closed/unknown. Simple
/// child rows of <see cref="Location"/> WITHOUT soft delete: the service replaces a
/// location's intervals wholesale on every update — nothing outside the location ever
/// references an individual interval row.
/// </summary>
public class LocationOpeningInterval : ITenantOwned, IHasId
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }

    /// <summary>ISO weekday: 1 = maandag .. 7 = zondag.</summary>
    public int DayOfWeek { get; set; }

    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime { get; set; }

    public string? Note { get; set; }
}
