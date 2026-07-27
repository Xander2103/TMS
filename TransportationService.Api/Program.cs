using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Controllers + enums als tekst in JSON
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<TransportationService.Api.Common.InvalidTenantReferenceExceptionFilter>();
        options.Filters.Add<TransportationService.Api.Common.DomainValidationExceptionFilter>();
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        )
    );

// OpenAPI
builder.Services.AddOpenApi();

// Consistent RFC7807 error responses
builder.Services.AddProblemDetails();

// JWT authentication + authorization (password hashing, token + auth services)
builder.Services.AddJwtAuthentication(builder.Configuration);

// CORS voor React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Algemene services
builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContextAccessors();
builder.Services.AddSingleton(TimeProvider.System);

// Cross-cutting persistence behaviour (audit stamps, soft delete, order status history)
builder.Services.AddSingleton<AuditingSaveChangesInterceptor>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor>();

// PostgreSQL + EF Core
builder.Services.AddDbContext<TransportationDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(
            serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor>())
);

// Identity en permissions
builder.Services.AddScoped<
    IPermissionAuthorizationService,
    PermissionAuthorizationService
>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserAccountFlowService, UserAccountFlowService>();
builder.Services.AddScoped<IJobFunctionRoleMappingService, JobFunctionRoleMappingService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Employees
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeHistoryService, EmployeeHistoryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Accounting.Services.IAccountingService, TransportationService.Api.Modules.Accounting.Services.AccountingService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Accounting.Services.IAccountingExportService, TransportationService.Api.Modules.Accounting.Services.AccountingExportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IEmployeeDocumentService,
    TransportationService.Api.Modules.Employees.Services.EmployeeDocumentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Hr.Services.IHrReminderConfigService,
    TransportationService.Api.Modules.Hr.Services.HrReminderConfigService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Hr.Services.ILeaveBalanceService,
    TransportationService.Api.Modules.Hr.Services.LeaveBalanceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IIssuedItemService,
    TransportationService.Api.Modules.Employees.Services.IssuedItemService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IInventoryService,
    TransportationService.Api.Modules.Employees.Services.InventoryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.ILowStockNotifier,
    TransportationService.Api.Modules.Employees.Services.LowStockNotifier>();

// Qualification file storage
builder.Services.AddSingleton<IFileStorageService>(
    new LocalFileStorageService(
        Path.Combine(
            builder.Environment.ContentRootPath,
            "App_Data"
        )
    )
);

// Qualifications
builder.Services.AddSingleton<
    IQualificationStatusCalculator,
    QualificationStatusCalculator
>();
builder.Services.AddScoped<
    IQualificationService,
    QualificationService
>();

// Eligibility
builder.Services.AddScoped<
    IDriverEligibilityService,
    DriverEligibilityService
>();
builder.Services.AddScoped<
    IEligibilityOverrideService,
    EligibilityOverrideService
>();

// Audit
builder.Services.AddScoped<IAuditService, AuditService>();

// Global reference data (country code validation against the seeded ISO list)
builder.Services.AddScoped<TransportationService.Api.Common.Reference.ICountryCodeValidator,
    TransportationService.Api.Common.Reference.CountryCodeValidator>();

// Generic tenant lookup CRUD (departments, functions, categories, reference data, ...)
builder.Services.AddScoped(typeof(TransportationService.Api.Common.Lookups.ILookupService<>),
    typeof(TransportationService.Api.Common.Lookups.LookupService<>));

// Partners
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerImportService,
    TransportationService.Api.Modules.Partners.Services.CustomerImportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerCommunicationService,
    TransportationService.Api.Modules.Partners.Services.CustomerCommunicationService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerBillingConfigService,
    TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService>();
// Official company-registry seam: no provider configured by default (no scraping).
builder.Services.AddSingleton<TransportationService.Api.Modules.Partners.Services.ICompanyRegistryProvider,
    TransportationService.Api.Modules.Partners.Services.NullCompanyRegistryProvider>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerService,
    TransportationService.Api.Modules.Partners.Services.CustomerService>();

