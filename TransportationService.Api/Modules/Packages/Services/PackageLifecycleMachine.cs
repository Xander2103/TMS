using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.Packages.Services;

/// <summary>
/// The single server-side authority on package lifecycle transitions. Impossible jumps
/// (Delivered→Loaded, Cancelled→anything, Missing→Delivered) simply have no edge; a
/// missing package must first be resolved through a controlled action (found → Loaded,
/// removed → Cancelled, return → ReturnPending) before it can ever reach Delivered.
/// </summary>
public static class PackageLifecycleMachine
{
    private static readonly IReadOnlyDictionary<PackageLifecycleStatus, PackageLifecycleStatus[]> Transitions =
        new Dictionary<PackageLifecycleStatus, PackageLifecycleStatus[]>
        {
            [PackageLifecycleStatus.Created] =
                [PackageLifecycleStatus.Labelled, PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded,
                 PackageLifecycleStatus.Missing, PackageLifecycleStatus.Damaged, PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.Labelled] =
                [PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded,
                 PackageLifecycleStatus.Missing, PackageLifecycleStatus.Damaged, PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.AwaitingLoading] =
                [PackageLifecycleStatus.Loaded, PackageLifecycleStatus.Missing, PackageLifecycleStatus.Damaged,
                 PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.Loaded] =
                [PackageLifecycleStatus.InTransit, PackageLifecycleStatus.AtStop, PackageLifecycleStatus.Delivered,
                 PackageLifecycleStatus.PartiallyDelivered, PackageLifecycleStatus.DeliveryFailed,
                 PackageLifecycleStatus.Refused, PackageLifecycleStatus.Missing, PackageLifecycleStatus.Damaged,
                 PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.InTransit] =
                [PackageLifecycleStatus.AtStop, PackageLifecycleStatus.Delivered, PackageLifecycleStatus.PartiallyDelivered,
                 PackageLifecycleStatus.DeliveryFailed, PackageLifecycleStatus.Refused, PackageLifecycleStatus.Missing,
                 PackageLifecycleStatus.Damaged],
            [PackageLifecycleStatus.AtStop] =
                [PackageLifecycleStatus.Delivered, PackageLifecycleStatus.PartiallyDelivered,
                 PackageLifecycleStatus.DeliveryFailed, PackageLifecycleStatus.Refused, PackageLifecycleStatus.Damaged,
                 PackageLifecycleStatus.Missing, PackageLifecycleStatus.InTransit],
            [PackageLifecycleStatus.Delivered] = [],
            [PackageLifecycleStatus.PartiallyDelivered] =
                [PackageLifecycleStatus.Delivered, PackageLifecycleStatus.ReturnPending,
                 PackageLifecycleStatus.RedeliveryPlanned, PackageLifecycleStatus.Quarantined],
            // Wave 4 §3: ReturnedToDepot appended — a trip-less return check-in registers the
            // physical arrival at the depot without a return trip ever being planned.
            [PackageLifecycleStatus.DeliveryFailed] =
                [PackageLifecycleStatus.ReturnPending, PackageLifecycleStatus.RedeliveryPlanned,
                 PackageLifecycleStatus.Quarantined, PackageLifecycleStatus.ReturnLoaded,
                 PackageLifecycleStatus.ReturnedToDepot],
            [PackageLifecycleStatus.Refused] =
                [PackageLifecycleStatus.ReturnPending, PackageLifecycleStatus.RedeliveryPlanned,
                 PackageLifecycleStatus.Quarantined, PackageLifecycleStatus.ReturnLoaded,
                 PackageLifecycleStatus.ReturnedToDepot],
            [PackageLifecycleStatus.Missing] =
                [PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded,
                 PackageLifecycleStatus.Cancelled, PackageLifecycleStatus.ReturnPending],
            [PackageLifecycleStatus.Damaged] =
                [PackageLifecycleStatus.AwaitingLoading, PackageLifecycleStatus.Loaded,
                 PackageLifecycleStatus.Delivered, PackageLifecycleStatus.ReturnPending,
                 PackageLifecycleStatus.Quarantined, PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.Cancelled] = [],
            [PackageLifecycleStatus.ReturnPending] =
                [PackageLifecycleStatus.ReturnLoaded, PackageLifecycleStatus.RedeliveryPlanned,
                 PackageLifecycleStatus.Quarantined, PackageLifecycleStatus.Cancelled,
                 PackageLifecycleStatus.ReturnedToDepot],
            [PackageLifecycleStatus.ReturnLoaded] =
                [PackageLifecycleStatus.ReturnedToDepot, PackageLifecycleStatus.ReturnedToSender],
            [PackageLifecycleStatus.ReturnedToDepot] =
                [PackageLifecycleStatus.RedeliveryPlanned, PackageLifecycleStatus.ReturnedToSender,
                 PackageLifecycleStatus.Quarantined, PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.ReturnedToSender] = [],
            [PackageLifecycleStatus.RedeliveryPlanned] =
                [PackageLifecycleStatus.Loaded, PackageLifecycleStatus.Cancelled],
            [PackageLifecycleStatus.Quarantined] =
                [PackageLifecycleStatus.ReturnPending, PackageLifecycleStatus.RedeliveryPlanned,
                 PackageLifecycleStatus.Cancelled],
        };

    public static bool IsAllowed(PackageLifecycleStatus from, PackageLifecycleStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<PackageLifecycleStatus> AllowedTargets(PackageLifecycleStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Fully terminal: no further custody movement is possible.</summary>
    public static bool IsTerminal(PackageLifecycleStatus status) =>
        status is PackageLifecycleStatus.Delivered
            or PackageLifecycleStatus.ReturnedToSender
            or PackageLifecycleStatus.Cancelled;

    /// <summary>States that still count toward "expected on the vehicle" during loading.</summary>
    public static bool IsPreTransit(PackageLifecycleStatus status) =>
        status is PackageLifecycleStatus.Created
            or PackageLifecycleStatus.Labelled
            or PackageLifecycleStatus.AwaitingLoading;
}
