using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Dtos;

/// <summary>One group of the per-customer locations view; CustomerId null = the
/// "Ongekoppelde locaties" bucket (rendered last).</summary>
public record LocationGroupDto(
    Guid? CustomerId, string? CustomerName, IReadOnlyList<LocationListItemDto> Locations);

public record LocationListItemDto(
    Guid Id,
    string Code,
    string Name,
    LocationType Type,
    string? City,
    string? CountryCode,
    string? CustomerName,
    bool IsActive,
    bool IsDefaultLoadingLocation,
    bool IsDefaultUnloadingLocation,
    bool IsDefaultBillingLocation = false);

public record LocationOptionDto(
    Guid Id,
    string Code,
    string Name,
    LocationType Type,
    string? City = null,
    bool IsDefaultLoadingLocation = false,
    bool IsDefaultUnloadingLocation = false,
    bool IsDefaultBillingLocation = false,
    /// <summary>Street + house number ("Noorderlaan 10") so pickers can render a full address line.</summary>
    string? Address = null,
    string? PostalCode = null);

/// <summary>
/// One structured opening window. Times travel as "HH:mm" strings (JSON friendly, exactly what
/// the weekly editor shows); DayOfWeek is ISO (1 = maandag .. 7 = zondag).
/// </summary>
public record LocationOpeningIntervalDto(int DayOfWeek, string FromTime, string ToTime, string? Note = null);

public record LocationDetailDto(
    Guid Id,
    string Code,
    string Name,
    LocationType Type,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? OpeningHours,
    string? LoadingInstructions,
    string? UnloadingInstructions,
    string? AccessInstructions,
    string? AccessRestrictions,
    string? VehicleRestrictions,
    string? TrailerRestrictions,
    bool AlfapassRequired,
    bool AppointmentRequired,
    bool IsActive,
    Guid? CustomerId,
    string? CustomerName,
    string? Notes,
    bool IsDefaultLoadingLocation,
    bool IsDefaultUnloadingLocation,
    bool IsDefaultBillingLocation = false,
    // Master-data wave 2026-08-05 (appended optional params only — positional callers exist).
    string? ExternalReference = null,
    string? ContactMobile = null,
    Guid? CustomerContactId = null,
    string? Gate = null,
    // Null when the caller lacks locations.view_sensitive (never exposed unmasked without it).
    string? AccessCode = null,
    string? ReceptionPoint = null,
    string? Dock = null,
    string? RouteDescription = null,
    bool DeliveryByAppointmentOnly = false,
    decimal? HeightRestrictionMeters = null,
    decimal? WeightRestrictionTons = null,
    bool? AdrAllowed = null,
    bool CraneRequired = false,
    bool ForkliftAvailable = false,
    string? DriverInstructions = null,
    string? InternalMemo = null,
    int? DefaultLoadingMinutes = null,
    int? DefaultUnloadingMinutes = null,
    string? PreferredArrivalFrom = null,
    string? PreferredArrivalTo = null,
    string? EarliestArrival = null,
    string? LatestArrival = null,
    IReadOnlyList<LocationOpeningIntervalDto>? OpeningIntervals = null);

public record CreateLocationRequest(
    // Optional since the master-data wave: when blank the service generates "LOC-xxxxxxxx".
    string? Code,
    string Name,
    LocationType Type,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? OpeningHours,
    string? LoadingInstructions,
    string? UnloadingInstructions,
    string? AccessInstructions,
    string? AccessRestrictions,
    string? VehicleRestrictions,
    string? TrailerRestrictions,
    bool AlfapassRequired,
    bool AppointmentRequired,
    Guid? CustomerId,
    string? Notes,
    bool IsDefaultLoadingLocation = false,
    bool IsDefaultUnloadingLocation = false,
    bool IsDefaultBillingLocation = false,
    string? ExternalReference = null,
    string? ContactMobile = null,
    Guid? CustomerContactId = null,
    string? Gate = null,
    string? AccessCode = null,
    string? ReceptionPoint = null,
    string? Dock = null,
    string? RouteDescription = null,
    bool DeliveryByAppointmentOnly = false,
    decimal? HeightRestrictionMeters = null,
    decimal? WeightRestrictionTons = null,
    bool? AdrAllowed = null,
    bool CraneRequired = false,
    bool ForkliftAvailable = false,
    string? DriverInstructions = null,
    string? InternalMemo = null,
    int? DefaultLoadingMinutes = null,
    int? DefaultUnloadingMinutes = null,
    string? PreferredArrivalFrom = null,
    string? PreferredArrivalTo = null,
    string? EarliestArrival = null,
    string? LatestArrival = null,
    IReadOnlyList<LocationOpeningIntervalDto>? OpeningIntervals = null);

public record UpdateLocationRequest(
    string Code,
    string Name,
    LocationType Type,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    decimal? Latitude,
    decimal? Longitude,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string? OpeningHours,
    string? LoadingInstructions,
    string? UnloadingInstructions,
    string? AccessInstructions,
    string? AccessRestrictions,
    string? VehicleRestrictions,
    string? TrailerRestrictions,
    bool AlfapassRequired,
    bool AppointmentRequired,
    bool IsActive,
    Guid? CustomerId,
    string? Notes,
    bool IsDefaultLoadingLocation = false,
    bool IsDefaultUnloadingLocation = false,
    bool IsDefaultBillingLocation = false,
    string? ExternalReference = null,
    string? ContactMobile = null,
    Guid? CustomerContactId = null,
    string? Gate = null,
    // Ignored (existing value preserved) when the caller lacks locations.view_sensitive.
    string? AccessCode = null,
    string? ReceptionPoint = null,
    string? Dock = null,
    string? RouteDescription = null,
    bool DeliveryByAppointmentOnly = false,
    decimal? HeightRestrictionMeters = null,
    decimal? WeightRestrictionTons = null,
    bool? AdrAllowed = null,
    bool CraneRequired = false,
    bool ForkliftAvailable = false,
    string? DriverInstructions = null,
    string? InternalMemo = null,
    int? DefaultLoadingMinutes = null,
    int? DefaultUnloadingMinutes = null,
    string? PreferredArrivalFrom = null,
    string? PreferredArrivalTo = null,
    string? EarliestArrival = null,
    string? LatestArrival = null,
    IReadOnlyList<LocationOpeningIntervalDto>? OpeningIntervals = null);

public record SetLocationActiveRequest(bool IsActive);

public record SetLocationDefaultsRequest(
    bool IsDefaultLoadingLocation,
    bool IsDefaultUnloadingLocation,
    bool IsDefaultBillingLocation = false);

public enum LocationOperationOutcome
{
    Success,
    NotFound,
    DuplicateCode,
    InvalidCoordinates,
    InvalidReference,
}

public record LocationOperationResult(LocationOperationOutcome Outcome, LocationDetailDto? Location)
{
    public static LocationOperationResult Success(LocationDetailDto location) => new(LocationOperationOutcome.Success, location);
    public static readonly LocationOperationResult NotFound = new(LocationOperationOutcome.NotFound, null);
    public static readonly LocationOperationResult DuplicateCode = new(LocationOperationOutcome.DuplicateCode, null);
    public static readonly LocationOperationResult InvalidCoordinates = new(LocationOperationOutcome.InvalidCoordinates, null);
    public static readonly LocationOperationResult InvalidReference = new(LocationOperationOutcome.InvalidReference, null);
}
