using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

public class TransportOrderService : ITransportOrderService
{
    private const string EntityType = "TransportOrder";

    /// <summary>
    /// Allowed workflow transitions. Planned is entered via the planning engine (Phase 6).
    /// Cancelled is deliberately absent: cancelling is a separate action (CancelAsync) with
    /// its own permission and a mandatory reason.
    /// </summary>
    private static readonly IReadOnlyDictionary<TransportOrderStatus, TransportOrderStatus[]> Transitions =
        new Dictionary<TransportOrderStatus, TransportOrderStatus[]>
        {
            [TransportOrderStatus.Draft] = [TransportOrderStatus.Confirmed],
            // Portal submissions: the planner accepts (Confirmed) or takes over for corrections (Draft).
            [TransportOrderStatus.Submitted] = [TransportOrderStatus.Confirmed, TransportOrderStatus.Draft],
            [TransportOrderStatus.Confirmed] = [TransportOrderStatus.Draft, TransportOrderStatus.InProgress],
            [TransportOrderStatus.Planned] = [TransportOrderStatus.InProgress],
            [TransportOrderStatus.InProgress] = [TransportOrderStatus.Completed],
            [TransportOrderStatus.Completed] = [],
            [TransportOrderStatus.Cancelled] = [],
        };

    /// <summary>Statuses from which an order can still be cancelled.</summary>
    private static readonly TransportOrderStatus[] CancellableStatuses =
        [TransportOrderStatus.Draft, TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned, TransportOrderStatus.InProgress];

