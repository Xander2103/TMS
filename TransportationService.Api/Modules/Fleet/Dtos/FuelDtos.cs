namespace TransportationService.Api.Modules.Fleet.Dtos;

/// <summary>Machine-readable anomaly codes; the frontend maps them to Dutch labels.</summary>
public enum FuelWarningCode
{
    OdometerLowerThanPrevious,
    ConsumptionAboveAverage,
    ConsumptionBelowAverage,
}

public record FuelTransactionDto(
    Guid Id,
    Guid VehicleId,
    Guid? DriverId,
    string? DriverName,
    Guid? TankCardId,
    string? TankCardNumber,
    DateOnly TransactionDate,
    decimal Litres,
    decimal TotalAmount,
    decimal? PricePerLitre,
    int? OdometerKm,
    string? Station,
    bool FullTank,
    decimal? ConsumptionLPer100Km,
    IReadOnlyList<FuelWarningCode> Warnings,
    string? Notes);

/// <summary>Vehicle fuel history with aggregates; consumption derives from the odometer trail.</summary>
public record FuelOverviewDto(
    IReadOnlyList<FuelTransactionDto> Items,
    decimal? AverageConsumptionLPer100Km,
    decimal TotalLitres,
    decimal TotalAmount);

/// <summary>Recent transaction with at least one anomaly, joined with its vehicle for the fleet dashboard.</summary>
public record FuelWarningDto(
    Guid TransactionId,
    Guid VehicleId,
    string VehicleInternalNumber,
    string VehicleLicensePlate,
    DateOnly TransactionDate,
    decimal Litres,
    decimal? ConsumptionLPer100Km,
    IReadOnlyList<FuelWarningCode> Warnings);

public record CreateFuelTransactionRequest(
    Guid? DriverId,
    Guid? TankCardId,
    DateOnly TransactionDate,
    decimal Litres,
    decimal TotalAmount,
    int? OdometerKm,
    string? Station,
    bool FullTank,
    string? Notes);

public record UpdateFuelTransactionRequest(
    Guid? DriverId,
    Guid? TankCardId,
    DateOnly TransactionDate,
    decimal Litres,
    decimal TotalAmount,
    int? OdometerKm,
    string? Station,
    bool FullTank,
    string? Notes);

public enum FuelOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    InvalidReference,
    ValidationFailed,
}

public record FuelOperationResult(FuelOperationOutcome Outcome, FuelTransactionDto? Transaction, string? Error = null)
{
    public static FuelOperationResult Success(FuelTransactionDto transaction) => new(FuelOperationOutcome.Success, transaction);
    public static readonly FuelOperationResult NotFound = new(FuelOperationOutcome.NotFound, null);
    public static readonly FuelOperationResult OwnerNotFound = new(FuelOperationOutcome.OwnerNotFound, null);
    public static readonly FuelOperationResult InvalidReference = new(FuelOperationOutcome.InvalidReference, null);
    public static FuelOperationResult Invalid(string error) => new(FuelOperationOutcome.ValidationFailed, null, error);
}
