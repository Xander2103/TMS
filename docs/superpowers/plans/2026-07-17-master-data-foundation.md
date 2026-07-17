# Master Data Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:test-driven-development
> for business-rule code (eligibility evaluator, admin safeguards, tenant
> isolation). This plan is executed **inline, in the same session, by the
> same agent that wrote it** (not handed to a fresh subagent per task) —
> see "Execution mode" below. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Build the Users/Roles/Permissions, Employees, Qualifications,
Eligibility engine, Overrides, and Audit Logging backend + a matching
React admin UI, per `docs/superpowers/specs/2026-07-17-master-data-foundation-design.md`.

**Architecture:** Modular monolith inside `TransportationService.Api`
under `Modules/{Tenancy,Identity,Employees,Qualifications,Eligibility,Auditing}`,
each with `Entities/`, `Dtos/`, `Services/`, `Configurations/`,
`Controllers/`. Pure business-rule core (eligibility evaluator) kept
dependency-free for unit testing. New `TransportationService.Api.Tests`
xUnit project using SQLite in-memory for DB-backed tests.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core 10 / Npgsql (runtime),
Sqlite in-memory (tests), xUnit, FluentAssertions-free (use xUnit
`Assert`, no new assertion library — YAGNI), React 19 / TS / Vite /
React Router 7 (existing).

## Global Constraints

- TenantId is never accepted from the client; always from `ITenantContext`.
- Every tenant-owned query/update/delete is filtered by `TenantId`.
- No business logic in controllers — controllers call services only.
- No hardcoded role-name checks — authorization is by permission code via `RequirePermissionAttribute`.
- DTOs only across the API boundary — never expose EF entities directly.
- Async + `CancellationToken` on every DB-touching method.
- Soft-deactivate (`IsActive = false`), never hard-delete, for Users/Roles/Employees/Qualifications.
- No secrets committed; no real password seeded.
- `dotnet build`, `dotnet test`, frontend `tsc -b`, `eslint`, `vite build` must all pass before this plan is considered done.

## Execution mode

Chosen per the commissioning brief's "choose the safest effective
execution mode yourself": **inline execution**, not
subagent-driven-development. Rationale: nearly every task in Phases 1-7
touches the same shared files (`TransportationDbContext.cs`,
`Program.cs`, `DatabaseSeeder.cs`) and contributes to one consolidated
migration. Parallel subagents editing those shared files concurrently
would produce merge conflicts and a high risk of an inconsistent EF
model — unacceptable given tenant-isolation correctness is priority #1.
Sequential inline execution with a commit after every task keeps the
model consistent and gives a clean rollback point per task.

Given this plan is executed by the same agent immediately after writing
it, task steps below specify **exact entities, field lists, method
signatures, routes, and permission codes** (so nothing is ambiguous or
invented differently at implementation time) but do not re-embed full
verbatim file contents for straightforward CRUD DTOs/controllers — that
would duplicate, not aid, the work. Full code is given for the
non-obvious/business-critical pieces: tenant/user context resolution,
permission enforcement, admin safeguards, and the eligibility evaluator.

---

## File Structure

```
TransportationService.Api/
  Modules/
    Tenancy/
      Entities/Tenant.cs, TenantSettings.cs
      Dtos/ (none needed yet — internal only)
      Services/ITenantContext.cs, DevTenantContext.cs, ITenantSettingsService.cs, TenantSettingsService.cs
      Configurations/TenantConfiguration.cs, TenantSettingsConfiguration.cs
      TenantContextMiddleware.cs
    Identity/
      Entities/User.cs, Role.cs, Permission.cs, UserRole.cs, RolePermission.cs
      Dtos/UserDtos.cs, RoleDtos.cs, PermissionDtos.cs
      Services/ICurrentUserContext.cs, DevCurrentUserContext.cs,
               IPermissionAuthorizationService.cs, PermissionAuthorizationService.cs,
               IUserService.cs, UserService.cs,
               IRoleService.cs, RoleService.cs
      Configurations/UserConfiguration.cs, RoleConfiguration.cs, PermissionConfiguration.cs,
                     UserRoleConfiguration.cs, RolePermissionConfiguration.cs
      Authorization/RequirePermissionAttribute.cs
      Controllers/UsersController.cs, RolesController.cs, PermissionsController.cs
      PermissionCodes.cs (static class of const strings)
    Employees/
      Entities/Employee.cs, EmploymentStatus.cs, EmployeeFunction.cs
      Dtos/EmployeeDtos.cs
      Services/IEmployeeService.cs, EmployeeService.cs
      Configurations/EmployeeConfiguration.cs
      Controllers/EmployeesController.cs
    Qualifications/
      Entities/QualificationType.cs, EmployeeQualification.cs, QualificationStatus.cs
      Dtos/QualificationDtos.cs
      Services/IQualificationService.cs, QualificationService.cs,
               IQualificationStatusCalculator.cs, QualificationStatusCalculator.cs,
               IFileStorageService.cs, LocalFileStorageService.cs
      Configurations/QualificationTypeConfiguration.cs, EmployeeQualificationConfiguration.cs
      Controllers/QualificationsController.cs
    Eligibility/
      Models/EligibilityRequirement.cs, EligibilityResult.cs, QualificationSnapshot.cs, DriverEligibilityRequest.cs
      Services/DriverEligibilityEvaluator.cs, IDriverEligibilityService.cs, DriverEligibilityService.cs
      Entities/EligibilityOverride.cs
      Dtos/EligibilityDtos.cs, OverrideDtos.cs
      Services/IEligibilityOverrideService.cs, EligibilityOverrideService.cs
      Configurations/EligibilityOverrideConfiguration.cs
      Controllers/DriverEligibilityController.cs, EligibilityOverridesController.cs
    Auditing/
      Entities/AuditLog.cs
      Services/IAuditService.cs, AuditService.cs
      Configurations/AuditLogConfiguration.cs
      Controllers/AuditLogsController.cs
  Data/
    TransportationDbContext.cs (modified: add DbSets, apply configurations)
    DatabaseSeeder.cs (renamed from TransportOrderSeeder usage site in Program.cs stays; new seeder added alongside)
  Program.cs (modified: DI registrations, middleware, seeding calls)
  Migrations/ (new migration)

TransportationService.Api.Tests/
  TransportationService.Api.Tests.csproj
  Eligibility/DriverEligibilityEvaluatorTests.cs
  Identity/AdminSafeguardTests.cs
  Identity/PermissionAssignmentTests.cs
  Employees/EmployeeWithoutUserTests.cs
  TenantIsolation/TenantIsolationTests.cs
  TestSupport/SqliteTestDbContextFactory.cs

TransportationService.Web/src/
  features/users/{api,hooks,pages,components,types}
  features/roles/{api,hooks,pages,components,types}
  features/employees/{api,hooks,pages,components,types}
  components/layout/Sidebar.tsx (modified: Master Data group)
  routes/AppRoutes.tsx (modified: new routes)
  config/env.ts (modified: dev tenant/user id)
  api/apiClient.ts (modified: attach dev headers)
```

---

## Task 0: Test project scaffold

**Files:**
- Create: `TransportationService.Api.Tests/TransportationService.Api.Tests.csproj`
- Create: `TransportationService.Api.Tests/TestSupport/SqliteTestDbContextFactory.cs`
- Modify: `TransportationService.slnx` (add test project)

**Interfaces:**
- Produces: `SqliteTestDbContextFactory.Create() -> TransportationDbContext` — opens a
  `SqliteConnection("DataSource=:memory:")`, keeps it open, calls
  `EnsureCreated()`, returns a context backed by it. Caller is responsible
  for disposing both (factory returns a `(TransportationDbContext Context, SqliteConnection Connection)` tuple so tests can dispose both in `Dispose()`).

- [ ] **Step 1: Create the test project**

```bash
cd TransportationService.Api.Tests 2>/dev/null || mkdir TransportationService.Api.Tests
cd TransportationService.Api.Tests
dotnet new xunit -n TransportationService.Api.Tests -o .
dotnet add reference ../TransportationService.Api/TransportationService.Api.csproj
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Add the project to the solution**

```bash
cd ..
dotnet sln TransportationService.slnx add TransportationService.Api.Tests/TransportationService.Api.Tests.csproj
```

- [ ] **Step 3: Write `SqliteTestDbContextFactory`**

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Tests.TestSupport;

public sealed class SqliteTestDbContext : IDisposable
{
    public TransportationDbContext Context { get; }
    private readonly SqliteConnection _connection;

    public SqliteTestDbContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransportationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new TransportationDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build TransportationService.slnx`
Expected: Build succeeded (no tests exist yet, that's fine).

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api.Tests TransportationService.slnx
git commit -m "test: scaffold xUnit test project with SQLite in-memory support"
```

---

## Task 1: Tenancy — Tenant, TenantSettings, ITenantContext, middleware

**Files:**
- Create: `TransportationService.Api/Modules/Tenancy/Entities/Tenant.cs`
- Create: `TransportationService.Api/Modules/Tenancy/Entities/TenantSettings.cs`
- Create: `TransportationService.Api/Modules/Tenancy/Services/ITenantContext.cs`
- Create: `TransportationService.Api/Modules/Tenancy/Services/DevTenantContext.cs`
- Create: `TransportationService.Api/Modules/Tenancy/Configurations/TenantConfiguration.cs`
- Create: `TransportationService.Api/Modules/Tenancy/Configurations/TenantSettingsConfiguration.cs`
- Create: `TransportationService.Api/Modules/Tenancy/TenantContextMiddleware.cs`
- Modify: `TransportationService.Api/Data/TransportationDbContext.cs`
- Modify: `TransportationService.Api/Program.cs`
- Test: `TransportationService.Api.Tests/TenantIsolation/TenantContextTests.cs`

**Interfaces:**
- Produces: `ITenantContext { Guid TenantId { get; } }`, resolved per-request.
- Produces: `Tenant { Guid Id; string Name; string Slug; bool IsActive; DateTime CreatedAt; }`
- Produces: `TenantSettings { Guid Id; Guid TenantId; string Timezone; string DefaultLanguage; int QualificationExpiryWarningDays; int DefaultPageSize; string? EmployeeNumberPrefix; int EmployeeNumberNextValue; string EnabledModulesJson; }`

```csharp
// Modules/Tenancy/Entities/Tenant.cs
namespace TransportationService.Api.Modules.Tenancy.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
```

```csharp
// Modules/Tenancy/Entities/TenantSettings.cs
namespace TransportationService.Api.Modules.Tenancy.Entities;

public class TenantSettings
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Timezone { get; set; } = "Europe/Amsterdam";
    public string DefaultLanguage { get; set; } = "nl";
    public int QualificationExpiryWarningDays { get; set; } = 30;
    public int DefaultPageSize { get; set; } = 25;
    public string? EmployeeNumberPrefix { get; set; }
    public int EmployeeNumberNextValue { get; set; } = 1;

    /// <summary>JSON-serialized <see cref="TenantModuleFlags"/>. Never read/written raw — always via ITenantSettingsService.</summary>
    public string EnabledModulesJson { get; set; } = "{}";
}

public record TenantModuleFlags(
    bool Employees = true,
    bool Qualifications = true,
    bool Eligibility = true,
    bool Overrides = true,
    bool AuditLog = true);
```

```csharp
// Modules/Tenancy/Services/ITenantContext.cs
namespace TransportationService.Api.Modules.Tenancy.Services;

public interface ITenantContext
{
    Guid TenantId { get; }
}
```

```csharp
// Modules/Tenancy/Services/DevTenantContext.cs
namespace TransportationService.Api.Modules.Tenancy.Services;

/// <summary>
/// Development-only tenant resolution. Reads the trusted X-Dev-Tenant-Id
/// header set by TenantContextMiddleware. Replace with a claims-based
/// implementation once JWT authentication issues a tenant_id claim —
/// no consuming service should need to change.
/// </summary>
public class DevTenantContext : ITenantContext
{
    public Guid TenantId { get; }

    public DevTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
```

```csharp
// Modules/Tenancy/TenantContextMiddleware.cs
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tenancy;

/// <summary>
/// Resolves the current tenant and (dev-mode) current user from trusted
/// development headers and registers scoped ITenantContext / ICurrentUserContext
/// instances for the rest of the request pipeline. This is the single seam
/// to replace when real authentication (JWT claims) is introduced.
/// </summary>
public class TenantContextMiddleware
{
    public const string TenantHeaderName = "X-Dev-Tenant-Id";
    public const string UserHeaderName = "X-Dev-User-Id";

    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, TransportationDbContext dbContext)
    {
        var tenantId = await ResolveTenantIdAsync(context, dbContext);
        var userId = ResolveUserId(context);

        context.Items[nameof(ITenantContext)] = new DevTenantContext(tenantId);
        context.Items[nameof(ICurrentUserContext)] = new DevCurrentUserContext(userId);

        await _next(context);
    }

