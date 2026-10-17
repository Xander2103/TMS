using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using L = TransportationService.Api.Modules.Fleet.Services.FleetHistoryLabels;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFleetHistoryService
{
    /// <summary>The vehicle's story, newest first; null when the vehicle does not exist in this tenant.</summary>
    Task<IReadOnlyList<FleetHistoryEventDto>?> GetVehicleHistoryAsync(Guid vehicleId, bool includeFinancial, CancellationToken cancellationToken);

    /// <summary>The trailer's story, newest first; null when the trailer does not exist in this tenant.</summary>
    Task<IReadOnlyList<FleetHistoryEventDto>?> GetTrailerHistoryAsync(Guid trailerId, bool includeFinancial, CancellationToken cancellationToken);
}

/// <summary>
/// FL-H3 — read model that reconstructs a vehicle's or trailer's lifecycle from the trails that
/// are ALREADY written: the asset's own audit rows, the audit rows of its documents, inspections,
/// maintenance, damage, fuel, leasing and tachograph records, the trips that carried it, and the
/// derived readiness moments (maintenance windows, heavy damage). Nothing is written.
///
/// Readability rules: every code is translated (<see cref="FleetHistoryLabels"/>), ids become
/// numbers/names, amounts are only included for fleet_finance.view callers, and automatic
/// readiness moments are attributed to <c>System</c>, never to the user whose action triggered them.
/// </summary>
public class FleetHistoryService : IFleetHistoryService
{
    private static readonly string[] ChildEntityTypes =
        ["MaintenanceRecord", "DamageReport", "FleetDocument", "Inspection", "FuelTransaction", "LeasingContract", "TachographCalibration"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public FleetHistoryService(TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<FleetHistoryEventDto>?> GetVehicleHistoryAsync(Guid vehicleId, bool includeFinancial, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var vehicle = await _dbContext.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.Id == vehicleId)
            .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
            .FirstOrDefaultAsync(cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        var asset = new Asset(AssetKind.Vehicle, vehicle.Id, vehicle.InternalNumber, vehicle.LicensePlate);
        return await BuildAsync(asset, includeFinancial, cancellationToken);
    }

    public async Task<IReadOnlyList<FleetHistoryEventDto>?> GetTrailerHistoryAsync(Guid trailerId, bool includeFinancial, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var trailer = await _dbContext.Trailers.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Id == trailerId)
            .Select(t => new { t.Id, t.InternalNumber, t.LicensePlate })
            .FirstOrDefaultAsync(cancellationToken);
        if (trailer is null)
        {
            return null;
        }

        var asset = new Asset(AssetKind.Trailer, trailer.Id, trailer.InternalNumber, trailer.LicensePlate);
        return await BuildAsync(asset, includeFinancial, cancellationToken);
    }

    private async Task<IReadOnlyList<FleetHistoryEventDto>> BuildAsync(Asset asset, bool includeFinancial, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var isVehicle = asset.Kind == AssetKind.Vehicle;
        var id = asset.Id;

        // --- the asset's child records (soft-deleted ones included: their history still happened) ---
        var maintenance = await _dbContext.MaintenanceRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId && (isVehicle ? m.VehicleId == id : m.TrailerId == id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var damage = await _dbContext.DamageReports.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && (isVehicle ? d.VehicleId == id : d.TrailerId == id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var documents = await _dbContext.FleetDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == tenantId && (isVehicle ? d.VehicleId == id : d.TrailerId == id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var inspections = await _dbContext.Inspections.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenantId && (isVehicle ? i.VehicleId == id : i.TrailerId == id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var leasing = await _dbContext.LeasingContracts.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId && (isVehicle ? l.VehicleId == id : l.TrailerId == id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        var fuel = isVehicle
            ? await _dbContext.FuelTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(f => f.TenantId == tenantId && f.VehicleId == id)
                .ToDictionaryAsync(f => f.Id, cancellationToken)
            : new Dictionary<Guid, FuelTransaction>();
        var tachograph = isVehicle
            ? await _dbContext.TachographCalibrations.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.VehicleId == id)
                .ToDictionaryAsync(t => t.Id, cancellationToken)
            : new Dictionary<Guid, TachographCalibration>();

        var childIds = maintenance.Keys.Concat(damage.Keys).Concat(documents.Keys).Concat(inspections.Keys)
            .Concat(leasing.Keys).Concat(fuel.Keys).Concat(tachograph.Keys)
            .Select(g => g.ToString()).ToList();
        var assetType = isVehicle ? "Vehicle" : "Trailer";
        var assetIdString = id.ToString();

        var assetAudits = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EntityType == assetType && a.EntityId == assetIdString)
            .ToListAsync(cancellationToken);
        var childAudits = childIds.Count == 0
            ? []
            : await _dbContext.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == tenantId && ChildEntityTypes.Contains(a.EntityType) && childIds.Contains(a.EntityId))
                .ToListAsync(cancellationToken);

        // --- trips that carried the asset: currently, or at any moment according to their audit ---
        var currentTripIds = await _dbContext.Trips.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TenantId == tenantId && (isVehicle ? t.VehicleId == id : t.TrailerId == id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var auditedTripIds = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EntityType == "Trip"
                        && ((a.NewValuesJson != null && a.NewValuesJson.Contains(assetIdString))
                            || (a.OldValuesJson != null && a.OldValuesJson.Contains(assetIdString))))
            .Select(a => a.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var tripIds = currentTripIds.Select(t => t.ToString()).Concat(auditedTripIds).Distinct().ToList();
        var tripGuids = tripIds.Select(t => Guid.TryParse(t, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        var trips = tripGuids.Count == 0
            ? new Dictionary<Guid, Trip>()
            : await _dbContext.Trips.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.TenantId == tenantId && tripGuids.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);
        var tripAudits = tripIds.Count == 0
            ? []
            : await _dbContext.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.EntityType == "Trip" && tripIds.Contains(a.EntityId))
                .ToListAsync(cancellationToken);

        // --- name lookups ---
        var allAudits = assetAudits.Concat(childAudits).Concat(tripAudits).ToList();
        var userIds = allAudits.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        var driverIds = new HashSet<Guid>();
        foreach (var a in assetAudits.Where(a => a.Action == "AssignmentChanged"))
        {
            if (GuidValue(Parse(a.OldValuesJson), "DriverId") is { } o) driverIds.Add(o);
            if (GuidValue(Parse(a.NewValuesJson), "DriverId") is { } n) driverIds.Add(n);
        }
        foreach (var d in damage.Values.Where(d => d.DriverId is not null)) driverIds.Add(d.DriverId!.Value);
        foreach (var t in trips.Values.Where(t => t.DriverId is not null)) driverIds.Add(t.DriverId!.Value);
        var driverNames = driverIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Drivers.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.IgnoreQueryFilters().AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName, d.DriverNumber })
                .ToDictionaryAsync(x => x.Id, x => $"{x.Name.Trim()} ({x.DriverNumber})", cancellationToken);

        // Vehicles a trip moved to/from (for "vervangen door VRT-0004").
        var otherAssetIds = new HashSet<Guid>();
        foreach (var a in tripAudits)
        {
            var field = isVehicle ? "VehicleId" : "TrailerId";
            if (GuidValue(Parse(a.OldValuesJson), field) is { } o && o != id) otherAssetIds.Add(o);
            if (GuidValue(Parse(a.NewValuesJson), field) is { } n && n != id) otherAssetIds.Add(n);
        }
        foreach (var t in trips.Values)
        {
            var current = isVehicle ? t.VehicleId : t.TrailerId;
            if (current is { } c && c != id) otherAssetIds.Add(c);
        }
        var otherAssetNames = otherAssetIds.Count == 0
            ? new Dictionary<Guid, string>()
            : isVehicle
                ? await _dbContext.Vehicles.IgnoreQueryFilters().AsNoTracking()
                    .Where(v => v.TenantId == tenantId && otherAssetIds.Contains(v.Id))
                    .ToDictionaryAsync(v => v.Id, v => $"{v.InternalNumber} ({v.LicensePlate})", cancellationToken)
                : await _dbContext.Trailers.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => t.TenantId == tenantId && otherAssetIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => $"{t.InternalNumber} ({t.LicensePlate})", cancellationToken);

        var damageTripIds = damage.Values.Where(d => d.TripId is not null).Select(d => d.TripId!.Value)
            .Where(t => !trips.ContainsKey(t)).Distinct().ToList();
        var damageTripNumbers = damageTripIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Trips.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.TenantId == tenantId && damageTripIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.TripNumber, cancellationToken);
        foreach (var t in trips.Values) damageTripNumbers[t.Id] = t.TripNumber;

        var context = new BuildContext(asset, includeFinancial, driverNames, otherAssetNames, damageTripNumbers);
        var lines = new List<Line>();

        AddAssetLines(lines, context, assetAudits);
        AddDocumentLines(lines, context, childAudits.Where(a => a.EntityType == "FleetDocument"), documents);
        AddInspectionLines(lines, context, childAudits.Where(a => a.EntityType == "Inspection"), inspections);
        AddMaintenanceLines(lines, context, childAudits.Where(a => a.EntityType == "MaintenanceRecord"), maintenance);
        AddDamageLines(lines, context, childAudits.Where(a => a.EntityType == "DamageReport"), damage);
        AddFuelLines(lines, context, childAudits.Where(a => a.EntityType == "FuelTransaction"), fuel);
        AddLeasingLines(lines, context, childAudits.Where(a => a.EntityType == "LeasingContract"), leasing);
        AddTachographLines(lines, context, childAudits.Where(a => a.EntityType == "TachographCalibration"), tachograph);
        AddTripLines(lines, context, tripAudits, trips);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lines.AddRange(FleetReadinessTimeline.Derive(asset, assetAudits,
            childAudits.Where(a => a.EntityType == "MaintenanceRecord").ToList(), maintenance,
            childAudits.Where(a => a.EntityType == "DamageReport").ToList(), damage, now));

        return lines
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.Rank)
            .Select(l =>
            {
                var actorName = l.System || l.UserId is not { } uid ? null : userNames.GetValueOrDefault(uid);
                var actorKind = actorName is null ? "System" : "User";
                var detail = Join(l.Detail, includeFinancial ? l.FinancialDetail : null);
                return new FleetHistoryEventDto(l.Id, l.At, actorKind, actorName, l.Category, l.Title, detail,
                    includeFinancial && l.FinancialDetail is not null, l.LinkPath, l.Technical);
            })
            .ToList();
    }

    // ------------------------------------------------------------------ asset

    private static void AddAssetLines(List<Line> lines, BuildContext ctx, List<AuditLog> audits)
    {
        var noun = ctx.Asset.Kind == AssetKind.Vehicle ? "Voertuig" : "Oplegger";
        foreach (var a in audits)
        {
            var before = Parse(a.OldValuesJson);
            var after = Parse(a.NewValuesJson);
            switch (a.Action)
            {
                case "Created":
                    lines.Add(Line.From(a, "lifecycle",
                        $"{noun} aangemaakt — {Str(after, "InternalNumber") ?? ctx.Asset.Number} · {Str(after, "LicensePlate") ?? ctx.Asset.Plate}",
                        null, link: ctx.Tab("techniek")));
                    break;
                case "Updated":
                    lines.Add(DescribeAssetUpdate(a, ctx, noun, before, after));
                    break;
                case "Activated":
                    lines.Add(Line.From(a, "status", $"{noun} geheractiveerd", "Opnieuw beschikbaar voor nieuwe ritten."));
                    break;
                case "Deactivated":
                    lines.Add(Line.From(a, "status", $"{noun} gedeactiveerd", "Niet meer inzetbaar voor nieuwe ritten; de historiek blijft bewaard."));
                    break;
                case "Deleted":
                    lines.Add(Line.From(a, "lifecycle", $"{noun} verwijderd", null));
                    break;
                case "AssignmentChanged":
                    lines.Add(DescribeAssignment(a, ctx, before, after));
                    break;
                default:
                    lines.Add(Line.From(a, "lifecycle", $"{noun}gegevens gewijzigd", null));
                    break;
            }
        }
    }

    private static Line DescribeAssetUpdate(AuditLog a, BuildContext ctx, string noun, JsonElement? before, JsonElement? after)
    {
        var oldStatus = Str(before, "OperationalStatus");
        var newStatus = Str(after, "OperationalStatus");
        var statusChanged = newStatus is not null && oldStatus is not null && newStatus != oldStatus;
        var reason = Str(after, "StatusReason");
        var changes = new List<string>();

        // Odometer correction (FL-M6): an explicit correction carries its reason.
        var correctionReason = Str(after, "OdometerCorrectionReason");
        var oldKm = Dec(before, "OdometerKm");
        var newKm = Dec(after, "OdometerKm");
        if (correctionReason is { Length: > 0 } && oldKm is { } ok && newKm is { } nk && ok != nk)
        {
            return Line.From(a, "lifecycle", $"Kilometerstand gecorrigeerd: {L.Km(ok)} → {L.Km(nk)}", $"Reden: {correctionReason}",
                link: ctx.Tab("techniek"));
        }

        if (before is { ValueKind: JsonValueKind.Object } b && after is { ValueKind: JsonValueKind.Object } n)
        {
            foreach (var property in n.EnumerateObject())
            {
                if (property.Name is "OperationalStatus" or "StatusReason" or "OdometerCorrectionReason")
                {
                    continue;
                }
                if (!b.TryGetProperty(property.Name, out var old) || JsonEquals(old, property.Value))
                {
                    continue;
                }
                var label = L.PropertyLabel(property.Name);
                if (label is null)
                {
                    continue;
                }
                changes.Add($"{label} {FormatValue(property.Name, old)} → {FormatValue(property.Name, property.Value)}");
            }
        }

        if (statusChanged)
        {
            var title = $"Operationele status: {L.OperationalStatus(oldStatus)} → {L.OperationalStatus(newStatus)}";
            var detail = Join(L.IsBlockingStatus(newStatus) && reason is { Length: > 0 } ? $"Reden: {reason}" : null,
                changes.Count > 0 ? "Ook gewijzigd: " + string.Join("; ", changes) : null);
            return Line.From(a, "status", title, detail);
        }

        if (changes.Count > 0)
        {
            return Line.From(a, "lifecycle", $"{noun}gegevens gewijzigd", Capitalize(string.Join("; ", changes)), link: ctx.Tab("techniek"));
        }

        // Technical edits (dimensions, equipment …) are not itemised in the audit trail.
        var reasonOnly = Str(before, "StatusReason") != reason && reason is { Length: > 0 } ? $"Reden: {reason}" : null;
        return Line.From(a, "lifecycle", $"{noun}gegevens gewijzigd", reasonOnly, link: ctx.Tab("techniek"));
    }

    private static Line DescribeAssignment(AuditLog a, BuildContext ctx, JsonElement? before, JsonElement? after)
    {
        var kind = Str(after, "Kind") ?? Str(before, "Kind");
        var slot = kind == "Current" ? "Actuele chauffeur" : "Vaste chauffeur";
        var oldDriver = GuidValue(before, "DriverId");
        var newDriver = GuidValue(after, "DriverId");
        string Name(Guid? driver) => driver is { } d ? ctx.DriverNames.GetValueOrDefault(d) ?? "onbekende chauffeur" : "—";
        var title = (oldDriver, newDriver) switch
        {
            (null, not null) => $"{slot} toegewezen: {Name(newDriver)}",
            (not null, null) => $"{slot} ontkoppeld: {Name(oldDriver)}",
            _ => $"{slot} gewijzigd: {Name(oldDriver)} → {Name(newDriver)}",
        };
        return Line.From(a, "assignment", title, null, link: ctx.Tab("techniek"));
    }

    // ------------------------------------------------------------------ documents

    private static void AddDocumentLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, FleetDocument> documents)
    {
        foreach (var a in audits)
        {
            var doc = documents.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var before = Parse(a.OldValuesJson);
            var after = Parse(a.NewValuesJson);
            var type = Enum.TryParse<FleetDocumentType>(Str(after, "DocumentType") ?? Str(before, "DocumentType"), out var t)
                ? t : doc?.DocumentType ?? FleetDocumentType.Other;
            var label = L.DocumentType(type, doc?.CustomTypeName);
            var link = ctx.Tab("documenten");
            switch (a.Action)
            {
                case "Created":
                {
                    var expiry = DateOnlyValue(after, "ExpiryDate") ?? doc?.ExpiryDate;
                    var detail = Join(doc?.DocumentNumber is { Length: > 0 } nr ? $"nr. {nr}" : null,
                        expiry is { } e ? $"geldig tot {L.Date(e)}" : "geen vervaldatum");
                    lines.Add(Line.From(a, "document", $"{label} toegevoegd", Capitalize(detail), link: link));
                    break;
                }
                case "Updated":
                {
                    var changes = new List<string>();
                    if (Str(before, "DocumentNumber") != Str(after, "DocumentNumber"))
                        changes.Add($"nummer {Str(before, "DocumentNumber") ?? "—"} → {Str(after, "DocumentNumber") ?? "—"}");
                    var oldExpiry = DateOnlyValue(before, "ExpiryDate");
                    var newExpiry = DateOnlyValue(after, "ExpiryDate");
                    if (oldExpiry != newExpiry)
                        changes.Add($"vervaldatum {(oldExpiry is { } o ? L.Date(o) : "—")} → {(newExpiry is { } n ? L.Date(n) : "—")}");
                    lines.Add(Line.From(a, "document", $"{label} bijgewerkt", changes.Count > 0 ? Capitalize(string.Join("; ", changes)) : null, link: link));
                    break;
                }
                case "DocumentUploaded":
                    lines.Add(Line.From(a, "document", $"Bestand toegevoegd aan {Lower(label)}", Str(after, "FileName"), link: link));
                    break;
                case "DocumentRemoved":
                    lines.Add(Line.From(a, "document", $"Bestand verwijderd bij {Lower(label)}", null, link: link));
                    break;
                case "Deleted":
                    lines.Add(Line.From(a, "document", $"{label} verwijderd",
                        Str(before, "DocumentNumber") is { Length: > 0 } nr2 ? $"Nr. {nr2}" : null, link: link));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ inspections

    private static void AddInspectionLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, Inspection> inspections)
    {
        foreach (var a in audits)
        {
            var inspection = inspections.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var before = Parse(a.OldValuesJson);
            var after = Parse(a.NewValuesJson);
            var type = Enum.TryParse<InspectionType>(Str(after, "InspectionType") ?? Str(before, "InspectionType"), out var t)
                ? t : inspection?.InspectionType ?? InspectionType.Other;
            var label = L.InspectionType(type, inspection?.CustomTypeName);
            var link = ctx.Tab(ctx.Asset.Kind == AssetKind.Vehicle ? "documenten" : "keuringen");
            switch (a.Action)
            {
                case "Created":
                {
                    var due = DateOnlyValue(after, "DueDate") ?? inspection?.DueDate;
                    lines.Add(Line.From(a, "inspection", $"{label} gepland",
                        due is { } d ? $"Vervalt op {L.Date(d)}" : null, link: link));
                    break;
                }
                case "Updated":
                {
                    var due = DateOnlyValue(after, "DueDate");
                    var oldDue = DateOnlyValue(before, "DueDate");
                    lines.Add(Line.From(a, "inspection", $"{label} bijgewerkt",
                        due is { } d && d != oldDue ? $"Vervaldatum {(oldDue is { } o ? L.Date(o) : "—")} → {L.Date(d)}" : null, link: link));
                    break;
                }
                case "Completed":
                {
                    var completed = DateOnlyValue(after, "CompletedDate") ?? inspection?.CompletedDate;
                    var result = L.InspectionResult(Str(after, "Result"));
                    var followUpDue = GuidValue(after, "FollowUpId") is { } f && inspections.TryGetValue(f, out var next) ? next.DueDate : (DateOnly?)null;
                    lines.Add(Line.From(a, "inspection", $"{label} geregistreerd — {result}",
                        Join(completed is { } c ? $"Uitgevoerd op {L.Date(c)}" : null,
                            followUpDue is { } n ? $"volgende keuring vóór {L.Date(n)}" : null), link: link));
                    break;
                }
                case "Deleted":
                    lines.Add(Line.From(a, "inspection", $"{label} verwijderd",
                        DateOnlyValue(before, "DueDate") is { } d2 ? $"Vervaldatum was {L.Date(d2)}" : null, link: link));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ maintenance

    private static void AddMaintenanceLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, MaintenanceRecord> records)
    {
        foreach (var a in audits)
        {
            var record = records.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var before = Parse(a.OldValuesJson);
            var after = Parse(a.NewValuesJson);
            var type = Enum.TryParse<MaintenanceType>(Str(after, "MaintenanceType") ?? Str(before, "MaintenanceType"), out var t)
                ? t : record?.MaintenanceType ?? MaintenanceType.Other;
            var label = L.MaintenanceType(type, record?.CustomTypeName);
            var link = ctx.Tab("onderhoud");
            switch (a.Action)
            {
                case "Created":
                {
                    var start = DateOnlyValue(after, "ScheduledDate") ?? record?.ScheduledDate;
                    var title = start is { } s
                        ? $"Onderhoud gepland: {label} — {L.Window(s, record?.ScheduledEndDate)}"
                        : $"Onderhoud gepland: {label}";
                    lines.Add(Line.From(a, "maintenance", title,
                        Join(record?.Description, record?.Provider), link: link));
                    break;
                }
                case "Updated":
                {
                    var oldStatus = Str(before, "Status");
                    var newStatus = Str(after, "Status");
                    if (newStatus is not null && newStatus != oldStatus)
                    {
                        var title = newStatus switch
                        {
                            "InProgress" or "1" => $"Onderhoud gestart: {label}",
                            "Cancelled" or "3" => $"Onderhoud geannuleerd: {label}",
                            "Planned" or "0" => $"Onderhoud teruggezet naar gepland: {label}",
                            _ => $"Onderhoud bijgewerkt: {label}",
                        };
                        lines.Add(Line.From(a, "maintenance", title, record?.Description, link: link));
                        break;
                    }
                    var oldDate = DateOnlyValue(before, "ScheduledDate");
                    var newDate = DateOnlyValue(after, "ScheduledDate");
                    lines.Add(Line.From(a, "maintenance",
                        newDate is { } nd && nd != oldDate ? $"Onderhoud verplaatst: {label} — {L.Date(nd)}" : $"Onderhoud bijgewerkt: {label}",
                        record?.Description, link: link));
                    break;
                }
                case "Completed":
                {
                    var completed = DateOnlyValue(after, "CompletedDate") ?? record?.CompletedDate;
                    var odometer = record?.CompletedOdometerKm;
                    var detail = Join(completed is { } c ? $"Afgerond op {L.Date(c)}" : null,
                        odometer is { } km ? $"kilometerstand {L.Km(km)}" : null,
                        record?.WorkPerformed, record?.Provider);
                    var cost = Dec(after, "Cost") ?? record?.Cost;
                    lines.Add(Line.From(a, "maintenance", $"Onderhoud afgerond: {label}", detail,
                        financialDetail: cost is { } amount ? $"kosten {L.Money(amount)}" : null, link: link));
                    break;
                }
                case "Deleted":
                    lines.Add(Line.From(a, "maintenance", $"Onderhoud verwijderd: {label}", Str(before, "Description"), link: link));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ damage

    private static void AddDamageLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, DamageReport> reports)
    {
        foreach (var a in audits)
        {
            var report = reports.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var before = Parse(a.OldValuesJson);
            var after = Parse(a.NewValuesJson);
            var link = ctx.Tab(ctx.Asset.Kind == AssetKind.Vehicle ? "onderhoud" : "schade");
            switch (a.Action)
            {
                case "Created":
                {
                    var severity = L.DamageSeverity(Str(after, "Severity") ?? report?.Severity.ToString());
                    var tripNumber = report?.TripId is { } tripId ? ctx.TripNumbers.GetValueOrDefault(tripId) : null;
                    var detail = Join(
                        report?.DriverId is { } d ? $"chauffeur {ctx.DriverNames.GetValueOrDefault(d) ?? "onbekend"}" : null,
                        tripNumber is not null ? $"rit {tripNumber}" : null,
                        report?.Location);
                    lines.Add(Line.From(a, "damage", $"Schade gemeld ({severity}): {report?.Description ?? "schade"}", Capitalize(detail),
                        financialDetail: Dec(after, "RepairCost") is { } cost && cost > 0 ? $"herstelkost {L.Money(cost)}" : null,
                        link: link));
                    break;
                }
                case "Updated":
                {
                    var oldStatus = Str(before, "Status");
                    var newStatus = Str(after, "Status");
                    var oldSeverity = Str(before, "Severity");
                    var newSeverity = Str(after, "Severity");
                    var oldCost = Dec(before, "RepairCost");
                    var newCost = Dec(after, "RepairCost");
                    var severityChange = newSeverity is not null && newSeverity != oldSeverity
                        ? $"Ernst {L.DamageSeverity(oldSeverity)} → {L.DamageSeverity(newSeverity)}" : null;
                    var costChange = newCost is { } nc && nc != oldCost ? $"herstelkost {L.Money(nc)}" : null;
                    var title = newStatus is not null && newStatus != oldStatus
                        ? L.DamageStatusTitle(newStatus)
                        : severityChange is not null ? "Ernst van de schade gewijzigd" : "Schademelding bijgewerkt";
                    if (title == "Schademelding bijgewerkt" && severityChange is null && costChange is not null && !ctx.IncludeFinancial)
                    {
                        // Only an amount changed: nothing to tell a viewer without finance rights.
                        continue;
                    }
                    lines.Add(Line.From(a, "damage", title,
                        Join(report?.Description, severityChange), financialDetail: costChange, link: link));
                    break;
                }
                case "Deleted":
                    lines.Add(Line.From(a, "damage", "Schademelding verwijderd", Str(before, "Description"), link: link));
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ fuel / leasing / tachograph

    private static void AddFuelLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, FuelTransaction> fuel)
    {
        foreach (var a in audits)
        {
            var tx = fuel.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var after = Parse(a.NewValuesJson);
            var before = Parse(a.OldValuesJson);
            var litres = Dec(after, "Litres") ?? Dec(before, "Litres") ?? tx?.Litres;
            var date = DateOnlyValue(after, "TransactionDate") ?? DateOnlyValue(before, "TransactionDate") ?? tx?.TransactionDate;
            var core = Join(litres is { } l ? L.Litres(l) : null, tx?.OdometerKm is { } km ? L.Km(km) : null);
            var amount = Dec(after, "TotalAmount") ?? tx?.TotalAmount;
            var link = ctx.Tab("kosten");
            switch (a.Action)
            {
                case "Created":
                    lines.Add(Line.From(a, "fuel", core is null ? "Tankbeurt geregistreerd" : $"Tankbeurt — {core}",
                        Join(date is { } d ? $"Getankt op {L.Date(d)}" : null, tx?.Station),
                        financialDetail: amount is { } m ? L.Money(m) : null, link: link));
                    break;
                case "Updated":
                    lines.Add(Line.From(a, "fuel", "Tankbeurt gewijzigd", Join(date is { } d2 ? $"Tankbeurt van {L.Date(d2)}" : null, core),
                        financialDetail: amount is { } m2 ? L.Money(m2) : null, link: link));
                    break;
                case "Deleted":
                    lines.Add(Line.From(a, "fuel", "Tankbeurt verwijderd", Join(date is { } d3 ? $"Tankbeurt van {L.Date(d3)}" : null, core), link: link));
                    break;
            }
        }
    }

    private static void AddLeasingLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, LeasingContract> contracts)
    {
        foreach (var a in audits)
        {
            var contract = contracts.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var after = Parse(a.NewValuesJson);
            var before = Parse(a.OldValuesJson);
            var company = Str(after, "LeasingCompany") ?? Str(before, "LeasingCompany") ?? contract?.LeasingCompany ?? "leasingmaatschappij";
            var end = DateOnlyValue(after, "EndDate") ?? contract?.EndDate;
            var link = ctx.Tab(ctx.Asset.Kind == AssetKind.Vehicle ? "kosten" : "leasing");
            var monthly = contract?.MonthlyAmount is { } amount ? $"maandbedrag {L.Money(amount)}" : null;
            switch (a.Action)
            {
                case "Created":
                    lines.Add(Line.From(a, "leasing", $"Leasingcontract toegevoegd: {company}",
                        Join(contract?.ContractNumber is { Length: > 0 } nr ? $"Contract {nr}" : null,
                            end is { } e ? $"loopt tot {L.Date(e)}" : null),
                        financialDetail: monthly, link: link));
                    break;
                case "Updated":
                {
                    var active = Bool(after, "IsActive");
                    lines.Add(Line.From(a, "leasing",
                        active == false ? $"Leasingcontract beëindigd: {company}" : $"Leasingcontract bijgewerkt: {company}",
                        end is { } endDate ? $"Einddatum {L.Date(endDate)}" : null, link: link));
                    break;
                }
                case "DocumentUploaded":
                    lines.Add(Line.From(a, "leasing", $"Contractbestand toegevoegd: {company}", Str(after, "FileName"), link: link));
                    break;
                case "Deleted":
                    lines.Add(Line.From(a, "leasing", $"Leasingcontract verwijderd: {company}", null, link: link));
                    break;
            }
        }
    }

    private static void AddTachographLines(List<Line> lines, BuildContext ctx, IEnumerable<AuditLog> audits, IReadOnlyDictionary<Guid, TachographCalibration> calibrations)
    {
        foreach (var a in audits)
        {
            var calibration = calibrations.GetValueOrDefault(System.Guid.Parse(a.EntityId));
            var after = Parse(a.NewValuesJson);
            var date = DateOnlyValue(after, "CalibrationDate") ?? calibration?.CalibrationDate;
            var next = DateOnlyValue(after, "NextCalibrationDue") ?? calibration?.NextCalibrationDue;
            var link = ctx.Tab("documenten");
            var title = a.Action switch
            {
                "Created" => date is { } d ? $"Tachograafijking geregistreerd op {L.Date(d)}" : "Tachograafijking geregistreerd",
                "Deleted" => "Tachograafijking verwijderd",
                "DocumentUploaded" => "Ijkingscertificaat toegevoegd",
                _ => "Tachograafijking bijgewerkt",
            };
            lines.Add(Line.From(a, "tachograph", title,
                Join(a.Action == "Deleted" ? null : next is { } n ? $"volgende ijking vóór {L.Date(n)}" : null, calibration?.Workshop) is { } detail
                    ? Capitalize(detail) : null,
                link: link));
        }
    }

    // ------------------------------------------------------------------ trips

    private static void AddTripLines(List<Line> lines, BuildContext ctx, List<AuditLog> audits, IReadOnlyDictionary<Guid, Trip> trips)
    {
        var field = ctx.Asset.Kind == AssetKind.Vehicle ? "VehicleId" : "TrailerId";
        foreach (var group in audits.GroupBy(a => a.EntityId))
        {
            if (!System.Guid.TryParse(group.Key, out var tripId) || !trips.TryGetValue(tripId, out var trip))
            {
                continue;
            }
            Guid? onTrip = null;
            var status = "Draft";
            var link = $"/planning/{trip.Id}";
            var driver = trip.DriverId is { } d ? ctx.DriverNames.GetValueOrDefault(d) : null;
            foreach (var a in group.OrderBy(a => a.Timestamp))
            {
                var after = Parse(a.NewValuesJson);
                var before = Parse(a.OldValuesJson);
                if (Has(after, field))
                {
                    var previous = onTrip ?? GuidValue(before, field);
                    var next = GuidValue(after, field);
                    onTrip = next;
                    var occupying = status is "Planned" or "InProgress" or "1" or "2";
                    if (previous != next && occupying)
                    {
                        var reason = Str(after, "Reason") ?? Str(after, "OverrideReason");
                        if (previous == ctx.Asset.Id)
                        {
                            var replacement = next is { } r ? ctx.OtherAssetNames.GetValueOrDefault(r) ?? "ander materieel" : "geen";
                            lines.Add(Line.From(a, "planning", $"Vervangen op {trip.TripNumber} door {replacement}",
                                Join($"Rit van {L.Date(trip.TripDate)}", reason is { Length: > 0 } ? $"reden: {reason}" : null), link: link));
                        }
                        else if (next == ctx.Asset.Id)
                        {
                            var replaced = previous is { } p ? ctx.OtherAssetNames.GetValueOrDefault(p) : null;
                            lines.Add(Line.From(a, "planning", $"Ingezet op {trip.TripNumber}{(replaced is null ? "" : $" ter vervanging van {replaced}")}",
                                Join($"Rit van {L.Date(trip.TripDate)}", reason is { Length: > 0 } ? $"reden: {reason}" : null), link: link));
                        }
                    }
                }
                else if (a.Action == "Created")
                {
                    onTrip = null;
                }

                if (a.Action == "StatusChanged" && Str(after, "Status") is { } newStatus)
                {
                    var previousStatus = status;
                    status = newStatus;
                    if (onTrip != ctx.Asset.Id)
                    {
                        continue;
                    }
                    var overridden = Bool(after, "Overridden") == true;
                    var overrideDetail = overridden && Str(after, "OverrideReason") is { Length: > 0 } why ? $"conflicten overschreven — reden: {why}" : null;
                    string? title = newStatus switch
                    {
                        "Planned" or "1" when previousStatus is "InProgress" or "2" => null,
                        "Planned" or "1" => $"Ingepland op {trip.TripNumber}",
                        "InProgress" or "2" => $"Rit {trip.TripNumber} gestart",
                        "Completed" or "3" => $"Rit {trip.TripNumber} afgerond",
                        "Cancelled" or "4" => $"Rit {trip.TripNumber} geannuleerd",
                        "Draft" or "0" when previousStatus is not ("Draft" or "0") => $"Rit {trip.TripNumber} teruggezet naar concept",
                        _ => null,
                    };
                    if (title is null)
                    {
                        continue;
                    }
                    var detail = Join($"Rit van {L.Date(trip.TripDate)}",
                        newStatus is "Planned" or "1" && driver is not null ? $"chauffeur {driver}" : null,
                        overrideDetail);
                    lines.Add(Line.From(a, "planning", title, detail, link: link));
                }
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    internal enum AssetKind { Vehicle, Trailer }

    internal sealed record Asset(AssetKind Kind, Guid Id, string Number, string Plate)
    {
        public string BasePath => Kind == AssetKind.Vehicle ? $"/vehicles/{Id}" : $"/trailers/{Id}";
    }

    private sealed record BuildContext(
        Asset Asset,
        bool IncludeFinancial,
        IReadOnlyDictionary<Guid, string> DriverNames,
        IReadOnlyDictionary<Guid, string> OtherAssetNames,
        IReadOnlyDictionary<Guid, string> TripNumbers)
    {
        public string Tab(string tab) => $"{Asset.BasePath}?tab={tab}";
    }

    internal sealed record Line(
        string Id,
        DateTime At,
        Guid? UserId,
        bool System,
        string Category,
        string Title,
        string? Detail,
        string? FinancialDetail,
        string? LinkPath,
        string? Technical,
        int Rank = 0)
    {
        public static Line From(AuditLog a, string category, string title, string? detail,
            string? financialDetail = null, string? link = null) =>
            new(a.Id.ToString(), DateTime.SpecifyKind(a.Timestamp, DateTimeKind.Utc), a.UserId, false, category, title, detail,
                financialDetail, link, TechnicalOf(a));

        private static string TechnicalOf(AuditLog a)
        {
            var text = $"{a.EntityType}.{a.Action}: {a.OldValuesJson ?? "—"} → {a.NewValuesJson ?? "—"}";
            return text.Length > 600 ? text[..600] + "…" : text;
        }
    }

    internal static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool Has(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out _);

    internal static string? Str(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e || !e.TryGetProperty(name, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    internal static decimal? Dec(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal() : null;

    internal static bool? Bool(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    internal static Guid? GuidValue(JsonElement? element, string name) =>
        Str(element, name) is { } s && System.Guid.TryParse(s, out var g) ? g : null;

    internal static DateOnly? DateOnlyValue(JsonElement? element, string name)
    {
        var s = Str(element, name);
        if (s is null)
        {
            return null;
        }
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? DateOnly.FromDateTime(dt) : null;
    }

    private static bool JsonEquals(JsonElement a, JsonElement b) => a.GetRawText() == b.GetRawText();

    private static string FormatValue(string property, JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "—",
            JsonValueKind.True => "ja",
            JsonValueKind.False => "nee",
            JsonValueKind.Number when property == "OdometerKm" => L.Km(value.GetDecimal()),
            JsonValueKind.Number when property is "PayloadKg" or "CapacityKg" or "GrossVehicleWeightKg" => L.Kg(value.GetDecimal()),
            JsonValueKind.Number => value.GetDecimal().ToString("#,##0.##", CultureInfo.GetCultureInfo("nl-BE")),
            JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : "—",
            _ => "—",
        };
    }

    internal static string? Join(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return present.Count == 0 ? null : string.Join(" · ", present);
    }

    private static string? Capitalize(string? text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("nl-BE")) + text[1..];

    private static string Lower(string text) =>
        text.Length > 1 && char.IsUpper(text[1]) ? text : char.ToLower(text[0], CultureInfo.GetCultureInfo("nl-BE")) + text[1..];
}

/// <summary>
/// FL-H3 — the readiness moments on the timeline (attributed to <c>System</c>): when a
/// maintenance window or heavy damage makes the asset undeployable, and when it becomes
/// deployable again because the LAST blocker went away. Kept in one place so it can be swapped
/// for the shared readiness evaluator later; it only reconstructs from audit rows.
///
/// Blockers considered: maintenance (from the start of its window or the moment it was started,
/// until completion/cancellation or the end of the window), open heavy damage (Zwaar / Totaal
/// verlies until Hersteld/Afgesloten/removed), the manual operational status In onderhoud /
/// Buiten dienst, and deactivation.
/// </summary>
internal static class FleetReadinessTimeline
{
    private static readonly TimeZoneInfo Brussels = ResolveBrussels();

    private sealed record Interval(string Kind, Guid? SourceId, DateTime Start, DateTime? End, string StartTitle, string? Detail, string? Link);

    public static IEnumerable<FleetHistoryService.Line> Derive(
        FleetHistoryService.Asset asset,
        List<AuditLog> assetAudits,
        List<AuditLog> maintenanceAudits,
        IReadOnlyDictionary<Guid, MaintenanceRecord> maintenance,
        List<AuditLog> damageAudits,
        IReadOnlyDictionary<Guid, DamageReport> damage,
        DateTime now)
    {
        var intervals = new List<Interval>();
        var noun = asset.Kind == FleetHistoryService.AssetKind.Vehicle ? "Voertuig" : "Oplegger";
        var maintenanceLink = $"{asset.BasePath}?tab=onderhoud";
        var damageLink = asset.Kind == FleetHistoryService.AssetKind.Vehicle ? maintenanceLink : $"{asset.BasePath}?tab=schade";

        // Manual status and deactivation (no lines of their own — the user's line says it).
        DateTime? manualStart = null;
        DateTime? inactiveStart = null;
        foreach (var a in assetAudits.OrderBy(a => a.Timestamp))
        {
            var at = Utc(a.Timestamp);
            if (a.Action == "Updated")
            {
                var status = FleetHistoryService.Str(FleetHistoryService.Parse(a.NewValuesJson), "OperationalStatus");
                if (status is null) continue;
                if (FleetHistoryLabels.IsBlockingStatus(status)) manualStart ??= at;
                else if (manualStart is { } s) { intervals.Add(new("manual", null, s, at, "", null, null)); manualStart = null; }
            }
            else if (a.Action == "Deactivated") inactiveStart ??= at;
            else if (a.Action == "Activated" && inactiveStart is { } i) { intervals.Add(new("inactive", null, i, at, "", null, null)); inactiveStart = null; }
        }
        if (manualStart is { } ms) intervals.Add(new("manual", null, ms, null, "", null, null));
        if (inactiveStart is { } ins) intervals.Add(new("inactive", null, ins, null, "", null, null));

        // Maintenance windows.
        foreach (var record in maintenance.Values)
        {
            var audits = maintenanceAudits.Where(a => a.EntityId == record.Id.ToString()).OrderBy(a => a.Timestamp).ToList();
            DateTime? started = audits.FirstOrDefault(a => FleetHistoryService.Str(FleetHistoryService.Parse(a.NewValuesJson), "Status") is "InProgress" or "1") is { } s
                ? Utc(s.Timestamp) : null;
            DateTime? windowStart = record.ScheduledDate is { } sd ? StartOfDay(sd) : null;
            DateTime? windowEnd = record.ScheduledDate is { } sd2 ? StartOfDay((record.ScheduledEndDate ?? sd2).AddDays(1)) : null;
            DateTime? closed = audits.FirstOrDefault(a => a.Action == "Completed"
                    || FleetHistoryService.Str(FleetHistoryService.Parse(a.NewValuesJson), "Status") is "Cancelled" or "3")
                is { } c ? Utc(c.Timestamp) : null;
            var deleted = audits.FirstOrDefault(a => a.Action == "Deleted") is { } d ? Utc(d.Timestamp) : (DateTime?)null;
            closed = Min(closed, deleted);

            var start = Min(started, windowStart);
            if (start is null) continue;
            // A record closed before its window even began never blocked anything.
            if (closed is { } cl && cl <= start) continue;
            // Started work blocks until it is closed; a planned window blocks for its days only.
            var end = started is null ? Min(closed, windowEnd) : closed;
            var label = FleetHistoryLabels.MaintenanceType(record.MaintenanceType, record.CustomTypeName);
            var window = record.ScheduledDate is { } wd ? FleetHistoryLabels.Window(wd, record.ScheduledEndDate) : null;
            intervals.Add(new("maintenance", record.Id, start.Value, end,
                $"{noun} niet inzetbaar wegens onderhoud{(window is null ? "" : $" ({window})")}", label, maintenanceLink));
        }

        // Heavy damage.
        foreach (var report in damage.Values)
        {
            var audits = damageAudits.Where(a => a.EntityId == report.Id.ToString()).OrderBy(a => a.Timestamp).ToList();
            DateTime? openFrom = null;
            string? severity = null;
            string? status = "Reported";
            foreach (var a in audits)
            {
                var after = FleetHistoryService.Parse(a.NewValuesJson);
                var at = Utc(a.Timestamp);
                if (a.Action == "Deleted")
                {
                    if (openFrom is { } of) intervals.Add(DamageInterval(report, of, at, noun, damageLink));
                    openFrom = null;
                    break;
                }
                severity = FleetHistoryService.Str(after, "Severity") ?? severity;
                status = FleetHistoryService.Str(after, "Status") ?? status;
                var blocking = FleetHistoryLabels.IsBlockingSeverity(severity) && !FleetHistoryLabels.IsResolvedDamageStatus(status);
                if (blocking && openFrom is null) openFrom = at;
                else if (!blocking && openFrom is { } from) { intervals.Add(DamageInterval(report, from, at, noun, damageLink)); openFrom = null; }
            }
            if (openFrom is { } stillOpen) intervals.Add(DamageInterval(report, stillOpen, null, noun, damageLink));
        }

        bool BlockedAt(DateTime t, Interval except) =>
            intervals.Any(i => !ReferenceEquals(i, except) && i.Start <= t && (i.End is null || i.End > t));

        var lines = new List<FleetHistoryService.Line>();
        foreach (var interval in intervals.Where(i => i.Kind is "maintenance" or "damage"))
        {
            if (interval.Start <= now && !BlockedAt(interval.Start, interval))
            {
                lines.Add(new($"readiness:{interval.Kind}:{interval.SourceId}:start", interval.Start, null, true, "readiness",
                    interval.StartTitle, interval.Detail, null, interval.Link, null, Rank: 1));
            }
            if (interval.End is { } end && end <= now && !BlockedAt(end, interval))
            {
                lines.Add(new($"readiness:{interval.Kind}:{interval.SourceId}:end", end, null, true, "readiness",
                    $"{noun} opnieuw inzetbaar", interval.Kind == "maintenance" ? "Onderhoud is afgelopen; er is geen andere blokkering." : "Schade is hersteld; er is geen andere blokkering.",
                    null, interval.Link, null, Rank: 1));
            }
        }
        return lines;
    }

    private static Interval DamageInterval(DamageReport report, DateTime from, DateTime? to, string noun, string link) =>
        new("damage", report.Id, from, to, $"{noun} niet inzetbaar wegens zware schade", report.Description, link);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime StartOfDay(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Brussels);
    }

    private static DateTime? Min(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : (a < b ? a : b);

    private static TimeZoneInfo ResolveBrussels()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
