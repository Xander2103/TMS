using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Counts the SQL statements a context sends (reader, scalar and non-query, sync and async), so
/// a test can pin a read model to a FIXED number of round trips instead of one per row (the
/// planning-board / trip-list N+1 found in the performance audit, PERF-BLOCKER 3).
/// Pass it as an extra interceptor to <see cref="SqliteTestDbContext"/>.
/// </summary>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    private int _count;

    /// <summary>Statements executed since construction or the last <see cref="Reset"/>.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Command texts in execution order — handy when a bound is exceeded.</summary>
    public List<string> Statements { get; } = [];

    public void Reset()
    {
        Volatile.Write(ref _count, 0);
        lock (Statements)
        {
            Statements.Clear();
        }
    }

    /// <summary>Runs <paramref name="action"/> and returns the number of statements it issued.</summary>
    public async Task<int> MeasureAsync(Func<Task> action)
    {
        var before = Count;
        await action();
        return Count - before;
    }

    private void Record(DbCommand command)
    {
        Interlocked.Increment(ref _count);
        lock (Statements)
        {
            Statements.Add(command.CommandText);
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