    private static async Task<Guid> ResolveTenantIdAsync(HttpContext context, TransportationDbContext dbContext)
    {
        if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed))
        {
            return parsed;
        }

        var defaultTenantId = await dbContext.Tenants
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .FirstOrDefaultAsync();

        return defaultTenantId;
    }

    private static Guid? ResolveUserId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(UserHeaderName, out var headerValue)
            && Guid.TryParse(headerValue, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

public static class TenantContextServiceCollectionExtensions
{
    public static IServiceCollection AddTenantContextAccessors(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext>(sp =>
            (ITenantContext?)sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[nameof(ITenantContext)]
            ?? throw new InvalidOperationException("Tenant context was not resolved. Ensure TenantContextMiddleware runs before this service is used."));

        services.AddScoped<ICurrentUserContext>(sp =>
            (ICurrentUserContext?)sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[nameof(ICurrentUserContext)]
            ?? throw new InvalidOperationException("Current user context was not resolved. Ensure TenantContextMiddleware runs before this service is used."));

        return services;
    }
}
```

- [ ] **Step 1: Write `TenantConfiguration` / `TenantSettingsConfiguration`**

```csharp
// Modules/Tenancy/Configurations/TenantConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Modules.Tenancy.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
```

```csharp
// Modules/Tenancy/Configurations/TenantSettingsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Tenancy.Entities;

namespace TransportationService.Api.Modules.Tenancy.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Timezone).IsRequired().HasMaxLength(100);
        builder.Property(s => s.DefaultLanguage).IsRequired().HasMaxLength(10);
        builder.Property(s => s.EmployeeNumberPrefix).HasMaxLength(20);
        builder.Property(s => s.EnabledModulesJson).IsRequired();
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithOne().HasForeignKey<TenantSettings>(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Wire into `TransportationDbContext`**

Modify `OnModelCreating` to call `modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationDbContext).Assembly)` instead of the manual `TransportOrder` block (keep the `TransportOrder` config as its own `IEntityTypeConfiguration<TransportOrder>` moved into `Data/Configurations/TransportOrderConfiguration.cs` so the switch to `ApplyConfigurationsFromAssembly` doesn't drop it — this is the one small refactor of existing code required to unblock the new configs). Add `DbSet<Tenant> Tenants` and `DbSet<TenantSettings> TenantSettings`.

- [ ] **Step 3: Register middleware + accessors in `Program.cs`**

Add `builder.Services.AddHttpContextAccessor();` and
`builder.Services.AddTenantContextAccessors();` before `builder.Build()`,
and `app.UseMiddleware<TenantContextMiddleware>();` immediately after
`app.UseCors("Frontend")` and before `app.UseAuthorization()`.

- [ ] **Step 4: Write test for tenant resolution fallback**

```csharp
// TenantIsolation/TenantContextTests.cs
using TransportationService.Api.Modules.Tenancy.Services;
using Xunit;

namespace TransportationService.Api.Tests.TenantIsolation;

public class TenantContextTests
{
    [Fact]
    public void DevTenantContext_ExposesConfiguredTenantId()
    {
        var tenantId = Guid.NewGuid();
        var context = new DevTenantContext(tenantId);

        Assert.Equal(tenantId, context.TenantId);
    }
}
```

- [ ] **Step 5: Build and run**

Run: `dotnet build TransportationService.slnx && dotnet test TransportationService.slnx`
Expected: Build succeeded, new test passes.

- [ ] **Step 6: Commit**

```bash
git add TransportationService.Api/Modules/Tenancy TransportationService.Api/Data/TransportationDbContext.cs TransportationService.Api/Program.cs TransportationService.Api.Tests
git commit -m "feat: add tenant context resolution and TenantSettings foundation"
```

---

## Task 2: Identity entities + EF configuration

**Files:**
- Create: `Modules/Identity/Entities/User.cs`, `Role.cs`, `Permission.cs`, `UserRole.cs`, `RolePermission.cs`
- Create: `Modules/Identity/Configurations/*.cs` (5 files, one per entity)
- Create: `Modules/Identity/PermissionCodes.cs`
- Modify: `Data/TransportationDbContext.cs`

**Interfaces:**

```csharp
// Modules/Identity/Entities/User.cs
namespace TransportationService.Api.Modules.Identity.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? CustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];
}
```

```csharp
// Modules/Identity/Entities/Role.cs
namespace TransportationService.Api.Modules.Identity.Entities;

public class Role
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<RolePermission> RolePermissions { get; set; } = [];
    public List<UserRole> UserRoles { get; set; } = [];
}
```

```csharp
// Modules/Identity/Entities/Permission.cs
namespace TransportationService.Api.Modules.Identity.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
```

```csharp
// Modules/Identity/Entities/UserRole.cs
namespace TransportationService.Api.Modules.Identity.Entities;

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
}
```

```csharp
// Modules/Identity/Entities/RolePermission.cs
namespace TransportationService.Api.Modules.Identity.Entities;

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}
```

```csharp
// Modules/Identity/PermissionCodes.cs
namespace TransportationService.Api.Modules.Identity;

public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersBlock = "users.block";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string RolesManagePermissions = "roles.manage_permissions";

    public const string EmployeesView = "employees.view";
    public const string EmployeesCreate = "employees.create";
    public const string EmployeesEdit = "employees.edit";
    public const string EmployeesDeactivate = "employees.deactivate";
    public const string EmployeesViewConfidential = "employees.view_confidential";

    public const string EmployeeDocumentsView = "employee_documents.view";
    public const string EmployeeDocumentsCreate = "employee_documents.create";
    public const string EmployeeDocumentsEdit = "employee_documents.edit";
    public const string EmployeeDocumentsDelete = "employee_documents.delete";
    public const string EmployeeDocumentsApprove = "employee_documents.approve";

    public const string PlanningView = "planning.view";
    public const string PlanningCreate = "planning.create";
    public const string PlanningEdit = "planning.edit";
    public const string PlanningOverrideRestriction = "planning.override_restriction";

    public const string AuditLogsView = "audit_logs.view";

    public static readonly IReadOnlyList<(string Code, string Module, string Action, string Description)> All =
    [
        (UsersView, "users", "view", "Gebruikers bekijken"),
        (UsersCreate, "users", "create", "Gebruikers aanmaken"),
        (UsersEdit, "users", "edit", "Gebruikers bewerken"),
        (UsersDelete, "users", "delete", "Gebruikers verwijderen"),
        (UsersBlock, "users", "block", "Gebruikers blokkeren"),
        (RolesView, "roles", "view", "Rollen bekijken"),
        (RolesCreate, "roles", "create", "Rollen aanmaken"),
        (RolesEdit, "roles", "edit", "Rollen bewerken"),
        (RolesDelete, "roles", "delete", "Rollen verwijderen"),
        (RolesManagePermissions, "roles", "manage_permissions", "Rechten van rollen beheren"),
        (EmployeesView, "employees", "view", "Personeel bekijken"),
        (EmployeesCreate, "employees", "create", "Personeel aanmaken"),
        (EmployeesEdit, "employees", "edit", "Personeel bewerken"),
        (EmployeesDeactivate, "employees", "deactivate", "Personeel deactiveren"),
        (EmployeesViewConfidential, "employees", "view_confidential", "Vertrouwelijke personeelsgegevens bekijken"),
        (EmployeeDocumentsView, "employee_documents", "view", "Personeelsdocumenten bekijken"),
        (EmployeeDocumentsCreate, "employee_documents", "create", "Personeelsdocumenten toevoegen"),
        (EmployeeDocumentsEdit, "employee_documents", "edit", "Personeelsdocumenten bewerken"),
        (EmployeeDocumentsDelete, "employee_documents", "delete", "Personeelsdocumenten verwijderen"),
        (EmployeeDocumentsApprove, "employee_documents", "approve", "Personeelsdocumenten goedkeuren"),
        (PlanningView, "planning", "view", "Planning bekijken"),
        (PlanningCreate, "planning", "create", "Planning aanmaken"),
        (PlanningEdit, "planning", "edit", "Planning bewerken"),
        (PlanningOverrideRestriction, "planning", "override_restriction", "Planningsbeperkingen overschrijven"),
        (AuditLogsView, "audit_logs", "view", "Auditlogboek bekijken"),
    ];
}
```

EF configurations (all in `Modules/Identity/Configurations/`):
- `UserConfiguration`: table `users`; `Email` required, max 250, **unique index on `(TenantId, Email)`**; `FirstName`/`LastName` max 100; `PasswordHash` max 500 nullable; index on `TenantId`.
- `RoleConfiguration`: table `roles`; `Name` required max 150, **unique index on `(TenantId, Name)`**; index on `TenantId`.
- `PermissionConfiguration`: table `permissions`; `Code` required max 100, **unique index on `Code`** (platform-wide, not tenant-scoped — permissions are a fixed catalog); `Module`/`Action` max 100.
- `UserRoleConfiguration`: table `user_roles`; composite key `(UserId, RoleId)`; FK to `User` with `DeleteBehavior.Cascade` (join row is meaningless without the user), FK to `Role` with `DeleteBehavior.Cascade`.
- `RolePermissionConfiguration`: table `role_permissions`; composite key `(RoleId, PermissionId)`; FK to `Role` cascade, FK to `Permission` cascade (join rows only, not audit data — cascade here is safe and required to avoid orphaned rows blocking role/permission cleanup).

- [ ] **Step 1: Write all 5 entity files** (as above).
- [ ] **Step 2: Write all 5 configuration files** (per the bullet spec above, following the exact pattern already used in `TransportOrderConfiguration` for `ToTable`/`HasKey`/`Property`/`HasIndex`).
- [ ] **Step 3: Add `PermissionCodes.cs`** (as above).
- [ ] **Step 4: Add DbSets to `TransportationDbContext`:** `Users`, `Roles`, `Permissions`, `UserRoles`, `RolePermissions`.
- [ ] **Step 5: Build**

Run: `dotnet build TransportationService.slnx`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add TransportationService.Api/Modules/Identity/Entities TransportationService.Api/Modules/Identity/Configurations TransportationService.Api/Modules/Identity/PermissionCodes.cs TransportationService.Api/Data/TransportationDbContext.cs
git commit -m "feat: add Identity entities (User, Role, Permission) and EF configuration"
```

---

## Task 3: Current user + permission authorization

**Files:**
- Create: `Modules/Identity/Services/ICurrentUserContext.cs`, `DevCurrentUserContext.cs`
- Create: `Modules/Identity/Services/IPermissionAuthorizationService.cs`, `PermissionAuthorizationService.cs`
- Create: `Modules/Identity/Authorization/RequirePermissionAttribute.cs`
- Modify: `Program.cs` (register `IPermissionAuthorizationService`)
- Test: `TransportationService.Api.Tests/Identity/PermissionAuthorizationServiceTests.cs`

**Interfaces:**

```csharp
// Modules/Identity/Services/ICurrentUserContext.cs
namespace TransportationService.Api.Modules.Identity.Services;

public interface ICurrentUserContext
{
    Guid? CurrentUserId { get; }
}
```

```csharp
// Modules/Identity/Services/DevCurrentUserContext.cs
namespace TransportationService.Api.Modules.Identity.Services;

public class DevCurrentUserContext : ICurrentUserContext
{
    public Guid? CurrentUserId { get; }

    public DevCurrentUserContext(Guid? currentUserId)
    {
        CurrentUserId = currentUserId;
    }
}
```

```csharp
// Modules/Identity/Services/IPermissionAuthorizationService.cs
namespace TransportationService.Api.Modules.Identity.Services;

public interface IPermissionAuthorizationService
{
    Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken);
}
```

```csharp
// Modules/Identity/Services/PermissionAuthorizationService.cs
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Identity.Services;

public class PermissionAuthorizationService : IPermissionAuthorizationService
{
    private readonly TransportationDbContext _dbContext;

    public PermissionAuthorizationService(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
    {
        return await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_dbContext.Roles.AsNoTracking().Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (ur, r) => r.Id)
            .Join(_dbContext.RolePermissions.AsNoTracking(), roleId => roleId, rp => rp.RoleId, (roleId, rp) => rp.PermissionId)
            .Join(_dbContext.Permissions.AsNoTracking(), permissionId => permissionId, p => p.Id, (permissionId, p) => p.Code)
            .AnyAsync(code => code == permissionCode, cancellationToken);
    }
}
```

```csharp
// Modules/Identity/Authorization/RequirePermissionAttribute.cs
using Microsoft.AspNetCore.Mvc.Filters;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permissionCode;

    public RequirePermissionAttribute(string permissionCode)
    {
        _permissionCode = permissionCode;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();

        if (currentUser.CurrentUserId is not { } userId)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }

        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var hasPermission = await authorizationService.UserHasPermissionAsync(userId, _permissionCode, context.HttpContext.RequestAborted);

        if (!hasPermission)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new { message = $"Missing permission: {_permissionCode}" })
            {
                StatusCode = 403
            };
            return;
        }

        await next();
    }
}
```

- [ ] **Step 1: Write the four service files + attribute** (as above).
- [ ] **Step 2: Register in `Program.cs`:** `builder.Services.AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>();`
- [ ] **Step 3: Write test**

```csharp
// Identity/PermissionAuthorizationServiceTests.cs
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Identity;

public class PermissionAuthorizationServiceTests
{
    [Fact]
    public async Task UserHasPermissionAsync_ReturnsTrue_WhenRoleGrantsPermission()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "a@b.com", FirstName = "A", LastName = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Planner", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "employees.view", Module = "employees", Action = "view", Description = "x" });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        await db.Context.SaveChangesAsync();

        var sut = new PermissionAuthorizationService(db.Context);

