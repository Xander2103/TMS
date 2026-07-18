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
        options.Filters.Add<TransportationService.Api.Common.InvalidTenantReferenceExceptionFilter>())
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
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Algemene services
builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContextAccessors();
builder.Services.AddSingleton(TimeProvider.System);

// Cross-cutting persistence behaviour (audit stamps, soft delete)
builder.Services.AddSingleton<AuditingSaveChangesInterceptor>();

// PostgreSQL + EF Core
builder.Services.AddDbContext<TransportationDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>())
);

// Identity en permissions
builder.Services.AddScoped<
    IPermissionAuthorizationService,
    PermissionAuthorizationService
>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();

// Employees
builder.Services.AddScoped<IEmployeeService, EmployeeService>();

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

// Generic tenant lookup CRUD (departments, functions, categories, reference data, ...)
builder.Services.AddScoped(typeof(TransportationService.Api.Common.Lookups.ILookupService<>),
    typeof(TransportationService.Api.Common.Lookups.LookupService<>));

// Partners
builder.Services.AddScoped<TransportationService.Api.Modules.Partners.Services.ICustomerService,
    TransportationService.Api.Modules.Partners.Services.CustomerService>();

// Drivers
builder.Services.AddScoped<TransportationService.Api.Modules.Drivers.Services.IDriverService,
    TransportationService.Api.Modules.Drivers.Services.DriverService>();

// Company / tenant settings
builder.Services.AddScoped<TransportationService.Api.Modules.Tenancy.Services.ICompanySettingsService,
    TransportationService.Api.Modules.Tenancy.Services.CompanySettingsService>();

// Locations
builder.Services.AddScoped<TransportationService.Api.Modules.Locations.Services.ILocationService,
    TransportationService.Api.Modules.Locations.Services.LocationService>();

// Fleet
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IVehicleService,
    TransportationService.Api.Modules.Fleet.Services.VehicleService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.ITrailerService,
    TransportationService.Api.Modules.Fleet.Services.TrailerService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IFleetDocumentService,
    TransportationService.Api.Modules.Fleet.Services.FleetDocumentService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Fleet.Services.IMaintenanceService,
    TransportationService.Api.Modules.Fleet.Services.MaintenanceService>();
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

// HR availability
builder.Services.AddScoped<TransportationService.Api.Modules.Hr.Services.IAbsenceService,
    TransportationService.Api.Modules.Hr.Services.AbsenceService>();

// Transport orders
builder.Services.AddScoped<TransportationService.Api.Modules.Orders.Services.ITransportOrderService,
    TransportationService.Api.Modules.Orders.Services.TransportOrderService>();

// Planning (trips + conflict engine)
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.IPlanningConflictService,
    TransportationService.Api.Modules.Planning.Services.PlanningConflictService>();
builder.Services.AddScoped<TransportationService.Api.Modules.Planning.Services.ITripService,
    TransportationService.Api.Modules.Planning.Services.TripService>();

var app = builder.Build();

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