using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using L = TransportationService.Api.Modules.Dossiers.Services.DossierTimelineLabels;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierTimelineService
{
    /// <summary>The dossier's business story, oldest first; null when the dossier is not visible to the tenant.</summary>
    Task<IReadOnlyList<DossierTimelineEventDto>?> GetAsync(Guid dossierId, CancellationToken cancellationToken);
}

/// <summary>
/// T-H6 — read model that reconstructs a dossier's story from the trails that are ALREADY written:
/// the dossier row and its audit entries, its orders (audit, immutable status history, pricing
/// audit, invoices), the trips carrying those orders (audit + trip/order links), stop execution
/// history, proofs of delivery, issued transport documents and uploads, incidents and execution
/// exceptions. Nothing new is recorded and nothing is written.
///
/// Readability rules (a story, not a dump):
/// <list type="bullet">
///   <item>Order status steps a trip caused (planned/started at the same moment as the trip) are
///   folded into the trip event; an order completion caused by the trip completing is Systeem.</item>
///   <item>Only terminal stop outcomes are shown ("Stop 2 … afgerond"); intermediate steps and the
///   per-collo custody events (PodFinalized, scans) stay on the order's own history.</item>
///   <item>Automatic events are attributed to <c>System</c>, never to the person whose action
///   happened to trigger them (auto-confirmation, a trip closing after its last stop, an incident
///   created from a failed stop).</item>
///   <item>Every code is translated (<see cref="DossierTimelineLabels"/>); ids become numbers/names.</item>
/// </list>
/// Visibility: the timeline is part of the dossier (<c>dossiers.view</c>), so it shows exactly what
/// the dossier page shows — prices included (the dossier detail exposes them to every viewer) —
/// and nothing from HR: driver names come from the trip, never employee records beyond the name.
/// </summary>
public class DossierTimelineService : IDossierTimelineService
{
    /// <summary>How close two writes of one request are (cascades are saved within milliseconds).</summary>
    private static readonly TimeSpan SameMoment = TimeSpan.FromSeconds(10);

    private const string ActorUser = "User";
    private const string ActorSystem = "System";
    private const string ActorUnknown = "Unknown";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public DossierTimelineService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    private sealed class Event
    {
        public required string Id { get; init; }
        public required DateTime At { get; init; }
        public required string Category { get; init; }
        public required string Type { get; init; }
        public required string Title { get; set; }
        public string? Detail { get; set; }
        public Guid? UserId { get; set; }
        public bool System { get; set; }
        /// <summary>Tie-breaker for events of the same instant: cause before consequence.</summary>
        public int Rank { get; init; } = 5;
        public List<DossierTimelineFieldDto> Fields { get; } = [];

        public Event Field(string label, string? value, string? before = null)
        {
            if (!string.IsNullOrWhiteSpace(value) || !string.IsNullOrWhiteSpace(before))
            {
                Fields.Add(new DossierTimelineFieldDto(label, string.IsNullOrWhiteSpace(value) ? null : value, string.IsNullOrWhiteSpace(before) ? null : before));
            }

            return this;
        }
    }

    private sealed record OrderInfo(Guid Id, string Number, TransportOrderStatus Status, string? CancellationReason, DateTime CreatedAt, Guid? CreatedByUserId);

    private sealed record ActivityInfo(Guid Id, string Name);

    public async Task<IReadOnlyList<DossierTimelineEventDto>?> GetAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await _dbContext.TransportDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        var events = new List<Event>();