// Drivers
builder.Services.AddScoped<TransportationService.Api.Modules.Drivers.Services.IDriverService,
    TransportationService.Api.Modules.Drivers.Services.DriverService>();

// Driver-vehicle assignment (single source of truth, editable from both sides)
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFleetAssignmentService,
    TransportationService.Api.Modules.Fleet.Services.FleetAssignmentService>();

// Company / tenant settings
builder.Services.AddScoped<TransportationService.Api.Modules.Tenancy.Services.ICompanySettingsService,
    TransportationService.Api.Modules.Tenancy.Services.CompanySettingsService>();

// Own companies / invoicing entities
builder.Services.AddScoped<TransportationService.Api.Modules.Organization.Services.ILegalEntityService,
    TransportationService.Api.Modules.Organization.Services.LegalEntityService>();

// Locations
builder.Services.AddScoped<TransportationService.Api.Modules.Locations.Services.ILocationService,
    TransportationService.Api.Modules.Locations.Services.LocationService>();

// Fleet
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IVehicleService,
    TransportationService.Api.Modules.Fleet.Services.VehicleService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.ITrailerService,
    TransportationService.Api.Modules.Fleet.Services.TrailerService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFleetKpiService,
    TransportationService.Api.Modules.Fleet.Services.FleetKpiService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.ITachographService,
    TransportationService.Api.Modules.Fleet.Services.TachographService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.ILeasingContractService,
    TransportationService.Api.Modules.Fleet.Services.LeasingContractService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFleetDocumentService,
    TransportationService.Api.Modules.Fleet.Services.FleetDocumentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IMaintenanceService,
    TransportationService.Api.Modules.Fleet.Services.MaintenanceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IMaintenancePolicyService,
    TransportationService.Api.Modules.Fleet.Services.MaintenancePolicyService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IInspectionService,
    TransportationService.Api.Modules.Fleet.Services.InspectionService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IDamageReportService,
    TransportationService.Api.Modules.Fleet.Services.DamageReportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.ITankCardService,
    TransportationService.Api.Modules.Fleet.Services.TankCardService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFuelService,
    TransportationService.Api.Modules.Fleet.Services.FuelService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFleetDashboardService,
    TransportationService.Api.Modules.Fleet.Services.FleetDashboardService>();

// Dossiers & incidents
builder.Services.AddScoped<TransportationService.Api.Modules.Dossiers.Services.IDossierService,
    TransportationService.Api.Modules.Dossiers.Services.DossierService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Incidents.Services.IIncidentService,
    TransportationService.Api.Modules.Incidents.Services.IncidentService>();

builder.Services.AddScoped<TransportationService.Api.Modules.Reference.Services.IUnitTypeMasterService,
    TransportationService.Api.Modules.Reference.Services.UnitTypeMasterService>();

// Tarification
builder.Services.AddScoped<TransportationService.Api.Modules.Tarification.Services.IPricingAdminService,
    TransportationService.Api.Modules.Tarification.Services.PricingAdminService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tarification.Services.IPricingEngine,
    TransportationService.Api.Modules.Tarification.Services.PricingEngine>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tarification.Services.IPriceAdjustmentService,
    TransportationService.Api.Modules.Tarification.Services.PriceAdjustmentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tarification.Services.IPricingExcelService,
    TransportationService.Api.Modules.Tarification.Services.PricingExcelService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderDocumentService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderDocumentService>();

// Planning center read models
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IPlanningBoardService,
    TransportationService.Api.Modules.Planning.Services.PlanningBoardService>();

// Operation control center (alerts projection + lifecycle)
builder.Services.AddScoped<TransportationService.Api.Modules.Operations.Services.IAlertService,
    TransportationService.Api.Modules.Operations.Services.AlertService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Operations.Services.IAlertSyncService,
    TransportationService.Api.Modules.Operations.Services.AlertSyncService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Operations.Services.IOperationsOverviewService,
    TransportationService.Api.Modules.Operations.Services.OperationsOverviewService>();

