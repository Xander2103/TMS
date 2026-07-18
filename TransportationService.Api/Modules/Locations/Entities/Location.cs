using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Locations.Entities;

/// <summary>
/// The kind of physical location. Drives default behaviour in planning/orders (e.g. loading vs
/// unloading stops) and lets planners filter the master list.
/// </summary>
public enum LocationType
{
    CompanySite,
    Depot,
    Warehouse,
    CustomerLocation,
    Terminal,
    LoadingLocation,
    UnloadingLocation,
    ParkingLocation,
    Office,
}

/// <summary>
/// A reusable transport location (site, depot, terminal, customer address, …). Referenced by
/// future orders, stops and planning. Address fields are kept inline for consistency with the
/// existing Customer master record; a shared address value object is intentionally not introduced
/// here to avoid destabilising the verified Customers module.
/// </summary>
public class Location : AuditableTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LocationType Type { get; set; } = LocationType.CustomerLocation;

    // Address
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    // Geocoordinates (optional; WGS84)
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // Contact
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    // Operational information
    public string? OpeningHours { get; set; }
    public string? LoadingInstructions { get; set; }
    public string? UnloadingInstructions { get; set; }
    public string? AccessInstructions { get; set; }
    public string? AccessRestrictions { get; set; }
    public string? VehicleRestrictions { get; set; }
    public string? TrailerRestrictions { get; set; }

    public bool AlfapassRequired { get; set; }
    public bool AppointmentRequired { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional link to the customer this location belongs to (customer sites).</summary>
    public Guid? CustomerId { get; set; }

    public string? Notes { get; set; }
}
