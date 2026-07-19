using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;

namespace TransportationService.Api.Tests.Packages;

public class PackageLifecycleMachineTests
{
    [Theory]
    [InlineData(PackageLifecycleStatus.Created, PackageLifecycleStatus.Loaded)]
    [InlineData(PackageLifecycleStatus.Labelled, PackageLifecycleStatus.Loaded)]
    [InlineData(PackageLifecycleStatus.Loaded, PackageLifecycleStatus.InTransit)]
    [InlineData(PackageLifecycleStatus.InTransit, PackageLifecycleStatus.Delivered)]
    [InlineData(PackageLifecycleStatus.Loaded, PackageLifecycleStatus.Missing)]
    [InlineData(PackageLifecycleStatus.InTransit, PackageLifecycleStatus.Damaged)]
    [InlineData(PackageLifecycleStatus.AtStop, PackageLifecycleStatus.Refused)]
    [InlineData(PackageLifecycleStatus.DeliveryFailed, PackageLifecycleStatus.ReturnPending)]
    [InlineData(PackageLifecycleStatus.ReturnLoaded, PackageLifecycleStatus.ReturnedToDepot)]
    [InlineData(PackageLifecycleStatus.Missing, PackageLifecycleStatus.Loaded)] // controlled "found" resolution
    public void AllowedTransitions_AreAllowed(PackageLifecycleStatus from, PackageLifecycleStatus to) =>
        Assert.True(PackageLifecycleMachine.IsAllowed(from, to));

    [Theory]
    [InlineData(PackageLifecycleStatus.Delivered, PackageLifecycleStatus.Loaded)]
    [InlineData(PackageLifecycleStatus.Cancelled, PackageLifecycleStatus.Delivered)]
    [InlineData(PackageLifecycleStatus.Cancelled, PackageLifecycleStatus.Loaded)]
    [InlineData(PackageLifecycleStatus.Missing, PackageLifecycleStatus.Delivered)] // must resolve first
    [InlineData(PackageLifecycleStatus.ReturnedToSender, PackageLifecycleStatus.Loaded)]
    [InlineData(PackageLifecycleStatus.Delivered, PackageLifecycleStatus.Cancelled)]
    public void ImpossibleTransitions_HaveNoEdge(PackageLifecycleStatus from, PackageLifecycleStatus to) =>
        Assert.False(PackageLifecycleMachine.IsAllowed(from, to));

    [Fact]
    public void EveryStatus_HasATransitionEntry_AndTerminalsAreClosed()
    {
        foreach (var status in Enum.GetValues<PackageLifecycleStatus>())
        {
            var targets = PackageLifecycleMachine.AllowedTargets(status);
            Assert.NotNull(targets);
            if (PackageLifecycleMachine.IsTerminal(status))
            {
                Assert.Empty(targets);
            }
        }
    }

    [Fact]
    public void MissingCannotReachDelivered_WithoutGoingThroughResolution()
    {
        // Direct edge absent…
        Assert.False(PackageLifecycleMachine.IsAllowed(PackageLifecycleStatus.Missing, PackageLifecycleStatus.Delivered));
        // …but found→Loaded→(transit)→Delivered is the controlled path.
        Assert.True(PackageLifecycleMachine.IsAllowed(PackageLifecycleStatus.Missing, PackageLifecycleStatus.Loaded));
        Assert.True(PackageLifecycleMachine.IsAllowed(PackageLifecycleStatus.Loaded, PackageLifecycleStatus.Delivered));
    }
}
