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
    /// <summary>
    /// Lines that were an automatic price the user adjusted. Their amount was DERIVED from the
    /// old customer's tariff, so they are kept only as unconfirmed proposals that must be
    /// explicitly confirmed (or removed) under the new customer before they count.
    /// </summary>
    int AdjustedLinesFlaggedForReview,
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
    int DraftInvoiceLinesReleased = 0,
    /// <summary>Set when the order belongs to a dossier whose customer it shares — change it there.</summary>
    Guid? OwningDossierId = null,
    string? OwningDossierNumber = null);

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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ApplyCoreAsync(context, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return context.Impact;
    }

    private async Task ApplyCoreAsync(ChangeContext context, ChangeOrderCustomerRequest request, CancellationToken cancellationToken)
    {
        var order = context.Order;
        var previousCustomerId = order.CustomerId;
        var previousEntityId = order.LegalEntityId;

        order.CustomerId = request.NewCustomerId;
        order.LegalEntityId = context.Impact.NewLegalEntityId;

        // Automatic pricing belonged to the OLD customer's tariffs. It is removed, never
        // silently kept: an amount that claims to be "automatic" must be reproducible from the
        // current customer's configuration.
        var automatic = context.PricingLines
            .Where(l => l.Kind is OrderPriceLineKind.Auto or OrderPriceLineKind.Proposed)
            .ToList();
        _dbContext.RemoveRange(automatic);

        // An auto line the user had adjusted started from the OLD customer's tariff, so its
        // amount is commercially suspect for the new one. The user's work is not thrown away,
        // but the amount is NOT allowed to count for the new customer on its own: the line
        // becomes an unconfirmed PROPOSAL (excluded from LinesTotal/AgreedPrice until someone
        // explicitly confirms it), its note names the old customer, and the engine baseline is
        // dropped so nothing still claims to be derived from the old tariff.
        var oldCustomerName = context.Impact.CurrentCustomerName;
        foreach (var adjusted in context.PricingLines.Where(l => l.Kind == OrderPriceLineKind.AutoAdjusted))
        {
            adjusted.Kind = OrderPriceLineKind.Proposed;
            adjusted.Proposed = true;
            adjusted.RuleName = null;
            adjusted.AgreementName = null;
            adjusted.RuleId = null;
            adjusted.OriginalAmount = null;
            adjusted.OriginalQuantity = null;
            adjusted.OriginalUnitPrice = null;
            adjusted.Source = "Klantwijziging — te bevestigen";
            var note = $"Overgenomen bij klantwijziging van '{oldCustomerName}' — bedrag bevestigen of verwijderen.";
            adjusted.AdjustReason = string.IsNullOrWhiteSpace(adjusted.AdjustReason)
                ? note
                : $"{note} ({adjusted.AdjustReason})";
        }

        // The order goes back to needing a price decision under the new customer. The frozen
        // numbers are flagged stale, so invoice readiness reports "pricing.stale" until a
        // recalculation/confirmation under the NEW customer happened — the old total can never
        // slip through to an invoice.
        if (context.Snapshot is { } snapshot)
        {
            snapshot.Status = OrderPricingStatus.Draft;
            snapshot.CalculatedTotal = null;
            snapshot.AgreementNames = null;
            snapshot.LinesTotal = decimal.Round(
                context.PricingLines
                    .Where(l => l.Kind == OrderPriceLineKind.Manual && !l.Informational)
                    .Sum(l => l.Amount), 2);
            snapshot.IsStale = true;
            snapshot.ConfirmedAtUtc = null;
            snapshot.ConfirmedByUserId = null;
            snapshot.ConfirmedByName = null;
        }

        order.AgreedPrice = null;

        // Sprint 6E: a concept invoice must never end up holding an order that now belongs to
        // another customer. The order's lines are released from the draft; the invoice itself
        // stays (possibly empty) so the invoicing user sees what happened and can rebuild it.
        // Audit fix: an order on a concept invoice carries Status = Invoiced; releasing its
        // lines must hand it back to Completed, otherwise it can never be invoiced again.
        _dbContext.RemoveRange(context.DraftInvoiceLines);
        if (context.DraftInvoiceLines.Count > 0 && order.Status == TransportOrderStatus.Invoiced)
        {
            order.Status = TransportOrderStatus.Completed;
        }

        order.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Readiness reads the persisted snapshot, so it runs after the stale flag is saved.
        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
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
                context.Impact.AdjustedLinesFlaggedForReview,
                context.Impact.NeedsPricingReview,
                DraftInvoiceLinesReleased = context.DraftInvoiceLines.Count,
            },
            cancellationToken);
    }

    private sealed record ChangeContext(
        TransportOrder Order,
        IReadOnlyList<TransportOrderPricingLine> PricingLines,
        TransportOrderPricingSnapshot? Snapshot,
        IReadOnlyList<InvoiceLine> DraftInvoiceLines,
        CustomerChangeImpactDto Impact);

    /// <summary>
    /// Applies the change for one order inside a transaction the CALLER owns (the dossier-level
    /// change moves every linked order in one unit of work). Skips the dossier guard, because
    /// the caller IS the dossier. Returns null when the order does not exist.
    /// </summary>
    public async Task<CustomerChangeImpactDto?> ApplyWithinDossierAsync(
        Guid orderId, Guid newCustomerId, string reason, CancellationToken cancellationToken)
    {
        var context = await LoadAsync(orderId, newCustomerId, cancellationToken, allowWithinDossier: true);
        if (context is null) return null;
        if (context.Impact.BlockedReason is { } blocked)
        {
            throw new DomainValidationException("customerId", blocked);
        }

        await ApplyCoreAsync(context, new ChangeOrderCustomerRequest(newCustomerId, reason), cancellationToken);
        return context.Impact;
    }

    /// <summary>Preview for the dossier flow: the dossier guard does not apply to itself.</summary>
    public async Task<CustomerChangeImpactDto?> PreviewWithinDossierAsync(
        Guid orderId, Guid newCustomerId, CancellationToken cancellationToken)
        => (await LoadAsync(orderId, newCustomerId, cancellationToken, allowWithinDossier: true))?.Impact;

    private async Task<ChangeContext?> LoadAsync(
        Guid orderId, Guid newCustomerId, CancellationToken cancellationToken, bool allowWithinDossier = false)
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

        // The new customer's default entity applies; the current entity is kept only when the
        // new customer has no default AND is allowed to be invoiced from it; otherwise the
        // tenant's default entity — an order is never left without an invoicing entity.
        var newEntityId = target.DefaultLegalEntityId ?? order.LegalEntityId;
        if (newEntityId is { } candidate
            && await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                _dbContext, TenantId, newCustomerId, candidate, cancellationToken) is not null)
        {
            newEntityId = target.DefaultLegalEntityId;
        }
        newEntityId ??= await _dbContext.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.IsActive && e.IsDefault)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // The dossier is the commercial authority for its orders: an order sharing its dossier's
        // customer is changed on the dossier, so linked orders never drift apart silently.
        var owningDossier = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.TransportOrderId == orderId)
            .Join(_dbContext.TransportDossiers.AsNoTracking().Where(d => d.TenantId == TenantId),
                l => l.DossierId, d => d.Id, (l, d) => new { d.Id, d.DossierNumber, d.CustomerId })
            .FirstOrDefaultAsync(d => d.CustomerId == order.CustomerId, cancellationToken);
        if (blocked is null && owningDossier is not null && !allowWithinDossier)
        {
            blocked = $"Deze order maakt deel uit van dossier {owningDossier.DossierNumber}. "
                      + "Wijzig de klant op het dossier, zodat alle gekoppelde orders samen blijven.";
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
            pricingLines.Count(l => l.Kind is OrderPriceLineKind.Manual),
            pricingLines.Count(l => l.Kind is OrderPriceLineKind.AutoAdjusted),
            // Review is needed when no tariff exists OR when adjusted amounts of the old
            // customer are being carried across for confirmation.
            !hasTariff || pricingLines.Any(l => l.Kind == OrderPriceLineKind.AutoAdjusted),
            newEntityId,
            newEntityId != order.LegalEntityId,
            target.InvoiceLanguageCode ?? target.DefaultLanguageCode,
            target.VatTreatment.ToString(),
            order.Stops.Count(s => !s.IsDeleted),
            await _dbContext.CargoItems.AsNoTracking()
                .CountAsync(c => c.TenantId == TenantId && c.TransportOrderId == orderId, cancellationToken),
            await _dbContext.TransportOrderDocuments.AsNoTracking()
                .CountAsync(d => d.TenantId == TenantId && d.TransportOrderId == orderId, cancellationToken),
            draftLines.Count,
            owningDossier?.Id,
            owningDossier?.DossierNumber);

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
    /// Financial safety: once the order sits on a FINALIZED invoice (Sent or Paid), its customer is
    /// history and must be corrected through a credit note, never rewritten in place. A cancelled
    /// draft is not a finalized document and does not block (A7).
    /// </summary>
    private async Task<string?> BlockingReasonAsync(TransportOrder order, Guid newCustomerId, CancellationToken cancellationToken)
    {
        if (order.CustomerId == newCustomerId)
        {
            return "Deze order staat al op deze klant.";
        }

        // Wave 1 fix A (A7): FINALIZED means Sent or Paid. The predicate used to be "not Draft",
        // which also caught a CANCELLED draft — never sent, impossible to credit (crediting
        // requires Sent/Paid) — so the user was told to correct via a credit note that can never
        // exist. Cancelling a draft leaves its lines in place, which is why the order still looks
        // invoiced here at all.
        var finalizedInvoice = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.TransportOrderId == order.Id)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.TenantId == TenantId),
                line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => invoice)
            .AnyAsync(i => i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid, cancellationToken);
        if (finalizedInvoice)
        {
            return "Deze order staat op een verzonden of geboekte factuur. Corrigeer via een creditnota; "
                   + "de historische factuur blijft ongewijzigd.";
        }

        return null;
    }
}
