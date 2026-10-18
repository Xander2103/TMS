using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

/// <summary>
/// EX-03: the ONE classification of "vehicle safety issue". Not every incident touches fleet
/// readiness — only a damage report does (existing Damage rules). This rule decides when the
/// office is PROMPTED to create one and when the fleet is told.
/// </summary>
public static class VehicleSafetyRules
{
    public static bool IsVehicleType(IncidentType type) => type is IncidentType.VehicleBreakdown or IncidentType.Accident;

    /// <summary>A breakdown/accident of High or Critical severity, or Critical damage, on a known vehicle/trailer.</summary>
    public static bool IsSafetyIssue(Incident incident) =>
        (incident.VehicleId is not null || incident.TrailerId is not null)
        && (IsVehicleType(incident.IncidentType)
            ? incident.Severity >= IncidentSeverity.High
            : incident.IncidentType == IncidentType.Damage && incident.Severity == IncidentSeverity.Critical);

    /// <summary>A vehicle-issue afwijking of High or Critical severity (the trip knows the vehicle).</summary>
    public static bool IsSafetyIssue(ExecutionException exception) =>
        exception.Type == ExecutionExceptionType.VehicleIssue && exception.Severity >= ExceptionSeverity.High;

    /// <summary>Pre-fill for the damage report: Critical → Severe (blocks readiness), High/Medium → Moderate, Low → Minor.</summary>
    public static DamageSeverity SuggestedDamageSeverity(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical => DamageSeverity.Severe,
        IncidentSeverity.Low => DamageSeverity.Minor,
        _ => DamageSeverity.Moderate,
    };

    public static DamageSeverity SuggestedDamageSeverity(ExceptionSeverity severity) => severity switch
    {
        ExceptionSeverity.Critical => DamageSeverity.Severe,
        ExceptionSeverity.Low => DamageSeverity.Minor,
        _ => DamageSeverity.Moderate,
    };
}

/// <summary>
/// Side effects every problem registration shares, after the business save:
/// <list type="bullet">
/// <item>EX-03: a vehicle safety issue notifies the fleet (damage_reports.edit holders) ONCE per problem
/// (FleetNotifiedAt), with a link the fleet can open (the vehicle's damage tab) — no customer, financial or HR data.</item>
/// <item>EX-05: a problem that concerns an ALREADY confirmed dossier never un-confirms it; it is recorded on the
/// dossier ("ProblemAfterConfirmation", shown in the dossier history) and dossier managers are told.</item>
/// </list>
/// Failures here never undo the committed problem.
/// </summary>
public static class ProblemSignals
{
    public const string AuditProblemAfterConfirmation = "ProblemAfterConfirmation";

    public static async Task AfterIncidentRegisteredAsync(
        TransportationDbContext db, Guid tenantId, Incident incident,
        INotificationService notifications, IAuditService audit, CancellationToken cancellationToken)
    {
        await NotifyFleetAsync(db, tenantId, incident, notifications, cancellationToken);
        var dossiers = await ProblemScope.DossiersOfIncidentAsync(db, tenantId, incident, cancellationToken);
        await RecordAfterConfirmationAsync(db, tenantId, dossiers, "Incident", incident.Id, incident.Title,
            incident.Severity.ToString(), $"/incidents/{incident.Id}", notifications, audit, cancellationToken);
    }

    /// <summary>After an office edit: a severity raised to a safety issue, or a newly reached confirmed dossier.</summary>
    public static async Task AfterIncidentChangedAsync(
        TransportationDbContext db, Guid tenantId, Incident incident,
        Guid? previousDossierId, Guid? previousOrderId, Guid? previousTripId,
        INotificationService notifications, IAuditService audit, CancellationToken cancellationToken)
    {
        await NotifyFleetAsync(db, tenantId, incident, notifications, cancellationToken);
        if (incident.DossierId == previousDossierId && incident.TransportOrderId == previousOrderId && incident.TripId == previousTripId)
        {
            return;
        }

        var before = await ProblemScope.DossiersOfIncidentAsync(db, tenantId,
            new Incident { DossierId = previousDossierId, TransportOrderId = previousOrderId, TripId = previousTripId }, cancellationToken);
        var now = await ProblemScope.DossiersOfIncidentAsync(db, tenantId, incident, cancellationToken);
        await RecordAfterConfirmationAsync(db, tenantId, now.Except(before).ToList(), "Incident", incident.Id, incident.Title,
            incident.Severity.ToString(), $"/incidents/{incident.Id}", notifications, audit, cancellationToken);
    }

    public static async Task AfterExceptionReportedAsync(
        TransportationDbContext db, Guid tenantId, ExecutionException exception, string label,
        INotificationService notifications, IAuditService audit, CancellationToken cancellationToken)
    {
        await NotifyFleetAsync(db, tenantId, exception, label, notifications, cancellationToken);
        var dossiers = await ProblemScope.DossiersOfExceptionAsync(db, tenantId, exception, cancellationToken);
        await RecordAfterConfirmationAsync(db, tenantId, dossiers, "Afwijking", exception.Id, label,
            exception.Severity.ToString(), $"/exceptions/{exception.Id}", notifications, audit, cancellationToken);
    }

