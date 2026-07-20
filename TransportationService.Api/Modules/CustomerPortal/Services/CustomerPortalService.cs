using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface ICustomerPortalService
{
    Task<PortalResult<PortalContextDto>> GetContextAsync(CancellationToken cancellationToken);

    Task<PortalResult<IReadOnlyList<PortalOrderListItemDto>>> ListMyOrdersAsync(CancellationToken cancellationToken);

    Task<PortalResult<PortalOrderDetailDto>> GetMyOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<PortalResult<PortalOrderDetailDto>> SubmitOrderAsync(PortalCreateOrderRequest request, CancellationToken cancellationToken);

    Task<PortalResult<IReadOnlyList<PortalLocationDto>>> ListMyLocationsAsync(CancellationToken cancellationToken);

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

    public CustomerPortalService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        ITransportOrderService orderService,
        ILocationService locationService,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _orderService = orderService;
        _locationService = locationService;
        _auditService = auditService;
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
        return customer is null
            ? PortalResult<PortalContextDto>.NoCustomerLink()
            : PortalResult<PortalContextDto>.Success(new PortalContextDto(customer.Value.CustomerId, customer.Value.CustomerName));
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

        return PortalResult<PortalOrderDetailDto>.Success(Trim(order));
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
            var owned = await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && l.CustomerId == customer.Value.CustomerId
                    && l.IsActive && locationIds.Contains(l.Id))
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

        var detail = await _orderService.GetByIdAsync(entity.Id, cancellationToken);
        return PortalResult<PortalOrderDetailDto>.Success(Trim(detail!));
    }

    public async Task<PortalResult<IReadOnlyList<PortalLocationDto>>> ListMyLocationsAsync(CancellationToken cancellationToken)
    {
        var customer = await MyCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return PortalResult<IReadOnlyList<PortalLocationDto>>.NoCustomerLink();
        }

        var locations = await _dbContext.Locations.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.CustomerId == customer.Value.CustomerId && l.IsActive)
            .OrderByDescending(l => l.IsDefaultLoadingLocation || l.IsDefaultUnloadingLocation)
            .ThenBy(l => l.Name)
            .Select(l => new PortalLocationDto(
                l.Id, l.Name, l.Street, l.HouseNumber, l.PostalCode, l.City, l.CountryCode,
                l.IsDefaultLoadingLocation, l.IsDefaultUnloadingLocation))
            .ToListAsync(cancellationToken);

        return PortalResult<IReadOnlyList<PortalLocationDto>>.Success(locations);
    }

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
            Notes: "Aangemaakt via het klantportaal"), cancellationToken);

        if (result.Outcome != LocationOperationOutcome.Success)
        {
            return PortalResult<PortalLocationDto>.Invalid("De locatie kon niet worden aangemaakt.");
        }

        var location = result.Location!;
        return PortalResult<PortalLocationDto>.Success(new PortalLocationDto(
            location.Id, location.Name, location.Street, location.HouseNumber, location.PostalCode,
            location.City, location.CountryCode, location.IsDefaultLoadingLocation, location.IsDefaultUnloadingLocation));
    }

    private static PortalOrderDetailDto Trim(TransportOrderDetailDto order) => new(
        order.Id, order.OrderNumber, order.OrderDate, order.Status, order.CustomerReference,
        order.GoodsDescription, order.Notes, order.CancellationReason,
        order.Stops.Select(s => new PortalStopDto(
            s.Sequence, s.StopType, s.LocationName, s.City, s.RequestedFrom, s.RequestedTo, s.Reference, s.Instructions)).ToList(),
        order.CargoItems.Select(c => new PortalCargoDto(
            c.Sequence, c.Description, c.ExpectedQuantity, c.QuantityUnit, c.UnitType, c.AdrRequired)).ToList());
}
