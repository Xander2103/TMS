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
using TransportationService.Api.Modules.Messaging.Configurations;
using TransportationService.Api.Modules.Messaging.Services;

var builder = WebApplication.CreateBuilder(args);

// Global request-body ceiling (L10): the server-wide default caps every endpoint; upload
// endpoints then narrow further with their own [RequestSizeLimit]. 32 MB comfortably covers the
// largest legitimate upload (10 MB documents) with headroom for multipart overhead.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 32 * 1024 * 1024);

// Controllers + enums als tekst in JSON
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<TransportationService.Api.Common.InvalidTenantReferenceExceptionFilter>();
        options.Filters.Add<TransportationService.Api.Common.DomainValidationExceptionFilter>();
        options.Filters.Add<TransportationService.Api.Common.NegativeStockConfirmationFilter>();
        // Stale dossier-version mutations → 409 with the current state (rebase contract).
        options.Filters.Add<TransportationService.Api.Modules.Dossiers.DossierVersionConflictExceptionFilter>();
        // Per-request session-revocation (security stamp) + mandatory-password-change enforcement,
        // applied centrally to every authenticated request.
        options.Filters.Add<TransportationService.Api.Modules.Identity.Authorization.AccountStateAuthorizationFilter>();
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()
        )
    );

// OpenAPI
builder.Services.AddOpenApi();

// Consistent RFC7807 error responses. Unhandled exceptions never expose stack traces, exception
// types or SQL/table details; the correlation id is the bridge to the server-side log.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions.Remove("exception");

        if (context.ProblemDetails.Status is StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "Er is een onverwachte fout opgetreden.";
            context.ProblemDetails.Detail =
                "De aanvraag kon niet worden verwerkt. Vermeld de correlatie-id bij een melding.";
        }
    };
});

// JWT authentication + authorization (password hashing, token + auth services)
builder.Services.AddJwtAuthentication(builder.Configuration);

// Anonymous auth endpoints (login, forgot/reset password, activation) are rate-limited per IP.
builder.Services.AddAuthRateLimiting();

// CORS: allowed origins come from configuration (Cors:AllowedOrigins), never a wildcard. In
// Development the local Vite ports are the fallback; outside Development an empty list means the
// policy allows nothing at all (fail closed) rather than silently opening up.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    corsOrigins = ["http://localhost:5173", "http://localhost:5174"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // The HttpOnly refresh cookie (H5) must travel on the auth endpoints; credentials
            // are only ever allowed together with the EXPLICIT origin list above, never "*".
            .AllowCredentials();
    });
});

// Trust forwarded headers ONLY from explicitly known proxies, so the rate limiter partitions on
// the real client IP instead of the proxy's (which would put every tenant in one bucket and make
// a login DoS trivial). An unlisted proxy is ignored �?" X-Forwarded-For is never blindly believed.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var proxy in builder.Configuration.GetSection("Network:KnownProxies").Get<string[]>() ?? [])
    {
        if (System.Net.IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var network in builder.Configuration.GetSection("Network:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/', 2);
        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
        }
    }
});

// Algemene services
builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContextAccessors();
// H1: ambient tenant for the DbContext's global query filter (null outside a resolved request).
builder.Services.AddSingleton<TransportationService.Api.Common.Persistence.ITenantQueryFilterAccessor,
    TransportationService.Api.Common.Persistence.HttpTenantQueryFilterAccessor>();
builder.Services.AddSingleton(TimeProvider.System);

// Cross-cutting persistence behaviour (audit stamps, soft delete, order status history)
builder.Services.AddSingleton<AuditingSaveChangesInterceptor>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Warehousing.Services.StorageClockInterceptor>();

// PostgreSQL + EF Core
builder.Services.AddDbContext<TransportationDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(
            serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<TransportationService.Api.Modules.Orders.Services.OrderStatusHistoryInterceptor>(),
            serviceProvider.GetRequiredService<TransportationService.Api.Modules.Warehousing.Services.StorageClockInterceptor>())
);

// Identity en permissions
builder.Services.AddScoped<
    IPermissionAuthorizationService,
    PermissionAuthorizationService
