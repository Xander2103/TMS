using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierActivityPricingService
{
    /// <summary>
    /// Sets (amount) or clears (null) the agreed sales price of a standalone billable activity.
    /// Returns null when the dossier does not exist in the tenant; throws
    /// <see cref="DossierVersionConflictException"/> on a stale token (409 with current state).
    /// </summary>
    Task<DossierDetailDto?> SetAgreedPriceAsync(
        Guid dossierId, Guid activityId, SetActivityPriceRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// D5: id-preserving replace of the activity's sales lines; the agreed price becomes the lines
    /// total. Empty list + <c>FreeConfirmed</c> = explicitly free, empty list without = unpriced.
    /// Same gates and the same 409 contract as <see cref="SetAgreedPriceAsync"/>.
    /// </summary>
    Task<DossierDetailDto?> SetPriceLinesAsync(
        Guid dossierId, Guid activityId, SetActivityPriceLinesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Step 13 (2026-09-11): the price-only command for a standalone activity — the activity-side
/// twin of <c>TransportOrderService.SetOneOffPriceAsync</c>. Same provenance semantics
/// (null = no price, € 0 = a deliberate price), same lock rule, own concurrency token, audited
/// old → new. The fixed-price command never touches the dossier's own Version: a price edit is
/// not a structural dossier mutation and must not 409 a colleague's open activity dialog.
/// <para>
/// D5 (2026-09-21): a second provenance — sales LINES (<see cref="SetPriceLinesAsync"/>). The
/// two are mutually exclusive per activity and switching is allowed in both directions; the
/// amount always lands once in <see cref="DossierActivityPricing.AgreedPrice"/>.
/// </para>
/// </summary>
public class DossierActivityPricingService : IDossierActivityPricingService
{
    public const string LockedMessage = "De prijs van deze activiteit is vergrendeld.";
    private const string EntityType = "DossierActivityPricing";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IDossierService _dossierService;

    public DossierActivityPricingService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IDossierService dossierService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _dossierService = dossierService;
    }

    public async Task<DossierDetailDto?> SetAgreedPriceAsync(
        Guid dossierId, Guid activityId, SetActivityPriceRequest request, CancellationToken cancellationToken)
    {
        if (await LoadPriceableAsync(dossierId, activityId, "fixedAmount", cancellationToken) is null)
        {
            return null;
        }

        if (request.FixedAmount is < 0m)
        {
            throw new DomainValidationException("fixedAmount", "Geef een geldig bedrag op (0 of meer).");
        }

        var pricing = await FindPricingAsync(activityId, request.Version, dossierId, cancellationToken);

        var before = pricing is null
            ? null
            : new { PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice };

        var created = pricing is null;
        pricing ??= NewPricing(activityId);

        // D5: a fixed price replaces a lines-based one — the lines go (soft delete), so the two
        // provenances can never both claim the amount. The switch shows in the audit entry
        // (PricingSource old → new).
        if (!created)
        {
            _dbContext.RemoveRange(await LoadLinesAsync(pricing.Id, cancellationToken));
        }

        var amount = request.FixedAmount is { } value ? Math.Round(value, 2) : (decimal?)null;
        pricing.PricingSource = amount is null ? ActivityPricingSource.None : ActivityPricingSource.OneOff;
        pricing.FixedAmount = amount;
        // A fixed price has no lines: the agreed amount IS the effective price.
        pricing.AgreedPrice = amount;
        // "Free" is confirmed through the lines command only; a fixed € 0 keeps its pricing.zero check.
        pricing.FreeConfirmed = false;
        pricing.Version = Guid.NewGuid();

        await SaveAsync(pricing, created, dossierId, cancellationToken);

        await _auditService.RecordAsync(EntityType, activityId.ToString(), "agreedPriceSet", before,
            new { PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice, DossierId = dossierId },
            cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> SetPriceLinesAsync(
        Guid dossierId, Guid activityId, SetActivityPriceLinesRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (await LoadPriceableAsync(dossierId, activityId, "lines", cancellationToken) is not { } target)
        {
            return null;
        }

        var inputs = request.Lines ?? [];
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (string.IsNullOrWhiteSpace(input.Label))
            {
                throw new DomainValidationException($"lines[{i}].label", "Een omschrijving is verplicht voor elke verkooplijn.");
            }

            if (input.Label.Trim().Length > 200)
            {
                throw new DomainValidationException($"lines[{i}].label", "De omschrijving mag maximaal 200 tekens bevatten.");
            }

            if (input.Quantity <= 0m)
            {
                throw new DomainValidationException($"lines[{i}].quantity", "Aantal moet groter zijn dan nul.");
            }

            if (input.UnitPrice < 0m)
            {
                throw new DomainValidationException($"lines[{i}].unitPrice", "Geef een geldige eenheidsprijs op (0 of meer).");
            }

            if (input.Unit is { } unit && unit.Trim().Length > 30)
            {
                throw new DomainValidationException($"lines[{i}].unit", "De eenheid mag maximaal 30 tekens bevatten.");
            }
        }

        // Every incoming sales code must be an own-tenant category (fail-closed → 400).
        await _dbContext.SalesCategories.EnsureAllBelongToTenantAsync(
            inputs.Where(l => l.SalesCategoryId is not null).Select(l => l.SalesCategoryId!.Value).ToList(),
            tenantId, "verkoopcategorie", cancellationToken);

        var pricing = await FindPricingAsync(activityId, request.Version, dossierId, cancellationToken);
        var existingLines = pricing is null ? [] : await LoadLinesAsync(pricing.Id, cancellationToken);
        var byId = existingLines.ToDictionary(l => l.Id);

        // Id-preserving replace: an id must be a line of THIS price record — a guessed id of
        // another activity/dossier/tenant is refused, never silently turned into a new line.
        var incomingIds = inputs.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToList();
        if (incomingIds.Count != incomingIds.Distinct().Count() || incomingIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new DomainValidationException(
                "lines", "Een van de verkooplijnen hoort niet bij deze activiteit. Laad het dossier opnieuw.");
        }

        var before = pricing is null
            ? null
            : new
            {
                PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice, pricing.FreeConfirmed,
                LineCount = existingLines.Count,
            };

        var created = pricing is null;
        pricing ??= NewPricing(activityId);

        var seen = new HashSet<Guid>();
        var sequence = 0;
        var total = 0m;
        foreach (var input in inputs)
        {
            DossierActivityPriceLine line;
            if (input.Id is { } lineId)
            {
                line = byId[lineId];
                seen.Add(lineId);
            }
            else
            {
                line = new DossierActivityPriceLine { Id = Guid.NewGuid(), TenantId = tenantId, DossierActivityPricingId = pricing.Id };
                _dbContext.Add(line);
            }

            line.Sequence = ++sequence;
            line.Label = input.Label!.Trim();
            line.Quantity = input.Quantity;
            line.Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim();
            line.UnitPrice = input.UnitPrice;
            // The server is the only calculator: a client-sent amount is never read.
            line.Amount = Math.Round(input.Quantity * input.UnitPrice, 2);
            line.SalesCategoryId = input.SalesCategoryId;
            total += line.Amount;
        }

        _dbContext.RemoveRange(existingLines.Where(l => !seen.Contains(l.Id)));

        // Provenance, never magnitude: lines ⇒ priced at their total (a € 0 total included);
        // no lines + explicit confirmation ⇒ free (priced at 0); no lines without ⇒ NOT priced
        // (null — a missing price is never stored as 0).
        var priced = inputs.Count > 0 || request.FreeConfirmed;
        pricing.PricingSource = priced ? ActivityPricingSource.Lines : ActivityPricingSource.None;
        pricing.FixedAmount = null;
        pricing.AgreedPrice = priced ? Math.Round(total, 2) : null;
        pricing.FreeConfirmed = priced && request.FreeConfirmed && pricing.AgreedPrice == 0m;
        pricing.Version = Guid.NewGuid();

        // Unlike the fixed-price command this one replaces a child LIST of the dossier, so it
        // bumps the dossier token the way the other activity mutations do (the saver receives
        // the fresh token in the returned detail). The GATE stays the price record's own token.
        target.Dossier.Version = Guid.NewGuid();

        await SaveAsync(pricing, created, dossierId, cancellationToken);

        await _auditService.RecordAsync(EntityType, activityId.ToString(), "priceLinesSet", before,
            new
            {
                PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice, pricing.FreeConfirmed,
                LineCount = inputs.Count, DossierId = dossierId,
            },
            cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    /// <summary>
    /// The gates every price command shares: the dossier exists (else null → 404) and is open, the
    /// activity belongs to it, is standalone (transport is priced through its order) and billable.
    /// </summary>
    private async Task<(TransportDossier Dossier, DossierActivity Activity)?> LoadPriceableAsync(
        Guid dossierId, Guid activityId, string field, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await _dbContext.TransportDossiers
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden bewerkt. Heropen het dossier eerst.");
        }

        var activity = await _dbContext.DossierActivities
            .Include(a => a.ActivityType)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken);
        if (activity?.ActivityType is null)
        {
            throw new DomainValidationException("Deze activiteit bestaat niet.");
        }

        if (activity.ActivityType.HasStops)
        {
            throw new DomainValidationException(field, "Transportactiviteiten worden via hun transportopdracht geprijsd.");
        }

        if (!activity.ActivityType.IsBillable)
        {
            throw new DomainValidationException(field, "Dit activiteitstype is niet factureerbaar en krijgt geen verkoopprijs.");
        }

        return (dossier, activity);
    }

    /// <summary>The activity's price record after the version gate and the lock rule (null = none yet).</summary>
    private async Task<DossierActivityPricing?> FindPricingAsync(
        Guid activityId, Guid? expectedVersion, Guid dossierId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var pricing = await _dbContext.DossierActivityPricings
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.DossierActivityId == activityId, cancellationToken);

        // Version gate on the RECORD (like the order's own token): null is accepted only while no
        // record exists yet (first save) or from legacy callers; a stale token rebases the client.
        if (pricing is not null && expectedVersion is { } expected && expected != pricing.Version)
        {
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }

        if (pricing is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            throw new DomainValidationException(LockedMessage);
        }

        return pricing;
    }

    private DossierActivityPricing NewPricing(Guid activityId)
    {
        var pricing = new DossierActivityPricing
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            DossierActivityId = activityId,
        };
        _dbContext.Add(pricing);
        return pricing;
    }

    private Task<List<DossierActivityPriceLine>> LoadLinesAsync(Guid pricingId, CancellationToken cancellationToken) =>
        _dbContext.DossierActivityPriceLines
            .Where(l => l.TenantId == _tenantContext.TenantId && l.DossierActivityPricingId == pricingId)
            .OrderBy(l => l.Sequence)
            .ToListAsync(cancellationToken);

    private async Task SaveAsync(DossierActivityPricing pricing, bool created, Guid dossierId, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (created)
        {
            // Two browsers saved a FIRST price at the same time: the unique index let one through.
            // The loser gets the same 409 + current state as a stale token, never a silent overwrite.
            foreach (var entry in _dbContext.ChangeTracker.Entries<DossierActivityPriceLine>()
                         .Where(e => e.Entity.DossierActivityPricingId == pricing.Id).ToList())
            {
                entry.State = EntityState.Detached;
            }

            _dbContext.Entry(pricing).State = EntityState.Detached;
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }
    }
}
