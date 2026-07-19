using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Integrations.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Integrations.Services;

public record CalendarSyncItemDto(
    Guid Id,
    string EventType,
    Guid EntityId,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Title,
    CalendarSyncOperation Operation,
    CalendarSyncStatus Status,
    string? ExternalEventId,
    int AttemptCount,
    DateTime? NextAttemptAt,
    DateTime? LastSyncAt,
    string? ErrorDetail,
    DateTime CreatedAt);

/// <summary>
/// Provider seam towards a real calendar (Microsoft Graph/Exchange later). Upserts return the
/// external event id; idempotency rides on that id. No real credentials in this milestone.
/// </summary>
public interface ICalendarProvider
{
    Task<string> UpsertEventAsync(CalendarSyncItem item, CancellationToken cancellationToken);

    Task CancelEventAsync(string externalEventId, CancellationToken cancellationToken);
}

/// <summary>Development fake: an in-memory calendar handing out stable fake event ids.</summary>
public sealed class FakeCalendarProvider : ICalendarProvider
{
    private int _counter;

    public List<CalendarSyncItem> Upserts { get; } = [];
    public List<string> Cancellations { get; } = [];
    public bool Fail { get; set; }

    public Task<string> UpsertEventAsync(CalendarSyncItem item, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("calendar provider unavailable");
        }

        Upserts.Add(item);
        return Task.FromResult(item.ExternalEventId ?? $"fake-evt-{Interlocked.Increment(ref _counter):D6}");
    }

    public Task CancelEventAsync(string externalEventId, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("calendar provider unavailable");
        }

        Cancellations.Add(externalEventId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real ICalendarSyncService: producers announce facts, this service keeps exactly one
/// queue row per (event type, entity) and the processor mirrors them to the provider.
/// </summary>
public class QueueingCalendarSyncService : ICalendarSyncService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public QueueingCalendarSyncService(
        TransportationDbContext dbContext, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task QueueAsync(CalendarSyncEvent syncEvent, CancellationToken cancellationToken)
    {
        var item = await _dbContext.CalendarSyncItems
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId
                                      && i.EventType == syncEvent.EventType
                                      && i.EntityId == syncEvent.EntityId, cancellationToken);
        if (item is null)
        {
            item = new CalendarSyncItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                EventType = syncEvent.EventType,
                EntityId = syncEvent.EntityId,
            };
            _dbContext.Add(item);
        }

        item.EmployeeId = syncEvent.EmployeeId;
        item.StartDate = syncEvent.Start;
        item.EndDate = syncEvent.End;
        item.Title = syncEvent.Title;
        item.Operation = CalendarSyncOperation.Upsert;
        item.Status = CalendarSyncStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptAt = null;
        item.ErrorDetail = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(string eventType, Guid entityId, CancellationToken cancellationToken)
    {
        var item = await _dbContext.CalendarSyncItems
            .FirstOrDefaultAsync(i => i.TenantId == _tenantContext.TenantId
                                      && i.EventType == eventType && i.EntityId == entityId, cancellationToken);
        if (item is null)
        {
            return; // never mirrored; nothing to cancel
        }

        if (item.ExternalEventId is null)
        {
            // Not yet synced: cancelling locally suffices.
            item.Status = CalendarSyncStatus.Cancelled;
        }
        else
        {
            item.Operation = CalendarSyncOperation.Cancel;
            item.Status = CalendarSyncStatus.Pending;
            item.AttemptCount = 0;
            item.NextAttemptAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<CalendarSyncItemDto>> ListAsync(
        CalendarSyncStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.CalendarSyncItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId);
        if (status is { } wanted)
        {
            query = query.Where(i => i.Status == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(_dbContext.Employees.AsNoTracking(),
                i => i.EmployeeId, e => e.Id,
                (i, e) => new CalendarSyncItemDto(
                    i.Id, i.EventType, i.EntityId, i.EmployeeId,
                    e.FirstName + " " + e.LastName,
                    i.StartDate, i.EndDate, i.Title, i.Operation, i.Status,
                    i.ExternalEventId, i.AttemptCount, i.NextAttemptAt, i.LastSyncAt,
                    i.ErrorDetail, i.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CalendarSyncItemDto>(items, total, page, pageSize);
    }

    /// <summary>Puts a failed item back in the queue with a fresh attempt budget.</summary>
    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.CalendarSyncItems
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == _tenantContext.TenantId, cancellationToken);
        if (item is null || item.Status != CalendarSyncStatus.Failed)
        {
            return false;
        }

        item.Status = CalendarSyncStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptAt = null;
        item.ErrorDetail = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>Drains pending sync items against the provider with backoff; tenant-agnostic.</summary>
public class CalendarSyncProcessor
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(10);

    private readonly TransportationDbContext _dbContext;
    private readonly ICalendarProvider _provider;
    private readonly TimeProvider _timeProvider;

    public CalendarSyncProcessor(TransportationDbContext dbContext, ICalendarProvider provider, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _provider = provider;
        _timeProvider = timeProvider;
    }

    public async Task<int> ProcessPendingAsync(int max, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var due = await _dbContext.CalendarSyncItems
            .Where(i => i.Status == CalendarSyncStatus.Pending && (i.NextAttemptAt == null || i.NextAttemptAt <= now))
            .OrderBy(i => i.CreatedAt)
            .Take(Math.Clamp(max, 1, 100))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var item in due)
        {
            item.AttemptCount += 1;
            try
            {
                if (item.Operation == CalendarSyncOperation.Cancel)
                {
                    if (item.ExternalEventId is { } externalId)
                    {
                        await _provider.CancelEventAsync(externalId, cancellationToken);
                    }

                    item.Status = CalendarSyncStatus.Cancelled;
                }
                else
                {
                    item.ExternalEventId = await _provider.UpsertEventAsync(item, cancellationToken);
                    item.Status = CalendarSyncStatus.Synced;
                }

                item.LastSyncAt = now;
                item.ErrorDetail = null;
                processed += 1;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.ErrorDetail = exception.Message;
                if (item.AttemptCount >= MaxAttempts)
                {
                    item.Status = CalendarSyncStatus.Failed;
                }
                else
                {
                    item.NextAttemptAt = now + BaseBackoff * Math.Pow(2, item.AttemptCount - 1);
                }
            }
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return processed;
    }
}
