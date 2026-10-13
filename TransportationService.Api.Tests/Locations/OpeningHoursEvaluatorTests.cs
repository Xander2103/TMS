using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;

namespace TransportationService.Api.Tests.Locations;

/// <summary>Pure unit tests — the evaluator never touches the database.</summary>
public class OpeningHoursEvaluatorTests
{
    private readonly OpeningHoursEvaluator _sut = new();

    private static LocationOpeningInterval Interval(int isoDay, string from, string to) => new()
    {
        Id = Guid.NewGuid(),
        DayOfWeek = isoDay,
        FromTime = TimeOnly.Parse(from),
        ToTime = TimeOnly.Parse(to),
    };

    /// <summary>Monday morning + afternoon block, Tuesday continuous; weekend closed.</summary>
    private static IReadOnlyList<LocationOpeningInterval> WeekSchedule() =>
    [
        Interval(1, "07:00", "12:00"),
        Interval(1, "13:00", "17:00"),
        Interval(2, "07:00", "17:00"),
    ];

    [Fact]
    public void NoIntervalsAtAll_ReportsNoData()
    {
        var check = _sut.Check([], DayOfWeek.Monday, new TimeOnly(9, 0));

        Assert.Equal(OpeningHoursStatus.NoData, check.Status);
        Assert.Empty(check.DayIntervals);
    }

    [Fact]
    public void DayWithoutIntervals_ReportsClosedDay()
    {
        var check = _sut.Check(WeekSchedule(), DayOfWeek.Saturday, new TimeOnly(9, 0));

        Assert.Equal(OpeningHoursStatus.ClosedDay, check.Status);
        Assert.Empty(check.DayIntervals);
    }

    [Fact]
    public void SundayMapsToIsoDaySeven()
    {
        IReadOnlyList<LocationOpeningInterval> sundayOnly = [Interval(7, "09:00", "12:00")];

        var check = _sut.Check(sundayOnly, DayOfWeek.Sunday, new TimeOnly(10, 0));

        Assert.Equal(OpeningHoursStatus.Inside, check.Status);
    }

    [Fact]
    public void TimeInsideAnInterval_ReportsInside_StartInclusive()
    {
        Assert.Equal(OpeningHoursStatus.Inside, _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(9, 30)).Status);
        // Start is inclusive, end is exclusive (at 17:00 the site is closing).
        Assert.Equal(OpeningHoursStatus.Inside, _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(7, 0)).Status);
        Assert.Equal(OpeningHoursStatus.AfterClosing, _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(17, 0)).Status);
    }

    [Fact]
    public void BeforeTheFirstInterval_ReportsBeforeOpening()
    {
        var check = _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(6, 15));

        Assert.Equal(OpeningHoursStatus.BeforeOpening, check.Status);
        // The day's intervals ride along, sorted by start time, for warning text.
        Assert.Equal(2, check.DayIntervals.Count);
        Assert.Equal(new TimeOnly(7, 0), check.DayIntervals[0].FromTime);
    }

    [Fact]
    public void TimeInTheLunchGap_ReportsBeforeOpeningOfTheNextInterval()
    {
        // 12:30 sits between the morning and afternoon blocks: the caller is EARLY for the
        // 13:00 block, not late for the morning one.
        var check = _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(12, 30));

        Assert.Equal(OpeningHoursStatus.BeforeOpening, check.Status);
    }

    [Fact]
    public void AfterTheLastInterval_ReportsAfterClosing()
    {
        var check = _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(18, 0));

        Assert.Equal(OpeningHoursStatus.AfterClosing, check.Status);
    }

    [Fact]
    public void MultipleIntervals_InsideTheSecondBlock_ReportsInside()
    {
        var check = _sut.Check(WeekSchedule(), DayOfWeek.Monday, new TimeOnly(14, 0));

        Assert.Equal(OpeningHoursStatus.Inside, check.Status);
        Assert.Equal(2, check.DayIntervals.Count);
    }
}
