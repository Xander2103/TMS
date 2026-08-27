using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Locations.Services;

public interface ICustomerAddressService
{
    Task<IReadOnlyList<CustomerAddressDto>> ListForCustomerAsync(Guid customerId, bool includeInactive, CancellationToken cancellationToken);
    Task<AddressDuplicateCheckResultDto> CheckDuplicatesAsync(AddressDuplicateCheckRequest request, CancellationToken cancellationToken);
    Task<CustomerAddressResult> LinkAsync(Guid customerId, LinkCustomerAddressRequest request, CancellationToken cancellationToken);
    Task<CustomerAddressResult> UpdateLinkAsync(Guid customerId, Guid linkId, UpdateCustomerAddressLinkRequest request, CancellationToken cancellationToken);
    Task<bool> UnlinkAsync(Guid customerId, Guid linkId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AddressPickerOptionDto>> PickerAsync(Guid? customerId, string? search, int take, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the customer ↔ physical address relationship (sprint 2). <see cref="LocationService"/>
/// keeps owning the physical address itself; this service only ever creates, edits and removes
/// the relationship — unlinking a customer never touches the address, and never touches the
/// frozen address snapshot on historical order stops.
/// </summary>
public class CustomerAddressService : ICustomerAddressService
{
    private const string EntityType = "CustomerLocationLink";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public CustomerAddressService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private IQueryable<CustomerLocationLink> Links() =>
        _dbContext.CustomerLocationLinks.Where(l => l.TenantId == _tenantContext.TenantId);

    private IQueryable<Location> Locations() =>
        _dbContext.Locations.Where(l => l.TenantId == _tenantContext.TenantId);

    // ---------------------------------------------------------------- read

    public async Task<IReadOnlyList<CustomerAddressDto>> ListForCustomerAsync(
        Guid customerId, bool includeInactive, CancellationToken cancellationToken)
    {
        var query = Links().AsNoTracking().Where(l => l.CustomerId == customerId);
        if (!includeInactive) query = query.Where(l => l.IsActive);

        var rows = await query
            .Join(Locations().AsNoTracking(), link => link.LocationId, loc => loc.Id, (link, loc) => new { link, loc })
            .OrderByDescending(x => x.link.IsDefaultLoading || x.link.IsDefaultUnloading || x.link.IsDefaultBilling)
            .ThenBy(x => x.link.Alias ?? x.loc.Name)
            .ToListAsync(cancellationToken);

        // One extra query instead of a correlated count per row: how many customers share each
        // of these addresses (the reason a user should reuse rather than re-create).
        var locationIds = rows.Select(r => r.loc.Id).ToList();
        var shareCounts = await Links().AsNoTracking()
            .Where(l => locationIds.Contains(l.LocationId))
            .GroupBy(l => l.LocationId)
            .Select(g => new { LocationId = g.Key, Count = g.Select(x => x.CustomerId).Distinct().Count() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Count, cancellationToken);

        return rows.Select(x => Map(x.link, x.loc, shareCounts.GetValueOrDefault(x.loc.Id, 1))).ToList();
    }

    private static CustomerAddressDto Map(CustomerLocationLink link, Location loc, int linkedCustomerCount) =>
        new(link.Id, loc.Id, link.CustomerId, loc.Code, loc.Name, link.Alias, link.CustomerReference, loc.Type,
            link.Role, link.IsDefaultLoading, link.IsDefaultUnloading, link.IsDefaultBilling, link.Instructions,
            link.IsActive, loc.IsActive, loc.Street, loc.HouseNumber, loc.PostalCode, loc.City, loc.CountryCode,
            linkedCustomerCount);

    // ------------------------------------------------------ duplicate check

    public async Task<AddressDuplicateCheckResultDto> CheckDuplicatesAsync(
        AddressDuplicateCheckRequest request, CancellationToken cancellationToken)
    {
        var exactKey = AddressNormalizer.ExactKey(
            request.CountryCode, request.PostalCode, request.City, request.Street, request.HouseNumber);
        var streetKey = AddressNormalizer.StreetKey(
            request.CountryCode, request.PostalCode, request.City, request.Street);

        // Not enough address to compare on: never report "possible duplicate" from two blanks.
        if (streetKey.Length == 0)
        {
            return new AddressDuplicateCheckResultDto(false, []);
        }

        var candidates = await Locations().AsNoTracking()
            .Where(l => l.AddressStreetKey == streetKey)
            .Where(l => request.ExcludeLocationId == null || l.Id != request.ExcludeLocationId)
            .OrderBy(l => l.Name)
            .Take(25)
            .Select(l => new
            {
                l.Id, l.Code, l.Name, l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode, l.IsActive,
                IsExact = l.AddressExactKey == exactKey,
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new AddressDuplicateCheckResultDto(false, []);
        }

        var ids = candidates.Select(c => c.Id).ToList();
        var customersByLocation = await Links().AsNoTracking()
            .Where(l => ids.Contains(l.LocationId))
            .Join(_dbContext.Customers.Where(c => c.TenantId == _tenantContext.TenantId),
                link => link.CustomerId, c => c.Id, (link, c) => new { link.LocationId, c.Name })
            .ToListAsync(cancellationToken);

        var grouped = customersByLocation
            .GroupBy(x => x.LocationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).Distinct().OrderBy(n => n).ToList());

        var dtos = candidates
            .Select(c => new AddressDuplicateCandidateDto(
                c.Id, c.Code, c.Name,
                c.IsExact && exactKey.Length > 0 ? AddressDuplicateMatch.Exact : AddressDuplicateMatch.SameStreet,
                c.Street, c.HouseNumber, c.PostalCode, c.City, c.CountryCode, c.IsActive,
                grouped.GetValueOrDefault(c.Id, [])))
            // Same front door first — that is the one the user almost certainly means.
            .OrderBy(c => c.Match == AddressDuplicateMatch.Exact ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToList();

        return new AddressDuplicateCheckResultDto(dtos.Any(d => d.Match == AddressDuplicateMatch.Exact), dtos);
    }

    // --------------------------------------------------------------- write

    public async Task<CustomerAddressResult> LinkAsync(
        Guid customerId, LinkCustomerAddressRequest request, CancellationToken cancellationToken)
    {
        var customerExists = await _dbContext.Customers
            .AnyAsync(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId, cancellationToken);
        var location = await Locations().FirstOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);
        if (!customerExists || location is null)
        {
            return CustomerAddressResult.InvalidReference;
        }

        if (await Links().AnyAsync(l => l.CustomerId == customerId && l.LocationId == request.LocationId, cancellationToken))
        {
            return CustomerAddressResult.AlreadyLinked;
        }

        var link = new CustomerLocationLink
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = customerId,
            LocationId = request.LocationId,
            Alias = Trim(request.Alias),
            CustomerReference = Trim(request.CustomerReference),
            Role = request.Role,
            Instructions = Trim(request.Instructions),
            IsActive = true,
        };

        // Promote/demote runs as an immediate UPDATE, so it shares the insert's transaction.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ApplyDefaultsAsync(link, request.IsDefaultLoading, request.IsDefaultUnloading, request.IsDefaultBilling, cancellationToken);
        _dbContext.CustomerLocationLinks.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncLegacyOwnerAsync(request.LocationId, customerId, cancellationToken);

        await _auditService.RecordAsync(EntityType, link.Id.ToString(), "Linked", null,
            new { link.CustomerId, link.LocationId, link.Role, link.IsDefaultLoading, link.IsDefaultUnloading, link.IsDefaultBilling },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerAddressResult.Success(Map(link, location, await ShareCountAsync(location.Id, cancellationToken)));
    }

    public async Task<CustomerAddressResult> UpdateLinkAsync(
        Guid customerId, Guid linkId, UpdateCustomerAddressLinkRequest request, CancellationToken cancellationToken)
    {
        var link = await Links().FirstOrDefaultAsync(l => l.Id == linkId && l.CustomerId == customerId, cancellationToken);
        if (link is null) return CustomerAddressResult.NotFound;

        var location = await Locations().FirstOrDefaultAsync(l => l.Id == link.LocationId, cancellationToken);
        if (location is null) return CustomerAddressResult.NotFound;

        var before = new { link.Alias, link.CustomerReference, link.Role, link.IsDefaultLoading, link.IsDefaultUnloading, link.IsDefaultBilling, link.Instructions, link.IsActive };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        link.Alias = Trim(request.Alias);
        link.CustomerReference = Trim(request.CustomerReference);
        link.Role = request.Role;
        link.Instructions = Trim(request.Instructions);
        link.IsActive = request.IsActive;
        await ApplyDefaultsAsync(link, request.IsDefaultLoading, request.IsDefaultUnloading, request.IsDefaultBilling, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncLegacyOwnerAsync(link.LocationId, customerId, cancellationToken);

        await _auditService.RecordAsync(EntityType, link.Id.ToString(), "Updated", before,
            new { link.Alias, link.CustomerReference, link.Role, link.IsDefaultLoading, link.IsDefaultUnloading, link.IsDefaultBilling, link.Instructions, link.IsActive },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerAddressResult.Success(Map(link, location, await ShareCountAsync(location.Id, cancellationToken)));
    }

    /// <summary>
    /// Removes ONLY the relationship. The physical address stays — other customers keep using
    /// it, and historical orders keep their frozen snapshot either way.
    /// </summary>
    public async Task<bool> UnlinkAsync(Guid customerId, Guid linkId, CancellationToken cancellationToken)
    {
        var link = await Links().FirstOrDefaultAsync(l => l.Id == linkId && l.CustomerId == customerId, cancellationToken);
        if (link is null) return false;

        var locationId = link.LocationId;
        var before = new { link.CustomerId, link.LocationId, link.Role };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.CustomerLocationLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncLegacyOwnerAsync(locationId, customerId, cancellationToken);

        await _auditService.RecordAsync(EntityType, linkId.ToString(), "Unlinked", before, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // -------------------------------------------------------------- picker

    /// <summary>
    /// Address selection for orders/dossiers (sprint 2E), ordered by usefulness:
    /// the customer's own addresses first, then addresses this tenant used recently,
    /// then the rest of the central master.
    /// </summary>
    public async Task<IReadOnlyList<AddressPickerOptionDto>> PickerAsync(
        Guid? customerId, string? search, int take, CancellationToken cancellationToken)
    {
        var query = Locations().AsNoTracking().Where(l => l.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                l.Name.ToLower().Contains(term) ||
                l.Code.ToLower().Contains(term) ||
                (l.City != null && l.City.ToLower().Contains(term)) ||
                (l.Street != null && l.Street.ToLower().Contains(term)) ||
                (l.PostalCode != null && l.PostalCode.ToLower().Contains(term)));
        }

        var linkedIds = customerId is { } cid
            ? await Links().AsNoTracking().Where(l => l.CustomerId == cid && l.IsActive)
                .Select(l => l.LocationId).ToListAsync(cancellationToken)
            : [];

        // "Recent" = used by a stop of this tenant lately. Stops keep their own frozen address,
        // so this only ranks the picker; it never reads history back into the master.
        var recentIds = await _dbContext.Set<TransportOrderStop>().AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.LocationId != null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.LocationId!.Value)
            .Take(200)
            .Distinct()
            .ToListAsync(cancellationToken);

        var candidates = await query
            .Select(l => new { l.Id, l.Code, l.Name, l.Type, l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode })
            .ToListAsync(cancellationToken);

        var linked = linkedIds.ToHashSet();
        var recent = recentIds.ToHashSet();

        return candidates
            .Select(l => new
            {
                Dto = new AddressPickerOptionDto(
                    l.Id, l.Code, l.Name, l.Type, l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode,
                    linked.Contains(l.Id)
                        ? AddressPickerGroup.CustomerAddress
                        : recent.Contains(l.Id) ? AddressPickerGroup.Recent : AddressPickerGroup.All),
            })
            .OrderBy(x => (int)x.Dto.Group)
            .ThenBy(x => x.Dto.Name)
            .Take(take <= 0 ? 50 : take)
            .Select(x => x.Dto)
            .ToList();
    }

    // ------------------------------------------------------------- helpers

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<int> ShareCountAsync(Guid locationId, CancellationToken cancellationToken) =>
        await Links().AsNoTracking().Where(l => l.LocationId == locationId)
            .Select(l => l.CustomerId).Distinct().CountAsync(cancellationToken);

    /// <summary>
    /// At most one default of each kind per customer: demote the previous holder with an
    /// immediate UPDATE before promoting, so a single SaveChanges batch cannot order the
    /// promote first and trip the filtered unique index (same approach as LocationService).
    /// </summary>
    private async Task ApplyDefaultsAsync(
        CustomerLocationLink link, bool loading, bool unloading, bool billing, CancellationToken cancellationToken)
    {
        if (loading || unloading || billing)
        {
            await Links()
                .Where(l => l.CustomerId == link.CustomerId && l.Id != link.Id
                    && ((loading && l.IsDefaultLoading) || (unloading && l.IsDefaultUnloading) || (billing && l.IsDefaultBilling)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.IsDefaultLoading, l => !loading && l.IsDefaultLoading)
                    .SetProperty(l => l.IsDefaultUnloading, l => !unloading && l.IsDefaultUnloading)
                    .SetProperty(l => l.IsDefaultBilling, l => !billing && l.IsDefaultBilling),
                    cancellationToken);
        }

        link.IsDefaultLoading = loading;
        link.IsDefaultUnloading = unloading;
        link.IsDefaultBilling = billing;
    }

    /// <summary>
    /// Compatibility bridge while the legacy <c>Location.CustomerId</c> + default flags are still
    /// present: mirror the OLDEST remaining relationship onto the address. With one link this is
    /// exactly the pre-sprint behaviour; with several, the original owner keeps the legacy slot
    /// rather than the value flip-flopping. Links remain the source of truth.
    ///
    /// Every address of the affected customer is refreshed, not just the one that changed:
    /// promoting a new default demotes another link, and the legacy per-customer default indexes
    /// on <c>locations</c> would otherwise still see two holders and reject the write.
    /// </summary>
    private async Task SyncLegacyOwnerAsync(Guid locationId, Guid customerId, CancellationToken cancellationToken)
    {
        var affectedIds = await Links().AsNoTracking()
            .Where(l => l.CustomerId == customerId)
            .Select(l => l.LocationId)
            .ToListAsync(cancellationToken);
        if (!affectedIds.Contains(locationId)) affectedIds.Add(locationId);

        var locations = await Locations().Where(l => affectedIds.Contains(l.Id)).ToListAsync(cancellationToken);
        var owners = await Links().AsNoTracking()
            .Where(l => affectedIds.Contains(l.LocationId))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        // Demote first, then promote: a single SaveChanges batch may order the promote first and
        // trip the filtered unique index that allows one default holder per customer.
        foreach (var location in locations)
        {
            location.CustomerId = null;
            location.IsDefaultLoadingLocation = false;
            location.IsDefaultUnloadingLocation = false;
            location.IsDefaultBillingLocation = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var location in locations)
        {
            var owner = owners.FirstOrDefault(o => o.LocationId == location.Id);
            if (owner is null) continue;

            location.CustomerId = owner.CustomerId;
            location.IsDefaultLoadingLocation = owner.IsDefaultLoading;
            location.IsDefaultUnloadingLocation = owner.IsDefaultUnloading;
            location.IsDefaultBillingLocation = owner.IsDefaultBilling;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
