using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFleetReadinessService
{
    Task<VehicleReadinessDto?> EvaluateVehicleAsync(Guid vehicleId, DateOnly date, CancellationToken cancellationToken);

    Task<VehicleReadinessDto?> EvaluateTrailerAsync(Guid trailerId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Batch variant for pickers and boards: one round trip per source table.</summary>
    Task<IReadOnlyDictionary<Guid, VehicleReadinessDto>> EvaluateVehiclesAsync(
        IReadOnlyCollection<Guid> vehicleIds, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, VehicleReadinessDto>> EvaluateTrailersAsync(
        IReadOnlyCollection<Guid> trailerIds, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// FL-H1/H2/H4 — the ONE fleet-side readiness verdict. It combines what used to be separate,
/// manually synchronised facts:
/// <list type="bullet">
/// <item>the manual operational status (Buiten dienst / In onderhoud) and the active flag;</item>
/// <item>legal documents and inspection records (technische keuring, verzekering);</item>
/// <item>maintenance windows (Gepland on the day, In uitvoering until completed);</item>
/// <item>open damage by severity (Zwaar / Totaal verlies block, Matig warns);</item>
/// <item>conditional items (ADR certificate, kraankeuring) that only matter for matching work.</item>
/// </list>
/// Nothing is persisted: completing maintenance or closing damage simply removes that blocker, and
/// the vehicle is deployable again only when no other blocker remains. Planning maps these issues
/// to its conflicts (see PlanningConflictService), so Fleet and Planning can no longer disagree.
/// </summary>
public class FleetReadinessService : IFleetReadinessService
{
    /// <summary>Document types whose expiry legally prevents driving the asset.</summary>
    private static readonly FleetDocumentType[] LegalBlockingTypes =
        [FleetDocumentType.TechnicalInspection, FleetDocumentType.Insurance];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public FleetReadinessService(TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<VehicleReadinessDto?> EvaluateVehicleAsync(Guid vehicleId, DateOnly date, CancellationToken cancellationToken) =>
        (await EvaluateVehiclesAsync([vehicleId], date, cancellationToken)).GetValueOrDefault(vehicleId);

    public async Task<VehicleReadinessDto?> EvaluateTrailerAsync(Guid trailerId, DateOnly date, CancellationToken cancellationToken) =>
        (await EvaluateTrailersAsync([trailerId], date, cancellationToken)).GetValueOrDefault(trailerId);

    public async Task<IReadOnlyDictionary<Guid, VehicleReadinessDto>> EvaluateVehiclesAsync(
        IReadOnlyCollection<Guid> vehicleIds, DateOnly date, CancellationToken cancellationToken)
    {
        if (vehicleIds.Count == 0)
        {
            return new Dictionary<Guid, VehicleReadinessDto>();
        }

        var tenantId = _tenantContext.TenantId;
        var ids = vehicleIds.Distinct().ToList();
        var assets = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == tenantId && ids.Contains(v.Id))
            .Select(v => new AssetFacts(v.Id, v.InternalNumber, v.LicensePlate, v.IsActive,
                v.OperationalStatus == VehicleOperationalStatus.OutOfService, v.OperationalStatus == VehicleOperationalStatus.InMaintenance,
                v.StatusReason, v.HasCrane, v.AdrSuitable))
            .ToListAsync(cancellationToken);
        var sources = await LoadSourcesAsync(ids, vehicles: true, cancellationToken);
        var tacho = await _dbContext.TachographCalibrations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.VehicleId))
            .GroupBy(c => c.VehicleId)
            .Select(g => new { VehicleId = g.Key, Due = g.Max(c => c.NextCalibrationDue) })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Due, cancellationToken);

        return assets.ToDictionary(a => a.Id, a =>
            Build("Vehicle", a, date, sources.For(a.Id), tacho.TryGetValue(a.Id, out var due) ? due : null));
    }

    public async Task<IReadOnlyDictionary<Guid, VehicleReadinessDto>> EvaluateTrailersAsync(
        IReadOnlyCollection<Guid> trailerIds, DateOnly date, CancellationToken cancellationToken)
    {
        if (trailerIds.Count == 0)
        {
            return new Dictionary<Guid, VehicleReadinessDto>();
        }

        var tenantId = _tenantContext.TenantId;
        var ids = trailerIds.Distinct().ToList();
        var assets = await _dbContext.Trailers.AsNoTracking()
            .Where(t => t.TenantId == tenantId && ids.Contains(t.Id))
            .Select(t => new AssetFacts(t.Id, t.InternalNumber, t.LicensePlate, t.IsActive,
                t.OperationalStatus == TrailerOperationalStatus.OutOfService, t.OperationalStatus == TrailerOperationalStatus.InMaintenance,
                t.StatusReason, false, t.AdrSuitable))
            .ToListAsync(cancellationToken);
        var sources = await LoadSourcesAsync(ids, vehicles: false, cancellationToken);

        return assets.ToDictionary(a => a.Id, a => Build("Trailer", a, date, sources.For(a.Id), null));
    }

    private VehicleReadinessDto Build(string kind, AssetFacts asset, DateOnly date, AssetSources sources, DateOnly? tachographDue)
    {
        var issues = Evaluate(kind, asset, date, Today, sources, tachographDue);
        var level = issues.Any(i => i.Severity == ReadinessSeverity.Blocking && i.Conditional is null)
            ? ReadinessLevel.Blocked
            : issues.Any(i => i.Severity == ReadinessSeverity.Warning || (i.Severity == ReadinessSeverity.Blocking && i.Conditional is not null))
                ? ReadinessLevel.Warning
                : ReadinessLevel.Deployable;
        var headline = issues
            .Where(i => level == ReadinessLevel.Blocked
                ? i.Severity == ReadinessSeverity.Blocking && i.Conditional is null
                : i.Severity != ReadinessSeverity.Info)
            .Select(i => i.Message)
            .ToList();
        var summary = level switch
        {
            ReadinessLevel.Deployable => "Inzetbaar",
            _ => string.Join(" · ", headline.Take(2)) + (headline.Count > 2 ? $" (+{headline.Count - 2})" : string.Empty),
        };

        return new VehicleReadinessDto(asset.Id, kind, asset.Number, asset.Plate, date, level,
            level != ReadinessLevel.Blocked, issues, summary);
    }

    /// <summary>
    /// The rules, pure and date-driven so they can be unit-tested without a database. "date" is the
    /// day the asset would be used; "today" decides whether an open item (overdue maintenance,
    /// in-progress work, open damage) is a present fact.
    /// </summary>
    internal static IReadOnlyList<ReadinessIssueDto> Evaluate(
        string kind, AssetFacts asset, DateOnly date, DateOnly today, AssetSources sources, DateOnly? tachographDue)
    {
        var issues = new List<ReadinessIssueDto>();
        var noun = kind == "Trailer" ? "Oplegger" : "Voertuig";

        if (!asset.IsActive)
        {
            issues.Add(new("inactive", $"{noun} {asset.Number} is gedeactiveerd.", ReadinessSeverity.Blocking));
        }

        if (asset.OutOfService)
        {
            issues.Add(new("status.out_of_service", Reason("Buiten dienst", asset.StatusReason), ReadinessSeverity.Blocking));
        }
        else if (asset.ManualMaintenance)
        {
            issues.Add(new("status.in_maintenance", Reason("In onderhoud (handmatig gemeld)", asset.StatusReason), ReadinessSeverity.Blocking));
        }

        // --- Maintenance windows --------------------------------------------------------------
        foreach (var m in sources.Maintenance)
        {
            var start = m.ScheduledDate;
            var end = m.ScheduledEndDate is { } e && start is { } s && e >= s ? e : start;
            var label = m.Label;
            if (m.Status == MaintenanceStatus.InProgress)
            {
                // In the garage now: unavailable from (planned) start until completion. A date beyond
                // the planned end is only a warning — the work may well be finished by then.
                // Started (possibly earlier than planned): in the garage from today at the latest.
                var from = start is { } st && st <= today ? st : today;
                if (date >= from && (end is null || date <= end || date <= today))
                {
                    issues.Add(new("maintenance.active", $"Onderhoud bezig: {label}{Window(start, end)}.", ReadinessSeverity.Blocking, from, end));
                }
                else if (end is { } plannedEnd && date > plannedEnd)
                {
                    issues.Add(new("maintenance.active_overrun", $"Onderhoud nog niet afgerond (gepland tot {plannedEnd:dd/MM/yyyy}): {label}.", ReadinessSeverity.Warning, start, end));
                }
            }
            else if (m.Status == MaintenanceStatus.Planned && start is { } plannedStart)
            {
                var plannedEnd = end ?? plannedStart;
                if (date >= plannedStart && date <= plannedEnd)
                {
                    issues.Add(new("maintenance.planned", $"Onderhoud gepland{Window(plannedStart, plannedEnd)}: {label}.", ReadinessSeverity.Blocking, plannedStart, plannedEnd));
                }
                else if (plannedEnd < today && date >= today)
                {
                    issues.Add(new("maintenance.overdue", $"Onderhoud achterstallig sinds {plannedStart:dd/MM/yyyy}: {label}.", ReadinessSeverity.Warning, plannedStart, plannedEnd));
                }
            }
        }

        // --- Damage ---------------------------------------------------------------------------
        foreach (var d in sources.Damage.Where(d => d.IncidentDate <= date || d.IncidentDate <= today))
        {
            var severity = d.Severity switch
            {
                DamageSeverity.Severe or DamageSeverity.TotalLoss => ReadinessSeverity.Blocking,
                DamageSeverity.Moderate => ReadinessSeverity.Warning,
                _ => ReadinessSeverity.Info,
            };
            var label = d.Severity switch
            {
                DamageSeverity.TotalLoss => "Totaal verlies",
                DamageSeverity.Severe => "Zware schade",
                DamageSeverity.Moderate => "Matige schade",
                _ => "Lichte schade",
            };
            issues.Add(new($"damage.{d.Severity.ToString().ToLowerInvariant()}",
                $"{label} (gemeld {d.IncidentDate:dd/MM/yyyy}): {Shorten(d.Description)}.", severity, d.IncidentDate));
        }

        // --- Legal documents / inspections ------------------------------------------------------
        // An open (not yet due) technical inspection record means the previous keuring was
        // registered as passed with a later next due date — that also covers the vehicle (FL-M3).
        var nextTechnicalDue = sources.Inspections
            .Where(i => i.Type is InspectionType.VehicleInspection or InspectionType.TrailerInspection)
            .Select(i => (DateOnly?)i.DueDate).Min();
        foreach (var type in LegalBlockingTypes)
        {
            var docs = sources.Documents.Where(x => x.Type == type).ToList();
            var validUntil = docs.Where(x => x.ExpiryDate is not null).Select(x => x.ExpiryDate).Max();
            var name = type == FleetDocumentType.TechnicalInspection ? "Technische keuring" : "Verzekering";
            if (type == FleetDocumentType.TechnicalInspection && nextTechnicalDue is { } nextDue && nextDue >= date)
            {
                continue;
            }
            if (validUntil is { } until && until < date)
            {
                issues.Add(new($"{(type == FleetDocumentType.TechnicalInspection ? "inspection" : "insurance")}.expired",
                    $"{name} verlopen op {until:dd/MM/yyyy}.", ReadinessSeverity.Blocking, Until: until));
            }
        }

        foreach (var i in sources.Inspections)
        {
            if (i.Type == InspectionType.CraneInspection)
            {
                if (i.DueDate < date)
                {
                    issues.Add(new("crane_inspection.overdue", $"Kraankeuring vervallen sinds {i.DueDate:dd/MM/yyyy} — niet inzetbaar voor kraanwerk.",
                        ReadinessSeverity.Blocking, Until: i.DueDate, Conditional: "crane"));
                }
            }
            else if (i.Type is InspectionType.VehicleInspection or InspectionType.TrailerInspection && i.DueDate < date
                     && !issues.Any(x => x.Code == "inspection.expired"))
            {
                issues.Add(new("inspection.overdue", $"Technische keuring achterstallig sinds {i.DueDate:dd/MM/yyyy}.",
                    ReadinessSeverity.Blocking, Until: i.DueDate));
            }
        }

        var craneDocs = sources.Documents.Where(x => x.Type == FleetDocumentType.CraneInspection && x.ExpiryDate is not null).ToList();
        if (craneDocs.Count > 0 && craneDocs.Max(x => x.ExpiryDate) is { } craneUntil && craneUntil < date
            && !issues.Any(x => x.Code == "crane_inspection.overdue"))
        {
            issues.Add(new("crane_inspection.expired", $"Kraankeuring verlopen op {craneUntil:dd/MM/yyyy} — niet inzetbaar voor kraanwerk.",
                ReadinessSeverity.Blocking, Until: craneUntil, Conditional: "crane"));
        }

        // MD-M16 (master-data acceptance 2026-10-18): a crane-equipped vehicle WITHOUT any crane
        // inspection read "Inzetbaar" while its own page warned "Geen Kraankeuring". No evidence at
        // all is the same conditional block as an expired one: not deployable for crane work.
        if (asset.HasCrane
            && !sources.Inspections.Any(x => x.Type == InspectionType.CraneInspection)
            && !sources.Documents.Any(x => x.Type == FleetDocumentType.CraneInspection))
        {
            issues.Add(new("crane_inspection.missing", "Geen kraankeuring geregistreerd — niet inzetbaar voor kraanwerk.",
                ReadinessSeverity.Blocking, Conditional: "crane"));
        }

        var adrDocs = sources.Documents.Where(x => x.Type == FleetDocumentType.AdrCertificate && x.ExpiryDate is not null).ToList();
        if (asset.AdrSuitable && adrDocs.Count > 0 && adrDocs.Max(x => x.ExpiryDate) is { } adrUntil && adrUntil < date)
        {
            issues.Add(new("adr.expired", $"ADR-certificaat verlopen op {adrUntil:dd/MM/yyyy} — niet inzetbaar voor ADR-transport.",
                ReadinessSeverity.Blocking, Until: adrUntil, Conditional: "adr"));
        }

        var coolDocs = sources.Documents.Where(x => x.Type == FleetDocumentType.RefrigerationCertificate && x.ExpiryDate is not null).ToList();
        if (coolDocs.Count > 0 && coolDocs.Max(x => x.ExpiryDate) is { } coolUntil && coolUntil < date)
        {
            issues.Add(new("refrigeration.expired", $"Koelcertificaat verlopen op {coolUntil:dd/MM/yyyy}.", ReadinessSeverity.Warning, Until: coolUntil));
        }

        if (tachographDue is { } tacho && tacho < date)
        {
            issues.Add(new("tachograph.overdue", $"Tachograafijking vervallen op {tacho:dd/MM/yyyy}.", ReadinessSeverity.Warning, Until: tacho));
        }

        // --- Leasing ---------------------------------------------------------------------------
        foreach (var lease in sources.Leasing)
        {
            if (lease.EndDate < date)
            {
                issues.Add(new("lease.ended", $"Leasingcontract ({lease.Company}) beëindigd op {lease.EndDate:dd/MM/yyyy}.", ReadinessSeverity.Warning, Until: lease.EndDate));
            }
            else if (lease.EndDate <= date.AddDays(30))
            {
                issues.Add(new("lease.ending", $"Leasingcontract ({lease.Company}) eindigt op {lease.EndDate:dd/MM/yyyy}.", ReadinessSeverity.Info, Until: lease.EndDate));
            }
        }

        return issues;
    }

    private async Task<SourceSet> LoadSourcesAsync(List<Guid> ids, bool vehicles, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var maintenance = await _dbContext.MaintenanceRecords.AsNoTracking()
            .Where(m => m.TenantId == tenantId
                        && (vehicles ? m.VehicleId != null && ids.Contains(m.VehicleId.Value) : m.TrailerId != null && ids.Contains(m.TrailerId.Value))
                        && (m.Status == MaintenanceStatus.Planned || m.Status == MaintenanceStatus.InProgress))
            .Select(m => new { Owner = vehicles ? m.VehicleId!.Value : m.TrailerId!.Value, m.Status, m.ScheduledDate, m.ScheduledEndDate, m.MaintenanceType, m.CustomTypeName, m.Description })
            .ToListAsync(cancellationToken);
        var damage = await _dbContext.DamageReports.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && (vehicles ? d.VehicleId != null && ids.Contains(d.VehicleId.Value) : d.TrailerId != null && ids.Contains(d.TrailerId.Value))
                        && d.Status != DamageStatus.Repaired && d.Status != DamageStatus.Closed)
            .Select(d => new { Owner = vehicles ? d.VehicleId!.Value : d.TrailerId!.Value, d.Severity, d.IncidentDate, d.Description })
            .ToListAsync(cancellationToken);
        var documents = await _dbContext.FleetDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && (vehicles ? d.VehicleId != null && ids.Contains(d.VehicleId.Value) : d.TrailerId != null && ids.Contains(d.TrailerId.Value)))
            .Select(d => new { Owner = vehicles ? d.VehicleId!.Value : d.TrailerId!.Value, d.DocumentType, d.ExpiryDate })
            .ToListAsync(cancellationToken);
        var inspections = await _dbContext.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.CompletedDate == null
                        && (vehicles ? i.VehicleId != null && ids.Contains(i.VehicleId.Value) : i.TrailerId != null && ids.Contains(i.TrailerId.Value)))
            .Select(i => new { Owner = vehicles ? i.VehicleId!.Value : i.TrailerId!.Value, i.InspectionType, i.DueDate })
            .ToListAsync(cancellationToken);
        var leasing = await _dbContext.LeasingContracts.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.IsActive && l.EndDate != null
                        && (vehicles ? l.VehicleId != null && ids.Contains(l.VehicleId.Value) : l.TrailerId != null && ids.Contains(l.TrailerId.Value)))
            .Select(l => new { Owner = vehicles ? l.VehicleId!.Value : l.TrailerId!.Value, l.LeasingCompany, l.EndDate })
            .ToListAsync(cancellationToken);

        return new SourceSet(
            maintenance.ToLookup(m => m.Owner, m => new MaintenanceFact(m.Status, m.ScheduledDate, m.ScheduledEndDate,
                MaintenanceLabel(m.MaintenanceType, m.CustomTypeName, m.Description))),
            damage.ToLookup(d => d.Owner, d => new DamageFact(d.Severity, d.IncidentDate, d.Description)),
            documents.ToLookup(d => d.Owner, d => new DocumentFact(d.DocumentType, d.ExpiryDate)),
            inspections.ToLookup(i => i.Owner, i => new InspectionFact(i.InspectionType, i.DueDate)),
            leasing.ToLookup(l => l.Owner, l => new LeaseFact(l.LeasingCompany, l.EndDate!.Value)));
    }

    internal static string MaintenanceLabel(MaintenanceType type, string? custom, string? description)
    {
        var typeName = type switch
        {
            MaintenanceType.PeriodicService => "Periodiek onderhoud",
            MaintenanceType.Repair => "Herstelling",
            MaintenanceType.TireService => "Bandenservice",
            MaintenanceType.BrakeService => "Remmenservice",
            MaintenanceType.Revision => "Revisie",
            _ => custom ?? "Onderhoud",
        };
        return string.IsNullOrWhiteSpace(description) ? typeName : $"{typeName} — {Shorten(description)}";
    }

    private static string Window(DateOnly? start, DateOnly? end) => start is not { } s
        ? string.Empty
        : end is { } e && e != s ? $" van {s:dd/MM/yyyy} t/m {e:dd/MM/yyyy}" : $" op {s:dd/MM/yyyy}";

    private static string Reason(string text, string? reason) => string.IsNullOrWhiteSpace(reason) ? $"{text}." : $"{text} — {reason.Trim()}.";

    private static string Shorten(string? text)
    {
        var value = (text ?? string.Empty).Trim().TrimEnd('.');
        return value.Length <= 80 ? value : value[..77] + "…";
    }

    internal sealed record AssetFacts(Guid Id, string Number, string Plate, bool IsActive, bool OutOfService, bool ManualMaintenance,
        string? StatusReason, bool HasCrane, bool AdrSuitable);

    internal sealed record MaintenanceFact(MaintenanceStatus Status, DateOnly? ScheduledDate, DateOnly? ScheduledEndDate, string Label);

    internal sealed record DamageFact(DamageSeverity Severity, DateOnly IncidentDate, string Description);

    internal sealed record DocumentFact(FleetDocumentType Type, DateOnly? ExpiryDate);

    internal sealed record InspectionFact(InspectionType Type, DateOnly DueDate);

    internal sealed record LeaseFact(string Company, DateOnly EndDate);

    internal sealed record AssetSources(
        IReadOnlyList<MaintenanceFact> Maintenance, IReadOnlyList<DamageFact> Damage, IReadOnlyList<DocumentFact> Documents,
        IReadOnlyList<InspectionFact> Inspections, IReadOnlyList<LeaseFact> Leasing);

    private sealed record SourceSet(
        ILookup<Guid, MaintenanceFact> Maintenance, ILookup<Guid, DamageFact> Damage, ILookup<Guid, DocumentFact> Documents,
        ILookup<Guid, InspectionFact> Inspections, ILookup<Guid, LeaseFact> Leasing)
    {
        public AssetSources For(Guid id) => new(Maintenance[id].ToList(), Damage[id].ToList(), Documents[id].ToList(),
            Inspections[id].ToList(), Leasing[id].ToList());
    }
}
