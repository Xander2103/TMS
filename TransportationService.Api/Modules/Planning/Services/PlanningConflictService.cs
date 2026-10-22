using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Qualifications;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// The planning conflict engine. The rules read from a <see cref="ConflictFacts"/> snapshot that
/// <see cref="LoadFactsAsync"/> fills with a FIXED number of set-based statements for the whole
/// trip set (PERF-BLOCKER 3: the board and the trip list used to pay ~15–20 round trips per
/// trip). The single-trip call is simply the batch of one, so both paths share one rule body —
/// same codes, severities, texts, enrichment and ordering by construction.
/// </summary>
public class PlanningConflictService : IPlanningConflictService
{
    private const int DefaultExpiryWarningDays = 30;

    /// <summary>Trip statuses that occupy a resource; drafts and cancelled trips never block others.</summary>
    private static readonly TripStatus[] OccupyingStatuses = [TripStatus.Planned, TripStatus.InProgress];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly Fleet.Services.IFleetReadinessService _fleetReadiness;

    public PlanningConflictService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider,
        Fleet.Services.IFleetReadinessService? fleetReadiness = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
        // FL-H1/H2/H4: the fleet verdict is the ONE source for asset availability (manual status,
        // maintenance windows, legal documents, damage) — Planning never re-derives it.
        _fleetReadiness = fleetReadiness ?? new Fleet.Services.FleetReadinessService(dbContext, tenantContext, timeProvider);
    }

    public Task<IReadOnlyList<PlanningConflictDto>> EvaluateAsync(Trip trip, CancellationToken cancellationToken)
        => EvaluateAsync(trip, requiresCraneOperator: false, cancellationToken);

    public async Task<IReadOnlyList<PlanningConflictDto>> EvaluateAsync(
        Trip trip, bool requiresCraneOperator, CancellationToken cancellationToken)
    {
        var facts = await LoadFactsAsync([trip], cancellationToken);
        return Evaluate(trip, requiresCraneOperator, facts);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlanningConflictDto>>> EvaluateManyAsync(
        IReadOnlyList<Trip> trips, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<PlanningConflictDto>>(trips.Count);
        if (trips.Count == 0)
        {
            return result;
        }

        var facts = await LoadFactsAsync(trips, cancellationToken);
        foreach (var trip in trips)
        {
            result[trip.Id] = Evaluate(trip, requiresCraneOperator: false, facts);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Rules (in memory, over the facts snapshot)
    // -----------------------------------------------------------------------

    private List<PlanningConflictDto> Evaluate(Trip trip, bool requiresCraneOperator, ConflictFacts facts)
    {
        var conflicts = new List<PlanningConflictDto>();

        if (trip.DriverId is null)
        {
            conflicts.Add(new(PlanningConflictCode.MissingDriver, true, "Er is nog geen chauffeur toegewezen."));
        }

        if (trip.VehicleId is null)
        {
            conflicts.Add(new(PlanningConflictCode.MissingVehicle, true, "Er is nog geen voertuig toegewezen."));
        }

        var orderIds = trip.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId).Distinct().ToList();
        if (orderIds.Count == 0)
        {
            conflicts.Add(new(PlanningConflictCode.NoOrders, true, "De rit bevat nog geen opdrachten."));
        }

        // Only orders that exist for this tenant count (a dangling link is simply absent).
        var orders = orderIds.Select(facts.Orders.GetValueOrDefault).Where(o => o is not null).Select(o => o!).ToList();

        if (trip.DriverId is { } driverId)
        {
            EvaluateDriver(conflicts, trip, driverId, requiresCraneOperator, facts);
        }

        if (trip.VehicleId is { } vehicleId)
        {
            var vehicle = facts.Vehicles.GetValueOrDefault(vehicleId);
            if (vehicle is not null)
            {
                var readiness = facts.VehicleReadiness.GetValueOrDefault((vehicleId, trip.TripDate));
                AddReadinessConflicts(conflicts, readiness, $"Voertuig {vehicle.InternalNumber}",
                    PlanningConflictCode.VehicleNotOperational, orders.Any(o => o.AdrRequired), orders.Any(o => o.CraneRequired),
                    orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber), orders.Where(o => o.CraneRequired).Select(o => o.OrderNumber));
                if (vehicle.OperationalStatus == VehicleOperationalStatus.InUse)
                {
                    // Warning only: "in gebruik" may simply reflect this very trip's planning.
                    conflicts.Add(new(PlanningConflictCode.VehicleNotOperational, false,
                        $"Voertuig {vehicle.InternalNumber} staat gemarkeerd als in gebruik."));
                }

                if (!vehicle.IsActive)
                {
                    conflicts.Add(new(PlanningConflictCode.VehicleInactive, true,
                        $"Voertuig {vehicle.InternalNumber} is inactief."));
                }

                var craneOrders = orders.Where(o => o.CraneRequired).Select(o => o.OrderNumber).ToList();
                if (craneOrders.Count > 0 && !vehicle.HasCrane)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresCrane, true,
                        $"Opdracht(en) {string.Join(", ", craneOrders)} vereisen een kraan; {vehicle.InternalNumber} heeft er geen."));
                }

                var adrOrders = orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber).ToList();
                if (adrOrders.Count > 0 && !vehicle.AdrSuitable)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresAdr, true,
                        $"Opdracht(en) {string.Join(", ", adrOrders)} vereisen ADR; {vehicle.InternalNumber} is niet ADR-geschikt."));
                }

                var doubleBooked = OtherTrip(trip, facts.OccupyingByVehicle[vehicleId]);
                if (doubleBooked is not null)
                {
                    conflicts.Add(new(PlanningConflictCode.VehicleDoubleBooked, true,
                        $"Voertuig {vehicle.InternalNumber} is op {trip.TripDate:dd/MM/yyyy} al ingepland op rit {doubleBooked}."));
                }

                // Driving-licence rank: the driver must hold a valid licence of at least the
                // rank the vehicle requires (e.g. a CE tractor needs a CE driver).
                if (trip.DriverId is { } licenceDriverId && vehicle.RequiredLicenceCode is { Length: > 0 } required)
                {
                    EvaluateLicenceRank(conflicts, licenceDriverId, vehicle.InternalNumber, required, facts);
                }
            }
        }

        if (trip.TrailerId is { } trailerId)
        {
            var trailer = facts.Trailers.GetValueOrDefault(trailerId);
            if (trailer is not null)
            {
                var trailerReadiness = facts.TrailerReadiness.GetValueOrDefault((trailerId, trip.TripDate));
                AddReadinessConflicts(conflicts, trailerReadiness, $"Oplegger {trailer.InternalNumber}",
                    PlanningConflictCode.TrailerNotOperational, orders.Any(o => o.AdrRequired), requiresCrane: false,
                    orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber), []);
                if (trailer.OperationalStatus == TrailerOperationalStatus.InUse)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerNotOperational, false,
                        $"Oplegger {trailer.InternalNumber} staat gemarkeerd als in gebruik."));
                }

                if (!trailer.IsActive)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerInactive, true,
                        $"Oplegger {trailer.InternalNumber} is inactief."));
                }

                var adrOrders = orders.Where(o => o.AdrRequired).Select(o => o.OrderNumber).ToList();
                if (adrOrders.Count > 0 && !trailer.AdrSuitable)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresAdr, true,
                        $"Opdracht(en) {string.Join(", ", adrOrders)} vereisen ADR; oplegger {trailer.InternalNumber} is niet ADR-geschikt."));
                }

                var doubleBooked = OtherTrip(trip, facts.OccupyingByTrailer[trailerId]);
                if (doubleBooked is not null)
                {
                    conflicts.Add(new(PlanningConflictCode.TrailerDoubleBooked, true,
                        $"Oplegger {trailer.InternalNumber} is op {trip.TripDate:dd/MM/yyyy} al ingepland op rit {doubleBooked}."));
                }
            }
        }

        if (orderIds.Count > 0)
        {
            EvaluateCapacity(conflicts, trip, orders, facts);
            EvaluateTailLift(conflicts, trip, orderIds, orders, facts);
        }

        return conflicts.Select(c => Enrich(c, trip)).ToList();
    }

    /// <summary>
    /// Maps the fleet readiness verdict onto planning conflicts. "inactive" is skipped (the
    /// dedicated Inactive conflict covers it); conditional items (ADR certificate, kraankeuring)
    /// only count when the trip carries matching work; tachograph keeps its own warning code.
    /// </summary>
    private static void AddReadinessConflicts(
        List<PlanningConflictDto> conflicts, Fleet.Dtos.VehicleReadinessDto? readiness, string assetLabel,
        PlanningConflictCode notOperational, bool requiresAdr, bool requiresCrane,
        IEnumerable<string> adrOrders, IEnumerable<string> craneOrders)
    {
        if (readiness is null)
        {
            return;
        }

        foreach (var issue in readiness.Issues)
        {
            if (issue.Code == "inactive" || issue.Severity == Fleet.Dtos.ReadinessSeverity.Info)
            {
                continue;
            }

            if (issue.Conditional == "adr")
            {
                if (requiresAdr)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresAdr, true,
                        $"Opdracht(en) {string.Join(", ", adrOrders)} vereisen ADR; {assetLabel}: {issue.Message}"));
                }
                continue;
            }

            if (issue.Conditional == "crane")
            {
                if (requiresCrane)
                {
                    conflicts.Add(new(PlanningConflictCode.OrderRequiresCrane, true,
                        $"Opdracht(en) {string.Join(", ", craneOrders)} vereisen een kraan; {assetLabel}: {issue.Message}"));
                }
                continue;
            }

            var code = issue.Code == "tachograph.overdue" ? PlanningConflictCode.TachographOverdue : notOperational;
            var blocking = issue.Severity == Fleet.Dtos.ReadinessSeverity.Blocking;
            conflicts.Add(new(code, blocking,
                blocking ? $"{assetLabel} is niet inzetbaar: {issue.Message}" : $"{assetLabel}: {issue.Message}"));
        }
    }

    /// <summary>
    /// Fills the structured fields (category, related entity, override metadata) from one central
    /// table so the rule sites above stay code + description only. Every blocking planning conflict
    /// is overrideable with <c>planning.override_restriction</c> plus a mandatory reason.
    /// </summary>
    internal static PlanningConflictDto Enrich(PlanningConflictDto conflict, Trip trip)
    {
        var (category, suggestedAction) = conflict.Code switch
        {
            PlanningConflictCode.MissingDriver => (ConflictCategory.Data, "Wijs een chauffeur toe."),
            PlanningConflictCode.MissingVehicle => (ConflictCategory.Data, "Wijs een voertuig toe."),
            PlanningConflictCode.NoOrders => (ConflictCategory.Data, "Koppel minstens één opdracht."),
            PlanningConflictCode.CapacityCheckIncomplete => (ConflictCategory.Data, "Vul gewicht/volume aan op de opdrachten."),
            PlanningConflictCode.DriverAbsent => (ConflictCategory.Availability, "Kies een andere chauffeur of datum."),
            PlanningConflictCode.DriverTraining => (ConflictCategory.Availability, "Controleer de opleiding of kies een andere chauffeur."),
            PlanningConflictCode.DriverShiftOverlap => (ConflictCategory.Timing, "Pas de shift of de rittijden aan."),
            PlanningConflictCode.DriverDoubleBooked => (ConflictCategory.Availability, "Kies een andere chauffeur of verplaats één van de ritten."),
            PlanningConflictCode.VehicleDoubleBooked => (ConflictCategory.Availability, "Kies een ander voertuig of verplaats één van de ritten."),
            PlanningConflictCode.TrailerDoubleBooked => (ConflictCategory.Availability, "Kies een andere oplegger of verplaats één van de ritten."),
            PlanningConflictCode.DriverBlocked => (ConflictCategory.Resource, "Deblokkeer de chauffeur of kies een andere."),
            PlanningConflictCode.DriverInactive => (ConflictCategory.Resource, "Activeer de chauffeur of kies een andere."),
            PlanningConflictCode.VehicleInactive => (ConflictCategory.Resource, "Activeer het voertuig of kies een ander."),
            PlanningConflictCode.TrailerInactive => (ConflictCategory.Resource, "Activeer de oplegger of kies een andere."),
            PlanningConflictCode.VehicleNotOperational => (ConflictCategory.Resource, "Kies een ander voertuig of los de blokkering op in het wagenpark."),
            PlanningConflictCode.TrailerNotOperational => (ConflictCategory.Resource, "Kies een andere oplegger of los de blokkering op in het wagenpark."),
            PlanningConflictCode.DriverNotReady => (ConflictCategory.Qualification, "Werk de kwalificatie bij of kies een andere chauffeur."),
            PlanningConflictCode.DriverLicenceInsufficient => (ConflictCategory.Qualification, "Kies een chauffeur met het juiste rijbewijs of een ander voertuig."),
            PlanningConflictCode.TachographOverdue => (ConflictCategory.Document, "Laat de tachograaf opnieuw ijken of kies een ander voertuig."),
            PlanningConflictCode.OrderRequiresCrane => (ConflictCategory.Equipment, "Kies een voertuig met kraan of verplaats de opdracht."),
            PlanningConflictCode.OrderRequiresAdr => (ConflictCategory.Equipment, "Kies ADR-geschikt materieel of verplaats de opdracht."),
            PlanningConflictCode.CapacityExceeded => (ConflictCategory.Capacity, "Verdeel de opdrachten over meerdere ritten of kies groter materieel."),
            PlanningConflictCode.TailLiftCapacityExceeded => (ConflictCategory.Capacity, "Kies een voertuig met een zwaardere laadklep of voorzie ander laad-/losmaterieel op de stop."),
            _ => (ConflictCategory.Resource, (string?)null),
        };

        var (relatedType, relatedId) = conflict.Code switch
        {
            PlanningConflictCode.DriverAbsent or PlanningConflictCode.DriverBlocked or PlanningConflictCode.DriverInactive
                or PlanningConflictCode.DriverNotReady or PlanningConflictCode.DriverDoubleBooked
                or PlanningConflictCode.DriverShiftOverlap or PlanningConflictCode.DriverTraining
                or PlanningConflictCode.DriverLicenceInsufficient
                => ("Driver", trip.DriverId),
            PlanningConflictCode.VehicleNotOperational or PlanningConflictCode.VehicleInactive
                or PlanningConflictCode.VehicleDoubleBooked or PlanningConflictCode.TachographOverdue
                or PlanningConflictCode.TailLiftCapacityExceeded
                => ("Vehicle", trip.VehicleId),
            PlanningConflictCode.TrailerNotOperational or PlanningConflictCode.TrailerInactive
                or PlanningConflictCode.TrailerDoubleBooked
                => ("Trailer", trip.TrailerId),
            _ => ((string?)null, (Guid?)null),
        };

        return conflict with
        {
            Category = category,
            SuggestedAction = suggestedAction,
            RelatedEntityType = relatedId is null ? null : relatedType,
            RelatedEntityId = relatedId,
            OverrideAllowed = conflict.Severity == ConflictSeverity.Blocking,
            RequiredPermission = conflict.Severity == ConflictSeverity.Blocking
                ? Identity.PermissionCodes.PlanningOverrideRestriction
                : null,
        };
    }

    /// <summary>
    /// Compares the trip's cargo totals with the assigned asset's capacity. The load rides
    /// the trailer when one is assigned, otherwise the vehicle. Measures without a configured
    /// capacity are skipped; orders without data never get invented values — they make the
    /// check explicitly incomplete. Exceeding blocks or warns per the tenant setting.
    /// </summary>
    private static void EvaluateCapacity(
        List<PlanningConflictDto> conflicts, Trip trip, List<OrderFact> orders, ConflictFacts facts)
    {
        // Capacity source: trailer when assigned, else vehicle.
        string? assetLabel = null;
        decimal? weightCapacity = null;
        decimal? volumeCapacity = null;
        if (trip.TrailerId is { } trailerId)
        {
            var trailer = facts.Trailers.GetValueOrDefault(trailerId);
            if (trailer is not null)
            {
                assetLabel = $"oplegger {trailer.InternalNumber}";
                weightCapacity = trailer.CapacityKg;
                volumeCapacity = trailer.VolumeM3;
            }
        }
        else if (trip.VehicleId is { } vehicleId)
        {
            var vehicle = facts.Vehicles.GetValueOrDefault(vehicleId);
            if (vehicle is not null)
            {
                assetLabel = $"voertuig {vehicle.InternalNumber}";
                weightCapacity = vehicle.PayloadKg;
                volumeCapacity = vehicle.VolumeM3;
            }
        }

        if (assetLabel is null || (weightCapacity is null && volumeCapacity is null))
        {
            return; // no configured capacity → the measure is skipped entirely
        }

        decimal totalWeight = 0;
        decimal totalVolume = 0;
        var ordersWithoutWeight = 0;
        var ordersWithoutVolume = 0;
        foreach (var order in orders)
        {
            // Structured cargo lines win over the order-level totals; nothing is invented.
            var lines = facts.CargoByOrder[order.Id].ToList();
            var lineWeights = lines
                .Select(l => l.TotalWeightKg ?? l.WeightPerUnitKg * l.ExpectedQuantity)
                .Where(w => w is not null)
                .Sum(w => w!.Value);
            var orderWeight = lineWeights > 0 ? lineWeights : order.WeightKg;
            if (orderWeight is { } weight)
            {
                totalWeight += weight;
            }
            else
            {
                ordersWithoutWeight += 1;
            }

            var lineVolumes = lines
                .Select(l => l.VolumeM3 * l.ExpectedQuantity)
                .Where(v => v is not null)
                .Sum(v => v!.Value);
            var orderVolume = lineVolumes > 0 ? lineVolumes : order.VolumeM3;
            if (orderVolume is { } volume)
            {
                totalVolume += volume;
            }
            else
            {
                ordersWithoutVolume += 1;
            }
        }

        var blocking = string.Equals(facts.Settings?.CapacityConflictSeverity, "Blocking", StringComparison.OrdinalIgnoreCase);

        if (weightCapacity is { } maxWeight && totalWeight > maxWeight)
        {
            // FL-M4: overloading is a legal/safety matter, not a tenant preference — above a small
            // rounding tolerance (0,5 %) it always blocks; only an authorised override with a reason
            // can plan it. Within the tolerance it stays a warning.
            var overload = totalWeight > maxWeight * 1.005m;
            conflicts.Add(new(PlanningConflictCode.CapacityExceeded, overload || blocking,
                $"Totaalgewicht {totalWeight:0.##} kg overschrijdt het laadvermogen van {maxWeight:0.##} kg van {assetLabel}"
                + (overload ? $" (+{totalWeight - maxWeight:0.##} kg)." : " (binnen de afrondingsmarge).")));
        }

        if (volumeCapacity is { } maxVolume && totalVolume > maxVolume)
        {
            conflicts.Add(new(PlanningConflictCode.CapacityExceeded, blocking,
                $"Totaalvolume {totalVolume:0.###} m³ overschrijdt het volume van {maxVolume:0.###} m³ van {assetLabel}."));
        }

        var missing = new List<string>();
        if (weightCapacity is not null && ordersWithoutWeight > 0)
        {
            missing.Add($"{ordersWithoutWeight} opdracht(en) zonder gewicht");
        }

        if (volumeCapacity is not null && ordersWithoutVolume > 0)
        {
            missing.Add($"{ordersWithoutVolume} opdracht(en) zonder volume");
        }

        if (missing.Count > 0)
        {
            conflicts.Add(new(PlanningConflictCode.CapacityCheckIncomplete, false,
                $"Capaciteitscontrole onvolledig: {string.Join(" en ", missing)}.",
                ConflictSeverity.Information));
        }
    }

    /// <summary>
    /// D4: tail-lift check. The heaviest SINGLE unit (max <c>WeightPerUnitKg</c> over the cargo
    /// lines that carry one) is compared with the tail-lift capacity of a vehicle that has a tail
    /// lift. Exceeding is always a warning — whether the lift is actually used at a stop is not
    /// modelled. When cargo weight exists but the lift capacity or a per-unit weight is unknown, the
    /// check is reported as still open; a per-unit weight is never derived from a line total and
    /// the outcome is never "safe".
    /// </summary>
    private static void EvaluateTailLift(
        List<PlanningConflictDto> conflicts, Trip trip, List<Guid> orderIds, List<OrderFact> orders, ConflictFacts facts)
    {
        if (trip.VehicleId is not { } vehicleId)
        {
            return;
        }

        var vehicle = facts.Vehicles.GetValueOrDefault(vehicleId);
        if (vehicle is not { HasTailLift: true })
        {
            return; // no tail lift → nothing to compare with (other handling equipment is not modelled)
        }

        // Cargo lines of every linked order id (a line survives its order's absence, as before).
        var cargoLines = orderIds.SelectMany(id => facts.CargoByOrder[id]).ToList();
        var hasOrderWeight = orders.Any(o => o.WeightKg != null && o.WeightKg > 0);

        var unitWeights = cargoLines.Where(l => l.WeightPerUnitKg is > 0).Select(l => l.WeightPerUnitKg!.Value).ToList();
        var linesWithoutUnitWeight = cargoLines.Count(l => l.WeightPerUnitKg is null && l.TotalWeightKg is > 0);
        var hasCargoWeight = unitWeights.Count > 0 || linesWithoutUnitWeight > 0 || hasOrderWeight;
        if (!hasCargoWeight)
        {
            return;
        }

        if (unitWeights.Count > 0 && vehicle.TailLiftCapacityKg is { } liftCapacity)
        {
            var heaviest = unitWeights.Max();
            if (heaviest > liftCapacity)
            {
                conflicts.Add(new(PlanningConflictCode.TailLiftCapacityExceeded, false,
                    $"De zwaarste eenheid ({heaviest:0.##} kg) overschrijdt de laadklepcapaciteit van {liftCapacity:0.##} kg van voertuig {vehicle.InternalNumber}."));
            }
        }

        var open = new List<string>();
        if (vehicle.TailLiftCapacityKg is null)
        {
            open.Add($"de laadklepcapaciteit van voertuig {vehicle.InternalNumber} is niet ingevuld");
        }

        if (linesWithoutUnitWeight > 0)
        {
            open.Add($"{linesWithoutUnitWeight} goederenlijn(en) zonder gewicht per eenheid");
        }
        else if (unitWeights.Count == 0)
        {
            open.Add("het gewicht per eenheid is niet gekend");
        }

        if (open.Count > 0)
        {
            conflicts.Add(new(PlanningConflictCode.CapacityCheckIncomplete, false,
                $"Capaciteit nog te controleren (laadklep): {string.Join(" en ", open)}.",
                ConflictSeverity.Information));
        }
    }

    /// <summary>Licence rank order (higher covers lower). Unknown codes rank 0.</summary>
    private static int LicenceRank(string code) => code.Trim().ToUpperInvariant() switch
    {
        "B" => 1,
        "C1" => 2,
        "C" => 3,
        "C1E" => 4,
        "CE" => 5,
        _ => 0,
    };

    private static readonly IReadOnlyDictionary<string, string> LicenceQualificationCodes = new Dictionary<string, string>
    {
        ["B"] = QualificationTypeCodes.DrivingLicenceB,
        ["C"] = QualificationTypeCodes.DrivingLicenceC,
        ["CE"] = QualificationTypeCodes.DrivingLicenceCE,
    };

    private void EvaluateLicenceRank(
        List<PlanningConflictDto> conflicts, Guid driverId, string vehicleNumber, string requiredCode, ConflictFacts facts)
    {
        var requiredRank = LicenceRank(requiredCode);
        if (requiredRank == 0)
        {
            return;
        }

        if (facts.Drivers.GetValueOrDefault(driverId) is not { } driver)
        {
            return;
        }

        // Held licence qualifications that are not expired/rejected/suspended.
        var warningDays = facts.Settings?.QualificationExpiryWarningDays ?? DefaultExpiryWarningDays;
        var today = facts.Today;

        var licenceTypeCodes = LicenceQualificationCodes.Values.ToList();
        var licences = facts.QualificationsByEmployee[driver.EmployeeId]
            .Where(q => licenceTypeCodes.Contains(q.Code))
            .ToList();

        var highestHeldRank = 0;
        foreach (var licence in licences)
        {
            var status = _statusCalculator.CalculateEffectiveStatus(licence.Qualification, today, warningDays);
            if (status is QualificationStatus.Expired or QualificationStatus.Rejected or QualificationStatus.Suspended)
            {
                continue;
            }

            var code = LicenceQualificationCodes.FirstOrDefault(kv => kv.Value == licence.Code).Key;
            if (code is not null)
            {
                highestHeldRank = Math.Max(highestHeldRank, LicenceRank(code));
            }
        }

        if (highestHeldRank < requiredRank)
        {
            conflicts.Add(new(PlanningConflictCode.DriverLicenceInsufficient, true,
                $"De chauffeur heeft geen geldig rijbewijs {requiredCode.ToUpperInvariant()} dat vereist is voor voertuig {vehicleNumber}."));
        }
    }

    private void EvaluateDriver(
        List<PlanningConflictDto> conflicts, Trip trip, Guid driverId, bool requiresCraneOperator, ConflictFacts facts)
    {
        var driver = facts.Drivers.GetValueOrDefault(driverId);
        if (driver is null)
        {
            return;
        }

        var person = facts.Employees.GetValueOrDefault(driver.EmployeeId);
        var driverName = person?.Name ?? driver.DriverNumber;

        if (driver.IsBlocked)
        {
            conflicts.Add(new(PlanningConflictCode.DriverBlocked, true,
                $"Chauffeur {driverName} is geblokkeerd{(driver.BlockReason is { Length: > 0 } r ? $": {r}" : ".")}"));
        }

        if (!driver.IsActive)
        {
            conflicts.Add(new(PlanningConflictCode.DriverInactive, true, $"Chauffeur {driverName} is inactief."));
        }

        // H5: the HR employment status is authoritative — a suspended/terminated employee is not
        // plannable even while the driver profile itself is still active.
        if (person is not null && EmploymentDeployability.Restriction(person.EmploymentStatus) is { } restriction)
        {
            conflicts.Add(new(PlanningConflictCode.DriverInactive, true,
                $"Chauffeur {driverName} is niet inzetbaar: dienstverband {restriction}."));
        }

        // MD-H6 follow-up: the employment end date is the LAST working day — a trip after it cannot
        // be planned for this driver (same rule as the planning picker and the access checks).
        if (person?.EmploymentEndDate is { } lastWorkingDay && lastWorkingDay < trip.TripDate)
        {
            conflicts.Add(new(PlanningConflictCode.DriverInactive, true,
                $"Chauffeur {driverName} is op {trip.TripDate:dd-MM-yyyy} niet meer in dienst (laatste werkdag {lastWorkingDay:dd-MM-yyyy})."));
        }

        var settings = facts.Settings;
        var trainingSeverity = ScheduleConflictRules.Parse(settings?.TrainingConflictSeverity);
        var shiftOverlapSeverity = ScheduleConflictRules.Parse(settings?.ShiftOverlapConflictSeverity);

        var absenceTypes = facts.AbsencesByEmployee[driver.EmployeeId]
            .Where(a => a.StartDate <= trip.TripDate && a.EndDate >= trip.TripDate)
            .Select(a => a.Type)
            .Distinct()
            .ToList();
        foreach (var absenceType in absenceTypes)
        {
            var severity = ScheduleConflictRules.TripVsAbsence(absenceType, trainingSeverity);
            if (absenceType == AbsenceType.Training)
            {
                conflicts.Add(new(PlanningConflictCode.DriverTraining, severity == ConflictSeverity.Blocking,
                    $"Chauffeur {driverName} heeft een goedgekeurde opleiding op {trip.TripDate:dd/MM/yyyy}.", severity));
            }
            else
            {
                conflicts.Add(new(PlanningConflictCode.DriverAbsent, true,
                    $"Chauffeur {driverName} is afwezig op {trip.TripDate:dd/MM/yyyy} ({Hr.AbsenceTypeLabels.Dutch(absenceType)}).",
                    ConflictSeverity.Blocking));
            }
        }

        // Manual shifts on the trip date: overlap severity is tenant-configurable.
        var tripStart = trip.PlannedStart is { } ps ? TimeOnly.FromDateTime(ps) : (TimeOnly?)null;
        var tripEnd = trip.PlannedEnd is { } pe ? TimeOnly.FromDateTime(pe) : (TimeOnly?)null;
        var shifts = facts.ShiftsByEmployee[driver.EmployeeId].Where(s => s.Date == trip.TripDate).ToList();
        foreach (var shift in shifts)
        {
            if (!ScheduleConflictRules.WindowsOverlap(shift.StartTime, shift.EndTime, tripStart, tripEnd))
            {
                continue;
            }

            if (shift.Type == ShiftType.Training)
            {
                conflicts.Add(new(PlanningConflictCode.DriverTraining, trainingSeverity == ConflictSeverity.Blocking,
                    $"Chauffeur {driverName} heeft een opleidingsshift op {trip.TripDate:dd/MM/yyyy} ({shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}).",
                    trainingSeverity));
            }
            else
            {
                conflicts.Add(new(PlanningConflictCode.DriverShiftOverlap, shiftOverlapSeverity == ConflictSeverity.Blocking,
                    $"Chauffeur {driverName} heeft al een shift op {trip.TripDate:dd/MM/yyyy} ({shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}).",
                    shiftOverlapSeverity));
            }
        }

        // Qualification state (QualificationReadinessPolicy, H4/P2-8): per type the best live
        // record counts — withdrawn/rejected records are history, a valid replacement satisfies.
        // An expired CORE qualification blocks; an expired situational one (ADR, kraan, …) only
        // warns here — work that needs it is blocked by its own requirement check (ADR below).
        var warningDays = settings?.QualificationExpiryWarningDays ?? DefaultExpiryWarningDays;
        var today = facts.Today;

        var qualifications = facts.QualificationsByEmployee[driver.EmployeeId].ToList();

        var summary = QualificationReadinessPolicy.Evaluate(qualifications.Select(entry =>
            new QualificationReadinessPolicy.Record(entry.Code, entry.Name,
                _statusCalculator.CalculateEffectiveStatus(entry.Qualification, today, warningDays),
                entry.Qualification.ExpiryDate)));
        foreach (var verdict in summary.Types)
        {
            switch (verdict.Outcome)
            {
                case QualificationReadinessPolicy.Outcome.Expired when verdict.IsCore:
                    conflicts.Add(new(PlanningConflictCode.DriverNotReady, true,
                        $"{verdict.TypeName} van {driverName} is niet geldig ({QualificationStatusLabels.Dutch(QualificationStatus.Expired)})."));
                    break;
                case QualificationReadinessPolicy.Outcome.Expired:
                    conflicts.Add(new(PlanningConflictCode.DriverNotReady, false,
                        $"{verdict.TypeName} van {driverName} is verlopen (niet vereist voor deze rit)."));
                    break;
                case QualificationReadinessPolicy.Outcome.ExpiringSoon:
                    conflicts.Add(new(PlanningConflictCode.DriverNotReady, false,
                        $"{verdict.TypeName} van {driverName} verloopt binnenkort."));
                    break;
            }
        }

        // P10: an ADR order needs a driver actually HOLDING a valid ADR certificate — not just
        // "no expired qualifications". Name-matched ("ADR") because qualification types are
        // tenant-defined; a tenant without any ADR type gets the explicit warning instead.
        // The requirement is read from the PERSISTED trip orders (as before), so a transient
        // probe trip without stored links states crane work through requiresCraneOperator.
        var tripWork = facts.TripWork.GetValueOrDefault(trip.Id);
        var tripNeedsAdr = tripWork.Adr;
        if (tripNeedsAdr)
        {
            var hasValidAdr = qualifications.Any(q =>
                q.Name.Contains("ADR", StringComparison.OrdinalIgnoreCase)
                && _statusCalculator.CalculateEffectiveStatus(q.Qualification, today, warningDays)
                    is QualificationStatus.Valid or QualificationStatus.ExpiringSoon);
            if (!hasValidAdr)
            {
                conflicts.Add(new(PlanningConflictCode.DriverNotReady, true,
                    $"ADR-transport: {driverName} heeft geen geldig ADR-certificaat."));
            }
        }

        // Transport acceptance M8: crane work (a crane transport or on-site lifting — the order
        // requires a crane) needs an operator holding a valid crane certificate, exactly like ADR
        // above. Situational: drivers without the certificate stay plannable for other work.
        // A standalone crane activity (ActivityType.RequiresCrane) states the requirement itself.
        var tripNeedsCrane = requiresCraneOperator || tripWork.Crane;
        if (tripNeedsCrane)
        {
            var hasValidCraneCertificate = qualifications.Any(q =>
                (q.Code == QualificationTypeCodes.CraneCertificate
                 || q.Name.Contains("kraan", StringComparison.OrdinalIgnoreCase)
                 || q.Name.Contains("crane", StringComparison.OrdinalIgnoreCase))
                && _statusCalculator.CalculateEffectiveStatus(q.Qualification, today, warningDays)
                    is QualificationStatus.Valid or QualificationStatus.ExpiringSoon);
            if (!hasValidCraneCertificate)
            {
                conflicts.Add(new(PlanningConflictCode.DriverNotReady, true,
                    $"Kraanwerk: {driverName} heeft geen geldig kraancertificaat."));
            }
        }

        var doubleBooked = OtherTrip(trip, facts.OccupyingByDriver[driverId]);
        if (doubleBooked is not null)
        {
            conflicts.Add(new(PlanningConflictCode.DriverDoubleBooked, true,
                $"Chauffeur {driverName} is op {trip.TripDate:dd/MM/yyyy} al ingepland op rit {doubleBooked}."));
        }
    }

    /// <summary>
    /// Trip number of another occupying trip on the same date among the resource's candidates, or
    /// null. The lowest trip number wins so the message is stable across reads.
    /// </summary>
    private static string? OtherTrip(Trip trip, IEnumerable<OccupyingTripFact> candidates) => candidates
        .Where(t => t.Id != trip.Id && t.TripDate == trip.TripDate)
        .Select(t => t.TripNumber)
        .Order(StringComparer.Ordinal)
        .FirstOrDefault();

    // -----------------------------------------------------------------------
    // Facts: one snapshot for the whole trip set, a fixed number of statements
    // -----------------------------------------------------------------------

    private sealed record OrderFact(Guid Id, string OrderNumber, bool AdrRequired, bool CraneRequired, decimal? WeightKg, decimal? VolumeM3);

    private sealed record CargoFact(decimal? TotalWeightKg, decimal? WeightPerUnitKg, decimal ExpectedQuantity, decimal? VolumeM3);

    private sealed record DriverFact(Guid Id, Guid EmployeeId, string DriverNumber, bool IsActive, bool IsBlocked, string? BlockReason);

    private sealed record PersonFact(string Name, Employees.Entities.EmploymentStatus EmploymentStatus, DateOnly? EmploymentEndDate);

    private sealed record SettingsFact(
        int QualificationExpiryWarningDays, string? TrainingConflictSeverity, string? ShiftOverlapConflictSeverity, string? CapacityConflictSeverity);

    private sealed record AbsenceFact(DateOnly StartDate, DateOnly EndDate, AbsenceType Type);

    private sealed record ShiftFact(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, ShiftType Type);

    private sealed record QualificationFact(EmployeeQualification Qualification, string Name, string Code);

    private sealed record VehicleFact(
        Guid Id, string InternalNumber, VehicleOperationalStatus OperationalStatus, bool IsActive, bool HasCrane, bool AdrSuitable,
        string? RequiredLicenceCode, decimal? PayloadKg, decimal? VolumeM3, bool HasTailLift, decimal? TailLiftCapacityKg);

    private sealed record TrailerFact(
        Guid Id, string InternalNumber, TrailerOperationalStatus OperationalStatus, bool IsActive, bool AdrSuitable,
        decimal? CapacityKg, decimal? VolumeM3);

    private sealed record OccupyingTripFact(Guid Id, string TripNumber, DateOnly TripDate, Guid? DriverId, Guid? VehicleId, Guid? TrailerId);

    /// <summary>Everything the rules read, keyed for O(1) access per trip.</summary>
    private sealed class ConflictFacts
    {
        public required DateOnly Today { get; init; }
        public required SettingsFact? Settings { get; init; }
        public required IReadOnlyDictionary<Guid, OrderFact> Orders { get; init; }
        public required ILookup<Guid, CargoFact> CargoByOrder { get; init; }
        public required IReadOnlyDictionary<Guid, DriverFact> Drivers { get; init; }
        public required IReadOnlyDictionary<Guid, PersonFact> Employees { get; init; }
        public required ILookup<Guid, AbsenceFact> AbsencesByEmployee { get; init; }
        public required ILookup<Guid, ShiftFact> ShiftsByEmployee { get; init; }
        public required ILookup<Guid, QualificationFact> QualificationsByEmployee { get; init; }
        public required IReadOnlyDictionary<Guid, VehicleFact> Vehicles { get; init; }
        public required IReadOnlyDictionary<Guid, TrailerFact> Trailers { get; init; }
        public required IReadOnlyDictionary<(Guid AssetId, DateOnly Date), Fleet.Dtos.VehicleReadinessDto> VehicleReadiness { get; init; }
        public required IReadOnlyDictionary<(Guid AssetId, DateOnly Date), Fleet.Dtos.VehicleReadinessDto> TrailerReadiness { get; init; }
        public required ILookup<Guid, OccupyingTripFact> OccupyingByDriver { get; init; }
        public required ILookup<Guid, OccupyingTripFact> OccupyingByVehicle { get; init; }
        public required ILookup<Guid, OccupyingTripFact> OccupyingByTrailer { get; init; }
        /// <summary>Per trip id: does any PERSISTED linked order require ADR / a crane.</summary>
        public required IReadOnlyDictionary<Guid, (bool Adr, bool Crane)> TripWork { get; init; }
    }

    /// <summary>
    /// Loads the snapshot: at most one statement per fact category (plus the fleet readiness
    /// bulk loads), each filtered on the tenant and on the ids the trips actually reference. Empty
    /// id sets skip their statement. Nothing here depends on the number of trips.
    /// </summary>
    private async Task<ConflictFacts> LoadFactsAsync(IReadOnlyList<Trip> trips, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var tripIds = trips.Select(t => t.Id).Distinct().ToList();
        var orderIds = trips.SelectMany(t => t.Orders.Where(o => !o.IsDeleted).Select(o => o.TransportOrderId)).Distinct().ToList();
        var driverIds = trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct().ToList();
        var vehicleIds = trips.Where(t => t.VehicleId != null).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var trailerIds = trips.Where(t => t.TrailerId != null).Select(t => t.TrailerId!.Value).Distinct().ToList();
        var minDate = trips.Min(t => t.TripDate);
        var maxDate = trips.Max(t => t.TripDate);

        // --- Orders and cargo ------------------------------------------------------------------
        var orders = orderIds.Count == 0
            ? new Dictionary<Guid, OrderFact>()
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new OrderFact(o.Id, o.OrderNumber, o.AdrRequired, o.CraneRequired, o.WeightKg, o.VolumeM3))
                .ToDictionaryAsync(o => o.Id, cancellationToken);
        var cargoLines = orderIds.Count == 0
            ? []
            : await _dbContext.CargoItems.AsNoTracking()
                .Where(c => c.TenantId == tenantId && orderIds.Contains(c.TransportOrderId))
                .Select(c => new { c.TransportOrderId, c.TotalWeightKg, c.WeightPerUnitKg, c.ExpectedQuantity, c.VolumeM3 })
                .ToListAsync(cancellationToken);

        // --- Driver, person, HR facts ------------------------------------------------------------
        var drivers = driverIds.Count == 0
            ? new Dictionary<Guid, DriverFact>()
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Select(d => new DriverFact(d.Id, d.EmployeeId, d.DriverNumber, d.IsActive, d.IsBlocked, d.BlockReason))
                .ToDictionaryAsync(d => d.Id, cancellationToken);
        var employeeIds = drivers.Values.Select(d => d.EmployeeId).Distinct().ToList();

        var employees = employeeIds.Count == 0
            ? new Dictionary<Guid, PersonFact>()
            : await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
                .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.EmploymentStatus, e.EmploymentEndDate })
                .ToDictionaryAsync(e => e.Id, e => new PersonFact(e.Name, e.EmploymentStatus, e.EmploymentEndDate), cancellationToken);

        var settings = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new SettingsFact(
                s.QualificationExpiryWarningDays, s.TrainingConflictSeverity, s.ShiftOverlapConflictSeverity, s.CapacityConflictSeverity))
            .FirstOrDefaultAsync(cancellationToken);

        var absences = employeeIds.Count == 0
            ? []
            : await _dbContext.Absences.AsNoTracking()
                .Where(a => a.TenantId == tenantId && employeeIds.Contains(a.EmployeeId)
                            && a.Status == AbsenceStatus.Approved
                            && a.StartDate <= maxDate && a.EndDate >= minDate)
                .Select(a => new { a.EmployeeId, a.StartDate, a.EndDate, a.Type })
                .ToListAsync(cancellationToken);
        var shifts = employeeIds.Count == 0
            ? []
            : await _dbContext.Shifts.AsNoTracking()
                .Where(s => s.TenantId == tenantId && employeeIds.Contains(s.EmployeeId) && s.Date >= minDate && s.Date <= maxDate)
                .Select(s => new { s.EmployeeId, s.Date, s.StartTime, s.EndTime, s.Type })
                .ToListAsync(cancellationToken);
        var qualifications = employeeIds.Count == 0
            ? []
            : await _dbContext.EmployeeQualifications.AsNoTracking()
                .Where(q => q.TenantId == tenantId && employeeIds.Contains(q.EmployeeId))
                .Join(_dbContext.QualificationTypes.AsNoTracking(),
                    q => q.QualificationTypeId, t => t.Id,
                    (q, t) => new { Qualification = q, t.Name, t.Code })
                .ToListAsync(cancellationToken);

        // --- Assets and their fleet verdicts ------------------------------------------------------
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<Guid, VehicleFact>()
            : await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .Select(v => new VehicleFact(v.Id, v.InternalNumber, v.OperationalStatus, v.IsActive, v.HasCrane, v.AdrSuitable,
                    v.RequiredLicenceCode, v.PayloadKg, v.VolumeM3, v.HasTailLift, v.TailLiftCapacityKg))
                .ToDictionaryAsync(v => v.Id, cancellationToken);
        var trailers = trailerIds.Count == 0
            ? new Dictionary<Guid, TrailerFact>()
            : await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .Select(t => new TrailerFact(t.Id, t.InternalNumber, t.OperationalStatus, t.IsActive, t.AdrSuitable, t.CapacityKg, t.VolumeM3))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        var vehicleReadiness = await _fleetReadiness.EvaluateVehiclesAsync(
            trips.Where(t => t.VehicleId != null).Select(t => (t.VehicleId!.Value, t.TripDate)).Distinct().ToList(), cancellationToken);
        var trailerReadiness = await _fleetReadiness.EvaluateTrailersAsync(
            trips.Where(t => t.TrailerId != null).Select(t => (t.TrailerId!.Value, t.TripDate)).Distinct().ToList(), cancellationToken);

        // --- Other occupying trips of the same resources in the date window ------------------------
        var occupying = driverIds.Count == 0 && vehicleIds.Count == 0 && trailerIds.Count == 0
            ? []
            : await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId
                            && t.TripDate >= minDate && t.TripDate <= maxDate
                            && OccupyingStatuses.Contains(t.Status)
                            && ((t.DriverId != null && driverIds.Contains(t.DriverId.Value))
                                || (t.VehicleId != null && vehicleIds.Contains(t.VehicleId.Value))
                                || (t.TrailerId != null && trailerIds.Contains(t.TrailerId.Value))))
                .Select(t => new OccupyingTripFact(t.Id, t.TripNumber, t.TripDate, t.DriverId, t.VehicleId, t.TrailerId))
                .ToListAsync(cancellationToken);

        // --- ADR / crane requirement of the persisted trip orders ------------------------------------
        var tripWorkRows = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && tripIds.Contains(to.TripId) && !to.IsDeleted)
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId),
                to => to.TransportOrderId, o => o.Id, (to, o) => new { to.TripId, o.AdrRequired, o.CraneRequired })
            .Where(x => x.AdrRequired || x.CraneRequired)
            .ToListAsync(cancellationToken);

        return new ConflictFacts
        {
            Today = today,
            Settings = settings,
            Orders = orders,
            CargoByOrder = cargoLines.ToLookup(c => c.TransportOrderId,
                c => new CargoFact(c.TotalWeightKg, c.WeightPerUnitKg, c.ExpectedQuantity, c.VolumeM3)),
            Drivers = drivers,
            Employees = employees,
            AbsencesByEmployee = absences
                .OrderBy(a => a.StartDate).ThenBy(a => a.EndDate).ThenBy(a => a.Type)
                .ToLookup(a => a.EmployeeId, a => new AbsenceFact(a.StartDate, a.EndDate, a.Type)),
            ShiftsByEmployee = shifts
                .OrderBy(s => s.Date).ThenBy(s => s.StartTime).ThenBy(s => s.EndTime)
                .ToLookup(s => s.EmployeeId, s => new ShiftFact(s.Date, s.StartTime, s.EndTime, s.Type)),
            QualificationsByEmployee = qualifications
                .ToLookup(q => q.Qualification.EmployeeId, q => new QualificationFact(q.Qualification, q.Name, q.Code)),
            Vehicles = vehicles,
            Trailers = trailers,
            VehicleReadiness = vehicleReadiness,
            TrailerReadiness = trailerReadiness,
            OccupyingByDriver = occupying.Where(t => t.DriverId != null).ToLookup(t => t.DriverId!.Value),
            OccupyingByVehicle = occupying.Where(t => t.VehicleId != null).ToLookup(t => t.VehicleId!.Value),
            OccupyingByTrailer = occupying.Where(t => t.TrailerId != null).ToLookup(t => t.TrailerId!.Value),
            TripWork = tripWorkRows
                .GroupBy(x => x.TripId)
                .ToDictionary(g => g.Key, g => (Adr: g.Any(x => x.AdrRequired), Crane: g.Any(x => x.CraneRequired))),
        };
    }
}
