using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// Master sprint 2026-09-21 (D2): a site stop (on-site work) is executed per stop id like any
/// other stop, but the driver never "loads" or "unloads" there.
/// </summary>
public class SiteStopStatusMachineTests
{
    [Fact]
    public void SiteStop_HasNoLoadingOrUnloadingStates_ButCompletesNormally()
    {
        var fromArrived = StopStatusMachine.AllowedTargets(StopExecutionStatus.Arrived, StopType.Site);

        Assert.DoesNotContain(StopExecutionStatus.Loading, fromArrived);
        Assert.DoesNotContain(StopExecutionStatus.Loaded, fromArrived);
        Assert.DoesNotContain(StopExecutionStatus.Unloading, fromArrived);
        Assert.Contains(StopExecutionStatus.Completed, fromArrived);
        Assert.Contains(StopExecutionStatus.PartiallyCompleted, fromArrived);
        Assert.Contains(StopExecutionStatus.Failed, fromArrived);

        Assert.True(StopStatusMachine.IsAllowed(StopExecutionStatus.Planned, StopExecutionStatus.EnRoute, StopType.Site, out _));
        Assert.True(StopStatusMachine.IsAllowed(StopExecutionStatus.EnRoute, StopExecutionStatus.Arrived, StopType.Site, out _));
        // One-tap completion still bridges the arrival, exactly like a loading/unloading stop.
        Assert.True(StopStatusMachine.IsAllowed(StopExecutionStatus.Planned, StopExecutionStatus.Completed, StopType.Site, out var bridged));
        Assert.True(bridged);
        Assert.False(StopStatusMachine.IsAllowed(StopExecutionStatus.Arrived, StopExecutionStatus.Unloading, StopType.Site, out _));
    }

    [Fact]
    public void LoadingAndUnloadingStops_KeepTheirHandlingStates()
    {
        Assert.Contains(StopExecutionStatus.Loading, StopStatusMachine.AllowedTargets(StopExecutionStatus.Arrived, StopType.Loading));
        Assert.Contains(StopExecutionStatus.Unloading, StopStatusMachine.AllowedTargets(StopExecutionStatus.Arrived, StopType.Unloading));
    }
}
