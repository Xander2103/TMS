using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TransportationService.Api.Data;

/// <summary>
/// Design-time factory used by EF Core tooling.
///
/// This prevents `dotnet ef` from booting the complete API host and running
/// startup seeders/backfills before a fresh database has been migrated.
/// </summary>
public sealed class TransportationDbContextFactory
    : IDesignTimeDbContextFactory<TransportationDbContext>
{
    public TransportationDbContext CreateDbContext(string[] args)
    {
        var environment =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var projectDirectory = FindProjectDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(
                $"appsettings.{environment}.json",
                optional: true)
            .AddUserSecrets<TransportationDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");
        }

        var options = new DbContextOptionsBuilder<TransportationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TransportationDbContext(options);
    }

    private static string FindProjectDirectory()
    {
        var current = Directory.GetCurrentDirectory();

        if (File.Exists(Path.Combine(current, "TransportationService.Api.csproj")))
        {
            return current;
        }

        var child = Path.Combine(current, "TransportationService.Api");

        if (File.Exists(Path.Combine(child, "TransportationService.Api.csproj")))
        {
            return child;
        }

        throw new InvalidOperationException(
            "Could not locate TransportationService.Api project directory.");
    }
}
