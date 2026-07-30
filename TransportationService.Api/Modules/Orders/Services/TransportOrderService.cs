using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
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
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<TransportOrderService>? _logger;

    public TransportOrderService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IPricingEngine? pricingEngine = null,
        ICurrentUserContext? currentUser = null,
        IPermissionAuthorizationService? permissionService = null,
        INotificationEventService? notificationEvents = null,
        ILogger<TransportOrderService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _pricingEngine = pricingEngine;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    /// <summary>Fire-and-forget event publication: never lets a notification failure break the
    /// business operation that already committed (see NotificationEventService's contract).</summary>
    private async Task PublishEventAsync(string eventKey, NotificationEventContext context, CancellationToken cancellationToken)
    {
        if (_notificationEvents is null)
        {
            return;
        }

        try
        {
            await _notificationEvents.PublishAsync(eventKey, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.", eventKey);
        }
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

        if (OneOffPricingError(
                request.PricingSource, request.OneOffFixedAmount,
                request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes)
            is { } oneOffError)
        {
            return TransportOrderOperationResult.Invalid(oneOffError);
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
        ApplyOneOffPricing(order, request.PricingSource, request.OneOffFixedAmount,
            request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes,
            request.OneOffExtraHourlyRate, request.OneOffNotes);
        ApplyDieselSurchargeOverride(order,
            request.DieselSurchargeOverride, request.DieselSurchargePercentOverride, request.DieselSurchargeOverrideReason);
        // Selling entity: explicit request value else the customer's default entity.
        order.LegalEntityId = await ResolveOrderLegalEntityAsync(request.LegalEntityId, request.CustomerId, cancellationToken);

        var cargoItems = BuildCargoItems(order.Id, request.CargoItems, order.Stops);
        if (await ApplyPricingAsync(order, request.AgreedPrice, ResolveServiceSelections(request.Services, request.ServiceOptionIds),
                request.PriceIsManual, request.PriceOverrideReason, cargoItems, cancellationToken) is { } pricingError)
        {
            return pricingError;
        }

        _dbContext.Add(order);
        _dbContext.AddRange(cargoItems);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => order.OrderNumber = GenerateOrderNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Created", null,
            new { order.OrderNumber, order.CustomerId, order.OrderDate, StopCount = order.Stops.Count }, cancellationToken);

        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        await PublishEventAsync(MessageKinds.OrderCreated, new NotificationEventContext(
            EntityType, order.Id.ToString(),
            new Dictionary<string, string>
            {
                ["orderNumber"] = order.OrderNumber,
                ["customerName"] = customerName,
                ["goodsDescription"] = order.GoodsDescription ?? string.Empty,
            })
        {
            CustomerId = order.CustomerId,
            LinkPath = $"/orders/{order.Id}",
            InAppMessage = $"{order.OrderNumber} ({customerName}) is aangemaakt.",
        }, cancellationToken);

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

        if (OneOffPricingError(
                request.PricingSource, request.OneOffFixedAmount,
                request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes)
            is { } oneOffError)
        {
            return TransportOrderOperationResult.Invalid(oneOffError);
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
        ApplyOneOffPricing(order, request.PricingSource, request.OneOffFixedAmount,
            request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes,
            request.OneOffExtraHourlyRate, request.OneOffNotes);

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
        var replacementCargo = BuildCargoItems(order.Id, request.CargoItems, order.Stops);
        _dbContext.AddRange(replacementCargo);

        if (await ApplyPricingAsync(order, request.AgreedPrice, ResolveServiceSelections(request.Services, request.ServiceOptionIds),
                request.PriceIsManual, request.PriceOverrideReason, replacementCargo, cancellationToken) is { } pricingError)
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

    /// <summary>
    /// Validates a one-off price agreement (spec Phase 6): a fixed amount is mandatory, and the
    /// combined and per-activity included-time variants are mutually exclusive. Returns null when
    /// PricingSource is Contract (nothing to validate) or when the OneOff fields are valid.
    /// </summary>
    private static string? OneOffPricingError(
        OrderPricingSource pricingSource, decimal? fixedAmount,
        int? includedLoadingMinutes, int? includedUnloadingMinutes, int? includedCombinedMinutes)
    {
        if (pricingSource != OrderPricingSource.OneOff)
        {
            return null;
        }

        if (fixedAmount is null || fixedAmount < 0)
        {
            return "Geef het vaste bedrag van de eenmalige prijsafspraak op.";
        }

        if (includedCombinedMinutes is not null && (includedLoadingMinutes is not null || includedUnloadingMinutes is not null))
        {
            return "Kies inbegrepen tijd per activiteit óf gecombineerd, niet beide.";
        }

        return null;
    }

    /// <summary>Sets the order's one-off price agreement fields; clears them when PricingSource is Contract.</summary>
    private static void ApplyOneOffPricing(
        TransportOrder order, OrderPricingSource pricingSource, decimal? fixedAmount,
        int? includedLoadingMinutes, int? includedUnloadingMinutes, int? includedCombinedMinutes,
        decimal? extraHourlyRate, string? notes)
    {
        order.PricingSource = pricingSource;
        if (pricingSource != OrderPricingSource.OneOff)
        {
            order.OneOffFixedAmount = null;
            order.OneOffIncludedLoadingMinutes = null;
            order.OneOffIncludedUnloadingMinutes = null;
            order.OneOffIncludedCombinedMinutes = null;
            order.OneOffExtraHourlyRate = null;
            order.OneOffNotes = null;
            return;
        }

        order.OneOffFixedAmount = NonNegative(fixedAmount);
        order.OneOffIncludedLoadingMinutes = includedLoadingMinutes;
        order.OneOffIncludedUnloadingMinutes = includedUnloadingMinutes;
        order.OneOffIncludedCombinedMinutes = includedCombinedMinutes;
        order.OneOffExtraHourlyRate = NonNegative(extraHourlyRate);
        order.OneOffNotes = Trim(notes);
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

        var wasSubmitted = order.Status == TransportOrderStatus.Submitted;
        var before = new { order.Status };
        order.Status = target;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "StatusChanged", before,
            new { order.Status }, cancellationToken);

        // Portal review outcome: a Submitted order the planner confirms or sends back to Draft.
        // Only these two ORIGINATE from a portal submission — Confirmed<->Draft transitions
        // elsewhere in the workflow (e.g. un-confirming an internally created order) are not
        // customer-facing decisions and stay silent.
        if (wasSubmitted && target is TransportOrderStatus.Confirmed or TransportOrderStatus.Draft)
        {
            var eventKey = target == TransportOrderStatus.Confirmed ? MessageKinds.OrderAccepted : MessageKinds.OrderRejected;
            var customerName = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            await PublishEventAsync(eventKey, new NotificationEventContext(
                EntityType, order.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["orderNumber"] = order.OrderNumber,
                    ["customerName"] = customerName,
                    ["goodsDescription"] = order.GoodsDescription ?? string.Empty,
                })
            {
                CustomerId = order.CustomerId,
                LinkPath = $"/portal/orders/{order.Id}",
            }, cancellationToken);
        }

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

        var wasSubmitted = order.Status == TransportOrderStatus.Submitted;
        var before = new { order.Status };
        order.PendingStatusChangeReason = reason.Trim();
        order.Status = TransportOrderStatus.Cancelled;
        order.CancellationReason = reason.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Cancelled", before,
            new { order.Status, order.CancellationReason }, cancellationToken);

        // Portal review outcome: a Submitted order rejected outright (Cancelled, not sent back
        // to Draft) still tells the customer their order was rejected, same event as the
        // Submitted->Draft "send back for corrections" path in ChangeStatusAsync.
        if (wasSubmitted)
        {
            var customerName = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            await PublishEventAsync(MessageKinds.OrderRejected, new NotificationEventContext(
                EntityType, order.Id.ToString(),
                new Dictionary<string, string>
                {
                    ["orderNumber"] = order.OrderNumber,
                    ["customerName"] = customerName,
                    ["goodsDescription"] = order.GoodsDescription ?? string.Empty,
                })
            {
                CustomerId = order.CustomerId,
                LinkPath = $"/portal/orders/{order.Id}",
            }, cancellationToken);
        }

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
                QuantityUnitCode = NormalizeUnitCode(input.QuantityUnitCode),
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
            .Select(l => new OrderPricingLineDto(
                l.Label, l.Amount, l.Source, l.Informational,
                l.RuleName, l.AgreementName, l.ActualQuantity, l.BillableQuantity, l.Proposed,
                l.Id, l.Kind, l.Quantity, l.UnitPrice, l.OriginalQuantity, l.OriginalUnitPrice, l.OriginalAmount,
                l.AdjustReason, l.LineKey))
            .ToListAsync(cancellationToken);
        // Recomputed from the persisted lines (never separately snapshotted) so it can never drift
        // from CalculatedPrice/pricingLines — a proposed extra-time charge is never invoiceable on its own.
        var totalWithProposed = order.CalculatedPrice is { } calculatedTotal
            ? calculatedTotal + pricingLines.Where(l => l.Proposed && !l.Informational).Sum(l => l.Amount)
            : (decimal?)null;
        var pricingSnapshot = await _dbContext.TransportOrderPricingSnapshots.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.TransportOrderId == order.Id)
            .Select(s => new OrderPricingSnapshotDto(
                s.TariffDate, s.Currency, s.ZoneCode, s.ZoneName,
                s.AgreementNames, s.UnitSummary, s.CalculatedTotal,
                s.OverrideAmount, s.OverrideReason, s.OverriddenByUserId, s.OverriddenAtUtc,
                s.Explanation, s.Status, s.LinesTotal))
            .FirstOrDefaultAsync(cancellationToken);
        var serviceLines = await _dbContext.TransportOrderServiceLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.NameSnapshot)
            .Select(l => new OrderServiceLineDto(l.ServiceOptionId, l.NameSnapshot, l.Kind, l.Value, l.Amount, l.Quantity, l.PalletCount, l.DayCount))
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
            pricingLines, serviceLines, pricingSnapshot,
            order.PricingSource, order.OneOffFixedAmount,
            order.OneOffIncludedLoadingMinutes, order.OneOffIncludedUnloadingMinutes, order.OneOffIncludedCombinedMinutes,
            order.OneOffExtraHourlyRate, order.OneOffNotes, totalWithProposed);
    }

    /// <summary>
    /// Runs the pricing engine, snapshots the breakdown + service lines on the order and
    /// determines the effective AgreedPrice:
    /// manual override (permission + reason) > calculated total > legacy manual entry when
    /// nothing could be calculated. Snapshots only change on an explicit save, so historical
    /// orders never move when master-data tariffs change.
    /// </summary>
    /// <summary>Newer quantity-aware selections win; the plain id list stays supported.</summary>
    private static IReadOnlyList<OrderServiceInput> ResolveServiceSelections(
        IReadOnlyList<OrderServiceInput>? services, IReadOnlyList<Guid>? serviceOptionIds) =>
        services is { Count: > 0 }
            ? services
            : (serviceOptionIds ?? []).Select(id => new OrderServiceInput(id)).ToList();

    /// <summary>
    /// The billable quantity the engine sees, KIND-aware: an explicit Quantity (manual
    /// correction) always wins; PerPalletDay derives pallets × days only when BOTH are known
    /// (a lone day count must never silently imply one pallet — it stays the informational
    /// "geef het aantal pallet-dagen op" prompt); PerDay accepts the lone day count.
    /// </summary>
    private static decimal? EffectiveServiceQuantity(OrderServiceInput selection, Modules.Tarification.Entities.SurchargeKind kind) =>
        kind switch
        {
            Modules.Tarification.Entities.SurchargeKind.PerPalletDay =>
                selection.Quantity ?? (selection.PalletCount is { } pallets && selection.DayCount is { } days ? pallets * days : null),
            Modules.Tarification.Entities.SurchargeKind.PerDay => selection.Quantity ?? selection.DayCount,
            _ => selection.Quantity,
        };

    private async Task<IReadOnlyList<PriceServiceInput>> ToEngineSelectionsAsync(
        IReadOnlyList<OrderServiceInput> selections, CancellationToken cancellationToken)
    {
        if (selections.Count == 0)
        {
            return [];
        }

        var optionIds = selections.Select(s => s.ServiceOptionId).Distinct().ToList();
        var kinds = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == _tenantContext.TenantId && optionIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Kind, cancellationToken);
        return selections
            .Select(s => new PriceServiceInput(s.ServiceOptionId, EffectiveServiceQuantity(s, kinds.GetValueOrDefault(s.ServiceOptionId))))
            .ToList();
    }

    /// <summary>Shared Dutch message for every endpoint that refuses to touch a Locked/Invoiced price.</summary>
    private const string PricingLockedMessage = "De prijs van deze order is vergrendeld. Ontgrendel eerst om te herberekenen.";

    /// <summary>Allowed pricing-status transitions; Invoiced is reachable only via invoice generation.</summary>
    private static readonly IReadOnlyDictionary<OrderPricingStatus, OrderPricingStatus[]> PricingStatusTransitions =
        new Dictionary<OrderPricingStatus, OrderPricingStatus[]>
        {
            [OrderPricingStatus.Draft] = [OrderPricingStatus.Reviewed, OrderPricingStatus.Locked],
            [OrderPricingStatus.Reviewed] = [OrderPricingStatus.Draft, OrderPricingStatus.Locked],
            [OrderPricingStatus.Locked] = [OrderPricingStatus.Reviewed],
            [OrderPricingStatus.Invoiced] = [],
        };

    /// <summary>Kinds whose (non-informational) amount counts towards LinesTotal/AgreedPrice.</summary>
    private static bool CountsTowardsLinesTotal(TransportOrderPricingLine line) =>
        !line.IsDeleted && !line.Informational
        && line.Kind is OrderPriceLineKind.Auto or OrderPriceLineKind.AutoAdjusted or OrderPriceLineKind.Manual;

    /// <summary>
    /// Runs the pricing engine, MERGES the result into the existing persisted lines (spec ch.
    /// 24-26: never delete-all-rewrite — Manual lines are preserved verbatim, AutoAdjusted lines
    /// keep the user's values and refresh their Original* baseline, orphaned adjustments become
    /// Manual) and determines the effective AgreedPrice: manual whole-order override > LinesTotal
    /// (when any qualifying line exists) > legacy manual entry. A Locked/Invoiced snapshot skips
    /// recalculation entirely; an attempt to change pricing-relevant inputs while locked is refused.
    /// </summary>
    private async Task<TransportOrderOperationResult?> ApplyPricingAsync(
        TransportOrder order, decimal? requestedAgreedPrice, IReadOnlyList<OrderServiceInput> serviceSelections,
        bool priceIsManual, string? overrideReason, IReadOnlyList<CargoItem>? cargoItems,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var existingSnapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == order.Id, cancellationToken);

        var engineSelections = await ToEngineSelectionsAsync(serviceSelections, cancellationToken);

        if (existingSnapshot is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            if (await PricingInputsChangedAsync(order, requestedAgreedPrice, priceIsManual, overrideReason, engineSelections, cancellationToken))
            {
                throw new Common.DomainValidationException(PricingLockedMessage);
            }

            // Non-pricing edits (notes, stops, ...) proceed without touching any pricing row.
            return null;
        }

        // Read-only for now — no removal/insertion yet. A priceIsManual permission/reason failure
        // below must be able to bail out WITHOUT leaving any newly-added pricing rows dangling in
        // the change tracker (they would reference an order that, on Create, never gets added).
        var existingLines = await _dbContext.TransportOrderPricingLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        var manualLines = existingLines.Where(l => l.Kind == OrderPriceLineKind.Manual).OrderBy(l => l.Sequence).ToList();
        var autoAdjustedLines = existingLines.Where(l => l.Kind == OrderPriceLineKind.AutoAdjusted).ToList();
        var obsoleteLines = existingLines.Where(l => l.Kind is OrderPriceLineKind.Auto or OrderPriceLineKind.Proposed).ToList();

        var existingServices = await _dbContext.TransportOrderServiceLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);

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
                    // Cargo lines with the same managed unit describe the physical detail of
                    // this quantity — dimensions feed billable-quantity contracts (oversize).
                    var details = (cargoItems ?? [])
                        .Where(c => !c.IsDeleted && string.Equals(c.QuantityUnitCode, code, StringComparison.OrdinalIgnoreCase))
                        .Select(c => new PriceCalculationLineDetail(
                            c.ExpectedQuantity,
                            c.LengthMeters is { } length ? length * 100m : null,
                            c.WidthMeters is { } width ? width * 100m : null))
                        .ToList();
                    lines.Add(new PriceCalculationLineInput(uid, quantity, details.Count > 0 ? details : null));
                }
            }

            var unloadingStops = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Unloading)
                .OrderBy(s => s.Sequence)
                .ToList();
            var delivery = unloadingStops.LastOrDefault();
            var (actualLoadingMinutes, actualUnloadingMinutes) = await ComputeActualStopMinutesAsync(order, cancellationToken);
            var oneOff = order.PricingSource == OrderPricingSource.OneOff
                ? new OneOffPricingInput(
                    order.OneOffFixedAmount ?? 0m, order.OneOffIncludedLoadingMinutes, order.OneOffIncludedUnloadingMinutes,
                    order.OneOffIncludedCombinedMinutes, order.OneOffExtraHourlyRate, order.OneOffNotes)
                : null;
            // Warehouses the order touches (stop at the warehouse's master location) — feeds
            // warehouse-conditioned service options (wave 2026-07-27 §2.4).
            var stopLocationIds = order.Stops
                .Where(s => !s.IsDeleted && s.LocationId is not null)
                .Select(s => s.LocationId!.Value)
                .Distinct()
                .ToList();
            var warehouseIds = stopLocationIds.Count == 0
                ? null
                : await _dbContext.Warehouses.AsNoTracking()
                    .Where(w => w.TenantId == tenantId && w.IsActive && stopLocationIds.Contains(w.LocationId))
                    .Select(w => w.Id)
                    .ToListAsync(cancellationToken);

            var groups = await BuildPricingGroupsAsync(order, cargoItems, cancellationToken);
            if (groups.Count == 0 && lines.Count > 0)
            {
                // No cargo item maps to a managed unit (or none exist) — fall back to a single
                // "order" group built from the order-level unit line, so Order-scope combined-unit
                // discounts still work even without per-stop cargo detail.
                groups = [new PriceCalculationGroup(
                    "order", "Order", lines.Select(l => new PriceCalculationGroupUnit(l.UnitTypeId, l.Quantity)).ToList())];
            }

            result = await _pricingEngine.CalculateAsync(new PriceCalculationRequest(
                order.CustomerId, order.OrderDate, lines,
                delivery?.CountryCode, delivery?.PostalCode,
                order.WeightKg, null, order.PalletCount,
                [], Services: engineSelections,
                VolumeM3: order.VolumeM3,
                StopCount: unloadingStops.Count > 0 ? unloadingStops.Count : null,
                AdrRequired: order.AdrRequired,
                CargoLineCount: cargoItems?.Count(c => !c.IsDeleted),
                OneOff: oneOff,
                ActualLoadingMinutes: actualLoadingMinutes,
                ActualUnloadingMinutes: actualUnloadingMinutes,
                Groups: groups.Count > 0 ? groups : null,
                WarehouseIds: warehouseIds), cancellationToken);
        }

        var calculated = result is { RequiresManualPrice: false } && result.Lines.Any(l => !l.Informational)
            ? result.Total
            : (decimal?)null;
        order.CalculatedPrice = calculated;

        // Validate the whole-order manual override BEFORE mutating anything: a failure here must
        // return without touching the change tracker (no dangling Added/Removed pricing rows).
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
        }

        _dbContext.RemoveRange(obsoleteLines);
        _dbContext.RemoveRange(existingServices);

        // --- Merge fresh engine lines into the surviving Manual/AutoAdjusted lines ------------
        var mergedLines = new List<TransportOrderPricingLine>();
        var sequence = 0;
        var consumedAdjustedIds = new HashSet<Guid>();
        foreach (var line in result?.Lines ?? [])
        {
            var matchedAdjusted = line.LineKey is not null
                ? autoAdjustedLines.FirstOrDefault(a => a.LineKey == line.LineKey && !consumedAdjustedIds.Contains(a.Id))
                : null;
            if (matchedAdjusted is not null)
            {
                consumedAdjustedIds.Add(matchedAdjusted.Id);
                matchedAdjusted.Sequence = sequence++;
                matchedAdjusted.Source = line.Source;
                matchedAdjusted.RuleName = line.RuleName;
                matchedAdjusted.AgreementName = line.AgreementName;
                matchedAdjusted.ActualQuantity = line.ActualQuantity;
                matchedAdjusted.RuleId = line.RuleId;
                matchedAdjusted.ServiceOptionId = line.ServiceOptionId;
                // The user's own Label/Quantity/UnitPrice/Amount/AdjustReason/AdjustedBy/At stay;
                // only the engine-derived baseline (Original*) refreshes to the fresh calculation.
                matchedAdjusted.OriginalQuantity = line.BillableQuantity ?? line.ActualQuantity;
                matchedAdjusted.OriginalUnitPrice = DeriveUnitPrice(line.LineKey, line.Amount, line.BillableQuantity);
                matchedAdjusted.OriginalAmount = decimal.Round(line.Amount, 2);
                // Invariant (Kind is authoritative, Proposed is derived — see the entity doc
                // comment): an AutoAdjusted line is never Proposed, even if it started life as one
                // (see SetKind — a Proposed line adjusted via the adjust endpoint becomes AutoAdjusted
                // and Proposed is cleared there too). Re-asserted here defensively on every merge.
                matchedAdjusted.Proposed = false;
                mergedLines.Add(matchedAdjusted);
                continue;
            }

            var kind = line.Proposed ? OrderPriceLineKind.Proposed : OrderPriceLineKind.Auto;
            mergedLines.Add(new TransportOrderPricingLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id,
                Sequence = sequence++, Label = line.Label, Amount = decimal.Round(line.Amount, 2),
                Source = line.Source, Informational = line.Informational,
                RuleName = line.RuleName, AgreementName = line.AgreementName,
                ActualQuantity = line.ActualQuantity, BillableQuantity = line.BillableQuantity,
                Proposed = line.Proposed, Kind = kind,
                Quantity = line.BillableQuantity,
                UnitPrice = DeriveUnitPrice(line.LineKey, line.Amount, line.BillableQuantity),
                RuleId = line.RuleId, ServiceOptionId = line.ServiceOptionId, LineKey = line.LineKey,
            });
        }

        // Orphaned adjustments (their engine source disappeared, e.g. a deleted rule): nothing
        // silently disappears — keep the row, convert it to a free Manual line.
        foreach (var orphan in autoAdjustedLines.Where(a => !consumedAdjustedIds.Contains(a.Id)))
        {
            SetKind(orphan, OrderPriceLineKind.Manual);
            orphan.Sequence = sequence++;
            mergedLines.Add(orphan);
        }

        foreach (var manual in manualLines)
        {
            manual.Sequence = sequence++;
            mergedLines.Add(manual);
        }

        foreach (var line in mergedLines)
        {
            if (_dbContext.Entry(line).State == EntityState.Detached)
            {
                _dbContext.TransportOrderPricingLines.Add(line);
            }
        }

        var linesTotal = decimal.Round(mergedLines.Where(CountsTowardsLinesTotal).Sum(l => l.Amount), 2);
        // A user-touched line (Manual, or AutoAdjusted surviving a merge — spec ch. 24-26) can hold
        // a real amount that a bare "nothing configured"/"no bracket for this quantity" diagnostic
        // Auto placeholder (amount 0, engine-generated, never touched by anyone) never can. Only the
        // former must force LinesTotal to win over a stale requestedAgreedPrice when the engine
        // itself came back empty-handed (RequiresManualPrice) — otherwise an order with NO usable
        // pricing configuration at all (only "Geen tarief geconfigureerd" placeholders) must keep
        // falling back to the legacy manual entry below, exactly as before this fix.
        var hasUserTouchedLines = mergedLines.Any(l =>
            !l.Informational && l.Kind is OrderPriceLineKind.Manual or OrderPriceLineKind.AutoAdjusted);

        if (priceIsManual)
        {
            // Already validated above.
            order.AgreedPrice = NonNegative(requestedAgreedPrice);
            order.PriceIsManual = true;
            order.PriceOverrideReason = overrideReason!.Trim();
        }
        else if (calculated is not null || hasUserTouchedLines)
        {
            // LinesTotal reflects manual adjustments (spec ch. 24-26) — it is the calculated
            // total when nothing was ever adjusted, so this is a strict generalisation of the
            // previous "calculated total wins" behaviour.
            order.AgreedPrice = linesTotal;
            order.PriceIsManual = false;
            order.PriceOverrideReason = null;
        }
        else
        {
            // No usable pricing configuration and no manual line either → the pre-engine manual
            // entry keeps working unchanged.
            order.AgreedPrice = NonNegative(requestedAgreedPrice);
            order.PriceIsManual = false;
            order.PriceOverrideReason = null;
        }

        var selectionByOptionId = serviceSelections
            .GroupBy(s => s.ServiceOptionId)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var serviceLine in result?.ServiceLines ?? [])
        {
            // Persist the per-day / per-pallet-day inputs alongside the billable quantity so the
            // UI can re-show them and a recalculation reproduces the exact same numbers.
            var selection = serviceLine.ServiceOptionId is { } optionId
                ? selectionByOptionId.GetValueOrDefault(optionId)
                : null;
            _dbContext.TransportOrderServiceLines.Add(new TransportOrderServiceLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id,
                ServiceOptionId = serviceLine.ServiceOptionId, NameSnapshot = serviceLine.Name,
                Kind = serviceLine.Kind, Value = serviceLine.Value, Amount = serviceLine.Amount,
                Quantity = serviceLine.Quantity,
                PalletCount = selection?.PalletCount,
                DayCount = selection?.DayCount,
                InvoiceDescriptionSnapshot = serviceLine.InvoiceLabel,
            });
        }

        if (result is not null)
        {
            var unitLine = result.Lines.FirstOrDefault(l => l.ActualQuantity is not null);
            var unitSummary = unitLine is null ? null : unitLine.Label;
            var agreementNames = string.Join("; ", result.Lines
                .Where(l => l.AgreementName is not null)
                .Select(l => l.AgreementName!)
                .Distinct());
            var explanation = string.Join("\n", result.Lines
                .Select(l => $"{l.Label}: {l.Amount:0.00} EUR ({l.Source})"));

            var snapshot = existingSnapshot;
            if (snapshot is null)
            {
                snapshot = new TransportOrderPricingSnapshot
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = order.Id,
                    Status = OrderPricingStatus.Draft,
                };
                _dbContext.TransportOrderPricingSnapshots.Add(snapshot);
            }

            snapshot.TariffDate = result.TariffDate ?? order.OrderDate;
            snapshot.Currency = result.Currency;
            snapshot.ZoneCode = result.ZoneCode;
            snapshot.ZoneName = result.ZoneName;
            snapshot.AgreementNames = string.IsNullOrEmpty(agreementNames) ? null : agreementNames;
            snapshot.UnitSummary = unitSummary;
            snapshot.CalculatedTotal = order.CalculatedPrice;
            snapshot.OverrideAmount = order.PriceIsManual ? order.AgreedPrice : null;
            snapshot.OverrideReason = order.PriceIsManual ? order.PriceOverrideReason : null;
            snapshot.OverriddenByUserId = order.PriceIsManual ? _currentUser?.CurrentUserId : null;
            snapshot.OverriddenAtUtc = order.PriceIsManual ? _timeProvider.GetUtcNow().UtcDateTime : null;
            snapshot.Explanation = explanation.Length > 4000 ? explanation[..4000] : explanation;
            snapshot.LinesTotal = linesTotal;
            // Status is deliberately left untouched — a save never resets Draft/Reviewed.
        }

        return null;
    }

    /// <summary>
    /// Combined-unit degression groups (spec §29-31): one group per unloading stop, built from
    /// non-deleted cargo items whose QuantityUnitCode resolves to a managed unit — quantities of
    /// different stops are never merged here (the engine merges per-address itself when the
    /// winning discount's Scope requires it). Returns [] when no cargo item maps to a managed
    /// unit; the caller falls back to a single "order" group from the order-level unit line.
    /// </summary>
    private async Task<IReadOnlyList<PriceCalculationGroup>> BuildPricingGroupsAsync(
        TransportOrder order, IReadOnlyList<CargoItem>? cargoItems, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var items = (cargoItems ?? [])
            .Where(c => !c.IsDeleted && !string.IsNullOrWhiteSpace(c.QuantityUnitCode))
            .ToList();
        if (items.Count == 0)
        {
            return [];
        }

        var codes = items.Select(c => c.QuantityUnitCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unitTypeIdByCode = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId && codes.Contains(u.Code))
            .ToDictionaryAsync(u => u.Code, u => u.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var mapped = items
            .Select(c => (Item: c, UnitTypeId: unitTypeIdByCode.GetValueOrDefault(c.QuantityUnitCode!)))
            .Where(x => x.UnitTypeId != Guid.Empty)
            .ToList();
        if (mapped.Count == 0)
        {
            return [];
        }

        var stopsById = order.Stops.Where(s => !s.IsDeleted).ToDictionary(s => s.Id);
        var groups = new List<PriceCalculationGroup>();
        foreach (var byStop in mapped.GroupBy(x => x.Item.UnloadingStopId))
        {
            var groupKey = byStop.Key?.ToString() ?? "order";
            var label = "Order";
            string? addressKey = null;
            if (byStop.Key is { } stopId && stopsById.TryGetValue(stopId, out var stop))
            {
                label = stop.City ?? stop.LocationName ?? stop.Address ?? "Order";
                addressKey = stop.LocationId is { } locationId
                    ? $"loc:{locationId}"
                    : $"{stop.Address}|{stop.PostalCode}|{stop.City}".ToLowerInvariant();
            }

            var units = byStop.GroupBy(x => x.UnitTypeId)
                .Select(g => new PriceCalculationGroupUnit(g.Key, g.Sum(x => x.Item.ExpectedQuantity)))
                .ToList();
            groups.Add(new PriceCalculationGroup(groupKey, label, units, addressKey));
        }

        return groups;
    }

    /// <summary>Derives a display unit price (amount / quantity) only where that is a real per-unit rate — never invented for bracket/base-amount rule lines.</summary>
    private static decimal? DeriveUnitPrice(string? lineKey, decimal amount, decimal? billableQuantity) =>
        lineKey is not null && lineKey.StartsWith("service:", StringComparison.Ordinal) && billableQuantity is { } q && q != 0
            ? decimal.Round(amount / q, 4)
            : null;

    /// <summary>
    /// Whether the caller is attempting to change a pricing-relevant input while the snapshot is
    /// Locked/Invoiced (spec ch. 24-26 status gate). One-off fields are already mutated onto
    /// <paramref name="order"/> by the time this runs, so they are compared against EF's tracked
    /// OriginalValues; priceIsManual/AgreedPrice/reason are compared against the order's current
    /// (not-yet-overwritten) stored values.
    /// </summary>
    private async Task<bool> PricingInputsChangedAsync(
        TransportOrder order, decimal? requestedAgreedPrice, bool priceIsManual, string? overrideReason,
        IReadOnlyList<PriceServiceInput> serviceSelections, CancellationToken cancellationToken)
    {
        if (priceIsManual != order.PriceIsManual)
        {
            return true;
        }

        if (priceIsManual
            && (NonNegative(requestedAgreedPrice) != order.AgreedPrice || (overrideReason?.Trim() ?? "") != (order.PriceOverrideReason ?? "")))
        {
            return true;
        }

        var entry = _dbContext.Entry(order);
        bool OneOffChanged(string propertyName) => !Equals(entry.OriginalValues[propertyName], entry.CurrentValues[propertyName]);
        if (OneOffChanged(nameof(TransportOrder.PricingSource))
            || OneOffChanged(nameof(TransportOrder.OneOffFixedAmount))
            || OneOffChanged(nameof(TransportOrder.OneOffIncludedLoadingMinutes))
            || OneOffChanged(nameof(TransportOrder.OneOffIncludedUnloadingMinutes))
            || OneOffChanged(nameof(TransportOrder.OneOffIncludedCombinedMinutes))
            || OneOffChanged(nameof(TransportOrder.OneOffExtraHourlyRate))
            || OneOffChanged(nameof(TransportOrder.OneOffNotes)))
        {
            return true;
        }

        var storedServices = await _dbContext.TransportOrderServiceLines
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        return await ServiceSelectionsChangedAsync(order.CustomerId, order.OrderDate, storedServices, serviceSelections, cancellationToken);
    }

    /// <summary>
    /// Compares the requested service selections against the PREVIOUSLY EXPLICIT (i.e. excluding
    /// currently auto-apply-eligible) persisted service lines, so an auto-applied contract service
    /// never makes an unrelated edit look like a "pricing change" while the price is locked.
    /// </summary>
    private async Task<bool> ServiceSelectionsChangedAsync(
        Guid customerId, DateOnly date, IReadOnlyList<TransportOrderServiceLine> stored,
        IReadOnlyList<PriceServiceInput> requested, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var optionIds = stored.Where(s => s.ServiceOptionId is not null).Select(s => s.ServiceOptionId!.Value).Distinct().ToList();
        if (optionIds.Count == 0)
        {
            return requested.Count > 0;
        }

        var autoApplyByOption = await _dbContext.ServiceOptions.AsNoTracking()
            .Where(o => o.TenantId == tenantId && optionIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.AutoApply, cancellationToken);
        var overridesByOption = (await _dbContext.CustomerServiceOptionPrices.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.CustomerId == customerId && optionIds.Contains(p.ServiceOptionId))
                .ToListAsync(cancellationToken))
            .Where(p => (p.EffectiveFrom is null || p.EffectiveFrom <= date) && (p.EffectiveUntil is null || p.EffectiveUntil >= date))
            .ToDictionary(p => p.ServiceOptionId);

        bool IsAutoApplied(Guid optionId) =>
            overridesByOption.TryGetValue(optionId, out var over)
                ? over.AutoApplyOverride ?? autoApplyByOption.GetValueOrDefault(optionId)
                : autoApplyByOption.GetValueOrDefault(optionId);

        var previousExplicit = stored
            .Where(s => s.ServiceOptionId is { } id && !IsAutoApplied(id))
            .Select(s => (s.ServiceOptionId!.Value, s.Quantity))
            .ToHashSet();
        var requestedSet = requested.Select(s => (s.ServiceOptionId, s.Quantity)).ToHashSet();
        return !previousExplicit.SetEquals(requestedSet);
    }

    /// <summary>
    /// Recomputes LinesTotal/AgreedPrice for ALL of the order's current pricing lines — queried
    /// fresh (never just "whatever happens to already be tracked" on this DbContext instance,
    /// which a caller touching only ONE line, e.g. <see cref="ConfirmOrderPriceLineAsync"/>, would
    /// under-count). The query already merges in any pending in-memory edits via EF's identity map
    /// and respects the soft-delete filter, so a just-obsoleted line never lingers in the total. A
    /// brand-new free/manual line Added earlier in the SAME call (not yet saved, so invisible to a
    /// plain query) is picked up separately from the change tracker.
    /// </summary>
    private async Task RecomputeLinesTotalAndAgreedPriceAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var persistedLines = await _dbContext.TransportOrderPricingLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        var pendingNewLines = _dbContext.ChangeTracker.Entries<TransportOrderPricingLine>()
            .Where(e => e.State == EntityState.Added
                        && e.Entity.TenantId == tenantId && e.Entity.TransportOrderId == order.Id)
            .Select(e => e.Entity);
        var currentLines = persistedLines
            .Where(l => _dbContext.Entry(l).State != EntityState.Deleted)
            .Concat(pendingNewLines)
            .ToList();
        var linesTotal = decimal.Round(currentLines.Where(CountsTowardsLinesTotal).Sum(l => l.Amount), 2);

        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == order.Id, cancellationToken);
        if (snapshot is not null)
        {
            snapshot.LinesTotal = linesTotal;
        }

        if (!order.PriceIsManual)
        {
            order.AgreedPrice = linesTotal;
        }
    }

    /// <summary>
    /// Line-level manual corrections/removals/free additions (spec ch. 24-26). Blocked while the
    /// pricing status is Locked/Invoiced.
    /// </summary>
    public async Task<TransportOrderOperationResult> SaveOrderPriceLinesAsync(
        Guid orderId, IReadOnlyList<SaveOrderPriceLineRequest> requests, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await TenantScoped().Include(o => o.Stops).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == orderId, cancellationToken);
        if (snapshot is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            return TransportOrderOperationResult.Invalid(PricingLockedMessage);
        }

        var existingLines = await _dbContext.TransportOrderPricingLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == orderId)
            .ToListAsync(cancellationToken);
        var byKey = existingLines.Where(l => l.LineKey is not null).ToLookup(l => l.LineKey!);
        var maxSequence = existingLines.Count == 0 ? 0 : existingLines.Max(l => l.Sequence);

        var auditBefore = new List<object>();
        var auditAfter = new List<object>();

        foreach (var request in requests)
        {
            if (request.LineKey is null)
            {
                if (string.IsNullOrWhiteSpace(request.Label))
                {
                    return TransportOrderOperationResult.Invalid("Een omschrijving is verplicht voor een vrije regel.");
                }

                var amount = ResolveAmount(request.Quantity, request.UnitPrice, request.Amount);
                if (amount is null)
                {
                    return TransportOrderOperationResult.Invalid("Geef een bedrag op, of een aantal en eenheidsprijs.");
                }

                var newLine = new TransportOrderPricingLine
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
                    Sequence = ++maxSequence, Label = request.Label.Trim(), Amount = decimal.Round(amount.Value, 2),
                    Source = "Manueel", Kind = OrderPriceLineKind.Manual,
                    Quantity = request.Quantity, UnitPrice = request.UnitPrice,
                    AdjustReason = Trim(request.AdjustReason),
                    AdjustedByUserId = _currentUser?.CurrentUserId, AdjustedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    LineKey = $"manual:{Guid.NewGuid()}",
                };
                _dbContext.TransportOrderPricingLines.Add(newLine);
                auditBefore.Add(new { key = (string?)null, label = (string?)null, amount = (decimal?)null });
                auditAfter.Add(new { key = newLine.LineKey, label = newLine.Label, amount = newLine.Amount });
                continue;
            }

            var existing = byKey[request.LineKey].FirstOrDefault();
            if (existing is null)
            {
                return TransportOrderOperationResult.Invalid("De opgegeven prijsregel bestaat niet meer; herbereken de prijs.");
            }

            if (request.Remove)
            {
                auditBefore.Add(new { key = existing.LineKey, label = existing.Label, amount = existing.Amount });
                if (existing.Kind == OrderPriceLineKind.Manual)
                {
                    _dbContext.Remove(existing);
                    auditAfter.Add(new { key = existing.LineKey, removed = true });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(request.AdjustReason))
                {
                    return TransportOrderOperationResult.Invalid("Geef een reden op voor de aanpassing.");
                }

                CaptureOriginalIfFirstAdjustment(existing);
                SetKind(existing, OrderPriceLineKind.AutoAdjusted);
                existing.Amount = 0m;
                existing.AdjustReason = request.AdjustReason.Trim();
                existing.AdjustedByUserId = _currentUser?.CurrentUserId;
                existing.AdjustedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                auditAfter.Add(new { key = existing.LineKey, label = existing.Label, amount = existing.Amount });
                continue;
            }

            auditBefore.Add(new { key = existing.LineKey, label = existing.Label, amount = existing.Amount });

            if (existing.Kind != OrderPriceLineKind.Manual && string.IsNullOrWhiteSpace(request.AdjustReason))
            {
                return TransportOrderOperationResult.Invalid("Geef een reden op voor de aanpassing.");
            }

            if (existing.Kind != OrderPriceLineKind.Manual)
            {
                CaptureOriginalIfFirstAdjustment(existing);
                SetKind(existing, OrderPriceLineKind.AutoAdjusted);
            }

            var effectiveQuantity = request.Quantity ?? existing.Quantity;
            var effectiveUnitPrice = request.UnitPrice ?? existing.UnitPrice;
            var newAmount = request.Amount ?? ResolveAmount(effectiveQuantity, effectiveUnitPrice, null);
            if (newAmount is null)
            {
                return TransportOrderOperationResult.Invalid("Geef een bedrag op, of een aantal en eenheidsprijs.");
            }

            existing.Quantity = effectiveQuantity;
            existing.UnitPrice = effectiveUnitPrice;
            existing.Amount = decimal.Round(newAmount.Value, 2);
            if (!string.IsNullOrWhiteSpace(request.Label))
            {
                existing.Label = request.Label.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.AdjustReason))
            {
                existing.AdjustReason = request.AdjustReason.Trim();
            }

            existing.AdjustedByUserId = _currentUser?.CurrentUserId;
            existing.AdjustedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            auditAfter.Add(new { key = existing.LineKey, label = existing.Label, amount = existing.Amount });
        }

        await RecomputeLinesTotalAndAgreedPriceAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "lines_adjusted", auditBefore, auditAfter, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Amount = explicit Amount, else Round(quantity × unitPrice, 2), else null (never invented).</summary>
    private static decimal? ResolveAmount(decimal? quantity, decimal? unitPrice, decimal? amount) =>
        amount ?? (quantity is { } q && unitPrice is { } p ? decimal.Round(q * p, 2) : (decimal?)null);

    /// <summary>Snapshots Quantity/UnitPrice/Amount into Original* the first time a line is adjusted; a second edit never overwrites the engine baseline.</summary>
    private static void CaptureOriginalIfFirstAdjustment(TransportOrderPricingLine line)
    {
        if (line.Kind != OrderPriceLineKind.AutoAdjusted)
        {
            line.OriginalQuantity = line.Quantity;
            line.OriginalUnitPrice = line.UnitPrice;
            line.OriginalAmount = line.Amount;
        }
    }

    /// <summary>
    /// Sets the manual-editing lifecycle Kind (spec ch. 24-26, the single source of truth) and
    /// keeps the legacy <see cref="TransportOrderPricingLine.Proposed"/> DTO-compat flag in
    /// lockstep — Proposed must always equal exactly (Kind == Proposed), never drift independently
    /// (e.g. a Proposed line manually adjusted here becomes AutoAdjusted and is no longer Proposed).
    /// </summary>
    private static void SetKind(TransportOrderPricingLine line, OrderPriceLineKind kind)
    {
        line.Kind = kind;
        line.Proposed = kind == OrderPriceLineKind.Proposed;
    }

    /// <summary>Explicit re-run of the pricing engine (merge-on-recalc). Blocked while Locked/Invoiced.</summary>
    public async Task<TransportOrderOperationResult> RecalculateOrderPricingAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await TenantScoped().Include(o => o.Stops).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == orderId, cancellationToken);
        if (snapshot is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            return TransportOrderOperationResult.Invalid(PricingLockedMessage);
        }

        var cargoItems = await _dbContext.CargoItems
            .Where(c => c.TenantId == tenantId && c.TransportOrderId == orderId && !c.IsDeleted)
            .ToListAsync(cancellationToken);
        var existingServiceLines = await _dbContext.TransportOrderServiceLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == orderId)
            .ToListAsync(cancellationToken);
        var serviceSelections = existingServiceLines
            .Where(l => l.ServiceOptionId is not null)
            .Select(l => new OrderServiceInput(l.ServiceOptionId!.Value, l.Quantity, l.PalletCount, l.DayCount))
            .ToList();

        var pricingError = await ApplyPricingAsync(
            order, order.AgreedPrice, serviceSelections, order.PriceIsManual, order.PriceOverrideReason, cargoItems, cancellationToken);
        if (pricingError is not null)
        {
            return pricingError;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "recalculated", null,
            new { order.AgreedPrice, order.CalculatedPrice }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Pricing status transition (Draft/Reviewed/Locked); Invoiced is set only by invoice generation.</summary>
    public async Task<TransportOrderOperationResult> SetOrderPricingStatusAsync(
        Guid orderId, OrderPricingStatus target, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await TenantScoped().Include(o => o.Stops).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == orderId, cancellationToken);
        if (snapshot is null)
        {
            return TransportOrderOperationResult.Invalid("Er is nog geen prijsberekening voor deze order.");
        }

        if (target == OrderPricingStatus.Invoiced)
        {
            return TransportOrderOperationResult.Invalid("Facturatiestatus wordt door facturatie gezet.");
        }

        if (snapshot.Status == OrderPricingStatus.Invoiced)
        {
            return TransportOrderOperationResult.Invalid("De status van een gefactureerde prijs kan niet meer wijzigen.");
        }

        if (!PricingStatusTransitions[snapshot.Status].Contains(target))
        {
            return TransportOrderOperationResult.InvalidState($"Prijsstatus '{snapshot.Status}' kan niet naar '{target}'.");
        }

        // Touching Locked in either direction (lock or unlock) requires the dedicated permission;
        // the Draft<->Reviewed pair only needs the ordinary edit permission.
        var requiresLockPermission = target == OrderPricingStatus.Locked || snapshot.Status == OrderPricingStatus.Locked;
        var userId = _currentUser?.CurrentUserId;
        var allowed = _permissionService is null
            || (userId is { } uid
                && (await _permissionService.UserHasPermissionAsync(
                        uid, requiresLockPermission ? PermissionCodes.OrdersLockPrice : PermissionCodes.OrdersEdit, cancellationToken)
                    || await _permissionService.UserHasPermissionAsync(uid, PermissionCodes.OrdersManage, cancellationToken)));
        if (!allowed)
        {
            return TransportOrderOperationResult.Invalid("Je hebt geen rechten voor deze statuswijziging.");
        }

        var before = new { snapshot.Status };
        snapshot.Status = target;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "status_changed", before, new { snapshot.Status }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Confirms an unconfirmed (Proposed) extra-time line so it counts in LinesTotal/AgreedPrice.</summary>
    public async Task<TransportOrderOperationResult> ConfirmOrderPriceLineAsync(
        Guid orderId, Guid lineId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await TenantScoped().Include(o => o.Stops).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        var snapshot = await _dbContext.TransportOrderPricingSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TransportOrderId == orderId, cancellationToken);
        if (snapshot is { Status: OrderPricingStatus.Locked or OrderPricingStatus.Invoiced })
        {
            return TransportOrderOperationResult.Invalid(PricingLockedMessage);
        }

        var line = await _dbContext.TransportOrderPricingLines
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.TransportOrderId == orderId && l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (line.Kind != OrderPriceLineKind.Proposed)
        {
            return TransportOrderOperationResult.Invalid("Alleen een voorstel kan worden bevestigd.");
        }

        SetKind(line, OrderPriceLineKind.Auto);

        await RecomputeLinesTotalAndAgreedPriceAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "line_confirmed",
            new { line.Id, line.Label }, new { line.Amount }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>
    /// Sums measured loading/unloading minutes from this order's stop executions (tenant-filtered,
    /// via the order's own stops), for the included-time extra-time proposal (spec Phase 6). Only
    /// stops with a recorded arrival AND a completion/departure count — an in-progress stop
    /// contributes nothing yet. Null when no stop of that type has a measurable execution.
    /// </summary>
    private async Task<(decimal? LoadingMinutes, decimal? UnloadingMinutes)> ComputeActualStopMinutesAsync(
        TransportOrder order, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var stopTypeById = order.Stops.Where(s => !s.IsDeleted).ToDictionary(s => s.Id, s => s.StopType);
        if (stopTypeById.Count == 0)
        {
            return (null, null);
        }

        var stopIds = stopTypeById.Keys.ToList();
        // Carried-over review item: a Failed or Skipped execution still gets CompletedAt/DepartedAt
        // stamped by the driver workflow, but that dwell time was never actually billable work —
        // exclude both statuses from the actual-minutes sums that feed extra-time proposals.
        var executions = await _dbContext.Set<Modules.Planning.Entities.StopExecution>().AsNoTracking()
            .Where(e => e.TenantId == tenantId && stopIds.Contains(e.TransportOrderStopId) && e.ArrivedAt != null
                        && e.Status != Modules.Planning.Entities.StopExecutionStatus.Failed
                        && e.Status != Modules.Planning.Entities.StopExecutionStatus.Skipped)
            .Select(e => new { e.TransportOrderStopId, e.ArrivedAt, e.CompletedAt, e.DepartedAt })
            .ToListAsync(cancellationToken);

        decimal? loadingMinutes = null;
        decimal? unloadingMinutes = null;
        foreach (var execution in executions)
        {
            var end = execution.DepartedAt ?? execution.CompletedAt;
            if (end is null || execution.ArrivedAt is not { } arrived
                || !stopTypeById.TryGetValue(execution.TransportOrderStopId, out var stopType))
            {
                continue;
            }

            var minutes = (decimal)(end.Value - arrived).TotalMinutes;
            if (stopType == StopType.Loading)
            {
                loadingMinutes = (loadingMinutes ?? 0m) + minutes;
            }
            else
            {
                unloadingMinutes = (unloadingMinutes ?? 0m) + minutes;
            }
        }

        return (loadingMinutes, unloadingMinutes);
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
