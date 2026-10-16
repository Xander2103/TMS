using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>
/// M15 ownership semantics: <c>TransportDossier.ResponsibleUserId</c> is the internal user who
/// owns the dossier's follow-up (the "Mijn open" quick view filters on it). It defaults to the
/// internal user who creates the dossier (wizard, order create, import); portal and EDI intake
/// stay unowned until a planner accepts the request, who then becomes the owner if nobody is
/// yet. The planner can always change it on the dossier header.
/// </summary>
public static class DossierOwnership
{
    /// <summary>The current user when it is an active INTERNAL user (not a customer-portal user);
    /// null for anonymous/background callers and portal users.</summary>
    public static async Task<Guid?> InternalCreatorAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid? currentUserId, CancellationToken cancellationToken)
    {
        if (currentUserId is not { } userId)
        {
            return null;
        }

        var isInternal = await dbContext.Users.AsNoTracking()
            .AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive && u.CustomerId == null, cancellationToken);
        return isInternal ? userId : null;
    }

    /// <summary>Accepting a portal request: the accepting planner becomes the owner of every
    /// containing dossier that has none yet. Does not save.</summary>
    public static async Task AssignIfUnownedAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid transportOrderId, Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        if (await InternalCreatorAsync(dbContext, tenantId, currentUserId, cancellationToken) is not { } owner)
        {
            return;
        }

        var dossierIds = dbContext.DossierOrders
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == transportOrderId)
            .Select(l => l.DossierId);
        var dossiers = await dbContext.TransportDossiers
            .Where(d => d.TenantId == tenantId && dossierIds.Contains(d.Id) && d.ResponsibleUserId == null)
            .ToListAsync(cancellationToken);
        foreach (var dossier in dossiers)
        {
            dossier.ResponsibleUserId = owner;
        }
    }
}
