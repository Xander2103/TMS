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

    Task<TripOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
