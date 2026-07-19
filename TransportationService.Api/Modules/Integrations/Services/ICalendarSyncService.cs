namespace TransportationService.Api.Modules.Integrations.Services;

/// <summary>One calendar-relevant fact the TMS wants mirrored to an external calendar later.</summary>
public record CalendarSyncEvent(
    string EventType,
    Guid EntityId,
    Guid EmployeeId,
    DateOnly Start,
    DateOnly End,
    string Title);

/// <summary>
/// Integration boundary for Outlook/Exchange (and other calendars). The TMS stays the source
/// of truth; callers only announce facts. Wave 12 supplies the queued implementation with
/// external event ids, retries and idempotency — until then the no-op keeps the seam honest.
/// </summary>
public interface ICalendarSyncService
{
    Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken);
}

/// <summary>Development placeholder: accepts every event and intentionally does nothing.</summary>
public sealed class NoOpCalendarSyncService : ICalendarSyncService
{
    public Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
