using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

public interface ITransportDocumentService
{
    /// <summary>Null when the order does not exist in the tenant.</summary>
    Task<(byte[] Content, string FileName)?> RenderAsync(Guid orderId, string kind, CancellationToken cancellationToken);

    /// <summary>Merged batch for every order on the trip (route order); null = unknown trip.</summary>
    Task<(byte[] Content, string FileName)?> RenderTripBatchAsync(Guid tripId, string kind, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 9: assembles delivery-note/CMR snapshots from frozen order data (seller = the order's
/// issuing entity, else the tenant default) and renders via the PDFsharp house renderer. Batch
/// = one merged PDF per trip, in route order — the "print everything for this trip" run.
/// </summary>
public class TransportDocumentService : ITransportDocumentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public TransportDocumentService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<(byte[] Content, string FileName)?> RenderAsync(
        Guid orderId, string kind, CancellationToken cancellationToken)
    {
        var snapshot = await BuildSnapshotAsync(orderId, NormalizeKind(kind), cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var prefix = snapshot.Kind == "Cmr" ? "cmr" : "leveringsbon";
        return (TransportDocumentRenderer.Render(snapshot), $"{prefix}-{snapshot.OrderNumber}.pdf");
    }

    public async Task<(byte[] Content, string FileName)?> RenderTripBatchAsync(
        Guid tripId, string kind, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var normalized = NormalizeKind(kind);
        var snapshots = new List<TransportDocumentSnapshot>();
        foreach (var tripOrder in trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence))
        {
            if (await BuildSnapshotAsync(tripOrder.TransportOrderId, normalized, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        var prefix = normalized == "Cmr" ? "cmr" : "leveringsbonnen";
        return (TransportDocumentRenderer.RenderBatch(snapshots), $"{prefix}-{trip.TripNumber}.pdf");
    }

    private static string NormalizeKind(string kind) =>
        string.Equals(kind, "cmr", StringComparison.OrdinalIgnoreCase) ? "Cmr" : "DeliveryNote";

    private async Task<TransportDocumentSnapshot?> BuildSnapshotAsync(
        Guid orderId, string kind, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == order.CustomerId, cancellationToken);
        var entity = order.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == entityId, cancellationToken)
            : await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.IsActive)
                .OrderByDescending(e => e.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);

        var cargo = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TransportOrderId == orderId && !c.IsDeleted)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);
        var lines = cargo.Count > 0
            ? cargo.Select(c => new TransportDocumentLine(
                    string.IsNullOrWhiteSpace(c.Description) ? "Goederen" : c.Description!,
                    c.ExpectedQuantity, c.QuantityUnitCode ?? c.QuantityUnit, c.TotalWeightKg))
                .ToList()
            : [new TransportDocumentLine(
                order.GoodsDescription ?? "Goederen", order.Quantity ?? 1m,
                order.QuantityUnitCode ?? order.QuantityUnit, order.WeightKg)];

        static string? Address(string? street, string? number, string? postal, string? city) =>
            string.Join(" ", new[] { street, number, postal, city }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } joined
                ? joined
                : null;

        return new TransportDocumentSnapshot(
            kind, order.OrderNumber, order.OrderDate,
            new TransportDocumentParty(
                entity?.LegalName ?? "—",
                Address(entity?.Street, entity?.HouseNumber, entity?.PostalCode, entity?.City),
                entity?.VatNumber),
            new TransportDocumentParty(
                customer?.Name ?? "—",
                Address(customer?.Street, customer?.HouseNumber, customer?.PostalCode, customer?.City),
                customer?.VatNumber),
            order.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence)
                .Select(s => new TransportDocumentStop(
                    s.StopType == StopType.Loading ? "Laden" : "Lossen",
                    s.LocationName ?? s.City,
                    Address(s.Address, null, s.PostalCode, s.City),
                    s.Reference))
                .ToList(),
            lines,
            order.WeightKg ?? (cargo.Count > 0 ? cargo.Sum(c => c.TotalWeightKg ?? 0m) : null),
            order.CustomerReference,
            order.Notes);
    }
}