>();
builder.Services.AddScoped<IAccountSecurityService, AccountSecurityService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserAccountFlowService, UserAccountFlowService>();
builder.Services.AddScoped<IJobFunctionRoleMappingService, JobFunctionRoleMappingService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Employees
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IEmployeeCompletenessService,
    TransportationService.Api.Modules.Employees.Services.EmployeeCompletenessService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IEmployeeHistoryService, EmployeeHistoryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IEmployeeNoteService,
    TransportationService.Api.Modules.Employees.Services.EmployeeNoteService>();
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
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IInventoryAlertService,
    TransportationService.Api.Modules.Employees.Services.InventoryAlertService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.INegativeStockGuard,
    TransportationService.Api.Modules.Employees.Services.NegativeStockGuard>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IInventoryInsightsService,
    TransportationService.Api.Modules.Employees.Services.InventoryInsightsService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.IReorderProposalService,
    TransportationService.Api.Modules.Employees.Services.ReorderProposalService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tasks.Services.IEmployeeTaskService,
    TransportationService.Api.Modules.Tasks.Services.EmployeeTaskService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tasks.Services.ITaskTemplateService,
    TransportationService.Api.Modules.Tasks.Services.TaskTemplateService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tasks.Services.ITaskAttachmentService,
    TransportationService.Api.Modules.Tasks.Services.TaskAttachmentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Notifications.Services.IEscalationPolicyService,
    TransportationService.Api.Modules.Notifications.Services.EscalationPolicyService>();

// Sprint sweeps (inventory status/returns/escalations, tasks/recurrence, notification retention).
builder.Services.AddScoped<TransportationService.Api.Modules.Employees.Services.InventorySweepWorker>();
builder.Services.AddScoped<TransportationService.Api.Modules.Tasks.Services.TaskSweepWorker>();
builder.Services.AddScoped<TransportationService.Api.Modules.Notifications.Services.NotificationMaintenanceWorker>();
builder.Services.AddHostedService<TransportationService.Api.Modules.Employees.Services.InventorySweepHostedService>();
builder.Services.AddHostedService<TransportationService.Api.Modules.Tasks.Services.TaskSweepHostedService>();
builder.Services.AddHostedService<TransportationService.Api.Modules.Notifications.Services.NotificationMaintenanceHostedService>();

// GDPR (H13): configurable retention + daily purge sweep, and data-subject export/anonymisation.
builder.Services.Configure<TransportationService.Api.Modules.Gdpr.RetentionOptions>(
    builder.Configuration.GetSection(TransportationService.Api.Modules.Gdpr.RetentionOptions.SectionName));
builder.Services.AddHostedService<TransportationService.Api.Modules.Gdpr.GdprRetentionHostedService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Gdpr.IDataSubjectService,
    TransportationService.Api.Modules.Gdpr.DataSubjectService>();

// Upload malware-scan seam (L10): pass-through until a real engine is attached — see
// docs/security/operational-checklist.md #18.
builder.Services.AddSingleton<TransportationService.Api.Modules.Security.IUploadScanner,
    TransportationService.Api.Modules.Security.PassThroughUploadScanner>();