        var result = await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task UserHasPermissionAsync_ReturnsFalse_WhenUserHasNoMatchingRole()
    {
        using var db = new SqliteTestDbContext();
        var userId = Guid.NewGuid();

        var sut = new PermissionAuthorizationService(db.Context);

        var result = await sut.UserHasPermissionAsync(userId, "employees.view", CancellationToken.None);

        Assert.False(result);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~PermissionAuthorizationServiceTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api/Modules/Identity/Services TransportationService.Api/Modules/Identity/Authorization TransportationService.Api/Program.cs TransportationService.Api.Tests/Identity
git commit -m "feat: add permission-based server-side authorization"
```

---

## Task 4: UserService + RoleService with admin safeguards (TDD)

**Files:**
- Create: `Modules/Identity/Services/IUserService.cs`, `UserService.cs`
- Create: `Modules/Identity/Services/IRoleService.cs`, `RoleService.cs`
- Create: `Modules/Identity/Dtos/UserDtos.cs`, `RoleDtos.cs`
- Test: `TransportationService.Api.Tests/Identity/AdminSafeguardTests.cs`

**Interfaces:**

```csharp
// Modules/Identity/Dtos/UserDtos.cs
namespace TransportationService.Api.Modules.Identity.Dtos;

public record UserDto(Guid Id, string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, bool IsActive, bool IsBlocked, IReadOnlyList<RoleSummaryDto> Roles);
public record RoleSummaryDto(Guid Id, string Name);
public record CreateUserRequest(string Email, string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId, IReadOnlyList<Guid> RoleIds);
public record UpdateUserRequest(string FirstName, string LastName, Guid? EmployeeId, Guid? CustomerId);
public record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);
```

```csharp
// Modules/Identity/Dtos/RoleDtos.cs
namespace TransportationService.Api.Modules.Identity.Dtos;

public record RoleDto(Guid Id, string Name, string? Description, bool IsSystemRole, bool IsActive, IReadOnlyList<string> PermissionCodes);
public record CreateRoleRequest(string Name, string? Description);
public record UpdateRoleRequest(string Name, string? Description);
public record AssignPermissionsRequest(IReadOnlyList<string> PermissionCodes);
public record PermissionDto(Guid Id, string Code, string Module, string Action, string Description);
```

```csharp
// Modules/Identity/Services/IUserService.cs
namespace TransportationService.Api.Modules.Identity.Services;

using TransportationService.Api.Modules.Identity.Dtos;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken);
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserDto?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<UserOperationResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task<UserOperationResult> SetBlockedAsync(Guid id, bool isBlocked, CancellationToken cancellationToken);
    Task<UserOperationResult> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken);
}

public enum UserOperationOutcome { Success, NotFound, LastActiveAdministrator }

public record UserOperationResult(UserOperationOutcome Outcome, UserDto? User);
```

`UserService` safeguard logic (the business rule under test):
"the final active administrator may not be deactivated, blocked, or lose
the administrator role." Implementation approach: before applying
`SetActiveAsync(id, false)`, `SetBlockedAsync(id, true)`, or
`AssignRolesAsync` (when the new role set drops the Administrator role
the user currently holds), check whether the target user currently holds
the tenant's `Administrator` role (`Role.Name == "Administrator" &&
Role.IsSystemRole`) **and** is currently active+unblocked; if so, count
other users in the same tenant who (a) hold the Administrator role, (b)
are active, and (c) are not blocked. If that count is zero (i.e., this
user is the last one), return `UserOperationOutcome.LastActiveAdministrator`
instead of applying the change.

```csharp
// Modules/Identity/Services/IRoleService.cs
namespace TransportationService.Api.Modules.Identity.Services;

using TransportationService.Api.Modules.Identity.Dtos;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken);
    Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleOperationResult> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken);
    Task<RoleOperationResult> DeactivateAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleOperationResult> AssignPermissionsAsync(Guid id, AssignPermissionsRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken);
}

public enum RoleOperationOutcome { Success, NotFound, SystemRoleProtected }

public record RoleOperationResult(RoleOperationOutcome Outcome, RoleDto? Role);
```

`RoleService` safeguards: `DeactivateAsync` on a role with
`IsSystemRole == true` returns `SystemRoleProtected` (system roles are
deactivated never — they're protected outright, since the brief says
"a system role may not accidentally be deleted" and there is no delete
endpoint at all, only deactivate, for any role — "roles that are
currently in use should be deactivated rather than physically deleted"
covers non-system roles). `UpdateAsync`/`AssignPermissionsAsync` on a
system role's **name** is blocked (renaming the Administrator role is
disallowed) but permission assignment on a system role IS allowed
(administrators must be able to review/extend what Administrator grants).
Concretely: `UpdateAsync` returns `SystemRoleProtected` only if
`request.Name != existing.Name` and `existing.IsSystemRole`.

Both services: every query/mutation is scoped by `_tenantContext.TenantId`
via constructor-injected `ITenantContext`, using `AsNoTracking()` for
reads and explicit `SaveChangesAsync(cancellationToken)` for writes,
mapping is done by hand (no AutoMapper — YAGNI per "simple proven
dependencies over unnecessary packages").

- [ ] **Step 1: Write failing test for the last-administrator safeguard**

```csharp
// Identity/AdminSafeguardTests.cs
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Identity;

