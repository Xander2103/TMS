namespace TransportationService.Api.Common.Lookups;

public record LookupItemDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Compact shape used to populate select/dropdown controls.</summary>
public record LookupOptionDto(Guid Id, string Code, string Name);

public record CreateLookupRequest(string Code, string Name, string? Description, bool IsActive, int SortOrder);

public record UpdateLookupRequest(string Code, string Name, string? Description, bool IsActive, int SortOrder);

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
