using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransportationService.Api.Data;

namespace TransportationService.Api.Common.Jobs;

/// <summary>
/// H7: cross-process mutual exclusion for background jobs and the startup sync. A job that cannot
/// get its lock is SKIPPED for this run (another API instance is doing it). Process-local locks
/// are not enough: the lock must live in the shared database so a second instance (scale-out,
/// blue/green overlap, a second systemd unit) sees it.
/// </summary>
public interface IJobLock
{
    /// <summary>Non-blocking: a held handle, or null when another process holds the lock.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);

    /// <summary>Waits (polling) up to <paramref name="timeout"/>; null when it could not be taken.</summary>
    Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL session-level advisory lock (<c>pg_try_advisory_lock(bigint)</c>) held on a
/// DEDICATED, non-pooled connection for the duration of the run, so it can never leak onto a
/// pooled connection that serves requests. Disposing unlocks and closes the connection; if the
/// process dies, PostgreSQL releases the lock with the session. On a non-Npgsql provider (the
/// SQLite test suite) the lock degrades to a no-op that always succeeds.
/// </summary>
public sealed class DatabaseJobLock : IJobLock
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseJobLock(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Stable 64-bit lock key from the job name (SHA-256 prefix; not process-randomised
    /// like string.GetHashCode, so every instance and release agrees on it).</summary>
    public static long KeyFor(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("tms-job:" + name));
        return BitConverter.ToInt64(hash, 0);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        string? connectionString;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();
            if (!db.Database.IsNpgsql())
            {
                return NoOpJobLock.Handle;
            }

            connectionString = db.Database.GetConnectionString();
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = "tms-job-lock:" + name,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", KeyFor(name));
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new AdvisoryLockHandle(connection, KeyFor(name));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var handle = await TryAcquireAsync(name, cancellationToken);
            if (handle is not null || DateTime.UtcNow >= deadline)
            {
                return handle;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private sealed class AdvisoryLockHandle(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            catch
            {
                // Closing the session below releases the lock as well.
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}

/// <summary>Always-granted lock for hosts without a shared database (tests).</summary>
public sealed class NoOpJobLock : IJobLock
{
    internal static readonly IAsyncDisposable Handle = new NoOpHandle();

    public Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable?>(Handle);

    public Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable?>(Handle);

    private sealed class NoOpHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