        // ---------------------------------------------------------------- scope of the dossier
        var typeRows = await _dbContext.ActivityTypes.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Code, t.Name })
            .ToListAsync(cancellationToken);
        var typeNameById = typeRows.ToDictionary(t => t.Id, t => t.Name);
        var typeNameByCode = typeRows.GroupBy(t => t.Code).ToDictionary(g => g.Key, g => g.First().Name);

        // Removed activities included: their audit rows still belong to the story.
        var activityRows = await _dbContext.DossierActivities.AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId)
            .Select(a => new { a.Id, a.ActivityTypeId, a.Label, a.LinkedTransportOrderId, a.IsDeleted })
            .ToListAsync(cancellationToken);
        var activities = activityRows.ToDictionary(
            a => a.Id,
            a => new ActivityInfo(a.Id, ActivityName(typeNameById.GetValueOrDefault(a.ActivityTypeId), a.Label)));

        var orderIds = (await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.DossierId == dossierId)
                .Select(l => l.TransportOrderId)
                .ToListAsync(cancellationToken))
            .Concat(activityRows.Where(a => !a.IsDeleted && a.LinkedTransportOrderId is not null).Select(a => a.LinkedTransportOrderId!.Value))
            .Distinct()
            .ToList();
        var orders = orderIds.Count == 0
            ? new Dictionary<Guid, OrderInfo>()
            : (await _dbContext.TransportOrders.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber, o.Status, o.CancellationReason, o.CreatedAt, o.CreatedByUserId })
                .ToListAsync(cancellationToken))
            .ToDictionary(o => o.Id, o => new OrderInfo(o.Id, o.OrderNumber, o.Status, o.CancellationReason, o.CreatedAt, o.CreatedByUserId));
        var orderKeys = orders.Keys.Select(k => k.ToString()).ToList();

        var tripLinks = orderIds.Count == 0
            ? []
            : await _dbContext.TripOrders.AsNoTracking().IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && orderIds.Contains(l.TransportOrderId))
                .Select(l => new { l.TripId, l.TransportOrderId, l.CreatedAt, l.CreatedByUserId, l.IsDeleted, l.DeletedAt, l.DeletedByUserId })
                .ToListAsync(cancellationToken);
        var tripIds = tripLinks.Select(l => l.TripId).Distinct().ToList();
        var trips = tripIds.Count == 0
            ? []
            : await _dbContext.Trips.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && tripIds.Contains(t.Id))
                .Select(t => new { t.Id, t.TripNumber, t.DriverId, t.VehicleId, t.TrailerId, t.CreatedAt, t.CreatedByUserId })
                .ToListAsync(cancellationToken);
        var tripNumber = trips.ToDictionary(t => t.Id, t => t.TripNumber);
        var tripKeys = tripIds.Select(k => k.ToString()).ToList();

        var stops = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .Select(s => new { s.Id, s.TransportOrderId, s.Sequence, s.StopType, s.City, s.LocationName })
                .ToListAsync(cancellationToken);
        var stopById = stops.ToDictionary(s => s.Id);
        string StopLabel(Guid stopId)
        {
            if (!stopById.TryGetValue(stopId, out var stop)) return "Een stop";
            var place = stop.City ?? stop.LocationName;
            return string.IsNullOrWhiteSpace(place)
                ? $"Stop {stop.Sequence} ({L.StopType(stop.StopType)})"
                : $"Stop {stop.Sequence} ({L.StopType(stop.StopType)}, {place.Trim()})";
        }
        string OrderNumber(Guid? id) => id is { } value && orders.TryGetValue(value, out var order) ? order.Number : "?";

        // ---------------------------------------------------------------- names referenced by the story
        // Only the drivers/vehicles/trailers the trips (now or in their audit trail) ever carried.
        var tripAudits = await AuditAsync(tenantId, "Trip", tripKeys, cancellationToken);
        var resourceIds = trips.SelectMany(t => new[] { t.DriverId, t.VehicleId, t.TrailerId })
            .Concat(tripAudits.SelectMany(a =>
            {
                var values = Json(a.NewValuesJson);
                return new[] { GuidOf(values, "DriverId"), GuidOf(values, "VehicleId"), GuidOf(values, "TrailerId") };
            }))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var driverNames = resourceIds.Count == 0 ? new Dictionary<Guid, string>() : await DriverNamesAsync(tenantId, resourceIds, cancellationToken);
        var vehicleNames = resourceIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.Vehicles.AsNoTracking().IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId && resourceIds.Contains(v.Id))
                .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                .ToDictionaryAsync(v => v.Id, v => Plate(v.InternalNumber, v.LicensePlate), cancellationToken);
        var trailerNames = resourceIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.Trailers.AsNoTracking().IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId && resourceIds.Contains(v.Id))
                .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                .ToDictionaryAsync(v => v.Id, v => Plate(v.InternalNumber, v.LicensePlate), cancellationToken);

        // ---------------------------------------------------------------- 1. the dossier itself
        events.Add(new Event
        {
            Id = $"dossier:{dossier.Id}:created", At = dossier.CreatedAt, Category = "dossier", Type = "dossier.created", Rank = 0,
            Title = $"Dossier {dossier.DossierNumber} aangemaakt", UserId = dossier.CreatedByUserId,
        }.Field("Titel", dossier.Title));

        var dossierAudits = await AuditAsync(tenantId, "TransportDossier", [dossierId.ToString()], cancellationToken);
        var referencedOrderIds = new HashSet<Guid>();
        var referencedDossierIds = new HashSet<Guid>();
        var referencedCustomerIds = new HashSet<Guid>();
        var referencedEntityIds = new HashSet<Guid>();
        var referencedUserIds = new HashSet<Guid>();
        foreach (var row in dossierAudits)
        {
            var after = Json(row.NewValuesJson);
            var before = Json(row.OldValuesJson);
            if (GuidOf(after, "TransportOrderId") is { } o) referencedOrderIds.Add(o);
            if (GuidOf(after, "TargetDossierId") is { } d) referencedDossierIds.Add(d);
            foreach (var c in new[] { GuidOf(after, "CustomerId"), GuidOf(before, "CustomerId") }) if (c is { } id) referencedCustomerIds.Add(id);
            foreach (var e in new[] { GuidOf(after, "LegalEntityId"), GuidOf(before, "LegalEntityId") }) if (e is { } id) referencedEntityIds.Add(id);
            foreach (var u in new[] { GuidOf(after, "ResponsibleUserId"), GuidOf(before, "ConfirmedByUserId") }) if (u is { } id) referencedUserIds.Add(id);
        }
        var otherOrderNumbers = referencedOrderIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.TransportOrders.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && referencedOrderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var dossierNumbers = referencedDossierIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.TransportDossiers.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && referencedDossierIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DossierNumber, cancellationToken);
        var customerNames = referencedCustomerIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.Customers.AsNoTracking().IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && referencedCustomerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var entityNames = referencedEntityIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _dbContext.LegalEntities.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId && referencedEntityIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.TradingName ?? e.LegalName, cancellationToken);

        // Standalone activity assignments (T-H7) name their operator and vehicle too.
        var activityResourceIds = dossierAudits
            .Where(r => r.Action == "ActivityAssigned")
            .SelectMany(r => { var v = Json(r.NewValuesJson); return new[] { GuidOf(v, "DriverId"), GuidOf(v, "VehicleId") }; })
            .Where(id => id is not null).Select(id => id!.Value).Where(id => !resourceIds.Contains(id)).Distinct().ToList();
        if (activityResourceIds.Count > 0)
        {
            foreach (var (id, name) in await DriverNamesAsync(tenantId, activityResourceIds, cancellationToken)) driverNames[id] = name;
            foreach (var v in await _dbContext.Vehicles.AsNoTracking().IgnoreQueryFilters()
                         .Where(v => v.TenantId == tenantId && activityResourceIds.Contains(v.Id))
                         .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate }).ToListAsync(cancellationToken))
            {
                vehicleNames[v.Id] = Plate(v.InternalNumber, v.LicensePlate);
            }
        }

        foreach (var row in dossierAudits)
        {
            var after = Json(row.NewValuesJson);
            var before = Json(row.OldValuesJson);
            var ev = DossierAuditEvent(row, before, after, activities, typeNameByCode,
                id => otherOrderNumbers.GetValueOrDefault(id, OrderNumber(id)), dossierNumbers, customerNames, entityNames,
                driverNames, vehicleNames);
            if (ev is not null) events.Add(ev);
        }

        // Standalone activity pricing (dossiers.price): its own audit stream, keyed by activity.
        var activityPricing = await AuditAsync(tenantId, "DossierActivityPricing", activities.Keys.Select(k => k.ToString()).ToList(), cancellationToken);
        foreach (var row in activityPricing)
        {
            var name = Guid.TryParse(row.EntityId, out var activityId) && activities.TryGetValue(activityId, out var activity) ? activity.Name : "een activiteit";
            var after = Json(row.NewValuesJson);
            var before = Json(row.OldValuesJson);
            var amount = Dec(after, "AgreedPrice");
            var free = Bool(after, "FreeConfirmed") == true;
            var lines = row.Action == "priceLinesSet";
            var ev = new Event
            {
                Id = $"audit:{row.Id}", At = row.Timestamp, Category = "pricing", UserId = row.UserId,
                Type = lines ? "activity.price_lines_set" : "activity.price_set",
                Title = free ? $"Activiteit {name} bevestigd als gratis"
                    : amount is null ? $"Prijs van activiteit {name} verwijderd"
                    : lines ? $"Verkooplijnen voor activiteit {name} vastgelegd: {L.Euro(amount.Value)}"
                    : $"Afgesproken prijs voor activiteit {name} vastgelegd: {L.Euro(amount.Value)}",
            };
            ev.Field("Prijs", amount is { } a ? L.Euro(a) : null, Dec(before, "AgreedPrice") is { } b ? L.Euro(b) : null);
            if (lines && Int(after, "LineCount") is { } count) ev.Field("Aantal verkooplijnen", count.ToString(CultureInfo.InvariantCulture));
            events.Add(ev);
        }

        // ---------------------------------------------------------------- 2. trips (planning)
        var tripEvents = new List<(Guid TripId, DateTime At, string Kind)>();
        var stopHistory = stops.Count == 0
            ? []
            : await StopHistoryAsync(tenantId, stops.Select(s => s.Id).ToList(), cancellationToken);
        foreach (var trip in trips)
        {
            var number = trip.TripNumber;
            Guid? driver = null, vehicle = null, trailer = null;
            var createdAudit = false;
            foreach (var row in tripAudits.Where(a => a.EntityId == trip.Id.ToString()))
            {
                var after = Json(row.NewValuesJson);
                if (Has(after, "DriverId")) driver = GuidOf(after, "DriverId");
                if (Has(after, "VehicleId")) vehicle = GuidOf(after, "VehicleId");
                if (Has(after, "TrailerId")) trailer = GuidOf(after, "TrailerId");
                string? overrideReason = Str(after, "OverrideReason") ?? (Bool(after, "Overridden") is null ? Str(after, "Overridden") : null);
                string Resources() => ResourcePhrase(driver is { } d ? driverNames.GetValueOrDefault(d) : null, vehicle is { } v ? vehicleNames.GetValueOrDefault(v) : null);

                Event? ev = null;
                switch (row.Action)
                {
                    case "Created":
                    {
                        createdAudit = true;
                        var withOrders = tripLinks
                            .Where(l => l.TripId == trip.Id && Near(l.CreatedAt, row.Timestamp))
                            .Select(l => OrderNumber(l.TransportOrderId)).Distinct().ToList();
                        ev = new Event
                        {
                            Id = $"audit:{row.Id}", At = row.Timestamp, Category = "planning", Type = "trip.created", Rank = 2, UserId = row.UserId,
                            Title = withOrders.Count > 0
                                ? $"Rit {number} aangemaakt voor {Plural(withOrders, "opdracht", "opdrachten")}"
                                : $"Rit {number} aangemaakt",
                        };
                        if (Date(after, "TripDate") is { } date) ev.Field("Ritdatum", date);
                        ev.Field("Chauffeur", driver is { } dr ? driverNames.GetValueOrDefault(dr) : null)
                          .Field("Voertuig", vehicle is { } ve ? vehicleNames.GetValueOrDefault(ve) : null);
                        tripEvents.Add((trip.Id, row.Timestamp, "created"));
                        break;
                    }
                    case "StatusChanged":
                    {
                        var status = EnumOf<TripStatus>(after, "Status");
                        var previous = EnumOf<TripStatus>(Json(row.OldValuesJson), "Status");
                        if (status is null) break;
                        var (type, title) = status.Value switch
                        {
                            TripStatus.Planned => ("trip.planned", $"Rit {number} gepland{Resources()}"),
                            TripStatus.InProgress => ("trip.started", $"Rit {number} gestart"),
                            TripStatus.Completed => ("trip.completed", $"Rit {number} voltooid"),
                            TripStatus.Cancelled => ("trip.cancelled", $"Rit {number} geannuleerd"),
                            _ => ("trip.status_changed", $"Rit {number} teruggezet naar concept"),
                        };
                        ev = new Event { Id = $"audit:{row.Id}", At = row.Timestamp, Category = "planning", Type = type, Rank = 3, UserId = row.UserId, Title = title };
                        if (status == TripStatus.Planned)
                        {
                            ev.Field("Chauffeur", driver is { } dr ? driverNames.GetValueOrDefault(dr) : null)
                              .Field("Voertuig", vehicle is { } ve ? vehicleNames.GetValueOrDefault(ve) : null)
                              .Field("Aanhangwagen", trailer is { } tr ? trailerNames.GetValueOrDefault(tr) : null);
                        }

                        if (status == TripStatus.Completed)
                        {
                            // The final stop closing the trip is automatic: the driver finished a stop, the system closed the trip.
                            var tripStopIds = stopHistory.Where(h => h.TripId == trip.Id).ToList();
                            if (tripStopIds.Any(h => L.StopOutcome(h.ToStatus) is not null && h.OccurredAt <= row.Timestamp && row.Timestamp - h.OccurredAt <= SameMoment))
                            {
                                ev.System = true;
                                ev.Title = $"Rit {number} voltooid na de laatste stop";
                            }
                        }

                        if (previous is { } p && status != TripStatus.Planned) ev.Field("Status", TripStatusLabel(status.Value), TripStatusLabel(p));
                        if (!string.IsNullOrWhiteSpace(overrideReason)) ev.Detail = $"Conflict bewust overschreven: {overrideReason.Trim()}";
                        tripEvents.Add((trip.Id, row.Timestamp, status.Value.ToString()));
                        break;
                    }
                    case "DriverChanged":
                        ev = Planning(row, "trip.driver_changed", driver is { } dr2 && driverNames.TryGetValue(dr2, out var dn)
                            ? $"Chauffeur van rit {number} gewijzigd naar {dn}" : $"Chauffeur van rit {number} verwijderd");
                        break;
                    case "VehicleChanged":
                        ev = Planning(row, "trip.vehicle_changed", vehicle is { } ve2 && vehicleNames.TryGetValue(ve2, out var vn)
                            ? $"Voertuig van rit {number} gewijzigd naar {vn}" : $"Voertuig van rit {number} verwijderd");
                        break;
                    case "TrailerChanged":
                        ev = Planning(row, "trip.trailer_changed", trailer is { } tr2 && trailerNames.TryGetValue(tr2, out var tn)
                            ? $"Aanhangwagen van rit {number} gewijzigd naar {tn}" : $"Aanhangwagen van rit {number} verwijderd");
                        break;
                    case "Rescheduled":
                        ev = Planning(row, "trip.rescheduled", Date(after, "TripDate") is { } newDate
                            ? $"Rit {number} verplaatst naar {newDate}" : $"Rit {number} verplaatst");
                        break;
                    case "Updated":
                        ev = Planning(row, "trip.updated", $"Rit {number} gewijzigd");
                        if (Date(after, "TripDate") is { } updatedDate) ev.Field("Ritdatum", updatedDate);
                        ev.Field("Chauffeur", driver is { } dr3 ? driverNames.GetValueOrDefault(dr3) : null)
                          .Field("Voertuig", vehicle is { } ve3 ? vehicleNames.GetValueOrDefault(ve3) : null);
                        break;
                    case "Deleted":
                        ev = Planning(row, "trip.deleted", $"Rit {number} verwijderd");
                        break;
                    // OrdersAssigned/OrderRemoved come from the trip–order links below (they name the
                    // order); OrdersReordered is planning-board noise for the dossier story.
                }

                if (ev is not null)
                {
                    if (ev.Detail is null && !string.IsNullOrWhiteSpace(overrideReason) && row.Action != "StatusChanged")
                    {
                        ev.Detail = $"Conflict bewust overschreven: {overrideReason.Trim()}";
                    }

                    events.Add(ev);
                }
            }

            if (!createdAudit)
            {
                // Older trips without an audit trail still tell when they were made.
                events.Add(new Event
                {
                    Id = $"trip:{trip.Id}:created", At = trip.CreatedAt, Category = "planning", Type = "trip.created", Rank = 2,
                    UserId = trip.CreatedByUserId, Title = $"Rit {number} aangemaakt",
                });
                tripEvents.Add((trip.Id, trip.CreatedAt, "created"));
            }
        }

        foreach (var link in tripLinks)
        {
            var number = tripNumber.GetValueOrDefault(link.TripId, "?");
            var createdWithTrip = tripEvents.Any(t => t.TripId == link.TripId && t.Kind == "created" && Near(t.At, link.CreatedAt));
            if (!createdWithTrip)
            {
                events.Add(new Event
                {
                    Id = $"triporder:{link.TripId}:{link.TransportOrderId}:{link.CreatedAt.Ticks}", At = link.CreatedAt, Category = "planning", Type = "trip.order_added", Rank = 2,
                    UserId = link.CreatedByUserId, Title = $"Opdracht {OrderNumber(link.TransportOrderId)} ingepland op rit {number}",
                });
                tripEvents.Add((link.TripId, link.CreatedAt, "assigned"));
            }

            if (link.IsDeleted && link.DeletedAt is { } removedAt)
            {
                events.Add(new Event
                {
                    Id = $"triporder:{link.TripId}:{link.TransportOrderId}:{removedAt.Ticks}:removed", At = removedAt, Category = "planning", Type = "trip.order_removed", Rank = 2,
                    UserId = link.DeletedByUserId, Title = $"Opdracht {OrderNumber(link.TransportOrderId)} van rit {number} gehaald",
                });
            }
        }

        // ---------------------------------------------------------------- 3. execution (terminal stop outcomes)
        foreach (var row in stopHistory)
        {
            if (L.StopOutcome(row.ToStatus) is not { } outcome || !stopById.TryGetValue(row.StopId, out var stop)) continue;
            events.Add(new Event
            {
                Id = $"stop:{row.Id}", At = row.OccurredAt, Category = "planning", Type = "stop." + row.ToStatus.ToString().ToLowerInvariant(), Rank = 1,
                UserId = row.UserId,
                Title = $"{StopLabel(row.StopId)} van opdracht {OrderNumber(stop.TransportOrderId)} {outcome}",
                Detail = string.IsNullOrWhiteSpace(row.Reason) ? null : $"Reden: {row.Reason.Trim()}",
            }.Field("Rit", tripNumber.GetValueOrDefault(row.TripId)));
        }

        // ---------------------------------------------------------------- 4. orders
        var orderAudits = await AuditAsync(tenantId, "TransportOrder", orderKeys, cancellationToken);
        var statusHistory = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStatusHistories.AsNoTracking()
                .Where(h => h.TenantId == tenantId && orderIds.Contains(h.TransportOrderId))
                .ToListAsync(cancellationToken);
        var tripIdsByOrder = tripLinks.GroupBy(l => l.TransportOrderId).ToDictionary(g => g.Key, g => g.Select(l => l.TripId).ToHashSet());
        bool TripMoment(Guid orderId, DateTime at, params string[] kinds) =>
            tripIdsByOrder.TryGetValue(orderId, out var ids)
            && tripEvents.Any(t => ids.Contains(t.TripId) && (kinds.Length == 0 || kinds.Contains(t.Kind)) && Near(t.At, at));

        foreach (var order in orders.Values)
        {
            var mine = orderAudits.Where(a => a.EntityId == order.Id.ToString()).ToList();
            var portal = mine.FirstOrDefault(a => a.Action == "PortalSubmitted");
            var created = mine.FirstOrDefault(a => a.Action == "Created");
            if (portal is not null)
            {
                events.Add(new Event
                {
                    Id = $"audit:{portal.Id}", At = portal.Timestamp, Category = "orders", Type = "order.portal_submitted", Rank = 1, UserId = portal.UserId,
                    Title = $"Transportaanvraag {order.Number} ingediend via het klantportaal",
                });
            }
            else
            {
                events.Add(new Event
                {
                    Id = created is null ? $"order:{order.Id}:created" : $"audit:{created.Id}", At = created?.Timestamp ?? order.CreatedAt,
                    Category = "orders", Type = "order.created", Rank = 1, UserId = created is null ? order.CreatedByUserId : created.UserId,
                    Title = $"Transportopdracht {order.Number} aangemaakt",
                });
            }

            foreach (var row in mine)
            {
                var after = Json(row.NewValuesJson);
                var title = row.Action switch
                {
                    "Updated" => $"Transportopdracht {order.Number} gewijzigd",
                    "CustomerChanged" => $"Klant van opdracht {order.Number} gewijzigd",
                    "LegalEntityChanged" => $"Facturerende entiteit van opdracht {order.Number} gewijzigd",
                    "PriorityChanged" => $"Prioriteit van opdracht {order.Number} gewijzigd",
                    "PortalInfoRequested" => $"Extra informatie gevraagd aan de klant over opdracht {order.Number}",
                    "Deleted" => $"Transportopdracht {order.Number} verwijderd",
                    _ => null,
                };
                if (row.Action == "DieselSurchargeOverridden")
                {
                    events.Add(new Event { Id = $"audit:{row.Id}", At = row.Timestamp, Category = "pricing", Type = "order.diesel_overridden", UserId = row.UserId, Title = $"Dieseltoeslag van opdracht {order.Number} handmatig aangepast" });
                    continue;
                }

                if (title is null) continue;
                var reason = Str(after, "Reason");
                events.Add(new Event
                {
                    Id = $"audit:{row.Id}", At = row.Timestamp, Category = "orders", Type = "order." + Snake(row.Action), UserId = row.UserId, Title = title,
                    Detail = string.IsNullOrWhiteSpace(reason) ? null : $"Reden: {reason.Trim()}",
                });
            }

            foreach (var row in statusHistory.Where(h => h.TransportOrderId == order.Id))
            {
                var ev = OrderStatusEvent(row, order, portal is not null, TripMoment);
                if (ev is not null) events.Add(ev);
            }
        }

        // Order pricing (snapshot / one-off price / sales lines) and invoices.
        var pricingAudits = await AuditAsync(tenantId, "OrderPricing", orderKeys, cancellationToken);
        foreach (var row in pricingAudits)
        {
            var number = Guid.TryParse(row.EntityId, out var orderId) ? OrderNumber(orderId) : "?";
            var after = Json(row.NewValuesJson);
            var before = Json(row.OldValuesJson);
            var amount = Dec(after, "AgreedPrice");
            Event? ev = row.Action switch
            {
                "oneOffPriceSet" => Pricing(row, "order.price_set", amount is { } a1
                    ? $"Afgesproken prijs voor opdracht {number} vastgelegd: {L.Euro(a1)}"
                    : $"Afgesproken prijs voor opdracht {number} verwijderd"),
                "lines_adjusted" => Pricing(row, "order.sales_lines_set", $"Verkooplijnen van opdracht {number} aangepast"),
                "recalculated" => Pricing(row, "order.price_recalculated", $"Prijs van opdracht {number} herberekend")
                    .Field("Berekende prijs", Dec(after, "CalculatedPrice") is { } calc ? L.Euro(calc) : null)
                    .Field("Afgesproken prijs", amount is { } a2 ? L.Euro(a2) : null),
                "status_changed" => Pricing(row, "order.price_status_changed", $"Prijs van opdracht {number} {L.OrderPricingStatus(Str(after, "Status"))}"),
                "price_confirmed" => Pricing(row, "order.price_confirmed", Dec(after, "LinesTotal") is { } total
                    ? $"Prijs van opdracht {number} bevestigd: {L.Euro(total)}"
                    : $"Prijs van opdracht {number} bevestigd"),
                "price_reopened" => Pricing(row, "order.price_reopened", $"Prijs van opdracht {number} heropend"),
                "line_confirmed" => Pricing(row, "order.sales_line_confirmed", Str(before, "Label") is { } label
                    ? $"Verkooplijn '{label}' van opdracht {number} bevestigd"
                    : $"Verkooplijn van opdracht {number} bevestigd")
                    .Field("Bedrag", Dec(after, "Amount") is { } lineAmount ? L.Euro(lineAmount) : null),
                _ => null, // cargo_links_set: which goods a line covers — detail for the price screen
            };
            if (ev is null) continue;
            if (Str(after, "Reason") is { } why && !string.IsNullOrWhiteSpace(why)) ev.Detail = $"Reden: {why.Trim()}";
            events.Add(ev);
        }

        var activityIds = activities.Keys.ToList();
        var invoices = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId
                        && ((l.TransportOrderId != null && orderIds.Contains(l.TransportOrderId.Value))
                            || (l.DossierActivityId != null && activityIds.Contains(l.DossierActivityId.Value))))
            .Join(_dbContext.Invoices.AsNoTracking(), l => l.InvoiceId, i => i.Id, (l, i) => new { i.Id, i.InvoiceNumber, i.CreatedAt, i.CreatedByUserId })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
        {
            events.Add(new Event
            {
                Id = $"invoice:{invoice.Id}", At = invoice.CreatedAt, Category = "pricing", Type = "invoice.created", UserId = invoice.CreatedByUserId,
                Title = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "Factuur aangemaakt" : $"Factuur {invoice.InvoiceNumber} aangemaakt",
            });
        }

        // ---------------------------------------------------------------- 5. documents and POD
        var issued = await _dbContext.IssuedTransportDocuments.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && (d.DossierId == dossierId || orderIds.Contains(d.TransportOrderId)))
            .ToListAsync(cancellationToken);
        foreach (var document in issued)
        {
            events.Add(new Event
            {
                Id = $"issued:{document.Id}", At = document.IssuedAt, Category = "documents", Type = "document.issued", UserId = document.CreatedByUserId,
                Title = $"{L.IssuedDocumentKind(document.Kind)} {document.DocumentNumber} uitgegeven voor opdracht {OrderNumber(document.TransportOrderId)}",
            }.Field("Extern nummer", document.ExternalNumber));
        }

        var uploads = await _dbContext.TransportOrderDocuments.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId
                        && (d.DossierId == dossierId
                            || (d.TransportOrderId != null && orderIds.Contains(d.TransportOrderId.Value))
                            || (d.DossierActivityId != null && activityIds.Contains(d.DossierActivityId.Value))))
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.CustomTypeName, d.TransportOrderId, d.DossierActivityId, d.FileName, d.CreatedAt, d.CreatedByUserId, d.IsDeleted, d.DeletedAt, d.DeletedByUserId })
            .ToListAsync(cancellationToken);
        var uploadAudits = await AuditAsync(tenantId, "TransportOrderDocument", uploads.Select(u => u.Id.ToString()).ToList(), cancellationToken);
        foreach (var document in uploads)
        {
            var scope = document.TransportOrderId is { } docOrder ? $"opdracht {OrderNumber(docOrder)}"
                : document.DossierActivityId is { } docActivity && activities.TryGetValue(docActivity, out var act) ? $"activiteit {act.Name}"
                : "het dossier";
            var label = L.DocumentType(document.DocumentType, document.CustomTypeName);
            var name = string.IsNullOrWhiteSpace(document.Title) ? label : $"'{document.Title.Trim()}'";
            var files = uploadAudits.Where(a => a.EntityId == document.Id.ToString() && a.Action == "FileAttached").ToList();
            var firstFile = files.FirstOrDefault(f => Near(f.Timestamp, document.CreatedAt));
            events.Add(new Event
            {
                Id = $"document:{document.Id}:created", At = document.CreatedAt, Category = "documents", Type = "document.added", UserId = document.CreatedByUserId,
                Title = $"Document {name} toegevoegd aan {scope}",
            }.Field("Soort", label).Field("Bestand", firstFile is null ? null : Str(Json(firstFile.NewValuesJson), "fileName") ?? document.FileName));
            foreach (var file in files.Where(f => f != firstFile))
            {
                events.Add(new Event
                {
                    Id = $"audit:{file.Id}", At = file.Timestamp, Category = "documents", Type = "document.file_attached", UserId = file.UserId,
                    Title = $"Bestand toegevoegd aan document {name}",
                }.Field("Bestand", Str(Json(file.NewValuesJson), "fileName")));
            }

            if (document.IsDeleted && document.DeletedAt is { } deletedAt)
            {
                events.Add(new Event
                {
                    Id = $"document:{document.Id}:deleted", At = deletedAt, Category = "documents", Type = "document.deleted", UserId = document.DeletedByUserId,
                    Title = $"Document {name} verwijderd van {scope}",
                });
            }
        }

        var pods = orderIds.Count == 0
            ? []
            : await _dbContext.ProofsOfDelivery.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && orderIds.Contains(p.TransportOrderId))
                .Select(p => new { p.Id, p.TransportOrderId, p.TransportOrderStopId, p.TripId, p.RecipientName, p.Outcome, p.DamageReported, p.MissingReported, p.DeliveredAt, p.CreatedAt, p.FinalisedByUserId, p.CreatedByUserId, p.CorrectedFromPodId, p.CorrectionReason })
                .ToListAsync(cancellationToken);
        foreach (var pod in pods)
        {
            var corrected = pod.CorrectedFromPodId is not null;
            var ev = new Event
            {
                Id = $"pod:{pod.Id}", At = pod.CreatedAt, Category = "documents", Rank = 1,
                Type = corrected ? "pod.corrected" : "pod.registered",
                UserId = pod.FinalisedByUserId ?? pod.CreatedByUserId,
                Title = corrected
                    ? $"Afleverbewijs gecorrigeerd voor {StopLabel(pod.TransportOrderStopId).ToLowerInvariantFirst()} van opdracht {OrderNumber(pod.TransportOrderId)}"
                    : $"Afleverbewijs geregistreerd voor {StopLabel(pod.TransportOrderStopId).ToLowerInvariantFirst()} van opdracht {OrderNumber(pod.TransportOrderId)}",
                Detail = corrected && !string.IsNullOrWhiteSpace(pod.CorrectionReason) ? $"Reden: {pod.CorrectionReason.Trim()}" : null,
            };
            ev.Field("Ontvanger", pod.RecipientName)
              .Field("Resultaat", L.PodOutcome(pod.Outcome))
              .Field("Schade gemeld", pod.DamageReported ? L.YesNo(true) : null)
              .Field("Ontbrekende goederen gemeld", pod.MissingReported ? L.YesNo(true) : null)
              .Field("Rit", tripNumber.GetValueOrDefault(pod.TripId));
            events.Add(ev);
        }

        // ---------------------------------------------------------------- 6. incidents and afwijkingen
        var incidents = await _dbContext.Incidents.AsNoTracking().IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && !i.IsDeleted
                        && (i.DossierId == dossierId || (i.TransportOrderId != null && orderIds.Contains(i.TransportOrderId.Value))))
            .Select(i => new { i.Id, i.Title, i.IncidentType, i.CustomTypeName, i.Severity, i.CreatedAt, i.CreatedByUserId, i.Resolution })
            .ToListAsync(cancellationToken);
        var incidentAudits = await AuditAsync(tenantId, "Incident", incidents.Select(i => i.Id.ToString()).ToList(), cancellationToken);
        foreach (var incident in incidents)
        {
            var name = string.IsNullOrWhiteSpace(incident.Title) ? "Incident" : $"Incident '{incident.Title.Trim()}'";
            var mine = incidentAudits.Where(a => a.EntityId == incident.Id.ToString()).ToList();
            var auto = mine.FirstOrDefault(a => a.Action == "AutoCreatedFromFailedStop");
            var created = mine.FirstOrDefault(a => a.Action == "Created");
            var createdEvent = new Event
            {
                Id = $"incident:{incident.Id}:created", At = auto?.Timestamp ?? created?.Timestamp ?? incident.CreatedAt, Category = "incidents", Rank = 2,
                Type = auto is not null ? "incident.auto_created" : "incident.created",
                UserId = auto is not null ? null : created?.UserId ?? incident.CreatedByUserId,
                System = auto is not null,
                Title = auto is not null ? $"{name} automatisch aangemaakt na een mislukte stop" : $"{name} gemeld",
            };
            createdEvent.Field("Soort", L.IncidentType(incident.IncidentType, incident.CustomTypeName)).Field("Ernst", L.IncidentSeverity(incident.Severity));
            if (auto is not null && Str(Json(auto.NewValuesJson), "Reason") is { } autoReason && !string.IsNullOrWhiteSpace(autoReason))
            {
                createdEvent.Detail = $"Reden: {autoReason.Trim()}";
            }

            events.Add(createdEvent);

            foreach (var row in mine)
            {
                var after = Json(row.NewValuesJson);
                Event? ev = null;
                if (row.Action == "StatusChanged")
                {
                    var status = EnumOf<IncidentStatus>(after, "Status");
                    var (type, title) = status switch
                    {
                        IncidentStatus.InProgress => ("incident.in_progress", $"{name} in behandeling genomen"),
                        IncidentStatus.Resolved => ("incident.resolved", $"{name} opgelost"),
                        IncidentStatus.Cancelled => ("incident.cancelled", $"{name} geannuleerd"),
                        IncidentStatus.New => ("incident.reopened", $"{name} heropend"),
                        _ => ("incident.status_changed", $"Status van {name.ToLowerInvariantFirst()} gewijzigd"),
                    };
                    ev = new Event { Id = $"audit:{row.Id}", At = row.Timestamp, Category = "incidents", Type = type, UserId = row.UserId, Title = title };
                    var resolution = Str(after, "Resolution");
                    if (!string.IsNullOrWhiteSpace(resolution)) ev.Detail = $"Oplossing: {resolution.Trim()}";
                }
                else if (row.Action == "RedeliveryCreated")
                {
                    var redelivery = Str(after, "OrderNumber");
                    ev = new Event
                    {
                        Id = $"audit:{row.Id}", At = row.Timestamp, Category = "incidents", Type = "incident.redelivery_created", UserId = row.UserId,
                        Title = string.IsNullOrWhiteSpace(redelivery) ? $"Herlevering aangemaakt voor {name.ToLowerInvariantFirst()}" : $"Herlevering {redelivery} aangemaakt voor {name.ToLowerInvariantFirst()}",
                    };
                }

                // Updated / charge decisions are the incident's own (financial) follow-up, not the dossier story.
                if (ev is not null) events.Add(ev);
            }
        }

        var exceptions = orderIds.Count == 0
            ? []
            : await _dbContext.ExecutionExceptions.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.TransportOrderId != null && orderIds.Contains(x.TransportOrderId.Value))
                .Select(x => new { x.Id, x.Type, x.Severity, x.Description, x.OccurredAt, x.ReportedByUserId, x.CreatedByUserId, x.TransportOrderId, x.TransportOrderStopId })
                .ToListAsync(cancellationToken);
        var exceptionAudits = await AuditAsync(tenantId, "ExecutionException", exceptions.Select(x => x.Id.ToString()).ToList(), cancellationToken);
        foreach (var exception in exceptions)
        {
            var kind = L.ExceptionType(exception.Type);
            var where = exception.TransportOrderStopId is { } stopId && stopById.ContainsKey(stopId)
                ? $" bij {StopLabel(stopId).ToLowerInvariantFirst()} van opdracht {OrderNumber(exception.TransportOrderId)}"
                : $" bij opdracht {OrderNumber(exception.TransportOrderId)}";
            events.Add(new Event
            {
                Id = $"exception:{exception.Id}", At = exception.OccurredAt, Category = "incidents", Type = "exception.reported", Rank = 1,
                UserId = exception.ReportedByUserId ?? exception.CreatedByUserId,
                Title = $"Afwijking gemeld: {kind.ToLowerInvariantFirst()}{where}",
                Detail = string.IsNullOrWhiteSpace(exception.Description) ? null : exception.Description.Trim(),
            }.Field("Ernst", L.ExceptionSeverity(exception.Severity)));

            foreach (var row in exceptionAudits.Where(a => a.EntityId == exception.Id.ToString() && a.Action == "StatusChanged"))
            {
                var after = Json(row.NewValuesJson);
                var (type, verb) = EnumOf<ExecutionExceptionStatus>(after, "Status") switch
                {
                    ExecutionExceptionStatus.Resolved => ("exception.resolved", "afgehandeld"),
                    ExecutionExceptionStatus.Rejected => ("exception.rejected", "afgewezen"),
                    ExecutionExceptionStatus.Investigating => ("exception.investigating", "in onderzoek"),
                    ExecutionExceptionStatus.CustomerActionRequired => ("exception.customer_action", "wacht op actie van de klant"),
                    _ => ("exception.reopened", "heropend"),
                };
                var note = Str(after, "Note");
                events.Add(new Event
                {
                    Id = $"audit:{row.Id}", At = row.Timestamp, Category = "incidents", Type = type, UserId = row.UserId,
                    Title = $"Afwijking ({kind.ToLowerInvariantFirst()}) {verb}",
                    Detail = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                });
            }
        }

        // ---------------------------------------------------------------- actors
        var userIds = events.Where(e => !e.System && e.UserId is not null).Select(e => e.UserId!.Value)
            .Concat(referencedUserIds).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FirstName + u.LastName) ? u.Email : $"{u.FirstName} {u.LastName}".Trim());
        foreach (var ev in events)
        {
            // Placeholders written while the names were not loaded yet ("{user:<id>}").
            for (var i = 0; i < ev.Fields.Count; i++)
            {
                var field = ev.Fields[i];
                ev.Fields[i] = field with { Value = ResolveUser(field.Value, users), Before = ResolveUser(field.Before, users) };
            }
        }

        return events
            .OrderBy(e => e.At)
            .ThenBy(e => e.Rank)
            .Select(e =>
            {
                var (kind, name) = e.System || e.UserId is null
                    ? (ActorSystem, (string?)null)
                    : users.TryGetValue(e.UserId.Value, out var resolved) ? (ActorUser, resolved) : (ActorUnknown, null);
                return new DossierTimelineEventDto(e.Id, DateTime.SpecifyKind(e.At, DateTimeKind.Utc), e.Category, e.Type, e.Title, e.Detail, kind, name, e.Fields);
            })
            .ToList();
    }

    // ==================================================================== event builders

    private static Event? DossierAuditEvent(
        AuditLog row, JsonElement? before, JsonElement? after, IReadOnlyDictionary<Guid, ActivityInfo> activities,
        IReadOnlyDictionary<string, string> typeNameByCode, Func<Guid, string> orderNumber,
        IReadOnlyDictionary<Guid, string> dossierNumbers, IReadOnlyDictionary<Guid, string> customerNames,
        IReadOnlyDictionary<Guid, string> entityNames,
        IReadOnlyDictionary<Guid, string> driverNames, IReadOnlyDictionary<Guid, string> vehicleNames)
    {
        Event Make(string type, string title, string category = "dossier", int rank = 5) =>
            new() { Id = $"audit:{row.Id}", At = row.Timestamp, Category = category, Type = type, Title = title, UserId = row.UserId, Rank = rank };
        string ActivityFromPayload()
        {
            if (GuidOf(after, "Id") is { } id && activities.TryGetValue(id, out var known)) return known.Name;
            var typeName = Str(after, "ActivityType") is { } code ? typeNameByCode.GetValueOrDefault(code, "Activiteit") : "Activiteit";
            return ActivityName(typeName, Str(after, "Label"));
        }
        string? Reason() => Str(after, "Reason") is { } r && !string.IsNullOrWhiteSpace(r) ? r.Trim() : null;
        string? Customer(JsonElement? values) => GuidOf(values, "CustomerId") is { } id ? customerNames.GetValueOrDefault(id) : null;
        string? Entity(JsonElement? values) => GuidOf(values, "LegalEntityId") is { } id ? entityNames.GetValueOrDefault(id) : null;

        switch (row.Action)
        {
            case "Created":
            case "ActivityOrderCreated":
                // Created: told by the dossier row itself (wrapper dossiers have no audit row).
                // ActivityOrderCreated: told by the order's own "aangemaakt" event.
                return null;
            case "Updated":
            {
                var ev = Make("dossier.updated", "Dossiergegevens gewijzigd");
                ev.Field("Titel", Str(after, "Title"))
                  .Field("Klant", Customer(after))
                  .Field("Klantreferentie", Str(after, "CustomerReference"))
                  .Field("Dossierdatum", Date(after, "DossierDate"))
                  .Field("Verantwoordelijke", GuidOf(after, "ResponsibleUserId") is { } responsible ? $"{{user:{responsible}}}" : null);
                return ev;
            }
            case "LegalEntityChanged":
            {
                var ev = Make("dossier.legal_entity_changed", "Facturerende entiteit gewijzigd");
                ev.Field("Facturerende entiteit", Entity(after), Entity(before));
                var moved = StrList(after, "OrdersMoved");
                if (moved.Count > 0) ev.Field("Mee verplaatste opdrachten", string.Join(", ", moved));
                ev.Detail = Reason() is { } reason ? $"Reden: {reason}" : null;
                return ev;
            }
            case "CustomerChanged":
            {
                var ev = Make("dossier.customer_changed", Customer(after) is { } name ? $"Klant gewijzigd naar {name}" : "Klant gewijzigd");
                ev.Field("Klant", Customer(after), Customer(before)).Field("Facturerende entiteit", Entity(after), Entity(before));
                ev.Detail = Reason() is { } reason ? $"Reden: {reason}" : null;
                return ev;
            }
            case "OrderLinked":
                return Make("dossier.order_linked", GuidOf(after, "TransportOrderId") is { } linked
                    ? $"Transportopdracht {orderNumber(linked)} gekoppeld aan het dossier" : "Transportopdracht gekoppeld aan het dossier", "orders");
            case "OrderUnlinked":
                return Make("dossier.order_unlinked", GuidOf(after, "TransportOrderId") is { } unlinked
                    ? $"Transportopdracht {orderNumber(unlinked)} losgekoppeld van het dossier" : "Transportopdracht losgekoppeld van het dossier", "orders");
            case "RelationAdded":
            case "RelationRemoved":
            {
                var target = GuidOf(after, "TargetDossierId") is { } t ? dossierNumbers.GetValueOrDefault(t) : null;
                var added = row.Action == "RelationAdded";
                return Make(added ? "dossier.relation_added" : "dossier.relation_removed",
                    target is null
                        ? added ? "Verwant dossier toegevoegd" : "Verwant dossier verwijderd"
                        : added ? $"Verwant dossier {target} toegevoegd" : $"Verwant dossier {target} verwijderd");
            }
            case "ActivityAdded":
                return Make("activity.added", $"Activiteit {ActivityFromPayload()} toegevoegd");
            case "ActivityUpdated":
            {
                var ev = Make("activity.updated", $"Activiteit {ActivityFromPayload()} gewijzigd");
                ev.Field("Omschrijving", Str(after, "Label"))
                  .Field("Geplande datum", Date(after, "PlannedDate"))
                  .Field("Duur (uren)", Dec(after, "DurationHours")?.ToString("0.##", CultureInfo.GetCultureInfo("nl-BE")));
                return ev;
            }
            case "ActivityRemoved":
                return Make("activity.removed", $"Activiteit {ActivityFromPayload()} verwijderd");
            // T-H7: standalone activities are executed like orders — their lifecycle is the story too.
            case "ActivityAssigned":
            {
                var driver = GuidOf(after, "DriverId") is { } d ? driverNames.GetValueOrDefault(d) : null;
                var vehicle = GuidOf(after, "VehicleId") is { } v ? vehicleNames.GetValueOrDefault(v) : null;
                var ev = Make("activity.assigned", $"Activiteit {ActivityFromPayload()} toegewezen", "planning");
                ev.Field("Chauffeur", driver).Field("Voertuig", vehicle);
                if (StrList(after, "OverriddenConflicts").Count > 0) ev.Detail = "Planningsconflict bewust overschreven";
                return ev;
            }
            case "ActivityStarted":
                return Make("activity.started", $"Activiteit {ActivityFromPayload()} gestart", "planning");
            case "ActivityCompleted":
                return Make("activity.completed", $"Activiteit {ActivityFromPayload()} uitgevoerd", "planning", rank: 7);
            case "ActivityCancelled":
            {
                var ev = Make("activity.cancelled", $"Activiteit {ActivityFromPayload()} geannuleerd", "planning");
                ev.Detail = Str(after, "CancellationReason") is { } reason && !string.IsNullOrWhiteSpace(reason) ? $"Reden: {reason.Trim()}" : null;
                return ev;
            }
            case "ActivityReopened":
                return Make("activity.reopened", $"Activiteit {ActivityFromPayload()} heropend", "planning");
            case DossierLifecycleService.ActionConfirmedManually:
            {
                var ev = Make("dossier.confirmed_manually", "Dossier handmatig bevestigd", rank: 9);
                var acknowledged = StrList(after, "WarningsAcknowledged");
                if (acknowledged.Count > 0)
                {
                    ev.Field("Bewust bevestigde aandachtspunten", string.Join("; ", acknowledged.Select(DossierTimelineLabels.ConfirmationCheck).Distinct()));
                }

                ev.Detail = Reason() is { } reason ? $"Opmerking: {reason}" : null;
                return ev;
            }
            case DossierLifecycleService.ActionConfirmedAutomatically:
            {
                // Always the system: the person whose stop/trip completion triggered the evaluation did
                // not confirm anything (product acceptance T-H6). They remain visible as the cause.
                var trigger = DossierTimelineLabels.AutoConfirmTrigger(Str(after, "Trigger"));
                var ev = Make("dossier.confirmed_automatically", trigger is null ? "Dossier automatisch bevestigd" : $"Dossier automatisch bevestigd {trigger}", rank: 9);
                ev.System = true;
                if (Int(after, "CompletedOrders") is { } completed and > 0) ev.Field("Voltooide opdrachten", completed.ToString(CultureInfo.InvariantCulture));
                if (Int(after, "ExecutedActivities") is { } executed and > 0) ev.Field("Uitgevoerde activiteiten", executed.ToString(CultureInfo.InvariantCulture));
                if (row.UserId is { } cause) ev.Field("Aanleiding", $"Laatste handeling door {{user:{cause}}}");
                var openExceptions = StrList(after, "OpenExceptions");
                if (openExceptions.Count > 0) ev.Field("Open afwijkingen bij bevestiging", string.Join("; ", openExceptions));
                return ev;
            }
            case DossierLifecycleService.ActionReopened:
            {
                var ev = Make("dossier.reopened", "Dossier heropend", rank: 9);
                var previous = EnumOf<DossierStatus>(before, "Status");
                ev.Field("Status", "Open", previous is { } p ? DossierTimelineLabels.DossierStatus(p) : null);
                if (previous == DossierStatus.Closed)
                {
                    ev.Field("Eerder bevestigd op", DateTimeOf(before, "ClosedAt") is { } closedAt ? DossierTimelineLabels.Date(closedAt) : null)
                      .Field("Eerdere bevestiging", Str(before, "ConfirmationSource") switch
                      {
                          "Automatic" => "Automatisch",
                          "Manual" => "Handmatig",
                          _ => null,
                      })
                      .Field("Eerder bevestigd door", GuidOf(before, "ConfirmedByUserId") is { } by ? $"{{user:{by}}}" : null)
                      .Field("Opmerking bij de bevestiging", Str(before, "ConfirmationReason"));
                }
                else if (previous == DossierStatus.Cancelled)
                {
                    ev.Field("Eerder geannuleerd op", DateTimeOf(before, "CancelledAt") is { } cancelledAt ? DossierTimelineLabels.Date(cancelledAt) : null)
                      .Field("Eerdere annuleringsreden", Str(before, "CancellationReason"));
                }

                ev.Detail = Reason() is { } reason ? $"Reden: {reason}" : null;
                return ev;
            }
            case DossierLifecycleService.ActionCancelled:
            {
                var ev = Make("dossier.cancelled", "Dossier geannuleerd", rank: 9);
                var cascaded = StrList(after, "DraftOrdersCancelled");
                if (cascaded.Count > 0) ev.Field("Mee geannuleerde conceptopdrachten", string.Join(", ", cascaded));
                ev.Detail = Reason() is { } reason ? $"Reden: {reason}" : null;
                return ev;
            }
            case Incidents.Services.ProblemSignals.AuditProblemAfterConfirmation:
            {
                // EX-05: a problem reported on an already confirmed dossier — the dossier stays confirmed.
                var kind = Str(after, "Kind") == "Afwijking" ? "Afwijking" : "Incident";
                var ev = Make("dossier.problem_after_confirmation", $"{kind} gemeld na de bevestiging: {Str(after, "Title")}", "incidents", rank: 3);
                ev.Field("Ernst", Str(after, "Severity") switch
                {
                    "Critical" => "Kritiek",
                    "High" => "Hoog",
                    "Medium" => "Gemiddeld",
                    "Low" => "Laag",
                    _ => null,
                });
                ev.Detail = "Het dossier blijft bevestigd; bekijk of opvolging nodig is.";
                return ev;
            }
            case "Closed":
                return Make("dossier.confirmed", "Dossier bevestigd", rank: 9);
            default:
                return Make("dossier.changed", "Dossier gewijzigd");
        }
    }

    private static Event? OrderStatusEvent(
        TransportOrderStatusHistory row, OrderInfo order, bool viaPortal, Func<Guid, DateTime, string[], bool> tripMoment)
    {
        var from = DossierTimelineLabels.OrderStatus(row.FromStatus);
        var to = DossierTimelineLabels.OrderStatus(row.ToStatus);
        Event Make(string type, string title, int rank = 4) => new()
        {
            Id = $"status:{row.Id}", At = row.ChangedAt, Category = "orders", Type = type, Title = title, UserId = row.ChangedByUserId, Rank = rank,
            Detail = string.IsNullOrWhiteSpace(row.Reason) ? null : $"Reden: {row.Reason.Trim()}",
        };

        if (row.IsCorrection)
        {
            return Make("order.status_corrected", $"Status van opdracht {order.Number} gecorrigeerd van {from} naar {to}")
                .Field("Status", to, from);
        }

        switch (row.ToStatus)
        {
            case TransportOrderStatus.Submitted:
                // The portal submission is told by its own event ("ingediend via het klantportaal").
                return viaPortal ? null : Make("order.submitted", $"Transportopdracht {order.Number} ingediend", 1);
            case TransportOrderStatus.Confirmed when row.FromStatus == TransportOrderStatus.Submitted:
                return Make("order.accepted", $"Transportaanvraag {order.Number} aanvaard", 1);
            case TransportOrderStatus.Confirmed:
                return Make("order.confirmed", $"Transportopdracht {order.Number} bevestigd", 1);
            case TransportOrderStatus.Planned:
                // The trip event already says it (trip created/planned/order added at the same moment).
                return tripMoment(order.Id, row.ChangedAt, []) ? null : Make("order.planned", $"Transportopdracht {order.Number} ingepland");
            case TransportOrderStatus.InProgress:
                return tripMoment(order.Id, row.ChangedAt, [nameof(TripStatus.InProgress)]) ? null : Make("order.started", $"Transportopdracht {order.Number} in uitvoering");
            case TransportOrderStatus.Completed:
            {
                var ev = Make("order.completed", $"Transportopdracht {order.Number} voltooid");
                // The trip completing completes its orders: a consequence, not a person's act.
                if (tripMoment(order.Id, row.ChangedAt, [nameof(TripStatus.Completed)]))
                {
                    ev.System = true;
                    ev.Title = $"Transportopdracht {order.Number} voltooid na voltooiing van de rit";
                }

                return ev;
            }
            case TransportOrderStatus.Cancelled:
            {
                var ev = Make("order.cancelled", $"Transportopdracht {order.Number} geannuleerd");
                // The dossier-cancel cascade stores the reason on the order only.
                if (ev.Detail is null && order.Status == TransportOrderStatus.Cancelled && !string.IsNullOrWhiteSpace(order.CancellationReason))
                {
                    ev.Detail = $"Reden: {order.CancellationReason.Trim()}";
                }

                return ev;
            }
            case TransportOrderStatus.Invoiced:
                return Make("order.invoiced", $"Transportopdracht {order.Number} gefactureerd");
            default:
                return Make("order.status_changed", $"Status van opdracht {order.Number} gewijzigd van {from} naar {to}").Field("Status", to, from);
        }
    }

    private static Event Planning(AuditLog row, string type, string title) =>
        new() { Id = $"audit:{row.Id}", At = row.Timestamp, Category = "planning", Type = type, Title = title, UserId = row.UserId, Rank = 2 };

    private static Event Pricing(AuditLog row, string type, string title) =>
        new() { Id = $"audit:{row.Id}", At = row.Timestamp, Category = "pricing", Type = type, Title = title, UserId = row.UserId, Rank = 6 };

    // ==================================================================== queries

    private async Task<List<AuditLog>> AuditAsync(Guid tenantId, string entityType, List<string> entityIds, CancellationToken cancellationToken)
    {
        if (entityIds.Count == 0) return [];
        return await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EntityType == entityType && entityIds.Contains(a.EntityId))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);
    }

    private sealed record StopHistoryRow(Guid Id, Guid StopId, Guid TripId, StopExecutionStatus ToStatus, DateTime OccurredAt, Guid? UserId, string? Reason);

    private async Task<List<StopHistoryRow>> StopHistoryAsync(Guid tenantId, List<Guid> stopIds, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.StopStatusHistories.AsNoTracking()
            .Where(h => h.TenantId == tenantId)
            .Join(_dbContext.StopExecutions.AsNoTracking().Where(e => e.TenantId == tenantId && stopIds.Contains(e.TransportOrderStopId)),
                h => h.StopExecutionId, e => e.Id,
                (h, e) => new { h.Id, e.TransportOrderStopId, e.TripId, h.ToStatus, h.OccurredAt, h.UserId, h.Reason })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new StopHistoryRow(r.Id, r.TransportOrderStopId, r.TripId, r.ToStatus, r.OccurredAt, r.UserId, r.Reason)).ToList();
    }

    private async Task<Dictionary<Guid, string>> DriverNamesAsync(Guid tenantId, List<Guid> driverIds, CancellationToken cancellationToken) =>
        (await _dbContext.Drivers.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
            .Join(_dbContext.Employees.AsNoTracking().IgnoreQueryFilters().Where(e => e.TenantId == tenantId),
                d => d.EmployeeId, e => e.Id, (d, e) => new { d.Id, e.FirstName, e.LastName })
            .ToListAsync(cancellationToken))
        .ToDictionary(d => d.Id, d => $"{d.FirstName} {d.LastName}".Trim());

    // ==================================================================== helpers

    private static string ActivityName(string? typeName, string? label)
    {
        var type = string.IsNullOrWhiteSpace(typeName) ? "Activiteit" : typeName.Trim();
        return string.IsNullOrWhiteSpace(label) ? $"'{type}'" : $"'{type} – {label.Trim()}'";
    }

    private static string Plate(string internalNumber, string licensePlate) =>
        string.IsNullOrWhiteSpace(licensePlate) ? internalNumber
        : string.IsNullOrWhiteSpace(internalNumber) ? licensePlate
        : $"{internalNumber} ({licensePlate})";

    private static string ResourcePhrase(string? driver, string? vehicle) => (driver, vehicle) switch
    {
        ({ } d, { } v) => $" met chauffeur {d} en voertuig {v}",
        ({ } d, null) => $" met chauffeur {d}",
        (null, { } v) => $" met voertuig {v}",
        _ => string.Empty,
    };

    private static string Plural(IReadOnlyList<string> numbers, string one, string many) =>
        numbers.Count == 1 ? $"{one} {numbers[0]}" : $"{many} {string.Join(", ", numbers)}";

    private static string TripStatusLabel(TripStatus status) => status switch
    {
        TripStatus.Draft => "Concept",
        TripStatus.Planned => "Gepland",
        TripStatus.InProgress => "Onderweg",
        TripStatus.Completed => "Voltooid",
        TripStatus.Cancelled => "Geannuleerd",
        _ => "Onbekend",
    };

    private static bool Near(DateTime a, DateTime b) => (a - b).Duration() <= SameMoment;

    private static string Snake(string action) =>
        string.Concat(action.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    private static string? ResolveUser(string? value, IReadOnlyDictionary<Guid, string> users)
    {
        if (value is null || !value.Contains("{user:", StringComparison.Ordinal)) return value;
        var start = value.IndexOf("{user:", StringComparison.Ordinal);
        var end = value.IndexOf('}', start);
        if (end < 0 || !Guid.TryParse(value[(start + 6)..end], out var id)) return value;
        var name = users.GetValueOrDefault(id, "een onbekende gebruiker");
        return value[..start] + name + value[(end + 1)..];
    }

    private static JsonElement? Json(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Has(JsonElement? values, string name) => values is { } v && v.TryGetProperty(name, out _);

    private static string? Str(JsonElement? values, string name)
    {
        if (values is not { } v || !v.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static List<string> StrList(JsonElement? values, string name) =>
        values is { } v && v.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList()
            : [];

    private static Guid? GuidOf(JsonElement? values, string name) => Guid.TryParse(Str(values, name), out var id) ? id : null;

    private static decimal? Dec(JsonElement? values, string name) =>
        decimal.TryParse(Str(values, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? Int(JsonElement? values, string name) =>
        int.TryParse(Str(values, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static bool? Bool(JsonElement? values, string name) => Str(values, name) switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static TEnum? EnumOf<TEnum>(JsonElement? values, string name) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(Str(values, name), ignoreCase: false, out var value) && Enum.IsDefined(value) ? value : null;

    private static DateTime? DateTimeOf(JsonElement? values, string name) =>
        DateTime.TryParse(Str(values, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    /// <summary>A date-only payload value ("2026-09-23") in the Belgian notation.</summary>
    private static string? Date(JsonElement? values, string name)
    {
        var raw = Str(values, name);
        if (raw is null) return null;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return DossierTimelineLabels.Date(date);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? DossierTimelineLabels.Date(dt) : null;
    }
}

internal static class DossierTimelineText
{
    /// <summary>"Stop 2 (lossen, Gent)" → "stop 2 (lossen, Gent)" inside a sentence.</summary>
    public static string ToLowerInvariantFirst(this string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
