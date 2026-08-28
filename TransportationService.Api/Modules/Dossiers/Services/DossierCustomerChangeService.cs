using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>What a dossier-level customer change will do, aggregated over its linked orders.</summary>
public record DossierCustomerChangeImpactDto(
    Guid DossierId,
    string DossierNumber,
    Guid? CurrentCustomerId,
    string? CurrentCustomerName,
    Guid NewCustomerId,
    string NewCustomerName,
    /// <summary>Blocking reason for the whole dossier; set as soon as ONE linked order is blocked.</summary>
    string? BlockedReason,
    Guid? NewLegalEntityId,
    string? NewInvoiceLanguage,
    string? NewVatTreatment,
    /// <summary>Per-order consequences for the orders that will move.</summary>
    IReadOnlyList<CustomerChangeImpactDto> Orders,
    /// <summary>Linked orders already on a DIFFERENT customer than the dossier; left untouched and reported.</summary>
    IReadOnlyList<string> OrdersLeftOnOtherCustomer);

public record ChangeDossierCustomerRequest(Guid NewCustomerId, string Reason, Guid? Version = null);

public interface IDossierCustomerChangeService
{
    Task<DossierCustomerChangeImpactDto?> PreviewAsync(Guid dossierId, Guid newCustomerId, CancellationToken cancellationToken);
    Task<DossierCustomerChangeImpactDto?> ApplyAsync(Guid dossierId, ChangeDossierCustomerRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Sprint 6 (audit completion) — the dossier is the commercial authority for its linked orders,
/// so changing the dossier's customer moves every order that shared it, in ONE unit of work,
/// through the same per-order logic as a standalone order change. No second engine: pricing
/// invalidation, draft-invoice release, entity policy and the sent-invoice block all come from
/// <see cref="OrderCustomerChangeService"/>.
///
/// Orders that already sit on a different customer than the dossier (a pre-existing mixed
/// state) are not touched and are reported, so the change never silently deepens a mix.
/// </summary>
public class DossierCustomerChangeService : IDossierCustomerChangeService
{
    private const string EntityType = "TransportDossier";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly OrderCustomerChangeService _orders;
    private readonly IDossierService _dossiers;

    public DossierCustomerChangeService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        OrderCustomerChangeService orders, IDossierService dossiers)
    {
        _dossiers = dossiers;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _orders = orders;
    }

    private Guid TenantId => _tenantContext.TenantId;

    public async Task<DossierCustomerChangeImpactDto?> PreviewAsync(
        Guid dossierId, Guid newCustomerId, CancellationToken cancellationToken)
    {
        var ctx = await LoadAsync(dossierId, newCustomerId, cancellationToken);
        return ctx?.Impact;
    }

    public async Task<DossierCustomerChangeImpactDto?> ApplyAsync(
        Guid dossierId, ChangeDossierCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Geef een reden op voor de klantwijziging.");
        }

        var ctx = await LoadAsync(dossierId, request.NewCustomerId, cancellationToken);
        if (ctx is null) return null;

        if (request.Version is { } version && version != ctx.Dossier.Version)
        {
            throw new DossierVersionConflictException((await _dossiers.GetAsync(dossierId, cancellationToken))!);
        }

        if (ctx.Impact.BlockedReason is { } blocked)
        {
            throw new DomainValidationException("customerId", blocked);
        }

        var previousCustomerId = ctx.Dossier.CustomerId;
        var previousEntityId = ctx.Dossier.LegalEntityId;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var orderImpact in ctx.Impact.Orders)
        {
            await _orders.ApplyWithinDossierAsync(orderImpact.OrderId, request.NewCustomerId, request.Reason, cancellationToken);
        }

        ctx.Dossier.CustomerId = request.NewCustomerId;
        ctx.Dossier.LegalEntityId = ctx.Impact.NewLegalEntityId;
        ctx.Dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, ctx.Dossier.Id.ToString(), "CustomerChanged",
            new { CustomerId = previousCustomerId, LegalEntityId = previousEntityId },
            new
            {
                ctx.Dossier.CustomerId,
                ctx.Dossier.LegalEntityId,
                Reason = request.Reason.Trim(),
                OrdersMoved = ctx.Impact.Orders.Select(o => o.OrderNumber).ToList(),
                ctx.Impact.OrdersLeftOnOtherCustomer,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ctx.Impact;
    }

    private sealed record Context(TransportDossier Dossier, DossierCustomerChangeImpactDto Impact);

    private async Task<Context?> LoadAsync(Guid dossierId, Guid newCustomerId, CancellationToken cancellationToken)
    {
        var dossier = await _dbContext.TransportDossiers
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null) return null;

        var target = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == TenantId && c.Id == newCustomerId, cancellationToken);
        if (target is null)
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        var currentName = dossier.CustomerId is { } currentId
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == TenantId && c.Id == currentId).Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        string? blocked = null;
        if (dossier.Status == DossierStatus.Closed)
        {
            blocked = "Een gesloten dossier kan niet van klant wijzigen; heropen het dossier eerst.";
        }
        else if (dossier.CustomerId == newCustomerId)
        {
            blocked = "Dit dossier staat al op deze klant.";
        }

        var linkedOrders = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.DossierId == dossierId)
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == TenantId),
                l => l.TransportOrderId, o => o.Id, (l, o) => new { o.Id, o.OrderNumber, o.CustomerId })
            .ToListAsync(cancellationToken);

        var impacts = new List<CustomerChangeImpactDto>();
        var leftAlone = new List<string>();
        foreach (var order in linkedOrders)
        {
            if (order.CustomerId == newCustomerId) continue;

            // Only orders that FOLLOW the dossier's customer move with it. A dossier without a
            // customer yet claims all its orders.
            if (dossier.CustomerId is { } dossierCustomer && order.CustomerId != dossierCustomer)
            {
                leftAlone.Add(order.OrderNumber);
                continue;
            }

            var impact = await _orders.PreviewWithinDossierAsync(order.Id, newCustomerId, cancellationToken);
            if (impact is null) continue;
            if (impact.BlockedReason is { } orderBlocked && blocked is null)
            {
                blocked = $"Order {order.OrderNumber}: {orderBlocked}";
            }

            impacts.Add(impact);
        }

        // The dossier's entity follows the new customer exactly like an order's does.
        var newEntityId = impacts.FirstOrDefault()?.NewLegalEntityId
                          ?? target.DefaultLegalEntityId
                          ?? dossier.LegalEntityId;

        return new Context(dossier, new DossierCustomerChangeImpactDto(
            dossier.Id, dossier.DossierNumber,
            dossier.CustomerId, currentName,
            newCustomerId, target.Name,
            blocked,
            newEntityId,
            target.InvoiceLanguageCode ?? target.DefaultLanguageCode,
            target.VatTreatment.ToString(),
            impacts,
            leftAlone));
    }
}