// File storage (all uploaded documents funnel through this chokepoint)
builder.Services.AddSingleton<IFileStorageService>(serviceProvider =>
    new LocalFileStorageService(
        Path.Combine(
            builder.Environment.ContentRootPath,
            "App_Data"
        ),
        serviceProvider.GetRequiredService<TransportationService.Api.Modules.Security.IUploadScanner>()
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
// Sprint 3: the contact-centric layer over the same communication rules.
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerContactSubscriptionService,
    TransportationService.Api.Modules.Partners.Services.CustomerContactSubscriptionService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerBillingConfigService,
    TransportationService.Api.Modules.Partners.Services.CustomerBillingConfigService>();
// Official company-registry seam: no provider configured by default (no scraping).
builder.Services.AddSingleton<TransportationService.Api.Modules.Partners.Services.ICompanyRegistryProvider,
    TransportationService.Api.Modules.Partners.Services.NullCompanyRegistryProvider>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerService,
    TransportationService.Api.Modules.Partners.Services.CustomerService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerHistoryService,
    TransportationService.Api.Modules.Partners.Services.CustomerHistoryService>();

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
// Sprint 2: the customer ↔ physical address relationship (link/unlink, duplicate detection).
builder.Services.AddScoped<TransportationService.Api.Modules.Locations.Services.ICustomerAddressService,
    TransportationService.Api.Modules.Locations.Services.CustomerAddressService>();
// Pure, stateless opening-hours check — singleton by design.
builder.Services.AddSingleton<TransportationService.Api.Modules.Locations.Services.IOpeningHoursEvaluator,
    TransportationService.Api.Modules.Locations.Services.OpeningHoursEvaluator>();

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
builder.Services.AddScoped<TransportationService.Api.Modules.Dossiers.Services.IActivityTypeSeeder,
    TransportationService.Api.Modules.Dossiers.Services.ActivityTypeSeeder>();
builder.Services.AddScoped<TransportationService.Api.Modules.Dossiers.Services.IDossierActivityService,
    TransportationService.Api.Modules.Dossiers.Services.DossierActivityService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Dossiers.Services.IDossierReadinessService,
    TransportationService.Api.Modules.Dossiers.Services.DossierReadinessService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Dossiers.Services.IActivityTypeService,
    TransportationService.Api.Modules.Dossiers.Services.ActivityTypeService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Incidents.Services.IIncidentService,
    TransportationService.Api.Modules.Incidents.Services.IncidentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Incidents.Services.IFailedDeliveryHandler,
    TransportationService.Api.Modules.Incidents.Services.FailedDeliveryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.OrderImport.Services.IOrderImportService,
    TransportationService.Api.Modules.OrderImport.Services.OrderImportService>();

// Settings/system wave 2026-08: Systeeminformatie + back-upbeheer.
builder.Services.Configure<TransportationService.Api.Modules.SystemAdmin.Services.BackupOptions>(
    builder.Configuration.GetSection(TransportationService.Api.Modules.SystemAdmin.Services.BackupOptions.SectionName));
builder.Services.AddScoped<TransportationService.Api.Modules.SystemAdmin.Services.ISystemInfoService,
    TransportationService.Api.Modules.SystemAdmin.Services.SystemInfoService>();
builder.Services.AddSingleton<TransportationService.Api.Modules.SystemAdmin.Services.IBackupProcessRunner,
    TransportationService.Api.Modules.SystemAdmin.Services.ProcessBackupRunner>();
builder.Services.AddScoped<TransportationService.Api.Modules.SystemAdmin.Services.IBackupService,
    TransportationService.Api.Modules.SystemAdmin.Services.BackupService>();
builder.Services.AddHostedService<TransportationService.Api.Modules.SystemAdmin.Services.AutomaticBackupHostedService>();

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
builder.Services.AddScoped<TransportationService.Api.Modules.Warehousing.Services.IWarehouseTraceService,
    TransportationService.Api.Modules.Warehousing.Services.WarehouseTraceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Warehousing.Services.IStorageBillingService,
    TransportationService.Api.Modules.Warehousing.Services.StorageBillingService>();
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
// Sprint 6: moving an order to the customer it really belongs to.
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.IOrderCustomerChangeService,
    TransportationService.Api.Modules.Orders.Services.OrderCustomerChangeService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderTimelineService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderTimelineService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.ICustomerPortalService,
    TransportationService.Api.Modules.CustomerPortal.Services.CustomerPortalService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.ICustomerPortalUserService,
    TransportationService.Api.Modules.CustomerPortal.Services.CustomerPortalUserService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.ICustomerMessageService,
    TransportationService.Api.Modules.CustomerPortal.Services.CustomerMessageService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IPortalAnnouncementService,
    TransportationService.Api.Modules.CustomerPortal.Services.PortalAnnouncementService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IPortalMessageService,
    TransportationService.Api.Modules.CustomerPortal.Services.PortalMessageService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IPortalDashboardService,
    TransportationService.Api.Modules.CustomerPortal.Services.PortalDashboardService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IPortalInvoiceService,
    TransportationService.Api.Modules.CustomerPortal.Services.PortalInvoiceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IPortalDocumentService,
    TransportationService.Api.Modules.CustomerPortal.Services.PortalDocumentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Invoicing.Services.IInvoicePdfService,
    TransportationService.Api.Modules.Invoicing.Services.InvoicePdfService>();
builder.Services.AddScoped<TransportationService.Api.Modules.CustomerPortal.Services.IOrderPortalReviewService,
    TransportationService.Api.Modules.CustomerPortal.Services.OrderPortalReviewService>();

// Planning (trips + conflict engine)
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IPlanningConflictService,
    TransportationService.Api.Modules.Planning.Services.PlanningConflictService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.ITripService,
    TransportationService.Api.Modules.Planning.Services.TripService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IPlanningProposalService,
    TransportationService.Api.Modules.Planning.Services.PlanningProposalService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportDocumentService,
    TransportationService.Api.Modules.Orders.Services.TransportDocumentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Invoicing.Services.IInvoiceControlService,
    TransportationService.Api.Modules.Invoicing.Services.InvoiceControlService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.ITripExecutionService,
    TransportationService.Api.Modules.Planning.Services.TripExecutionService>();

// Scanning
builder.Services.AddScoped<TransportationService.Api.Modules.Scanning.Services.IScanService,
    TransportationService.Api.Modules.Scanning.Services.ScanService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Scanning.Services.IWarehouseScanService,
    TransportationService.Api.Modules.Scanning.Services.WarehouseScanService>();

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

// Time & attendance (urenregistratie + prikklokken)
builder.Services.AddSingleton<TransportationService.Api.Modules.Attendance.Security.IAttendancePinHasher,
    TransportationService.Api.Modules.Attendance.Security.AttendancePinHasher>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Attendance.Services.IKioskInteractionTokenStore,
    TransportationService.Api.Modules.Attendance.Services.KioskInteractionTokenStore>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceCorrectionService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceCorrectionService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceOverviewService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceOverviewService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceReportService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceReportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceExportService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceExportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceSettingsService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceSettingsService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IAttendanceCredentialService,
    TransportationService.Api.Modules.Attendance.Services.AttendanceCredentialService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IKioskDeviceService,
    TransportationService.Api.Modules.Attendance.Services.KioskDeviceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.IKioskPunchService,
    TransportationService.Api.Modules.Attendance.Services.KioskPunchService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Attendance.Services.AttendanceSweepWorker>();
builder.Services.AddHostedService<TransportationService.Api.Modules.Attendance.Services.AttendanceSweepHostedService>();

// Trip costing: effective-dated rate cards + estimated/actual/final cost engine
builder.Services.AddScoped<TransportationService.Api.Modules.TripCosting.Services.ICostRateService,
    TransportationService.Api.Modules.TripCosting.Services.CostRateService>();
builder.Services.AddScoped<TransportationService.Api.Modules.TripCosting.Services.ITripCostingService,
    TransportationService.Api.Modules.TripCosting.Services.TripCostingService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IKpiQueryService,
    TransportationService.Api.Modules.Reporting.Services.KpiQueryService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IKpiExportService,
    TransportationService.Api.Modules.Reporting.Services.KpiExportService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Reporting.Services.IActivityKpiService,
    TransportationService.Api.Modules.Reporting.Services.ActivityKpiService>();

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

// Messaging (provider-neutral email/SMS): outbox, dispatcher.
// The DevelopmentSinkProvider writes rendered messages (including invite/activation links with
// raw tokens) to App_Data/message-sink, so it is registered ONLY in Development. Outside
// Development a fail-closed placeholder is registered and StartupSecurityValidator refuses to
// boot until a real provider exists — raw tokens can never leak via a production sink.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<DevelopmentSinkProvider>(_ =>
        new DevelopmentSinkProvider(
            Path.Combine(builder.Environment.ContentRootPath, "App_Data", "message-sink")));

    builder.Services.AddSingleton<IEmailProvider>(sp =>
        sp.GetRequiredService<DevelopmentSinkProvider>());

    builder.Services.AddSingleton<ISmsProvider>(sp =>
        sp.GetRequiredService<DevelopmentSinkProvider>());
}
else
{
    var smtpSection = builder.Configuration.GetSection(SmtpOptions.SectionName);
    var smtpConfigured = !string.IsNullOrWhiteSpace(smtpSection["Host"]);

    if (smtpConfigured)
    {
        builder.Services
            .AddOptions<SmtpOptions>()
            .Bind(smtpSection)
            .ValidateDataAnnotations()
            .Validate(
                options => options.UseTls,
                "Email:Smtp:UseTls moet true zijn voor deze productieconfiguratie.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
    }
    else
    {
        builder.Services.AddSingleton<IEmailProvider, UnconfiguredEmailProvider>();
    }

    builder.Services.AddSingleton<ISmsProvider, UnconfiguredSmsProvider>();
}

builder.Services.AddScoped<TransportationService.Api.Modules.Messaging.Services.IMessageOutboxService,
    TransportationService.Api.Modules.Messaging.Services.MessageOutboxService>();
builder.Services.AddScoped(sp => new TransportationService.Api.Modules.Messaging.Services.MessageDispatcher(
    sp.GetRequiredService<TransportationService.Api.Data.TransportationDbContext>(),
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.IEmailProvider>(),
    sp.GetRequiredService<TransportationService.Api.Modules.Messaging.Services.ISmsProvider>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<TransportationService.Api.Modules.Messaging.Services.OutboxDispatcherHostedService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Messaging.Services.INotificationEventService,
    TransportationService.Api.Modules.Messaging.Services.NotificationEventService>();

// EDI foundation (generic-json-v1 profile; partner formats plug in as profiles later)
builder.Services.AddScoped<TransportationService.Api.Modules.Edi.Services.IEdiService,
    TransportationService.Api.Modules.Edi.Services.EdiService>();

// Peppol (provider-neutral; only the deterministic sandbox Access Point adapter is registered �?"
// a real provider plugs in as an extra IPeppolProvider + configuration, never code changes here)
builder.Services.AddSingleton<TransportationService.Api.Modules.Peppol.Services.IPeppolProvider,
    TransportationService.Api.Modules.Peppol.Services.SandboxPeppolProvider>();
builder.Services.AddSingleton<TransportationService.Api.Modules.Peppol.Services.IPeppolProviderFactory,
    TransportationService.Api.Modules.Peppol.Services.PeppolProviderFactory>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolSettingsService,
    TransportationService.Api.Modules.Peppol.Services.PeppolSettingsService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolCustomerVerificationService,
    TransportationService.Api.Modules.Peppol.Services.PeppolCustomerVerificationService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolInvoiceService,
    TransportationService.Api.Modules.Peppol.Services.PeppolInvoiceService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolTransmissionService,
    TransportationService.Api.Modules.Peppol.Services.PeppolTransmissionService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolIncomingService,
    TransportationService.Api.Modules.Peppol.Services.PeppolIncomingService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Peppol.Services.IPeppolWebhookService,
    TransportationService.Api.Modules.Peppol.Services.PeppolWebhookService>();
builder.Services.AddHostedService<TransportationService.Api.Modules.Peppol.Services.PeppolDispatcherHostedService>();

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

// Column-encryption key ring (Fase 9): keys come from env/vault, never from the repo. Without a
// key the layer is pass-through — the validator below refuses that outside Development.
TransportationService.Api.Modules.Security.ColumnEncryption.Initialize(
    app.Configuration["ColumnEncryption:Key"],
    app.Configuration.GetSection("ColumnEncryption:PreviousKeys").Get<string[]>());

// Fail-fast on production-unsafe configuration (impersonation headers enabled, no real mail
// provider, etc.) before the host starts serving.
TransportationService.Api.Modules.Security.StartupSecurityValidator.Validate(
    app.Environment, app.Configuration, app.Services);

// Global reference data runs in every environment: the ISO country list must exist for
// country validation and comboboxes regardless of how tenants are onboarded.
using (var referenceScope = app.Services.CreateScope())
{
    var referenceDbContext = referenceScope.ServiceProvider.GetRequiredService<TransportationDbContext>();
    await CountrySeeder.SyncAsync(referenceDbContext);

    // Dossier foundation: idempotently wrap pre-wave orders in their own dossier so the
    // dossier list covers ALL historical work. Cheap after the first run (one indexed
    // query per tenant returning nothing).
    await TransportationService.Api.Modules.Dossiers.Services.DossierBackfillSeeder.SyncAsync(
        referenceDbContext, app.Logger);

    // Wave 2: typed coverage roll-up for pre-wave pricing snapshots (idempotent, indexed-only).
    await TransportationService.Api.Modules.Orders.Services.CoverageStatusBackfillSeeder.SyncAsync(
        referenceDbContext);

    // Sprint 2 (central address master): derive the address duplicate-detection keys and turn
    // legacy Location.CustomerId ownership into customer↔address relationships. Idempotent;
    // the legacy columns are left untouched.
    await TransportationService.Api.Modules.Locations.Services.AddressMasterBackfillSeeder.SyncAsync(
        referenceDbContext);
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

    // Fictional demo master data, only on explicit request:
    //   dotnet run --project TransportationService.Api -- --seed-demo
    if (args.Contains(DevDemoDataSeeder.CommandLineFlag))
    {
        await DevDemoDataSeeder.SeedAsync(dbContext, app.Logger);
    }
}

// Security pipeline order (documented deliberately):
//  1. forwarded headers  -> the real client IP/scheme must be known before anything keys off it
//  2. exception handler  -> catches everything downstream, emits ProblemDetails without internals
//  3. security headers   -> stamped on every response, including error responses
//  4. HTTPS/HSTS         -> transport hardening in real environments
//  5. CORS               -> before auth so preflights are answered correctly
//  6. authentication     -> populates HttpContext.User
//  7. rate limiting      -> after auth so policies can key on the authenticated identity
//  8. tenant context     -> derives tenant/user from the validated principal
//  9. authorization      -> fallback policy + permission filters + account-state filter
// 10. endpoints
app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseMiddleware<TransportationService.Api.Modules.Security.SecurityHeadersMiddleware>();

// Enforce HTTPS in real environments. In Development the SPA talks to the API over http
// (http://localhost:5019); redirecting to https there would drop the Authorization header on the
// cross-scheme 307, so redirection is intentionally skipped for local development only.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();

app.UseRateLimiter();

app.UseMiddleware<TenantContextMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();