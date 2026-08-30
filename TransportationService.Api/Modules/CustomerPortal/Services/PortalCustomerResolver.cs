using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

/// <summary>The caller's portal identity: which user, which customer, and the two language
/// preferences a portal service may need. Never contains anything a customer may not see.</summary>
public sealed record PortalCustomerLink(
    Guid UserId, Guid CustomerId, string CustomerName, string? UserLanguageCode, string? CustomerLanguageCode);

/// <summary>
/// THE single definition of "which customer is the caller" for the portal (H-14 / I-4). Every
/// <c>/api/customer-portal/*</c> service answers that question before it touches data, and each of
/// them used to spell the query out again — which is how <c>GET /announcements</c> ended up as the
/// one endpoint with no resolver at all, still serving a customer the tenant had deactivated.
///
/// Two invariants live here and nowhere else:
/// <list type="bullet">
/// <item>the customer context comes from the authenticated user's own row, never from the client;</item>
/// <item>the customer must still be ACTIVE — deactivating a customer cuts portal access at once.</item>
/// </list>
/// </summary>
public static class PortalCustomerResolver
{
    /// <summary>The caller's active customer, or null when there is no user, no customer link,
    /// another tenant's customer, or the customer is deactivated/soft-deleted.</summary>
    public static async Task<PortalCustomerLink?> ResolveAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return null;
        }

        return await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == id && u.TenantId == tenantId && u.CustomerId != null)
            .Join(dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId && c.IsActive),
                u => u.CustomerId, c => c.Id,
                (u, c) => new PortalCustomerLink(u.Id, c.Id, c.Name, u.PreferredLanguageCode, c.DefaultLanguageCode))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Id-only convenience for the services that need nothing else.</summary>
    public static async Task<Guid?> ResolveCustomerIdAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid? userId, CancellationToken cancellationToken) =>
        (await ResolveAsync(dbContext, tenantId, userId, cancellationToken))?.CustomerId;
}
