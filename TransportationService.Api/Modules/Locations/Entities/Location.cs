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
    RegisteredOffice,
    AdministrativeAddress,
    BillingAddress,
    ReturnsAddress,
    // Master-data wave 2026-08-05 (additive; the column stores the enum as string, so
    // appending values is safe for existing rows).
    ConstructionSite,
    TemporaryLocation,
    Other,
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
    public string? ContactMobile { get; set; }

    /// <summary>
    /// Optional link to a <c>CustomerContact</c> of the SAME customer as <see cref="CustomerId"/>
    /// (validated by the service; FK is SetNull so removing the contact never breaks the location).
    /// </summary>
    public Guid? CustomerContactId { get; set; }

    /// <summary>The customer's / partner's own reference for this location.</summary>
    public string? ExternalReference { get; set; }

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

    // Site access & handling (master-data wave 2026-08-05)
    public string? Gate { get; set; }

    /// <summary>SENSITIVE: only exposed to callers holding locations.view_sensitive; masked "•••" in audit.</summary>
    public string? AccessCode { get; set; }

    public string? ReceptionPoint { get; set; }
    public string? Dock { get; set; }
    public string? RouteDescription { get; set; }

    public bool DeliveryByAppointmentOnly { get; set; }

    public decimal? HeightRestrictionMeters { get; set; }
    public decimal? WeightRestrictionTons { get; set; }

    /// <summary>Null = unknown (never guessed to false).</summary>
    public bool? AdrAllowed { get; set; }

    public bool CraneRequired { get; set; }
    public bool ForkliftAvailable { get; set; }

    public string? DriverInstructions { get; set; }

    /// <summary>Internal-only memo: shown in the back office, never on driver/customer surfaces or list DTOs.</summary>
    public string? InternalMemo { get; set; }

    /// <summary>Default handling times in minutes (0..1440).</summary>
    public int? DefaultLoadingMinutes { get; set; }
    public int? DefaultUnloadingMinutes { get; set; }

    // Arrival preferences/bounds (informational; planning warnings, never hard blocks).
    public TimeOnly? PreferredArrivalFrom { get; set; }
    public TimeOnly? PreferredArrivalTo { get; set; }
    public TimeOnly? EarliestArrival { get; set; }
    public TimeOnly? LatestArrival { get; set; }

    /// <summary>
    /// Structured weekly opening hours. A day without intervals is closed/unknown; the legacy
    /// free-text <see cref="OpeningHours"/> stays as display fallback. Rows are replaced
    /// wholesale on update (no external references exist to individual intervals).
    /// </summary>
    public List<LocationOpeningInterval> OpeningIntervals { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>Optional link to the customer this location belongs to (customer sites).</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Default loading/unloading site for the linked customer. At most ONE default per kind
    /// per customer (enforced by the service + filtered unique indexes); only meaningful when
    /// <see cref="CustomerId"/> is set.
    /// </summary>
    public bool IsDefaultLoadingLocation { get; set; }

    /// <inheritdoc cref="IsDefaultLoadingLocation"/>
    public bool IsDefaultUnloadingLocation { get; set; }

    /// <inheritdoc cref="IsDefaultLoadingLocation"/>
    public bool IsDefaultBillingLocation { get; set; }

    public string? Notes { get; set; }
}
