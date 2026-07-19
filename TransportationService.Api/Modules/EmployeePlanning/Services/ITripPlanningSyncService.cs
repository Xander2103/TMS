using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

public enum TripPlanningSyncAction
{
    None,
    Created,
    Updated,
    Moved,
    Cancelled,
    Removed,
}

/// <summary>
/// Outcome of one sync pass. <see cref="Entry"/> is the tracked row so the caller can stamp
/// late-assigned values (the trip number is claimed inside the numbering save callback).
/// </summary>
public sealed record TripPlanningSyncResult(
    TripPlanningSyncAction Action,
    Guid? EntryId,
    Guid? PreviousEmployeeId,
    Guid? EmployeeId,
    TripPlanningEntry? Entry)
{
    public static readonly TripPlanningSyncResult None = new(TripPlanningSyncAction.None, null, null, null, null);
}

/// <summary>
/// Projects trips into <see cref="TripPlanningEntry"/> rows. All methods STAGE changes on the
/// shared DbContext and never save — the calling trip mutation saves, keeping trip + planning
/// entry in one atomic transaction. Callers audit using the returned action.
/// </summary>
public interface ITripPlanningSyncService
{
    /// <summary>Upserts (or soft-deletes, when the trip has no driver) the entry for this trip.</summary>
    Task<TripPlanningSyncResult> ApplyAsync(Trip trip, CancellationToken cancellationToken);

    /// <summary>Refreshes ActualStart/ActualEnd from the trip's stop executions.</summary>
    Task<TripPlanningSyncResult> ApplyActualsAsync(Guid tripId, CancellationToken cancellationToken);

    /// <summary>Soft-deletes the entry because the trip itself is being deleted.</summary>
    Task<TripPlanningSyncResult> ApplyRemovalAsync(Guid tripId, CancellationToken cancellationToken);
}
