using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

public sealed record ProposalOrderDto(
    Guid TransportOrderId, string OrderNumber, string CustomerName, DateOnly OrderDate,
    string? DeliveryCity, string? DeliveryPostalCode,
    decimal? WeightKg, decimal? LoadingMeters, int? PalletCount,
    bool Overdue,
    /// <summary>P10: per-order feasibility notes (ADR/uitrusting/venster/openingsuren) —
    /// infeasibility is explained, never hidden.</summary>
    IReadOnlyList<string> Constraints);

public sealed record TourProposalDto(
    string ZoneCode, string ZoneName,
    IReadOnlyList<ProposalOrderDto> Orders,
    decimal TotalWeightKg, decimal TotalLoadingMeters, int TotalPallets,
    /// <summary>The heuristic explains itself — never a black box.</summary>
    IReadOnlyList<string> Explanations);

public sealed record PlanningProposalsDto(
    DateOnly Date,
    IReadOnlyList<TourProposalDto> Proposals,
    /// <summary>Confirmed orders NOT proposed, each with its reason (transparent exclusions).</summary>
    IReadOnlyList<string> Excluded);

public sealed record AcceptProposalRequest(DateOnly TripDate, IReadOnlyList<Guid> OrderIds);

public interface IPlanningProposalService
{
    Task<PlanningProposalsDto> GetProposalsAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 7: the transparent tour-proposal heuristic. Ready-to-plan = Confirmed, not on an
/// active trip, order date within the window (yesterday's leftovers surface FIRST, flagged
/// overdue). Grouping reuses THE zone concept (pricing zones, same postal-range matching);
/// within a zone orders sort by postal code as a route-proximity proxy. Deliberately simple:
/// every proposal and every exclusion carries its reason. Accepting composes the EXISTING
/// trip create/assign flows — all conflict machinery applies unchanged.
/// </summary>
public class PlanningProposalService : IPlanningProposalService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public PlanningProposalService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<PlanningProposalsDto> GetProposalsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        // Orders already riding on a non-cancelled trip are out of scope.
        var plannedOrderIds = _dbContext.TripOrders
            .Where(to => to.TenantId == tenantId)
            .Join(_dbContext.Trips.Where(t => t.Status != TripStatus.Cancelled),
                to => to.TripId, t => t.Id, (to, t) => to.TransportOrderId);

