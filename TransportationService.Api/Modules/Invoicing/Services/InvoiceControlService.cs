using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public sealed record ControlOrderDto(
    Guid TransportOrderId, string OrderNumber, DateOnly OrderDate, decimal? AgreedPrice,
    string? DossierNumber, string? CustomerReference,
    string InvoiceReadiness, IReadOnlyList<string> Reasons);

public sealed record InvoiceProposalDto(
    Guid CustomerId, string CustomerName, string Grouping,
    /// <summary>"Dossier DOS-0012" | "Week 33" | "Maand augustus" | "Referentie X" | "Klaar".</summary>
    string GroupLabel,
    IReadOnlyList<ControlOrderDto> Orders,
    decimal TotalAmount);

public sealed record InvoiceControlDto(
    /// <summary>Proposals of READY orders, honoring each customer's grouping preference.</summary>
    IReadOnlyList<InvoiceProposalDto> Proposals,
    /// <summary>Completed but ReviewRequired orders with their readiness reasons.</summary>
    IReadOnlyList<ControlOrderDto> NeedsReview,
    /// <summary>Approved incident charges whose sales line could not land (locked/invoiced order).</summary>
    IReadOnlyList<string> PendingCharges);

public interface IInvoiceControlService
{
    Task<InvoiceControlDto> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Wave 10: the invoice-control workspace. Reads the Wave-2 readiness projection (never
/// recomputes it), groups READY uninvoiced orders per customer according to the customer's
/// InvoiceGrouping preference (PerDossier / Weekly / Monthly / ByReference; Manual customers
/// get one plain "Klaar" bucket) and surfaces ReviewRequired orders with their reasons plus
/// approved-but-unapplied incident charges. Creating the invoice = the EXISTING invoice
/// create endpoint with the proposal's order ids.
/// </summary>
public class InvoiceControlService : IInvoiceControlService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InvoiceControlService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<InvoiceControlDto> GetAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var invoicedOrderIds = _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId != null)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != Entities.InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => l.TransportOrderId!.Value);

        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Status == TransportOrderStatus.Completed
                        && !invoicedOrderIds.Contains(o.Id))
            .Join(_dbContext.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new
                {
                    o.Id, o.OrderNumber, o.OrderDate, o.AgreedPrice, o.CustomerReference,
                    o.InvoiceReadiness, o.InvoiceReadinessReasons,
                    CustomerId = c.Id, CustomerName = c.Name, c.InvoiceGrouping,
                })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var dossierByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && orderIds.Contains(l.TransportOrderId))
                .Join(_dbContext.TransportDossiers.AsNoTracking(), l => l.DossierId, d => d.Id,
                    (l, d) => new { l.TransportOrderId, d.DossierNumber })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.First().DossierNumber);

        var dtoById = orders.ToDictionary(
            o => o.Id,
            o => new ControlOrderDto(
                o.Id, o.OrderNumber, o.OrderDate, o.AgreedPrice,
                dossierByOrder.GetValueOrDefault(o.Id), o.CustomerReference,
                o.InvoiceReadiness,
                o.InvoiceReadinessReasons?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? []));

        var needsReview = orders
            .Where(o => o.InvoiceReadiness == "ReviewRequired")
            .OrderBy(o => o.OrderDate)
            .Select(o => dtoById[o.Id])
            .ToList();

        static string GroupKey(string grouping, DateOnly date, string? dossier, string? reference) => grouping switch
        {
            "PerDossier" => dossier ?? "Zonder dossier",
            "Weekly" => $"Week {System.Globalization.ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue))}",
            "Monthly" => $"Maand {date:MM-yyyy}",
            "ByReference" => reference is { Length: > 0 } ? $"Referentie {reference}" : "Zonder referentie",
            _ => "Klaar voor facturatie",
        };

        var proposals = orders
            .Where(o => o.InvoiceReadiness == "ReadyForInvoice")
            .GroupBy(o => new
            {
                o.CustomerId, o.CustomerName, o.InvoiceGrouping,
                Label = GroupKey(o.InvoiceGrouping, o.OrderDate,
                    dossierByOrder.GetValueOrDefault(o.Id), o.CustomerReference),
            })
            .Select(g => new InvoiceProposalDto(
                g.Key.CustomerId, g.Key.CustomerName, g.Key.InvoiceGrouping, g.Key.Label,
                g.OrderBy(o => o.OrderDate).Select(o => dtoById[o.Id]).ToList(),
                g.Sum(o => o.AgreedPrice ?? 0m)))
            .OrderBy(p => p.CustomerName).ThenBy(p => p.GroupLabel)
            .ToList();

        // Approved incident charges that could not land on the order (locked/invoiced price).
        var pendingCharges = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ChargeDecision == "Approved" && i.TransportOrderId != null)
            .Join(_dbContext.TransportOrderPricingSnapshots.AsNoTracking()
                    .Where(s => s.Status == OrderPricingStatus.Locked || s.Status == OrderPricingStatus.Invoiced),
                i => i.TransportOrderId, s => s.TransportOrderId,
                (i, s) => new { i.Title, i.ChargeAmount, i.TransportOrderId })
            .Join(_dbContext.TransportOrders.AsNoTracking(), x => x.TransportOrderId, o => o.Id,
                (x, o) => $"{o.OrderNumber}: € {x.ChargeAmount:0.00} — {x.Title} (prijs vergrendeld; voeg de lijn handmatig toe bij facturatie)")
            .ToListAsync(cancellationToken);

        return new InvoiceControlDto(proposals, needsReview, pendingCharges);
    }
}
