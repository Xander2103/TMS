using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Eligibility.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Data;

public class TransportationDbContext : DbContext
{
    public TransportationDbContext(DbContextOptions<TransportationDbContext> options)
        : base(options)
    {
    }

    // Transport orders (Phase 5)
    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();
    public DbSet<TransportOrderStop> TransportOrderStops => Set<TransportOrderStop>();

    // Planning (Phase 6)
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripOrder> TripOrders => Set<TripOrder>();
    public DbSet<StopExecution> StopExecutions => Set<StopExecution>();
    public DbSet<StopStatusHistory> StopStatusHistories => Set<StopStatusHistory>();
    public DbSet<CargoItem> CargoItems => Set<CargoItem>();
    public DbSet<TransportationService.Api.Modules.Exceptions.Entities.ExecutionException> ExecutionExceptions => Set<TransportationService.Api.Modules.Exceptions.Entities.ExecutionException>();
    public DbSet<TransportationService.Api.Modules.Exceptions.Entities.ExceptionPhoto> ExceptionPhotos => Set<TransportationService.Api.Modules.Exceptions.Entities.ExceptionPhoto>();
    public DbSet<ScanEvent> ScanEvents => Set<ScanEvent>();
    public DbSet<TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery> ProofsOfDelivery => Set<TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery>();
    public DbSet<TransportationService.Api.Modules.Pod.Entities.PodPhoto> PodPhotos => Set<TransportationService.Api.Modules.Pod.Entities.PodPhoto>();
    public DbSet<TransportationService.Api.Modules.EmployeePlanning.Entities.Shift> Shifts => Set<TransportationService.Api.Modules.EmployeePlanning.Entities.Shift>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.OutboxMessage> OutboxMessages => Set<TransportationService.Api.Modules.Messaging.Entities.OutboxMessage>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.MessagingProfile> MessagingProfiles => Set<TransportationService.Api.Modules.Messaging.Entities.MessagingProfile>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.MessageTemplate> MessageTemplates => Set<TransportationService.Api.Modules.Messaging.Entities.MessageTemplate>();

    // Invoicing (Phase 8)
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    // Notifications (Phase 10)
    public DbSet<Notification> Notifications => Set<Notification>();

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
    public DbSet<Trailer> Trailers => Set<Trailer>();
    public DbSet<FleetDocument> FleetDocuments => Set<FleetDocument>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<DamageReport> DamageReports => Set<DamageReport>();
    public DbSet<TankCard> TankCards => Set<TankCard>();
    public DbSet<FuelTransaction> FuelTransactions => Set<FuelTransaction>();

    // Drivers
    public DbSet<Driver> Drivers => Set<Driver>();

    // HR availability
    public DbSet<Absence> Absences => Set<Absence>();

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
