using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Money;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

/// <summary>
/// Accounting remediation (ACC-B1/H1/H6/M4): the ONE set of rules that decides whether a draft
/// may be issued. The Draft → Sent transition enforces them and <c>GET /api/invoices/{id}/issue-preflight</c>
/// shows them beforehand, so the accountant never discovers a problem after the number is gone.
/// Every issue names the lines it concerns and what to do about it.
/// </summary>
public static class InvoiceIssuePreflight
{
    public const string LinesEmpty = "lines.empty";
    public const string EntityMissing = "entity.missing";
    public const string EntityMismatch = "entity.mismatch";
    public const string PoRequired = "po.required";
    public const string CustomerVatRequired = "customer.vat_required";
    public const string VatTreatmentMissing = "vat.treatment_missing";
    public const string UnpricedSource = "lines.unpriced_source";
    public const string ZeroAmount = "lines.zero_unconfirmed";
    public const string VatMismatch = "lines.vat_mismatch";
    public const string CategoryMissing = "lines.category_missing";
    public const string LedgerMissing = "lines.ledger_missing";
    public const string QuantityInvalid = "lines.quantity";
    public const string NegativePrice = "lines.negative_price";

    public sealed record Issue(string Code, string Message, IReadOnlyList<Guid> LineIds)
    {
        public InvoiceIssueIssueDto ToDto() => new(Code, Message, LineIds);
    }

    /// <summary>
    /// Evaluates a Draft invoice against today's master data. Sent/Paid/Cancelled documents
    /// return a single state issue (nothing to issue).
    /// </summary>
    public static async Task<IReadOnlyList<Issue>> EvaluateAsync(
        TransportationDbContext db, Guid tenantId, Invoice invoice, CancellationToken cancellationToken)
    {
        var issues = new List<Issue>();
        void Add(string code, string message, IEnumerable<InvoiceLine>? lines = null) =>
            issues.Add(new Issue(code, message, lines?.Select(l => l.Id).ToList() ?? []));

        var lines = invoice.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Sequence).ToList();
        if (lines.Count == 0)
        {
            Add(LinesEmpty, "Een factuur zonder lijnen kan niet verzonden worden.");
        }

        // Issuing entity: valid, active, same tenant; the only exemption is a tenant without any entity.
        var entityValid = invoice.LegalEntityId is { } entityId
            ? await db.LegalEntities.AnyAsync(e => e.TenantId == tenantId && e.Id == entityId && e.IsActive, cancellationToken)
            : !await db.LegalEntities.AnyAsync(e => e.TenantId == tenantId, cancellationToken);
        if (!entityValid)
        {
            Add(EntityMissing, "Deze factuur heeft geen geldige facturerende entiteit en kan niet worden verzonden.");
        }

