using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>
/// Wave 2 (spec Part O): the allowed-issuing-entities policy of a customer. An EMPTY set means
/// every active entity is allowed (backward-compatible default). Static helpers instead of an
/// injected service so the four consuming services (customer/dossier/order/invoice) need no
/// constructor changes.
/// </summary>
public static class CustomerEntityPolicy
{
    /// <summary>The customer's allowed entity ids; empty = no restriction configured.</summary>
    public static Task<List<Guid>> GetAllowedIdsAsync(
        TransportationDbContext db, Guid tenantId, Guid customerId, CancellationToken cancellationToken) =>
        db.CustomerAllowedLegalEntities.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId)
            .Select(x => x.LegalEntityId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Null when the entity choice is acceptable for this customer (no restriction, or inside
    /// the set); otherwise a Dutch validation message naming the allowed entities.
    /// </summary>
    public static async Task<string?> ValidateAsync(
        TransportationDbContext db, Guid tenantId, Guid customerId, Guid? legalEntityId,
        CancellationToken cancellationToken)
    {
        if (legalEntityId is not { } entityId)
        {
            return null;
        }

        var allowed = await GetAllowedIdsAsync(db, tenantId, customerId, cancellationToken);
        if (allowed.Count == 0 || allowed.Contains(entityId))
        {
            return null;
        }

        var names = await db.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && allowed.Contains(e.Id))
            .OrderBy(e => e.LegalName)
            .Select(e => e.LegalName)
            .ToListAsync(cancellationToken);
        return $"Deze facturerende entiteit is niet toegestaan voor deze klant. Toegestaan: {string.Join(", ", names)}.";
    }
}
