using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>What changing the customer will do, shown BEFORE the user confirms (sprint 6A).</summary>
public record CustomerChangeImpactDto(
    Guid OrderId,
    string OrderNumber,
    Guid CurrentCustomerId,
    string CurrentCustomerName,
    Guid NewCustomerId,
    string NewCustomerName,
    /// <summary>Blocking reason; when set the change is refused.</summary>
    string? BlockedReason,
    /// <summary>Automatic price lines that will be invalidated because they came from the old customer.</summary>
    int AutomaticLinesInvalidated,
    /// <summary>Manual lines that stay, and stay visibly manual.</summary>
    int ManualLinesKept,
    /// <summary>True when the new customer has no usable tariff, so the order lands in pricing review.</summary>
    bool NeedsPricingReview,
    /// <summary>The invoicing entity that will apply after the change.</summary>
    Guid? NewLegalEntityId,
    bool LegalEntityChanges,
    string? NewInvoiceLanguage,
    string? NewVatTreatment,
    /// <summary>Operational data that is explicitly NOT touched.</summary>
    int StopsKept,
    int GoodsKept,
    int DocumentsKept,
    /// <summary>
    /// Draft invoice lines for this order that will be released, so no concept invoice is left
    /// holding an order that now belongs to a different customer (sprint 6E).
    /// </summary>
    int DraftInvoiceLinesReleased = 0);

public record ChangeOrderCustomerRequest(Guid NewCustomerId, string Reason);

