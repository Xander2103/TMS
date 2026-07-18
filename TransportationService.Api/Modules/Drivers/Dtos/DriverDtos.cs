using TransportationService.Api.Modules.Drivers.Entities;

namespace TransportationService.Api.Modules.Drivers.Dtos;

public record DriverListItemDto(
    Guid Id,
    string DriverNumber,
    string FullName,
    string EmployeeNumber,
    string? CategoryName,
    DriverAvailabilityStatus AvailabilityStatus,
    bool IsActive,
    bool IsBlocked);

public record DriverReadinessDto(
    string Status,               // "Ready" | "Warning" | "NotReady" | "Blocked"
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings);

public record DriverQualificationDto(
    string TypeCode,
    string TypeName,
    string Status,
    DateOnly? ExpiryDate);

public record DriverDetailDto(
    Guid Id,
    string DriverNumber,
    Guid EmployeeId,
    string FullName,
    string EmployeeNumber,
    Guid? CategoryId,
    string? CategoryName,
    DriverAvailabilityStatus AvailabilityStatus,
    bool IsActive,
    bool IsBlocked,
    string? BlockReason,
    bool FixedVehiclePreference,
    Guid? DefaultVehicleId,
    Guid? PreferredVehicleId,
    Guid? DefaultTrailerId,
    string? Notes,
    DriverReadinessDto Readiness,
    IReadOnlyList<DriverQualificationDto> Qualifications);

public record CreateDriverRequest(
    Guid EmployeeId,
    Guid? DriverCategoryId,
    DriverAvailabilityStatus AvailabilityStatus,
    bool FixedVehiclePreference,
    Guid? DefaultVehicleId,
    Guid? PreferredVehicleId,
    Guid? DefaultTrailerId,
    string? Notes);

public record UpdateDriverRequest(
    Guid? DriverCategoryId,
    DriverAvailabilityStatus AvailabilityStatus,
    bool IsActive,
    bool FixedVehiclePreference,
    Guid? DefaultVehicleId,
    Guid? PreferredVehicleId,
    Guid? DefaultTrailerId,
    string? Notes);

public record SetDriverBlockedRequest(bool IsBlocked, string? Reason);

public enum DriverOperationOutcome
{
    Success,
    NotFound,
    EmployeeNotFound,
    EmployeeAlreadyDriver,
    InvalidReference,
}

public record DriverOperationResult(DriverOperationOutcome Outcome, DriverDetailDto? Driver)
{
    public static DriverOperationResult Success(DriverDetailDto driver) => new(DriverOperationOutcome.Success, driver);
    public static readonly DriverOperationResult NotFound = new(DriverOperationOutcome.NotFound, null);
    public static readonly DriverOperationResult EmployeeNotFound = new(DriverOperationOutcome.EmployeeNotFound, null);
    public static readonly DriverOperationResult EmployeeAlreadyDriver = new(DriverOperationOutcome.EmployeeAlreadyDriver, null);
    public static readonly DriverOperationResult InvalidReference = new(DriverOperationOutcome.InvalidReference, null);
}
