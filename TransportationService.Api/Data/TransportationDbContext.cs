using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Models;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Eligibility.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Reference.Entities;
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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<QualificationType> QualificationTypes => Set<QualificationType>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();

    public DbSet<EligibilityOverride> EligibilityOverrides => Set<EligibilityOverride>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Organisation master data
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobFunction> JobFunctions => Set<JobFunction>();

    // Classification categories
    public DbSet<VehicleCategory> VehicleCategories => Set<VehicleCategory>();
    public DbSet<TrailerCategory> TrailerCategories => Set<TrailerCategory>();
    public DbSet<DriverCategory> DriverCategories => Set<DriverCategory>();
    public DbSet<CustomerCategory> CustomerCategories => Set<CustomerCategory>();

    // Fleet
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    // Drivers
    public DbSet<Driver> Drivers => Set<Driver>();

    // Locations
    public DbSet<Location> Locations => Set<Location>();

    // Partners
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();

    // Reference data
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<ContractType> ContractTypes => Set<ContractType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationDbContext).Assembly);
    }
}