public interface IOrderCustomerChangeService
{
    Task<CustomerChangeImpactDto?> PreviewAsync(Guid orderId, Guid newCustomerId, CancellationToken cancellationToken);
    Task<CustomerChangeImpactDto?> ApplyAsync(Guid orderId, ChangeOrderCustomerRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Sprint 6 — moving an order to the customer it really belongs to.
///
/// Orders are routinely created before the real customer is known (a placeholder, "onbekende
/// klant", a temporary account). Re-keying the whole dossier afterwards is the thing this
/// avoids: stops, goods, scans, documents, planning and POD are FACTS about what happened and
/// are never touched.
///
/// What must change is everything the OLD customer decided: an automatically calculated price
/// belonged to the old customer's tariffs and is invalidated rather than silently carried over.
/// Manual prices stay, and stay visibly manual — someone typed those on purpose.
/// </summary>
public class OrderCustomerChangeService : IOrderCustomerChangeService
{
    private const string EntityType = "TransportOrder";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public OrderCustomerChangeService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    private Guid TenantId => _tenantContext.TenantId;

    public async Task<CustomerChangeImpactDto?> PreviewAsync(
        Guid orderId, Guid newCustomerId, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(orderId, newCustomerId, cancellationToken);
        return context?.Impact;
    }

    public async Task<CustomerChangeImpactDto?> ApplyAsync(
        Guid orderId, ChangeOrderCustomerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Geef een reden op voor de klantwijziging.");
        }

        var context = await LoadAsync(orderId, request.NewCustomerId, cancellationToken);
        if (context is null) return null;

        if (context.Impact.BlockedReason is { } blocked)
        {
            throw new DomainValidationException("customerId", blocked);
        }

        var order = context.Order;
        var previousCustomerId = order.CustomerId;
        var previousEntityId = order.LegalEntityId;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        order.CustomerId = request.NewCustomerId;
        order.LegalEntityId = context.Impact.NewLegalEntityId;

        // Automatic pricing belonged to the OLD customer's tariffs. It is removed, never
        // silently kept: an amount that claims to be "automatic" must be reproducible from the
        // current customer's configuration.
        var automatic = context.PricingLines
            .Where(l => l.Kind is OrderPriceLineKind.Auto or OrderPriceLineKind.Proposed)
            .ToList();
        _dbContext.RemoveRange(automatic);

        // An auto line the user had adjusted keeps its amount, but loses its automatic basis:
        // it becomes plainly manual so nobody mistakes it for a current tariff result.
        foreach (var adjusted in context.PricingLines.Where(l => l.Kind == OrderPriceLineKind.AutoAdjusted))
        {
            adjusted.Kind = OrderPriceLineKind.Manual;
            adjusted.Proposed = false;
            adjusted.RuleName = null;
            adjusted.AgreementName = null;
        }

        // The order goes back to needing a price decision under the new customer.
        if (context.Snapshot is { } snapshot)
        {
            snapshot.Status = OrderPricingStatus.Draft;
            snapshot.CalculatedTotal = null;
            snapshot.AgreementNames = null;
        }

        order.AgreedPrice = null;

        // Sprint 6E: a concept invoice must never end up holding an order that now belongs to
        // another customer. The order's lines are released from the draft; the invoice itself
        // stays (possibly empty) so the invoicing user sees what happened and can rebuild it.
        _dbContext.RemoveRange(context.DraftInvoiceLines);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "CustomerChanged",
            new { CustomerId = previousCustomerId, LegalEntityId = previousEntityId },
            new
            {
                CustomerId = order.CustomerId,
                LegalEntityId = order.LegalEntityId,
                Reason = request.Reason.Trim(),
                AutomaticLinesInvalidated = automatic.Count,
                context.Impact.ManualLinesKept,
                context.Impact.NeedsPricingReview,
                DraftInvoiceLinesReleased = context.DraftInvoiceLines.Count,
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return context.Impact;
    }

    private sealed record ChangeContext(
        TransportOrder Order,
        IReadOnlyList<TransportOrderPricingLine> PricingLines,
        TransportOrderPricingSnapshot? Snapshot,
        IReadOnlyList<InvoiceLine> DraftInvoiceLines,
        CustomerChangeImpactDto Impact);

    private async Task<ChangeContext?> LoadAsync(Guid orderId, Guid newCustomerId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.TransportOrders
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == TenantId && o.Id == orderId, cancellationToken);
        if (order is null) return null;

        var current = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == TenantId && c.Id == order.CustomerId, cancellationToken);
        var target = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == TenantId && c.Id == newCustomerId, cancellationToken);
        if (target is null)
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        var pricingLines = await _dbContext.TransportOrderPricingLines
            .Where(l => l.TenantId == TenantId && l.TransportOrderId == orderId)
            .ToListAsync(cancellationToken);
        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == TenantId && s.TransportOrderId == orderId, cancellationToken);

        var blocked = await BlockingReasonAsync(order, newCustomerId, cancellationToken);
        var draftLines = await DraftInvoiceLinesAsync(order.Id, cancellationToken);

        // The new customer's default entity applies, unless it is not allowed for them.
        var newEntityId = target.DefaultLegalEntityId ?? order.LegalEntityId;
        if (newEntityId is { } candidate)
        {
            var policyError = await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                _dbContext, TenantId, newCustomerId, candidate, cancellationToken);
            if (policyError is not null) newEntityId = target.DefaultLegalEntityId;
        }

        // "Does the new customer actually have a price for this?" — an assigned agreement or an
        // own price rule. Without one the order lands in pricing review instead of silently
        // keeping the old customer's amount.
        var hasTariff = await _dbContext.PricingAgreementAssignments.AsNoTracking()
            .AnyAsync(a => a.TenantId == TenantId && a.CustomerId == newCustomerId, cancellationToken)
            || await _dbContext.PriceRules.AsNoTracking()
                .AnyAsync(r => r.TenantId == TenantId && r.CustomerId == newCustomerId && r.IsActive, cancellationToken);

        var impact = new CustomerChangeImpactDto(
            order.Id, order.OrderNumber,
            order.CustomerId, current?.Name ?? "—",
            newCustomerId, target.Name,
            blocked,
            pricingLines.Count(l => l.Kind is OrderPriceLineKind.Auto or OrderPriceLineKind.Proposed),
            pricingLines.Count(l => l.Kind is OrderPriceLineKind.Manual or OrderPriceLineKind.AutoAdjusted),
            !hasTariff,
            newEntityId,
            newEntityId != order.LegalEntityId,
            target.InvoiceLanguageCode ?? target.DefaultLanguageCode,
            target.VatTreatment.ToString(),
            order.Stops.Count(s => !s.IsDeleted),
            await _dbContext.CargoItems.AsNoTracking()
                .CountAsync(c => c.TenantId == TenantId && c.TransportOrderId == orderId, cancellationToken),
            await _dbContext.TransportOrderDocuments.AsNoTracking()
                .CountAsync(d => d.TenantId == TenantId && d.TransportOrderId == orderId, cancellationToken),
            draftLines.Count);

        return new ChangeContext(order, pricingLines, snapshot, draftLines, impact);
    }

    /// <summary>The order's lines on invoices that are still Draft — released on a customer change.</summary>
    private async Task<IReadOnlyList<InvoiceLine>> DraftInvoiceLinesAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var draftInvoiceIds = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == TenantId && i.Status == InvoiceStatus.Draft)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        return await _dbContext.InvoiceLines
            .Where(l => l.TenantId == TenantId && l.TransportOrderId == orderId && draftInvoiceIds.Contains(l.InvoiceId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Financial safety: once the order sits on an invoice that left Draft, its customer is
    /// history and must be corrected through a credit note, never rewritten in place.
    /// </summary>
    private async Task<string?> BlockingReasonAsync(TransportOrder order, Guid newCustomerId, CancellationToken cancellationToken)
    {
        if (order.CustomerId == newCustomerId)
        {
            return "Deze order staat al op deze klant.";
        }

        var finalizedInvoice = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.TransportOrderId == order.Id)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.TenantId == TenantId),
                line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => invoice)
            .AnyAsync(i => i.Status != InvoiceStatus.Draft, cancellationToken);
        if (finalizedInvoice)
        {
            return "Deze order staat op een verzonden of geboekte factuur. Corrigeer via een creditnota; "
                   + "de historische factuur blijft ongewijzigd.";
        }

        return null;
    }
}