    /// <summary>
    /// Controlled CORRECTIVE (backward) transitions for fixing an accidentally selected status.
    /// Guarded by orders.correct_status, a mandatory reason and an immutable history row.
    /// Invoiced is deliberately absent: once invoiced, a correction would have to unwind
    /// financial documents — that requires the invoicing flow, not a status rollback.
    /// </summary>
    private static readonly IReadOnlyDictionary<TransportOrderStatus, TransportOrderStatus[]> CorrectiveTransitions =
        new Dictionary<TransportOrderStatus, TransportOrderStatus[]>
        {
            [TransportOrderStatus.Confirmed] = [TransportOrderStatus.Draft],
            [TransportOrderStatus.Planned] = [TransportOrderStatus.Confirmed],
            [TransportOrderStatus.InProgress] = [TransportOrderStatus.Confirmed],
            [TransportOrderStatus.Completed] = [TransportOrderStatus.InProgress],
            [TransportOrderStatus.Cancelled] = [TransportOrderStatus.Draft],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IPricingEngine? _pricingEngine;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IPermissionAuthorizationService? _permissionService;

    public TransportOrderService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IPricingEngine? pricingEngine = null,
        ICurrentUserContext? currentUser = null,
        IPermissionAuthorizationService? permissionService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _pricingEngine = pricingEngine;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    private IQueryable<TransportOrder> TenantScoped() =>
        _dbContext.TransportOrders.Where(o => o.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<TransportOrderListItemDto>> SearchAsync(
        string? search, TransportOrderStatus? status, Guid? customerId,
        DateOnly? fromDate, DateOnly? toDate, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(o => o.Status == s);
        if (customerId is { } c) query = query.Where(o => o.CustomerId == c);
        if (fromDate is { } from) query = query.Where(o => o.OrderDate >= from);
        if (toDate is { } to) query = query.Where(o => o.OrderDate <= to);

        var joined = from o in query
                     join cu in _dbContext.Customers.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId)
                         on o.CustomerId equals cu.Id
                     select new { o, CustomerName = cu.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            joined = joined.Where(x =>
                x.o.OrderNumber.ToLower().Contains(term) ||
                (x.o.CustomerReference != null && x.o.CustomerReference.ToLower().Contains(term)) ||
                x.CustomerName.ToLower().Contains(term) ||
                (x.o.GoodsDescription != null && x.o.GoodsDescription.ToLower().Contains(term)));
        }

        var totalCount = await joined.CountAsync(cancellationToken);

        var rows = await joined
            .OrderByDescending(x => x.o.OrderDate).ThenByDescending(x => x.o.OrderNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new
            {
                x.o.Id, x.o.OrderNumber, x.o.OrderDate, x.o.CustomerId, x.CustomerName,
                x.o.CustomerReference, x.o.Status, x.o.GoodsDescription, x.o.AdrRequired, x.o.CraneRequired,
                x.o.Priority,
            })
            .ToListAsync(cancellationToken);

        // Route summary per page row: first loading / last unloading city, resolved from stop or master location.
        var orderIds = rows.Select(r => r.Id).ToList();
        var stops = await _dbContext.Set<TransportOrderStop>().AsNoTracking()
            .Where(st => st.TenantId == _tenantContext.TenantId && orderIds.Contains(st.TransportOrderId))
            .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == _tenantContext.TenantId),
                st => st.LocationId, l => l.Id,
                (st, locations) => new { Stop = st, Locations = locations })
            .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new
            {
                x.Stop.TransportOrderId, x.Stop.Sequence, x.Stop.StopType,
                City = x.Stop.City ?? (l != null ? l.City : null),
            })
            .ToListAsync(cancellationToken);

        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);

        var items = rows.Select(r =>
        {
            var orderStops = stopsByOrder[r.Id].OrderBy(s => s.Sequence).ToList();
            return new TransportOrderListItemDto(
                r.Id, r.OrderNumber, r.OrderDate, r.CustomerId, r.CustomerName, r.CustomerReference,
                r.Status, r.GoodsDescription,
                orderStops.FirstOrDefault(s => s.StopType == StopType.Loading)?.City,
                orderStops.LastOrDefault(s => s.StopType == StopType.Unloading)?.City,
                orderStops.Count,
                r.AdrRequired, r.CraneRequired, r.Priority);
        }).ToList();

        return new PagedResult<TransportOrderListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    public async Task<TransportOrderDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await TenantScoped().AsNoTracking()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? null : await MapDetailAsync(order, cancellationToken);
    }

    public async Task<TransportOrderOperationResult> CreateAsync(
        CreateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request.CustomerId, request.CustomerReference, request.GoodsDescription,
            request.Stops, enforceCustomerIntake: true, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        if (CargoItemsError(request.CargoItems, request.Stops) is { } cargoError)
        {
            return TransportOrderOperationResult.Invalid(cargoError);
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var order = new TransportOrder
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            CustomerId = request.CustomerId,
            CustomerReference = Trim(request.CustomerReference),
            OrderDate = request.OrderDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
            Status = TransportOrderStatus.Draft,
            GoodsDescription = Trim(request.GoodsDescription),
            Quantity = NonNegative(request.Quantity),
            QuantityUnit = Trim(request.QuantityUnit),
            QuantityUnitCode = NormalizeUnitCode(request.QuantityUnitCode),
            WeightKg = NonNegative(request.WeightKg),
            VolumeM3 = NonNegative(request.VolumeM3),
            PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null,
            AdrRequired = request.AdrRequired,
            CraneRequired = request.CraneRequired,
            Priority = request.Priority ?? OrderPriority.Normal,
            AgreedPrice = NonNegative(request.AgreedPrice),
            Notes = Trim(request.Notes),
            Stops = BuildStops(request.Stops),
        };
        ApplyDieselSurchargeOverride(order,
            request.DieselSurchargeOverride, request.DieselSurchargePercentOverride, request.DieselSurchargeOverrideReason);
        // Selling entity: explicit request value else the customer's default entity.
        order.LegalEntityId = await ResolveOrderLegalEntityAsync(request.LegalEntityId, request.CustomerId, cancellationToken);

        if (await ApplyPricingAsync(order, request.AgreedPrice, request.ServiceOptionIds,
                request.PriceIsManual, request.PriceOverrideReason, cancellationToken) is { } pricingError)
        {
            return pricingError;
        }

        _dbContext.Add(order);
        _dbContext.AddRange(BuildCargoItems(order.Id, request.CargoItems, order.Stops));
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => order.OrderNumber = GenerateOrderNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Created", null,
            new { order.OrderNumber, order.CustomerId, order.OrderDate, StopCount = order.Stops.Count }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<TransportOrderOperationResult> UpdateAsync(
        Guid id, UpdateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (order.Status is not (TransportOrderStatus.Draft or TransportOrderStatus.Submitted or TransportOrderStatus.Confirmed))
        {
            return TransportOrderOperationResult.InvalidState(
                "Alleen concept-, ingediende en bevestigde opdrachten kunnen worden bewerkt.");
        }

        // Switching an order TO a blocked customer is refused; editing an existing order whose
        // customer became blocked afterwards stays possible (dispatch still needs to manage it).
        var validation = await ValidateAsync(request.CustomerId, request.CustomerReference, request.GoodsDescription,
            request.Stops, enforceCustomerIntake: request.CustomerId != order.CustomerId, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        // A confirmed order must keep satisfying the confirmation rules after the edit.
        if (order.Status == TransportOrderStatus.Confirmed && ConfirmationError(request.Stops) is { } confirmError)
        {
            return TransportOrderOperationResult.Invalid(confirmError);
        }

        if (CargoItemsError(request.CargoItems, request.Stops) is { } cargoError)
        {
            return TransportOrderOperationResult.Invalid(cargoError);
        }

        var before = new { order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count };

        order.CustomerId = request.CustomerId;
        order.CustomerReference = Trim(request.CustomerReference);
        order.OrderDate = request.OrderDate ?? order.OrderDate;
        order.GoodsDescription = Trim(request.GoodsDescription);
        order.Quantity = NonNegative(request.Quantity);
        order.QuantityUnit = Trim(request.QuantityUnit);
        order.QuantityUnitCode = NormalizeUnitCode(request.QuantityUnitCode);
        order.WeightKg = NonNegative(request.WeightKg);
        order.VolumeM3 = NonNegative(request.VolumeM3);
        order.PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null;
        order.AdrRequired = request.AdrRequired;
        order.CraneRequired = request.CraneRequired;
        // Null = unchanged, so older clients that don't send a priority never reset it.
        order.Priority = request.Priority ?? order.Priority;
        order.Notes = Trim(request.Notes);

        var surchargeBefore = new { order.DieselSurchargeOverride, order.DieselSurchargePercentOverride };
        ApplyDieselSurchargeOverride(order,
            request.DieselSurchargeOverride, request.DieselSurchargePercentOverride, request.DieselSurchargeOverrideReason);
        var surchargeChanged = surchargeBefore.DieselSurchargeOverride != order.DieselSurchargeOverride
            || surchargeBefore.DieselSurchargePercentOverride != order.DieselSurchargePercentOverride;

        // Null keeps the current entity (never silently cleared); explicit ids are validated.
        if (request.LegalEntityId is { } requestedEntity && requestedEntity != order.LegalEntityId)
        {
            order.LegalEntityId = await ResolveOrderLegalEntityAsync(requestedEntity, order.CustomerId, cancellationToken);
        }

        // Wholesale stop replacement; removal is soft, so the trail stays auditable.
        _dbContext.RemoveRange(order.Stops);
        order.Stops = BuildStops(request.Stops);
        foreach (var stop in order.Stops)
        {
            stop.TransportOrderId = order.Id;
        }

        // The new stops carry client-generated ids; navigation discovery would attach them as
        // Modified (phantom UPDATE). Mark them Added explicitly.
        _dbContext.AddRange(order.Stops);

        // Cargo items follow the same wholesale-replacement model as stops (soft delete).
        var existingCargo = await _dbContext.CargoItems
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(existingCargo);
        _dbContext.AddRange(BuildCargoItems(order.Id, request.CargoItems, order.Stops));

        if (await ApplyPricingAsync(order, request.AgreedPrice, request.ServiceOptionIds,
                request.PriceIsManual, request.PriceOverrideReason, cancellationToken) is { } pricingError)
        {
            return pricingError;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Updated", before,
            new { order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count }, cancellationToken);

        if (surchargeChanged)
        {
            // Record the inherited customer default next to the override for the audit trail.
            var inherited = await _dbContext.CustomerDieselSurcharges.AsNoTracking()
                .Where(s => s.TenantId == _tenantContext.TenantId && s.CustomerId == order.CustomerId)
                .Select(s => (decimal?)s.Percent)
                .FirstOrDefaultAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, order.Id.ToString(), "DieselSurchargeOverridden",
                new { surchargeBefore.DieselSurchargeOverride, surchargeBefore.DieselSurchargePercentOverride, InheritedPercent = inherited },
                new { order.DieselSurchargeOverride, order.DieselSurchargePercentOverride, order.DieselSurchargeOverrideReason },
                cancellationToken);
        }

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    private async Task<Guid?> ResolveOrderLegalEntityAsync(
        Guid? requestedId, Guid customerId, CancellationToken cancellationToken)
    {
        if (requestedId is { } id)
        {
            var valid = await _dbContext.LegalEntities.AnyAsync(
                e => e.TenantId == _tenantContext.TenantId && e.Id == id && e.IsActive, cancellationToken);
            if (!valid)
            {
                throw new Common.DomainValidationException("legalEntityId",
                    "De gekozen facturerende entiteit bestaat niet of is niet actief.");
            }

            return id;
        }

        return await _dbContext.Customers
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId)
            .Select(c => c.DefaultLegalEntityId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Override requires an explicit percentage and a reason; clearing wipes both.</summary>
    private static void ApplyDieselSurchargeOverride(
        TransportOrder order, bool overrideEnabled, decimal? percent, string? reason)
    {
        if (!overrideEnabled)
        {
            order.DieselSurchargeOverride = false;
            order.DieselSurchargePercentOverride = null;
            order.DieselSurchargeOverrideReason = null;
            return;
        }

        if (percent is null or < 0 or > 100)
        {
            throw new Common.DomainValidationException("dieselSurchargePercentOverride",
                "Een overschreven dieseltoeslag vereist een percentage tussen 0 en 100.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new Common.DomainValidationException("dieselSurchargeOverrideReason",
                "Een reden is verplicht bij het overschrijven van de dieseltoeslag.");
        }

        order.DieselSurchargeOverride = true;
        order.DieselSurchargePercentOverride = percent;
        order.DieselSurchargeOverrideReason = reason.Trim();
    }

    public async Task<TransportOrderOperationResult> ChangeStatusAsync(
        Guid id, TransportOrderStatus target, CancellationToken cancellationToken)
    {
        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (!Transitions[order.Status].Contains(target))
        {
            return TransportOrderOperationResult.InvalidState(
                $"Een opdracht met status '{order.Status}' kan niet naar '{target}'.");
        }

        if (target == TransportOrderStatus.Confirmed)
        {
            var stops = order.Stops
                .Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList();
            if (ConfirmationError(stops) is { } confirmError)
            {
                return TransportOrderOperationResult.Invalid(confirmError);
            }
        }

        var before = new { order.Status };
        order.Status = target;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "StatusChanged", before,
            new { order.Status }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<TransportOrderOperationResult> CorrectStatusAsync(
        Guid id, TransportOrderStatus target, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TransportOrderOperationResult.Invalid("Een reden is verplicht bij een statuscorrectie.");
        }

        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (!CorrectiveTransitions.TryGetValue(order.Status, out var targets) || !targets.Contains(target))
        {
            return TransportOrderOperationResult.InvalidState(
                $"Een opdracht met status '{order.Status}' kan niet worden gecorrigeerd naar '{target}'.");
        }

        // Corrections never touch POD, scan or invoice history; an order on a live trip must
        // be released through planning first so trip and order state cannot diverge.
        if (await _dbContext.TripOrders.AnyAsync(t => t.TransportOrderId == order.Id
                && _dbContext.Trips.Any(trip => trip.Id == t.TripId
                    && (trip.Status == Modules.Planning.Entities.TripStatus.Planned
                        || trip.Status == Modules.Planning.Entities.TripStatus.InProgress)), cancellationToken))
        {
            return TransportOrderOperationResult.InvalidState(
                "Deze opdracht is aan een actieve rit gekoppeld; haal ze eerst uit de planning voordat je de status corrigeert.");
        }

        var before = new { order.Status };
        order.PendingStatusChangeReason = reason.Trim();
        order.PendingStatusChangeIsCorrection = true;
        order.Status = target;
        if (order.Status != TransportOrderStatus.Cancelled)
        {
            order.CancellationReason = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "StatusCorrected", before,
            new { order.Status, Reason = reason.Trim() }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<TransportOrderOperationResult> CancelAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TransportOrderOperationResult.Invalid("Een reden is verplicht bij het annuleren van een opdracht.");
        }

        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (!CancellableStatuses.Contains(order.Status))
        {
            return TransportOrderOperationResult.InvalidState(
                $"Een opdracht met status '{order.Status}' kan niet meer worden geannuleerd.");
        }

        var before = new { order.Status };
        order.PendingStatusChangeReason = reason.Trim();
        order.Status = TransportOrderStatus.Cancelled;
        order.CancellationReason = reason.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Cancelled", before,
            new { order.Status, order.CancellationReason }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<IReadOnlyList<TransportOrderListItemDto>> ListForExportAsync(
        string? search, TransportOrderStatus? status, Guid? customerId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        // Bounded export: one page of up to 5000 rows through the normal search pipeline.
        var page = await SearchAsync(search, status, customerId, fromDate, toDate,
            new PageRequest(1, 5000), cancellationToken);
        return page.Items;
    }

    /// <summary>Final statuses in which the execution plan of a stop can no longer change.</summary>
    private static readonly TransportOrderStatus[] ExecutionPlanLockedStatuses =
        [TransportOrderStatus.Completed, TransportOrderStatus.Invoiced, TransportOrderStatus.Cancelled];

    public async Task<TransportOrderOperationResult> UpdateStopExecutionPlanAsync(
        Guid orderId, Guid stopId, UpdateStopExecutionPlanRequest request, CancellationToken cancellationToken)
    {
        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (ExecutionPlanLockedStatuses.Contains(order.Status))
        {
            return TransportOrderOperationResult.InvalidState(
                $"Bij een opdracht met status '{order.Status}' kan het uitvoeringsplan niet meer worden aangepast.");
        }

        var stop = order.Stops.FirstOrDefault(s => s.Id == stopId && !s.IsDeleted);
        if (stop is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (WindowError(request.ConfirmedFrom, request.ConfirmedTo) is { } windowError)
        {
            return TransportOrderOperationResult.Invalid(windowError);
        }

        if (request.EarliestAllowed is { } earliest && request.LatestAllowed is { } latest && latest < earliest)
        {
            return TransportOrderOperationResult.Invalid(
                "Het uiterste tijdstip moet na het vroegst toegelaten tijdstip liggen.");
        }

        var before = new
        {
            stop.ConfirmedFrom, stop.ConfirmedTo, stop.EarliestAllowed, stop.LatestAllowed,
            stop.AppointmentRequired, stop.AppointmentReference,
        };

        stop.ConfirmedFrom = request.ConfirmedFrom;
        stop.ConfirmedTo = request.ConfirmedTo;
        stop.EarliestAllowed = request.EarliestAllowed;
        stop.LatestAllowed = request.LatestAllowed;
        stop.AppointmentRequired = request.AppointmentRequired;
        stop.AppointmentReference = Trim(request.AppointmentReference);
        stop.AccessInstructions = Trim(request.AccessInstructions);
        stop.LoadingInstructions = Trim(request.LoadingInstructions);
        stop.UnloadingInstructions = Trim(request.UnloadingInstructions);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "StopExecutionPlanUpdated", before,
            new
            {
                StopId = stop.Id, stop.ConfirmedFrom, stop.ConfirmedTo, stop.EarliestAllowed, stop.LatestAllowed,
                stop.AppointmentRequired, stop.AppointmentReference,
            }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<TransportOrderOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (order.Status is not (TransportOrderStatus.Draft or TransportOrderStatus.Cancelled))
        {
            return TransportOrderOperationResult.InvalidState(
                "Alleen concept- of geannuleerde opdrachten kunnen worden verwijderd.");
        }

        _dbContext.RemoveRange(order.Stops);
        _dbContext.Remove(order); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Deleted",
            new { order.OrderNumber, order.Status }, null, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<TransportOrderOperationResult> ChangePriorityAsync(
        Guid id, OrderPriority priority, CancellationToken cancellationToken)
    {
        var order = await TenantScoped().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (order.Status is TransportOrderStatus.Completed or TransportOrderStatus.Invoiced or TransportOrderStatus.Cancelled)
        {
            return TransportOrderOperationResult.InvalidState(
                "De prioriteit van een afgeronde of geannuleerde opdracht kan niet meer wijzigen.");
        }

        if (order.Priority != priority)
        {
            var before = new { order.Priority };
            order.Priority = priority;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, order.Id.ToString(), "PriorityChanged",
                before, new { order.Priority }, cancellationToken);
        }

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Shared shape validation for create/update. Returns null when valid.</summary>
    private async Task<TransportOrderOperationResult?> ValidateAsync(
        Guid customerId, string? customerReference, string? goodsDescription,
        IReadOnlyList<TransportOrderStopInput> stops, bool enforceCustomerIntake,
        CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers
            .Where(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => new { c.IsBlocked, c.IsActive, c.CustomerReferenceRequired })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return TransportOrderOperationResult.InvalidReference("De gekoppelde klant bestaat niet.");
        }

        // Intake gate for NEW work for this customer (create, or switching an order to another
        // customer): blocked and deactivated customers refuse new orders; existing orders for
        // the same customer stay editable.
        if (enforceCustomerIntake && customer.IsBlocked)
        {
            return TransportOrderOperationResult.Invalid(
                "Deze klant is geblokkeerd; er kunnen geen opdrachten voor worden aangemaakt.");
        }

        if (enforceCustomerIntake && !customer.IsActive)
        {
            return TransportOrderOperationResult.Invalid(
                "Deze klant is gedeactiveerd; heractiveer de klant om nieuwe opdrachten aan te maken.");
        }

        if (customer.CustomerReferenceRequired && string.IsNullOrWhiteSpace(customerReference))
        {
            return TransportOrderOperationResult.Invalid(
                "Deze klant vereist een klantreferentie bij elke opdracht.");
        }

        foreach (var stop in stops)
        {
            if (stop.LocationId is null && string.IsNullOrWhiteSpace(stop.City))
            {
                return TransportOrderOperationResult.Invalid(
                    "Elke stop heeft een locatie of minstens een plaatsnaam nodig.");
            }

            if ((WindowError(stop.PlannedFrom, stop.PlannedTo)
                 ?? WindowError(stop.RequestedFrom, stop.RequestedTo)
                 ?? WindowError(stop.ConfirmedFrom, stop.ConfirmedTo)) is { } windowError)
            {
                return TransportOrderOperationResult.Invalid(windowError);
            }

            if (stop.EarliestAllowed is { } earliest && stop.LatestAllowed is { } latest && latest < earliest)
            {
                return TransportOrderOperationResult.Invalid(
                    "Het uiterste tijdstip moet na het vroegst toegelaten tijdstip liggen.");
            }
        }

        var locationIds = stops.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        if (locationIds.Count > 0)
        {
            var known = await _dbContext.Locations
                .Where(l => l.TenantId == _tenantContext.TenantId && locationIds.Contains(l.Id))
                .CountAsync(cancellationToken);
            if (known != locationIds.Count)
            {
                return TransportOrderOperationResult.InvalidReference("Een gekoppelde locatie bestaat niet.");
            }
        }

        return null;
    }

    /// <summary>Validates the cargo list: description + positive quantity per item, barcode unambiguous within the order.</summary>
    private static string? CargoItemsError(IReadOnlyList<CargoItemInput>? items, IReadOnlyList<TransportOrderStopInput> stops)
    {
        if (items is null || items.Count == 0)
        {
            return null;
        }

        if (items.Any(i => string.IsNullOrWhiteSpace(i.Description)))
        {
            return "Elke goederenlijn heeft een omschrijving nodig.";
        }

        if (items.Any(i => i.ExpectedQuantity <= 0))
        {
            return "De verwachte hoeveelheid van een goederenlijn moet groter dan nul zijn.";
        }

        if (items.Any(i => i.TotalWeightKg is < 0 || i.WeightPerUnitKg is < 0
            || i.LengthMeters is < 0 || i.WidthMeters is < 0 || i.HeightMeters is < 0 || i.VolumeM3 is < 0))
        {
            return "Gewichten, afmetingen en volume van een goederenlijn mogen niet negatief zijn.";
        }

        foreach (var item in items)
        {
            if (item.LoadingStopIndex is { } load)
            {
                if (load < 0 || load >= stops.Count || stops[load].StopType != StopType.Loading)
                {
                    return "De laadstop van een goederenlijn moet naar een bestaande laadstop verwijzen.";
                }
            }

            if (item.UnloadingStopIndex is { } unload)
            {
                if (unload < 0 || unload >= stops.Count || stops[unload].StopType != StopType.Unloading)
                {
                    return "De losstop van een goederenlijn moet naar een bestaande losstop verwijzen.";
                }
            }

            if (item.LoadingStopIndex is { } l2 && item.UnloadingStopIndex is { } u2 && l2 >= u2)
            {
                return "De laadstop van een goederenlijn moet vóór de losstop liggen.";
            }
        }

        var barcodes = items
            .Select(i => i.Barcode?.Trim().ToLowerInvariant())
            .Where(b => !string.IsNullOrEmpty(b))
            .ToList();
        if (barcodes.Count != barcodes.Distinct().Count())
        {
            return "Een barcode mag maar één keer voorkomen binnen dezelfde opdracht.";
        }

        return null;
    }

    private List<CargoItem> BuildCargoItems(Guid orderId, IReadOnlyList<CargoItemInput>? inputs, IReadOnlyList<TransportOrderStop> stops)
    {
        // Unambiguous orders (one loading + one unloading stop) auto-link omitted stop indexes.
        var loadingStops = stops.Where(s => s.StopType == StopType.Loading).ToList();
        var unloadingStops = stops.Where(s => s.StopType == StopType.Unloading).ToList();
        var defaultLoading = loadingStops.Count == 1 ? loadingStops[0].Id : (Guid?)null;
        var defaultUnloading = unloadingStops.Count == 1 ? unloadingStops[0].Id : (Guid?)null;

        return (inputs ?? []).Select((input, index) =>
        {
            var (volume, volumeIsManual) = Modules.Fleet.Services.FleetFieldRules.ResolveVolume(
                input.LengthMeters, input.WidthMeters, input.HeightMeters, input.VolumeM3, input.VolumeIsManual,
                field: $"cargoItems[{index}].volumeM3");

            return new CargoItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TransportOrderId = orderId,
                Sequence = index + 1,
                Description = input.Description.Trim(),
                Barcode = Trim(input.Barcode),
                ExpectedQuantity = input.ExpectedQuantity,
                QuantityUnit = Trim(input.QuantityUnit),
                Notes = Trim(input.Notes),
                UnitType = input.UnitType,
                UnitTypeLabel = Trim(input.UnitTypeLabel),
                TotalWeightKg = NonNegative(input.TotalWeightKg),
                WeightPerUnitKg = NonNegative(input.WeightPerUnitKg),
                LengthMeters = NonNegative(input.LengthMeters),
                WidthMeters = NonNegative(input.WidthMeters),
                HeightMeters = NonNegative(input.HeightMeters),
                VolumeM3 = volume,
                VolumeIsManual = volumeIsManual,
                AdrRequired = input.AdrRequired,
                AdrDetails = Trim(input.AdrDetails),
                Stackable = input.Stackable,
                Reference = Trim(input.Reference),
                LoadingStopId = input.LoadingStopIndex is { } load ? stops[load].Id : defaultLoading,
                UnloadingStopId = input.UnloadingStopIndex is { } unload ? stops[unload].Id : defaultUnloading,
            };
        }).ToList();
    }

    private static string? WindowError(DateTime? from, DateTime? to) =>
        from is { } f && to is { } t && t < f
            ? "Het einde van een tijdvenster moet na het begin liggen."
            : null;

    /// <summary>Rules an order must satisfy to be (or stay) confirmed. Returns null when satisfied.</summary>
    private static string? ConfirmationError(IReadOnlyList<TransportOrderStopInput> stops)
    {
        if (!stops.Any(s => s.StopType == StopType.Loading) || !stops.Any(s => s.StopType == StopType.Unloading))
        {
            return "Een bevestigde opdracht heeft minstens één laad- en één losstop nodig.";
        }

        return null;
    }

    private List<TransportOrderStop> BuildStops(IReadOnlyList<TransportOrderStopInput> inputs) =>
        inputs.Select((input, index) => new TransportOrderStop
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Sequence = index + 1,
            StopType = input.StopType,
            LocationId = input.LocationId,
            LocationName = Trim(input.LocationName),
            Address = Trim(input.Address),
            PostalCode = Trim(input.PostalCode),
            City = Trim(input.City),
            CountryCode = Trim(input.CountryCode)?.ToUpperInvariant(),
            PlannedFrom = input.PlannedFrom,
            PlannedTo = input.PlannedTo,
            RequestedFrom = input.RequestedFrom,
            RequestedTo = input.RequestedTo,
            ConfirmedFrom = input.ConfirmedFrom,
            ConfirmedTo = input.ConfirmedTo,
            EarliestAllowed = input.EarliestAllowed,
            LatestAllowed = input.LatestAllowed,
            AppointmentRequired = input.AppointmentRequired,
            AppointmentReference = Trim(input.AppointmentReference),
            Reference = Trim(input.Reference),
            Instructions = Trim(input.Instructions),
            AccessInstructions = Trim(input.AccessInstructions),
            LoadingInstructions = Trim(input.LoadingInstructions),
            UnloadingInstructions = Trim(input.UnloadingInstructions),
        }).ToList();

    private async Task<TransportOrderDetailDto> MapDetailAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var locationIds = order.Stops.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        var locations = locationIds.Count == 0
            ? []
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);

        var stops = order.Stops
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Sequence)
            .Select(s =>
            {
                var location = s.LocationId is { } lid && locations.TryGetValue(lid, out var l) ? l : null;
                var locationAddress = location is null
                    ? null
                    : string.Join(" ", new[] { location.Street, location.HouseNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));
                return new TransportOrderStopDto(
                    s.Id, s.Sequence, s.StopType,
                    s.LocationId,
                    location?.Code,
                    location?.Name ?? s.LocationName ?? s.City ?? string.Empty,
                    s.Address ?? (string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress),
                    s.PostalCode ?? location?.PostalCode,
                    s.City ?? location?.City,
                    s.CountryCode ?? location?.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
                    s.RequestedFrom, s.RequestedTo, s.ConfirmedFrom, s.ConfirmedTo,
                    s.EarliestAllowed, s.LatestAllowed,
                    s.AppointmentRequired, s.AppointmentReference,
                    s.AccessInstructions, s.LoadingInstructions, s.UnloadingInstructions);
            })
            .ToList();

        var cargoItems = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
            .OrderBy(c => c.Sequence)
            .Select(c => new CargoItemDto(
                c.Id, c.Sequence, c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes,
                c.UnitType, c.UnitTypeLabel, c.TotalWeightKg, c.WeightPerUnitKg,
                c.LengthMeters, c.WidthMeters, c.HeightMeters, c.VolumeM3, c.VolumeIsManual,
                c.AdrRequired, c.AdrDetails, c.Stackable, c.Reference, c.LoadingStopId, c.UnloadingStopId))
            .ToListAsync(cancellationToken);

        var pricingLines = await _dbContext.TransportOrderPricingLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.Sequence)
            .Select(l => new OrderPricingLineDto(l.Label, l.Amount, l.Source, l.Informational))
            .ToListAsync(cancellationToken);
        var serviceLines = await _dbContext.TransportOrderServiceLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.NameSnapshot)
            .Select(l => new OrderServiceLineDto(l.ServiceOptionId, l.NameSnapshot, l.Kind, l.Value, l.Amount))
            .ToListAsync(cancellationToken);

        return new TransportOrderDetailDto(
            order.Id, order.OrderNumber, order.OrderDate, order.CustomerId, customerName,
            order.CustomerReference, order.Status, order.GoodsDescription,
            order.Quantity, order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount,
            order.AdrRequired, order.CraneRequired, order.AgreedPrice, order.Notes,
            order.CancellationReason,
            stops, cargoItems, Transitions[order.Status],
            CancellableStatuses.Contains(order.Status),
            CorrectiveTransitions.TryGetValue(order.Status, out var corrections) ? corrections : [],
            order.Priority,
            order.DieselSurchargeOverride, order.DieselSurchargePercentOverride, order.DieselSurchargeOverrideReason,
            order.LegalEntityId, order.QuantityUnitCode,
            order.CalculatedPrice, order.PriceIsManual, order.PriceOverrideReason,
            pricingLines, serviceLines);
    }

    /// <summary>
    /// Runs the pricing engine, snapshots the breakdown + service lines on the order and
    /// determines the effective AgreedPrice:
    /// manual override (permission + reason) > calculated total > legacy manual entry when
    /// nothing could be calculated. Snapshots only change on an explicit save, so historical
    /// orders never move when master-data tariffs change.
    /// </summary>
    private async Task<TransportOrderOperationResult?> ApplyPricingAsync(
        TransportOrder order, decimal? requestedAgreedPrice, IReadOnlyList<Guid>? serviceOptionIds,
        bool priceIsManual, string? overrideReason, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var existingPricing = await _dbContext.TransportOrderPricingLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(existingPricing);
        var existingServices = await _dbContext.TransportOrderServiceLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(existingServices);

        PriceCalculationResult? result = null;
        if (_pricingEngine is not null)
        {
            var lines = new List<PriceCalculationLineInput>();
            if (order.Quantity is { } quantity && quantity > 0 && order.QuantityUnitCode is { } code)
            {
                var unitTypeId = await _dbContext.UnitTypes.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && u.Code == code)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (unitTypeId is { } uid)
                {
                    lines.Add(new PriceCalculationLineInput(uid, quantity));
                }
            }

            var delivery = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Unloading)
                .OrderBy(s => s.Sequence)
                .LastOrDefault();
            result = await _pricingEngine.CalculateAsync(new PriceCalculationRequest(
                order.CustomerId, order.OrderDate, lines,
                delivery?.CountryCode, delivery?.PostalCode,
                order.WeightKg, null, order.PalletCount,
                serviceOptionIds ?? []), cancellationToken);
        }

        var calculated = result is { RequiresManualPrice: false } && result.Lines.Any(l => !l.Informational)
            ? result.Total
            : (decimal?)null;
        order.CalculatedPrice = calculated;

        if (priceIsManual)
        {
            if (string.IsNullOrWhiteSpace(overrideReason))
            {
                return TransportOrderOperationResult.Invalid("Een reden is verplicht bij een handmatige prijs.");
            }

            var userId = _currentUser?.CurrentUserId;
            var allowed = _permissionService is null
                || (userId is { } id && await _permissionService.UserHasPermissionAsync(id, PermissionCodes.OrdersOverridePrice, cancellationToken));
            if (!allowed)
            {
                return TransportOrderOperationResult.Invalid("Je hebt geen rechten om de berekende prijs te overschrijven.");
            }

            order.AgreedPrice = NonNegative(requestedAgreedPrice);
            order.PriceIsManual = true;
            order.PriceOverrideReason = overrideReason.Trim();
        }
        else if (calculated is { } total)
        {
            order.AgreedPrice = total;
            order.PriceIsManual = false;
            order.PriceOverrideReason = null;
        }
        else
        {
            // No pricing configuration → the pre-engine manual entry keeps working unchanged.
            order.AgreedPrice = NonNegative(requestedAgreedPrice);
            order.PriceIsManual = false;
            order.PriceOverrideReason = null;
        }

        if (result is not null)
        {
            var sequence = 0;
            foreach (var line in result.Lines)
            {
                _dbContext.TransportOrderPricingLines.Add(new TransportOrderPricingLine
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id,
                    Sequence = sequence++, Label = line.Label, Amount = line.Amount,
                    Source = line.Source, Informational = line.Informational,
                });
            }

            foreach (var serviceLine in result.ServiceLines)
            {
                _dbContext.TransportOrderServiceLines.Add(new TransportOrderServiceLine
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id,
                    ServiceOptionId = serviceLine.ServiceOptionId, NameSnapshot = serviceLine.Name,
                    Kind = serviceLine.Kind, Value = serviceLine.Value, Amount = serviceLine.Amount,
                });
            }
        }

        return null;
    }

    /// <summary>Uppercases a managed unit code; blank → null (free-text QuantityUnit is the fallback).</summary>
    private static string? NormalizeUnitCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static string GenerateOrderNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }

        var number = $"{settings.OrderNumberPrefix}{settings.OrderNumberNextValue:0000}";
        settings.OrderNumberNextValue++;
        return number;
    }

    private static decimal? NonNegative(decimal? value) => value is < 0 ? 0 : value;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
