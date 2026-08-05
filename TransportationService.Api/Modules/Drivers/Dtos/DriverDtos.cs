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

/// <summary>Compact reference to an assigned vehicle/trailer for display and linking.</summary>
public record AssignedAssetRef(Guid Id, string Label);

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
    /// <summary>Resolved from Vehicle.FixedDriverId — the single source of truth.</summary>
    AssignedAssetRef? FixedVehicle,
    /// <summary>Resolved from Vehicle.CurrentDriverId — the single source of truth.</summary>
    AssignedAssetRef? CurrentVehicle,
    Guid? FixedTrailerId,
    string? FixedTrailerLabel,
    string? Notes,
    DriverReadinessDto Readiness,
    IReadOnlyList<DriverQualificationDto> Qualifications,
    IReadOnlyList<Guid>? CategoryIds = null,
    IReadOnlyList<string>? CategoryNames = null);

public record CreateDriverRequest(
    Guid EmployeeId,
    Guid? DriverCategoryId,
    DriverAvailabilityStatus AvailabilityStatus = DriverAvailabilityStatus.Available,
    Guid? FixedTrailerId = null,
    string? Notes = null,
    /// <summary>Multi-category selection; when provided, the first entry is the primary.</summary>
    IReadOnlyList<Guid>? DriverCategoryIds = null);

public record UpdateDriverRequest(
    Guid? DriverCategoryId,
    DriverAvailabilityStatus AvailabilityStatus,
    bool IsActive,
    Guid? FixedTrailerId = null,
    string? Notes = null,
    /// <summary>Ordered multi-category selection; the first entry is the primary and mirrors
    /// DriverCategoryId. Empty list clears all categories (primary becomes null); null leaves
    /// the categories unchanged unless the legacy single DriverCategoryId is set.</summary>
    IReadOnlyList<Guid>? DriverCategoryIds = null);

/// <summary>Body for the driver-side assignment endpoints; null VehicleId clears the slot.</summary>
public record AssignDriverVehicleRequest(Guid? VehicleId, bool ReplaceExisting = false);

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
