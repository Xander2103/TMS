using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Tests.TestSupport;

public sealed class SqliteTestDbContext : IDisposable
{
    public TransportationDbContext Context { get; }
    private readonly SqliteConnection _connection;

    public SqliteTestDbContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransportationDbContext>()
            .UseSqlite(_connection)
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
