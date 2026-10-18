using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IMyActivitiesService
{
    /// <summary>The signed-in driver's standalone activities in the window; empty without a driver profile.</summary>
    Task<IReadOnlyList<MyActivityDto>> ListMineAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    /// <summary>Null when the activity is not assigned to the signed-in driver (no existence leak).</summary>
    Task<MyActivityDto?> StartMineAsync(Guid activityId, CancellationToken cancellationToken);

    /// <summary>Completes the driver's own activity; the dossier auto-confirmation runs as for office completion.</summary>
    Task<MyActivityDto?> CompleteMineAsync(Guid activityId, CancellationToken cancellationToken);
}

/// <summary>
/// T-H7: the person doing standalone work (the operator on site) starts and completes it from the
/// driver app. Strictly self-scoped through the signed-in user's driver profile, like "my trips";
/// the transitions themselves are the office ones (<see cref="IDossierActivityExecutionService"/>),
/// so the status model, the date rules and the lifecycle re-evaluation are shared, not copied.
/// </summary>
public class MyActivitiesService : IMyActivitiesService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDossierActivityExecutionService _execution;
    private readonly TimeProvider _timeProvider;

    public MyActivitiesService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUser,
        IDossierActivityExecutionService execution, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _execution = execution;
        _timeProvider = timeProvider;
    }

    private Guid TenantId => _tenantContext.TenantId;

    private Task<Guid?> CurrentDriverIdAsync(CancellationToken cancellationToken) =>
        Planning.Services.DriverIdentity.ResolveAsync(_dbContext, TenantId, _currentUser.CurrentUserId, cancellationToken);

    public async Task<IReadOnlyList<MyActivityDto>> ListMineAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (await CurrentDriverIdAsync(cancellationToken) is not { } driverId)
        {
            return [];
        }

        // Same window as "my trips": from the tenant's today, one week ahead by default.
        var zone = await TenantTimeZone.ForTenantAsync(_dbContext, TenantId, cancellationToken);
        var today = DateOnly.FromDateTime(TenantTimeZone.ToWallClock(_timeProvider.GetUtcNow().UtcDateTime, zone));
        var rangeFrom = from ?? today;
        var rangeTo = to ?? rangeFrom.AddDays(7);

        var ids = await Mine(driverId)
            .Where(a => a.ExecutionStatus != DossierActivityExecutionStatus.Cancelled
                        && (a.PlannedDate == null || (a.PlannedDate >= rangeFrom && a.PlannedDate <= rangeTo)
                            // Work still running from an earlier day stays on the list until it is completed.
                            || (a.PlannedDate < rangeFrom && a.ExecutionStatus == DossierActivityExecutionStatus.InProgress)))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        return await ProjectAsync(ids, cancellationToken);
    }

    public Task<MyActivityDto?> StartMineAsync(Guid activityId, CancellationToken cancellationToken) =>
        TransitionAsync(activityId, (dossierId, ct) => _execution.StartAsync(dossierId, activityId, new DossierActivityTransitionRequest(), ct), cancellationToken);

    public Task<MyActivityDto?> CompleteMineAsync(Guid activityId, CancellationToken cancellationToken) =>
        TransitionAsync(activityId, (dossierId, ct) => _execution.CompleteAsync(dossierId, activityId, new DossierActivityTransitionRequest(), ct), cancellationToken);

    private async Task<MyActivityDto?> TransitionAsync(
        Guid activityId, Func<Guid, CancellationToken, Task<DossierDetailDto?>> transition, CancellationToken cancellationToken)
    {
        if (await CurrentDriverIdAsync(cancellationToken) is not { } driverId)
        {
            return null;
        }

        var dossierId = await Mine(driverId).Where(a => a.Id == activityId).Select(a => (Guid?)a.DossierId)
            .FirstOrDefaultAsync(cancellationToken);
        if (dossierId is null)
        {
            return null;
        }

        await transition(dossierId.Value, cancellationToken);
        return (await ProjectAsync([activityId], cancellationToken)).FirstOrDefault();
    }

    /// <summary>Standalone (no stops), planning-relevant activities assigned to this driver.</summary>
    private IQueryable<DossierActivity> Mine(Guid driverId) =>
        _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.DriverId == driverId && !a.IsDeleted
                        && a.ActivityType!.PlanningRelevant && !a.ActivityType.HasStops);

    private async Task<IReadOnlyList<MyActivityDto>> ProjectAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == TenantId && ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id, a.PlannedDate, a.StartTime, a.DurationHours, a.SiteAddress, a.WorkDescription, a.Label,
                a.ExecutionStatus, a.StartedAt, a.CompletedAt, TypeName = a.ActivityType!.Name,
                Dossier = _dbContext.TransportDossiers.Where(d => d.Id == a.DossierId)
                    .Select(d => new { d.DossierNumber, d.CustomerId }).FirstOrDefault(),
                Vehicle = _dbContext.Vehicles.Where(v => v.TenantId == a.TenantId && v.Id == a.VehicleId)
                    .Select(v => new { v.InternalNumber, v.LicensePlate }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var customerIds = rows.Select(r => r.Dossier?.CustomerId).OfType<Guid>().Distinct().ToList();
        var customers = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == TenantId && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return rows
            .OrderBy(r => r.PlannedDate ?? DateOnly.MaxValue).ThenBy(r => r.StartTime ?? TimeOnly.MaxValue)
            .Select(r => new MyActivityDto(
                r.Id, r.Dossier?.DossierNumber ?? "—",
                r.Dossier?.CustomerId is { } cid && customers.TryGetValue(cid, out var name) ? name : null,
                r.TypeName, r.Label, r.PlannedDate, r.StartTime, r.DurationHours,
                StandaloneActivitySchedule.PlannedEnd(r.PlannedDate, r.StartTime, r.DurationHours),
                r.SiteAddress, r.WorkDescription, r.Vehicle?.InternalNumber, r.Vehicle?.LicensePlate,
                r.ExecutionStatus.ToString(), r.StartedAt, r.CompletedAt))
            .ToList();
    }
}
