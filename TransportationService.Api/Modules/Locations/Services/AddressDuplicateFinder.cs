using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Locations.Dtos;

namespace TransportationService.Api.Modules.Locations.Services;

/// <summary>
/// The one duplicate-detection query, shared by the explicit duplicate-check endpoint and the
/// create-address use case (which enforces the rule server-side). Matching is done on the
/// persisted normalised keys, never on a display string.
/// </summary>
public static class AddressDuplicateFinder
{
    /// <summary>
    /// D3 ("Opslaan in adresboek" on an order stop): the ACTIVE address with exactly this front door,
    /// if any — the one a save must LINK instead of creating a second copy. Same persisted key and
    /// same "only an active exact match counts" rule as <see cref="FindAsync"/>, without the
    /// candidate list (and therefore without its 25-row display cut-off). Oldest first, so the
    /// answer is stable when legacy data already holds several copies.
    /// </summary>
    public static async Task<Guid?> FindActiveExactAsync(
        TransportationDbContext db, Guid tenantId, string exactKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(exactKey))
        {
            return null; // not enough address to compare on
        }

        return await db.Locations.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive && l.AddressExactKey == exactKey)
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<AddressDuplicateCheckResultDto> FindAsync(
        TransportationDbContext db, Guid tenantId, AddressDuplicateCheckRequest request, CancellationToken cancellationToken)
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

        var candidates = await db.Locations.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.AddressStreetKey == streetKey)
            .Where(l => request.ExcludeLocationId == null || l.Id != request.ExcludeLocationId)
            .OrderBy(l => l.Name)
            .Take(25)
            .Select(l => new
            {
                l.Id, l.Code, l.Name, l.Type, l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode, l.IsActive,
                IsExact = l.AddressExactKey == exactKey,
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new AddressDuplicateCheckResultDto(false, []);
        }

        var ids = candidates.Select(c => c.Id).ToList();
        var customersByLocation = await db.CustomerLocationLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && ids.Contains(l.LocationId))
            .Join(db.Customers.Where(c => c.TenantId == tenantId),
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
                grouped.GetValueOrDefault(c.Id, []),
                c.Type))
            // Same front door first — that is the one the user almost certainly means.
            .OrderBy(c => c.Match == AddressDuplicateMatch.Exact ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToList();

        // Only an ACTIVE same-front-door address blocks a create; a deactivated one is still
        // listed so the user can see (and reactivate) it, but it is no reason to refuse.
        return new AddressDuplicateCheckResultDto(dtos.Any(d => d.Match == AddressDuplicateMatch.Exact && d.IsActive), dtos);
    }
}
