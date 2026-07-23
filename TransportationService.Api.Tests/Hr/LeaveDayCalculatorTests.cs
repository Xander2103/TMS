using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using Xunit;

namespace TransportationService.Api.Tests.Hr;

public class LeaveDayCalculatorTests
{
    [Fact]
    public void FullDay_SingleDay_CountsOne()
    {
        var d = new DateOnly(2027, 3, 10);
        Assert.Equal(1m, LeaveDayCalculator.CountDaysInYear(d, d, AbsencePartDay.FullDay, 2027));
    }

    [Fact]
    public void HalfDay_SingleDay_CountsHalf()
    {
        var d = new DateOnly(2027, 3, 10);
        Assert.Equal(0.5m, LeaveDayCalculator.CountDaysInYear(d, d, AbsencePartDay.Morning, 2027));
        Assert.Equal(0.5m, LeaveDayCalculator.CountDaysInYear(d, d, AbsencePartDay.Afternoon, 2027));
    }

    [Fact]
    public void MultiDay_CountsInclusiveCalendarDays()
    {
        Assert.Equal(5m, LeaveDayCalculator.CountDaysInYear(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 5), AbsencePartDay.FullDay, 2027));
    }

    [Fact]
    public void Range_IsClippedToTheYear()
    {
        // 30 Dec 2026 .. 2 Jan 2027 → only 1 & 2 Jan fall in 2027.
        Assert.Equal(2m, LeaveDayCalculator.CountDaysInYear(new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2), AbsencePartDay.FullDay, 2027));
        // ...and 30 & 31 Dec fall in 2026.
        Assert.Equal(2m, LeaveDayCalculator.CountDaysInYear(new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2), AbsencePartDay.FullDay, 2026));
    }

    [Fact]
    public void SingleDay_OutsideYear_CountsZero()
    {
        var d = new DateOnly(2026, 5, 1);
        Assert.Equal(0m, LeaveDayCalculator.CountDaysInYear(d, d, AbsencePartDay.FullDay, 2027));
    }
}
