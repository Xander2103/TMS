using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.Packages.Dtos;

public record PackageDto(
    Guid Id,
    Guid TransportOrderId,
    string PackageNumber,
    string BarcodeValue,
    PackageBarcodeType BarcodeType,
    string? ExternalBarcode,
    string? ExternalPackageReference,
    string? CustomerReference,
    string Description,
    decimal Quantity,
    PackageUnitType UnitType,
    string? UnitTypeLabel,
    decimal? WeightKg,
    decimal? VolumeM3,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    Guid? ParentPackageId,
    Guid? LoadingStopId,
    Guid? DeliveryStopId,
    bool IsMandatory,
    bool IsFragile,
    bool RequiresTemperatureControl,
    bool RequiresSignature,
    PackageLifecycleStatus Status,
    PackageExceptionState ExceptionState,
    string? Notes,
    DateTime CreatedAt);

public record PackageBarcodeDto(
    Guid Id,
    string Value,
    PackageBarcodeType Type,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? RetiredAt,
    string? RetireReason);

public record CreatePackageRequest(
    string Description,
    decimal Quantity = 1m,
    PackageUnitType UnitType = PackageUnitType.Colli,
    string? UnitTypeLabel = null,
    string? ExternalBarcode = null,
    string? ExternalPackageReference = null,
    string? CustomerReference = null,
    decimal? WeightKg = null,
    decimal? VolumeM3 = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    Guid? LoadingStopId = null,
    Guid? DeliveryStopId = null,
    Guid? ParentPackageId = null,
    bool IsMandatory = true,
    bool IsFragile = false,
    bool RequiresTemperatureControl = false,
    bool RequiresSignature = false,
    string? Notes = null);

public record UpdatePackageRequest(
    string Description,
    decimal Quantity,
    PackageUnitType UnitType,
    string? UnitTypeLabel,
    string? ExternalPackageReference,
    string? CustomerReference,
    decimal? WeightKg,
    decimal? VolumeM3,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    Guid? LoadingStopId,
    Guid? DeliveryStopId,
    bool IsMandatory,
    bool IsFragile,
    bool RequiresTemperatureControl,
    bool RequiresSignature,
    string? Notes);

public record CancelPackageRequest(string Reason);

public record BulkCreatePackagesRequest(
    int Count,
    string Description,
    PackageUnitType UnitType = PackageUnitType.Colli,
    decimal? WeightKg = null,
    decimal? VolumeM3 = null,
    Guid? LoadingStopId = null,
    Guid? DeliveryStopId = null,
    /// <summary>When set, packages get sequential customer references "{prefix}-1..N".</summary>
    string? ReferencePrefix = null,
    /// <summary>Create a pallet parent and group all packages on it.</summary>
    bool GroupOnPallet = false,
    string? PalletDescription = null,
    bool IsMandatory = true,
    bool IsFragile = false,
    bool RequiresTemperatureControl = false,
    bool RequiresSignature = false);

public record BulkCreateResultDto(IReadOnlyList<PackageDto> Packages, PackageDto? Pallet);

public record RelabelPackageRequest(string Reason);

public enum PackageOutcome
{
    Success,
    NotFound,
    InvalidState,
    ValidationFailed,
    DuplicateBarcode,
}

public record PackageOperationResult(PackageOutcome Outcome, PackageDto? Package, string? Error = null)
{
    public static PackageOperationResult Success(PackageDto package) => new(PackageOutcome.Success, package);
    public static readonly PackageOperationResult NotFound = new(PackageOutcome.NotFound, null);
    public static PackageOperationResult InvalidState(string error) => new(PackageOutcome.InvalidState, null, error);
    public static PackageOperationResult Invalid(string error) => new(PackageOutcome.ValidationFailed, null, error);
    public static PackageOperationResult Duplicate(string error) => new(PackageOutcome.DuplicateBarcode, null, error);
}