public class AdminSafeguardTests
{
    private static async Task<(SqliteTestDbContext Db, Guid TenantId, Guid AdminRoleId, Guid UserId)> SeedSingleAdministratorAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var adminRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Roles.Add(new Role { Id = adminRoleId, TenantId = tenantId, Name = "Administrator", IsSystemRole = true, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "admin@tenant.com", FirstName = "Admin", LastName = "User", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Context.UserRoles.Add(new UserRole { UserId = userId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        return (db, tenantId, adminRoleId, userId);
    }

    [Fact]
    public async Task SetActiveAsync_RejectsDeactivatingLastActiveAdministrator()
    {
        var (db, tenantId, _, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.SetActiveAsync(userId, false, CancellationToken.None);

        Assert.Equal(UserOperationOutcome.LastActiveAdministrator, result.Outcome);
        var reloaded = await sut.GetByIdAsync(userId, CancellationToken.None);
        Assert.True(reloaded!.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_AllowsDeactivatingAdministrator_WhenAnotherActiveAdministratorExists()
    {
        var (db, tenantId, adminRoleId, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var secondAdminId = Guid.NewGuid();
        db.Context.Users.Add(new Entities_User(secondAdminId, tenantId));
        db.Context.UserRoles.Add(new UserRole { UserId = secondAdminId, RoleId = adminRoleId });
        await db.Context.SaveChangesAsync();

        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.SetActiveAsync(userId, false, CancellationToken.None);

        Assert.Equal(UserOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task AssignRolesAsync_RejectsRemovingAdministratorRole_FromLastActiveAdministrator()
    {
        var (db, tenantId, _, userId) = await SeedSingleAdministratorAsync();
        using var _ = db;
        var sut = new UserService(db.Context, new DevTenantContext(tenantId));

        var result = await sut.AssignRolesAsync(userId, new AssignRolesRequest([]), CancellationToken.None);

        Assert.Equal(UserOperationOutcome.LastActiveAdministrator, result.Outcome);
    }
}

// Local helper to avoid repeating the full User object literal in the second test.
file static class Entities_UserFactory { }

file sealed class Entities_User : User
{
    public Entities_User(Guid id, Guid tenantId)
    {
        Id = id;
        TenantId = tenantId;
        Email = $"{id}@tenant.com";
        FirstName = "Second";
        LastName = "Admin";
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~AdminSafeguardTests`
Expected: FAIL (compile error — `UserService` doesn't exist yet).

- [ ] **Step 3: Implement `UserService`**

```csharp
// Modules/Identity/Services/UserService.cs
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

public class UserService : IUserService
{
    private const string AdministratorRoleName = "Administrator";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public UserService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        return await MapManyAsync(users, cancellationToken);
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);

        return user is null ? null : await MapAsync(user, cancellationToken);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmployeeId = request.EmployeeId,
            CustomerId = request.CustomerId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.Users.Add(user);

        foreach (var roleId in request.RoleIds.Distinct())
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await MapAsync(user, cancellationToken))!;
    }

    public async Task<UserDto?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return null;

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.EmployeeId = request.EmployeeId;
        user.CustomerId = request.CustomerId;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<UserOperationResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        if (!isActive && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    public async Task<UserOperationResult> SetBlockedAsync(Guid id, bool isBlocked, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        if (isBlocked && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        user.IsBlocked = isBlocked;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    public async Task<UserOperationResult> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        var newRoleIds = request.RoleIds.Distinct().ToHashSet();
        var wasAdministrator = await IsAdministratorAsync(user.Id, cancellationToken);
        var staysAdministrator = await RoleIdsIncludeAdministratorAsync(newRoleIds, cancellationToken);

        if (wasAdministrator && !staysAdministrator && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        var existingRoles = await _dbContext.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken);
        _dbContext.UserRoles.RemoveRange(existingRoles);
        foreach (var roleId in newRoleIds)
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    private async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)
            .AnyAsync(r => r.Name == AdministratorRoleName && r.IsSystemRole, cancellationToken);
    }

    private async Task<bool> RoleIdsIncludeAdministratorAsync(IReadOnlySet<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return false;

        return await _dbContext.Roles
            .Where(r => roleIds.Contains(r.Id) && r.Name == AdministratorRoleName && r.IsSystemRole)
            .AnyAsync(cancellationToken);
    }

    private async Task<bool> IsLastActiveAdministratorAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.IsActive || user.IsBlocked)
        {
            // Only currently-active, unblocked administrators count toward the "must keep one" guarantee.
        }

        var isCurrentlyAdministrator = await IsAdministratorAsync(user.Id, cancellationToken);
        if (!isCurrentlyAdministrator) return false;

        var otherActiveAdministratorCount = await _dbContext.UserRoles
            .Join(_dbContext.Roles.Where(r => r.Name == AdministratorRoleName && r.IsSystemRole), ur => ur.RoleId, r => r.Id, (ur, r) => ur.UserId)
            .Join(_dbContext.Users.Where(u => u.TenantId == _tenantContext.TenantId && u.IsActive && !u.IsBlocked && u.Id != user.Id), userId => userId, u => u.Id, (userId, u) => u.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        return otherActiveAdministratorCount == 0;
    }

    private async Task<UserDto> MapAsync(User user, CancellationToken cancellationToken) => (await MapManyAsync([user], cancellationToken))[0];

    private async Task<IReadOnlyList<UserDto>> MapManyAsync(IReadOnlyList<User> users, CancellationToken cancellationToken)
    {
        var userIds = users.Select(u => u.Id).ToList();

        var roleRows = await _dbContext.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleSummary = new RoleSummaryDto(r.Id, r.Name) })
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new UserDto(
                u.Id, u.Email, u.FirstName, u.LastName, u.EmployeeId, u.CustomerId, u.IsActive, u.IsBlocked,
                roleRows.Where(r => r.UserId == u.Id).Select(r => r.RoleSummary).ToList()))
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~AdminSafeguardTests`
Expected: 3 passed.

- [ ] **Step 5: Implement `RoleService`** using the same tenant-scoping and
      mapping pattern as `UserService`, with the `SystemRoleProtected`
      safeguard described above. Include `ListPermissionsAsync` reading
      the platform-wide `Permissions` table (no tenant filter — permissions
      are not tenant-owned).

- [ ] **Step 6: Register both services in `Program.cs`:**
      `builder.Services.AddScoped<IUserService, UserService>();`
      `builder.Services.AddScoped<IRoleService, RoleService>();`

- [ ] **Step 7: Build and run full test suite**

Run: `dotnet build TransportationService.slnx && dotnet test TransportationService.slnx`
Expected: Build succeeded, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add TransportationService.Api/Modules/Identity/Services TransportationService.Api/Modules/Identity/Dtos TransportationService.Api/Program.cs TransportationService.Api.Tests/Identity
git commit -m "feat: add UserService/RoleService with last-administrator and system-role safeguards"
```

---

## Task 5: Identity controllers

**Files:**
- Create: `Modules/Identity/Controllers/UsersController.cs`
- Create: `Modules/Identity/Controllers/RolesController.cs`
- Create: `Modules/Identity/Controllers/PermissionsController.cs`

**Interfaces (routes):**

`UsersController` (`api/users`), each action decorated with
`[RequirePermission(PermissionCodes.X)]`:
- `GET /api/users` → `UsersView` → `IReadOnlyList<UserDto>`
- `GET /api/users/{id}` → `UsersView` → `UserDto` or 404
- `POST /api/users` → `UsersCreate` → validates `Email` non-empty/valid
  format/max 250, `FirstName`/`LastName` non-empty/max 100; 409 if email
  already used in tenant (service throws `InvalidOperationException`
  caught here as 409, OR service returns a result type — chosen approach:
  rely on the unique index + catch `DbUpdateException` → `Conflict()`,
  matching the existing `TransportOrdersController` pattern exactly).
- `PUT /api/users/{id}` → `UsersEdit` → 404 if missing.
- `PATCH /api/users/{id}/active` body `{ "isActive": bool }` → `UsersEdit`
  → maps `UserOperationOutcome` to 404 / 409 (`LastActiveAdministrator`) / 200.
- `PATCH /api/users/{id}/blocked` body `{ "isBlocked": bool }` → `UsersBlock`
  → same outcome mapping.
- `POST /api/users/{id}/roles` body `AssignRolesRequest` → `UsersEdit`
  → same outcome mapping.

`RolesController` (`api/roles`):
- `GET /api/roles` → `RolesView`
- `GET /api/roles/{id}` → `RolesView`
- `POST /api/roles` → `RolesCreate` → validates `Name` non-empty max 150.
- `PUT /api/roles/{id}` → `RolesEdit` → maps `SystemRoleProtected` to 409.
- `POST /api/roles/{id}/deactivate` → `RolesDelete` → maps `SystemRoleProtected` to 409.
- `POST /api/roles/{id}/permissions` body `AssignPermissionsRequest` → `RolesManagePermissions`.

`PermissionsController` (`api/permissions`):
- `GET /api/permissions` → `RolesView` (viewing the catalog is part of managing roles, no separate permission needed — YAGNI).

- [ ] **Step 1: Write `UsersController`** implementing the routes above,
      constructor-injecting `IUserService`, following the existing
      `TransportOrdersController` conventions (trim inputs, `BadRequest`
      with a message string on validation failure, `NotFound()`,
      `CreatedAtAction`, catch `DbUpdateException` → `Conflict(...)`
      around `CreateAsync`).
- [ ] **Step 2: Write `RolesController`** the same way.
- [ ] **Step 3: Write `PermissionsController`** the same way.
- [ ] **Step 4: Build**

Run: `dotnet build TransportationService.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Manual smoke test via `curl` against a running dev server** (documented in Task "Phase 8 verification" below, not repeated per-task) — skip here, covered later.

- [ ] **Step 6: Commit**

```bash
git add TransportationService.Api/Modules/Identity/Controllers
git commit -m "feat: add Users/Roles/Permissions API endpoints"
```

---

## Task 6: Employees module

**Files:**
- Create: `Modules/Employees/Entities/Employee.cs`, `EmploymentStatus.cs`, `EmployeeFunction.cs`
- Create: `Modules/Employees/Configurations/EmployeeConfiguration.cs`
- Create: `Modules/Employees/Dtos/EmployeeDtos.cs`
- Create: `Modules/Employees/Services/IEmployeeService.cs`, `EmployeeService.cs`
- Create: `Modules/Employees/Controllers/EmployeesController.cs`
- Modify: `Data/TransportationDbContext.cs`
- Test: `TransportationService.Api.Tests/Employees/EmployeeWithoutUserTests.cs`

**Interfaces:**

```csharp
// Modules/Employees/Entities/EmploymentStatus.cs
namespace TransportationService.Api.Modules.Employees.Entities;

public enum EmploymentStatus { Active, OnLeave, Suspended, Terminated }
```

```csharp
// Modules/Employees/Entities/EmployeeFunction.cs
namespace TransportationService.Api.Modules.Employees.Entities;

public enum EmployeeFunction { DriverB, DriverC, DriverCE, CraneOperator, WarehouseWorker, Planner, Dispatcher, OfficeWorker, Mechanic, Other }
```

```csharp
// Modules/Employees/Entities/Employee.cs
namespace TransportationService.Api.Modules.Employees.Entities;

public class Employee
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public DateOnly EmploymentStartDate { get; set; }
    public DateOnly? EmploymentEndDate { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public EmployeeFunction PrimaryFunction { get; set; }
    public bool IsActive { get; set; } = true;
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

`EmployeeConfiguration`: table `employees`; required/max-length on
`EmployeeNumber` (30), `FirstName`/`LastName` (100), `Street` (150),
`HouseNumber` (20), `PostalCode` (20), `City` (100), `Country` (100),
`PhoneNumber` (30), `Email` (250); `EmploymentStatus`/`PrimaryFunction`
stored as string via `.HasConversion<string>()` (readable in DB, stable
technical value per brief — storing enums as strings avoids silent
renumbering breakage if the enum order changes); **unique index on
`(TenantId, EmployeeNumber)`**; index on `TenantId`; index on
`(TenantId, IsActive)` for the common "active employees" list filter.

```csharp
// Modules/Employees/Dtos/EmployeeDtos.cs
namespace TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;

public record EmployeeListItemDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, EmployeeFunction PrimaryFunction, EmploymentStatus EmploymentStatus, bool IsActive);

public record EmployeeDetailDto(
    Guid Id, string EmployeeNumber, string FirstName, string LastName,
    string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth,
    DateOnly EmploymentStartDate, DateOnly? EmploymentEndDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction, bool IsActive,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record CreateEmployeeRequest(
    string FirstName, string LastName, string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth, DateOnly EmploymentStartDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record UpdateEmployeeRequest(
    string FirstName, string LastName, string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth, DateOnly? EmploymentEndDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record EmployeePagedResult(IReadOnlyList<EmployeeListItemDto> Items, int TotalCount, int Page, int PageSize);
```

```csharp
// Modules/Employees/Services/IEmployeeService.cs
namespace TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Employees.Dtos;

public interface IEmployeeService
{
    Task<EmployeePagedResult> SearchAsync(string? searchText, bool? isActive, int page, int pageSize, CancellationToken cancellationToken);
    Task<EmployeeDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken);
    Task<EmployeeDetailDto?> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
```

`EmployeeService.CreateAsync` generates `EmployeeNumber` from
`TenantSettings.EmployeeNumberPrefix` + zero-padded
`EmployeeNumberNextValue` (read, use, increment, save within the same
`SaveChangesAsync` — acceptable race window given this is an internal
admin tool with low concurrent-create volume; documented as a known
non-goal to add a `SELECT ... FOR UPDATE` style lock, matching "avoid
overengineering... unless it solves a real problem in this codebase").
`SearchAsync` filters by `TenantId` always, by `IsActive` when provided,
and by `searchText` matching `EmployeeNumber`, `FirstName`, `LastName`,
or `Email` (case-insensitive `Contains`) when provided; orders by
`LastName, FirstName`; applies `Skip((page-1)*pageSize).Take(pageSize)`.
`DeactivateAsync` sets `IsActive = false` and `EmploymentEndDate =
DateOnly.FromDateTime(DateTime.UtcNow)` if not already set — never
deletes the row (historical records must not disappear).

- [ ] **Step 1: Write failing test proving an employee can exist without a user**

```csharp
// Employees/EmployeeWithoutUserTests.cs
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

public class EmployeeWithoutUserTests
{
    [Fact]
    public async Task CreateAsync_CreatesEmployee_WithNoLinkedUser()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "EMP-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var sut = new EmployeeService(db.Context, new DevTenantContext(tenantId));

        var created = await sut.CreateAsync(new CreateEmployeeRequest(
            "Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE",
            "+32000000000", "jan@example.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1),
            EmploymentStatus.Active, EmployeeFunction.DriverC, null, null, null), CancellationToken.None);

        Assert.Equal("EMP-0001", created.EmployeeNumber);
        var usersLinkedToEmployee = db.Context.Users.Count(u => u.EmployeeId == created.Id);
        Assert.Equal(0, usersLinkedToEmployee);
    }

    [Fact]
    public async Task SearchAsync_DoesNotReturnEmployeesFromOtherTenants()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "A-", EmployeeNumberNextValue = 1 });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeNumberPrefix = "B-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var sutA = new EmployeeService(db.Context, new DevTenantContext(tenantA));
        var sutB = new EmployeeService(db.Context, new DevTenantContext(tenantB));

        await sutA.CreateAsync(new CreateEmployeeRequest("Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE", "+32", "jan@a.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);

        var resultForTenantB = await sutB.SearchAsync(null, null, 1, 25, CancellationToken.None);

        Assert.Empty(resultForTenantB.Items);
    }
}
```

- [ ] **Step 2: Run test to verify it fails to compile** (entities/service don't exist).
- [ ] **Step 3: Implement `Employee`, `EmploymentStatus`, `EmployeeFunction`, `EmployeeConfiguration`, `EmployeeDtos`, `EmployeeService`** per the specs above.
- [ ] **Step 4: Add `DbSet<Employee> Employees` to `TransportationDbContext`.**
- [ ] **Step 5: Register `IEmployeeService` in `Program.cs`.**
- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~Employees`
Expected: 2 passed.

- [ ] **Step 7: Write `EmployeesController`** (`api/employees`):
      `GET /api/employees?search=&isActive=&page=&pageSize=` → `EmployeesView` → `EmployeePagedResult`;
      `GET /api/employees/{id}` → `EmployeesView` → `EmployeeDetailDto` or 404;
      `POST /api/employees` → `EmployeesCreate` → validation errors as `BadRequest`, `DbUpdateException` → `Conflict`;
      `PUT /api/employees/{id}` → `EmployeesEdit`;
      `POST /api/employees/{id}/deactivate` → `EmployeesDeactivate`.

- [ ] **Step 8: Build**

Run: `dotnet build TransportationService.slnx`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add TransportationService.Api/Modules/Employees TransportationService.Api/Data/TransportationDbContext.cs TransportationService.Api/Program.cs TransportationService.Api.Tests/Employees
git commit -m "feat: add Employees module with tenant-scoped search and safeguards"
```

---

## Task 7: File storage abstraction

**Files:**
- Create: `Modules/Qualifications/Services/IFileStorageService.cs`, `LocalFileStorageService.cs`
- Modify: `Program.cs`, `.gitignore` (already covers `App_Data/`)
- Test: `TransportationService.Api.Tests/Qualifications/LocalFileStorageServiceTests.cs`

**Interfaces:**

```csharp
// Modules/Qualifications/Services/IFileStorageService.cs
namespace TransportationService.Api.Modules.Qualifications.Services;

public interface IFileStorageService
{
    /// <returns>An opaque storage key. Never a client-supplied path.</returns>
    Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
```

```csharp
// Modules/Qualifications/Services/LocalFileStorageService.cs
namespace TransportationService.Api.Modules.Qualifications.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(string rootPath)
    {
        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var sanitizedFileName = SanitizeFileName(fileName);
        var storageKey = $"tenant-{tenantId}/{SanitizeSegment(category)}/{Guid.NewGuid()}-{sanitizedFileName}";
        var fullPath = ResolveFullPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return storageKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(storageKey);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolveFullPath(string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/');
        if (normalized.Contains("..")) throw new ArgumentException("Invalid storage key.", nameof(storageKey));

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootFullPath = Path.GetFullPath(_rootPath);
        if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Storage key escapes the storage root.", nameof(storageKey));
        }

        return fullPath;
    }

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(segment.Where(c => !invalid.Contains(c)).ToArray());
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
```

- [ ] **Step 1: Write the two files above.**
- [ ] **Step 2: Write path-traversal test**

```csharp
// Qualifications/LocalFileStorageServiceTests.cs
using System.Text;
using TransportationService.Api.Modules.Qualifications.Services;
using Xunit;

namespace TransportationService.Api.Tests.Qualifications;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tms-tests-" + Guid.NewGuid());

    [Fact]
    public async Task SaveAndOpenRead_RoundTripsContent()
    {
        var sut = new LocalFileStorageService(_root);
        var tenantId = Guid.NewGuid();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var key = await sut.SaveAsync(tenantId, "qualifications", "license.pdf", content, CancellationToken.None);

        await using var readBack = await sut.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(readBack);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_RejectsPathTraversalKeys()
    {
        var sut = new LocalFileStorageService(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.OpenReadAsync("../../etc/passwd", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 3: Register in `Program.cs`:**
      `builder.Services.AddSingleton<IFileStorageService>(new LocalFileStorageService(Path.Combine(builder.Environment.ContentRootPath, "App_Data")));`

- [ ] **Step 4: Run tests**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~LocalFileStorageServiceTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api/Modules/Qualifications/Services TransportationService.Api/Program.cs TransportationService.Api.Tests/Qualifications
git commit -m "feat: add local file storage abstraction for future document uploads"
```

---

## Task 8: Qualifications module (types + employee qualifications + status calculator)

**Files:**
- Create: `Modules/Qualifications/Entities/QualificationType.cs`, `EmployeeQualification.cs`, `QualificationStatus.cs`
- Create: `Modules/Qualifications/Configurations/QualificationTypeConfiguration.cs`, `EmployeeQualificationConfiguration.cs`
- Create: `Modules/Qualifications/Dtos/QualificationDtos.cs`
- Create: `Modules/Qualifications/Services/IQualificationStatusCalculator.cs`, `QualificationStatusCalculator.cs`
- Create: `Modules/Qualifications/Services/IQualificationService.cs`, `QualificationService.cs`
- Create: `Modules/Qualifications/Controllers/QualificationsController.cs`
- Modify: `Data/TransportationDbContext.cs`
- Test: `TransportationService.Api.Tests/Qualifications/QualificationStatusCalculatorTests.cs`

**Interfaces:**

```csharp
// Modules/Qualifications/Entities/QualificationStatus.cs
namespace TransportationService.Api.Modules.Qualifications.Entities;

public enum QualificationStatus { Pending, Valid, ExpiringSoon, Expired, Rejected, Suspended }
```

```csharp
// Modules/Qualifications/Entities/QualificationType.cs
namespace TransportationService.Api.Modules.Qualifications.Entities;

public class QualificationType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool RequiresExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class QualificationTypeCodes
{
    public const string DrivingLicenceB = "DrivingLicenceB";
    public const string DrivingLicenceC = "DrivingLicenceC";
    public const string DrivingLicenceCE = "DrivingLicenceCE";
    public const string Code95 = "Code95";
    public const string Adr = "ADR";
    public const string MedicalFitness = "MedicalFitness";
    public const string DriverCard = "DriverCard";
    public const string CraneCertificate = "CraneCertificate";
    public const string ForkliftCertificate = "ForkliftCertificate";
    public const string Alfapass = "Alfapass";
    public const string Other = "Other";
}
```

```csharp
// Modules/Qualifications/Entities/EmployeeQualification.cs
namespace TransportationService.Api.Modules.Qualifications.Entities;

public class EmployeeQualification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid QualificationTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly ObtainedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public QualificationStatus Status { get; set; }
    public string? DocumentPath { get; set; }
    public string? Notes { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Configurations: `QualificationTypeConfiguration` — table
`qualification_types`; `Code` required max 50, **unique index on `Code`**
(platform-wide catalog, not tenant-scoped, matching `Permission`);
`Name` max 150, `Category` max 50. `EmployeeQualificationConfiguration`
— table `employee_qualifications`; `DocumentNumber` max 100 nullable,
`DocumentPath` max 500 nullable, `Notes` max 2000 nullable; `Status`
stored via `.HasConversion<string>()`; index on `(TenantId, EmployeeId)`;
index on `(TenantId, ExpiryDate)` for the expiry-scan queries; FK to
`Employee` via `EmployeeId` with `DeleteBehavior.Restrict` (qualification
history must survive; employees are deactivated, never deleted, so this
FK is defensive) — **no navigation property required on `Employee`**,
queried by `EmployeeId` explicitly (keeps `Employee` entity free of a
qualifications collection it doesn't need for its own module).

```csharp
// Modules/Qualifications/Services/IQualificationStatusCalculator.cs
namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;

public interface IQualificationStatusCalculator
{
    /// <summary>
    /// Computes the *effective* status for display/eligibility purposes.
    /// Suspended/Rejected/Pending are authoritative (never overridden by dates).
    /// For Valid/ExpiringSoon/Expired, the stored Status is recomputed from
    /// ExpiryDate vs. evaluationDate and the tenant's warning window, so a
    /// row that was "Valid" yesterday correctly reads "Expired" today
    /// without a background job having to touch every row.
    /// </summary>
    QualificationStatus CalculateEffectiveStatus(EmployeeQualification qualification, DateOnly evaluationDate, int expiryWarningDays);
}
```

```csharp
// Modules/Qualifications/Services/QualificationStatusCalculator.cs
namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;

public class QualificationStatusCalculator : IQualificationStatusCalculator
{
    public QualificationStatus CalculateEffectiveStatus(EmployeeQualification qualification, DateOnly evaluationDate, int expiryWarningDays)
    {
        if (qualification.Status is QualificationStatus.Suspended or QualificationStatus.Rejected or QualificationStatus.Pending)
        {
            return qualification.Status;
        }

        if (qualification.ExpiryDate is not { } expiryDate)
        {
            return QualificationStatus.Valid;
        }

        if (expiryDate < evaluationDate)
        {
            return QualificationStatus.Expired;
        }

        if (expiryDate <= evaluationDate.AddDays(expiryWarningDays))
        {
            return QualificationStatus.ExpiringSoon;
        }

        return QualificationStatus.Valid;
    }
}
```

```csharp
// Modules/Qualifications/Dtos/QualificationDtos.cs
namespace TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;

public record QualificationTypeDto(Guid Id, string Code, string Name, string? Description, string Category, bool RequiresExpiryDate, bool IsActive);

public record EmployeeQualificationDto(
    Guid Id, Guid EmployeeId, Guid QualificationTypeId, string QualificationTypeCode, string QualificationTypeName,
    string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, QualificationStatus StoredStatus, QualificationStatus EffectiveStatus,
    string? DocumentPath, string? Notes, DateTime? VerifiedAt, Guid? VerifiedByUserId);

public record CreateEmployeeQualificationRequest(Guid QualificationTypeId, string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, string? Notes);
public record UpdateEmployeeQualificationRequest(string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, string? Notes);
```

`IQualificationService` methods: `ListForEmployeeAsync(employeeId, ct)`,
`GetByIdAsync(id, ct)`, `CreateAsync(employeeId, request, ct)` (Status
starts `Pending`), `UpdateAsync(id, request, ct)`,
`VerifyAsync(id, verifyingUserId, ct)` (sets `Status = Valid`,
`VerifiedAt = UtcNow`, `VerifiedByUserId`), `SuspendAsync(id, ct)` (sets
`Status = Suspended`), `DeactivateAsync(id, ct)` (there is no qualification
delete — "remove or deactivate qualification" is satisfied by reusing
`Suspended` as the deactivated state, since a separate `IsActive` flag
would create two competing sources of truth for "is this qualification
usable" — this is a deliberate simplification, documented here),
`ListExpiringWithinDaysAsync(days, ct)`, `ListExpiredAsync(ct)`. All
tenant-scoped via `ITenantContext`; `EffectiveStatus` in every returned
DTO computed via `IQualificationStatusCalculator` using
`TenantSettings.QualificationExpiryWarningDays` (default 30 if no
`TenantSettings` row).

- [ ] **Step 1: Write failing tests for the status calculator**

```csharp
// Qualifications/QualificationStatusCalculatorTests.cs
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using Xunit;

namespace TransportationService.Api.Tests.Qualifications;

public class QualificationStatusCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 17);
    private readonly QualificationStatusCalculator _sut = new();

    private static EmployeeQualification Qualification(QualificationStatus status, DateOnly? expiryDate) =>
        new() { Status = status, ExpiryDate = expiryDate, ObtainedDate = Today.AddYears(-1) };

    [Fact]
    public void Suspended_StaysSuspended_RegardlessOfDates()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Suspended, Today.AddYears(1)), Today, 30);
        Assert.Equal(QualificationStatus.Suspended, result);
    }

    [Fact]
    public void NoExpiryDate_IsValid()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, null), Today, 30);
        Assert.Equal(QualificationStatus.Valid, result);
    }

    [Fact]
    public void ExpiryDateInPast_IsExpired()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(-1)), Today, 30);
        Assert.Equal(QualificationStatus.Expired, result);
    }

    [Fact]
    public void ExpiryDateWithinWarningWindow_IsExpiringSoon()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(10)), Today, 30);
        Assert.Equal(QualificationStatus.ExpiringSoon, result);
    }

    [Fact]
    public void ExpiryDateBeyondWarningWindow_IsValid()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Valid, Today.AddDays(90)), Today, 30);
        Assert.Equal(QualificationStatus.Valid, result);
    }

    [Fact]
    public void PendingQualification_StaysPending_EvenIfExpiryFarInFuture()
    {
        var result = _sut.CalculateEffectiveStatus(Qualification(QualificationStatus.Pending, Today.AddYears(1)), Today, 30);
        Assert.Equal(QualificationStatus.Pending, result);
    }
}
```

- [ ] **Step 2: Run to verify failure, then implement `QualificationStatusCalculator`, entities, configurations, DTOs.**
- [ ] **Step 3: Run to verify the 6 tests pass.**
- [ ] **Step 4: Implement `QualificationService`** per the method list above, tenant-scoped, using `IQualificationStatusCalculator` for `EffectiveStatus` on every DTO.
- [ ] **Step 5: Add `DbSet<QualificationType> QualificationTypes`, `DbSet<EmployeeQualification> EmployeeQualifications` to `TransportationDbContext`.**
- [ ] **Step 6: Register `IQualificationStatusCalculator` and `IQualificationService` in `Program.cs`.**
- [ ] **Step 7: Write `QualificationsController`** (`api/employees/{employeeId}/qualifications`):
      `GET` → `EmployeeDocumentsView`; `POST` → `EmployeeDocumentsCreate`;
      `PUT /{id}` → `EmployeeDocumentsEdit`; `POST /{id}/verify` → `EmployeeDocumentsApprove`;
      `POST /{id}/suspend` → `EmployeeDocumentsEdit`; plus top-level
      `GET /api/qualifications/expiring?days=` and
      `GET /api/qualifications/expired` → `EmployeeDocumentsView`;
      `GET /api/qualification-types` → `EmployeeDocumentsView`.
- [ ] **Step 8: Build and run full suite**

Run: `dotnet build TransportationService.slnx && dotnet test TransportationService.slnx`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add TransportationService.Api/Modules/Qualifications TransportationService.Api/Data/TransportationDbContext.cs TransportationService.Api/Program.cs TransportationService.Api.Tests/Qualifications
git commit -m "feat: add qualification types, employee qualifications, and status calculation"
```

