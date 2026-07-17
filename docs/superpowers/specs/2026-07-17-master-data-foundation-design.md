# Master Data Foundation — Design Spec

Date: 2026-07-17
Status: Approved for autonomous execution (see "Process note" below)

## Process note

The commissioning prompt for this work explicitly instructs full autonomous
execution: no stopping for approval gates except for missing credentials,
irreversible production-data actions, contradictory requirements, or paid
external service selection. None of those apply here. Per
`using-superpowers` ("user instructions … take precedence over skills"),
this spec is written to document the architecture decisions the
brainstorming skill would normally negotiate interactively, and execution
proceeds straight to `writing-plans` and implementation without a human
approval pause. Every non-obvious decision below is recorded with its
rationale so it can be reviewed after the fact.

## Goal

Add the technical foundation of the Master Data module to the existing
TransportationService solution: multi-tenant Users/Roles/Permissions,
Employees, Qualifications, a driver eligibility engine, controlled
overrides, and audit logging — plus a matching React admin UI. This is a
foundation for a commercial multi-tenant TMS, not a prototype.

## Current state (inspected)

- `TransportationService.Api`: flat structure (`Models/`, `Controllers/`,
  `Data/`), single `TransportOrder` entity, EF Core + Npgsql, no service
  layer, no auth, no tests project, no tenancy.
- `TransportationService.Web`: React 19 + TS + Vite + React Router 7,
  feature-based folders, a small `apiClient` fetch wrapper, shared
  `AppLayout`/`Sidebar`/`PageHeader`/`LoadingState`/`ErrorState`.
- No test project exists anywhere in the solution.

## Architecture decisions

### 1. Modular monolith folder structure

Introduce feature modules under `TransportationService.Api/Modules/`,
each with its own `Entities/`, `Dtos/`, `Services/`, `Configurations/`
(EF `IEntityTypeConfiguration<T>`), and `Controllers/`. Modules:

- `Tenancy` — `Tenant`, `TenantSettings`, `ITenantContext`.
- `Identity` — `User`, `Role`, `Permission`, `UserRole`, `RolePermission`,
  permission/authorization services.
- `Employees` — `Employee`.
- `Qualifications` — `QualificationType`, `EmployeeQualification`,
  qualification status calculation, `IFileStorageService`.
- `Eligibility` — eligibility engine + `EligibilityOverride`.
- `Auditing` — `AuditLog`, `IAuditService`.

The existing `TransportOrder` stays where it is — it is unrelated working
functionality and is out of scope.

Rationale: the spec explicitly asks for "clear module boundaries over
generic folders" and a modular monolith over premature microservices.
Feature folders keep each module's entities/services/controllers next to
each other without introducing project-per-module overhead.

### 2. Tenancy

Add a `Tenant` entity (`Id`, `Name`, `Slug`, `IsActive`, `CreatedAt`).
Add `ITenantContext` with a single `TenantId` (Guid) property, resolved
once per request by `TenantContextMiddleware`. Development implementation
reads a trusted header `X-Dev-Tenant-Id`; if absent, falls back to a
single seeded default tenant. The middleware is registered only for the
purpose of resolving tenant context — it is explicitly documented as a
placeholder to be replaced by claims-based resolution (JWT `tenant_id`
claim) or subdomain resolution later, without changing any consuming
code (services depend on `ITenantContext`, never on the header).

All tenant-owned entities get a `TenantId` column set exclusively from
`ITenantContext` server-side; DTOs never carry an editable `TenantId`
field. Every query/update/delete in every service is filtered by
`TenantId`. Composite unique indexes are `(TenantId, <naturally unique
field>)` (e.g. employee number, role name, qualification type code is
platform-wide so stays globally unique).

### 3. Current user + permissions (server-enforced now, JWT-ready later)

Mirroring the tenant header pattern (which the brief explicitly allows
for development), add `ICurrentUserContext` with a nullable
`CurrentUserId`, resolved by the same middleware from a trusted dev
header `X-Dev-User-Id`. This is intentionally symmetric with
`ITenantContext` so both can be swapped for JWT-claims-based
implementations later without touching services or controllers.