        var lineOrderIds = lines.Where(l => l.TransportOrderId is not null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var orders = lineOrderIds.Count == 0
            ? new Dictionary<Guid, OrderFacts>()
            : (await db.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && lineOrderIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id, o.OrderNumber, o.LegalEntityId, o.AgreedPrice, o.InvoiceReadiness, o.Status,
                    // ACC-M1: explicitly free = the flag, or the pre-existing intentional-zero shapes (one-off 0 / override 0).
                    Free = o.FreeConfirmed || (o.PricingSource == OrderPricingSource.OneOff && o.OneOffFixedAmount == 0m) || (o.PriceIsManual && o.AgreedPrice == 0m),
                    o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount,
                })
                .ToListAsync(cancellationToken))
                .ToDictionary(o => o.Id, o => new OrderFacts(o.Id, o.OrderNumber, o.LegalEntityId, o.AgreedPrice, o.Free, o.InvoiceReadiness, o.Status,
                    // Resolved = ready for invoicing, or explicitly free. A completed order whose readiness
                    // projection was never evaluated ("NotReady" never describes a completed order) is
                    // judged by its pricing provenance instead — the same rule the invoice builder applies.
                    Resolved: o.InvoiceReadiness == InvoiceReadinessEvaluator.ReadyForInvoice || o.Free
                              || (o.InvoiceReadiness == InvoiceReadinessEvaluator.NotReady
                                  && OrderPricingState.IsPriced(o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.AgreedPrice))));

        var mismatched = orders.Values
            .Where(o => o.LegalEntityId != null && o.LegalEntityId != invoice.LegalEntityId)
            .Select(o => o.OrderNumber).ToList();
        if (mismatched.Count > 0)
        {
            Add(EntityMismatch,
                $"De facturerende entiteit wijkt af van die van opdracht(en) {string.Join(", ", mismatched)}. Corrigeer de factuur of de opdrachten voor verzending.",
                lines.Where(l => l.TransportOrderId is { } oid && orders.TryGetValue(oid, out var o) && o.LegalEntityId != null && o.LegalEntityId != invoice.LegalEntityId));
        }

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == invoice.CustomerId)
            .Select(c => new { c.PurchaseOrderPolicy, c.VatTreatment, c.VatNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer?.PurchaseOrderPolicy == PurchaseOrderPolicy.Required && string.IsNullOrWhiteSpace(invoice.PurchaseOrderNumber))
        {
            Add(PoRequired, "Deze klant vereist een PO-nummer. Voeg een geldig PO-nummer toe voor verzending.");
        }

        var treatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed) ? (VatTreatment?)parsed : null;
        if (treatment is null)
        {
            Add(VatTreatmentMissing, "De btw-regeling van de klant ontbreekt op deze factuur; bewerk en bewaar de conceptfactuur eerst opnieuw.");
        }
        else if (VatTreatmentCatalog.Resolve(treatment.Value).RequiresVatNumber
                 && string.IsNullOrWhiteSpace(invoice.CustomerVatNumberSnapshot ?? customer?.VatNumber))
        {
            Add(CustomerVatRequired, "Deze btw-regeling vereist een BTW-nummer van de klant. Vul het BTW-nummer aan voor verzending.");
        }

        // --- ACC-B1: a source-backed line must represent a RESOLVED commercial decision. ---
        // An order is resolved when it is ready for invoicing (priced, or explicitly free); an
        // activity when its price is set (or confirmed free). Anything else is "Nakijken" and
        // can never leave the building as € 0,00.
        var unpricedOrderLines = lines
            .Where(l => l.TransportOrderId is { } oid && orders.TryGetValue(oid, out var o)
                        && !o.Resolved
                        // An already-issued line (re-run on a Sent document) keeps its facts; only drafts are judged.
                        && invoice.Status == InvoiceStatus.Draft)
            .ToList();
        if (unpricedOrderLines.Count > 0)
        {
            var numbers = unpricedOrderLines.Select(l => orders[l.TransportOrderId!.Value].OrderNumber).Distinct();
            Add(UnpricedSource,
                $"Opdracht(en) {string.Join(", ", numbers)} zijn niet factureerbaar (geen afgewerkte prijs: 'Nakijken'). Prijs de opdracht of markeer ze expliciet als gratis; niet-geprijsd werk wordt nooit als € 0,00 gefactureerd.",
                unpricedOrderLines);
        }

        var lineActivityIds = lines.Where(l => l.DossierActivityId is not null).Select(l => l.DossierActivityId!.Value).Distinct().ToList();
        if (lineActivityIds.Count > 0 && invoice.Status == InvoiceStatus.Draft)
        {
            var pricings = await db.DossierActivityPricings.AsNoTracking()
                .Where(p => p.TenantId == tenantId && lineActivityIds.Contains(p.DossierActivityId))
                .ToDictionaryAsync(p => p.DossierActivityId, cancellationToken);
            var unpricedActivityLines = lines
                .Where(l => l.DossierActivityId is { } aid
                            && (!pricings.TryGetValue(aid, out var p) || !ActivityPricingState.IsPriced(p) || ActivityPricingState.IsUnconfirmedZero(p)))
                .ToList();
            if (unpricedActivityLines.Count > 0)
            {
                Add(UnpricedSource,
                    "Een of meer activiteitslijnen horen bij een activiteit zonder afgewerkte verkoopprijs ('Nakijken'). Prijs de activiteit of bevestig ze als gratis.",
                    unpricedActivityLines);
            }
        }

        // A € 0 source line is only legitimate when the source is EXPLICITLY free.
        var zeroSourceLines = await ZeroSourceLinesAsync(db, tenantId, invoice, lines, orders, cancellationToken);
        if (zeroSourceLines.Count > 0)
        {
            Add(ZeroAmount,
                "Lijn(en) " + string.Join(", ", zeroSourceLines.Select(l => l.Sequence)) + " hebben een bedrag van € 0,00 zonder dat de bron expliciet gratis is. Een ontbrekende prijs wordt nooit stilzwijgend € 0.",
                zeroSourceLines);
        }

        // --- Quantities and prices ---
        var badQuantity = lines.Where(l => l.Quantity <= 0).ToList();
        if (badQuantity.Count > 0)
        {
            Add(QuantityInvalid, "Lijn(en) " + Sequences(badQuantity) + ": de hoeveelheid moet groter zijn dan nul.", badQuantity);
        }

        var negative = lines.Where(l => l.UnitPrice < 0).ToList();
        if (negative.Count > 0)
        {
            Add(NegativePrice,
                "Lijn(en) " + Sequences(negative) + " hebben een negatieve prijs. Een korting of correctie is een creditnota, geen negatieve factuurlijn.",
                negative);
        }

        // --- ACC-H1: every line's VAT must agree with the invoice's treatment. ---
        if (treatment is { } t)
        {
            var categoryIds = lines.Where(l => l.SalesCategoryId is not null).Select(l => l.SalesCategoryId!.Value).Distinct().ToList();
            var categories = categoryIds.Count == 0
                ? new Dictionary<Guid, Modules.Accounting.Entities.SalesCategory>()
                : await db.SalesCategories.AsNoTracking()
                    .Include(c => c.LedgerMappings)
                    .Include(c => c.LedgerAccount)
                    .Where(c => c.TenantId == tenantId && categoryIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, cancellationToken);
            var info = VatTreatmentCatalog.Resolve(t);
            // A MIRRORED credit-note line is a frozen copy of the credited document (H-06): its VAT,
            // category and ledger are the credited invoice's truth and are never re-judged here.
            var ownLines = lines.Where(l => !InvoiceLineMirror.IsMirrored(invoice, l)).ToList();
            var mismatches = new List<(InvoiceLine Line, string Why)>();
            foreach (var line in ownLines)
            {
                var category = line.SalesCategoryId is { } cid ? categories.GetValueOrDefault(cid) : null;
                var lineOverride = Enum.TryParse<VatTreatment>(line.VatTreatmentOverride, out var o) ? (VatTreatment?)o : null;
                var resolution = Modules.Accounting.Services.InvoiceLineFiscalResolver.Resolve(
                    lineOverride, category, t, line.VatRatePercent, line.VatRatePercent);
                var resolvedInfo = VatTreatmentCatalog.Resolve(resolution.Treatment);
                if (resolvedInfo.DefaultRatePercent is { } fixedRate)
                {
                    // Statutory 0% regimes: a charged rate contradicts the exemption the line declares.
                    if (line.VatRatePercent != fixedRate)
                    {
                        mismatches.Add((line, $"{resolvedInfo.Label} vereist {fixedRate:0.##}%, de lijn heeft {line.VatRatePercent:0.##}%"));
                    }
                }
                else if (resolution.Treatment == VatTreatment.DomesticVat)
                {
                    // The exact ACC-H1 defect: 0% under domestic VAT without any exemption. The
                    // catalogue derives "Z" for ANY domestic 0% line, so the exemption must be
                    // declared on the sales code itself (a zero-rated or exempt category override).
                    var declaredExemption = category?.VatCategoryOverride?.Trim().ToUpperInvariant() is "Z" or "E";
                    if (line.VatRatePercent == 0m && !declaredExemption)
                    {
                        mismatches.Add((line, "0% btw onder de binnenlandse btw-regeling zonder vrijstelling"));
                    }
                    else if (!info.AllowsCustomRate && !info.StandardRates.Contains(line.VatRatePercent))
                    {
                        mismatches.Add((line, $"{line.VatRatePercent:0.##}% is geen geldig binnenlands tarief ({string.Join("/", info.StandardRates.Select(r => r.ToString("0.##")))}%)"));
                    }
                }
            }

            if (mismatches.Count > 0)
            {
                Add(VatMismatch,
                    "Btw-tarief klopt niet met de btw-regeling: " + string.Join("; ", mismatches.Select(m => $"lijn {m.Line.Sequence}: {m.Why}")) + ". Corrigeer het tarief of de verkoopcategorie.",
                    mismatches.Select(m => m.Line));
            }

            // --- ACC-H6: accounting classification must be complete BEFORE issue. ---
            // A line that already carries a frozen ledger account (a mirrored credit-note line, or a
            // legacy line classified before categories existed) is booked and exported as-is.
            var noCategory = ownLines.Where(l => l.SalesCategoryId is null && l.LedgerAccountNumberSnapshot is null).ToList();
            if (noCategory.Count > 0)
            {
                Add(CategoryMissing,
                    "Lijn(en) " + Sequences(noCategory) + " hebben geen verkoopcategorie. Kies er een per lijn (Bewerken) zodat de factuur geboekt en geëxporteerd kan worden.",
                    noCategory);
            }

            // The ledger account is demanded as soon as the tenant has configured accounting at all
            // (any ledger account on file); a tenant that never set up its chart of accounts keeps
            // issuing, and the accounting export's own guard still refuses the document until it is.
            var accountingConfigured = await db.LedgerAccounts.AnyAsync(a => a.TenantId == tenantId && a.IsActive, cancellationToken);
            var noLedger = accountingConfigured
                ? ownLines
                    .Where(l => l.SalesCategoryId is { } cid && categories.TryGetValue(cid, out var c)
                                && Modules.Accounting.Services.InvoiceLineFiscalResolver.LedgerFor(c, invoice.LegalEntityId).LedgerAccountId is null
                                && l.LedgerAccountNumberSnapshot is null)
                    .ToList()
                : [];
            if (noLedger.Count > 0)
            {
                var names = noLedger.Select(l => categories[l.SalesCategoryId!.Value].Name).Distinct();
                Add(LedgerMissing,
                    $"Geen grootboekrekening gekoppeld aan verkoopcategorie {string.Join(", ", names.Select(n => $"'{n}'"))}. Koppel de rekening bij Instellingen → Boekhouding voor je verzendt.",
                    noLedger);
            }
        }

        return issues;
    }

    public static InvoiceIssuePreflightDto ToDto(IReadOnlyList<Issue> issues) =>
        new(issues.Count == 0, issues.Select(i => i.ToDto()).ToList());

    /// <summary>One sentence for a refused transition: every reason, in order.</summary>
    public static string Summarize(IReadOnlyList<Issue> issues) =>
        string.Join(" ", issues.Select(i => i.Message));

    private static string Sequences(IEnumerable<InvoiceLine> lines) => string.Join(", ", lines.Select(l => l.Sequence));

    private sealed record OrderFacts(
        Guid Id, string OrderNumber, Guid? LegalEntityId, decimal? AgreedPrice, bool FreeConfirmed, string InvoiceReadiness, TransportOrderStatus Status,
        bool Resolved);

    private static async Task<bool> IsFreeActivityAsync(TransportationDbContext db, Guid tenantId, Guid activityId, CancellationToken cancellationToken)
    {
        var pricing = await db.DossierActivityPricings.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.DossierActivityId == activityId, cancellationToken);
        return pricing is not null && ActivityPricingState.IsFree(pricing);
    }

    private static async Task<List<InvoiceLine>> ZeroSourceLinesAsync(
        TransportationDbContext db, Guid tenantId, Invoice invoice, List<InvoiceLine> lines,
        IReadOnlyDictionary<Guid, OrderFacts> orders, CancellationToken cancellationToken)
    {
        var result = new List<InvoiceLine>();
        if (invoice.Status != InvoiceStatus.Draft)
        {
            return result;
        }

        foreach (var line in lines.Where(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice) == 0m))
        {
            if (line.TransportOrderId is { } oid && orders.TryGetValue(oid, out var order))
            {
                // Service/diesel lines of a priced order may legitimately be € 0; the base line of an
                // unpriced order is the case to catch (no agreed price and not free).
                if (order.AgreedPrice is null && !order.FreeConfirmed)
                {
                    result.Add(line);
                }
            }
            else if (line.DossierActivityId is { } aid && !await IsFreeActivityAsync(db, tenantId, aid, cancellationToken))
            {
                result.Add(line);
            }
        }

        return result;
    }
}
