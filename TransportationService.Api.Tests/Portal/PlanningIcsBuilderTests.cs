using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.Portal.Services;

namespace TransportationService.Api.Tests.Portal;

public class PlanningIcsBuilderTests
{
    [Fact]
    public void Build_RendersTimedAndAllDayEvents_WithStableUids_AndEscapedText()
    {
        var tripId = Guid.NewGuid();
        var absenceId = Guid.NewGuid();
        var days = new List<ScheduleDayDto>
        {
            new(new DateOnly(2026, 7, 21),
            [
                new ScheduleEntryDto(ScheduleEntryState.Trip, null, null, tripId, "Trip",
                    "Rit RIT-0001; Antwerpen", new TimeOnly(8, 0), new TimeOnly(16, 30),
                    null, "Antwerpen, kade 12", "VRT-0001", "Gepland"),
            ]),
            new(new DateOnly(2026, 7, 22),
            [
                new ScheduleEntryDto(ScheduleEntryState.LeaveApproved, null, absenceId, null, "Absence",
                    "Verlof", null, null, null, null, null, null),
            ]),
        };

        var ics = PlanningIcsBuilder.Build(days);

        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains($"UID:{tripId}-20260721@transportationservice", ics);
        Assert.Contains("DTSTART:20260721T080000", ics);
        Assert.Contains("DTEND:20260721T163000", ics);
        Assert.Contains(@"SUMMARY:Rit RIT-0001\; Antwerpen (VRT-0001)", ics);
        Assert.Contains(@"LOCATION:Antwerpen\, kade 12", ics);

        // Absence without times renders as an all-day event.
        Assert.Contains("DTSTART;VALUE=DATE:20260722", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260723", ics);
        Assert.Contains("END:VCALENDAR", ics.TrimEnd());
    }
}
