using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface ICustomerPortalService
{
    Task<PortalResult<PortalContextDto>> GetContextAsync(CancellationToken cancellationToken);

    Task<PortalResult<PortalContextDto>> SetLanguageAsync(string language, CancellationToken cancellationToken);

    Task<PortalResult<IReadOnlyList<PortalOrderListItemDto>>> ListMyOrdersAsync(CancellationToken cancellationToken);

    Task<PortalResult<PortalOrderDetailDto>> GetMyOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<PortalResult<PortalOrderDetailDto>> SubmitOrderAsync(PortalCreateOrderRequest request, CancellationToken cancellationToken);

    Task<PortalResult<IReadOnlyList<PortalLocationDto>>> ListMyLocationsAsync(CancellationToken cancellationToken);

    // Wave 11: the customer's own notification preferences.
    Task<PortalResult<PortalNotificationPreferencesDto>> GetNotificationPreferencesAsync(CancellationToken cancellationToken);
    Task<PortalResult<PortalNotificationPreferencesDto>> SaveNotificationPreferencesAsync(
        SavePortalNotificationPreferencesRequest request, CancellationToken cancellationToken);

    Task<PortalResult<PortalLocationDto>> CreateMyLocationAsync(PortalCreateLocationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The customer-facing portal surface. The customer context is ALWAYS derived from the
/// authenticated user's linked customer — never from anything the client sends — and every
/// query and reference validation is scoped to that customer. Order intake reuses the ONE
/// internal TransportOrderService use case (same validators, cargo rules, numbering, audit,
/// status history, package generation on confirm); portal submissions merely enter the
/// workflow as Submitted so planners review them before confirmation.
/// </summary>
public class CustomerPortalService : ICustomerPortalService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ITransportOrderService _orderService;
    private readonly ILocationService _locationService;
    private readonly IAuditService _auditService;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<CustomerPortalService>? _logger;

    public CustomerPortalService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        ITransportOrderService orderService,
        ILocationService locationService,
        IAuditService auditService,
        INotificationEventService? notificationEvents = null,
        ILogger<CustomerPortalService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _orderService = orderService;
        _locationService = locationService;
        _auditService = auditService;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    private async Task<(Guid CustomerId, string CustomerName)?> MyCustomerAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        var link = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId && u.CustomerId != null)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId),
                u => u.CustomerId, c => c.Id,
                (u, c) => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return link is null ? null : (link.Id, link.Name);
    }

    public async Task<PortalResult<PortalContextDto>> GetContextAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalContextDto>.NoCustomerLink();
        }

        var preferredLanguage = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == _currentUserContext.CurrentUserId && u.TenantId == _tenantContext.TenantId)
            .Select(u => u.PreferredLanguageCode)
            .FirstOrDefaultAsync(cancellationToken);
        return PortalResult<PortalContextDto>.Success(
            new PortalContextDto(customer.Value.CustomerId, customer.Value.CustomerName, preferredLanguage));
    }

    /// <summary>Persists the portal user's UI/e-mail language (nl/fr/en); audited.</summary>
    public async Task<PortalResult<PortalContextDto>> SetLanguageAsync(string language, CancellationToken cancellationToken)
    {
        if (Common.SupportedLanguages.NormalizeOrNull(language) is not { } normalized)
        {
            throw new DomainValidationException("language", "Kies nl, fr of en.");
        }

        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null || _currentUserContext.CurrentUserId is not { } userId)
        {
            return PortalResult<PortalContextDto>.NoCustomerLink();
        }

        var user = await _dbContext.Users
            .FirstAsync(u => u.Id == userId && u.TenantId == _tenantContext.TenantId, cancellationToken);
        var old = user.PreferredLanguageCode;
        if (old != normalized)
        {
            user.PreferredLanguageCode = normalized;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync("User", userId.ToString(), "LanguageChanged",
                new { Language = old }, new { Language = normalized }, cancellationToken);
        }

        return PortalResult<PortalContextDto>.Success(
            new PortalContextDto(customer.Value.CustomerId, customer.Value.CustomerName, normalized));
    }

    public async Task<PortalResult<IReadOnlyList<PortalOrderListItemDto>>> ListMyOrdersAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<IReadOnlyList<PortalOrderListItemDto>>.NoCustomerLink();
        }

        // The shared search service already scopes by customer and computes the route summary.
        var page = await _orderService.SearchAsync(
            search: null, status: null, customerId: customer.Value.CustomerId,
            fromDate: null, toDate: null, Common.Models.PageRequest.Of(1, 200), cancellationToken);

        return PortalResult<IReadOnlyList<PortalOrderListItemDto>>.Success(page.Items
            .Select(o => new PortalOrderListItemDto(
                o.Id, o.OrderNumber, o.OrderDate, o.Status, o.CustomerReference, o.GoodsDescription,
                o.FirstLoadingCity, o.LastUnloadingCity))
            .ToList());
    }

    public async Task<PortalResult<PortalOrderDetailDto>> GetMyOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalOrderDetailDto>.NoCustomerLink();
        }

        var order = await _orderService.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.CustomerId != customer.Value.CustomerId)
        {
            // Other customers' orders are indistinguishable from non-existent ones.
            return PortalResult<PortalOrderDetailDto>.NotFound();
        }

        return PortalResult<PortalOrderDetailDto>.Success(await TrimAsync(order, cancellationToken));
    }

    public async Task<PortalResult<PortalOrderDetailDto>> SubmitOrderAsync(
        PortalCreateOrderRequest request, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalOrderDetailDto>.NoCustomerLink();
        }

        if (request.Stops is not { Count: > 0 })
        {
            return PortalResult<PortalOrderDetailDto>.Invalid("Een opdracht heeft minstens één stop nodig.");
        }

        // Every referenced location must belong to THIS customer (company sites and other
        // customers' locations are equally off-limits from the portal).
        var locationIds = request.Stops.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList();
        if (locationIds.Count > 0)
        {
            var owned = await MyLocationsQuery(customer.Value.CustomerId)
                .Where(l => locationIds.Contains(l.Id))
                .CountAsync(cancellationToken);
            if (owned != locationIds.Count)
            {
                return PortalResult<PortalOrderDetailDto>.Invalid(
                    "Eén of meer gekozen locaties horen niet bij uw klantaccount.");
            }
        }

        var createRequest = new CreateTransportOrderRequest(
            customer.Value.CustomerId,
            request.CustomerReference,
            request.OrderDate,
            request.GoodsDescription,
            Quantity: null, QuantityUnit: null, WeightKg: null, VolumeM3: null, PalletCount: null,
            AdrRequired: (request.CargoItems ?? []).Any(c => c.AdrRequired),
            CraneRequired: false,
            AgreedPrice: null, // pricing is internal; never portal-supplied
            Notes: request.Remarks,
            Stops: request.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                PlannedFrom: null, PlannedTo: null, s.Reference, s.Instructions,
                RequestedFrom: s.RequestedFrom, RequestedTo: s.RequestedTo)).ToList(),
            CargoItems: (request.CargoItems ?? []).Select(c => new CargoItemInput(
                c.Description, Barcode: null, c.ExpectedQuantity, c.QuantityUnit, Notes: null,
                UnitType: c.UnitType, TotalWeightKg: c.TotalWeightKg,
                AdrRequired: c.AdrRequired, AdrDetails: c.AdrDetails)).ToList());

        var created = await _orderService.CreateAsync(createRequest, cancellationToken);
        if (created.Outcome != TransportOrderOperationOutcome.Success)
        {
            return PortalResult<PortalOrderDetailDto>.Invalid(created.Error ?? "De opdracht kon niet worden ingediend.");
        }

        // Draft → Submitted: the planner reviews before confirmation. Recorded in the
        // immutable status history with an explicit portal marker.
        var entity = await _dbContext.TransportOrders
            .FirstAsync(o => o.Id == created.Order!.Id && o.TenantId == _tenantContext.TenantId, cancellationToken);
        entity.PendingStatusChangeReason = "Ingediend via het klantportaal";
        entity.Status = TransportOrderStatus.Submitted;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("TransportOrder", entity.Id.ToString(), "PortalSubmitted", null,
            new { entity.OrderNumber, CustomerId = customer.Value.CustomerId }, cancellationToken);

        if (_notificationEvents is not null)
        {
            try
            {
                await _notificationEvents.PublishAsync(MessageKinds.OrderSubmittedPortal, new NotificationEventContext(
                    "TransportOrder", entity.Id.ToString(),
                    new Dictionary<string, string>
                    {
                        ["orderNumber"] = entity.OrderNumber,
                        ["customerName"] = customer.Value.CustomerName,
                        ["goodsDescription"] = entity.GoodsDescription ?? string.Empty,
                    })
                {
                    CustomerId = customer.Value.CustomerId,
                    LinkPath = $"/orders/{entity.Id}",
                    InAppMessage = $"{entity.OrderNumber} ({customer.Value.CustomerName}) is ingediend via het klantportaal.",
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.",
                    MessageKinds.OrderSubmittedPortal);
            }
        }

        var detail = await _orderService.GetByIdAsync(entity.Id, cancellationToken);
        return PortalResult<PortalOrderDetailDto>.Success(await TrimAsync(detail!, cancellationToken));
    }

    public async Task<PortalResult<IReadOnlyList<PortalLocationDto>>> ListMyLocationsAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<IReadOnlyList<PortalLocationDto>>.NoCustomerLink();
        }

        var customerId = customer.Value.CustomerId;
        // The per-customer defaults live on the relationship (sprint 2); the legacy address
        // flags only stand in for a pre-backfill single-owner address.
        var locations = await MyLocationsQuery(customerId)
            .Select(l => new
            {
                Location = l,
                Link = _dbContext.CustomerLocationLinks
                    .Where(link => link.LocationId == l.Id && link.CustomerId == customerId && link.IsActive)
                    .Select(link => new { link.IsDefaultLoading, link.IsDefaultUnloading })
                    .FirstOrDefault(),
            })
            .Select(x => new PortalLocationDto(
                x.Location.Id, x.Location.Name, x.Location.Street, x.Location.HouseNumber, x.Location.PostalCode,
                x.Location.City, x.Location.CountryCode,
                x.Link != null ? x.Link.IsDefaultLoading : x.Location.IsDefaultLoadingLocation,
                x.Link != null ? x.Link.IsDefaultUnloading : x.Location.IsDefaultUnloadingLocation))
            .ToListAsync(cancellationToken);

        return PortalResult<IReadOnlyList<PortalLocationDto>>.Success(locations
            .OrderByDescending(l => l.IsDefaultLoadingLocation || l.IsDefaultUnloadingLocation)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>
    /// The addresses THIS customer may see and use: every active address with an active
    /// relationship to the customer (a shared address counts for each of its customers).
    /// A single-owner address that only carries the legacy owner column — no relationship
    /// row at all — behaves exactly as before, so nothing disappears before the backfill.
    /// </summary>
    private IQueryable<Location> MyLocationsQuery(Guid customerId) =>
        _dbContext.Locations.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.IsActive)
            .Where(l => _dbContext.CustomerLocationLinks.Any(link => link.LocationId == l.Id && link.CustomerId == customerId && link.IsActive)
                        || (l.CustomerId == customerId && !_dbContext.CustomerLocationLinks.Any(link => link.LocationId == l.Id)));

    public async Task<PortalResult<PortalLocationDto>> CreateMyLocationAsync(
        PortalCreateLocationRequest request, CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<PortalLocationDto>.NoCustomerLink();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return PortalResult<PortalLocationDto>.Invalid("Een naam is verplicht voor een locatie.");
        }

        // Same location use case as the internal module; the code is generated because
        // portal users do not manage master-data codes.
        var result = await _locationService.CreateAsync(new CreateLocationRequest(
            Code: $"PRT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            Name: request.Name.Trim(),
            Type: LocationType.CustomerLocation,
            Street: request.Street, HouseNumber: request.HouseNumber, PostalCode: request.PostalCode,
            City: request.City, CountryCode: request.CountryCode,
            Latitude: null, Longitude: null,
            ContactName: null, ContactPhone: null, ContactEmail: null,
            OpeningHours: null, LoadingInstructions: null, UnloadingInstructions: null,
            AccessInstructions: null, AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
            AlfapassRequired: false, AppointmentRequired: false,
            CustomerId: customer.Value.CustomerId,
            Notes: "Aangemaakt via het klantportaal",
            // Portal users cannot pick from the central master, so the duplicate rule would
            // only block them; the planner reconciles duplicates internally.
            OverrideDuplicate: true), cancellationToken);

        if (result.Outcome != LocationOperationOutcome.Success)
        {
            return PortalResult<PortalLocationDto>.Invalid("De locatie kon niet worden aangemaakt.");
        }

        var location = result.Location!;
        return PortalResult<PortalLocationDto>.Success(new PortalLocationDto(
            location.Id, location.Name, location.Street, location.HouseNumber, location.PostalCode,
            location.City, location.CountryCode, location.IsDefaultLoadingLocation, location.IsDefaultUnloadingLocation));
    }

    /// <summary>Statuses a customer may ever see on their own order's timeline; "Invoiced" and any
    /// future internal-only step stay off it (a customer sees "Afgerond", not billing state).</summary>
    private static readonly TransportOrderStatus[] CustomerVisibleStatuses =
    [
        TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned,
        TransportOrderStatus.InProgress, TransportOrderStatus.Completed, TransportOrderStatus.Cancelled,
    ];

    private async Task<PortalOrderDetailDto> TrimAsync(TransportOrderDetailDto order, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var timeline = await _dbContext.TransportOrderStatusHistories.AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.TransportOrderId == order.Id && CustomerVisibleStatuses.Contains(h.ToStatus))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new PortalTimelineEventDto(h.ToStatus, h.ChangedAt, h.Reason))
            .ToListAsync(cancellationToken);

        var exceptions = await _dbContext.Set<Modules.Exceptions.Entities.ExecutionException>().AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TransportOrderId == order.Id && e.CustomerVisible)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new PortalExceptionDto(e.Type.ToString(), e.Description, e.Status.ToString(), e.OccurredAt))
            .ToListAsync(cancellationToken);

        // Wave 8: the live ETA of the LAST unloading stop, straight from the dispatcher's ETA
        // machinery (overrides included) — the portal's tracking answer.
        var unloadingStopIds = order.Stops
            .Where(s => s.StopType == StopType.Unloading)
            .Select(s => s.Id)
            .ToList();
        var expectedDelivery = unloadingStopIds.Count == 0
            ? null
            : await _dbContext.StopEtas.AsNoTracking()
                .Where(e => e.TenantId == tenantId && unloadingStopIds.Contains(e.TransportOrderStopId))
                .OrderByDescending(e => e.CurrentEta)
                .Select(e => (DateTime?)e.CurrentEta)
                .FirstOrDefaultAsync(cancellationToken);

        // Wave 11: proof-of-delivery summary (data, never the stored files) once a current POD exists.
        var pod = await _dbContext.ProofsOfDelivery.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.TransportOrderId == order.Id && p.IsCurrent && p.CustomerVisible)
            .OrderByDescending(p => p.DeliveredAt)
            .Select(p => new PortalPodSummaryDto(p.DeliveredAt, p.RecipientName, p.Outcome.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

        return new PortalOrderDetailDto(
            order.Id, order.OrderNumber, order.OrderDate, order.Status, order.CustomerReference,
            order.GoodsDescription, order.Notes, order.CancellationReason,
            order.Stops.Select(s => new PortalStopDto(
                s.Sequence, s.StopType, s.LocationName, s.City, s.RequestedFrom, s.RequestedTo, s.Reference, s.Instructions)).ToList(),
            order.CargoItems.Select(c => new PortalCargoDto(
                c.Sequence,
                string.IsNullOrWhiteSpace(c.Description)
                    ? $"{c.ExpectedQuantity:0.##} × {c.QuantityUnitCode ?? c.UnitTypeLabel ?? "stuks"}"
                    : c.Description,
                c.ExpectedQuantity, c.QuantityUnit, c.UnitType, c.AdrRequired)).ToList(),
            timeline, exceptions,
            expectedDelivery, pod);
    }

    /// <summary>Customer-facing message kinds a portal user may toggle (Wave 11). Single source
    /// of truth lives in Messaging: the outbox interprets a customer's EnabledKinds list strictly
    /// against this same set, so the preference screen and the suppression rule can never drift.</summary>
    private static IReadOnlyList<string> PortalNotificationKinds =>
        Modules.Messaging.Entities.MessageKinds.CustomerConfigurable;

    public async Task<PortalResult<PortalNotificationPreferencesDto>> GetNotificationPreferencesAsync(
        CancellationToken cancellationToken)
    {
        if (await MyCustomerAsync(cancellationToken) is not { } me)
        {
            return PortalResult<PortalNotificationPreferencesDto>.NoCustomerLink();
        }

        var profile = await _dbContext.Set<Modules.Messaging.Entities.MessagingProfile>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId
                                      && p.OwnerType == Modules.Messaging.Entities.MessageOwnerType.Customer
                                      && p.OwnerId == me.CustomerId, cancellationToken);
        var enabled = profile?.EnabledKindsJson is { Length: > 0 } json
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(json)
            : null;
        return PortalResult<PortalNotificationPreferencesDto>.Success(new PortalNotificationPreferencesDto(
            profile?.EmailEnabled ?? true, profile?.SmsEnabled ?? false, profile?.PreferredLanguage,
            enabled, PortalNotificationKinds));
    }

    public async Task<PortalResult<PortalNotificationPreferencesDto>> SaveNotificationPreferencesAsync(
        SavePortalNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (await MyCustomerAsync(cancellationToken) is not { } me)
        {
            return PortalResult<PortalNotificationPreferencesDto>.NoCustomerLink();
        }

        var tenantId = _tenantContext.TenantId;
        var profile = await _dbContext.Set<Modules.Messaging.Entities.MessagingProfile>()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId
                                      && p.OwnerType == Modules.Messaging.Entities.MessageOwnerType.Customer
                                      && p.OwnerId == me.CustomerId, cancellationToken);
        if (profile is null)
        {
            profile = new Modules.Messaging.Entities.MessagingProfile
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                OwnerType = Modules.Messaging.Entities.MessageOwnerType.Customer, OwnerId = me.CustomerId,
            };
            _dbContext.Add(profile);
        }

        profile.EmailEnabled = request.EmailEnabled;
        profile.SmsEnabled = request.SmsEnabled;
        profile.PreferredLanguage = Common.SupportedLanguages.NormalizeOrNull(request.PreferredLanguage);
        // Only the customer-facing subset is toggleable from the portal; null = all kinds.
        profile.EnabledKindsJson = request.EnabledKinds is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(
                request.EnabledKinds.Where(k => PortalNotificationKinds.Contains(k)).Distinct().ToList());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetNotificationPreferencesAsync(cancellationToken);
    }
}
