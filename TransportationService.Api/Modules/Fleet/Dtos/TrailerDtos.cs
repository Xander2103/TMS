using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record TrailerListItemDto(
    Guid Id,
    string InternalNumber,
    string LicensePlate,
    string? Brand,
    string? Model,
    string? CategoryName,
    TrailerOperationalStatus OperationalStatus,
    bool IsActive);

public record TrailerOptionDto(Guid Id, string InternalNumber, string LicensePlate, string? Brand, string? Model);

public record TrailerDetailDto(
    Guid Id,
    string InternalNumber,
    string LicensePlate,
    string? Vin,
    Guid? CategoryId,
    string? CategoryName,
    string? Brand,
    string? Model,
    int? Year,
    DateOnly? FirstRegistrationDate,
    decimal? CapacityKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    bool VolumeIsManual,
    bool HasRefrigeration,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    TrailerOperationalStatus OperationalStatus,
    string? StatusReason,
    bool IsActive,
    string? Notes,
    int AxleCount = 0,
    decimal LoadingMeters = 0);

public record CreateTrailerRequest(
    string LicensePlate,
    string? Vin,
    Guid? CategoryId,
    string? Brand,
    string? Model,
    int? Year,
    DateOnly? FirstRegistrationDate,
    decimal? CapacityKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    bool HasRefrigeration,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    string? Notes,
    bool VolumeIsManual = false,
    int AxleCount = 0,
    decimal LoadingMeters = 0);

public record UpdateTrailerRequest(
    string LicensePlate,
    string? Vin,
    Guid? CategoryId,
    string? Brand,
    string? Model,
    int? Year,
    DateOnly? FirstRegistrationDate,
    decimal? CapacityKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    bool HasRefrigeration,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    TrailerOperationalStatus OperationalStatus,
    bool IsActive,
    string? Notes,
    string? StatusReason = null,
    bool VolumeIsManual = false,
    int AxleCount = 0,
    decimal LoadingMeters = 0);

public record SetTrailerActiveRequest(bool IsActive);

public enum TrailerOperationOutcome
{
    Success,
    NotFound,
    DuplicateLicensePlate,
    InvalidReference,
}

public record TrailerOperationResult(TrailerOperationOutcome Outcome, TrailerDetailDto? Trailer)
{
    public static TrailerOperationResult Success(TrailerDetailDto trailer) => new(TrailerOperationOutcome.Success, trailer);
    public static readonly TrailerOperationResult NotFound = new(TrailerOperationOutcome.NotFound, null);
    public static readonly TrailerOperationResult DuplicateLicensePlate = new(TrailerOperationOutcome.DuplicateLicensePlate, null);
    public static readonly TrailerOperationResult InvalidReference = new(TrailerOperationOutcome.InvalidReference, null);
}
