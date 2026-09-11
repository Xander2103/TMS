using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
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
}

/// <summary>
/// Step 13 (2026-09-11): the price-only command for a standalone activity — the activity-side
/// twin of <c>TransportOrderService.SetOneOffPriceAsync</c>. Same provenance semantics
/// (null = no price, € 0 = a deliberate price), same lock rule, own concurrency token, audited
/// old → new. It never touches the dossier's own Version: a price edit is not a structural
/// dossier mutation and must not 409 a colleague's open activity dialog.
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
            throw new DomainValidationException("fixedAmount", "Transportactiviteiten worden via hun transportopdracht geprijsd.");
        }

        if (!activity.ActivityType.IsBillable)
        {
            throw new DomainValidationException("fixedAmount", "Dit activiteitstype is niet factureerbaar en krijgt geen verkoopprijs.");
        }

        if (request.FixedAmount is < 0m)
        {
            throw new DomainValidationException("fixedAmount", "Geef een geldig bedrag op (0 of meer).");
        }

        var pricing = await _dbContext.DossierActivityPricings
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.DossierActivityId == activityId, cancellationToken);

        // Version gate on the RECORD (like the order's own token): null is accepted only while no
        // record exists yet (first save) or from legacy callers; a stale token rebases the client.
        if (pricing is not null && request.Version is { } expected && expected != pricing.Version)
        {
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }

        if (pricing is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            throw new DomainValidationException(LockedMessage);
        }

        var before = pricing is null
            ? null
            : new { PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice };

        var created = pricing is null;
        if (pricing is null)
        {
            pricing = new DossierActivityPricing
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DossierActivityId = activityId,
            };
            _dbContext.Add(pricing);
        }

        var amount = request.FixedAmount is { } value ? Math.Round(value, 2) : (decimal?)null;
        pricing.PricingSource = amount is null ? ActivityPricingSource.None : ActivityPricingSource.OneOff;
        pricing.FixedAmount = amount;
        // No lines exist for standalone activities (yet): the agreed amount IS the effective price.
        pricing.AgreedPrice = amount;
        pricing.Version = Guid.NewGuid();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (created)
        {
            // Two browsers saved a FIRST price at the same time: the unique index let one through.
            // The loser gets the same 409 + current state as a stale token, never a silent overwrite.
            _dbContext.Entry(pricing).State = EntityState.Detached;
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }

        await _auditService.RecordAsync(EntityType, activityId.ToString(), "agreedPriceSet", before,
            new { PricingSource = pricing.PricingSource.ToString(), pricing.FixedAmount, pricing.AgreedPrice, DossierId = dossierId },
            cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }
}
