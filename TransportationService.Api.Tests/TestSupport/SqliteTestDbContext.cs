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
    private readonly DbContextOptions<TransportationDbContext> _options;

    /// <param name="ambientTenantId">When set, the context behaves like a request-scoped context
    /// of that tenant: the global tenant query filter (H1) is ACTIVE. Default (null) mimics
    /// system/background scope — filter open, exactly like production seeders and dispatchers.</param>
    public SqliteTestDbContext(Guid? ambientTenantId = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var interceptor = new AuditingSaveChangesInterceptor(new HttpContextAccessor(), TimeProvider.System);
        var statusHistoryInterceptor = new TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor(
            new HttpContextAccessor(), TimeProvider.System);

        _options = new DbContextOptionsBuilder<TransportationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor, statusHistoryInterceptor)
            .Options;

        Context = new TransportationDbContext(_options, new FixedTenantQueryFilterAccessor(ambientTenantId));
        Context.Database.EnsureCreated();
    }

    /// <summary>Second context over the SAME database, scoped to <paramref name="tenantId"/> —
    /// what a request-scoped context of that tenant sees through the H1 filter. Caller disposes.</summary>
    public TransportationDbContext CreateContextForTenant(Guid tenantId) =>
        new(_options, new FixedTenantQueryFilterAccessor(tenantId));

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
