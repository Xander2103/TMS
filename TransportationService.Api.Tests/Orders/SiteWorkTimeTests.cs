using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Master sprint 2026-09-21 (D2): the pure end-time rule of on-site work — end = start + the
/// activity's planned duration, for non-manual SITE stops only.
/// </summary>
public class SiteWorkTimeTests
{
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, DateTimeKind.Utc);

    private static TransportOrderStopInput Stop(StopType type, DateTime? from, DateTime? to, bool manual = false) =>
        new(type, null, "Werf", "Kaai 12", "9000", "Gent", "BE", from, to, null, null, PlannedToIsManual: manual);

    [Theory]
    [InlineData(8, 0, 4.0, 22, 12, 0)]   // 08:00 + 4 h = 12:00
    [InlineData(8, 30, 1.5, 22, 10, 0)]  // 08:30 + 1.5 h = 10:00
    [InlineData(22, 0, 4.0, 23, 2, 0)]   // 22:00 + 4 h = 02:00 the NEXT day
    [InlineData(9, 0, 0.25, 22, 9, 15)]  // a quarter of an hour is 15 minutes
    public void End_IsStartPlusDuration(int hour, int minute, double hours, int expectedDay, int expectedHour, int expectedMinute)
    {
        var end = SiteWorkTime.ResolvePlannedTo(
            StopType.Site, At(22, hour, minute), plannedTo: null, plannedToIsManual: false, durationHours: (decimal)hours);

        Assert.Equal(At(expectedDay, expectedHour, expectedMinute), end);
        Assert.Equal(DateTimeKind.Utc, end!.Value.Kind);
    }

    [Fact]
    public void ManualEnd_SurvivesAStartChange()
    {
        var manualEnd = At(22, 17);

        var end = SiteWorkTime.ResolvePlannedTo(StopType.Site, At(22, 10), manualEnd, plannedToIsManual: true, durationHours: 4m);

        Assert.Equal(manualEnd, end);
    }

    [Fact]
    public void NonManualEnd_FollowsAStartChange_AndADurationChange()
    {
        // Stored: 08:00 → 12:00 (4 h). The planner moves the start to 10:00.
        var afterStartChange = SiteWorkTime.ResolvePlannedTo(StopType.Site, At(22, 10), At(22, 12), false, 4m);
        Assert.Equal(At(22, 14), afterStartChange);

        // Then the duration goes from 4 h to 6 h.
        var afterDurationChange = SiteWorkTime.ResolvePlannedTo(StopType.Site, At(22, 10), afterStartChange, false, 6m);
        Assert.Equal(At(22, 16), afterDurationChange);
    }

    [Fact]
    public void LoadingAndUnloadingStops_AreNeverTouched()
    {
        var plannedTo = At(22, 9);

        Assert.Equal(plannedTo, SiteWorkTime.ResolvePlannedTo(StopType.Loading, At(22, 8), plannedTo, false, 4m));
        Assert.Equal(plannedTo, SiteWorkTime.ResolvePlannedTo(StopType.Unloading, At(22, 8), plannedTo, false, 4m));
        Assert.Null(SiteWorkTime.ResolvePlannedTo(StopType.Unloading, At(22, 8), null, false, 4m));
    }

    [Fact]
    public void UnknownOrNegativeDuration_OrMissingStart_KeepsTheGivenEnd()
    {
        var plannedTo = At(22, 15);

        Assert.Equal(plannedTo, SiteWorkTime.ResolvePlannedTo(StopType.Site, At(22, 8), plannedTo, false, durationHours: null));
        Assert.Equal(plannedTo, SiteWorkTime.ResolvePlannedTo(StopType.Site, At(22, 8), plannedTo, false, durationHours: -1m));
        Assert.Equal(plannedTo, SiteWorkTime.ResolvePlannedTo(StopType.Site, plannedFrom: null, plannedTo, false, 4m));
    }

    [Fact]
    public void Normalize_ResolvesSiteStops_ClearsTheFlagElsewhere_AndLeavesCustomerWindowsAlone()
    {
        var site = Stop(StopType.Site, At(22, 22), null) with
        {
            RequestedFrom = At(22, 7), RequestedTo = At(22, 9),
            TimeRequirement = StopTimeRequirementKind.Before, TimeRequirementTo = new TimeOnly(10, 0),
        };
        var loading = Stop(StopType.Loading, At(22, 8), At(22, 9), manual: true);

        var normalized = SiteWorkTime.Normalize([site, loading], 4m);

        Assert.Equal(At(23, 2), normalized[0].PlannedTo);
        Assert.Equal(At(22, 7), normalized[0].RequestedFrom);
        Assert.Equal(At(22, 9), normalized[0].RequestedTo);
        Assert.Equal(new TimeOnly(10, 0), normalized[0].TimeRequirementTo);
        Assert.Equal(At(22, 9), normalized[1].PlannedTo);
        Assert.False(normalized[1].PlannedToIsManual);
    }

    [Fact]
    public void Normalize_WithNothingToChange_ReturnsTheSameInstance()
    {
        IReadOnlyList<TransportOrderStopInput> stops =
            [Stop(StopType.Loading, At(22, 8), At(22, 9)), Stop(StopType.Unloading, At(22, 13), null)];

        Assert.Same(stops, SiteWorkTime.Normalize(stops, 4m));
    }

    [Fact]
    public void Recompute_SkipsManualAndDeletedStops()
    {
        var follows = new TransportOrderStop { StopType = StopType.Site, PlannedFrom = At(22, 8), PlannedTo = At(22, 12) };
        var manual = new TransportOrderStop { StopType = StopType.Site, PlannedFrom = At(22, 8), PlannedTo = At(22, 17), PlannedToIsManual = true };
        var deleted = new TransportOrderStop { StopType = StopType.Site, PlannedFrom = At(22, 8), PlannedTo = At(22, 12), IsDeleted = true };

        var changed = SiteWorkTime.Recompute([follows, manual, deleted], 6m);

        Assert.True(changed);
        Assert.Equal(At(22, 14), follows.PlannedTo);
        Assert.Equal(At(22, 17), manual.PlannedTo);
        Assert.Equal(At(22, 12), deleted.PlannedTo);
        Assert.False(SiteWorkTime.Recompute([follows, manual], 6m));
    }
}