---

## Task 9: Driver eligibility evaluator (pure, TDD — highest priority business logic)

**Files:**
- Create: `Modules/Eligibility/Models/QualificationSnapshot.cs`, `DriverEligibilityRequest.cs`, `EligibilityResult.cs`
- Create: `Modules/Eligibility/Services/DriverEligibilityEvaluator.cs`
- Test: `TransportationService.Api.Tests/Eligibility/DriverEligibilityEvaluatorTests.cs`

**Interfaces:**

```csharp
// Modules/Eligibility/Models/QualificationSnapshot.cs
namespace TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Qualifications.Entities;

public record QualificationSnapshot(string QualificationTypeCode, QualificationStatus EffectiveStatus, DateOnly? ExpiryDate);
```

```csharp
// Modules/Eligibility/Models/DriverEligibilityRequest.cs
namespace TransportationService.Api.Modules.Eligibility.Models;

public record DriverEligibilityRequest(
    string? RequiredDrivingLicenceCategory, // "B" | "C" | "CE" | null
    bool RequiresCode95,
    bool RequiresAdr,
    bool RequiresMedicalFitness,
    bool RequiresCraneCertificate,
    IReadOnlyList<string> RequiredAdditionalQualificationCodes,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate);
```

```csharp
// Modules/Eligibility/Models/EligibilityResult.cs
namespace TransportationService.Api.Modules.Eligibility.Models;

public record EligibilityCheckedQualification(string QualificationTypeCode, bool Satisfied, string? Reason);

public record EligibilityResult(
    bool IsEligible,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EligibilityCheckedQualification> CheckedQualifications);
```

```csharp
// Modules/Eligibility/Services/DriverEligibilityEvaluator.cs
namespace TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Qualifications.Entities;

/// <summary>
/// Pure, dependency-free evaluation of the eligibility business rules.
/// No database access — callers (IDriverEligibilityService) load
/// qualifications and pass snapshots in. Kept pure so the highest-risk
/// business rules in the system can be unit tested without EF Core.
/// </summary>
public class DriverEligibilityEvaluator
{
    private static readonly IReadOnlyDictionary<string, int> LicenceRank = new Dictionary<string, int>
    {
        ["B"] = 1,
        ["C"] = 2,
        ["CE"] = 3,
    };

    public EligibilityResult Evaluate(IReadOnlyList<QualificationSnapshot> qualifications, DriverEligibilityRequest request)
    {
        var blockingReasons = new List<string>();
        var warnings = new List<string>();
        var checkedQualifications = new List<EligibilityCheckedQualification>();

        if (request.RequiredDrivingLicenceCategory is { } requiredCategory)
        {
            CheckLicenceCategory(qualifications, requiredCategory, request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresCode95)
        {
            CheckSimpleRequirement(qualifications, "Code95", "Code 95 is verplicht voor beroepsmatig vervoer.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresAdr)
        {
            CheckSimpleRequirement(qualifications, "ADR", "Een geldig ADR-certificaat is verplicht voor dit transport.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresMedicalFitness)
        {
            CheckSimpleRequirement(qualifications, "MedicalFitness", "Een geldige medische keuring is verplicht.", request, blockingReasons, checkedQualifications);
        }

        if (request.RequiresCraneCertificate)
        {
            CheckSimpleRequirement(qualifications, "CraneCertificate", "Een geldig kraancertificaat is verplicht voor kraanwerkzaamheden.", request, blockingReasons, checkedQualifications);
        }

        foreach (var additionalCode in request.RequiredAdditionalQualificationCodes)
        {
            CheckSimpleRequirement(qualifications, additionalCode, $"Kwalificatie '{additionalCode}' ontbreekt of is niet geldig.", request, blockingReasons, checkedQualifications);
        }

        return new EligibilityResult(blockingReasons.Count == 0, blockingReasons, warnings, checkedQualifications);
    }

    private void CheckLicenceCategory(
        IReadOnlyList<QualificationSnapshot> qualifications, string requiredCategory, DriverEligibilityRequest request,
        List<string> blockingReasons, List<EligibilityCheckedQualification> checkedQualifications)
    {
        if (requiredCategory == "CE")
        {
            // A CE combination specifically requires the CE licence — a plain C licence does not extend to CE.
            CheckSimpleRequirement(qualifications, "DrivingLicenceCE", "Rijbewijs CE is verplicht voor deze combinatie; rijbewijs C volstaat niet.", request, blockingReasons, checkedQualifications);
            return;
        }

        var requiredRank = LicenceRank[requiredCategory];
        var holderRank = ["B", "C", "CE"]
            .Where(code => IsQualificationValidForPeriod(qualifications, $"DrivingLicence{code}", request))
            .Select(code => LicenceRank[code])
            .DefaultIfEmpty(0)
            .Max();

        var satisfied = holderRank >= requiredRank;
        checkedQualifications.Add(new EligibilityCheckedQualification($"DrivingLicence{requiredCategory}", satisfied,
            satisfied ? null : $"Rijbewijs {requiredCategory} (of hoger) is verplicht; chauffeur heeft dit niet of het is niet geldig."));

        if (!satisfied)
        {
            blockingReasons.Add($"Rijbewijs {requiredCategory} is verplicht voor dit transport.");
        }
    }

    private void CheckSimpleRequirement(
        IReadOnlyList<QualificationSnapshot> qualifications, string qualificationTypeCode, string blockingMessage,
        DriverEligibilityRequest request, List<string> blockingReasons, List<EligibilityCheckedQualification> checkedQualifications)
    {
        var satisfied = IsQualificationValidForPeriod(qualifications, qualificationTypeCode, request);
        checkedQualifications.Add(new EligibilityCheckedQualification(qualificationTypeCode, satisfied, satisfied ? null : blockingMessage));

        if (!satisfied)
        {
            blockingReasons.Add(blockingMessage);
        }
    }

    private static bool IsQualificationValidForPeriod(IReadOnlyList<QualificationSnapshot> qualifications, string qualificationTypeCode, DriverEligibilityRequest request)
    {
        var match = qualifications.FirstOrDefault(q => q.QualificationTypeCode == qualificationTypeCode);
        if (match is null) return false;

        if (match.EffectiveStatus is not (QualificationStatus.Valid or QualificationStatus.ExpiringSoon)) return false;

        // A qualification that expires before the planned transport ends is not valid for that transport,
        // even if it currently reads as Valid/ExpiringSoon "today".
        if (match.ExpiryDate is { } expiry && expiry < request.PlannedEndDate) return false;

        return true;
    }
}
```

- [ ] **Step 1: Write the full test file (all Phase 8 required scenarios) before writing the evaluator's business logic bodies** — write the three model files first (needed to compile), then this test file, then run it to see it fail, then implement `DriverEligibilityEvaluator`.