    public static async Task AfterExceptionChangedAsync(
        TransportationDbContext db, Guid tenantId, ExecutionException exception, string label,
        INotificationService notifications, CancellationToken cancellationToken) =>
        await NotifyFleetAsync(db, tenantId, exception, label, notifications, cancellationToken);

    private static async Task NotifyFleetAsync(
        TransportationDbContext db, Guid tenantId, Incident incident, INotificationService notifications, CancellationToken cancellationToken)
    {
        if (incident.FleetNotifiedAt is not null || !VehicleSafetyRules.IsSafetyIssue(incident))
        {
            return;
        }

        var (assetLabel, link) = await AssetAsync(db, tenantId, incident.VehicleId, incident.TrailerId, cancellationToken);
        var trip = await TripNumberAsync(db, tenantId, incident.TripId, cancellationToken);
        incident.FleetNotifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPermissionHoldersAsync(
            PermissionCodes.DamageReportsEdit, "vehicle_safety_issue",
            $"Voertuigprobleem gemeld: {assetLabel}",
            $"{SeverityLabel(incident.Severity.ToString())} voertuigprobleem op {assetLabel}{(trip is null ? string.Empty : $" (rit {trip})")}: {incident.Title}. "
            + "Controleer de inzetbaarheid; een schademelding bepaalt de beschikbaarheid.",
            link, cancellationToken);
    }

    private static async Task NotifyFleetAsync(
        TransportationDbContext db, Guid tenantId, ExecutionException exception, string label,
        INotificationService notifications, CancellationToken cancellationToken)
    {
        if (exception.FleetNotifiedAt is not null || !VehicleSafetyRules.IsSafetyIssue(exception))
        {
            return;
        }

        var trip = await db.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Id == exception.TripId)
            .Select(t => new { t.TripNumber, t.VehicleId, t.TrailerId })
            .FirstOrDefaultAsync(cancellationToken);
        if (trip is null || (trip.VehicleId is null && trip.TrailerId is null))
        {
            return;
        }

        var (assetLabel, link) = await AssetAsync(db, tenantId, trip.VehicleId, trip.VehicleId is null ? trip.TrailerId : null, cancellationToken);
        exception.FleetNotifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyPermissionHoldersAsync(
            PermissionCodes.DamageReportsEdit, "vehicle_safety_issue",
            $"Voertuigprobleem gemeld: {assetLabel}",
            $"{SeverityLabel(exception.Severity.ToString())} afwijking ({label.ToLowerInvariant()}) op {assetLabel} (rit {trip.TripNumber}). "
            + "Controleer de inzetbaarheid; een schademelding bepaalt de beschikbaarheid.",
            link, cancellationToken);
    }

    private static async Task RecordAfterConfirmationAsync(
        TransportationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> dossierIds, string kind, Guid problemId,
        string title, string severity, string link, INotificationService notifications, IAuditService audit,
        CancellationToken cancellationToken)
    {
        if (dossierIds.Count == 0)
        {
            return;
        }

        var confirmed = await db.TransportDossiers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && dossierIds.Contains(d.Id) && d.Status == DossierStatus.Closed)
            .Select(d => new { d.Id, d.DossierNumber })
            .ToListAsync(cancellationToken);
        foreach (var dossier in confirmed)
        {
            await audit.RecordAsync("TransportDossier", dossier.Id.ToString(), AuditProblemAfterConfirmation, null,
                new { Kind = kind, ProblemId = problemId, Title = title, Severity = severity }, cancellationToken);
            await notifications.NotifyPermissionHoldersAsync(
                PermissionCodes.DossiersManage, "problem_after_confirmation",
                $"Probleem na bevestiging: {dossier.DossierNumber}",
                $"{kind} '{title}' werd gemeld op het al bevestigde dossier {dossier.DossierNumber}. Het dossier blijft bevestigd; bekijk of opvolging nodig is.",
                link, cancellationToken);
        }
    }

    private static async Task<(string Label, string Link)> AssetAsync(
        TransportationDbContext db, Guid tenantId, Guid? vehicleId, Guid? trailerId, CancellationToken cancellationToken)
    {
        if (vehicleId is { } v)
        {
            var plate = await db.Vehicles.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == v)
                .Select(x => x.LicensePlate).FirstOrDefaultAsync(cancellationToken);
            return (plate ?? "voertuig", $"/vehicles/{v}?tab=schade");
        }

        var trailerPlate = trailerId is { } t
            ? await db.Trailers.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == t)
                .Select(x => x.LicensePlate).FirstOrDefaultAsync(cancellationToken)
            : null;
        return (trailerPlate ?? "oplegger", trailerId is { } tid ? $"/trailers/{tid}" : "/vehicles");
    }

    private static async Task<string?> TripNumberAsync(TransportationDbContext db, Guid tenantId, Guid? tripId, CancellationToken cancellationToken) =>
        tripId is { } id
            ? await db.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.Id == id).Select(t => t.TripNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

    private static string SeverityLabel(string severity) => severity switch
    {
        "Critical" => "Kritiek",
        "High" => "Ernstig",
        "Medium" => "Gemiddeld",
        _ => "Licht",
    };
}
