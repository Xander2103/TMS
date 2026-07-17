using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Models;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Eligibility.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
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

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<QualificationType> QualificationTypes => Set<QualificationType>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();

    public DbSet<EligibilityOverride> EligibilityOverrides => Set<EligibilityOverride>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationDbContext).Assembly);
    }
}
