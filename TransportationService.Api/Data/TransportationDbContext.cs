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
    public DbSet<TransportOrderStatusHistory> TransportOrderStatusHistories => Set<TransportOrderStatusHistory>();
    public DbSet<TransportationService.Api.Modules.Exceptions.Entities.ExecutionException> ExecutionExceptions => Set<TransportationService.Api.Modules.Exceptions.Entities.ExecutionException>();
    public DbSet<TransportationService.Api.Modules.Exceptions.Entities.ExceptionPhoto> ExceptionPhotos => Set<TransportationService.Api.Modules.Exceptions.Entities.ExceptionPhoto>();
    public DbSet<ScanEvent> ScanEvents => Set<ScanEvent>();
    public DbSet<TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery> ProofsOfDelivery => Set<TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery>();
    public DbSet<TransportationService.Api.Modules.Pod.Entities.PodPhoto> PodPhotos => Set<TransportationService.Api.Modules.Pod.Entities.PodPhoto>();
    public DbSet<TransportationService.Api.Modules.EmployeePlanning.Entities.Shift> Shifts => Set<TransportationService.Api.Modules.EmployeePlanning.Entities.Shift>();
    public DbSet<TransportationService.Api.Modules.EmployeePlanning.Entities.TripPlanningEntry> TripPlanningEntries => Set<TransportationService.Api.Modules.EmployeePlanning.Entities.TripPlanningEntry>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.OutboxMessage> OutboxMessages => Set<TransportationService.Api.Modules.Messaging.Entities.OutboxMessage>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.MessagingProfile> MessagingProfiles => Set<TransportationService.Api.Modules.Messaging.Entities.MessagingProfile>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.MessageTemplate> MessageTemplates => Set<TransportationService.Api.Modules.Messaging.Entities.MessageTemplate>();
    public DbSet<TransportationService.Api.Modules.Planning.Entities.ConflictOverride> ConflictOverrides => Set<TransportationService.Api.Modules.Planning.Entities.ConflictOverride>();
    public DbSet<TransportationService.Api.Modules.Operations.Entities.OperationalAlert> OperationalAlerts => Set<TransportationService.Api.Modules.Operations.Entities.OperationalAlert>();
    public DbSet<TransportationService.Api.Modules.Portal.Entities.UserResourceLink> UserResourceLinks => Set<TransportationService.Api.Modules.Portal.Entities.UserResourceLink>();
    public DbSet<TransportationService.Api.Modules.Warehousing.Entities.Warehouse> Warehouses => Set<TransportationService.Api.Modules.Warehousing.Entities.Warehouse>();
    public DbSet<TransportationService.Api.Modules.Warehousing.Entities.Dock> Docks => Set<TransportationService.Api.Modules.Warehousing.Entities.Dock>();
    public DbSet<TransportationService.Api.Modules.Warehousing.Entities.DockAppointment> DockAppointments => Set<TransportationService.Api.Modules.Warehousing.Entities.DockAppointment>();
    public DbSet<TransportationService.Api.Modules.Eta.Entities.StopEta> StopEtas => Set<TransportationService.Api.Modules.Eta.Entities.StopEta>();
    public DbSet<TransportationService.Api.Modules.Eta.Entities.StopEtaHistory> StopEtaHistories => Set<TransportationService.Api.Modules.Eta.Entities.StopEtaHistory>();
    public DbSet<TransportationService.Api.Modules.Edi.Entities.TradingPartner> TradingPartners => Set<TransportationService.Api.Modules.Edi.Entities.TradingPartner>();
    public DbSet<TransportationService.Api.Modules.Edi.Entities.EdiPartnerLocation> EdiPartnerLocations => Set<TransportationService.Api.Modules.Edi.Entities.EdiPartnerLocation>();
    public DbSet<TransportationService.Api.Modules.Edi.Entities.EdiMessage> EdiMessages => Set<TransportationService.Api.Modules.Edi.Entities.EdiMessage>();
    public DbSet<TransportationService.Api.Modules.Integrations.Entities.CalendarSyncItem> CalendarSyncItems => Set<TransportationService.Api.Modules.Integrations.Entities.CalendarSyncItem>();
    public DbSet<TransportationService.Api.Modules.Identity.Entities.RoleTemplateState> RoleTemplateStates => Set<TransportationService.Api.Modules.Identity.Entities.RoleTemplateState>();
    public DbSet<TransportationService.Api.Modules.Packages.Entities.Package> Packages => Set<TransportationService.Api.Modules.Packages.Entities.Package>();
    public DbSet<TransportationService.Api.Modules.Packages.Entities.PackageBarcode> PackageBarcodes => Set<TransportationService.Api.Modules.Packages.Entities.PackageBarcode>();
    public DbSet<TransportationService.Api.Modules.Packages.Entities.PackageEvent> PackageEvents => Set<TransportationService.Api.Modules.Packages.Entities.PackageEvent>();
    public DbSet<TransportationService.Api.Modules.Packages.Entities.PackageLabel> PackageLabels => Set<TransportationService.Api.Modules.Packages.Entities.PackageLabel>();
    public DbSet<TransportationService.Api.Modules.TripCosting.Entities.CostRateSet> CostRateSets => Set<TransportationService.Api.Modules.TripCosting.Entities.CostRateSet>();
    public DbSet<TransportationService.Api.Modules.TripCosting.Entities.TripCostLine> TripCostLines => Set<TransportationService.Api.Modules.TripCosting.Entities.TripCostLine>();
    public DbSet<TransportationService.Api.Modules.TripCosting.Entities.TripCostSummary> TripCostSummaries => Set<TransportationService.Api.Modules.TripCosting.Entities.TripCostSummary>();

    // Dossiers & incidents
    public DbSet<TransportationService.Api.Modules.Dossiers.Entities.TransportDossier> TransportDossiers => Set<TransportationService.Api.Modules.Dossiers.Entities.TransportDossier>();
    public DbSet<TransportationService.Api.Modules.Dossiers.Entities.DossierOrder> DossierOrders => Set<TransportationService.Api.Modules.Dossiers.Entities.DossierOrder>();
    public DbSet<TransportationService.Api.Modules.Dossiers.Entities.DossierRelation> DossierRelations => Set<TransportationService.Api.Modules.Dossiers.Entities.DossierRelation>();
    public DbSet<TransportationService.Api.Modules.Incidents.Entities.Incident> Incidents => Set<TransportationService.Api.Modules.Incidents.Entities.Incident>();

    // Tarification
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.RateCard> RateCards => Set<TransportationService.Api.Modules.Tarification.Entities.RateCard>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.RateSurcharge> RateSurcharges => Set<TransportationService.Api.Modules.Tarification.Entities.RateSurcharge>();

    // Invoicing (Phase 8)
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    // Notifications (Phase 10)
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();
    public DbSet<JobFunctionRoleMapping> JobFunctionRoleMappings => Set<JobFunctionRoleMapping>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeEmergencyContact> EmployeeEmergencyContacts => Set<EmployeeEmergencyContact>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    public DbSet<QualificationType> QualificationTypes => Set<QualificationType>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();

    public DbSet<EligibilityOverride> EligibilityOverrides => Set<EligibilityOverride>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Organisation master data
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobFunction> JobFunctions => Set<JobFunction>();

    // Own companies / invoicing entities
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<UserLegalEntitySelection> UserLegalEntitySelections => Set<UserLegalEntitySelection>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<InvoiceAttachment> InvoiceAttachments => Set<InvoiceAttachment>();

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
    public DbSet<MaintenancePolicy> MaintenancePolicies => Set<MaintenancePolicy>();

    // Drivers
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<DriverDriverCategory> DriverDriverCategories => Set<DriverDriverCategory>();

    // HR availability
    public DbSet<Absence> Absences => Set<Absence>();

    // Locations
    public DbSet<Location> Locations => Set<Location>();

    // Partners
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<ContactDepartment> ContactDepartments => Set<ContactDepartment>();
    public DbSet<CustomerCommunicationRule> CustomerCommunicationRules => Set<CustomerCommunicationRule>();
    public DbSet<CustomerCommunicationRuleContact> CustomerCommunicationRuleContacts => Set<CustomerCommunicationRuleContact>();
    public DbSet<CustomerDieselSurcharge> CustomerDieselSurcharges => Set<CustomerDieselSurcharge>();
    public DbSet<CustomerPurchaseOrderNumber> CustomerPurchaseOrderNumbers => Set<CustomerPurchaseOrderNumber>();

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
