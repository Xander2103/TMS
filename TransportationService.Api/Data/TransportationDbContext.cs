using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Models;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Data;

public class TransportationDbContext : DbContext
{
    public TransportationDbContext(DbContextOptions<TransportationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationDbContext).Assembly);
    }
}
