using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Dtos;

/// <summary>
/// One customer's relationship to a physical address: the address facts plus everything that
/// is specific to THIS customer using it (sprint 2, central address master).
/// </summary>
public record CustomerAddressDto(
    Guid LinkId,
    Guid LocationId,
    Guid CustomerId,
    string Code,
    /// <summary>The address's own name.</summary>
    string Name,
    /// <summary>Customer-specific name, when set; falls back to <paramref name="Name"/> in the UI.</summary>
    string? Alias,
    string? CustomerReference,
    LocationType Type,
    CustomerLocationRole Role,
    bool IsDefaultLoading,
    bool IsDefaultUnloading,
    bool IsDefaultBilling,
    string? Instructions,
    bool IsActive,
    /// <summary>False when the underlying physical address itself is deactivated.</summary>
    bool AddressIsActive,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    /// <summary>How many customers use this same physical address (including this one).</summary>
    int LinkedCustomerCount);

public record LinkCustomerAddressRequest(
    Guid LocationId,
    string? Alias,
    string? CustomerReference,
    CustomerLocationRole Role,
    bool IsDefaultLoading,
    bool IsDefaultUnloading,
    bool IsDefaultBilling,
    string? Instructions);

public record UpdateCustomerAddressLinkRequest(
    string? Alias,
    string? CustomerReference,
    CustomerLocationRole Role,
    bool IsDefaultLoading,
    bool IsDefaultUnloading,
    bool IsDefaultBilling,
    string? Instructions,
    bool IsActive);

/// <summary>How closely a candidate matches the address being entered.</summary>
public enum AddressDuplicateMatch
{
    /// <summary>Same country, postcode, city, street AND house number — the same front door.</summary>
    Exact,

    /// <summary>Same street, different house number — a normal, allowed situation worth showing.</summary>
    SameStreet,
}

/// <summary>One existing address that may be what the user is about to re-create.</summary>
public record AddressDuplicateCandidateDto(
    Guid LocationId,
    string Code,
    string Name,
    AddressDuplicateMatch Match,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    bool IsActive,
    /// <summary>Names of the customers already using this address — the reason to reuse it.</summary>
    IReadOnlyList<string> LinkedCustomers);

public record AddressDuplicateCheckRequest(
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    /// <summary>Ignore this address when editing an existing one.</summary>
    Guid? ExcludeLocationId);

/// <summary>
/// Result of a duplicate check. <see cref="HasExactMatch"/> drives the "explicit override
/// required" rule: a same-front-door address may only be created a second time deliberately.
/// </summary>
public record AddressDuplicateCheckResultDto(
    bool HasExactMatch,
    IReadOnlyList<AddressDuplicateCandidateDto> Candidates);

/// <summary>
/// Why an address is offered, in the order a planner wants to see them (sprint 2E). The
/// numeric order IS the ranking, so do not reorder these members.
/// </summary>
public enum AddressPickerGroup
{
    /// <summary>Linked to the selected customer.</summary>
    CustomerAddress = 0,

    /// <summary>Recently used by this tenant.</summary>
    Recent = 1,

    /// <summary>Everything else in the central address master.</summary>
    All = 2,
}

/// <summary>One address offered in an order/dossier stop selector.</summary>
public record AddressPickerOptionDto(
    Guid LocationId,
    string Code,
    string Name,
    LocationType Type,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    AddressPickerGroup Group);

public enum CustomerAddressOutcome
{
    Success,
    NotFound,
    /// <summary>Customer or address does not exist in this tenant.</summary>
    InvalidReference,
    /// <summary>This customer is already linked to this address.</summary>
    AlreadyLinked,
}

public record CustomerAddressResult(CustomerAddressOutcome Outcome, CustomerAddressDto? Address)
{
    public static CustomerAddressResult Success(CustomerAddressDto address) => new(CustomerAddressOutcome.Success, address);
    public static readonly CustomerAddressResult NotFound = new(CustomerAddressOutcome.NotFound, null);
    public static readonly CustomerAddressResult InvalidReference = new(CustomerAddressOutcome.InvalidReference, null);
    public static readonly CustomerAddressResult AlreadyLinked = new(CustomerAddressOutcome.AlreadyLinked, null);
}
