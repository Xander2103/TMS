using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

/// <summary>Next-working-day arithmetic: skips Saturday, Sunday and tenant holidays.</summary>
public static class BusinessDayCalculator
{
    public static DateOnly NextWorkingDay(DateOnly after, IReadOnlySet<DateOnly> holidays)
    {
        var day = after.AddDays(1);
        // Bounded walk: a tenant cannot configure >366 consecutive holidays away.
        for (var i = 0; i < 370; i++)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(day))
            {
                return day;
            }

            day = day.AddDays(1);
        }

        return after.AddDays(1);
    }
}

public interface IFailedDeliveryHandler
{
    /// <summary>Reacts to a stop failing during execution: idempotently creates the problem
    /// record and — depending on TenantSettings.RedeliveryMode — recommends or creates the
    /// redelivery. Never throws into the execution flow (a failed hook must not fail the stop).</summary>
    Task HandleStopFailureAsync(Guid tripId, Guid stopId, string? reason, CancellationToken cancellationToken);
}

/// <summary>
/// Follow-up wave P4: the failed-delivery automation. A stop failure creates exactly ONE
/// incident (unique filtered index on SourceStopId — replays are no-ops), linked to the order,
/// trip, customer and dossier. Mode Propose flags a redelivery recommendation for dispatch;
/// mode Automatic creates the linked redelivery order immediately (same dossier, next working
/// day). The goods themselves stay on the vehicle until a real return scan — see P0.
/// </summary>
public class FailedDeliveryService : IFailedDeliveryHandler
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IIncidentService _incidentService;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FailedDeliveryService>? _logger;

    public FailedDeliveryService(
        TransportationDbContext dbContext, ITenantContext tenantContext,
        IIncidentService incidentService, IAuditService auditService, TimeProvider timeProvider,
        ILogger<FailedDeliveryService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _incidentService = incidentService;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleStopFailureAsync(
        Guid tripId, Guid stopId, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            await HandleCoreAsync(tripId, stopId, reason, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The stop failure itself is already recorded; the automation must never undo that.
            _logger?.LogError(ex, "Failed-delivery automation failed for stop {StopId}", stopId);
        }
    }

    private async Task HandleCoreAsync(Guid tripId, Guid stopId, string? reason, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (await _dbContext.Incidents.AnyAsync(
                i => i.TenantId == tenantId && i.SourceStopId == stopId, cancellationToken))
        {
            return;
        }

        var stop = await _dbContext.TransportOrderStops.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == stopId, cancellationToken);
        if (stop is null)
        {
            return;
        }

        var order = await _dbContext.TransportOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == stop.TransportOrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var dossierId = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == order.Id)
            .OrderBy(l => l.CreatedAt)
            .Select(l => (Guid?)l.DossierId)
            .FirstOrDefaultAsync(cancellationToken);

        var mode = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.RedeliveryMode)
            .FirstOrDefaultAsync(cancellationToken) ?? "Manual";

        var incident = new Incident
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            IncidentType = IncidentType.Other, CustomTypeName = "Mislukte levering",
            Title = $"Mislukte levering {order.OrderNumber}",
            Description = string.IsNullOrWhiteSpace(reason)
                ? $"Levering aan stop {stop.LocationName ?? stop.City ?? "?"} is mislukt."
                : $"Levering aan stop {stop.LocationName ?? stop.City ?? "?"} is mislukt: {reason}",
            Severity = IncidentSeverity.Medium,
            CustomerId = order.CustomerId, TransportOrderId = order.Id, TripId = tripId,
            DossierId = dossierId, SourceStopId = stopId,
            RedeliverySuggested = mode is "Propose" or "Automatic",
        };
        _dbContext.Incidents.Add(incident);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent replay lost the unique-index race — the incident already exists.
            return;
        }

        await _auditService.RecordAsync("Incident", incident.Id.ToString(), "AutoCreatedFromFailedStop",
            null, new { StopId = stopId, order.OrderNumber, Mode = mode, Reason = reason }, cancellationToken);

        if (mode == "Automatic")
        {
            // Idempotent by design: LinkedRedeliveryOrderId refuses a second creation.
            await _incidentService.CreateRedeliveryAsync(incident.Id, cancellationToken);
        }
    }
}
