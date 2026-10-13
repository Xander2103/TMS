using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Services;

public interface ITripService
{
    /// <summary>Trips within a date window (defaults to the requested day, or today), optionally filtered.</summary>
    Task<IReadOnlyList<TripListItemDto>> ListAsync(
        DateOnly? from, DateOnly? to, TripStatus? status, Guid? driverId, CancellationToken cancellationToken);

    Task<TripDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TripOperationResult> CreateAsync(CreateTripRequest request, CancellationToken cancellationToken);

    /// <summary>Assignment and order-list edits are Draft-only; revert a planned trip first.</summary>
    Task<TripOperationResult> UpdateAsync(Guid id, UpdateTripRequest request, CancellationToken cancellationToken);

    /// <summary>Dry-run of the conflict engine for the current assignment.</summary>
    Task<TripOperationResult> ValidateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Guarded transition. Draft→Planned runs the conflict engine (blocking conflicts stop it unless
    /// <paramref name="allowOverride"/>) and propagates order statuses on every transition.
    /// </summary>
    Task<TripOperationResult> ChangeStatusAsync(
        Guid id, TripStatus target, bool allowOverride, bool releaseOverride, string? overrideReason,
        CancellationToken cancellationToken);

    /// <summary>As above, with an optimistic-concurrency check when <paramref name="version"/> is supplied.</summary>
    Task<TripOperationResult> ChangeStatusAsync(
        Guid id, TripStatus target, bool allowOverride, bool releaseOverride, string? overrideReason, Guid? version,
        CancellationToken cancellationToken);

    Task<TripOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    // Targeted planning-center commands: allowed on Draft AND Planned trips; a Planned trip is
    // re-validated by the conflict engine and blocking conflicts need an override with reason.

    /// <summary>Appends orders to the trip (incremental; existing orders stay untouched).</summary>
    Task<TripOperationResult> AssignOrdersAsync(Guid id, AssignOrdersRequest request, CancellationToken cancellationToken);

    Task<TripOperationResult> RemoveOrderAsync(Guid id, Guid orderId, Guid? version, CancellationToken cancellationToken);

    /// <summary>Resequences the trip's orders; the list must contain exactly the current set.</summary>
    Task<TripOperationResult> ReorderOrdersAsync(Guid id, ReorderTripOrdersRequest request, CancellationToken cancellationToken);

    Task<TripOperationResult> AssignDriverAsync(Guid id, AssignResourceRequest request, CancellationToken cancellationToken);

    Task<TripOperationResult> AssignVehicleAsync(Guid id, AssignResourceRequest request, CancellationToken cancellationToken);

    Task<TripOperationResult> AssignTrailerAsync(Guid id, AssignResourceRequest request, CancellationToken cancellationToken);

    Task<TripOperationResult> RescheduleAsync(Guid id, RescheduleTripRequest request, CancellationToken cancellationToken);

    /// <summary>Dry-run: conflicts a hypothetical assignment WOULD create. Never mutates.</summary>
    Task<TripOperationResult> ValidateAssignmentAsync(Guid id, ValidateAssignmentRequest request, CancellationToken cancellationToken);
}