// Warehouse & dock planning
builder.Services.AddScoped<TransportationService.Api.Modules.Warehousing.Services.IWarehouseAdminService,
    TransportationService.Api.Modules.Warehousing.Services.WarehouseAdminService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Warehousing.Services.IDockPlanningService,
    TransportationService.Api.Modules.Warehousing.Services.DockPlanningService>();

// Profitability read models + export
builder.Services.AddScoped<TransportationService.Api.Modules.Profitability.Services.IProfitabilityQueryService,
    TransportationService.Api.Modules.Profitability.Services.ProfitabilityQueryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Profitability.Services.IProfitabilityExportService,
    TransportationService.Api.Modules.Profitability.Services.ProfitabilityExportService>();

// Driver app (self-scoped dashboard, documents and incident reporting)
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IDriverAppService,
    TransportationService.Api.Modules.Planning.Services.DriverAppService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Incidents.Services.IDriverIncidentService,
    TransportationService.Api.Modules.Incidents.Services.DriverIncidentService>();

// Favorites / recent items / pinned resources (self-scoped)
builder.Services.AddScoped<TransportationService.Api.Modules.Portal.Services.IResourceLinkService,
    TransportationService.Api.Modules.Portal.Services.ResourceLinkService>();

// Global (Ctrl+K) search
builder.Services.AddScoped<TransportationService.Api.Modules.Search.Services.IGlobalSearchService,
    TransportationService.Api.Modules.Search.Services.GlobalSearchService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Identity.Services.IPermissionSetService,
    TransportationService.Api.Modules.Identity.Services.PermissionSetService>();

// HR availability
builder.Services.AddScoped<TransportationService.Api.Modules.Hr.Services.IAbsenceService,
    TransportationService.Api.Modules.Hr.Services.AbsenceService>();

// Transport orders
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderTimelineService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderTimelineService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.ICustomerPortalService,
    TransportationService.Api.Modules.CustomerPortal.Services.CustomerPortalService>();

// Planning (trips + conflict engine)
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IPlanningConflictService,
    TransportationService.Api.Modules.Planning.Services.PlanningConflictService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.ITripService,
    TransportationService.Api.Modules.Planning.Services.TripService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.ITripExecutionService,
    TransportationService.Api.Modules.Planning.Services.TripExecutionService>();

// Scanning
builder.Services.AddScoped<TransportationService.Api.Modules.Scanning.Services.IScanService,
    TransportationService.Api.Modules.Scanning.Services.ScanService>();

// Execution exceptions
builder.Services.AddScoped<TransportationService.Api.Modules.Exceptions.Services.IExecutionExceptionService,
    TransportationService.Api.Modules.Exceptions.Services.ExecutionExceptionService>();

// Proof of delivery
builder.Services.AddScoped<TransportationService.Api.Modules.Pod.Services.IPodService,
    TransportationService.Api.Modules.Pod.Services.PodService>();

// Employee portal (self-scoped)
builder.Services.AddScoped<TransportationService.Api.Modules.Portal.Services.IPortalService,
    TransportationService.Api.Modules.Portal.Services.PortalService>();

// Employee planning (personnel shifts)
builder.Services.AddScoped<TransportationService.Api.Modules.EmployeePlanning.Services.IShiftService,
    TransportationService.Api.Modules.EmployeePlanning.Services.ShiftService>();
builder.Services.AddScoped<TransportationService.Api.Modules.EmployeePlanning.Services.ITripPlanningSyncService,
    TransportationService.Api.Modules.EmployeePlanning.Services.TripPlanningSyncService>();

// Trip costing: effective-dated rate cards + estimated/actual/final cost engine
builder.Services.AddScoped<TransportationService.Api.Modules.TripCosting.Services.ICostRateService,
    TransportationService.Api.Modules.TripCosting.Services.CostRateService>();
