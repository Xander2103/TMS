using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
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
    /// Cancelled is deliberately absent as a TARGET: cancelling is a separate action
    /// (CancelAsync) with its own permission and a mandatory reason.
    /// EVERY <see cref="TransportOrderStatus"/> member must have an entry here — statuses set by
    /// other modules (Invoiced) reach both ChangeStatusAsync and MapDetailAsync, and a missing
    /// key used to surface as a 500 on plain GETs (wave 1 blocker C-04). Both readers use
    /// TryGetValue as a second line of defence; OrderInvoicedStatusTests guards the coverage.
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
            // Terminal for the manual workflow: unwinding an invoice runs through invoicing.
            [TransportOrderStatus.Invoiced] = [],
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
    private readonly IOpeningHoursEvaluator _openingHoursEvaluator;

    /// <summary>Lazily resolved tenant zone (see <see cref="ResolveTenantTimeZoneAsync"/>); the
    /// service is request-scoped, so this caches for exactly one request.</summary>
    private TimeZoneInfo? _tenantTimeZone;

    public TransportOrderService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IPricingEngine? pricingEngine = null,
        ICurrentUserContext? currentUser = null,
        IPermissionAuthorizationService? permissionService = null,
        INotificationEventService? notificationEvents = null,
        ILogger<TransportOrderService>? logger = null,
        IOpeningHoursEvaluator? openingHoursEvaluator = null)
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
        // The evaluator is a stateless pure function; defaulting keeps existing call sites/tests working.
        _openingHoursEvaluator = openingHoursEvaluator ?? new OpeningHoursEvaluator();
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
            request.Quantity, request.QuantityUnitCode ?? request.QuantityUnit,
            hasCargoLines: request.CargoItems is { Count: > 0 },
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

        if (IncludedTimeOverrideError(
                request.PricingSource, request.IncludedLoadingMinutesOverride, request.IncludedUnloadingMinutesOverride,
                request.ExtraTimeHourlyRateOverride, request.ExtraTimeRoundingStepMinutes, request.ExtraTimeMinimumBillableMinutes)
            is { } includedTimeOverrideError)
        {
            return TransportOrderOperationResult.Invalid(includedTimeOverrideError);
        }

        // Dossier containment is prepared BEFORE anything is staged on the context: the lazy
        // activity-type seed runs its own SaveChanges, which must never flush half an order.
        Modules.Dossiers.Entities.TransportDossier? targetDossier = null;
        Modules.Dossiers.Entities.ActivityType? wrapperTransportType = null;
        if (request.DossierId is { } requestedDossierId)
        {
            targetDossier = await _dbContext.TransportDossiers
                .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == requestedDossierId, cancellationToken);
            if (targetDossier is null)
            {
                return TransportOrderOperationResult.InvalidReference("Het opgegeven dossier bestaat niet.");
            }

            if (targetDossier.Status == Modules.Dossiers.Entities.DossierStatus.Closed)
            {
                return TransportOrderOperationResult.InvalidState(
                    "Dit dossier is gesloten; heropen het dossier voor je een opdracht toevoegt.");
            }
        }
        else
        {
            wrapperTransportType = await ResolveDefaultTransportActivityTypeAsync(cancellationToken);
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        // Master-location stops get their location data snapshotted at creation (Phase 7).
        var stops = await BuildStopsAsync(request.Stops, cancellationToken);

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
            DistanceKm = NonNegative(request.DistanceKm),
            LoadingMeters = NonNegative(request.LoadingMeters),
            PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null,
            AdrRequired = request.AdrRequired,
            CraneRequired = request.CraneRequired,
            PlateauRequired = request.PlateauRequired,
            MoffettRequired = request.MoffettRequired,
            IsReturnMovement = request.IsReturnMovement,
            Priority = request.Priority ?? OrderPriority.Normal,
            AgreedPrice = NonNegative(request.AgreedPrice),
            Notes = Trim(request.Notes),
            Stops = stops,
        };
        ApplyOneOffPricing(order, request.PricingSource, request.OneOffFixedAmount,
            request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes,
            request.OneOffExtraHourlyRate, request.OneOffNotes);
        ApplyIncludedTimeOverrides(order,
            request.IncludedLoadingMinutesOverride, request.IncludedUnloadingMinutesOverride,
            request.ExtraTimeHourlyRateOverride, request.ExtraTimeRoundingStepMinutes, request.ExtraTimeMinimumBillableMinutes);
        ApplyDieselSurchargeOverride(order,
            request.DieselSurchargeOverride, request.DieselSurchargePercentOverride, request.DieselSurchargeOverrideReason);
        // Selling entity: explicit request value else the customer's default entity.
        order.LegalEntityId = await ResolveOrderLegalEntityAsync(request.LegalEntityId, request.CustomerId, cancellationToken);

        var cargoItems = BuildCargoItems(order.Id, request.CargoItems, order.Stops);
        DeriveSummaryFromCargo(order, cargoItems);
        if (await ApplyPricingAsync(order, request.AgreedPrice, ResolveServiceSelections(request.Services, request.ServiceOptionIds),
                request.PriceIsManual, request.PriceOverrideReason, cargoItems, cancellationToken,
                // P6: the wrapper activity is staged AFTER pricing (pricing failures must leave
                // the tracker clean), so its already-resolved type is passed as an explicit hint.
                activityTypeHint: wrapperTransportType?.Id) is { } pricingError)
        {
            return pricingError;
        }

        // Dossier containment: an order created inside a dossier is linked to it; every other
        // create (EDI, portal, legacy API) gets its own wrapper dossier in the SAME save, so
        // no order exists outside a dossier and no caller has to change.
        Modules.Dossiers.Entities.TransportDossier? wrapperDossier = null;
        if (targetDossier is not null)
        {
            _dbContext.Add(new Modules.Dossiers.Entities.DossierOrder
            {
                Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId,
                DossierId = targetDossier.Id, TransportOrderId = order.Id,
            });
            targetDossier.Version = Guid.NewGuid();
        }
        else
        {
            wrapperDossier = new Modules.Dossiers.Entities.TransportDossier
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                Title = "wordt hieronder gezet", // assigned with the claimed numbers below
                CustomerId = order.CustomerId,
                CustomerReference = order.CustomerReference,
                LegalEntityId = order.LegalEntityId,
                DossierDate = order.OrderDate,
                OriginTransportOrderId = order.Id,
            };
            _dbContext.Add(wrapperDossier);
            if (wrapperTransportType is not null)
            {
                _dbContext.Add(new Modules.Dossiers.Entities.DossierActivity
                {
                    Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, DossierId = wrapperDossier.Id,
                    ActivityTypeId = wrapperTransportType.Id, Sequence = 1, LinkedTransportOrderId = order.Id,
                });
            }

            _dbContext.Add(new Modules.Dossiers.Entities.DossierOrder
            {
                Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId,
                DossierId = wrapperDossier.Id, TransportOrderId = order.Id,
            });
        }

        var customerNameForTitle = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

        _dbContext.Add(order);
        _dbContext.AddRange(cargoItems);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () =>
            {
                order.OrderNumber = GenerateOrderNumber(settings);
                if (wrapperDossier is not null)
                {
                    wrapperDossier.DossierNumber = settings is null
                        ? $"DOS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
                        : $"{settings.DossierNumberPrefix}{settings.DossierNumberNextValue++:0000}";
                    var title = customerNameForTitle is null
                        ? order.OrderNumber
                        : $"{order.OrderNumber} — {customerNameForTitle}";
                    wrapperDossier.Title = title.Length > 200 ? title[..200] : title;
                }
            },
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

    public async Task<OrderLegalEntityChangeImpactDto?> PreviewLegalEntityChangeAsync(
        Guid id, Guid legalEntityId, CancellationToken cancellationToken)
    {
        var order = await TenantScoped().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var (impact, _) = await LoadLegalEntityChangeAsync(order, legalEntityId, cancellationToken);
        return impact;
    }

    public async Task<TransportOrderOperationResult> ChangeLegalEntityAsync(
        Guid id, ChangeOrderLegalEntityRequest request, CancellationToken cancellationToken)
    {
        var order = await TenantScoped()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return TransportOrderOperationResult.NotFound;
        }

        if (request.Version is { } expectedVersion && expectedVersion != order.Version)
        {
            return TransportOrderOperationResult.Conflict(await MapDetailAsync(order, cancellationToken));
        }

        if (request.LegalEntityId == order.LegalEntityId)
        {
            return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
        }

        var (impact, draftLines) = await LoadLegalEntityChangeAsync(order, request.LegalEntityId, cancellationToken);
        if (impact.BlockedReason is { } blocked)
        {
            return TransportOrderOperationResult.InvalidState(blocked);
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (impact.DeviatesFromCustomerDefault)
        {
            if (impact.RequiresOverridePermission)
            {
                return TransportOrderOperationResult.Invalid(
                    "Je hebt geen rechten om deze order naar een andere entiteit dan de klantstandaard te verplaatsen.");
            }

            if (reason is null)
            {
                return TransportOrderOperationResult.Invalid("Een reden is verplicht bij een afwijkende facturerende entiteit.");
            }
        }

        var previousEntityId = order.LegalEntityId;
        // Draft coherence: a concept invoice belongs to ONE entity, so lines of this order on a
        // concept of the old entity are released (the sent-invoice case was refused above).
        // Audit fix: an order on a concept invoice carries Status = Invoiced; once its lines are
        // released it is handed back to Completed so it can be invoiced again under the new entity.
        _dbContext.InvoiceLines.RemoveRange(draftLines);
        if (draftLines.Count > 0 && order.Status == TransportOrderStatus.Invoiced)
        {
            order.Status = TransportOrderStatus.Completed;
            await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
        }
        order.LegalEntityId = request.LegalEntityId;
        order.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "LegalEntityChanged",
            new { LegalEntityId = previousEntityId },
            new { order.LegalEntityId, Reason = reason, DraftInvoiceLinesReleased = draftLines.Count },
            cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    public async Task<string?> ChangeLegalEntityWithinDossierAsync(
        Guid id, Guid legalEntityId, string? reason, CancellationToken cancellationToken)
    {
        var order = await TenantScoped().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return "De order bestaat niet.";
        }

        if (order.LegalEntityId == legalEntityId)
        {
            return null;
        }

        var (impact, draftLines) = await LoadLegalEntityChangeAsync(order, legalEntityId, cancellationToken);
        if (impact.BlockedReason is { } blocked)
        {
            return blocked;
        }

        var previousEntityId = order.LegalEntityId;
        _dbContext.InvoiceLines.RemoveRange(draftLines);
        if (draftLines.Count > 0 && order.Status == TransportOrderStatus.Invoiced)
        {
            order.Status = TransportOrderStatus.Completed;
            await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
        }
        order.LegalEntityId = legalEntityId;
        order.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "LegalEntityChanged",
            new { LegalEntityId = previousEntityId },
            new { order.LegalEntityId, Reason = reason, DraftInvoiceLinesReleased = draftLines.Count, ViaDossier = true },
            cancellationToken);
        return null;
    }

    private async Task<(OrderLegalEntityChangeImpactDto Impact, List<Modules.Invoicing.Entities.InvoiceLine> DraftLines)>
        LoadLegalEntityChangeAsync(TransportOrder order, Guid legalEntityId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        string? blocked = null;

        // Financial safety is decided by the INVOICE state, not the order flag: an order on a
        // concept invoice (Status = Invoiced) may still move — its concept lines are released —
        // while any order on a sent/booked invoice is refused below.
        if (order.Status is TransportOrderStatus.Cancelled)
        {
            blocked = "Een geannuleerde opdracht kan niet van facturerende entiteit wijzigen.";
        }
        else if (!await _dbContext.LegalEntities.AnyAsync(
                     e => e.TenantId == tenantId && e.Id == legalEntityId && e.IsActive, cancellationToken))
        {
            blocked = "De gekozen facturerende entiteit bestaat niet of is niet actief.";
        }
        else if (await _dbContext.InvoiceLines.AsNoTracking()
                     .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
                     .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.TenantId == tenantId),
                         line => line.InvoiceId, invoice => invoice.Id, (line, invoice) => invoice)
                     .AnyAsync(i => i.Status != Modules.Invoicing.Entities.InvoiceStatus.Draft, cancellationToken))
        {
            blocked = "Deze opdracht staat op een verzonden of geboekte factuur; de entiteit kan niet meer wijzigen. "
                      + "Corrigeer via een creditnota; de historische factuur blijft ongewijzigd.";
        }
        else if (await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                     _dbContext, tenantId, order.CustomerId, legalEntityId, cancellationToken) is { } policyError)
        {
            blocked = policyError;
        }

        var customerDefault = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == order.CustomerId)
            .Select(c => c.DefaultLegalEntityId)
            .FirstOrDefaultAsync(cancellationToken);
        var deviates = customerDefault is null || legalEntityId != customerDefault;

        // Fail-closed: no wired authorization service means NO override rights.
        var userId = _currentUser?.CurrentUserId;
        var mayOverride = deviates
            && _permissionService is not null
            && userId is { } uid
            && await _permissionService.UserHasPermissionAsync(uid, PermissionCodes.DossiersOverrideEntity, cancellationToken);

        var draftInvoiceIds = await _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == Modules.Invoicing.Entities.InvoiceStatus.Draft
                        && i.LegalEntityId != legalEntityId)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        var draftLines = await _dbContext.InvoiceLines
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id && draftInvoiceIds.Contains(l.InvoiceId))
            .ToListAsync(cancellationToken);

        return (new OrderLegalEntityChangeImpactDto(
            order.Id, order.LegalEntityId, legalEntityId, customerDefault,
            deviates, deviates && !mayOverride, blocked, draftLines.Count), draftLines);
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

        // Optimistic concurrency (Trip pattern): a stale token yields 409 with the CURRENT
        // state so the client rebases instead of silently overwriting a colleague's changes.
        // Null (legacy/EDI/portal callers) skips the check.
        if (request.Version is { } expectedVersion && expectedVersion != order.Version)
        {
            return TransportOrderOperationResult.Conflict(await MapDetailAsync(order, cancellationToken));
        }

        // Wave 1 blocker C-02: a plain header edit may NEVER move an order to another customer or
        // invoicing entity. Both have dedicated flows (OrderCustomerChangeService.ApplyAsync /
        // ChangeLegalEntityAsync) that demand a reason, re-evaluate pricing and the entity policy,
        // release draft invoice lines and audit the move. CustomerId stays on the request for
        // backwards compatibility, but it must ECHO the stored value — a different value is
        // refused rather than silently ignored. Mirrors DossierService.UpdateAsync.
        if (request.CustomerId != order.CustomerId)
        {
            return TransportOrderOperationResult.Invalid(
                "Gebruik 'Klant wijzigen' om de klant van een bestaande opdracht aan te passen; "
                + "prijzen, facturatie-entiteit en gekoppelde facturen worden dan mee herbeoordeeld.");
        }

        // Null keeps the current entity (older clients never send it); an explicit DIFFERENT
        // entity belongs in the dedicated flow, which also handles the override right, the
        // sent/booked-invoice guard and the release of draft invoice lines.
        if (request.LegalEntityId is { } requestedEntityId && requestedEntityId != order.LegalEntityId)
        {
            return TransportOrderOperationResult.Invalid(
                "Gebruik 'Entiteit wijzigen' om de facturerende entiteit van een bestaande opdracht aan te passen; "
                + "openstaande conceptfacturen worden dan mee herbeoordeeld.");
        }

        // Switching an order TO a blocked customer is refused; editing an existing order whose
        // customer became blocked afterwards stays possible (dispatch still needs to manage it).
        // Null CargoItems means "leave unchanged" (API contract), so the minimal-cargo rule falls
        // back to the currently persisted lines in that case.
        var hasCargoLines = request.CargoItems is not null
            ? request.CargoItems.Count > 0
            : await _dbContext.CargoItems.AsNoTracking()
                .AnyAsync(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id, cancellationToken);
        // The guard above proves request.CustomerId == order.CustomerId, so this edit is never
        // "new work for another customer": the blocked/deactivated intake gate does not apply.
        var validation = await ValidateAsync(order.CustomerId, request.CustomerReference, request.GoodsDescription,
            request.Quantity, request.QuantityUnitCode ?? request.QuantityUnit, hasCargoLines,
            request.Stops, enforceCustomerIntake: false, cancellationToken);
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

        if (IncludedTimeOverrideError(
                request.PricingSource, request.IncludedLoadingMinutesOverride, request.IncludedUnloadingMinutesOverride,
                request.ExtraTimeHourlyRateOverride, request.ExtraTimeRoundingStepMinutes, request.ExtraTimeMinimumBillableMinutes)
            is { } includedTimeOverrideError)
        {
            return TransportOrderOperationResult.Invalid(includedTimeOverrideError);
        }

        // C-01: decide AND validate the whole stop plan while nothing has been mutated yet, so a
        // refusal (removing/retyping an operationally referenced stop, a duplicate echoed id)
        // leaves the tracked entity exactly as it was loaded. The plan is executed further down,
        // after the header fields — it is the last guard of the fail-before-mutate block.
        var (stopPlan, stopPlanError) = await PlanStopSyncAsync(order, request.Stops, cancellationToken);
        if (stopPlanError is not null)
        {
            return stopPlanError;
        }

        // Cargo: id-matched in-place sync (loaded up front so both the audit "before" snapshot
        // and the sync below share one query). null = leave unchanged (API contract); [] = clear.
        var existingCargo = await _dbContext.CargoItems
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);
        var cargoBefore = existingCargo
            .Select(c => new { c.Description, c.ExpectedQuantity, c.QuantityUnitCode }).ToList();

        var before = new {
            order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count, Cargo = cargoBefore,
            StopRequirements = SummarizeStopRequirements(order.Stops),
            order.IncludedLoadingMinutesOverride, order.IncludedUnloadingMinutesOverride,
            order.ExtraTimeHourlyRateOverride, order.ExtraTimeRoundingStepMinutes, order.ExtraTimeMinimumBillableMinutes,
        };

        // CustomerId is deliberately NOT assigned here (C-02): the guard above already proved the
        // request echoes the stored value, and the only sanctioned way to move an order is
        // OrderCustomerChangeService.
        order.CustomerReference = Trim(request.CustomerReference);
        order.OrderDate = request.OrderDate ?? order.OrderDate;
        order.GoodsDescription = Trim(request.GoodsDescription);
        order.Quantity = NonNegative(request.Quantity);
        order.QuantityUnit = Trim(request.QuantityUnit);
        order.QuantityUnitCode = NormalizeUnitCode(request.QuantityUnitCode);
        order.WeightKg = NonNegative(request.WeightKg);
        order.VolumeM3 = NonNegative(request.VolumeM3);
        order.DistanceKm = NonNegative(request.DistanceKm);
        order.LoadingMeters = NonNegative(request.LoadingMeters);
        order.PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null;
        order.AdrRequired = request.AdrRequired;
        order.CraneRequired = request.CraneRequired;
        order.PlateauRequired = request.PlateauRequired;
        order.MoffettRequired = request.MoffettRequired;
        order.IsReturnMovement = request.IsReturnMovement;
        // Null = unchanged, so older clients that don't send a priority never reset it.
        order.Priority = request.Priority ?? order.Priority;
        order.Notes = Trim(request.Notes);
        ApplyOneOffPricing(order, request.PricingSource, request.OneOffFixedAmount,
            request.OneOffIncludedLoadingMinutes, request.OneOffIncludedUnloadingMinutes, request.OneOffIncludedCombinedMinutes,
            request.OneOffExtraHourlyRate, request.OneOffNotes);
        ApplyIncludedTimeOverrides(order,
            request.IncludedLoadingMinutesOverride, request.IncludedUnloadingMinutesOverride,
            request.ExtraTimeHourlyRateOverride, request.ExtraTimeRoundingStepMinutes, request.ExtraTimeMinimumBillableMinutes);

        var surchargeBefore = new { order.DieselSurchargeOverride, order.DieselSurchargePercentOverride };
        ApplyDieselSurchargeOverride(order,
            request.DieselSurchargeOverride, request.DieselSurchargePercentOverride, request.DieselSurchargeOverrideReason);
        var surchargeChanged = surchargeBefore.DieselSurchargeOverride != order.DieselSurchargeOverride
            || surchargeBefore.DieselSurchargePercentOverride != order.DieselSurchargePercentOverride;

        // LegalEntityId is deliberately NOT assigned here (C-02): moving an order to another
        // invoicing entity runs through ChangeLegalEntityAsync, which owns the override-right
        // check, the entity policy, the sent/booked-invoice guard, the draft-line release and the
        // LegalEntityChanged audit entry. The guard at the top of this method proved the request
        // either omits the field or echoes the stored value.

        // Identity-preserving stop sync (C-01) — executing the plan validated above, so this
        // phase can no longer refuse anything.
        await ApplyStopSyncAsync(order, request.Stops, stopPlan!, cancellationToken);

        List<CargoItem> replacementCargo;
        if (request.CargoItems is not null)
        {
            var byId = existingCargo.ToDictionary(c => c.Id);
            var seen = new HashSet<Guid>();
            var sequence = 1;
            replacementCargo = new List<CargoItem>(request.CargoItems.Count);
            foreach (var input in request.CargoItems)
            {
                if (input.Id is { } lineId && byId.TryGetValue(lineId, out var entity))
                {
                    ApplyCargoInput(entity, input, sequence++, order.Stops);
                    seen.Add(lineId);
                    replacementCargo.Add(entity);
                }
                else
                {
                    var added = BuildCargoItem(order.Id, input, sequence++, order.Stops);
                    _dbContext.Add(added);
                    replacementCargo.Add(added);
                }
            }
            _dbContext.RemoveRange(existingCargo.Where(c => !seen.Contains(c.Id)));
        }
        else
        {
            // null = leave cargo unchanged; still feed the (unmodified) current lines to pricing.
            // A link to a stop that SURVIVED the sync stays exactly as it was (C-01); only a
            // dangling link (its stop was removed, or a legacy id-less client replaced the whole
            // stop set) is re-resolved — soft delete never fires the SetNull FK.
            RelinkCargoToSurvivingStops(existingCargo, order.Stops);
            replacementCargo = existingCargo;
        }

        DeriveSummaryFromCargo(order, replacementCargo);
        if (await ApplyPricingAsync(order, request.AgreedPrice, ResolveServiceSelections(request.Services, request.ServiceOptionIds),
                request.PriceIsManual, request.PriceOverrideReason, replacementCargo, cancellationToken) is { } pricingError)
        {
            return pricingError;
        }

        // Wave 2 §6: pricing/coverage may have changed — keep the readiness projection current.
        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var cargoAfter = replacementCargo
            .Select(c => new { c.Description, c.ExpectedQuantity, c.QuantityUnitCode }).ToList();
        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Updated", before,
            new {
                order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count, Cargo = cargoAfter,
                StopRequirements = SummarizeStopRequirements(order.Stops),
                order.IncludedLoadingMinutesOverride, order.IncludedUnloadingMinutesOverride,
                order.ExtraTimeHourlyRateOverride, order.ExtraTimeRoundingStepMinutes, order.ExtraTimeMinimumBillableMinutes,
            }, cancellationToken);

        // Deliberate "Opnieuw overnemen van locatie": one order-level audit entry per refreshed
        // stop (inputs and rebuilt stops align by index).
        for (var i = 0; i < request.Stops.Count; i++)
        {
            if (request.Stops[i] is { RefreshSnapshot: true, LocationId: not null })
            {
                var stop = order.Stops[i];
                await _auditService.RecordAsync(EntityType, order.Id.ToString(), "StopSnapshotRefreshed", null,
                    new { stop.Sequence, stop.LocationName, stop.SnapshotAt }, cancellationToken);
            }
        }

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

            // Wave 2 (spec Part O): an explicit entity outside the customer's allowed set is a
            // validation error naming the allowed entities. The inherited default below needs no
            // check — CustomerService keeps the default inside the set.
            if (await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                    _dbContext, _tenantContext.TenantId, customerId, id, cancellationToken) is { } policyError)
            {
                throw new Common.DomainValidationException("legalEntityId", policyError);
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

    /// <summary>
    /// Validates order-level included-time overrides (Task 10): all five values must be
    /// non-negative when provided, and the feature is exclusive to contract pricing — a one-off
    /// order carries its own included-time fields (see <see cref="OneOffPricingError"/>) and never
    /// consults the engaged agreement these overrides target.
    /// </summary>
    private static string? IncludedTimeOverrideError(
        OrderPricingSource pricingSource,
        int? includedLoadingOverride, int? includedUnloadingOverride, decimal? extraHourlyRateOverride,
        int? roundingStepMinutes, int? minimumBillableMinutes)
    {
        if (includedLoadingOverride is < 0 || includedUnloadingOverride is < 0 || extraHourlyRateOverride is < 0
            || roundingStepMinutes is < 0 || minimumBillableMinutes is < 0)
        {
            return "Afwijkende laad-/lostijdwaarden mogen niet negatief zijn.";
        }

        var anySet = includedLoadingOverride is not null || includedUnloadingOverride is not null
            || extraHourlyRateOverride is not null || roundingStepMinutes is not null || minimumBillableMinutes is not null;
        if (anySet && pricingSource == OrderPricingSource.OneOff)
        {
            return "Laad-/lostijdafwijkingen gelden alleen bij contractprijzen; gebruik de eenmalige prijsvelden.";
        }

        return null;
    }

    /// <summary>Sets the order's included-time overrides (Task 10) verbatim; null means "use the agreement's own value".</summary>
    private static void ApplyIncludedTimeOverrides(
        TransportOrder order,
        int? includedLoadingOverride, int? includedUnloadingOverride, decimal? extraHourlyRateOverride,
        int? roundingStepMinutes, int? minimumBillableMinutes)
    {
        order.IncludedLoadingMinutesOverride = includedLoadingOverride;
        order.IncludedUnloadingMinutesOverride = includedUnloadingOverride;
        order.ExtraTimeHourlyRateOverride = extraHourlyRateOverride;
        order.ExtraTimeRoundingStepMinutes = roundingStepMinutes;
        order.ExtraTimeMinimumBillableMinutes = minimumBillableMinutes;
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

        if (!Transitions.TryGetValue(order.Status, out var allowedTargets) || !allowedTargets.Contains(target))
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
        // Wave 2 §6: readiness is a projection of the current state — recompute on every
        // status change (Completed evaluates the rules; anything else resets to NotReady).
        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
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

        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
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

        var cargo = await _dbContext.CargoItems
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(cargo); // interceptor converts to IsDeleted = true

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
        decimal? quantity, string? quantityUnit, bool hasCargoLines,
        IReadOnlyList<TransportOrderStopInput> stops, bool enforceCustomerIntake,
        CancellationToken cancellationToken)
    {
        // Minimal cargo information (wave 2026-08-04 §3): quantity + unit, a commercial goods
        // line, or a general description — any one is enough. Descriptions are never required
        // when a meaningful quantity exists.
        var hasMeaningfulCargo = (quantity is > 0 && !string.IsNullOrWhiteSpace(quantityUnit))
            || hasCargoLines
            || !string.IsNullOrWhiteSpace(goodsDescription);
        if (!hasMeaningfulCargo)
        {
            return TransportOrderOperationResult.Invalid(
                "Vul minstens een hoeveelheid en eenheid in, voeg een goederenlijn toe of beschrijf de goederen.");
        }

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

            // §18: a stop-level included-time override may not be negative.
            if (stop.IncludedTimeMinutesOverride is < 0)
            {
                return TransportOrderOperationResult.Invalid(
                    "De afwijkende inbegrepen tijd van een stop mag niet negatief zijn.");
            }

            // §15: the simple time requirement needs the time(s) its kind uses.
            switch (stop.TimeRequirement)
            {
                case StopTimeRequirementKind.Before when stop.TimeRequirementTo is null:
                    return TransportOrderOperationResult.Invalid(
                        "Geef het uur op waarvóór deze stop moet gebeuren.");
                case StopTimeRequirementKind.After when stop.TimeRequirementFrom is null:
                    return TransportOrderOperationResult.Invalid(
                        "Geef het uur op waarvóór deze stop niet mag gebeuren.");
                case StopTimeRequirementKind.Window
                    when stop.TimeRequirementFrom is null || stop.TimeRequirementTo is null:
                    return TransportOrderOperationResult.Invalid(
                        "Geef het volledige tijdvenster (van en tot) van deze stop op.");
                case StopTimeRequirementKind.Window
                    when stop.TimeRequirementTo <= stop.TimeRequirementFrom:
                    return TransportOrderOperationResult.Invalid(
                        "Het einde van het tijdvenster moet na het begin liggen.");
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

    /// <summary>
    /// Validates the cargo list: positive quantity per item, barcode unambiguous within the order.
    /// Line descriptions are optional (see <see cref="ValidateAsync"/> for the "at least one
    /// description somewhere" rule).
    /// </summary>
    private static string? CargoItemsError(IReadOnlyList<CargoItemInput>? items, IReadOnlyList<TransportOrderStopInput> stops)
    {
        if (items is null || items.Count == 0)
        {
            return null;
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

        // A repeated Id would otherwise apply two inputs to the same tracked line during the
        // update sync (one silently lost) and double-count that line in pricing.
        var ids = items.Select(i => i.Id).Where(id => id is not null).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            return "Dezelfde goederenlijn mag maar één keer voorkomen.";
        }

        return null;
    }

    private List<CargoItem> BuildCargoItems(Guid orderId, IReadOnlyList<CargoItemInput>? inputs, IReadOnlyList<TransportOrderStop> stops) =>
        (inputs ?? []).Select((input, index) => BuildCargoItem(orderId, input, index + 1, stops)).ToList();

    /// <summary>
    /// Commercial cargo lines are the source of truth when they exist (wave 2026-08-04 §2): the
    /// order-level summary is derived from them, never independently edited, so summary and lines
    /// can no longer contradict each other. Facet by facet: quantity+unit collapse to the single
    /// shared managed unit (multi-unit orders keep a null header pair — the summary lives in the
    /// lines); weight/volume/pallets replace the header value as soon as any line carries that
    /// measure. Orders without lines keep the legacy hand-entered header fields.
    /// </summary>
    private static void DeriveSummaryFromCargo(TransportOrder order, IReadOnlyList<CargoItem> cargoItems)
    {
        var lines = cargoItems.Where(c => !c.IsDeleted).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        if (lines.All(c => !string.IsNullOrWhiteSpace(c.QuantityUnitCode)))
        {
            var codes = lines
                .Select(c => c.QuantityUnitCode!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (codes.Count == 1)
            {
                order.Quantity = lines.Sum(c => c.ExpectedQuantity);
                order.QuantityUnitCode = codes[0];
                order.QuantityUnit = null;
            }
            else
            {
                order.Quantity = null;
                order.QuantityUnitCode = null;
                order.QuantityUnit = null;
            }
        }
        // Lines without managed codes: the header pair stays as the legacy fallback.

        if (lines.Any(c => c.TotalWeightKg is not null))
        {
            order.WeightKg = lines.Sum(c => c.TotalWeightKg ?? 0m);
        }

        if (lines.Any(c => c.VolumeM3 is not null))
        {
            // Audit fix (Wave 1 §12): line VolumeM3 is per stuk, so the header total multiplies by
            // the expected quantity (lines without a volume are skipped). Weight needs no factor —
            // TotalWeightKg is already a line total.
            order.VolumeM3 = lines
                .Where(c => c.VolumeM3 is not null)
                .Sum(c => c.VolumeM3!.Value * c.ExpectedQuantity);
        }

        if (lines.Any(c => c.PalletCount is not null))
        {
            // Ceiling: a started pallet place occupies a whole one commercially.
            order.PalletCount = (int)Math.Ceiling(lines.Sum(c => c.PalletCount ?? 0m));
        }
    }

    private CargoItem BuildCargoItem(Guid orderId, CargoItemInput input, int sequence, IReadOnlyList<TransportOrderStop> stops)
    {
        var item = new CargoItem
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TransportOrderId = orderId,
        };
        ApplyCargoInput(item, input, sequence, stops);
        return item;
    }

    /// <summary>
    /// Sets every mutable cargo field from an input, shared by create (via <see cref="BuildCargoItem"/>)
    /// and the id-preserving update sync. Unambiguous orders (one loading + one unloading stop)
    /// auto-link omitted stop indexes.
    /// </summary>
    private static void ApplyCargoInput(CargoItem target, CargoItemInput input, int sequence, IReadOnlyList<TransportOrderStop> stops)
    {
        var (defaultLoading, defaultUnloading) = DefaultCargoStopLinks(stops);

        var (volume, volumeIsManual) = Modules.Fleet.Services.FleetFieldRules.ResolveVolume(
            input.LengthMeters, input.WidthMeters, input.HeightMeters, input.VolumeM3, input.VolumeIsManual,
            field: $"cargoItems[{sequence - 1}].volumeM3");

        target.Sequence = sequence;
        target.Description = Trim(input.Description);
        target.Barcode = Trim(input.Barcode);
        target.ExpectedQuantity = input.ExpectedQuantity;
        target.QuantityUnit = Trim(input.QuantityUnit);
        target.QuantityUnitCode = NormalizeUnitCode(input.QuantityUnitCode);
        target.Notes = Trim(input.Notes);
        target.UnitType = input.UnitType;
        target.UnitTypeLabel = Trim(input.UnitTypeLabel);
        target.TotalWeightKg = NonNegative(input.TotalWeightKg);
        target.WeightPerUnitKg = NonNegative(input.WeightPerUnitKg);
        target.LengthMeters = NonNegative(input.LengthMeters);
        target.WidthMeters = NonNegative(input.WidthMeters);
        target.HeightMeters = NonNegative(input.HeightMeters);
        target.VolumeM3 = volume;
        target.VolumeIsManual = volumeIsManual;
        target.AdrRequired = input.AdrRequired;
        target.AdrDetails = Trim(input.AdrDetails);
        target.Stackable = input.Stackable;
        target.Reference = Trim(input.Reference);
        target.PalletCount = NonNegative(input.PalletCount);
        target.LoadingStopId = input.LoadingStopIndex is { } load ? stops[load].Id : defaultLoading;
        target.UnloadingStopId = input.UnloadingStopIndex is { } unload ? stops[unload].Id : defaultUnloading;
    }

    /// <summary>
    /// Unambiguous-order auto-link rule shared by <see cref="ApplyCargoInput"/> and the
    /// leave-unchanged cargo relink below: exactly one loading + one unloading stop auto-links;
    /// otherwise the link is left to the caller (null by default).
    /// </summary>
    private static (Guid? DefaultLoading, Guid? DefaultUnloading) DefaultCargoStopLinks(IReadOnlyList<TransportOrderStop> stops)
    {
        var loadingStops = stops.Where(s => s.StopType == StopType.Loading).ToList();
        var unloadingStops = stops.Where(s => s.StopType == StopType.Unloading).ToList();
        return (
            loadingStops.Count == 1 ? loadingStops[0].Id : (Guid?)null,
            unloadingStops.Count == 1 ? unloadingStops[0].Id : (Guid?)null);
    }

    /// <summary>
    /// Repairs DANGLING cargo stop links when CargoItems is omitted (leave-unchanged). Soft
    /// delete never fires the SetNull FK, so a link to a removed — or, for legacy clients that
    /// don't echo stop ids, wholesale-replaced — stop would keep pointing at a hidden row.
    /// A link to a stop that survived the sync is left untouched (C-01: stop identity is
    /// preserved, so churning the link would be pure damage). Dangling links fall back to the
    /// same unambiguous-order auto-link rule as ApplyCargoInput/BuildCargoItem: exactly one
    /// loading + one unloading stop auto-links, otherwise the link is cleared.
    /// </summary>
    private static void RelinkCargoToSurvivingStops(IEnumerable<CargoItem> cargoItems, IReadOnlyList<TransportOrderStop> stops)
    {
        // A link is only kept when its stop survived AND still plays the matching role: a stop
        // that was retyped (allowed while nothing references it) can no longer be the goods'
        // loading stop.
        var survivingLoading = stops.Where(s => s.StopType == StopType.Loading).Select(s => s.Id).ToHashSet();
        var survivingUnloading = stops.Where(s => s.StopType == StopType.Unloading).Select(s => s.Id).ToHashSet();
        var (defaultLoading, defaultUnloading) = DefaultCargoStopLinks(stops);
        foreach (var item in cargoItems)
        {
            if (item.LoadingStopId is not { } loadingId || !survivingLoading.Contains(loadingId))
            {
                item.LoadingStopId = defaultLoading;
            }

            if (item.UnloadingStopId is not { } unloadingId || !survivingUnloading.Contains(unloadingId))
            {
                item.UnloadingStopId = defaultUnloading;
            }
        }
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

    /// <summary>Readable per-stop time-requirement summary for the Updated audit trail (§19).</summary>
    private static List<string> SummarizeStopRequirements(IEnumerable<TransportOrderStop> stops) =>
        stops
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Sequence)
            .Select(s => s.TimeRequirement switch
            {
                StopTimeRequirementKind.Before => $"{s.StopType}: vóór {s.TimeRequirementTo:HH\\:mm}",
                StopTimeRequirementKind.After => $"{s.StopType}: na {s.TimeRequirementFrom:HH\\:mm}",
                StopTimeRequirementKind.Window => $"{s.StopType}: {s.TimeRequirementFrom:HH\\:mm}–{s.TimeRequirementTo:HH\\:mm}",
                _ => $"{s.StopType}: geen tijdseis",
            } + (s.AppointmentRequired ? " (afspraak verplicht)" : string.Empty))
            .ToList();

    private List<TransportOrderStop> BuildStops(IReadOnlyList<TransportOrderStopInput> inputs) =>
        inputs.Select((input, index) =>
        {
            var stop = new TransportOrderStop { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
            ApplyStopInput(stop, input, index + 1);
            return stop;
        }).ToList();

    /// <summary>
    /// Sets every CLIENT-EXPRESSIBLE field of one stop from its input, shared by create (via
    /// <see cref="BuildStops"/>) and the id-preserving update sync. The location-snapshot fields
    /// are deliberately untouched here — they are resolved afterwards
    /// (<see cref="ApplyLocationSnapshot"/> / <see cref="CarryOverSnapshot"/>).
    /// </summary>
    private static void ApplyStopInput(TransportOrderStop stop, TransportOrderStopInput input, int sequence)
    {
        stop.Sequence = sequence;
        stop.StopType = input.StopType;
        stop.LocationId = input.LocationId;
        stop.LocationName = Trim(input.LocationName);
        stop.Address = Trim(input.Address);
        stop.PostalCode = Trim(input.PostalCode);
        stop.City = Trim(input.City);
        stop.CountryCode = Trim(input.CountryCode)?.ToUpperInvariant();
        stop.PlannedFrom = input.PlannedFrom;
        stop.PlannedTo = input.PlannedTo;
        stop.RequestedFrom = input.RequestedFrom;
        stop.RequestedTo = input.RequestedTo;
        stop.ConfirmedFrom = input.ConfirmedFrom;
        stop.ConfirmedTo = input.ConfirmedTo;
        stop.EarliestAllowed = input.EarliestAllowed;
        stop.LatestAllowed = input.LatestAllowed;
        stop.AppointmentRequired = input.AppointmentRequired;
        stop.AppointmentReference = Trim(input.AppointmentReference);
        // §15: only the fields the chosen kind actually uses are stored — a leftover time
        // from a previously chosen kind must never linger.
        stop.TimeRequirement = input.TimeRequirement;
        stop.TimeRequirementFrom = input.TimeRequirement is StopTimeRequirementKind.After or StopTimeRequirementKind.Window
            ? input.TimeRequirementFrom
            : null;
        stop.TimeRequirementTo = input.TimeRequirement is StopTimeRequirementKind.Before or StopTimeRequirementKind.Window
            ? input.TimeRequirementTo
            : null;
        stop.IncludedTimeMinutesOverride = input.IncludedTimeMinutesOverride;
        stop.Reference = Trim(input.Reference);
        stop.Instructions = Trim(input.Instructions);
        stop.AccessInstructions = Trim(input.AccessInstructions);
        stop.LoadingInstructions = Trim(input.LoadingInstructions);
        stop.UnloadingInstructions = Trim(input.UnloadingInstructions);
    }

    /// <summary>
    /// Detached copy of the fields the snapshot resolution reads back (address quintet,
    /// instructions and the frozen location snapshot), taken BEFORE the input overwrites them.
    /// </summary>
    private static TransportOrderStop CaptureStopSnapshot(TransportOrderStop stop) => new()
    {
        Id = stop.Id,
        LocationId = stop.LocationId,
        LocationName = stop.LocationName,
        Address = stop.Address,
        PostalCode = stop.PostalCode,
        City = stop.City,
        CountryCode = stop.CountryCode,
        ContactName = stop.ContactName,
        ContactPhone = stop.ContactPhone,
        ContactMobile = stop.ContactMobile,
        ContactEmail = stop.ContactEmail,
        OpeningHoursSummary = stop.OpeningHoursSummary,
        Gate = stop.Gate,
        AccessCode = stop.AccessCode,
        Dock = stop.Dock,
        RouteDescription = stop.RouteDescription,
        DefaultLoadingMinutes = stop.DefaultLoadingMinutes,
        DefaultUnloadingMinutes = stop.DefaultUnloadingMinutes,
        SnapshotAt = stop.SnapshotAt,
        Instructions = stop.Instructions,
        AccessInstructions = stop.AccessInstructions,
        LoadingInstructions = stop.LoadingInstructions,
        UnloadingInstructions = stop.UnloadingInstructions,
    };

    /// <summary>
    /// Resets the snapshot-only fields of a preserved stop so it starts the snapshot resolution
    /// in exactly the state a freshly built row would: a free-address stop keeps no location
    /// snapshot, and a carry-over/refresh re-fills them deliberately.
    /// </summary>
    private static void ClearLocationSnapshot(TransportOrderStop stop)
    {
        stop.ContactName = null;
        stop.ContactPhone = null;
        stop.ContactMobile = null;
        stop.ContactEmail = null;
        stop.OpeningHoursSummary = null;
        stop.Gate = null;
        stop.AccessCode = null;
        stop.Dock = null;
        stop.RouteDescription = null;
        stop.DefaultLoadingMinutes = null;
        stop.DefaultUnloadingMinutes = null;
        stop.SnapshotAt = null;
    }

    /// <summary>
    /// Operational references pinned to a stop. HARD references record something that actually
    /// happened at that stop (executions, POD, ETA promises, scans, exceptions, package events,
    /// incidents) — detaching them destroys the trail, and three of them carry a NON-nullable FK,
    /// so removing the stop would be outright corruption. PACKAGE PINS are best-effort links the
    /// scan pipeline falls back on; they may still be released while the order is not confirmed.
    /// </summary>
    private sealed record StopReferences(HashSet<Guid> Hard, HashSet<Guid> PackagePinned)
    {
        public bool IsReferenced(Guid stopId) => Hard.Contains(stopId) || PackagePinned.Contains(stopId);
    }

    private async Task<StopReferences> LoadStopReferencesAsync(
        IReadOnlyCollection<Guid> stopIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (stopIds.Count == 0)
        {
            return new StopReferences([], []);
        }

        var ids = stopIds.ToList();
        var hard = new HashSet<Guid>();

        hard.UnionWith(await _dbContext.StopExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.TransportOrderStopId))
            .Select(e => e.TransportOrderStopId).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TenantId == tenantId && ids.Contains(p.TransportOrderStopId))
            .Select(p => p.TransportOrderStopId).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.StopEtas.AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.TransportOrderStopId))
            .Select(e => e.TransportOrderStopId).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.ScanEvents.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TransportOrderStopId != null && ids.Contains(s.TransportOrderStopId!.Value))
            .Select(s => s.TransportOrderStopId!.Value).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TransportOrderStopId != null && ids.Contains(e.TransportOrderStopId!.Value))
            .Select(e => e.TransportOrderStopId!.Value).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.PackageEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TransportOrderStopId != null && ids.Contains(e.TransportOrderStopId!.Value))
            .Select(e => e.TransportOrderStopId!.Value).Distinct().ToListAsync(cancellationToken));
        hard.UnionWith(await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.SourceStopId != null && ids.Contains(i.SourceStopId!.Value))
            .Select(i => i.SourceStopId!.Value).Distinct().ToListAsync(cancellationToken));

        var pinned = await _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                        && ((p.LoadingStopId != null && ids.Contains(p.LoadingStopId!.Value))
                            || (p.DeliveryStopId != null && ids.Contains(p.DeliveryStopId!.Value))))
            .Select(p => new { p.LoadingStopId, p.DeliveryStopId })
            .ToListAsync(cancellationToken);
        var packagePinned = new HashSet<Guid>();
        foreach (var pin in pinned)
        {
            if (pin.LoadingStopId is { } l && stopIds.Contains(l))
            {
                packagePinned.Add(l);
            }

            if (pin.DeliveryStopId is { } d && stopIds.Contains(d))
            {
                packagePinned.Add(d);
            }
        }

        return new StopReferences(hard, packagePinned);
    }

    /// <summary>
    /// The decided outcome of matching a request's stop inputs onto an order's existing stops.
    /// Produced by <see cref="PlanStopSyncAsync"/> (read-only, fully validated) and consumed by
    /// <see cref="ApplyStopSync"/> (mutating). Splitting the two is what lets
    /// <see cref="UpdateAsync"/> refuse a stop plan BEFORE it has touched the tracked entity.
    /// </summary>
    /// <param name="Matched">Per input index: the existing stop it addresses, or null for a new one.</param>
    /// <param name="Removed">Existing stops no input echoes; to be soft-deleted.</param>
    /// <param name="PackagePinsToRelease">
    /// Ids of removed stops whose (best-effort) package pins must be set to null — only ever
    /// populated while the order is not yet Confirmed.
    /// </param>
    private sealed record StopSyncPlan(
        TransportOrderStop?[] Matched,
        List<TransportOrderStop> Removed,
        List<Guid> PackagePinsToRelease);

    /// <summary>
    /// Wave 1 blocker C-01 — decides and VALIDATES the identity-preserving stop sync without
    /// mutating anything. Stop identity:
    /// <list type="bullet">
    /// <item>an input echoing an <c>Id</c> that belongs to THIS order is the SAME stop and will be
    /// updated in place (id, and therefore every package pin, execution, POD, scan, ETA and
    /// exception pointing at it, survives);</item>
    /// <item>an input without an id — or with an id this order does not own, including one of
    /// another order or tenant — is a NEW stop and always gets a freshly generated id (a client
    /// can never adopt someone else's row);</item>
    /// <item>the same id echoed twice in one request is ambiguous and refused outright — the
    /// client cannot mean one row twice, and guessing at stop identity is exactly what this
    /// blocker exists to stop;</item>
    /// <item>an existing stop no input echoes is REMOVED (soft-deleted), unless it is still
    /// operationally referenced;</item>
    /// <item>changing the <c>StopType</c> of a referenced stop is a replacement in identity terms
    /// and is refused — the packages/executions hanging off it describe a load, not a delivery.</item>
    /// </list>
    /// Returns the failure result when a rule refuses the edit, otherwise the plan.
    /// </summary>
    private async Task<(StopSyncPlan? Plan, TransportOrderOperationResult? Error)> PlanStopSyncAsync(
        TransportOrder order, IReadOnlyList<TransportOrderStopInput> inputs, CancellationToken cancellationToken)
    {
        var existing = order.Stops.Where(s => !s.IsDeleted).ToDictionary(s => s.Id);

        var matched = new TransportOrderStop?[inputs.Count];
        var claimed = new HashSet<Guid>();
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Id is not { } echoedId || !existing.TryGetValue(echoedId, out var stop))
            {
                continue; // no id, or an id this order does not own → a new stop.
            }

            if (!claimed.Add(echoedId))
            {
                return (null, TransportOrderOperationResult.Invalid(
                    $"Stop {stop.Sequence} komt meermaals voor in deze aanvraag; elke stop mag maar één keer worden meegestuurd."));
            }

            matched[i] = stop;
        }

        var removed = existing.Values.Where(s => !claimed.Contains(s.Id)).ToList();
        var retyped = Enumerable.Range(0, inputs.Count)
            .Where(i => matched[i] is { } m && m.StopType != inputs[i].StopType)
            .Select(i => matched[i]!)
            .ToList();

        var pinsToRelease = new List<Guid>();
        if (removed.Count > 0 || retyped.Count > 0)
        {
            var references = await LoadStopReferencesAsync(
                removed.Concat(retyped).Select(s => s.Id).ToHashSet(), cancellationToken);

            foreach (var stop in removed)
            {
                // Hard references pin the stop unconditionally; package pins may still be
                // released while the order is not yet confirmed (the scan pipeline documents a
                // null pin as the fallback), but never on a physically bound order.
                if (references.Hard.Contains(stop.Id)
                    || (references.PackagePinned.Contains(stop.Id) && order.Status == TransportOrderStatus.Confirmed))
                {
                    return (null, TransportOrderOperationResult.Invalid(
                        $"Stop {stop.Sequence} is al operationeel in gebruik (colli, uitvoering, aflevering of scans) "
                        + "en kan niet meer worden verwijderd."));
                }
            }

            foreach (var stop in retyped)
            {
                if (references.IsReferenced(stop.Id))
                {
                    return (null, TransportOrderOperationResult.Invalid(
                        $"Het type van stop {stop.Sequence} kan niet meer worden gewijzigd: "
                        + "er hangen al colli, uitvoeringen of scans aan deze stop."));
                }
            }

            pinsToRelease.AddRange(removed.Select(s => s.Id).Where(references.PackagePinned.Contains));
        }

        return (new StopSyncPlan(matched, removed, pinsToRelease), null);
    }

    /// <summary>
    /// Executes a validated <see cref="StopSyncPlan"/>: releases the package pins of removed
    /// stops (Draft/Submitted only — the plan never lists any otherwise), updates the preserved
    /// stops in place, creates the new ones, renumbers, resolves the location snapshots and stages
    /// the removals as soft deletes.
    /// </summary>
    private async Task ApplyStopSyncAsync(
        TransportOrder order, IReadOnlyList<TransportOrderStopInput> inputs, StopSyncPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.PackagePinsToRelease.Count > 0)
        {
            var tenantId = _tenantContext.TenantId;
            var releasable = plan.PackagePinsToRelease;
            var packages = await _dbContext.Packages
                .Where(p => p.TenantId == tenantId
                            && ((p.LoadingStopId != null && releasable.Contains(p.LoadingStopId!.Value))
                                || (p.DeliveryStopId != null && releasable.Contains(p.DeliveryStopId!.Value))))
                .ToListAsync(cancellationToken);
            foreach (var package in packages)
            {
                if (package.LoadingStopId is { } l && releasable.Contains(l))
                {
                    package.LoadingStopId = null;
                }

                if (package.DeliveryStopId is { } d && releasable.Contains(d))
                {
                    package.DeliveryStopId = null;
                }
            }
        }

        // Snapshot the preserved rows BEFORE the inputs overwrite them, so the carry-over rules
        // (Phase 7) keep working unchanged for an unchanged master-location stop.
        var previousStops = plan.Matched.Where(s => s is not null)
            .ToDictionary(s => s!.Id, s => CaptureStopSnapshot(s!));

        var stops = new List<TransportOrderStop>(inputs.Count);
        var added = new List<TransportOrderStop>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var stop = plan.Matched[i];
            if (stop is null)
            {
                // Never reuse the client-supplied id: an unknown id is a NEW stop, not an adoption.
                stop = new TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, TransportOrderId = order.Id,
                };
                added.Add(stop);
            }
            else
            {
                ClearLocationSnapshot(stop);
            }

            ApplyStopInput(stop, inputs[i], i + 1);
            stops.Add(stop);
        }

        await ResolveStopSnapshotsAsync(stops, inputs, previousStops, cancellationToken);

        // Removals first (explicit soft delete), so reassigning the navigation never leaves EF to
        // guess what happened to the dropped rows.
        _dbContext.RemoveRange(plan.Removed);
        order.Stops = stops;
        // New rows carry service-generated ids; navigation discovery would attach them as
        // Modified (phantom UPDATE). Mark them Added explicitly.
        _dbContext.AddRange(added);
    }

    /// <summary>
    /// CREATE path: builds brand-new stop rows and takes each one's location snapshot fresh from
    /// the tenant's master location (there is no previous row to carry anything over from — the
    /// update path owns that case, via <see cref="ApplyStopSyncAsync"/>).
    /// </summary>
    private async Task<List<TransportOrderStop>> BuildStopsAsync(
        IReadOnlyList<TransportOrderStopInput> inputs, CancellationToken cancellationToken)
    {
        var stops = BuildStops(inputs);
        await ResolveStopSnapshotsAsync(stops, inputs, previousStops: null, cancellationToken);
        return stops;
    }

    /// <summary>
    /// Resolves each stop's location snapshot (Phase 7), index-aligned with its input: an
    /// unchanged master-location stop (echoed id, same LocationId, no RefreshSnapshot) carries
    /// the previous snapshot over; a new/changed/refreshed one takes a fresh copy from the
    /// tenant's location (single batched query). Free-address stops are untouched.
    /// </summary>
    private async Task ResolveStopSnapshotsAsync(
        IReadOnlyList<TransportOrderStop> stops,
        IReadOnlyList<TransportOrderStopInput> inputs,
        IReadOnlyDictionary<Guid, TransportOrderStop>? previousStops,
        CancellationToken cancellationToken)
    {
        var freshLocationIds = inputs
            .Where(input => NeedsFreshSnapshot(input, previousStops))
            .Select(input => input.LocationId!.Value)
            .Distinct()
            .ToList();
        var locations = freshLocationIds.Count == 0
            ? new Dictionary<Guid, Location>()
            : await _dbContext.Locations.AsNoTracking()
                .Include(l => l.OpeningIntervals)
                .Where(l => l.TenantId == _tenantContext.TenantId && freshLocationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (input.LocationId is not { } locationId)
            {
                continue; // free-address stop: inline fields only, no snapshot.
            }

            if (NeedsFreshSnapshot(input, previousStops))
            {
                if (locations.TryGetValue(locationId, out var location))
                {
                    ApplyLocationSnapshot(stops[i], location);
                }
            }
            else if (previousStops!.TryGetValue(input.Id!.Value, out var previous))
            {
                CarryOverSnapshot(stops[i], previous);
            }
        }
    }

    /// <summary>Fresh copy needed for: new stop, changed LocationId, or an explicit refresh.</summary>
    private static bool NeedsFreshSnapshot(
        TransportOrderStopInput input, IReadOnlyDictionary<Guid, TransportOrderStop>? previousStops)
    {
        if (input.LocationId is null)
        {
            return false;
        }

        if (input.RefreshSnapshot)
        {
            return true;
        }

        return previousStops is null
            || input.Id is not { } id
            || !previousStops.TryGetValue(id, out var previous)
            || previous.LocationId != input.LocationId;
    }

    /// <summary>
    /// Copies the master location onto the stop: the address quintet and operational snapshot
    /// fields are REPLACED (the snapshot is the agreed address); instruction fields are only
    /// filled where the input left them empty — user-entered instructions always win.
    /// </summary>
    private void ApplyLocationSnapshot(TransportOrderStop stop, Location location)
    {
        stop.LocationName = location.Name;
        var addressLine = string.Join(" ",
            new[] { location.Street, location.HouseNumber }.Where(p => !string.IsNullOrWhiteSpace(p)));
        stop.Address = string.IsNullOrWhiteSpace(addressLine) ? null : addressLine;
        stop.PostalCode = Trim(location.PostalCode);
        stop.City = Trim(location.City);
        stop.CountryCode = Trim(location.CountryCode)?.ToUpperInvariant();

        stop.ContactName = Trim(location.ContactName);
        stop.ContactPhone = Trim(location.ContactPhone);
        stop.ContactMobile = Trim(location.ContactMobile);
        stop.ContactEmail = Trim(location.ContactEmail);
        // Structured hours first; the legacy free-text field is the display fallback.
        var hoursSummary = OpeningHoursFormatter.Summarize(location.OpeningIntervals) ?? Trim(location.OpeningHours);
        stop.OpeningHoursSummary = hoursSummary is { Length: > 500 } ? hoursSummary[..500] : hoursSummary;
        stop.Gate = Trim(location.Gate);
        stop.AccessCode = Trim(location.AccessCode);
        stop.Dock = Trim(location.Dock);
        stop.RouteDescription = Trim(location.RouteDescription);
        stop.DefaultLoadingMinutes = location.DefaultLoadingMinutes;
        stop.DefaultUnloadingMinutes = location.DefaultUnloadingMinutes;
        // OR-ed in once at snapshot time; afterwards the stop keeps the user's own override.
        stop.AppointmentRequired |= location.AppointmentRequired;
        stop.SnapshotAt = _timeProvider.GetUtcNow().UtcDateTime;

        stop.Instructions ??= Trim(location.DriverInstructions);
        stop.AccessInstructions ??= Trim(location.AccessInstructions);
        stop.LoadingInstructions ??= Trim(location.LoadingInstructions);
        stop.UnloadingInstructions ??= Trim(location.UnloadingInstructions);
    }

    /// <summary>
    /// Rebuilt-but-unchanged stop: the previous snapshot rides along. Fields the input CAN
    /// express (address quintet, instructions) keep input-wins semantics; snapshot-only fields
    /// are copied verbatim.
    /// </summary>
    private static void CarryOverSnapshot(TransportOrderStop stop, TransportOrderStop previous)
    {
        stop.ContactName = previous.ContactName;
        stop.ContactPhone = previous.ContactPhone;
        stop.ContactMobile = previous.ContactMobile;
        stop.ContactEmail = previous.ContactEmail;
        stop.OpeningHoursSummary = previous.OpeningHoursSummary;
        stop.Gate = previous.Gate;
        stop.AccessCode = previous.AccessCode;
        stop.Dock = previous.Dock;
        stop.RouteDescription = previous.RouteDescription;
        stop.DefaultLoadingMinutes = previous.DefaultLoadingMinutes;
        stop.DefaultUnloadingMinutes = previous.DefaultUnloadingMinutes;
        stop.SnapshotAt = previous.SnapshotAt;

        stop.LocationName ??= previous.LocationName;
        stop.Address ??= previous.Address;
        stop.PostalCode ??= previous.PostalCode;
        stop.City ??= previous.City;
        stop.CountryCode ??= previous.CountryCode;
        stop.Instructions ??= previous.Instructions;
        stop.AccessInstructions ??= previous.AccessInstructions;
        stop.LoadingInstructions ??= previous.LoadingInstructions;
        stop.UnloadingInstructions ??= previous.UnloadingInstructions;
    }

    private static readonly string[] DutchDayNames =
        ["maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag", "zondag"];

    /// <summary>
    /// The tenant's IANA zone (<c>TenantSettings.Timezone</c>) as a <see cref="TimeZoneInfo"/>,
    /// resolved at most once per service instance (= per request).
    /// </summary>
    /// <remarks>
    /// The one transport-time convention (C-03): stored/wire values are UTC instants, everything a
    /// human types or reads is tenant wall clock. Opening hours are stored as local wall clock
    /// (<see cref="TimeOnly"/>), so any comparison between the two has to pass through this zone.
    /// Resolution goes through <see cref="TenantTimeZone"/>, the single resolver in the API.
    /// </remarks>
    private async Task<TimeZoneInfo> ResolveTenantTimeZoneAsync(CancellationToken cancellationToken) =>
        _tenantTimeZone ??= await TenantTimeZone.ForTenantAsync(_dbContext, _tenantContext.TenantId, cancellationToken);

    /// <summary>
    /// Advisory (never blocking) opening-hours warnings for one stop. Evaluated against the
    /// LIVE location intervals on purpose: the warning answers "will the site be open at the
    /// planned time", while the snapshot answers "what did we agree back then".
    /// </summary>
    /// <param name="zone">Tenant zone; the planned UTC instants are projected onto it before they
    /// are compared with the location's local opening hours (C-03). Null when the caller resolved
    /// no zone because the order has no master-location stop — which is also the only case in
    /// which no warning can exist, so the two conditions are checked together.</param>
    private IReadOnlyList<string>? BuildOpeningHoursWarnings(TransportOrderStop stop, Location? location, TimeZoneInfo? zone)
    {
        if (location is null || zone is null || location.OpeningIntervals.Count == 0)
        {
            return null; // Free-address stop or no structured hours (NoData) → no warning.
        }

        var activity = stop.StopType == StopType.Loading ? "laadtijd" : "lostijd";
        var name = stop.LocationName ?? location.Name;
        List<string>? warnings = null;
        foreach (var moment in new[] { stop.PlannedFrom, stop.PlannedTo })
        {
            if (moment is not { } planned)
            {
                continue;
            }

            var local = TenantTimeZone.ToWallClock(planned, zone);

            // A lone LOCAL midnight "from" is the wire encoding of a date-only stop (no time
            // chosen: the client sends 00:00 tenant time, which is 22:00Z / 23:00Z on the wire);
            // warning about 00:00 would be noise.
            if (local.TimeOfDay == TimeSpan.Zero && stop.PlannedTo is null)
            {
                continue;
            }

            var time = TimeOnly.FromDateTime(local);
            var check = _openingHoursEvaluator.Check(location.OpeningIntervals, local.DayOfWeek, time);
            var message = check.Status switch
            {
                OpeningHoursStatus.BeforeOpening or OpeningHoursStatus.AfterClosing =>
                    $"De geplande {activity} van {time:HH\\:mm} valt buiten de openingsuren " +
                    $"({OpeningHoursFormatter.FormatDayIntervals(check.DayIntervals)}) van {name}.",
                OpeningHoursStatus.ClosedDay =>
                    $"De geplande {activity} op {DutchDayNames[local.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)local.DayOfWeek - 1]} valt op een sluitingsdag van {name}.",
                _ => null,
            };
            if (message is not null)
            {
                warnings ??= [];
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }
        }

        return warnings;
    }

    private async Task<TransportOrderDetailDto> MapDetailAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var customerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == order.CustomerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        // Live locations serve two remaining purposes: the code (identifier, not snapshot data)
        // + opening-hours warnings, and a LEGACY fallback for stop rows created before the
        // snapshot backfill whose inline fields are still null. Snapshot fields always win.
        var locationIds = order.Stops.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        var locations = locationIds.Count == 0
            ? []
            : await _dbContext.Locations.AsNoTracking()
                .Include(l => l.OpeningIntervals)
                .Where(l => l.TenantId == _tenantContext.TenantId && locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);

        // Sensitive access codes only surface for locations.view_sensitive holders (fail-closed).
        var canViewSensitiveAccess = await CurrentUserHasAnyAsync(cancellationToken, PermissionCodes.LocationsViewSensitive);

        // Opening-hours warnings compare a UTC instant with LOCAL opening hours, so they need the
        // tenant zone (C-03). Only a stop with a master location can produce one; with no such
        // stop there is nothing to resolve, and the null travels into the guard rather than a
        // placeholder zone that would be silently wrong if the guard ever moved.
        TimeZoneInfo? tenantZone = locations.Count == 0
            ? null
            : await ResolveTenantTimeZoneAsync(cancellationToken);

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
                    s.LocationName ?? location?.Name ?? s.City ?? string.Empty,
                    s.Address ?? (string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress),
                    s.PostalCode ?? location?.PostalCode,
                    s.City ?? location?.City,
                    s.CountryCode ?? location?.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
                    s.RequestedFrom, s.RequestedTo, s.ConfirmedFrom, s.ConfirmedTo,
                    s.EarliestAllowed, s.LatestAllowed,
                    s.AppointmentRequired, s.AppointmentReference,
                    s.AccessInstructions, s.LoadingInstructions, s.UnloadingInstructions,
                    s.TimeRequirement, s.TimeRequirementFrom, s.TimeRequirementTo,
                    s.IncludedTimeMinutesOverride,
                    ContactName: s.ContactName,
                    ContactPhone: s.ContactPhone,
                    ContactMobile: s.ContactMobile,
                    ContactEmail: s.ContactEmail,
                    OpeningHoursSummary: s.OpeningHoursSummary,
                    Gate: s.Gate,
                    AccessCode: canViewSensitiveAccess ? s.AccessCode : null,
                    Dock: s.Dock,
                    RouteDescription: s.RouteDescription,
                    DefaultLoadingMinutes: s.DefaultLoadingMinutes,
                    DefaultUnloadingMinutes: s.DefaultUnloadingMinutes,
                    SnapshotAt: s.SnapshotAt,
                    Warnings: BuildOpeningHoursWarnings(s, location, tenantZone));
            })
            .ToList();

        var cargoItems = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
            .OrderBy(c => c.Sequence)
            .Select(c => new CargoItemDto(
                c.Id, c.Sequence, c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes,
                c.UnitType, c.UnitTypeLabel, c.TotalWeightKg, c.WeightPerUnitKg,
                c.LengthMeters, c.WidthMeters, c.HeightMeters, c.VolumeM3, c.VolumeIsManual,
                c.AdrRequired, c.AdrDetails, c.Stackable, c.Reference, c.LoadingStopId, c.UnloadingStopId,
                c.QuantityUnitCode, c.PalletCount))
            .ToListAsync(cancellationToken);

        var pricingLines = await _dbContext.TransportOrderPricingLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.Sequence)
            .Select(l => new OrderPricingLineDto(
                l.Label, l.Amount, l.Source, l.Informational,
                l.RuleName, l.AgreementName, l.ActualQuantity, l.BillableQuantity, l.Proposed,
                l.Id, l.Kind, l.Quantity, l.UnitPrice, l.OriginalQuantity, l.OriginalUnitPrice, l.OriginalAmount,
                l.AdjustReason, l.LineKey, l.Unit, l.ServiceOptionId))
            .ToListAsync(cancellationToken);
        // Recomputed from the persisted lines (never separately snapshotted) so it can never drift
        // from CalculatedPrice/pricingLines — a proposed extra-time charge is never invoiceable on its own.
        var totalWithProposed = order.CalculatedPrice is { } calculatedTotal
            ? calculatedTotal + pricingLines.Where(l => l.Proposed && !l.Informational).Sum(l => l.Amount)
            : (decimal?)null;
        var snapshotEntity = await _dbContext.TransportOrderPricingSnapshots.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.TransportOrderId == order.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var pricingSnapshot = snapshotEntity is null
            ? null
            : new OrderPricingSnapshotDto(
                snapshotEntity.TariffDate, snapshotEntity.Currency, snapshotEntity.ZoneCode, snapshotEntity.ZoneName,
                snapshotEntity.AgreementNames, snapshotEntity.UnitSummary, snapshotEntity.CalculatedTotal,
                snapshotEntity.OverrideAmount, snapshotEntity.OverrideReason, snapshotEntity.OverriddenByUserId,
                snapshotEntity.OverriddenAtUtc, snapshotEntity.Explanation, snapshotEntity.Status, snapshotEntity.LinesTotal,
                Coverage: DeserializeCoverage(snapshotEntity.CoverageJson),
                ConfirmedAtUtc: snapshotEntity.ConfirmedAtUtc,
                ConfirmedByUserId: snapshotEntity.ConfirmedByUserId,
                ConfirmedByName: snapshotEntity.ConfirmedByName,
                ConfirmedWithUnpricedGoodsReason: snapshotEntity.ConfirmedWithUnpricedGoodsReason,
                CoverageStatus: snapshotEntity.CoverageStatus,
                IsStale: snapshotEntity.IsStale);
        var serviceLines = await _dbContext.TransportOrderServiceLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.NameSnapshot)
            .Select(l => new OrderServiceLineDto(l.ServiceOptionId, l.NameSnapshot, l.Kind, l.Value, l.Amount, l.Quantity, l.PalletCount, l.DayCount, l.Note))
            .ToListAsync(cancellationToken);

        // Containing dossier for the header chip: the order's own wrapper wins, else the
        // first (oldest) user-created link.
        var dossierRef = await _dbContext.TransportDossiers.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && d.OriginTransportOrderId == order.Id)
                .Select(d => new { d.Id, d.DossierNumber })
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
                .OrderBy(l => l.CreatedAt)
                .Join(_dbContext.TransportDossiers.AsNoTracking(), l => l.DossierId, d => d.Id,
                    (l, d) => new { d.Id, d.DossierNumber })
                .FirstOrDefaultAsync(cancellationToken);

        return new TransportOrderDetailDto(
            order.Id, order.OrderNumber, order.OrderDate, order.CustomerId, customerName,
            order.CustomerReference, order.Status, order.GoodsDescription,
            order.Quantity, order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount,
            order.AdrRequired, order.CraneRequired, order.AgreedPrice, order.Notes,
            order.CancellationReason,
            stops, cargoItems,
            Transitions.TryGetValue(order.Status, out var allowedTransitions) ? allowedTransitions : [],
            CancellableStatuses.Contains(order.Status),
            CorrectiveTransitions.TryGetValue(order.Status, out var corrections) ? corrections : [],
            order.Priority,
            order.DieselSurchargeOverride, order.DieselSurchargePercentOverride, order.DieselSurchargeOverrideReason,
            order.LegalEntityId, order.QuantityUnitCode,
            order.CalculatedPrice, order.PriceIsManual, order.PriceOverrideReason,
            pricingLines, serviceLines, pricingSnapshot,
            order.PricingSource, order.OneOffFixedAmount,
            order.OneOffIncludedLoadingMinutes, order.OneOffIncludedUnloadingMinutes, order.OneOffIncludedCombinedMinutes,
            order.OneOffExtraHourlyRate, order.OneOffNotes, totalWithProposed,
            order.IncludedLoadingMinutesOverride, order.IncludedUnloadingMinutesOverride,
            order.ExtraTimeHourlyRateOverride, order.ExtraTimeRoundingStepMinutes, order.ExtraTimeMinimumBillableMinutes,
            order.Version, dossierRef?.Id, dossierRef?.DossierNumber,
            order.DistanceKm, order.LoadingMeters,
            order.PlateauRequired, order.MoffettRequired, order.IsReturnMovement);
    }

    /// <summary>
    /// The tenant's default transport activity type for auto-wrapped orders; seeds the
    /// catalogue lazily and falls back to any active HasStops type for reshaped tenants.
    /// Null only when the tenant has no transport-capable type at all (wrapper then carries
    /// no activity; the dossier page renders the linked order directly).
    /// </summary>
    private async Task<Modules.Dossiers.Entities.ActivityType?> ResolveDefaultTransportActivityTypeAsync(
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var type = await _dbContext.ActivityTypes
            .Where(t => t.TenantId == tenantId && t.IsActive && t.IsSystemDefaultTransport)
            .FirstOrDefaultAsync(cancellationToken);
        if (type is not null)
        {
            return type;
        }

        await new Modules.Dossiers.Services.ActivityTypeSeeder(_dbContext, _tenantContext)
            .EnsureSeededAsync(cancellationToken);
        return await _dbContext.ActivityTypes
                .Where(t => t.TenantId == tenantId && t.IsActive && t.IsSystemDefaultTransport)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.ActivityTypes
                .Where(t => t.TenantId == tenantId && t.IsActive && t.HasStops)
                .OrderBy(t => t.SortOrder)
                .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record WarehouseActivityCounts(
        decimal? ScannedIn, decimal? ScannedOut, decimal? Picked, decimal? PalletDays);

    /// <summary>
    /// P7: the ACTUAL warehouse activity of the order's packages, for event-sourced services
    /// (QuantitySource ScannedIn/ScannedOut/Picked/PalletDays). Skipped entirely (null) when
    /// the tenant has no such service — the legacy pricing path costs nothing extra. Counts
    /// are distinct packages, so scan replays never inflate a quantity; pallet-days follow
    /// the storage clock (started days, open stays counted to today).
    /// </summary>
    private async Task<WarehouseActivityCounts?> ResolveWarehouseActivityAsync(
        TransportOrder order, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var hasEventSourcedService = await _dbContext.ServiceOptions.AsNoTracking()
            .AnyAsync(o => o.TenantId == tenantId && o.IsActive && o.QuantitySource != "Ordered", cancellationToken);
        if (!hasEventSourcedService)
        {
            return null;
        }

        var packageIds = await _dbContext.Packages.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TransportOrderId == order.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (packageIds.Count == 0)
        {
            return new WarehouseActivityCounts(null, null, null, null);
        }

        var events = await _dbContext.PackageEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && packageIds.Contains(e.PackageId))
            .Select(e => new { e.PackageId, e.EventType })
            .ToListAsync(cancellationToken);
        var inTypes = new[]
        {
            Modules.Packages.Entities.PackageEventType.Received,
            Modules.Packages.Entities.PackageEventType.ReturnedToDepot,
        };
        var outTypes = new[]
        {
            Modules.Packages.Entities.PackageEventType.LoadScan,
            Modules.Packages.Entities.PackageEventType.ReturnLoaded,
            Modules.Packages.Entities.PackageEventType.RedeliveryLoaded,
            Modules.Packages.Entities.PackageEventType.ReturnedToSender,
        };
        decimal? Count(Func<Modules.Packages.Entities.PackageEventType, bool> match)
        {
            var count = events.Where(e => match(e.EventType)).Select(e => e.PackageId).Distinct().Count();
            return count > 0 ? count : null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var stays = await _dbContext.StorageStays.AsNoTracking()
            .Where(s => s.TenantId == tenantId && packageIds.Contains(s.PackageId))
            .Select(s => new { s.InAt, s.OutAt })
            .ToListAsync(cancellationToken);
        decimal? palletDays = stays.Count > 0
            ? stays.Sum(s => Math.Max(1m, (decimal)Math.Ceiling(((s.OutAt ?? now) - s.InAt).TotalDays)))
            : null;

        return new WarehouseActivityCounts(
            Count(t => inTypes.Contains(t)),
            Count(t => outTypes.Contains(t)),
            Count(t => t == Modules.Packages.Entities.PackageEventType.Staged),
            palletDays);
    }

    /// <summary>
    /// P6: the order's linked dossier activity type for activity-bound pricing. Checks the
    /// change tracker FIRST — at creation time the auto-wrap activity is staged in the same
    /// save and not yet queryable.
    /// </summary>
    private async Task<Guid?> ResolveLinkedActivityTypeAsync(
        TransportOrder order, CancellationToken cancellationToken)
    {
        var staged = _dbContext.ChangeTracker.Entries<Modules.Dossiers.Entities.DossierActivity>()
            .Select(e => e.Entity)
            .Where(a => a.TenantId == order.TenantId && a.LinkedTransportOrderId == order.Id)
            .OrderBy(a => a.Sequence)
            .Select(a => (Guid?)a.ActivityTypeId)
            .FirstOrDefault();
        return staged ?? await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == order.TenantId && a.LinkedTransportOrderId == order.Id)
            .OrderBy(a => a.Sequence)
            .Select(a => (Guid?)a.ActivityTypeId)
            .FirstOrDefaultAsync(cancellationToken);
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
        CancellationToken cancellationToken, Guid? activityTypeHint = null)
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
        // Coverage entries for cargo lines that never reach the engine (§7): missing/unknown unit.
        var unpricedCargoCoverage = new List<OrderPricingCoverageDto>();
        if (_pricingEngine is not null)
        {
            // Commercial cargo lines are the pricing source of truth as soon as any carries a
            // managed unit code (wave 2026-08-04 §2/§6): one engine line per distinct unit with
            // summed quantities, so a unit change on a goods line immediately re-prices and the
            // header pair can never feed a stale unit into the engine. The order-level pair only
            // serves orders without coded lines (legacy fallback). Never both — that would
            // double-count.
            var lines = new List<PriceCalculationLineInput>();
            var allCargo = (cargoItems ?? []).Where(c => !c.IsDeleted).ToList();
            var codedCargo = allCargo
                .Where(c => !string.IsNullOrWhiteSpace(c.QuantityUnitCode))
                .ToList();
            // Cargo the engine can never price per unit (unknown/missing code) — reported as
            // "Niet geprijsd" coverage entries next to the engine's own coverage (§7).
            foreach (var uncoded in allCargo.Where(c => string.IsNullOrWhiteSpace(c.QuantityUnitCode)))
            {
                unpricedCargoCoverage.Add(new OrderPricingCoverageDto(
                    null,
                    uncoded.UnitTypeLabel ?? uncoded.QuantityUnit ?? "stuks",
                    uncoded.ExpectedQuantity, "None",
                    Reason: "Geen eenheid gekozen voor deze goederenlijn"));
            }

            if (codedCargo.Count > 0)
            {
                var codes = codedCargo
                    .Select(c => c.QuantityUnitCode!.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();
                var unitTypeIds = await _dbContext.UnitTypes.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && codes.Contains(u.Code))
                    .ToDictionaryAsync(u => u.Code, u => u.Id, cancellationToken);
                foreach (var group in codedCargo.GroupBy(c => c.QuantityUnitCode!.Trim().ToUpperInvariant()))
                {
                    if (!unitTypeIds.TryGetValue(group.Key, out var uid))
                    {
                        // Unknown code: cannot be priced per unit — pricing coverage reports it.
                        unpricedCargoCoverage.Add(new OrderPricingCoverageDto(
                            null, group.Key, group.Sum(c => c.ExpectedQuantity), "None",
                            Reason: "Onbekende eenheid"));
                        continue;
                    }

                    // Per-line dimensions feed billable-quantity contracts (oversize).
                    var details = group
                        .Select(c => new PriceCalculationLineDetail(
                            c.ExpectedQuantity,
                            c.LengthMeters is { } length ? length * 100m : null,
                            c.WidthMeters is { } width ? width * 100m : null))
                        .ToList();
                    lines.Add(new PriceCalculationLineInput(uid, group.Sum(c => c.ExpectedQuantity), details));
                }
            }
            else if (order.Quantity is { } quantity && quantity > 0 && order.QuantityUnitCode is { } code)
            {
                var unitTypeId = await _dbContext.UnitTypes.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && u.Code == code)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (unitTypeId is { } uid)
                {
                    lines.Add(new PriceCalculationLineInput(uid, quantity, null));
                }
            }

            var unloadingStops = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Unloading)
                .OrderBy(s => s.Sequence)
                .ToList();
            var delivery = unloadingStops.LastOrDefault();
            // Wave 3 §2: the FIRST loading stop resolves the origin zone (O/D-dimension rules).
            var origin = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Loading)
                .OrderBy(s => s.Sequence)
                .FirstOrDefault();
            var (actualLoadingMinutes, actualUnloadingMinutes) = await ComputeActualStopMinutesAsync(order, cancellationToken);
            var oneOff = order.PricingSource == OrderPricingSource.OneOff
                ? new OneOffPricingInput(
                    order.OneOffFixedAmount ?? 0m, order.OneOffIncludedLoadingMinutes, order.OneOffIncludedUnloadingMinutes,
                    order.OneOffIncludedCombinedMinutes, order.OneOffExtraHourlyRate, order.OneOffNotes)
                : null;
            // Task 10 + wave 2026-08-04 §18: included-time resolution stop → order → contract.
            // A stop-level override replaces the activity's included minutes (summed when several
            // stops of the activity carry one). Contract mode only (never built for a one-off
            // order — ValidateAsync/IncludedTimeOverrideError already rejects that combination,
            // but this stays a belt-and-braces guard against ever feeding them into the one-off
            // branch of PricingEngine.CalculateAsync).
            var loadingStopOverrides = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Loading && s.IncludedTimeMinutesOverride is not null)
                .Select(s => s.IncludedTimeMinutesOverride!.Value)
                .ToList();
            var unloadingStopOverrides = order.Stops
                .Where(s => !s.IsDeleted && s.StopType == StopType.Unloading && s.IncludedTimeMinutesOverride is not null)
                .Select(s => s.IncludedTimeMinutesOverride!.Value)
                .ToList();
            int? stopLoadingOverride = loadingStopOverrides.Count > 0 ? loadingStopOverrides.Sum() : null;
            int? stopUnloadingOverride = unloadingStopOverrides.Count > 0 ? unloadingStopOverrides.Sum() : null;
            var effectiveLoadingOverride = stopLoadingOverride ?? order.IncludedLoadingMinutesOverride;
            var effectiveUnloadingOverride = stopUnloadingOverride ?? order.IncludedUnloadingMinutesOverride;
            var includedTimeOverrides = order.PricingSource != OrderPricingSource.OneOff
                && (effectiveLoadingOverride is not null || effectiveUnloadingOverride is not null
                    || order.ExtraTimeHourlyRateOverride is not null || order.ExtraTimeRoundingStepMinutes is not null
                    || order.ExtraTimeMinimumBillableMinutes is not null)
                ? new IncludedTimeOverrideInput(
                    effectiveLoadingOverride, effectiveUnloadingOverride,
                    order.ExtraTimeHourlyRateOverride, order.ExtraTimeRoundingStepMinutes, order.ExtraTimeMinimumBillableMinutes,
                    FromStopOverride: stopLoadingOverride is not null || stopUnloadingOverride is not null)
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

            // §16: per-stop time facts feed time-based service conditions (never hardcoded times).
            // C-03: PlannedDate drives the WEEKEND and HOLIDAY surcharges (PricingEngine
            // ServiceConditionKind.Weekend/Holiday), so it must be the tenant-local calendar day.
            // Truncating the raw instant prices a Monday 00:30 stop (Sunday 22:30Z) as weekend work
            // and drops the surcharge from a Saturday 00:30 stop (Friday 22:30Z): money, both ways.
            var pricingZone = await ResolveTenantTimeZoneAsync(cancellationToken);
            var stopTimeInputs = order.Stops
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.Sequence)
                .Select(s => new StopTimeInput(
                    s.StopType == StopType.Unloading,
                    s.TimeRequirement.ToString(),
                    s.TimeRequirementFrom,
                    s.TimeRequirementTo,
                    s.AppointmentRequired,
                    (s.PlannedFrom ?? s.PlannedTo) is { } planned ? TenantTimeZone.ToLocalDate(planned, pricingZone) : null))
                .ToList();

            var warehouseActivity = await ResolveWarehouseActivityAsync(order, cancellationToken);
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
                order.WeightKg, order.DistanceKm, order.PalletCount,
                [], Services: engineSelections,
                VolumeM3: order.VolumeM3,
                LoadingMeters: order.LoadingMeters,
                StopCount: unloadingStops.Count > 0 ? unloadingStops.Count : null,
                AdrRequired: order.AdrRequired,
                CraneRequired: order.CraneRequired,
                PlateauRequired: order.PlateauRequired,
                MoffettRequired: order.MoffettRequired,
                IsReturnMovement: order.IsReturnMovement,
                ActivityTypeId: activityTypeHint ?? await ResolveLinkedActivityTypeAsync(order, cancellationToken),
                ScannedInCount: warehouseActivity?.ScannedIn,
                ScannedOutCount: warehouseActivity?.ScannedOut,
                PickedCount: warehouseActivity?.Picked,
                PalletDays: warehouseActivity?.PalletDays,
                CargoLineCount: cargoItems?.Count(c => !c.IsDeleted),
                OneOff: oneOff,
                ActualLoadingMinutes: actualLoadingMinutes,
                ActualUnloadingMinutes: actualUnloadingMinutes,
                OriginCountryCode: origin?.CountryCode,
                OriginPostalCode: origin?.PostalCode,
                Groups: groups.Count > 0 ? groups : null,
                WarehouseIds: warehouseIds,
                IncludedTimeOverrides: includedTimeOverrides,
                StopTimes: stopTimeInputs.Count > 0 ? stopTimeInputs : null), cancellationToken);
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

            // Fail-closed (L7): no wired authorization service means NO override rights, never a
            // silent allow-all.
            var userId = _currentUser?.CurrentUserId;
            var allowed = _permissionService is not null
                && userId is { } id
                && await _permissionService.UserHasPermissionAsync(id, PermissionCodes.OrdersOverridePrice, cancellationToken);
            if (!allowed)
            {
                return TransportOrderOperationResult.Invalid("Je hebt geen rechten om de berekende prijs te overschrijven.");
            }
        }

        _dbContext.RemoveRange(obsoleteLines);
        _dbContext.RemoveRange(existingServices);

        // Wave 2: resolve the sales code per engine line (rule's wins over the engaged
        // agreement's; a service option carries its own) and freeze it on the persisted lines,
        // so invoicing and KPIs can group without re-resolving masterdata later.
        var categoryRuleIds = (result?.Lines ?? [])
            .Where(l => l.RuleId is not null).Select(l => l.RuleId!.Value).Distinct().ToList();
        var categoryAgreementIds = (result?.Lines ?? [])
            .Where(l => l.AgreementId is not null).Select(l => l.AgreementId!.Value).Distinct().ToList();
        var categoryOptionIds = (result?.ServiceLines ?? [])
            .Select(l => l.ServiceOptionId).Distinct().ToList();
        var ruleCategoryById = categoryRuleIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _dbContext.PriceRules.AsNoTracking()
                .Where(r => r.TenantId == tenantId && categoryRuleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.SalesCategoryId, cancellationToken);
        var agreementCategoryById = categoryAgreementIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _dbContext.PricingAgreements.AsNoTracking()
                .Where(a => a.TenantId == tenantId && categoryAgreementIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.SalesCategoryId, cancellationToken);
        var optionCategoryById = categoryOptionIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _dbContext.ServiceOptions.AsNoTracking()
                .Where(o => o.TenantId == tenantId && categoryOptionIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.SalesCategoryId, cancellationToken);
        Guid? ResolveLineSalesCategory(Modules.Tarification.Dtos.PriceBreakdownLine line) =>
            (line.RuleId is { } rid ? ruleCategoryById.GetValueOrDefault(rid) : null)
            ?? (line.AgreementId is { } aid ? agreementCategoryById.GetValueOrDefault(aid) : null);

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
                matchedAdjusted.SalesCategoryId = ResolveLineSalesCategory(line);
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
                SalesCategoryId = ResolveLineSalesCategory(line),
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
                Note = selection?.Note,
                SalesCategoryId = optionCategoryById.GetValueOrDefault(serviceLine.ServiceOptionId),
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
            // Wave 2026-08-04 §7: freeze per-goods-line coverage with the calculation — engine
            // coverage (per unit line it received) + cargo the engine never saw.
            var coverage = (result.Coverage ?? [])
                .Select(c => new OrderPricingCoverageDto(
                    c.UnitTypeId, c.UnitLabel, c.Quantity, c.Status,
                    c.BaseAmount, c.BaseRuleName, c.ServicesAmount, c.Reason))
                .Concat(unpricedCargoCoverage)
                .ToList();
            snapshot.CoverageJson = coverage.Count > 0 ? JsonSerializer.Serialize(coverage, CoverageJsonOptions) : null;
            // Wave 2 §5: the typed, queryable projection of the same coverage (worst entry
            // wins); a fresh calculation is by definition not stale.
            snapshot.CoverageStatus = coverage.Count == 0
                ? "NotApplicable"
                : coverage.Any(c => c.Status == "None") ? "None"
                : coverage.Any(c => c.Status == "Partial") ? "Partial"
                : "Full";
            snapshot.IsStale = false;
            // Status is deliberately left untouched — a save never resets Draft/Reviewed.
        }
        else if (existingSnapshot is not null
                 && await PricingInputsChangedAsync(order, requestedAgreedPrice, priceIsManual, overrideReason,
                     engineSelections, cancellationToken))
        {
            // Wave 2 §5: pricing-relevant inputs changed but no recalculation ran (no engine
            // configuration) — never silently recalculate, flag the frozen numbers instead.
            existingSnapshot.IsStale = true;
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

    /// <summary>Camel-cased, matching the API's wire format so the frontend can share the shape.</summary>
    private static readonly JsonSerializerOptions CoverageJsonOptions = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<OrderPricingCoverageDto>? DeserializeCoverage(string? coverageJson)
    {
        if (string.IsNullOrWhiteSpace(coverageJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<OrderPricingCoverageDto>>(coverageJson, CoverageJsonOptions);
        }
        catch (JsonException)
        {
            // A malformed historical payload must never break the order detail.
            return null;
        }
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
            || OneOffChanged(nameof(TransportOrder.OneOffNotes))
            || OneOffChanged(nameof(TransportOrder.IncludedLoadingMinutesOverride))
            || OneOffChanged(nameof(TransportOrder.IncludedUnloadingMinutesOverride))
            || OneOffChanged(nameof(TransportOrder.ExtraTimeHourlyRateOverride))
            || OneOffChanged(nameof(TransportOrder.ExtraTimeRoundingStepMinutes))
            || OneOffChanged(nameof(TransportOrder.ExtraTimeMinimumBillableMinutes)))
        {
            return true;
        }

        // Wave 2026-08-04 §6: goods ARE pricing inputs. A locked price must refuse (not silently
        // ignore) quantity/unit/measure changes and any commercial-cargo-line change — otherwise
        // the frozen price no longer describes the order's goods and nothing warns anyone.
        if (OneOffChanged(nameof(TransportOrder.Quantity))
            || OneOffChanged(nameof(TransportOrder.QuantityUnit))
            || OneOffChanged(nameof(TransportOrder.QuantityUnitCode))
            || OneOffChanged(nameof(TransportOrder.WeightKg))
            || OneOffChanged(nameof(TransportOrder.VolumeM3))
            || OneOffChanged(nameof(TransportOrder.DistanceKm))
            || OneOffChanged(nameof(TransportOrder.LoadingMeters))
            || OneOffChanged(nameof(TransportOrder.PalletCount))
            || OneOffChanged(nameof(TransportOrder.AdrRequired)))
        {
            return true;
        }

        if (CargoPricingInputsChanged(order.Id))
        {
            return true;
        }

        if (StopTimeRequirementsChanged(order))
        {
            return true;
        }

        var storedServices = await _dbContext.TransportOrderServiceLines
            .Where(l => l.TenantId == _tenantContext.TenantId && l.TransportOrderId == order.Id)
            .ToListAsync(cancellationToken);
        return await ServiceSelectionsChangedAsync(order.CustomerId, order.OrderDate, storedServices, serviceSelections, cancellationToken);
    }

    /// <summary>Cargo-line properties that feed the pricing engine (quantity/unit/measures/dims/ADR).</summary>
    private static readonly string[] CargoPricingProperties =
    [
        nameof(CargoItem.ExpectedQuantity), nameof(CargoItem.QuantityUnitCode),
        nameof(CargoItem.TotalWeightKg), nameof(CargoItem.VolumeM3), nameof(CargoItem.PalletCount),
        nameof(CargoItem.LengthMeters), nameof(CargoItem.WidthMeters), nameof(CargoItem.AdrRequired),
    ];

    /// <summary>
    /// Whether the update currently in the change tracker adds/removes a commercial cargo line of
    /// this order or touches a pricing-relevant property of one. Stop relinking after the
    /// wholesale stop replacement only moves Loading/UnloadingStopId, which is deliberately NOT
    /// in the list — an unrelated edit must stay possible on a locked order.
    /// </summary>
    private bool CargoPricingInputsChanged(Guid orderId)
    {
        foreach (var cargoEntry in _dbContext.ChangeTracker.Entries<CargoItem>()
                     .Where(e => e.Entity.TransportOrderId == orderId))
        {
            if (cargoEntry.State is EntityState.Added or EntityState.Deleted)
            {
                return true;
            }

            if (cargoEntry.State == EntityState.Modified
                && CargoPricingProperties.Any(p => !Equals(cargoEntry.OriginalValues[p], cargoEntry.CurrentValues[p])))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wave 2026-08-04 §16/§21: stop time requirements feed time-based surcharges and are
    /// therefore pricing inputs. Since C-01 stops are synced in place, so the "before" side is
    /// read from the change tracker's ORIGINAL values (Deleted and Modified/Unchanged rows) and
    /// the "after" side from the current entities (Added rows included); compared as ordered
    /// multisets of the pricing-relevant facts. Planned dates/windows are deliberately NOT
    /// compared — planning must stay possible on a locked price.
    /// </summary>
    private bool StopTimeRequirementsChanged(TransportOrder order)
    {
        var entries = _dbContext.ChangeTracker.Entries<TransportOrderStop>()
            .Where(e => e.Entity.TransportOrderId == order.Id)
            .ToList();

        static (StopType, StopTimeRequirementKind, TimeOnly?, TimeOnly?, bool, int?) Key(TransportOrderStop s) =>
            (s.StopType, s.TimeRequirement, s.TimeRequirementFrom, s.TimeRequirementTo, s.AppointmentRequired,
             s.IncludedTimeMinutesOverride);

        static (StopType, StopTimeRequirementKind, TimeOnly?, TimeOnly?, bool, int?) OriginalKey(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TransportOrderStop> e) =>
            (e.OriginalValues.GetValue<StopType>(nameof(TransportOrderStop.StopType)),
             e.OriginalValues.GetValue<StopTimeRequirementKind>(nameof(TransportOrderStop.TimeRequirement)),
             e.OriginalValues.GetValue<TimeOnly?>(nameof(TransportOrderStop.TimeRequirementFrom)),
             e.OriginalValues.GetValue<TimeOnly?>(nameof(TransportOrderStop.TimeRequirementTo)),
             e.OriginalValues.GetValue<bool>(nameof(TransportOrderStop.AppointmentRequired)),
             e.OriginalValues.GetValue<int?>(nameof(TransportOrderStop.IncludedTimeMinutesOverride)));

        var before = entries.Where(e => e.State != EntityState.Added).Select(OriginalKey).OrderBy(k => k).ToList();
        if (before.Count == 0)
        {
            // Create path: nothing persisted to compare against.
            return false;
        }

        var after = entries.Where(e => e.State != EntityState.Deleted).Select(e => Key(e.Entity)).OrderBy(k => k).ToList();
        return !before.SequenceEqual(after);
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

                var contradiction = ValidateQuantityAmountConsistency(request.Quantity, request.UnitPrice, request.Amount);
                if (contradiction is not null)
                {
                    return TransportOrderOperationResult.Invalid(contradiction);
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
                    Quantity = request.Quantity, UnitPrice = request.UnitPrice, Unit = NormalizeUnitCode(request.Unit),
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

            var updateContradiction = ValidateQuantityAmountConsistency(request.Quantity, request.UnitPrice, request.Amount);
            if (updateContradiction is not null)
            {
                return TransportOrderOperationResult.Invalid(updateContradiction);
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
            if (!string.IsNullOrWhiteSpace(request.Unit))
            {
                existing.Unit = NormalizeUnitCode(request.Unit);
            }

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
        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "lines_adjusted", auditBefore, auditAfter, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Amount = explicit Amount, else Round(quantity × unitPrice, 2), else null (never invented).</summary>
    private static decimal? ResolveAmount(decimal? quantity, decimal? unitPrice, decimal? amount) =>
        amount ?? (quantity is { } q && unitPrice is { } p ? decimal.Round(q * p, 2) : (decimal?)null);

    /// <summary>
    /// Manual price-line guard (Task 5): when quantity, unit price AND amount are all explicitly
    /// provided on the same request, they must agree; and an explicit quantity must be positive.
    /// Operates on the request's own values only — never combined with a stored line's values, so
    /// a partial update (e.g. Amount alone) never gets falsely flagged against unrelated stored
    /// Quantity/UnitPrice.
    /// </summary>
    private static string? ValidateQuantityAmountConsistency(decimal? quantity, decimal? unitPrice, decimal? amount)
    {
        if (quantity is { } q && unitPrice is { } up && amount is { } a && Math.Round(q * up, 2) != Math.Round(a, 2))
        {
            return "Het totaalbedrag komt niet overeen met aantal × eenheidsprijs. Laat het bedrag leeg of corrigeer de waarden.";
        }

        if (quantity is <= 0)
        {
            return "Aantal moet groter zijn dan nul.";
        }

        return null;
    }

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
            .Select(l => new OrderServiceInput(l.ServiceOptionId!.Value, l.Quantity, l.PalletCount, l.DayCount, l.Note))
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
        // Fail-closed (L7): a missing authorization service denies, never allows.
        var userId = _currentUser?.CurrentUserId;
        var allowed = _permissionService is not null
            && userId is { } uid
            && (await _permissionService.UserHasPermissionAsync(
                    uid, requiresLockPermission ? PermissionCodes.OrdersLockPrice : PermissionCodes.OrdersEdit, cancellationToken)
                || await _permissionService.UserHasPermissionAsync(uid, PermissionCodes.OrdersManage, cancellationToken));
        if (!allowed)
        {
            return TransportOrderOperationResult.Invalid("Je hebt geen rechten voor deze statuswijziging.");
        }

        var before = new { snapshot.Status };
        snapshot.Status = target;
        await InvoiceReadinessEvaluator.EvaluateAsync(_dbContext, order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "status_changed", before, new { snapshot.Status }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>Fail-closed permission check shared by the confirmation workflow.</summary>
    private async Task<bool> CurrentUserHasAnyAsync(CancellationToken cancellationToken, params string[] codes)
    {
        if (_permissionService is null || _currentUser?.CurrentUserId is not { } userId)
        {
            return false;
        }

        foreach (var code in codes)
        {
            if (await _permissionService.UserHasPermissionAsync(userId, code, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wave 2026-08-04 §8/§10: the single visible "Prijs bevestigen" action. Technically locks
    /// the snapshot (every existing locked-price protection applies) and stamps who/when.
    /// Refused while any goods line lacks a base transport tariff, unless the caller holds the
    /// dedicated override permission AND gives a reason — that reason stays visibly attached to
    /// the confirmed price.
    /// </summary>
    public async Task<TransportOrderOperationResult> ConfirmOrderPricingAsync(
        Guid orderId, string? unpricedGoodsReason, CancellationToken cancellationToken)
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

        if (snapshot.Status == OrderPricingStatus.Invoiced)
        {
            return TransportOrderOperationResult.Invalid("De prijs van een gefactureerde order kan niet meer wijzigen.");
        }

        if (snapshot.Status == OrderPricingStatus.Locked)
        {
            return TransportOrderOperationResult.Invalid("De prijs is al bevestigd.");
        }

        if (!await CurrentUserHasAnyAsync(cancellationToken, PermissionCodes.OrdersLockPrice, PermissionCodes.OrdersManage))
        {
            return TransportOrderOperationResult.Invalid("Je hebt geen rechten om de prijs te bevestigen.");
        }

        // §10: coverage gate — a price with unpriced goods needs the dedicated override + reason.
        var coverage = DeserializeCoverage(snapshot.CoverageJson) ?? [];
        var unpriced = coverage.Where(c => c.Status != "Full").ToList();
        string? overrideReason = null;
        if (unpriced.Count > 0)
        {
            var affected = string.Join(", ", unpriced.Select(c => $"{c.Quantity:0.##} {c.UnitLabel}"));
            if (!await CurrentUserHasAnyAsync(cancellationToken, PermissionCodes.OrdersConfirmIncompletePrice, PermissionCodes.OrdersManage))
            {
                return TransportOrderOperationResult.Invalid(
                    $"De prijs kan niet worden bevestigd. {affected} zonder passend basistarief.");
            }

            if (string.IsNullOrWhiteSpace(unpricedGoodsReason))
            {
                return TransportOrderOperationResult.Invalid(
                    "Geef een reden op om te bevestigen terwijl niet alle goederen geprijsd zijn.");
            }

            overrideReason = unpricedGoodsReason.Trim();
        }

        var userId = _currentUser?.CurrentUserId;
        var confirmerName = userId is { } confirmerId
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == confirmerId && u.TenantId == tenantId)
                .Select(u => (u.FirstName + " " + u.LastName).Trim())
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var before = new { snapshot.Status, snapshot.LinesTotal };
        snapshot.Status = OrderPricingStatus.Locked;
        snapshot.ConfirmedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        snapshot.ConfirmedByUserId = userId;
        snapshot.ConfirmedByName = string.IsNullOrWhiteSpace(confirmerName) ? null : confirmerName;
        snapshot.ConfirmedWithUnpricedGoodsReason = overrideReason;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "price_confirmed", before,
            new
            {
                snapshot.Status, snapshot.LinesTotal, snapshot.ConfirmedAtUtc, snapshot.ConfirmedByName,
                snapshot.ConfirmedWithUnpricedGoodsReason,
                UnpricedGoods = unpriced.Select(c => $"{c.Quantity:0.##} {c.UnitLabel}: {c.Reason}").ToList(),
            }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
    }

    /// <summary>
    /// Wave 2026-08-04 §8: "Prijs aanpassen" — reopens a confirmed price with a mandatory reason.
    /// The old total and confirmation stamp stay in the audit trail; the price returns to
    /// "Nog te bevestigen" (Draft) and must be confirmed again after editing.
    /// </summary>
    public async Task<TransportOrderOperationResult> ReopenOrderPricingAsync(
        Guid orderId, string? reason, CancellationToken cancellationToken)
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

        if (snapshot.Status == OrderPricingStatus.Invoiced)
        {
            return TransportOrderOperationResult.Invalid("De prijs van een gefactureerde order kan niet meer wijzigen.");
        }

        if (snapshot.Status != OrderPricingStatus.Locked)
        {
            return TransportOrderOperationResult.Invalid("Alleen een bevestigde prijs kan worden aangepast.");
        }

        if (!await CurrentUserHasAnyAsync(cancellationToken, PermissionCodes.OrdersLockPrice, PermissionCodes.OrdersManage))
        {
            return TransportOrderOperationResult.Invalid("Je hebt geen rechten om de bevestigde prijs aan te passen.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return TransportOrderOperationResult.Invalid("Geef een reden op om de bevestigde prijs aan te passen.");
        }

        var before = new
        {
            snapshot.Status, snapshot.LinesTotal,
            snapshot.ConfirmedAtUtc, snapshot.ConfirmedByName, snapshot.ConfirmedWithUnpricedGoodsReason,
        };
        snapshot.Status = OrderPricingStatus.Draft;
        snapshot.ConfirmedAtUtc = null;
        snapshot.ConfirmedByUserId = null;
        snapshot.ConfirmedByName = null;
        snapshot.ConfirmedWithUnpricedGoodsReason = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("OrderPricing", orderId.ToString(), "price_reopened", before,
            new { snapshot.Status, snapshot.LinesTotal, Reason = reason.Trim() }, cancellationToken);

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
