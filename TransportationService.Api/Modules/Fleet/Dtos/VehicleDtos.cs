using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record VehicleListItemDto(
    Guid Id,
    string InternalNumber,
    string LicensePlate,
    string? Brand,
    string? Model,
    string? CategoryName,
    VehicleOperationalStatus OperationalStatus,
    bool IsActive);

public record VehicleOptionDto(Guid Id, string InternalNumber, string LicensePlate, string? Brand, string? Model);

public record VehicleDetailDto(
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
    FuelType FuelType,
    EmissionClass? EmissionClass,
    decimal? GrossVehicleWeightKg,
    decimal? PayloadKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    int OdometerKm,
    bool HasCrane,
    bool HasRefrigeration,
    bool HasTailLift,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    VehicleOperationalStatus OperationalStatus,
    bool IsActive,
    Guid? FixedDriverId,
    string? FixedDriverName,
    Guid? CurrentDriverId,
    string? CurrentDriverName,
    string? Notes);

public record CreateVehicleRequest(
    string LicensePlate,
    string? Vin,
    Guid? CategoryId,
    string? Brand,
    string? Model,
    int? Year,
    DateOnly? FirstRegistrationDate,
    FuelType FuelType,
    EmissionClass? EmissionClass,
    decimal? GrossVehicleWeightKg,
    decimal? PayloadKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    int OdometerKm,
    bool HasCrane,
    bool HasRefrigeration,
    bool HasTailLift,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    Guid? FixedDriverId,
    Guid? CurrentDriverId,
    string? Notes);

public record UpdateVehicleRequest(
    string LicensePlate,
    string? Vin,
    Guid? CategoryId,
    string? Brand,
    string? Model,
    int? Year,
    DateOnly? FirstRegistrationDate,
    FuelType FuelType,
    EmissionClass? EmissionClass,
    decimal? GrossVehicleWeightKg,
    decimal? PayloadKg,
    decimal? LengthMeters,
    decimal? WidthMeters,
    decimal? HeightMeters,
    decimal? VolumeM3,
    int OdometerKm,
    bool HasCrane,
    bool HasRefrigeration,
    bool HasTailLift,
    bool AdrSuitable,
    VehicleOwnershipType OwnershipType,
    VehicleOperationalStatus OperationalStatus,
    bool IsActive,
    Guid? FixedDriverId,
    Guid? CurrentDriverId,
    string? Notes);

public enum VehicleOperationOutcome
{
    Success,
    NotFound,
    DuplicateLicensePlate,
    InvalidReference,
}

public record VehicleOperationResult(VehicleOperationOutcome Outcome, VehicleDetailDto? Vehicle)
{
    public static VehicleOperationResult Success(VehicleDetailDto vehicle) => new(VehicleOperationOutcome.Success, vehicle);
    public static readonly VehicleOperationResult NotFound = new(VehicleOperationOutcome.NotFound, null);
    public static readonly VehicleOperationResult DuplicateLicensePlate = new(VehicleOperationOutcome.DuplicateLicensePlate, null);
    public static readonly VehicleOperationResult InvalidReference = new(VehicleOperationOutcome.InvalidReference, null);
}
