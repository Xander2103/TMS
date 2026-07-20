using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// In-memory SQLite context wired with the real <see cref="AuditingSaveChangesInterceptor"/> so
/// tests exercise the production audit-stamp and soft-delete behaviour. No HTTP context is
/// present, so the audited user id resolves to null (as it does during seeding).
/// </summary>
public sealed class SqliteTestDbContext : IDisposable
{
    public TransportationDbContext Context { get; }
    private readonly SqliteConnection _connection;

    public SqliteTestDbContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var interceptor = new AuditingSaveChangesInterceptor(new HttpContextAccessor(), TimeProvider.System);
        var statusHistoryInterceptor = new TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor(
            new HttpContextAccessor(), TimeProvider.System);

        var options = new DbContextOptionsBuilder<TransportationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor, statusHistoryInterceptor)
            .Options;

        Context = new TransportationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