        var candidates = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Status == TransportOrderStatus.Confirmed
                        && o.OrderDate <= date && !plannedOrderIds.Contains(o.Id))
            .Join(_dbContext.Customers.AsNoTracking(), o => o.CustomerId, c => c.Id,
                (o, c) => new
                {
                    o.Id, o.OrderNumber, o.OrderDate, CustomerName = c.Name,
                    o.WeightKg, o.LoadingMeters, o.PalletCount,
                    o.AdrRequired, o.CraneRequired, o.PlateauRequired, o.MoffettRequired,
                })
            .ToListAsync(cancellationToken);

        var orderIds = candidates.Select(c => c.Id).ToList();
        var deliveries = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId)
                            && s.StopType == StopType.Unloading)
                .OrderBy(s => s.Sequence)
                .Select(s => new
                {
                    s.TransportOrderId, s.City, s.PostalCode, s.CountryCode,
                    s.RequestedFrom, s.RequestedTo, s.LocationId,
                })
                .ToListAsync(cancellationToken);
        var deliveryByOrder = deliveries
            .GroupBy(d => d.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.Last());

        // P10: opening hours of the delivery locations, for the proposal date's weekday.
        var locationIds = deliveries.Where(d => d.LocationId is not null)
            .Select(d => d.LocationId!.Value).Distinct().ToList();
        var isoDay = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;
        var openingByLocation = locationIds.Count == 0
            ? []
            : (await _dbContext.Set<Modules.Locations.Entities.LocationOpeningInterval>().AsNoTracking()
                .Where(i => i.TenantId == tenantId && locationIds.Contains(i.LocationId) && i.DayOfWeek == isoDay)
                .ToListAsync(cancellationToken))
                .GroupBy(i => i.LocationId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var hasOpeningData = (await _dbContext.Set<Modules.Locations.Entities.LocationOpeningInterval>().AsNoTracking()
            .Where(i => i.TenantId == tenantId && locationIds.Contains(i.LocationId))
            .Select(i => i.LocationId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        // C-03: a requested window is a UTC instant, opening hours are LOCAL wall clock. Both the
        // constraint text the planner reads and the comparison against the intervals run in the
        // tenant zone — comparing the raw instant reports a normal 08:00 delivery as "outside".
        var tenantZone = await TenantTimeZone.ForTenantAsync(_dbContext, tenantId, cancellationToken);

        // Capacity signal: the largest active vehicle bounds what one tour can carry.
        var maxPayload = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsActive && v.PayloadKg != null)
            .MaxAsync(v => (decimal?)v.PayloadKg, cancellationToken);

        var zones = await _dbContext.PricingZones.AsNoTracking()
            .Include(z => z.Areas)
            .Where(z => z.TenantId == tenantId && z.IsActive)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .ToListAsync(cancellationToken);

        var excluded = new List<string>();
        var byZone = new Dictionary<string, (PricingZone? Zone, List<ProposalOrderDto> Orders)>();
        foreach (var order in candidates)
        {
            if (!deliveryByOrder.TryGetValue(order.Id, out var delivery))
            {
                excluded.Add($"{order.OrderNumber}: geen losstop — vul de route aan.");
                continue;
            }

            var zone = ResolveZone(zones, delivery.CountryCode, delivery.PostalCode);
            var key = zone?.Code ?? "?";
            if (!byZone.TryGetValue(key, out var bucket))
            {
                bucket = (zone, []);
                byZone[key] = bucket;
            }

            // P10: explain what a feasible assignment must satisfy — data permitting.
            var constraints = new List<string>();
            if (order.AdrRequired)
            {
                constraints.Add("ADR: vereist een ADR-geschikt voertuig/aanhanger en chauffeur.");
            }

            if (order.CraneRequired)
            {
                constraints.Add("Kraan vereist: kies een voertuig met kraan.");
            }

            if (order.PlateauRequired)
            {
                constraints.Add("Plateau vereist.");
            }

            if (order.MoffettRequired)
            {
                constraints.Add("Moffett/meeneemheftruck vereist.");
            }

            var requestedFromLocal = TenantTimeZone.ToWallClock(delivery.RequestedFrom, tenantZone);
            var requestedToLocal = TenantTimeZone.ToWallClock(delivery.RequestedTo, tenantZone);
            if (requestedFromLocal is not null || requestedToLocal is not null)
            {
                var windowText = $"{requestedFromLocal:HH:mm}–{requestedToLocal:HH:mm}";
                constraints.Add($"Gevraagd venster {windowText}.");
            }

            if (delivery.LocationId is { } deliveryLocation && hasOpeningData.Contains(deliveryLocation))
            {
                var intervals = openingByLocation.GetValueOrDefault(deliveryLocation);
                if (intervals is null or { Count: 0 })
                {
                    constraints.Add("Losadres is gesloten op deze dag (openingsuren).");
                }
                else if (requestedFromLocal is { } requestedFrom)
                {
                    var requestedTime = TimeOnly.FromDateTime(requestedFrom);
                    if (!intervals.Any(i => requestedTime >= i.FromTime && requestedTime <= i.ToTime))
                    {
                        var hours = string.Join(", ", intervals.Select(i => $"{i.FromTime:HH\\:mm}–{i.ToTime:HH\\:mm}"));
                        constraints.Add($"Gevraagd venster valt buiten de openingsuren ({hours}).");
                    }
                }
            }

            bucket.Orders.Add(new ProposalOrderDto(
                order.Id, order.OrderNumber, order.CustomerName, order.OrderDate,
                delivery.City, delivery.PostalCode,
                order.WeightKg, order.LoadingMeters, order.PalletCount,
                Overdue: order.OrderDate < date,
                Constraints: constraints));
        }

        var proposals = byZone
            .Select(kv =>
            {
                var (zone, orders) = kv.Value;
                var ordered = orders
                    .OrderByDescending(o => o.Overdue)
                    .ThenBy(o => o.DeliveryPostalCode ?? "")
                    .ToList();
                var explanations = new List<string>
                {
                    zone is null
                        ? "Geen zone voor deze postcodes — controleer de zoneconfiguratie (Prijsinstellingen → Zones)."
                        : $"Gegroepeerd op leverzone {zone.Code} ({zone.Name}); binnen de zone gesorteerd op postcode.",
                };
                var overdueCount = ordered.Count(o => o.Overdue);
                if (overdueCount > 0)
                {
                    explanations.Add($"{overdueCount} order(s) van eerdere dagen staan bovenaan (achterstand eerst).");
                }

                // P10: capacity signal against the LARGEST active vehicle — a tour heavier than
                // that cannot be assigned as one trip, and the proposal says so upfront.
                var totalWeight = ordered.Sum(o => o.WeightKg ?? 0m);
                if (maxPayload is { } payload && totalWeight > payload)
                {
                    explanations.Add(
                        $"Totaalgewicht {totalWeight:0.#} kg overschrijdt het grootste voertuig ({payload:0.#} kg) — splits deze tour.");
                }

                var equipmentCount = ordered.Count(o => o.Constraints.Count > 0);
                if (equipmentCount > 0)
                {
                    explanations.Add($"{equipmentCount} order(s) hebben voorwaarden (ADR/uitrusting/venster) — zie de orderregels.");
                }

                return new TourProposalDto(
                    zone?.Code ?? "—", zone?.Name ?? "Ongezoneerd",
                    ordered,
                    ordered.Sum(o => o.WeightKg ?? 0m),
                    ordered.Sum(o => o.LoadingMeters ?? 0m),
                    ordered.Sum(o => o.PalletCount ?? 0),
                    explanations);
            })
            .OrderBy(p => p.ZoneCode == "—" ? 1 : 0).ThenBy(p => p.ZoneCode)
            .ToList();

        return new PlanningProposalsDto(date, proposals, excluded);
    }

    /// <summary>Same postal-range semantics as the pricing engine — ONE zone concept.</summary>
    private static PricingZone? ResolveZone(IReadOnlyList<PricingZone> zones, string? countryCode, string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        var country = string.IsNullOrWhiteSpace(countryCode) ? "BE" : countryCode.Trim().ToUpperInvariant();
        var code = postalCode.Trim();
        foreach (var zone in zones)
        {
            foreach (var area in zone.Areas)
            {
                if (!string.Equals(area.CountryCode, country, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(code, out var numeric)
                    && int.TryParse(area.PostalCodeFrom, out var from) && int.TryParse(area.PostalCodeTo, out var to))
                {
                    if (numeric >= from && numeric <= to)
                    {
                        return zone;
                    }
                }
                else if (string.CompareOrdinal(code, area.PostalCodeFrom) >= 0
                         && string.CompareOrdinal(code, area.PostalCodeTo) <= 0)
                {
                    return zone;
                }
            }
        }

        return null;
    }
}