```csharp
// Eligibility/DriverEligibilityEvaluatorTests.cs
using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using Xunit;

namespace TransportationService.Api.Tests.Eligibility;

public class DriverEligibilityEvaluatorTests
{
    private static readonly DateOnly Start = new(2026, 8, 1);
    private static readonly DateOnly End = new(2026, 8, 3);
    private readonly DriverEligibilityEvaluator _sut = new();

    private static QualificationSnapshot Valid(string code, DateOnly? expiry = null) =>
        new(code, QualificationStatus.Valid, expiry ?? End.AddYears(1));

    private static DriverEligibilityRequest RequestFor(string? licence = null, bool code95 = false, bool adr = false, bool medical = false, bool crane = false, IReadOnlyList<string>? additional = null) =>
        new(licence, code95, adr, medical, crane, additional ?? [], Start, End);

    [Fact]
    public void LicenceB_DoesNotSatisfy_RequiredC()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceB")], RequestFor(licence: "C"));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("C"));
    }

    [Fact]
    public void LicenceC_DoesNotAutomaticallySatisfy_RequiredCE()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceC")], RequestFor(licence: "CE"));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void CeVehicleCombination_RequiresDrivingLicenceCE_Specifically()
    {
        var result = _sut.Evaluate([Valid("DrivingLicenceCE")], RequestFor(licence: "CE"));

        Assert.True(result.IsEligible);
    }

    [Fact]
    public void ExpiredAdrCertificate_Blocks_AdrTransport()
    {
        var expired = new QualificationSnapshot("ADR", QualificationStatus.Expired, Start.AddDays(-1));

        var result = _sut.Evaluate([expired], RequestFor(adr: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("ADR"));
    }

    [Fact]
    public void CertificateExpiringBeforeTransportEndDate_IsInvalid_ForThatTransport()
    {
        var expiresDuringTransport = Valid("ADR", expiry: Start.AddDays(1)); // expires before End (Aug 3)

        var result = _sut.Evaluate([expiresDuringTransport], RequestFor(adr: true));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MissingMedicalFitness_Blocks_DriverEligibility()
    {
        var result = _sut.Evaluate([], RequestFor(medical: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("medische"));
    }

    [Fact]
    public void ValidCe_Code95_AndMedicalFitness_PassTheAppropriateCheck()
    {
        var result = _sut.Evaluate(
            [Valid("DrivingLicenceCE"), Valid("Code95"), Valid("MedicalFitness")],
            RequestFor(licence: "CE", code95: true, medical: true));

        Assert.True(result.IsEligible);
        Assert.Empty(result.BlockingReasons);
    }

    [Fact]
    public void SuspendedQualification_IsInvalid()
    {
        var suspended = new QualificationSnapshot("ADR", QualificationStatus.Suspended, End.AddYears(1));

        var result = _sut.Evaluate([suspended], RequestFor(adr: true));

        Assert.False(result.IsEligible);
    }

    [Fact]
    public void MissingQualification_ProducesClearReason()
    {
        var result = _sut.Evaluate([], RequestFor(crane: true));

        Assert.False(result.IsEligible);
        Assert.Contains(result.BlockingReasons, r => r.Contains("kraancertificaat"));
    }

    [Fact]
    public void CraneOperation_RequiresValidCraneCertificate()
    {
        var result = _sut.Evaluate([Valid("CraneCertificate")], RequestFor(crane: true));

        Assert.True(result.IsEligible);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~DriverEligibilityEvaluatorTests`
Expected: FAIL (types don't exist).

- [ ] **Step 3: Implement the three model files and `DriverEligibilityEvaluator`** exactly as specified above.

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~DriverEligibilityEvaluatorTests`
Expected: 10 passed.

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api/Modules/Eligibility/Models TransportationService.Api/Modules/Eligibility/Services/DriverEligibilityEvaluator.cs TransportationService.Api.Tests/Eligibility
git commit -m "feat: add pure driver eligibility rule evaluator with full rule coverage"
```

---

## Task 10: Eligibility service + check endpoint

**Files:**
- Create: `Modules/Eligibility/Services/IDriverEligibilityService.cs`, `DriverEligibilityService.cs`
- Create: `Modules/Eligibility/Dtos/EligibilityDtos.cs`
- Create: `Modules/Eligibility/Controllers/DriverEligibilityController.cs`

**Interfaces:**

```csharp
// Modules/Eligibility/Dtos/EligibilityDtos.cs
namespace TransportationService.Api.Modules.Eligibility.Dtos;

public record CheckEligibilityRequest(
    Guid EmployeeId, string? RequiredDrivingLicenceCategory, bool RequiresCode95, bool RequiresAdr,
    bool RequiresMedicalFitness, bool RequiresCraneCertificate, IReadOnlyList<string> RequiredAdditionalQualificationCodes,
    DateOnly PlannedStartDate, DateOnly PlannedEndDate);
```

```csharp
// Modules/Eligibility/Services/IDriverEligibilityService.cs
namespace TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Eligibility.Models;

public interface IDriverEligibilityService
{
    Task<EligibilityResult> CheckEligibilityAsync(Guid employeeId, DriverEligibilityRequest request, CancellationToken cancellationToken);
}
```

`DriverEligibilityService` loads all `EmployeeQualification` rows for
`(TenantId, employeeId)`, maps each to a `QualificationSnapshot` using
`IQualificationStatusCalculator.CalculateEffectiveStatus` (today's date,
tenant's warning window) joined against `QualificationType.Code`, then
delegates to `DriverEligibilityEvaluator.Evaluate`.

- [ ] **Step 1: Implement `DriverEligibilityService`** per the description above, constructor-injecting `TransportationDbContext`, `ITenantContext`, `IQualificationStatusCalculator`, `TimeProvider` (use `TimeProvider.System` registered via `builder.Services.AddSingleton(TimeProvider.System);` — this is the standard .NET 8+ way to make "now" testable/injectable without a bespoke `IClock` abstraction).
- [ ] **Step 2: Register `IDriverEligibilityService` in `Program.cs`.**
- [ ] **Step 3: Implement `DriverEligibilityController`** — `POST /api/driver-eligibility/check`, permission `PlanningView` (checking eligibility is a read/support operation, not a planning mutation).
- [ ] **Step 4: Build**

Run: `dotnet build TransportationService.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api/Modules/Eligibility/Services/IDriverEligibilityService.cs TransportationService.Api/Modules/Eligibility/Services/DriverEligibilityService.cs TransportationService.Api/Modules/Eligibility/Dtos TransportationService.Api/Modules/Eligibility/Controllers/DriverEligibilityController.cs TransportationService.Api/Program.cs
git commit -m "feat: wire eligibility evaluator to a DB-backed service and check endpoint"
```

---

## Task 11: Eligibility overrides

**Files:**
- Create: `Modules/Eligibility/Entities/EligibilityOverride.cs`
- Create: `Modules/Eligibility/Configurations/EligibilityOverrideConfiguration.cs`
- Create: `Modules/Eligibility/Dtos/OverrideDtos.cs`
- Create: `Modules/Eligibility/Services/IEligibilityOverrideService.cs`, `EligibilityOverrideService.cs`
- Create: `Modules/Eligibility/Controllers/EligibilityOverridesController.cs`
- Modify: `Data/TransportationDbContext.cs`
- Test: `TransportationService.Api.Tests/Eligibility/EligibilityOverrideServiceTests.cs`

**Interfaces:**

```csharp
// Modules/Eligibility/Entities/EligibilityOverride.cs
namespace TransportationService.Api.Modules.Eligibility.Entities;

public class EligibilityOverride
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

Configuration: table `eligibility_overrides`; `RelatedEntityType` max
100, `RuleCode` max 100, `Reason` required max 2000 (**required — a blank
reason must fail validation**); index on `(TenantId, EmployeeId)`; no FK
cascade to `Employee` (`DeleteBehavior.Restrict` — an override is an
audit-relevant historical record and must survive independent of any
future employee-deletion path, even though employees are never hard
deleted today).

```csharp
// Modules/Eligibility/Dtos/OverrideDtos.cs
namespace TransportationService.Api.Modules.Eligibility.Dtos;

public record EligibilityOverrideDto(Guid Id, Guid EmployeeId, string RelatedEntityType, Guid? RelatedEntityId, string RuleCode, string Reason, Guid ApprovedByUserId, DateTime ApprovedAt, DateTime? ValidUntil);
public record CreateEligibilityOverrideRequest(Guid EmployeeId, string RelatedEntityType, Guid? RelatedEntityId, string RuleCode, string Reason, DateTime? ValidUntil);
```

`IEligibilityOverrideService.CreateAsync(Guid approvingUserId,
CreateEligibilityOverrideRequest request, CancellationToken ct)` —
validates `Reason` is non-empty after trim (throws
`ArgumentException` caught by the controller as 400 — matches the
existing `TransportOrdersController` validate-in-controller-with-
BadRequest style used elsewhere, kept consistent: validation of simple
required-field rules happens in the controller before calling the
service, exactly like `TransportOrdersController.Create`); sets
`ApprovedByUserId = approvingUserId` (never trusts a client-supplied
approver — this is the resolved `ICurrentUserContext.CurrentUserId`,
passed in by the controller after the `RequirePermission` check already
proved that user holds `planning.override_restriction`), `ApprovedAt =
UtcNow`, `TenantId` from `ITenantContext`. **Does not modify or store
any "already applied" eligibility result — it is a separate record a
caller (future planning module) must explicitly consult alongside
`CheckEligibilityAsync`, never a hidden mutation of it**, per the spec's
"must not silently change or hide the original eligibility result."
Also writes an audit log entry via `IAuditService` (wired in Task 12,
so this task's `EligibilityOverrideService` takes an `IAuditService`
dependency now with a call already in place, and it's genuinely used
starting Task 12 — see note in that task about audit service being
implemented before this compiles cleanly. **Reorder note:** because of
this dependency, Task 12 (Auditing) is implemented as the first half of
this task, before `EligibilityOverrideService`. See the reordered steps
below).

- [ ] **Step 1: Implement `Modules/Auditing/Entities/AuditLog.cs`, `Modules/Auditing/Configurations/AuditLogConfiguration.cs`, `Modules/Auditing/Services/IAuditService.cs`, `AuditService.cs`** now (pulled forward from Task 12 because `EligibilityOverrideService` depends on it):

```csharp
// Modules/Auditing/Entities/AuditLog.cs
namespace TransportationService.Api.Modules.Auditing.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}
```

```csharp
// Modules/Auditing/Configurations/AuditLogConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransportationService.Api.Modules.Auditing.Entities;

namespace TransportationService.Api.Modules.Auditing.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        builder.HasIndex(a => a.Timestamp);
    }
}
```

```csharp
// Modules/Auditing/Services/IAuditService.cs
namespace TransportationService.Api.Modules.Auditing.Services;

public interface IAuditService
{
    Task RecordAsync(string entityType, string entityId, string action, object? oldValues, object? newValues, CancellationToken cancellationToken);
}
```

```csharp
// Modules/Auditing/Services/AuditService.cs
using System.Text.Json;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Auditing.Services;

/// <summary>
/// Never pass raw entities containing PasswordHash or document bytes into
/// oldValues/newValues — callers pass purpose-built anonymous objects with
/// only the fields worth auditing (see call sites for the pattern).
/// </summary>
public class AuditService : IAuditService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public AuditService(TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
    }

    public async Task RecordAsync(string entityType, string entityId, string action, object? oldValues, object? newValues, CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUserContext.CurrentUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues),
            Timestamp = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Add `DbSet<AuditLog> AuditLogs` to `TransportationDbContext`; register `IAuditService` scoped in `Program.cs`.**

- [ ] **Step 3: Implement `EligibilityOverride` entity, configuration, DTOs, `EligibilityOverrideService`** per the spec above, calling `_auditService.RecordAsync("EligibilityOverride", override.Id.ToString(), "Created", null, new { override.EmployeeId, override.RuleCode, override.Reason, override.ApprovedByUserId }, ct)` after `SaveChangesAsync`.

- [ ] **Step 4: Write failing test for the required-reason rule**

```csharp
// Eligibility/EligibilityOverrideServiceTests.cs
using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Eligibility;

public class EligibilityOverrideServiceTests
{
    [Fact]
    public async Task CreateAsync_Throws_WhenReasonIsBlank()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(Guid.NewGuid(), "TransportOrder", null, "AdrRequired", "   ", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RecordsApprovingUser_AndWritesAuditLog()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tenantContext = new DevTenantContext(tenantId);
        var auditService = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(approverId));
        var sut = new EligibilityOverrideService(db.Context, tenantContext, auditService);

        var result = await sut.CreateAsync(
            approverId,
            new CreateEligibilityOverrideRequest(Guid.NewGuid(), "TransportOrder", null, "AdrRequired", "Klant heeft alternatieve begeleiding geregeld.", null),
            CancellationToken.None);

        Assert.Equal(approverId, result.ApprovedByUserId);
        Assert.Single(db.Context.AuditLogs);
    }
}
```

