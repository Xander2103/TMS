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
    private readonly Common.Persistence.ITenantQueryFilterAccessor? _tenantFilter;

    public TransportationDbContext(
        DbContextOptions<TransportationDbContext> options,
        Common.Persistence.ITenantQueryFilterAccessor? tenantFilter = null)
        : base(options)
    {
        _tenantFilter = tenantFilter;
    }

    /// <summary>
    /// Ambient tenant for the global query filter (H1). Null (no accessor, or system/background
    /// scope) leaves the filter open; a request-scoped context is fenced to its own tenant on
    /// EVERY tenant-owned entity, independent of the services' explicit Where-clauses.
    /// Referenced from the model's query filters — EF re-parameterizes the context reference per
    /// query, so the value is read live on each execution.
    /// </summary>
    public Guid? CurrentTenantFilterId => _tenantFilter?.CurrentTenantIdOrNull;

    // Transport orders (Phase 5)
    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();
    public DbSet<TransportOrderStop> TransportOrderStops => Set<TransportOrderStop>();

    // Planning (Phase 6)
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripOrder> TripOrders => Set<TripOrder>();
    public DbSet<StopExecution> StopExecutions => Set<StopExecution>();
    public DbSet<StopStatusHistory> StopStatusHistories => Set<StopStatusHistory>();
    public DbSet<CargoItem> CargoItems => Set<CargoItem>();
    public DbSet<TransportOrderPricingLine> TransportOrderPricingLines => Set<TransportOrderPricingLine>();
    public DbSet<TransportOrderServiceLine> TransportOrderServiceLines => Set<TransportOrderServiceLine>();
    public DbSet<TransportOrderPricingSnapshot> TransportOrderPricingSnapshots => Set<TransportOrderPricingSnapshot>();
    public DbSet<TransportOrderDocument> TransportOrderDocuments => Set<TransportOrderDocument>();
    public DbSet<TenantDocumentRule> TenantDocumentRules => Set<TenantDocumentRule>();
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
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.NotificationRule> NotificationRules => Set<TransportationService.Api.Modules.Messaging.Entities.NotificationRule>();
    public DbSet<TransportationService.Api.Modules.Messaging.Entities.CustomerNotificationOverride> CustomerNotificationOverrides => Set<TransportationService.Api.Modules.Messaging.Entities.CustomerNotificationOverride>();
    public DbSet<TransportationService.Api.Modules.Planning.Entities.ConflictOverride> ConflictOverrides => Set<TransportationService.Api.Modules.Planning.Entities.ConflictOverride>();
    public DbSet<TransportationService.Api.Modules.Operations.Entities.OperationalAlert> OperationalAlerts => Set<TransportationService.Api.Modules.Operations.Entities.OperationalAlert>();
    public DbSet<TransportationService.Api.Modules.Portal.Entities.UserResourceLink> UserResourceLinks => Set<TransportationService.Api.Modules.Portal.Entities.UserResourceLink>();
    public DbSet<TransportationService.Api.Modules.Portal.Entities.PersonalCalendarNote> PersonalCalendarNotes => Set<TransportationService.Api.Modules.Portal.Entities.PersonalCalendarNote>();
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
    public DbSet<TransportationService.Api.Modules.Partners.Entities.CustomerAllowedLegalEntity> CustomerAllowedLegalEntities => Set<TransportationService.Api.Modules.Partners.Entities.CustomerAllowedLegalEntity>();
    public DbSet<TransportationService.Api.Modules.Dossiers.Entities.ActivityType> ActivityTypes => Set<TransportationService.Api.Modules.Dossiers.Entities.ActivityType>();
    public DbSet<TransportationService.Api.Modules.Dossiers.Entities.DossierActivity> DossierActivities => Set<TransportationService.Api.Modules.Dossiers.Entities.DossierActivity>();
    public DbSet<TransportationService.Api.Modules.Incidents.Entities.Incident> Incidents => Set<TransportationService.Api.Modules.Incidents.Entities.Incident>();
    public DbSet<TransportationService.Api.Modules.Incidents.Entities.IncidentChargePolicy> IncidentChargePolicies => Set<TransportationService.Api.Modules.Incidents.Entities.IncidentChargePolicy>();

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
    public DbSet<EmployeeNote> EmployeeNotes => Set<EmployeeNote>();
    public DbSet<IssuedItemTemplate> IssuedItemTemplates => Set<IssuedItemTemplate>();
    public DbSet<IssuedItemCategory> IssuedItemCategories => Set<IssuedItemCategory>();
    public DbSet<EmployeeIssuedItem> EmployeeIssuedItems => Set<EmployeeIssuedItem>();
    public DbSet<IssuedItemAttributeDefinition> IssuedItemAttributeDefinitions => Set<IssuedItemAttributeDefinition>();
    public DbSet<IssuedItemAttributeOption> IssuedItemAttributeOptions => Set<IssuedItemAttributeOption>();
    public DbSet<IssuedItemTemplateAttribute> IssuedItemTemplateAttributes => Set<IssuedItemTemplateAttribute>();
    public DbSet<IssuedItemVariant> IssuedItemVariants => Set<IssuedItemVariant>();
    public DbSet<IssuedItemVariantValue> IssuedItemVariantValues => Set<IssuedItemVariantValue>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryAlert> InventoryAlerts => Set<InventoryAlert>();
    public DbSet<ReorderProposal> ReorderProposals => Set<ReorderProposal>();

    public DbSet<TransportationService.Api.Modules.Tasks.Entities.TaskCategory> TaskCategories =>
        Set<TransportationService.Api.Modules.Tasks.Entities.TaskCategory>();
    public DbSet<TransportationService.Api.Modules.Tasks.Entities.EmployeeTask> EmployeeTasks =>
        Set<TransportationService.Api.Modules.Tasks.Entities.EmployeeTask>();
    public DbSet<TransportationService.Api.Modules.Tasks.Entities.TaskAttachment> TaskAttachments =>
        Set<TransportationService.Api.Modules.Tasks.Entities.TaskAttachment>();
    public DbSet<TransportationService.Api.Modules.Tasks.Entities.TaskTemplate> TaskTemplates =>
        Set<TransportationService.Api.Modules.Tasks.Entities.TaskTemplate>();
    public DbSet<TransportationService.Api.Modules.Tasks.Entities.TaskTemplateItem> TaskTemplateItems =>
        Set<TransportationService.Api.Modules.Tasks.Entities.TaskTemplateItem>();
    public DbSet<TransportationService.Api.Modules.Tasks.Entities.TaskRecurrence> TaskRecurrences =>
        Set<TransportationService.Api.Modules.Tasks.Entities.TaskRecurrence>();

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
    public DbSet<TachographCalibration> TachographCalibrations => Set<TachographCalibration>();
    public DbSet<LeasingContract> LeasingContracts => Set<LeasingContract>();
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
    public DbSet<TransportationService.Api.Modules.Hr.Entities.HrReminderSettings> HrReminderSettings => Set<TransportationService.Api.Modules.Hr.Entities.HrReminderSettings>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.ExpiryReminderPolicy> ExpiryReminderPolicies => Set<TransportationService.Api.Modules.Hr.Entities.ExpiryReminderPolicy>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.ReminderDispatchLog> ReminderDispatchLogs => Set<TransportationService.Api.Modules.Hr.Entities.ReminderDispatchLog>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.LeaveBalanceType> LeaveBalanceTypes => Set<TransportationService.Api.Modules.Hr.Entities.LeaveBalanceType>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.LeaveType> LeaveTypes => Set<TransportationService.Api.Modules.Hr.Entities.LeaveType>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.EmployeeLeaveBalance> EmployeeLeaveBalances => Set<TransportationService.Api.Modules.Hr.Entities.EmployeeLeaveBalance>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.LeaveBalanceAdjustment> LeaveBalanceAdjustments => Set<TransportationService.Api.Modules.Hr.Entities.LeaveBalanceAdjustment>();
    public DbSet<TransportationService.Api.Modules.Hr.Entities.LeaveEntitlementSettings> LeaveEntitlementSettings => Set<TransportationService.Api.Modules.Hr.Entities.LeaveEntitlementSettings>();

    // Customer portal (messages, announcements)
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.CustomerMessage> CustomerMessages =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.CustomerMessage>();
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.CustomerMessageRead> CustomerMessageReads =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.CustomerMessageRead>();
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.PortalAnnouncement> PortalAnnouncements =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.PortalAnnouncement>();
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessage> PortalMessages =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessage>();
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessageRecipient> PortalMessageRecipients =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessageRecipient>();
    public DbSet<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessageReceipt> PortalMessageReceipts =>
        Set<TransportationService.Api.Modules.CustomerPortal.Entities.PortalMessageReceipt>();

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
    public DbSet<TransportationService.Api.Modules.Reference.Entities.UnitType> UnitTypes => Set<TransportationService.Api.Modules.Reference.Entities.UnitType>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingZone> PricingZones => Set<TransportationService.Api.Modules.Tarification.Entities.PricingZone>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingZoneArea> PricingZoneAreas => Set<TransportationService.Api.Modules.Tarification.Entities.PricingZoneArea>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PriceRule> PriceRules => Set<TransportationService.Api.Modules.Tarification.Entities.PriceRule>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PriceRuleBracket> PriceRuleBrackets => Set<TransportationService.Api.Modules.Tarification.Entities.PriceRuleBracket>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PriceRuleBracketOverride> PriceRuleBracketOverrides => Set<TransportationService.Api.Modules.Tarification.Entities.PriceRuleBracketOverride>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.ServiceOptionCondition> ServiceOptionConditions => Set<TransportationService.Api.Modules.Tarification.Entities.ServiceOptionCondition>();
    public DbSet<TransportationService.Api.Modules.Accounting.Entities.LedgerAccount> LedgerAccounts => Set<TransportationService.Api.Modules.Accounting.Entities.LedgerAccount>();
    public DbSet<TransportationService.Api.Modules.Accounting.Entities.SalesCategory> SalesCategories => Set<TransportationService.Api.Modules.Accounting.Entities.SalesCategory>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.ServiceOption> ServiceOptions => Set<TransportationService.Api.Modules.Tarification.Entities.ServiceOption>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.TenantHoliday> TenantHolidays => Set<TransportationService.Api.Modules.Tarification.Entities.TenantHoliday>();
    public DbSet<TransportationService.Api.Modules.Warehousing.Entities.WarehouseLocation> WarehouseLocations => Set<TransportationService.Api.Modules.Warehousing.Entities.WarehouseLocation>();
    public DbSet<TransportationService.Api.Modules.Warehousing.Entities.StorageStay> StorageStays => Set<TransportationService.Api.Modules.Warehousing.Entities.StorageStay>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.CustomerServiceOptionPrice> CustomerServiceOptionPrices => Set<TransportationService.Api.Modules.Tarification.Entities.CustomerServiceOptionPrice>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.CustomerPreferredUnit> CustomerPreferredUnits => Set<TransportationService.Api.Modules.Tarification.Entities.CustomerPreferredUnit>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingAgreement> PricingAgreements => Set<TransportationService.Api.Modules.Tarification.Entities.PricingAgreement>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementSurcharge> PricingAgreementSurcharges => Set<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementSurcharge>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementAssignment> PricingAgreementAssignments => Set<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementAssignment>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementModifier> PricingAgreementModifiers => Set<TransportationService.Api.Modules.Tarification.Entities.PricingAgreementModifier>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.ScheduledPriceAdjustment> ScheduledPriceAdjustments => Set<TransportationService.Api.Modules.Tarification.Entities.ScheduledPriceAdjustment>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.ScheduledPriceAdjustmentRule> ScheduledPriceAdjustmentRules => Set<TransportationService.Api.Modules.Tarification.Entities.ScheduledPriceAdjustmentRule>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscount> CombinedUnitDiscounts => Set<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscount>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscountUnit> CombinedUnitDiscountUnits => Set<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscountUnit>();
    public DbSet<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscountTier> CombinedUnitDiscountTiers => Set<TransportationService.Api.Modules.Tarification.Entities.CombinedUnitDiscountTier>();
    public DbSet<TransportationService.Api.Modules.Peppol.Entities.PeppolSettings> PeppolSettings => Set<TransportationService.Api.Modules.Peppol.Entities.PeppolSettings>();
    public DbSet<TransportationService.Api.Modules.Peppol.Entities.PeppolTransmission> PeppolTransmissions => Set<TransportationService.Api.Modules.Peppol.Entities.PeppolTransmission>();
    public DbSet<TransportationService.Api.Modules.Peppol.Entities.PeppolTransmissionEvent> PeppolTransmissionEvents => Set<TransportationService.Api.Modules.Peppol.Entities.PeppolTransmissionEvent>();
    public DbSet<TransportationService.Api.Modules.Peppol.Entities.PeppolIncomingDocument> PeppolIncomingDocuments => Set<TransportationService.Api.Modules.Peppol.Entities.PeppolIncomingDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationDbContext).Assembly);
        ApplyGlobalTenantFilters(modelBuilder);
        ApplyColumnEncryption(modelBuilder);
    }

    /// <summary>
    /// Fase 9: special-category identifiers are encrypted at the application boundary, so the
    /// database (and its backups/exports) only ever sees ciphertext. Pass-through when no key is
    /// configured (Development); legacy plaintext keeps reading either way.
    /// </summary>
    private static void ApplyColumnEncryption(ModelBuilder modelBuilder)
    {
        var converter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string, string>(
            value => Modules.Security.ColumnEncryption.Protect(value),
            stored => Modules.Security.ColumnEncryption.Unprotect(stored));

        var employee = modelBuilder.Entity<Employee>();
        employee.Property(e => e.NationalRegisterNumber).HasConversion(converter!);
        employee.Property(e => e.IdentityCardNumber).HasConversion(converter!);
    }

    /// <summary>
    /// H1: one structural fence instead of hundreds of hand-written Where-clauses. Every entity
    /// implementing <see cref="Common.Abstractions.ITenantOwned"/> gets
    /// <c>CurrentTenantFilterId == null || e.TenantId == CurrentTenantFilterId</c> AND-ed onto
    /// whatever filter its configuration already declared (typically soft delete). Callers using
    /// <c>IgnoreQueryFilters()</c> therefore bypass BOTH — every such site must (and does) carry
    /// its own explicit tenant predicate.
    /// </summary>
    private void ApplyGlobalTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Common.Abstractions.ITenantOwned).IsAssignableFrom(entityType.ClrType)
                || entityType.IsOwned()
                || entityType.BaseType is not null)
            {
                continue;
            }

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var currentTenant = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(this), nameof(CurrentTenantFilterId));
            var tenantBody = System.Linq.Expressions.Expression.OrElse(
                System.Linq.Expressions.Expression.Equal(
                    currentTenant, System.Linq.Expressions.Expression.Constant(null, typeof(Guid?))),
                System.Linq.Expressions.Expression.Equal(
                    System.Linq.Expressions.Expression.Convert(
                        System.Linq.Expressions.Expression.Property(
                            parameter, nameof(Common.Abstractions.ITenantOwned.TenantId)),
                        typeof(Guid?)),
                    currentTenant));

            var body = tenantBody;
            foreach (var existing in entityType.GetDeclaredQueryFilters())
            {
                if (existing.Expression is not { } existingExpression)
                {
                    continue;
                }

                var rebased = Microsoft.EntityFrameworkCore.Query.ReplacingExpressionVisitor.Replace(
                    existingExpression.Parameters[0], parameter, existingExpression.Body);
                body = System.Linq.Expressions.Expression.AndAlso(rebased, body);
            }

            entityType.SetQueryFilter(System.Linq.Expressions.Expression.Lambda(body, parameter));
        }
    }
}
