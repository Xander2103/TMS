using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// MD-M6 (master-data acceptance 2026-10-18): a completed order freezes the customer name it was
/// executed for, so renaming (or, legacy, deleting) the customer never rewrites executed history.
/// Applied at every completion point; never overwritten once set.
/// </summary>
public static class OrderCompletionSnapshot
{
    public static async Task ApplyAsync(TransportationDbContext db, TransportOrder order, CancellationToken cancellationToken)
    {
        if (order.CustomerNameAtCompletion is not null)
        {
            return;
        }

        order.CustomerNameAtCompletion = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == order.TenantId && c.Id == order.CustomerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
