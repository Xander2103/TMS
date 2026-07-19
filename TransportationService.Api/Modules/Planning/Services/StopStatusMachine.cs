using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// The controlled stop-status model. Skipped is only reachable before arrival (a visited stop
/// fails or completes partially instead), handling states must match the stop type, and from
/// the pre-arrival states a handling/completion target implicitly records the Arrived step
/// (the "arrival bridge") so one-tap driver actions still yield a complete, explainable history.
/// </summary>
public static class StopStatusMachine
{
    private static readonly IReadOnlyDictionary<StopExecutionStatus, StopExecutionStatus[]> Explicit =
        new Dictionary<StopExecutionStatus, StopExecutionStatus[]>
        {
            [StopExecutionStatus.Planned] = [StopExecutionStatus.EnRoute, StopExecutionStatus.Arrived, StopExecutionStatus.Skipped],
            [StopExecutionStatus.EnRoute] = [StopExecutionStatus.Arrived, StopExecutionStatus.Skipped, StopExecutionStatus.Failed],
            [StopExecutionStatus.Arrived] =
            [
                StopExecutionStatus.Loading, StopExecutionStatus.Loaded, StopExecutionStatus.Unloading,
                StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted, StopExecutionStatus.Failed,
            ],
            [StopExecutionStatus.Loading] = [StopExecutionStatus.Loaded, StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted, StopExecutionStatus.Failed],
            [StopExecutionStatus.Loaded] = [StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted, StopExecutionStatus.Failed],
            [StopExecutionStatus.Unloading] = [StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted, StopExecutionStatus.Failed],
            [StopExecutionStatus.Completed] = [],
            [StopExecutionStatus.PartiallyCompleted] = [],
            [StopExecutionStatus.Failed] = [],
            [StopExecutionStatus.Skipped] = [],
        };

    /// <summary>Targets reachable from the pre-arrival states through the implicit arrival step.
    /// Failed is deliberately absent: a stop that was never reached is skipped, not failed.</summary>
    private static readonly StopExecutionStatus[] BridgeTargets =
    [
        StopExecutionStatus.Loading, StopExecutionStatus.Loaded, StopExecutionStatus.Unloading,
        StopExecutionStatus.Completed, StopExecutionStatus.PartiallyCompleted,
    ];

    public static bool IsTerminal(StopExecutionStatus status) =>
        status is StopExecutionStatus.Completed or StopExecutionStatus.PartiallyCompleted
            or StopExecutionStatus.Failed or StopExecutionStatus.Skipped;

    /// <summary>Statuses whose transition demands a reason regardless of timing.</summary>
    public static bool RequiresReason(StopExecutionStatus target) =>
        target is StopExecutionStatus.Skipped or StopExecutionStatus.Failed or StopExecutionStatus.PartiallyCompleted;

    private static bool MatchesStopType(StopExecutionStatus target, StopType stopType) => target switch
    {
        StopExecutionStatus.Loading or StopExecutionStatus.Loaded => stopType == StopType.Loading,
        StopExecutionStatus.Unloading => stopType == StopType.Unloading,
        _ => true,
    };

    /// <summary>
    /// Whether <paramref name="to"/> is reachable from <paramref name="from"/> for this stop type.
    /// <paramref name="viaArrivalBridge"/> is true when the move implicitly records the Arrived step.
    /// </summary>
    public static bool IsAllowed(StopExecutionStatus from, StopExecutionStatus to, StopType stopType, out bool viaArrivalBridge)
    {
        viaArrivalBridge = false;
        if (!MatchesStopType(to, stopType))
        {
            return false;
        }

        if (Explicit[from].Contains(to))
        {
            return true;
        }

        if (from is StopExecutionStatus.Planned or StopExecutionStatus.EnRoute && BridgeTargets.Contains(to))
        {
            viaArrivalBridge = true;
            return true;
        }

        return false;
    }

    /// <summary>All reachable targets (explicit + bridged), in enum declaration order.</summary>
    public static IReadOnlyList<StopExecutionStatus> AllowedTargets(StopExecutionStatus from, StopType stopType)
    {
        var targets = Explicit[from].AsEnumerable();
        if (from is StopExecutionStatus.Planned or StopExecutionStatus.EnRoute)
        {
            targets = targets.Concat(BridgeTargets);
        }

        return targets
            .Where(t => MatchesStopType(t, stopType))
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>Whether recording <paramref name="to"/> counts as the moment of arrival.</summary>
    public static bool RecordsArrival(StopExecutionStatus to, bool viaArrivalBridge) =>
        to == StopExecutionStatus.Arrived || viaArrivalBridge;
}