- [ ] **Step 5: Run to verify first test fails to compile, then implement, then run to verify both pass.**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~EligibilityOverrideServiceTests`
Expected: 2 passed.

- [ ] **Step 6: Implement `EligibilityOverridesController`** — `POST /api/eligibility-overrides`, permission `PlanningOverrideRestriction`; `GET /api/eligibility-overrides?employeeId=` permission `PlanningView`. Controller resolves `approvingUserId` from `ICurrentUserContext.CurrentUserId` (401 if null — mirrors `RequirePermissionAttribute`'s own check, defensive since the attribute already guarantees a non-null user reached this point, but the controller doesn't blindly trust that without its own explicit check, matching "server-side enforcement" as a layered guarantee rather than a single point of trust).

- [ ] **Step 7: Build and run full suite**

Run: `dotnet build TransportationService.slnx && dotnet test TransportationService.slnx`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add TransportationService.Api/Modules/Auditing TransportationService.Api/Modules/Eligibility/Entities TransportationService.Api/Modules/Eligibility/Configurations TransportationService.Api/Modules/Eligibility/Dtos/OverrideDtos.cs TransportationService.Api/Modules/Eligibility/Services/IEligibilityOverrideService.cs TransportationService.Api/Modules/Eligibility/Services/EligibilityOverrideService.cs TransportationService.Api/Modules/Eligibility/Controllers/EligibilityOverridesController.cs TransportationService.Api/Data/TransportationDbContext.cs TransportationService.Api/Program.cs TransportationService.Api.Tests/Eligibility/EligibilityOverrideServiceTests.cs
git commit -m "feat: add audit logging and controlled eligibility overrides"
```

---

## Task 12: Wire audit logging into remaining mutations + AuditLogsController

**Files:**
- Modify: `Modules/Identity/Services/UserService.cs` (inject `IAuditService`, call `RecordAsync` in `CreateAsync`, `UpdateAsync`, `SetActiveAsync`, `SetBlockedAsync`, `AssignRolesAsync` — new/old values limited to non-sensitive fields, **never `PasswordHash`**)
- Modify: `Modules/Identity/Services/RoleService.cs` (audit create/update/deactivate/permission-assignment)
- Modify: `Modules/Employees/Services/EmployeeService.cs` (audit create/update/deactivate)
- Modify: `Modules/Qualifications/Services/QualificationService.cs` (audit create/verify/suspend)
- Create: `Modules/Auditing/Controllers/AuditLogsController.cs`
- Create: `Modules/Auditing/Dtos/AuditLogDto.cs`

**Interfaces:**

```csharp
// Modules/Auditing/Dtos/AuditLogDto.cs
namespace TransportationService.Api.Modules.Auditing.Dtos;

public record AuditLogDto(Guid Id, Guid? UserId, string EntityType, string EntityId, string Action, string? OldValuesJson, string? NewValuesJson, DateTime Timestamp);
```

- [ ] **Step 1: Add `IAuditService` as a constructor dependency to `UserService`, `RoleService`, `EmployeeService`, `QualificationService`.** For each mutating method, after a successful `SaveChangesAsync`, call `_auditService.RecordAsync(entityType, id.ToString(), action, oldValuesSnapshot, newValuesSnapshot, cancellationToken)` where `oldValuesSnapshot`/`newValuesSnapshot` are small anonymous objects built from the DTO-safe fields only (e.g. for `User`: `new { user.Email, user.FirstName, user.LastName, user.IsActive, user.IsBlocked }` — explicitly excluding `PasswordHash`).
- [ ] **Step 2: Update every `Program.cs` registration call site** for these four services — no signature change needed since DI resolves the new constructor parameter automatically; just confirm nothing manually `new`s these services anywhere (none do — verified by grep in Step 4).
- [ ] **Step 3: Implement `AuditLogsController`** — `GET /api/audit-logs?entityType=&entityId=&page=&pageSize=`, permission `AuditLogsView`, tenant-scoped, ordered by `Timestamp desc`.
- [ ] **Step 4: Verify no direct `new UserService(...)` / `new RoleService(...)` / etc. exist outside DI registration and test files**

Run: `grep -rn "new UserService(\|new RoleService(\|new EmployeeService(\|new QualificationService(" TransportationService.Api --include=*.cs`
Expected: only `Program.cs` DI lambdas (if any) — none found means constructor injection via `AddScoped<TService, TImpl>()` handles it and no call sites need updating.

- [ ] **Step 5: Update existing tests that construct these services directly** (`AdminSafeguardTests`, `EmployeeWithoutUserTests`) to pass a real `AuditService` instance (same pattern as `EligibilityOverrideServiceTests`) instead of failing to compile.

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build TransportationService.slnx && dotnet test TransportationService.slnx`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add TransportationService.Api/Modules TransportationService.Api.Tests
git commit -m "feat: wire audit logging into user/role/employee/qualification mutations"
```

---

## Task 13: Migration

**Files:**
- Create: `TransportationService.Api/Migrations/<timestamp>_MasterDataFoundation.cs` (generated)

- [ ] **Step 1: Generate the migration**

Run: `cd TransportationService.Api && dotnet ef migrations add MasterDataFoundation`
Expected: migration file created, no errors.

- [ ] **Step 2: Read the generated migration file and check for:**
      - No `DropColumn`/`DropTable` against the existing `transport_orders` table (this migration should be purely additive).
      - Every new FK with cascade behavior matches the plan (join tables cascade; content/history tables `Restrict`).
      - Unique indexes present: `(TenantId, Email)` on users, `(TenantId, Name)` on roles, `Code` on permissions, `(TenantId, EmployeeNumber)` on employees, `Code` on qualification_types, `Slug` on tenants, `TenantId` on tenant_settings.
      - Non-unique indexes present on every `TenantId` column and the composite lookup indexes called out per-entity above.
      If any of these are missing or wrong, fix the relevant `IEntityTypeConfiguration` and regenerate (`dotnet ef migrations remove` then re-add) rather than hand-editing the generated migration.

- [ ] **Step 3: Apply it against a real Postgres instance to prove it runs** (uses the `docker-compose.yml` already in the repo)

Run: `docker compose up -d db 2>/dev/null; cd TransportationService.Api && dotnet ef database update`
Expected: "Done." with no errors. If no `db` service / Docker isn't available in this environment, note this as a risk in the final report instead of claiming it was verified — do not fabricate success.

- [ ] **Step 4: Commit**

```bash
git add TransportationService.Api/Migrations
git commit -m "feat: add MasterDataFoundation migration"
```

---

## Task 14: Seed data

**Files:**
- Create: `TransportationService.Api/Data/MasterDataSeeder.cs`
- Modify: `Program.cs` (call `MasterDataSeeder.SeedAsync` alongside the existing `TransportOrderSeeder.SeedAsync`, both inside the existing `IsDevelopment()` block)

`MasterDataSeeder.SeedAsync(TransportationDbContext dbContext)`:
idempotent (guarded by `if (await dbContext.Tenants.AnyAsync()) return;`,
same pattern as `TransportOrderSeeder`). Seeds, in order: one `Tenant`
("Development Transportbedrijf", slug `dev`), its `TenantSettings`
(prefix `"MED-"`, next value 1, warning days 30, page size 25), all
`Permission` rows from `PermissionCodes.All`, the `Administrator` `Role`
(`IsSystemRole = true`) with a `RolePermission` row for every seeded
permission, one development `User` (`admin@dev.local`, `PasswordHash =
null`) with a `UserRole` linking to Administrator, and all 11
`QualificationType` rows from `QualificationTypeCodes` (`Code95`,
`Adr`, `MedicalFitness`, `DriverCard`, `CraneCertificate`,
`ForkliftCertificate`, `Alfapass` have `RequiresExpiryDate = true`;
`DrivingLicenceB/C/CE` have `RequiresExpiryDate = true`; `Other` has
`RequiresExpiryDate = false`).

- [ ] **Step 1: Implement `MasterDataSeeder.cs`** per the spec above, following the existing `TransportOrderSeeder.SeedAsync` static-method-taking-`TransportationDbContext` pattern exactly.
- [ ] **Step 2: Call it from `Program.cs`** right after the existing `await TransportOrderSeeder.SeedAsync(dbContext);` line.
- [ ] **Step 3: Run and manually verify via the API** (requires a reachable Postgres — see Task 15 for the full verification pass; if Postgres isn't reachable here, verify via a one-off SQLite `EnsureCreated` + `MasterDataSeeder.SeedAsync` call in a throwaway test instead, then delete that test if it's not a durable part of the suite — a seeding smoke test IS worth keeping, so instead add it permanently):

```csharp
// Add to TransportationService.Api.Tests/SeedingTests.cs
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests;

public class SeedingTests
{
    [Fact]
    public async Task SeedAsync_CreatesAdministratorRole_WithEveryPermission()
    {
        using var db = new SqliteTestDbContext();

        await MasterDataSeeder.SeedAsync(db.Context);

        var permissionCount = await db.Context.Permissions.CountAsync();
        var adminRole = await db.Context.Roles.FirstAsync(r => r.Name == "Administrator");
        var adminPermissionCount = await db.Context.RolePermissions.CountAsync(rp => rp.RoleId == adminRole.Id);

        Assert.True(permissionCount > 0);
        Assert.Equal(permissionCount, adminPermissionCount);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        using var db = new SqliteTestDbContext();

        await MasterDataSeeder.SeedAsync(db.Context);
        await MasterDataSeeder.SeedAsync(db.Context);

        var tenantCount = await db.Context.Tenants.CountAsync();
        Assert.Equal(1, tenantCount);
    }
}
```

- [ ] **Step 4: Run**

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~SeedingTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api/Data/MasterDataSeeder.cs TransportationService.Api/Program.cs TransportationService.Api.Tests/SeedingTests.cs
git commit -m "feat: seed Administrator role, permission catalog, qualification types, and dev tenant"
```

---

## Task 15: Full backend verification

- [ ] **Step 1:** `dotnet build TransportationService.slnx` — expect Build succeeded, 0 errors.
- [ ] **Step 2:** `dotnet test TransportationService.slnx` — expect all tests passed, note the total count.
- [ ] **Step 3:** Add and run the tenant-isolation cross-module test explicitly required by the spec:

```csharp
// TenantIsolation/TenantIsolationTests.cs
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.TenantIsolation;

