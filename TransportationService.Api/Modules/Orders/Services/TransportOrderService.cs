using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
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
            [TransportOrderStatus.Confirmed] = [TransportOrderStatus.Draft, TransportOrderStatus.InProgress],
            [TransportOrderStatus.Planned] = [TransportOrderStatus.InProgress],
            [TransportOrderStatus.InProgress] = [TransportOrderStatus.Completed],
            [TransportOrderStatus.Completed] = [],
            [TransportOrderStatus.Cancelled] = [],
        };

    /// <summary>Statuses from which an order can still be cancelled.</summary>
    private static readonly TransportOrderStatus[] CancellableStatuses =
        [TransportOrderStatus.Draft, TransportOrderStatus.Confirmed, TransportOrderStatus.Planned, TransportOrderStatus.InProgress];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public TransportOrderService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
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
                x.o.GoodsDescription.ToLower().Contains(term));
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
                r.AdrRequired, r.CraneRequired);
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
            request.Stops, rejectBlockedCustomer: true, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        if (CargoItemsError(request.CargoItems) is { } cargoError)
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
            GoodsDescription = request.GoodsDescription.Trim(),
            Quantity = NonNegative(request.Quantity),
            QuantityUnit = Trim(request.QuantityUnit),
            WeightKg = NonNegative(request.WeightKg),
            VolumeM3 = NonNegative(request.VolumeM3),
            PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null,
            AdrRequired = request.AdrRequired,
            CraneRequired = request.CraneRequired,
            AgreedPrice = NonNegative(request.AgreedPrice),
            Notes = Trim(request.Notes),
            Stops = BuildStops(request.Stops),
        };

        _dbContext.Add(order);
        _dbContext.AddRange(BuildCargoItems(order.Id, request.CargoItems));
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

        if (order.Status is not (TransportOrderStatus.Draft or TransportOrderStatus.Confirmed))
        {
            return TransportOrderOperationResult.InvalidState(
                "Alleen concept- en bevestigde opdrachten kunnen worden bewerkt.");
        }

        // Switching an order TO a blocked customer is refused; editing an existing order whose
        // customer became blocked afterwards stays possible (dispatch still needs to manage it).
        var validation = await ValidateAsync(request.CustomerId, request.CustomerReference, request.GoodsDescription,
            request.Stops, rejectBlockedCustomer: request.CustomerId != order.CustomerId, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        // A confirmed order must keep satisfying the confirmation rules after the edit.
        if (order.Status == TransportOrderStatus.Confirmed && ConfirmationError(request.Stops) is { } confirmError)
        {
            return TransportOrderOperationResult.Invalid(confirmError);
        }

        if (CargoItemsError(request.CargoItems) is { } cargoError)
        {
            return TransportOrderOperationResult.Invalid(cargoError);
        }

        var before = new { order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count };

        order.CustomerId = request.CustomerId;
        order.CustomerReference = Trim(request.CustomerReference);
        order.OrderDate = request.OrderDate ?? order.OrderDate;
        order.GoodsDescription = request.GoodsDescription.Trim();
        order.Quantity = NonNegative(request.Quantity);
        order.QuantityUnit = Trim(request.QuantityUnit);
        order.WeightKg = NonNegative(request.WeightKg);
        order.VolumeM3 = NonNegative(request.VolumeM3);
        order.PalletCount = request.PalletCount is { } p ? Math.Max(0, p) : null;
        order.AdrRequired = request.AdrRequired;
        order.CraneRequired = request.CraneRequired;
        order.AgreedPrice = NonNegative(request.AgreedPrice);
        order.Notes = Trim(request.Notes);

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
        _dbContext.AddRange(BuildCargoItems(order.Id, request.CargoItems));

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, order.Id.ToString(), "Updated", before,
            new { order.CustomerId, order.GoodsDescription, StopCount = order.Stops.Count }, cancellationToken);

        return TransportOrderOperationResult.Success(await MapDetailAsync(order, cancellationToken));
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

    /// <summary>Shared shape validation for create/update. Returns null when valid.</summary>
    private async Task<TransportOrderOperationResult?> ValidateAsync(
        Guid customerId, string? customerReference, string goodsDescription,
        IReadOnlyList<TransportOrderStopInput> stops, bool rejectBlockedCustomer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(goodsDescription))
        {
            return TransportOrderOperationResult.Invalid("Een omschrijving van de goederen is verplicht.");
        }

        var customer = await _dbContext.Customers
            .Where(c => c.Id == customerId && c.TenantId == _tenantContext.TenantId)
            .Select(c => new { c.IsBlocked, c.CustomerReferenceRequired })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return TransportOrderOperationResult.InvalidReference("De gekoppelde klant bestaat niet.");
        }

        if (rejectBlockedCustomer && customer.IsBlocked)
        {
            return TransportOrderOperationResult.Invalid(
                "Deze klant is geblokkeerd; er kunnen geen opdrachten voor worden aangemaakt.");
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
    private static string? CargoItemsError(IReadOnlyList<CargoItemInput>? items)
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

    private List<CargoItem> BuildCargoItems(Guid orderId, IReadOnlyList<CargoItemInput>? inputs) =>
        (inputs ?? []).Select((input, index) => new CargoItem
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
        }).ToList();

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
            .Select(c => new CargoItemDto(c.Id, c.Sequence, c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes))
            .ToListAsync(cancellationToken);

        return new TransportOrderDetailDto(
            order.Id, order.OrderNumber, order.OrderDate, order.CustomerId, customerName,
            order.CustomerReference, order.Status, order.GoodsDescription,
            order.Quantity, order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount,
            order.AdrRequired, order.CraneRequired, order.AgreedPrice, order.Notes,
            order.CancellationReason,
            stops, cargoItems, Transitions[order.Status],
            CancellableStatuses.Contains(order.Status));
    }

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
