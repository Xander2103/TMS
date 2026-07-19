using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public enum AssignmentKind
{
    /// <summary>Vaste chauffeur/voertuig — long-term preference.</summary>
    Fixed,

    /// <summary>Actuele chauffeur/voertuig — temporary operational assignment.</summary>
    Current,
}

public enum AssignmentOutcome
{
    Success,
    NotFound,
    InvalidReference,
    /// <summary>The requested pairing would displace an existing assignment; retry with ReplaceExisting.</summary>
    Conflict,
}

/// <summary>Describes the assignment that would be (or was) displaced.</summary>
public sealed record AssignmentConflictDto(Guid VehicleId, string VehicleLabel, Guid DriverId, string DriverName);

public sealed record AssignmentResult(AssignmentOutcome Outcome, AssignmentConflictDto? Conflict)
{
    public static readonly AssignmentResult Success = new(AssignmentOutcome.Success, null);
    public static readonly AssignmentResult NotFound = new(AssignmentOutcome.NotFound, null);
    public static readonly AssignmentResult InvalidReference = new(AssignmentOutcome.InvalidReference, null);
    public static AssignmentResult ConflictWith(AssignmentConflictDto conflict) => new(AssignmentOutcome.Conflict, conflict);
}

/// <summary>
/// The ONLY write path for the driver↔vehicle relationship (stored on Vehicle.FixedDriverId /
/// Vehicle.CurrentDriverId). Both the vehicle page and the driver page call this service, so
/// the two sides can never drift apart. Reassignments are transactional: clearing the old
/// holder and setting the new one commit together.
/// </summary>
public interface IFleetAssignmentService
{
    /// <summary>Sets (or clears, driverId = null) the driver in the given slot of a vehicle.</summary>
    Task<AssignmentResult> SetVehicleDriverAsync(Guid vehicleId, AssignmentKind kind, Guid? driverId, bool replaceExisting, CancellationToken cancellationToken);

    /// <summary>Sets (or clears, vehicleId = null) the vehicle a driver holds in the given slot.</summary>
    Task<AssignmentResult> SetDriverVehicleAsync(Guid driverId, AssignmentKind kind, Guid? vehicleId, bool replaceExisting, CancellationToken cancellationToken);
}