public class TenantIsolationTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsNull_ForEmployeeBelongingToAnotherTenant()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "A-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var serviceForTenantA = new EmployeeService(db.Context, new DevTenantContext(tenantA));
        var created = await serviceForTenantA.CreateAsync(new CreateEmployeeRequest(
            "Jan", "Janssen", "Kerkstraat", "1", "1000", "Brussel", "BE", "+32", "jan@a.com",
            new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null),
            CancellationToken.None);

        var serviceForTenantB = new EmployeeService(db.Context, new DevTenantContext(tenantB));
        var resultFromWrongTenant = await serviceForTenantB.GetByIdAsync(created.Id, CancellationToken.None);

        Assert.Null(resultFromWrongTenant);
    }

    [Fact]
    public async Task DifferentTenants_CanUseTheSameEmployeeNumber_BecauseUniquenessIsTenantScoped()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantA, EmployeeNumberPrefix = "SAME-", EmployeeNumberNextValue = 1 });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantB, EmployeeNumberPrefix = "SAME-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();

        var serviceForTenantA = new EmployeeService(db.Context, new DevTenantContext(tenantA));
        var serviceForTenantB = new EmployeeService(db.Context, new DevTenantContext(tenantB));

        var createdA = await serviceForTenantA.CreateAsync(new CreateEmployeeRequest("Jan", "A", "S", "1", "1000", "C", "BE", "+32", "a@a.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);
        var createdB = await serviceForTenantB.CreateAsync(new CreateEmployeeRequest("Piet", "B", "S", "1", "1000", "C", "BE", "+32", "b@b.com", new DateOnly(1990, 1, 1), new DateOnly(2020, 1, 1), EmploymentStatus.Active, EmployeeFunction.DriverB, null, null, null), CancellationToken.None);

        Assert.Equal(createdA.EmployeeNumber, createdB.EmployeeNumber);
    }
}
```

Run: `dotnet test TransportationService.slnx --filter FullyQualifiedName~TenantIsolationTests`
Expected: 2 passed.

- [ ] **Step 4:** Full suite one more time: `dotnet test TransportationService.slnx`. Expected: all green. Fix anything red using systematic-debugging before proceeding — do not move to frontend work with failing backend tests.
- [ ] **Step 5: Commit**

```bash
git add TransportationService.Api.Tests/TenantIsolation/TenantIsolationTests.cs
git commit -m "test: add explicit cross-tenant isolation tests for Employees"
```

---

## Task 16: Frontend — API client + config for dev headers

**Files:**
- Modify: `TransportationService.Web/src/config/env.ts`
- Modify: `TransportationService.Web/src/api/apiClient.ts`
- Modify: `TransportationService.Web/.env.example`

- [ ] **Step 1: Read current `env.ts` to match its export style, then add:**

```ts
export const devTenantId = import.meta.env.VITE_DEV_TENANT_ID ?? ''
export const devUserId = import.meta.env.VITE_DEV_USER_ID ?? ''
```

- [ ] **Step 2: Add both env vars to `.env.example`** with a comment that they're development-only stand-ins for authentication, removed once real login exists.

- [ ] **Step 3: Modify `apiClient.ts`'s `getJson`/`postJson`** to attach `X-Dev-Tenant-Id`/`X-Dev-User-Id` headers when `devTenantId`/`devUserId` are non-empty, and add a `putJson`, `patchJson`, `deleteRequest` following the exact same try/catch/`ApiError` pattern already used by `postJson` (needed by the new features' update/deactivate/etc. calls).

- [ ] **Step 4: Build**

Run: `cd TransportationService.Web && npm run build`
Expected: build succeeds (env vars are optional/empty-safe).

- [ ] **Step 5: Commit**

```bash
git add TransportationService.Web/src/config/env.ts TransportationService.Web/src/api/apiClient.ts TransportationService.Web/.env.example
git commit -m "feat: extend API client with dev tenant/user headers and PUT/PATCH/DELETE helpers"
```

---

## Task 17: Frontend — Users feature

**Files:**
- Create: `features/users/types/user.ts`
- Create: `features/users/api/usersApi.ts`
- Create: `features/users/hooks/useUsers.ts`, `useUserMutations.ts`
- Create: `features/users/components/UsersTable.tsx` (+ `.css`), `UserForm.tsx` (+ `.css`), `RoleAssignmentPanel.tsx`
- Create: `features/users/pages/UsersPage.tsx`, `NewUserPage.tsx`, `UserDetailPage.tsx`
- Modify: `routes/AppRoutes.tsx`, `components/layout/Sidebar.tsx`

Follows the exact `transport-orders` feature conventions already
inspected: types file with the API-shape interfaces, a thin `*Api.ts`
module of one function per endpoint using `apiClient`, a `use*` hook per
page doing the `useEffect`/`useState` load pattern, page components
composing `PageHeader` + `LoadingState`/`ErrorState` + a
table/form component, feature-scoped `.css` files.

- [ ] **Step 1: `types/user.ts`** — mirror the backend `UserDto`/`RoleSummaryDto`/`CreateUserRequest`/`UpdateUserRequest` shapes exactly (TS `interface`s, camelCase field names matching the JSON serializer's default camelCase output).
- [ ] **Step 2: `api/usersApi.ts`** — `getUsers()`, `getUser(id)`, `createUser(input)`, `updateUser(id, input)`, `setUserActive(id, isActive)`, `setUserBlocked(id, isBlocked)`, `assignUserRoles(id, roleIds)`, plus `getRoles()` re-exported from the roles feature for the role-picker (import from `../../roles/api/rolesApi` once Task 18 exists — implement this file's role-list call last, after Task 18, to avoid a forward reference; note this ordering explicitly here).
- [ ] **Step 3: `hooks/useUsers.ts`** — same shape as `useTransportOrders.ts`. **`hooks/useUserMutations.ts`** — wraps create/update/activate/block/assign-roles with loading/error state, same pattern as `useCreateTransportOrder.ts`.
- [ ] **Step 4: `components/UsersTable.tsx`** — columns: name, email, roles (comma-joined badges), status badge (Actief/Inactief, Geblokkeerd — status never conveyed by color alone: pair each badge with text, matching the "status must never rely on color alone" requirement), actions (activate/deactivate, block/unblock — each behind a confirmation dialog for the deactivate/block direction since those are the destructive-feeling ones).
- [ ] **Step 5: `components/UserForm.tsx`** — first name, last name, email, employee link (optional, a plain text Guid input for now — no employee picker UI exists yet outside this task; note as TODO), customer link (optional, same treatment), inline validation mirroring `transportOrderValidation.ts`'s style (required fields, email format, max lengths matching the backend's).
- [ ] **Step 6: `components/RoleAssignmentPanel.tsx`** — checkbox list of all roles from `getRoles()`, pre-checked per the user's current roles, a Save button calling `assignUserRoles`.
- [ ] **Step 7: Pages** — `UsersPage` (table + "Nieuwe gebruiker" link, same as `TransportOrdersPage`), `NewUserPage` (form, on submit `createUser` then navigate to `/users`), `UserDetailPage` (loads one user, shows `UserForm` in edit mode + `RoleAssignmentPanel` + activate/block controls).
- [ ] **Step 8: Wire routes** in `AppRoutes.tsx`: `/users`, `/users/new`, `/users/:id`.
- [ ] **Step 9: Wire sidebar** — add a "Master Data" `<div className="nav-group-label">Master Data</div>` above a sub-list containing Gebruikers/Rollen en rechten/Personeel, inserted between the existing top-level items and "Instellingen" (read `Sidebar.css` first to match existing class naming before adding a new `.nav-group-label` style).
- [ ] **Step 10: Type-check**

Run: `cd TransportationService.Web && npx tsc -b --noEmit`
Expected: no errors (fix any before proceeding — this task isn't done until it's clean).

- [ ] **Step 11: Commit**

```bash
git add TransportationService.Web/src/features/users TransportationService.Web/src/routes/AppRoutes.tsx TransportationService.Web/src/components/layout/Sidebar.tsx TransportationService.Web/src/components/layout/Sidebar.css
git commit -m "feat: add Users management UI"
```

---

## Task 18: Frontend — Roles feature (permission matrix)

**Files:**
- Create: `features/roles/types/role.ts`
- Create: `features/roles/api/rolesApi.ts`
- Create: `features/roles/hooks/useRoles.ts`, `useRoleMutations.ts`
- Create: `features/roles/components/RolesTable.tsx` (+ `.css`), `PermissionMatrix.tsx` (+ `.css`)
- Create: `features/roles/pages/RolesPage.tsx`, `RoleDetailPage.tsx`

- [ ] **Step 1: `types/role.ts`** — mirror `RoleDto`/`PermissionDto`/`CreateRoleRequest`/`AssignPermissionsRequest`.
- [ ] **Step 2: `api/rolesApi.ts`** — `getRoles()`, `getRole(id)`, `createRole(input)`, `updateRole(id, input)`, `deactivateRole(id)`, `getPermissions()`, `assignRolePermissions(id, codes)`.
- [ ] **Step 3: `hooks/useRoles.ts`, `useRoleMutations.ts`** — same pattern as the Users feature's hooks.
- [ ] **Step 4: `components/PermissionMatrix.tsx`** — groups `PermissionDto[]` by `module` (from the flat list returned by `getPermissions()`), renders one row per `(module, action)` pair with a checkbox, a "select all in module" checkbox per module group, a single Save button posting the full checked-code set via `assignRolePermissions`. This directly satisfies "permission matrix grouped by module... checkboxes... save permissions."
- [ ] **Step 5: `components/RolesTable.tsx`** — name, description, system-role badge (system roles show a "Systeemrol" badge and their deactivate action is disabled/hidden — the safeguard is server-enforced already; the UI reflects it instead of hiding the mismatch), active/inactive status text badge, link to detail.
- [ ] **Step 6: Pages** — `RolesPage` (table, "Nieuwe rol" inline create — a role only needs name+description to create, so a modal/inline form is enough, no separate `/roles/new` route needed, simplifying the route list versus Users), `RoleDetailPage` (name/description edit + `PermissionMatrix` + deactivate button, deactivate disabled with a tooltip-style hint text for system roles).
- [ ] **Step 7: Wire routes** `/roles`, `/roles/:id` in `AppRoutes.tsx`.
- [ ] **Step 8: Type-check**

Run: `npx tsc -b --noEmit`
Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add TransportationService.Web/src/features/roles TransportationService.Web/src/routes/AppRoutes.tsx
git commit -m "feat: add Roles management UI with module-grouped permission matrix"
```

---

## Task 19: Frontend — Employees feature (incl. qualifications tab)

**Files:**
- Create: `features/employees/types/employee.ts`, `qualification.ts`
- Create: `features/employees/api/employeesApi.ts`, `qualificationsApi.ts`
- Create: `features/employees/hooks/useEmployees.ts`, `useEmployee.ts`, `useEmployeeQualifications.ts`, `useEmployeeMutations.ts`
- Create: `features/employees/components/EmployeesTable.tsx` (+ `.css`), `EmployeeFilters.tsx`, `EmployeeForm.tsx` (+ `.css`), `QualificationStatusBadge.tsx` (+ `.css`), `QualificationsTab.tsx`, `QualificationDialog.tsx` (+ `.css`), `Pagination.tsx` (+ `.css`, shared-worthy but scoped here since no other feature needs it yet — YAGNI on a shared component until a second consumer exists)
- Create: `features/employees/pages/EmployeesPage.tsx`, `NewEmployeePage.tsx`, `EmployeeDetailPage.tsx`

- [ ] **Step 1: `types/employee.ts`, `types/qualification.ts`** — mirror `EmployeeListItemDto`/`EmployeeDetailDto`/`EmployeePagedResult`/`Create/UpdateEmployeeRequest` and `EmployeeQualificationDto`/`QualificationTypeDto`/`Create/UpdateEmployeeQualificationRequest`. Include the `EmploymentStatus`/`EmployeeFunction`/`QualificationStatus` string-literal unions matching the backend's `.HasConversion<string>()` enum names exactly (`"Active" | "OnLeave" | "Suspended" | "Terminated"`, etc.) — this is the one place a mismatch would silently break at runtime, so double check spelling against the C# enum member names.
- [ ] **Step 2: `api/employeesApi.ts`** — `searchEmployees(params: { search?: string; isActive?: boolean; page: number; pageSize: number })`, `getEmployee(id)`, `createEmployee(input)`, `updateEmployee(id, input)`, `deactivateEmployee(id)`. **`api/qualificationsApi.ts`** — `getEmployeeQualifications(employeeId)`, `createEmployeeQualification(employeeId, input)`, `updateEmployeeQualification(employeeId, id, input)`, `verifyQualification(employeeId, id)`, `suspendQualification(employeeId, id)`, `getQualificationTypes()`, `getExpiringQualifications(days)`.
- [ ] **Step 3: Hooks** — `useEmployees(params)` (re-fetches on param change via a `useEffect` dependency array, same abort-on-unmount pattern as `useTransportOrders`), `useEmployee(id)`, `useEmployeeQualifications(employeeId)`, mutation hooks for create/update/deactivate/qualification actions.
- [ ] **Step 4: `components/QualificationStatusBadge.tsx`** — maps `EffectiveStatus` to a Dutch label + badge class (`Geldig`/`Verloopt binnenkort`/`Verlopen`/`In behandeling`/`Afgewezen`/`Geschorst`), each with a distinct icon/shape in addition to color, not color alone.
- [ ] **Step 5: `components/EmployeesTable.tsx` + `EmployeeFilters.tsx` + `Pagination.tsx`** — search box (debounced via a simple `setTimeout` in the hook, no extra dependency), active/inactive filter, paginated table showing number/name/function/status, links to detail.
- [ ] **Step 6: `components/EmployeeForm.tsx`** — all `CreateEmployeeRequest` fields, function/status as `<select>`s populated from a small local constant list of the enum values (Dutch labels), validation for required fields/email format/date-of-birth in the past/employment start date required.
- [ ] **Step 7: `components/QualificationsTab.tsx` + `QualificationDialog.tsx`** — table of the employee's qualifications with `QualificationStatusBadge`, an "Toevoegen" button opening `QualificationDialog` (type picker from `getQualificationTypes()`, document number, obtained date, expiry date shown/required only when the selected type's `requiresExpiryDate` is true, notes), row actions Verify/Suspend gated by nothing client-side beyond hiding buttons the user's permissions wouldn't allow (server enforces regardless — hiding is UX polish, not the security boundary).
- [ ] **Step 8: Pages** — `EmployeesPage` (filters + table + pagination + "Nieuwe medewerker" link), `NewEmployeePage` (`EmployeeForm`), `EmployeeDetailPage` (tabs: "Gegevens" showing `EmployeeForm` in edit mode + deactivate button with a confirmation dialog, "Kwalificaties" showing `QualificationsTab`).
- [ ] **Step 9: Wire routes** `/employees`, `/employees/new`, `/employees/:id` in `AppRoutes.tsx`; add "Personeel" to the Master Data sidebar group (already scaffolded in Task 17).
- [ ] **Step 10: Type-check**

Run: `npx tsc -b --noEmit`
Expected: no errors.

- [ ] **Step 11: Commit**

```bash
git add TransportationService.Web/src/features/employees TransportationService.Web/src/routes/AppRoutes.tsx
git commit -m "feat: add Employees management UI with qualifications tab"
```

---

## Task 20: Frontend verification + dev server smoke test

- [ ] **Step 1:** `cd TransportationService.Web && npm install` (only if `package.json` changed — it doesn't in this plan, so this is a no-op safety check).
- [ ] **Step 2:** `npm run lint` — expect 0 errors (warnings acceptable only if the existing codebase already had some; otherwise 0).
- [ ] **Step 3:** `npx tsc -b` — expect success.
- [ ] **Step 4:** `npm run build` — expect success, `dist/` produced.
- [ ] **Step 5:** Start the backend (`dotnet run --project TransportationService.Api`) and the frontend dev server (`npm run dev`), open the app, and click through: Users list loads, create a user, assign a role; Roles list loads, open the Administrator role's permission matrix (all checked from seed data); Employees list loads, create an employee, add a qualification, verify it, confirm the status badge updates. Fix anything broken. Stop both processes when done.
- [ ] **Step 6:** If any step above cannot be completed in this environment (e.g., no reachable Postgres for `dotnet run`), state exactly that in the final report instead of claiming it was verified.

---

## Self-Review

**Spec coverage:** Every phase (0-9) and every explicit requirement in
the design spec (tenancy, permission-based server-side authorization,
config-per-company, file storage abstraction, pure eligibility
evaluator, overrides, audit logging, migration review, seeding, tests,
React UI) maps to a task above. Frontend routes/sidebar entries match
the brief's explicit list (`/users`, `/users/new`, `/users/:id`,
`/roles`, `/roles/:id`, `/employees`, `/employees/new`, `/employees/:id`
— `/roles/new` was deliberately dropped in favor of an inline create
form, documented in Task 18 Step 6, since role creation needs only two
fields).

**Placeholder scan:** No task step says "add validation" or "handle
edge cases" without specifying which validation/which edge case; every
DTO/entity has its full field list; every safeguard has its exact
trigger condition spelled out.

**Type consistency:** `EligibilityResult`, `DriverEligibilityRequest`,
`QualificationSnapshot` signatures introduced in Task 9 are reused
verbatim (same field names/order) in Task 10's `DriverEligibilityService`
and Task 11's overrides work. `UserOperationOutcome`/`RoleOperationOutcome`
introduced in Task 4 are the exact types Task 5's controllers switch on.
`IAuditService.RecordAsync` signature introduced in Task 11 Step 1 is
the exact signature Task 12 calls from the other four services.

**Known risk flagged rather than hidden:** Tasks 13/15/20 explicitly
require reporting — not silently skipping — any step that can't be
verified because Postgres/Docker isn't reachable in the execution
environment.
