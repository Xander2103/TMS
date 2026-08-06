namespace TransportationService.Api.Common.Lookups;

/// <summary>
/// <paramref name="RequiresEndDate"/> is null for every lookup type except
/// <see cref="TransportationService.Api.Modules.Reference.Entities.ContractType"/>, where it
/// carries that entity's own flag (HR maturity wave §5). Kept on the shared DTO rather than a
/// bespoke ContractType-only DTO/controller so the one existing generic lookup CRUD surface
/// stays the single place every lookup type (incl. contract types) is managed.
/// </summary>
public record LookupItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool? RequiresEndDate = null);

/// <summary>
/// Compact shape used to populate select/dropdown controls. <paramref name="RequiresEndDate"/>
/// is populated only for contract-type options; the employee form reads it to decide whether
/// EmploymentEndDate is mandatory for the chosen contract type.
/// </summary>
public record LookupOptionDto(Guid Id, string Code, string Name, bool? RequiresEndDate = null);

public record CreateLookupRequest(string Code, string Name, string? Description, bool IsActive, int SortOrder, bool? RequiresEndDate = null);

public record UpdateLookupRequest(string Code, string Name, string? Description, bool IsActive, int SortOrder, bool? RequiresEndDate = null);

/// <summary>Outcome of a lookup mutation, kept transport-agnostic so controllers map it to HTTP.</summary>
public enum LookupOperationStatus
{
    Success,
    NotFound,
    DuplicateCode,
    ValidationFailed,
}

public record LookupOperationResult(LookupOperationStatus Status, LookupItemDto? Item = null, string? Error = null)
{
    public static LookupOperationResult Ok(LookupItemDto item) => new(LookupOperationStatus.Success, item);
    public static LookupOperationResult NotFound() => new(LookupOperationStatus.NotFound);
    public static LookupOperationResult Duplicate(string code) =>
        new(LookupOperationStatus.DuplicateCode, Error: $"Er bestaat al een item met code '{code}'.");
    public static LookupOperationResult Invalid(string error) => new(LookupOperationStatus.ValidationFailed, Error: error);
}