Authorization is enforced **server-side now**, not deferred to "later
when auth exists": a `RequirePermissionAttribute(string permissionCode)`
resolves `ICurrentUserContext`, loads the user's roles → role permissions
from the database (small cached lookup), and returns 401/403 if the
code is missing. This satisfies the non-negotiable "authorization exists
only in the frontend" ban and "server-side enforcement over
frontend-only restriction" preference while authentication itself
(password verification, token issuance) remains out of scope per the
brief ("authentication itself does not need to be fully implemented
yet").

Permission codes are never hardcoded as role behavior — a role's
permissions come exclusively from `RolePermission` rows.

### 4. Configuration per company

`TenantSettings` (1:1 with `Tenant`): `Timezone`, `DefaultLanguage`,
`QualificationExpiryWarningDays` (int, default 30), `DefaultPageSize`,
`EmployeeNumberPrefix`/`EmployeeNumberNextValue` (simple numbering
format), `EnabledModulesJson` (typed wrapper `TenantModuleFlags` record
with booleans for Employees/Qualifications/Eligibility/Overrides/
AuditLog — deserialized/validated through a small config service, never
consumed as raw JSON by business logic). Disabling a module only hides
it in UI/gates new writes; it never deletes historical rows.

This intentionally does **not** attempt every configurable field listed
in the brief (branding colors/logo, full terminology overrides, document
numbering for every entity). Those are UI/branding concerns better
added when the pages that need them exist. Building typed config for
fields with no current consumer would violate "keep universal domain
rules strongly typed and only make genuine company differences
configurable" — this is recorded as a deliberate scope decision, not an
oversight.

### 5. Eligibility engine — pure core + thin service

`DriverEligibilityEvaluator` is a pure, dependency-free class:
`Evaluate(IReadOnlyList<QualificationSnapshot> qualifications,
DriverEligibilityRequest request, DateOnly evaluationDate) →
EligibilityResult`. All business rules (B/C/CE hierarchy, ADR, crane,
Code 95, medical fitness, expiry-before-transport-end, suspended/
rejected/expired handling) live here and are unit tested without a
database. `IDriverEligibilityService` (DB-backed) loads the employee's
qualifications, maps them to snapshots, and delegates to the evaluator.
This gives fast, deterministic tests for the highest-risk business logic
(priority 4 "business-rule correctness" and priority 5 "testability").

### 6. File storage abstraction

`IFileStorageService` with `SaveAsync(tenantId, category, fileName,
Stream, contentType, ct) → storageKey` and `OpenReadAsync(storageKey,
ct) → Stream`. `LocalFileStorageService` writes under
`App_Data/tenant-{tenantId}/{category}/{guid}-{sanitizedFileName}`
(gitignored). Callers never accept a client-supplied path — the returned
opaque key is what's persisted as `DocumentPath`. Swapping to Azure
Blob/S3 later means adding a new `IFileStorageService` implementation
and changing DI registration only.

### 7. Testing strategy

New xUnit project `TransportationService.Api.Tests`:
- Pure unit tests for `DriverEligibilityEvaluator` (no DB) — covers all
  Phase 8 required rules.
- Service/integration tests against EF Core with the SQLite in-memory
  provider (`DataSource=:memory:`, connection kept open for the test's
  lifetime) — this (unlike the EF InMemory provider) enforces real
  unique indexes and relational constraints, so tenant-isolation and
  uniqueness tests are meaningful.
- Tenant-isolation tests: create two tenants, assert cross-tenant reads/
  writes are rejected/filtered for Users, Roles, Employees,
  Qualifications.
- Safeguard tests: last active administrator cannot be deactivated or
  lose the role; employee can exist with no linked user.

### 8. Migration & seeding

One consolidated EF Core migration for all new tables. Seeder extended
(idempotent, guarded by existence checks like the current
`TransportOrderSeeder`) to add: a default development `Tenant`, the
`Administrator` system role with every permission, the full permission
catalog, standard `QualificationType` rows (platform-wide, not
tenant-scoped), and one development administrator `User` — no password
hash is seeded (`PasswordHash` stays null until real auth exists), so
this seeded account is not usable for real login, only for exercising
the dev-header-based authorization path locally.

### 9. Frontend

New features `features/users`, `features/roles`, `features/employees`
following the existing `transport-orders` feature's conventions exactly
(hooks fetch via `apiClient`, pages compose `PageHeader` +
`LoadingState`/`ErrorState` + a table/form component). Sidebar gets a
new "Master Data" group with Gebruikers / Rollen en rechten / Personeel,
inserted above the existing items without changing them. The dev
API client is extended to attach the `X-Dev-Tenant-Id` / `X-Dev-User-Id`
headers (values from `src/config/env.ts`, defaulting to the seeded
dev tenant/admin) so the UI can exercise permission-gated endpoints
without real login.

## Explicit assumptions

- No real authentication (password verification / JWT issuance) is
  implemented in this task, per the brief. The data model
  (`PasswordHash` nullable, `ICurrentUserContext` abstraction) is ready
  for it.
- "Planning" module/endpoints beyond the eligibility check and override
  support endpoints are out of scope (explicitly deferred by the brief).
- Branding (logo/colors) and full terminology overrides are deferred —
  no current UI consumes them yet; see decision 4.