builder.Services.AddScoped<TransportationService.Api.Modules.TripCosting.Services.ITripCostingService,
    TransportationService.Api.Modules.TripCosting.Services.TripCostingService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IKpiQueryService,
    TransportationService.Api.Modules.Reporting.Services.KpiQueryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IKpiExportService,
    TransportationService.Api.Modules.Reporting.Services.KpiExportService>();

// Packages: tracked cargo units, barcode registry and immutable chain of custody
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageService,
    TransportationService.Api.Modules.Packages.Services.PackageService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageBarcodeService,
    TransportationService.Api.Modules.Packages.Services.PackageBarcodeService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageEventWriter,
    TransportationService.Api.Modules.Packages.Services.PackageEventWriter>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageImportService,
    TransportationService.Api.Modules.Packages.Services.PackageImportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageGenerationService,
    TransportationService.Api.Modules.Packages.Services.PackageGenerationService>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Packages.Labels.ILabelRenderService,
    TransportationService.Api.Modules.Packages.Labels.LabelRenderService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Labels.IPackageLabelService,
    TransportationService.Api.Modules.Packages.Labels.PackageLabelService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageScanProcessor,
    TransportationService.Api.Modules.Packages.Services.PackageScanProcessor>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.ITripPackageService,
    TransportationService.Api.Modules.Packages.Services.TripPackageService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IWarehouseService,
    TransportationService.Api.Modules.Packages.Services.WarehouseService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Packages.Services.IPackageReportService,
    TransportationService.Api.Modules.Packages.Services.PackageReportService>();

// Outlook/Exchange foundation: queued calendar sync behind a provider seam. The fake provider
// stands in until Microsoft Graph credentials exist; swap ICalendarProvider then.
builder.Services.AddScoped<TransportationService.Api.Modules.Integrations.Services.QueueingCalendarSyncService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Integrations.Services.ICalendarSyncService>(sp =>
    sp.GetRequiredService<TransportationService.Api.Modules.Integrations.Services.QueueingCalendarSyncService>());
builder.Services.AddSingleton<TransportationService.Api.Modules.Integrations.Services.ICalendarProvider,
    TransportationService.Api.Modules.Integrations.Services.FakeCalendarProvider>();
