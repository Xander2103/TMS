using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record TankCardDto(
    Guid Id,
    string CardNumber,
    string Provider,
    Guid? VehicleId,
    string? VehicleInternalNumber,
    string? VehicleLicensePlate,
    Guid? DriverId,
    string? DriverName,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    TankCardStatus Status,
    bool IsBlocked,
    string? BlockedReason,
    string? Notes);

public record CreateTankCardRequest(
    string CardNumber,
    string Provider,
    Guid? VehicleId,
    Guid? DriverId,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? Notes);

public record UpdateTankCardRequest(
    string CardNumber,
    string Provider,
    Guid? VehicleId,
    Guid? DriverId,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? Notes);

public record SetTankCardBlockedRequest(bool IsBlocked, string? Reason);

public enum TankCardOperationOutcome
{
    Success,
    NotFound,
    DuplicateCardNumber,
    InvalidReference,
    ValidationFailed,
}

public record TankCardOperationResult(TankCardOperationOutcome Outcome, TankCardDto? Card, string? Error = null)
{
    public static TankCardOperationResult Success(TankCardDto card) => new(TankCardOperationOutcome.Success, card);
    public static readonly TankCardOperationResult NotFound = new(TankCardOperationOutcome.NotFound, null);
    public static readonly TankCardOperationResult DuplicateCardNumber = new(TankCardOperationOutcome.DuplicateCardNumber, null);
    public static readonly TankCardOperationResult InvalidReference = new(TankCardOperationOutcome.InvalidReference, null);
    public static TankCardOperationResult Invalid(string error) => new(TankCardOperationOutcome.ValidationFailed, null, error);
}
