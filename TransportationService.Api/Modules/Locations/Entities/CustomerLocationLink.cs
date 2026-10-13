using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Locations.Entities;

/// <summary>
/// How a customer uses a physical address. A place is normally both a loading and an
/// unloading address; the narrower roles let planning offer only the addresses that make
/// sense for a stop kind.
/// </summary>
public enum CustomerLocationRole
{
    Both,
    Loading,
    Unloading,
}

/// <summary>
/// The relationship between a customer and a physical <see cref="Location"/>
/// (sprint 2, central address master).
///
/// A physical address exists ONCE and may be used by zero, one or many customers, so the
/// customer relationship — and everything customer-specific about it (alias, the customer's
/// own reference, the loading/unloading role, the per-customer defaults and instructions) —
/// lives here rather than on the address itself. Unlinking a customer removes this row only;
/// the physical address, and every historical order that referenced it, stay untouched.
///
/// Compatibility: <see cref="Location.CustomerId"/> and the <c>IsDefault…Location</c> flags on
/// the address are kept in sync for the single-link case while the legacy columns are still
/// read by older code paths. Links are the source of truth.
/// </summary>
public class CustomerLocationLink : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }

    public Guid LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>Customer-specific name for this place, e.g. "Magazijn Noord". Null = the address's own name.</summary>
    public string? Alias { get; set; }

    /// <summary>The customer's own code/reference for this address, as used in their EDI/Excel files.</summary>
    public string? CustomerReference { get; set; }

    public CustomerLocationRole Role { get; set; } = CustomerLocationRole.Both;

    /// <summary>At most one per customer per kind (filtered unique indexes).</summary>
    public bool IsDefaultLoading { get; set; }

    /// <inheritdoc cref="IsDefaultLoading"/>
    public bool IsDefaultUnloading { get; set; }

    /// <inheritdoc cref="IsDefaultLoading"/>
    public bool IsDefaultBilling { get; set; }

    /// <summary>Customer-specific handling instructions for this address.</summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Inactive links stay out of the selectors but keep the relationship (and its history)
    /// visible, mirroring how addresses themselves are deactivated rather than deleted.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