builder.Services.AddScoped(sp => new TransportationService.Api.Modules.Integrations.Services.CalendarSyncProcessor(
    sp.GetRequiredService<TransportationService.Api.Data.TransportationDbContext>(),
    sp.GetRequiredService<TransportationService.Api.Modules.Integrations.Services.ICalendarProvider>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<TransportationService.Api.Modules.Integrations.Services.CalendarSyncHostedService>();

// Periodic expiry sweep (qualifications + fleet documents -> notifications)
builder.Services.AddHostedService<TransportationService.Api.Modules.Notifications.Services.ExpiryNotificationHostedService>();

// Messaging (provider-neutral email/SMS): dev sink providers, outbox, dispatcher
builder.Services.AddSingleton<TransportationService.Api.Modules.Messaging.Services.DevelopmentSinkProvider>(_ =>
    new TransportationService.Api.Modules.Messaging.Services.DevelopmentSinkProvider(
        Path.Combine(builder.Environment.ContentRootPath, "App_Data", "message-sink")));
builder.Services.AddSingleton<TransportationService.Api.Modules.Messaging.Services.IEmailProvider>(sp =>
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.DevelopmentSinkProvider>());
builder.Services.AddSingleton<TransportationService.Api.Modules.Messaging.Services.ISmsProvider>(sp =>
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.DevelopmentSinkProvider>());
builder.Services.AddScoped<TransportationService.Api.Modules.Messaging.Services.IMessageOutboxService,
    TransportationService.Api.Modules.Messaging.Services.MessageOutboxService>();
builder.Services.AddScoped(sp => new TransportationService.Api.Modules.Messaging.Services.MessageDispatcher(
    sp.GetRequiredService<TransportationService.Api.Data.TransportationDbContext>(),
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.IEmailProvider>(),
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.ISmsProvider>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<TransportationService.Api.Modules.Messaging.Services.OutboxDispatcherHostedService>();

// EDI foundation (generic-json-v1 profile; partner formats plug in as profiles later)
builder.Services.AddScoped<TransportationService.Api.Modules.Edi.Services.IEdiService,
    TransportationService.Api.Modules.Edi.Services.EdiService>();

// ETA foundation (sequential raming; IRouteEstimationProvider is the future PTV seam)
builder.Services.AddSingleton<TransportationService.Api.Modules.Eta.Services.IRouteEstimationProvider,
    TransportationService.Api.Modules.Eta.Services.NoRouteEstimationProvider>();
builder.Services.AddScoped<TransportationService.Api.Modules.Eta.Services.IEtaService,
    TransportationService.Api.Modules.Eta.Services.EtaService>();

// Invoicing
builder.Services.AddScoped<TransportationService.Api.Modules.Invoicing.Services.IInvoiceService,
    TransportationService.Api.Modules.Invoicing.Services.InvoiceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Invoicing.Services.IInvoiceNumberService,
    TransportationService.Api.Modules.Invoicing.Services.InvoiceNumberService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Invoicing.Services.IInvoiceAttachmentService,
    TransportationService.Api.Modules.Invoicing.Services.InvoiceAttachmentService>();

// Notifications (in-app)
builder.Services.AddScoped<TransportationService.Api.Modules.Notifications.Services.INotificationService,
    TransportationService.Api.Modules.Notifications.Services.NotificationService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Notifications.Services.IInternalMessageService,
    TransportationService.Api.Modules.Notifications.Services.InternalMessageService>();

// Reporting (company dashboard)
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IDashboardService,
    TransportationService.Api.Modules.Reporting.Services.DashboardService>();

var app = builder.Build();

// Global reference data runs in every environment: the ISO country list must exist for
// country validation and comboboxes regardless of how tenants are onboarded.
using (var referenceScope = app.Services.CreateScope())
{
    var referenceDbContext = referenceScope.ServiceProvider.GetRequiredService<TransportationDbContext>();
    await CountrySeeder.SyncAsync(referenceDbContext);
}

// Development-only setup
if (app.Environment.IsDevelopment())
{
    // OpenAPI JSON
    app.MapOpenApi();

    // Interactieve Scalar API-interface
    app.MapScalarApiReference();

    // Development seed data
    using var scope = app.Services.CreateScope();

    var dbContext = scope.ServiceProvider
        .GetRequiredService<TransportationDbContext>();

    await MasterDataSeeder.SeedAsync(dbContext);

    // Idempotent every startup: keep the permission catalog in sync and seed starter lookups.
    await PermissionCatalogSeeder.SyncAsync(dbContext);
    await ReferenceDataSeeder.SeedAsync(dbContext);
    // Legacy rate cards convert once into pricing agreements (idempotent, data preserved).
    await TransportationService.Api.Modules.Tarification.Services.RateCardConversionService.ConvertAsync(dbContext);
    await DefaultRoleSeeder.SyncAsync(dbContext);
    await LegalEntitySeeder.SeedAsync(dbContext);
    await ExpiryPolicySeeder.SeedAsync(dbContext);
    await IssuedItemTemplateSeeder.SeedAsync(dbContext);

    // Ensure the development administrator has a usable password (only when unset, so a
    // deliberately-changed password is never reset). Reported to the console below.
    var passwordHasher = scope.ServiceProvider
        .GetRequiredService<TransportationService.Api.Modules.Authentication.Services.IPasswordHasher>();
    await DevAdminSeeder.EnsurePasswordAsync(dbContext, passwordHasher, app.Logger);
}

// Enforce HTTPS in real environments. In Development the SPA talks to the API over http
// (http://localhost:5019); redirecting to https there would drop the Authorization header on the
// cross-scheme 307, so redirection is intentionally skipped for local development only.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();

app.UseMiddleware<TenantContextMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();