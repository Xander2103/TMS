using TransportationService.Api.Modules.Warehousing.Entities;

namespace TransportationService.Api.Modules.Warehousing.Services;

/// <summary>
/// Controlled transitions for dock appointments (same style as the stop-status machine):
/// an explicit adjacency map, no jumps. Timestamps are stamped by the service on the
/// transitions that represent physical events (arrival, start, completion).
/// </summary>
public static class DockAppointmentStatusMachine
{
    private static readonly IReadOnlyDictionary<DockAppointmentStatus, DockAppointmentStatus[]> Transitions =
        new Dictionary<DockAppointmentStatus, DockAppointmentStatus[]>
        {
            [DockAppointmentStatus.Planned] =
                [DockAppointmentStatus.Expected, DockAppointmentStatus.Arrived, DockAppointmentStatus.Cancelled],
            [DockAppointmentStatus.Expected] =
                [DockAppointmentStatus.Arrived, DockAppointmentStatus.NoShow, DockAppointmentStatus.Cancelled],
            [DockAppointmentStatus.Arrived] =
                [DockAppointmentStatus.Waiting, DockAppointmentStatus.AssignedToDock, DockAppointmentStatus.Cancelled],
            [DockAppointmentStatus.Waiting] =
                [DockAppointmentStatus.AssignedToDock, DockAppointmentStatus.Cancelled],
            [DockAppointmentStatus.AssignedToDock] =
                [DockAppointmentStatus.InProgress, DockAppointmentStatus.Waiting, DockAppointmentStatus.Cancelled],
            [DockAppointmentStatus.InProgress] = [DockAppointmentStatus.Completed],
            [DockAppointmentStatus.Completed] = [],
            [DockAppointmentStatus.Cancelled] = [],
            [DockAppointmentStatus.NoShow] = [],
        };

    public static bool IsAllowed(DockAppointmentStatus from, DockAppointmentStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<DockAppointmentStatus> AllowedTargets(DockAppointmentStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : [];

    public static bool IsTerminal(DockAppointmentStatus status) =>
        status is DockAppointmentStatus.Completed or DockAppointmentStatus.Cancelled or DockAppointmentStatus.NoShow;

    /// <summary>Statuses that occupy a dock slot (conflict-relevant).</summary>
    public static bool Occupies(DockAppointmentStatus status) => !IsTerminal(status);
}
