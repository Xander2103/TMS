using System.Text;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;

namespace TransportationService.Api.Modules.Portal.Services;

/// <summary>
/// Renders a personal schedule as a standards-compliant iCalendar document (RFC 5545) —
/// the first, integration-free step towards Google/Outlook synchronisation: users import
/// or subscribe with any calendar client. UIDs are stable per source entity, so a
/// re-import updates instead of duplicating.
/// </summary>
public static class PlanningIcsBuilder
{
    public static string Build(IReadOnlyList<ScheduleDayDto> days)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BEGIN:VCALENDAR");
        builder.AppendLine("VERSION:2.0");
        builder.AppendLine("PRODID:-//TransportationService//Planning//NL");
        builder.AppendLine("CALSCALE:GREGORIAN");

        foreach (var day in days)
        {
            foreach (var entry in day.Entries)
            {
                var uidSource = entry.TripId ?? entry.ShiftId ?? entry.AbsenceId;
                if (uidSource is null)
                {
                    continue;
                }

                builder.AppendLine("BEGIN:VEVENT");
                builder.AppendLine($"UID:{uidSource}-{day.Date:yyyyMMdd}@transportationservice");
                if (entry.StartTime is { } start && entry.EndTime is { } end)
                {
                    builder.AppendLine($"DTSTART:{day.Date:yyyyMMdd}T{start:HHmmss}");
                    builder.AppendLine($"DTEND:{day.Date:yyyyMMdd}T{end:HHmmss}");
                }
                else
                {
                    // All-day (absences, unscheduled assignments).
                    builder.AppendLine($"DTSTART;VALUE=DATE:{day.Date:yyyyMMdd}");
                    builder.AppendLine($"DTEND;VALUE=DATE:{day.Date.AddDays(1):yyyyMMdd}");
                }

                var summary = entry.VehicleSummary is { Length: > 0 } vehicle
                    ? $"{entry.Label} ({vehicle})"
                    : entry.Label;
                builder.AppendLine($"SUMMARY:{Escape(summary)}");
                if (entry.WorkLocation is { Length: > 0 } location)
                {
                    builder.AppendLine($"LOCATION:{Escape(location)}");
                }

                if (entry.StatusLabel is { Length: > 0 } status)
                {
                    builder.AppendLine($"DESCRIPTION:{Escape(status)}");
                }

                builder.AppendLine("END:VEVENT");
            }
        }

        builder.AppendLine("END:VCALENDAR");
        return builder.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
