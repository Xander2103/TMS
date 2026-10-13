using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Data;

namespace TransportationService.Api.Common.Persistence;

/// <summary>
/// Shared guards for foreign-key style references supplied by a client. Any id that arrives in a
/// request body must be proven to belong to the caller's tenant BEFORE it is stored or projected —
/// otherwise a guessed id from another tenant can be attached to local data (and leak its
/// name/address back through a later projection). Fail-closed: an unknown or foreign id throws
/// <see cref="InvalidTenantReferenceException"/>, which the global filter maps to a 400.
/// </summary>
public static class TenantReferenceGuard
{
    /// <summary>Throws when the referenced entity does not exist inside <paramref name="tenantId"/>.</summary>
    public static async Task EnsureBelongsToTenantAsync<TEntity>(
        this DbSet<TEntity> set, Guid? id, Guid tenantId, string dutchLabel, CancellationToken cancellationToken)
        where TEntity : class, ITenantOwned, IHasId
    {
        if (id is not { } value)
        {
            return;
        }

        var exists = await set.AsNoTracking().AnyAsync(e => e.Id == value && e.TenantId == tenantId, cancellationToken);
        if (!exists)
        {
            throw new InvalidTenantReferenceException(dutchLabel);
        }
    }

    /// <summary>Throws when any of the referenced entities is missing from the tenant.</summary>
    public static async Task EnsureAllBelongToTenantAsync<TEntity>(
        this DbSet<TEntity> set, IReadOnlyCollection<Guid> ids, Guid tenantId, string dutchLabel,
        CancellationToken cancellationToken)
        where TEntity : class, ITenantOwned, IHasId
    {
        if (ids.Count == 0)
        {
            return;
        }

        var distinct = ids.Distinct().ToList();
        var found = await set.AsNoTracking()
            .CountAsync(e => distinct.Contains(e.Id) && e.TenantId == tenantId, cancellationToken);
        if (found != distinct.Count)
        {
            throw new InvalidTenantReferenceException(dutchLabel);
        }
    }
}
