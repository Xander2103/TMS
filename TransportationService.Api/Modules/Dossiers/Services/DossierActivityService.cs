using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierActivityService
{
    Task<DossierDetailDto?> AddAsync(Guid dossierId, SaveDossierActivityRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> UpdateAsync(Guid dossierId, Guid activityId, SaveDossierActivityRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> DeleteAsync(Guid dossierId, Guid activityId, Guid? version, CancellationToken cancellationToken);

    Task<DossierDetailDto?> ReorderAsync(Guid dossierId, ReorderDossierActivitiesRequest request, CancellationToken cancellationToken);

    /// <summary>Creates the linked draft order for an existing order-less transport activity.</summary>
    Task<DossierDetailDto?> CreateOrderForActivityAsync(Guid dossierId, Guid activityId, Guid? version, CancellationToken cancellationToken);
}

/// <summary>
/// Operational activities inside a dossier. Transport-shaped activities (type.HasStops)
/// execute through a linked TransportOrder — created here on demand and always linked into
/// the dossier (DossierOrder) so the existing financial rollup and list UIs keep working.
/// The activity layer itself stores no operational payload (see DossierActivity remarks).
/// </summary>
public class DossierActivityService : IDossierActivityService
{
    private const string EntityType = "TransportDossier";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IDossierService _dossierService;
    private readonly ITransportOrderService _orderService;
    private readonly TimeProvider _timeProvider;

    public DossierActivityService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IDossierService dossierService,
        ITransportOrderService orderService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _dossierService = dossierService;
        _orderService = orderService;
        _timeProvider = timeProvider;
    }

    public async Task<DossierDetailDto?> AddAsync(
        Guid dossierId, SaveDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindOpenDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var type = await _dbContext.ActivityTypes
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == request.ActivityTypeId && t.IsActive, cancellationToken);
        if (type is null)
        {
            throw new DomainValidationException("activityTypeId", "Het gekozen activiteitstype bestaat niet of is inactief.");
        }

        ValidateScalars(request, type);
        await ValidateAccompanimentAsync(dossierId, request.LinkedActivityId, activityId: null, cancellationToken);

        var maxSequence = await _dbContext.DossierActivities
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId)
            .MaxAsync(a => (int?)a.Sequence, cancellationToken) ?? 0;

        var activity = new DossierActivity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DossierId = dossierId,
            ActivityTypeId = type.Id,
            Sequence = maxSequence + 1,
            Label = Trim(request.Label),
            PlannedDate = request.PlannedDate,
            DurationHours = request.DurationHours,
            LinkedActivityId = request.LinkedActivityId,
            Notes = Trim(request.Notes),
        };

        if (request.CreateLinkedOrder)
        {
            if (!type.HasStops)
            {
                throw new DomainValidationException(
                    "createLinkedOrder", "Alleen transportactiviteiten krijgen een transportopdracht.");
            }

            activity.LinkedTransportOrderId = await CreateDraftOrderAsync(dossier, Trim(request.Label), type.Name, cancellationToken);
        }

        _dbContext.Add(activity);
        dossier.Version = Guid.NewGuid(); // marks the dossier Modified; interceptor stamps the token
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityAdded", null,
            new { ActivityType = type.Code, activity.Label, activity.LinkedTransportOrderId }, cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> UpdateAsync(
        Guid dossierId, Guid activityId, SaveDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindOpenDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var activity = await _dbContext.DossierActivities
            .Include(a => a.ActivityType)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken);
        if (activity is null)
        {
            throw new DomainValidationException("Deze activiteit bestaat niet.");
        }

        if (request.ActivityTypeId != activity.ActivityTypeId)
        {
            throw new DomainValidationException(
                "activityTypeId", "Het type van een activiteit kan niet gewijzigd worden; verwijder de activiteit en voeg een nieuwe toe.");
        }

        ValidateScalars(request, activity.ActivityType!);
        await ValidateAccompanimentAsync(dossierId, request.LinkedActivityId, activityId, cancellationToken);

        activity.Label = Trim(request.Label);
        activity.PlannedDate = request.PlannedDate;
        activity.DurationHours = request.DurationHours;
        activity.LinkedActivityId = request.LinkedActivityId;
        activity.Notes = Trim(request.Notes);
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityUpdated", null,
            new { activity.Id, activity.Label, activity.PlannedDate, activity.DurationHours }, cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> DeleteAsync(
        Guid dossierId, Guid activityId, Guid? version, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindOpenDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        await RequireVersionAsync(dossier, version, cancellationToken);

        var activity = await _dbContext.DossierActivities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken);
        if (activity is null)
        {
            throw new DomainValidationException("Deze activiteit bestaat niet.");
        }

        if (activity.LinkedTransportOrderId is { } orderId)
        {
            var orderStatus = await _dbContext.TransportOrders
                .Where(o => o.TenantId == tenantId && o.Id == orderId)
                .Select(o => (TransportOrderStatus?)o.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (orderStatus is not (null or TransportOrderStatus.Draft or TransportOrderStatus.Cancelled))
            {
                throw new DomainValidationException(
                    "Deze activiteit heeft een actieve transportopdracht en kan niet verwijderd worden. Annuleer eerst de opdracht.");
            }
        }

        // Accompaniment links pointing here are released first (Restrict FK; the plateau
        // simply loses its operational anchor, it is not deleted).
        var dependents = await _dbContext.DossierActivities
            .Where(a => a.TenantId == tenantId && a.LinkedActivityId == activityId)
            .ToListAsync(cancellationToken);
        foreach (var dependent in dependents)
        {
            dependent.LinkedActivityId = null;
        }

        _dbContext.Remove(activity);
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityRemoved", null,
            new { activity.Id, activity.Label, activity.LinkedTransportOrderId }, cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> ReorderAsync(
        Guid dossierId, ReorderDossierActivitiesRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindOpenDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var activities = await _dbContext.DossierActivities
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId)
            .ToListAsync(cancellationToken);
        if (request.ActivityIds.Count != activities.Count
            || activities.Any(a => !request.ActivityIds.Contains(a.Id)))
        {
            throw new DomainValidationException(
                "activityIds", "De volgorde moet exact alle activiteiten van het dossier bevatten.");
        }

        var byId = activities.ToDictionary(a => a.Id);
        for (var index = 0; index < request.ActivityIds.Count; index++)
        {
            byId[request.ActivityIds[index]].Sequence = index + 1;
        }

        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> CreateOrderForActivityAsync(
        Guid dossierId, Guid activityId, Guid? version, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindOpenDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        await RequireVersionAsync(dossier, version, cancellationToken);

        var activity = await _dbContext.DossierActivities
            .Include(a => a.ActivityType)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken);
        if (activity is null)
        {
            throw new DomainValidationException("Deze activiteit bestaat niet.");
        }

        if (activity.ActivityType?.HasStops != true)
        {
            throw new DomainValidationException("Alleen transportactiviteiten krijgen een transportopdracht.");
        }

        if (activity.LinkedTransportOrderId is not null)
        {
            throw new DomainValidationException("Deze activiteit heeft al een opdracht.");
        }

        activity.LinkedTransportOrderId = await CreateDraftOrderAsync(
            dossier, activity.Label, activity.ActivityType.Name, cancellationToken);
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityOrderCreated", null,
            new { activity.Id, activity.Label, activity.LinkedTransportOrderId }, cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    /// <summary>
    /// Minimal draft order inside THIS dossier (so no wrapper is auto-created). The activity
    /// label / type name doubles as the goods description, which satisfies the order's
    /// minimal-cargo rule until real goods are entered.
    /// </summary>
    private async Task<Guid> CreateDraftOrderAsync(
        TransportDossier dossier, string? label, string typeName, CancellationToken cancellationToken)
    {
        if (dossier.CustomerId is not { } customerId)
        {
            throw new DomainValidationException(
                "createLinkedOrder", "Koppel eerst een klant aan het dossier voor je een opdracht aanmaakt.");
        }

        var orderResult = await _orderService.CreateAsync(new CreateTransportOrderRequest(
            CustomerId: customerId,
            CustomerReference: dossier.CustomerReference,
            OrderDate: dossier.DossierDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
            GoodsDescription: label ?? typeName,
            Quantity: null, QuantityUnit: null, WeightKg: null, VolumeM3: null, PalletCount: null,
            AdrRequired: false, CraneRequired: false, AgreedPrice: null, Notes: null,
            Stops: [],
            LegalEntityId: dossier.LegalEntityId,
            DossierId: dossier.Id), cancellationToken);
        if (orderResult.Outcome != TransportOrderOperationOutcome.Success)
        {
            throw new DomainValidationException(orderResult.Error ?? "De transportopdracht kon niet aangemaakt worden.");
        }

        return orderResult.Order!.Id;
    }

    private static void ValidateScalars(SaveDossierActivityRequest request, ActivityType type)
    {
        if (request.DurationHours is { } duration)
        {
            if (!type.AllowsDuration)
            {
                throw new DomainValidationException("durationHours", "Dit activiteitstype ondersteunt geen duur.");
            }

            if (duration < 0)
            {
                throw new DomainValidationException("durationHours", "De duur kan niet negatief zijn.");
            }
        }

        if (request.Label is { Length: > 200 })
        {
            throw new DomainValidationException("label", "Het label mag maximaal 200 tekens lang zijn.");
        }
    }

    private async Task ValidateAccompanimentAsync(
        Guid dossierId, Guid? linkedActivityId, Guid? activityId, CancellationToken cancellationToken)
    {
        if (linkedActivityId is not { } linkedId)
        {
            return;
        }

        if (linkedId == activityId)
        {
            throw new DomainValidationException("linkedActivityId", "Een activiteit kan niet aan zichzelf gekoppeld worden.");
        }

        var inSameDossier = await _dbContext.DossierActivities.AnyAsync(
            a => a.TenantId == _tenantContext.TenantId && a.DossierId == dossierId && a.Id == linkedId, cancellationToken);
        if (!inSameDossier)
        {
            throw new DomainValidationException(
                "linkedActivityId", "De gekoppelde activiteit moet in hetzelfde dossier zitten.");
        }
    }

    private async Task<TransportDossier?> FindOpenDossierAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var dossier = await _dbContext.TransportDossiers
            .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden bewerkt. Heropen het dossier eerst.");
        }

        return dossier;
    }

    private async Task RequireVersionAsync(TransportDossier dossier, Guid? version, CancellationToken cancellationToken)
    {
        if (version is { } expected && expected != dossier.Version)
        {
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossier.Id, cancellationToken))!);
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
