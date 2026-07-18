using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Dtos;

public record LocationListItemDto(
    Guid Id,
    string Code,
    string Name,
    LocationType Type,
    string? City,
    string? CountryCode,
    string? CustomerName,
    bool IsActive);

public record LocationOptionDto(Guid Id, string Code, string Name, LocationType Type);

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
    string? Notes);

public record CreateLocationRequest(
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
    Guid? CustomerId,
    string? Notes);

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
    string? Notes);

public enum LocationOperationOutcome
{
    Success,
    NotFound,
    DuplicateCode,
    InvalidCoordinates,
}

public record LocationOperationResult(LocationOperationOutcome Outcome, LocationDetailDto? Location)
{
    public static LocationOperationResult Success(LocationDetailDto location) => new(LocationOperationOutcome.Success, location);
    public static readonly LocationOperationResult NotFound = new(LocationOperationOutcome.NotFound, null);
    public static readonly LocationOperationResult DuplicateCode = new(LocationOperationOutcome.DuplicateCode, null);
    public static readonly LocationOperationResult InvalidCoordinates = new(LocationOperationOutcome.InvalidCoordinates, null);
}
