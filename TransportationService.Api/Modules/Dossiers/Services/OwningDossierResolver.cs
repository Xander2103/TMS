using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Dossiers.Services;

public sealed record OwningDossierRef(Guid Id, string DossierNumber);

/// <summary>
/// THE rule for "the dossier of an order" (an order may be linked to several): the order's own
/// wrapper (<c>OriginTransportOrderId</c>) wins, else the first (oldest) user-created link. One
/// definition shared by the order header chip, order documents (D6: <c>DossierId</c> of an order
/// document) and issued transport documents; the migration backfill uses the same precedence.
/// </summary>
public static class OwningDossierResolver
{
    /// <param name="excludingLinkId">A <c>DossierOrder</c> link that is being removed in the current
    /// unit of work — resolves the owner as it will be AFTER that removal.</param>
    public static async Task<OwningDossierRef?> ResolveAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid orderId, CancellationToken cancellationToken,
        Guid? excludingLinkId = null)
    {
        var wrapper = await dbContext.TransportDossiers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.OriginTransportOrderId == orderId)
            .Select(d => new { d.Id, d.DossierNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (wrapper is not null)
        {
            return new OwningDossierRef(wrapper.Id, wrapper.DossierNumber);
        }

        var linked = await dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == orderId
                        && (excludingLinkId == null || l.Id != excludingLinkId))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Join(dbContext.TransportDossiers.AsNoTracking(), l => l.DossierId, d => d.Id,
                (l, d) => new { d.Id, d.DossierNumber })
            .FirstOrDefaultAsync(cancellationToken);
        return linked is null ? null : new OwningDossierRef(linked.Id, linked.DossierNumber);
    }

    /// <summary>Set-based variant: owning dossier id per order (orders without a dossier are absent). Two queries.</summary>
    public static async Task<Dictionary<Guid, Guid>> ResolveManyAsync(
        TransportationDbContext dbContext, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Guid>();
        if (orderIds.Count == 0)
        {
            return result;
        }

        var nullableIds = orderIds.Select(id => (Guid?)id).ToList();
        var wrappers = await dbContext.TransportDossiers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && nullableIds.Contains(d.OriginTransportOrderId))
            .Select(d => new { OrderId = d.OriginTransportOrderId!.Value, d.Id })
            .ToListAsync(cancellationToken);
        foreach (var wrapper in wrappers)
        {
            result[wrapper.OrderId] = wrapper.Id;
        }

        var links = await dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && orderIds.Contains(l.TransportOrderId))
            .Join(dbContext.TransportDossiers.AsNoTracking(), l => l.DossierId, d => d.Id,
                (l, d) => new { l.TransportOrderId, l.DossierId, l.CreatedAt, l.Id })
            .ToListAsync(cancellationToken);
        foreach (var link in links.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id))
        {
            result.TryAdd(link.TransportOrderId, link.DossierId);
        }

        return result;
    }
}
