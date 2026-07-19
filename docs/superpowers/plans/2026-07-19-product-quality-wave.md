# Product-Quality Improvement Wave — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline execution, this session — author = executor). Steps use checkbox (`- [ ]`) syntax for tracking. Each sub-wave ends in a verified commit.

**Goal:** Make Master Data and Fleet workflows feel like a professional commercial TMS: coherent permissions, a single source of truth for statuses and driver/vehicle assignment, a complete customer VAT/Peppol profile, an employee-first driver workflow, and one consistent searchable-select + form-layout system.

**Architecture:** Extend the existing modular monolith (.NET 10 + EF Core/Npgsql, per-module folders) and the existing hand-rolled React UI kit (`src/components/ui`). No new backend frameworks; one new FE dev-dependency set (vitest + testing-library) for component tests. All schema changes ship as EF migrations; all new writes are audited via the existing `IAuditService` + `AuditingSaveChangesInterceptor`.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + in-memory SQLite (`SqliteTestDbContext`), React 19 + Vite 8 + TS 6, react-router 7 (migrating to `createBrowserRouter`), plain CSS with tokens.

## Global Constraints

- UI copy is **Dutch**. Permission codes stay in the existing `area.action` snake_case convention (`orders.cancel`, not `TransportOrders.Cancel`) — the spec's PascalCase names map onto these.
- Backend enforcement is authoritative; frontend `hasPermission` gating is UX only.
- Never duplicate Employee data onto Driver. Never create two writable stores for driver↔vehicle assignment.
- Migrations: `dotnet ef migrations add <Name> --project TransportationService.Api` (run from repo root; Postgres via docker-compose; dev data is disposable but migrations must be best-effort data-preserving).
- Tests: xUnit, plain `Assert`, no mocking libs, `SqliteTestDbContext` + `DevTenantContext` + `AuditService(db, tenant, new DevCurrentUserContext(null))`, `TestClock` for time. New FE tests: vitest + @testing-library/react.
- Verification per sub-wave: `dotnet test TransportationService.Api.Tests/TransportationService.Api.Tests.csproj` (focused filter first, full suite before commit), `npm run lint`, `npx tsc -b`, `npm run build`, `npm run test` (once vitest exists). Kill stray API processes before builds (Get-CimInstance matching CommandLine 'TransportationService.Api'; kill dotnet.exe wrapper AND exe).
- Do not touch `StartUp.txt` (user's personal note, gitignored).
- Commit after each verified sub-wave; message style `feat(<area>): <summary>` + Co-Authored-By trailer.

## Key design decisions (locked)

1. **Permission naming**: spec's `TransportOrders.*` ⇒ `orders.view/create/edit/delete/cancel/change_status/assign/export/manage`. `orders.manage` acts as an any-of umbrella: `RequirePermissionAttribute` gains `params string[]` any-of semantics and every orders endpoint lists its specific code + `orders.manage`.
2. **Cancel ≠ status change ≠ delete**: dedicated `POST /api/transport-orders/{id}/cancel` (+ required reason, stored in new `CancellationReason` column, audited). `Cancelled` is removed from the manual transition map so `orders.change_status` can no longer cancel. Delete stays Draft/Cancelled-only.
3. **Default roles** seeded per tenant, idempotent by name, `IsSystemRole=false` (tenant-customizable; permissions granted at creation only, never re-synced): Planner, Dispatcher, Management, Accounting, Chauffeur (Driver), Klantportaal (Customer). Administrator stays the sole system role with auto-full-catalog.
4. **Countries become global reference data**: `Country` leaves the per-tenant `LookupEntity` hierarchy → global table (Code alpha-2 unique, Alpha3, Dutch Name, IsEuMember, IsActive, SortOrder pinning BeNeLux). Seeded/synced by a new `CountrySeeder` running in ALL environments. Read-only API `GET /api/countries/options` for any authenticated user. Tenant CRUD, sidebar entry, and per-tenant seeding removed. Country values elsewhere remain ISO-code strings, validated against the global table on write (Customer, Location, Employee, TenantSettings).
5. **Vehicle/Trailer status model**: administrative `IsActive` (bool) stays; operational enum members renamed `Available / InUse / InMaintenance / OutOfService` (`Active`→`Available`, `Decommissioned`→`OutOfService` + `IsActive=false` in migration) + new `StatusReason` string shown when not Available. Planning treats InMaintenance/OutOfService as errors, InUse as warning. Labels: Beschikbaar / In gebruik / In onderhoud / Buiten dienst — the duplicate "Actief" badge dies at the label source.
6. **Driver↔vehicle assignment single source of truth = Vehicle side** (`Vehicle.FixedDriverId`, `Vehicle.CurrentDriverId`), with filtered unique indexes per tenant on both columns (a driver holds at most one fixed and one current vehicle). Driver-side columns `DefaultVehicleId`/`PreferredVehicleId`/`FixedVehiclePreference` are dropped (data migrated into `Vehicle.FixedDriverId`); `DefaultTrailerId` renamed `FixedTrailerId` (no counterpart → no drift). New `FleetAssignmentService` does atomic, transactional, audited reassignment with 409-conflict + `replaceExisting` flow; both driver-side and vehicle-side endpoints call it.
7. **Employee is the person record** (already true). Expansion: audit stamps (`IAuditableEntity`), `MobilePhone`, `PlaceOfBirth`, `NationalityCode`, `NationalRegisterNumber`†, `PreferredLanguageCode`, `Iban`†, `Bic`†, `CountryCode` (replaces free-text `Country`), `DepartmentId` FK, `ContractTypeId` FK, and **multi job-functions** via `EmployeeJobFunction` join (replaces `PrimaryFunction` enum; migration maps enum values → seeded JobFunction codes). † = confidential: served/editable only with `employees.view_confidential` (dead permission gets wired).
8. **Employee→Driver workflow**: `CreateEmployeeRequest.DriverProfile?` creates Employee + Driver atomically in one transaction; `EmployeeDetailDto.DriverId` links them; removing the driver role asks to deactivate (never deletes) the driver profile. Job function ≠ security role (helper text; roles stay in Users admin).
9. **VAT/Peppol on Customer**: `VatTreatment` enum (DomesticVat/ReverseCharge/IntraCommunitySupply/ExportOutsideEu/VatExempt/Other) separate from `DefaultVatRatePercent` (null = tenant default; non-domestic treatments default to 0). Belgian VAT checksum (mod-97) enforced only for BE numbers; foreign numbers loosely validated. Peppol ID optional, `scheme:value` shape. Invoicing default-rate chain becomes customer → tenant → 21. `CustomerReferenceRequired` enforced on order create/update; PO / signed-CMR flags surfaced as hints on the order form. No "Blocked" seed category — `IsBlocked` already models that (no duplicated state).
10. **New shared FE primitives**: `SearchableSelect` (type-to-search combobox with keyboard nav, clear, loading/empty states, permission-gated inline-create), `Tabs`, `FormSection` (responsive 1–3 col), `FormActions` (sticky), `StatusBadges` (one component for admin/operational/blocked state), `UnsavedChangesGuard` (useBlocker — requires `createBrowserRouter` migration), `CountryCombobox`, `LookupSelect` (lookup-backed SearchableSelect with inline create). `QualificationStatusBadge` folds into `Badge`.
11. **No new scope**: no external Peppol/accounting integration, no background scheduler (qualification alerts = dashboard + overview page + existing endpoints; notification producer noted as extension point), no per-category colour picker (Badge tones only — consistent with UI system), no tenant-scoped qualification types.

---

### Sub-wave 1: Shared UI foundation (frontend)

**Files:**
- Modify: `TransportationService.Web/package.json` (vitest, jsdom, @testing-library/react, @testing-library/user-event, @testing-library/jest-dom; scripts `test`)
- Modify: `TransportationService.Web/vite.config.ts` (vitest config, jsdom env, setup file)
- Create: `TransportationService.Web/src/test/setup.ts`
- Modify: `TransportationService.Web/src/routes/AppRoutes.tsx` + `src/main.tsx`/`src/App.tsx` — `createBrowserRouter(createRoutesFromElements(...))` + `RouterProvider` (ToastProvider/AuthProvider stay outside via router-level layout route)
- Create: `src/components/ui/SearchableSelect.tsx` + `.css`
- Create: `src/components/ui/Tabs.tsx` + `.css`
- Create: `src/components/ui/FormSection.tsx` + `.css`
- Create: `src/components/ui/FormActions.tsx` + `.css`
- Create: `src/components/ui/StatusBadges.tsx` (+ small css if needed)
- Create: `src/components/ui/UnsavedChangesGuard.tsx`
- Modify: `src/styles/global.css` (semantic tokens `--success`/`--warning`/`--danger`/`--info` + spacing vars; Badge.css consumes)
- Modify: `src/features/locations/components/LocationSelect.tsx` — reimplement on SearchableSelect, same public props
- Test: `src/components/ui/__tests__/SearchableSelect.test.tsx`, `Tabs.test.tsx`, `StatusBadges.test.tsx`

**Interfaces (produces):**
```ts
export interface SearchableSelectOption { value: string; label: string; description?: string; keywords?: string }
export interface SearchableSelectProps {
  id?: string; value: string | null; onChange: (v: string | null) => void;
  options: SearchableSelectOption[]; placeholder?: string; disabled?: boolean;
  isLoading?: boolean; clearable?: boolean; emptyMessage?: string; ariaLabel?: string;
  onCreate?: { label: (q: string) => string; create: (q: string) => Promise<SearchableSelectOption | null> };
}
// Tabs
export interface TabItem { id: string; label: ReactNode; badge?: ReactNode }
export function Tabs(props: { tabs: TabItem[]; activeId: string; onChange: (id: string) => void }): JSX.Element
// FormSection
export function FormSection(props: { title: string; description?: string; columns?: 1 | 2 | 3; children: ReactNode }): JSX.Element
// FormActions: sticky bar; children = buttons
export function FormActions(props: { children: ReactNode; dirty?: boolean }): JSX.Element
// StatusBadges
export interface StatusBadgesProps {
  active: boolean; activeLabels?: { active: string; inactive: string };
  operational?: { label: string; tone: BadgeTone; reason?: string | null };
  blocked?: { isBlocked: boolean; reason?: string | null };
}
// UnsavedChangesGuard
export function UnsavedChangesGuard(props: { when: boolean }): JSX.Element | null // useBlocker + ConfirmDialog + beforeunload
```

**Steps:**
- [ ] Add vitest deps + config + setup; smoke test runs.
- [ ] Migrate router to `createBrowserRouter` (route tree unchanged; verify login redirect + deep links still work via build).
- [ ] Implement SearchableSelect (combobox role, aria-activedescendant, ArrowUp/Down/Enter/Escape, filter on label+keywords case-insensitive, clear button, loading/empty rows, create-action row when `onCreate` and query non-empty → create → auto-select result).
- [ ] Implement Tabs, FormSection, FormActions, StatusBadges, UnsavedChangesGuard; add semantic color tokens; wire Badge.css to tokens (visuals unchanged).
- [ ] Reimplement LocationSelect on SearchableSelect (same props → orders form untouched).
- [ ] Tests: SearchableSelect filter/keyboard/select/clear/create-flow; Tabs switching + arrow keys; StatusBadges renders distinct labels and blocked reason.
- [ ] `npm run test`, `npm run lint`, `npx tsc -b`, `npm run build` → all green.
- [ ] Commit `feat(ui): searchable select, tabs, status badges, sticky form actions, unsaved-changes guard`.

### Sub-wave 2: Countries as global reference data

**Files (backend):**
- Rewrite: `Modules/Reference/Entities/Country.cs` (global entity: Id, Code, Alpha3, Name, IsEuMember, IsActive, SortOrder)
- Create: `Modules/Reference/Configurations/CountryConfiguration.cs` (table `countries`, unique index Code)
- Rewrite: `Modules/Reference/Controllers/CountriesController.cs` → `[Authorize]` read-only: `GET api/countries/options` → `IReadOnlyList<CountryOptionDto>`; `GET api/countries` (same data, full list) — no tenant CRUD
- Create: `Modules/Reference/Dtos/CountryDtos.cs` — `CountryOptionDto(string Code, string Alpha3, string Name, bool IsEuMember)`
- Create: `Data/CountrySeedData.cs` (full ISO 3166-1 list, Dutch names, 27 EU flags, BeNeLux+DE+FR+PL pinned via SortOrder) + `Data/CountrySeeder.cs` (`SyncAsync`: upsert by Code, all environments, every startup)
- Create: `Common/DomainValidationException.cs` + extend `Common/InvalidTenantReferenceExceptionFilter.cs` (or sibling filter) → 400 ProblemDetails
- Create: `Common/Reference/ICountryCodeValidator.cs` + impl (`IsValidAsync(code)`, cached per-request scope is fine) — used by Customer/Location/CompanySettings services on write
- Modify: `Data/ReferenceDataSeeder.cs` (remove Country block), `Data/TransportationDbContext.cs` (DbSet type unchanged name), `Program.cs` (register seeder outside dev-gate, register validator)
- Migration: `GlobalCountries` (drop per-tenant countries table incl. lookup columns, create global table)
- Test: `Tests/Reference/CountrySeederTests.cs`, `Tests/Partners/CustomerServiceTests.cs` (invalid country rejected)

**Files (frontend):**
- Create: `src/features/reference/api/countriesApi.ts` (module-level cached `getCountryOptions()`)
- Create: `src/features/reference/components/CountryCombobox.tsx` (SearchableSelect; label = `name`, keywords = `code alpha3`, value = code; props `{ id?, value: string|null, onChange, disabled?, placeholder? }`)
- Modify: `src/features/master-data/lookupRegistry.ts` (remove `landen`), Sidebar auto-updates; `CustomerForm` country + VAT country later waves use it; replace country `<select>`/inputs in `CustomerForm.tsx`, location form(s), `SettingsPage.tsx`, `TransportOrderForm.tsx` stop-country now.

**Steps:**
- [ ] Backend entity/config/controller/seeder/validator + Program wiring; remove old lookup traces (permission use of `reference_data.*` stays for languages/nationalities/contract types).
- [ ] Migration `GlobalCountries`; `dotnet ef database update` against docker Postgres.
- [ ] Tests: seeder idempotent + fills all rows + re-adds missing; options ordering (BE first); CustomerService rejects unknown `CountryCode` with DomainValidationException; valid code accepted case-insensitively (normalized upper).
- [ ] Frontend CountryCombobox + replace all country selects listed above; remove sidebar entry.
- [ ] Full backend test run + FE lint/tsc/build/test.
- [ ] Commit `feat(reference): global ISO country reference with searchable combobox`.

### Sub-wave 3: Customer categories + Peppol/VAT

**Files (backend):**
- Modify: `Modules/Partners/Entities/Customer.cs` (+9 fields per decision 9), create `Modules/Partners/Entities/VatTreatment.cs`
- Modify: `CustomerConfiguration.cs` (lengths: PeppolId 64, PeppolScheme 10, VatCountryCode 2, VatNotes 1000, InvoiceLanguageCode 10; VatTreatment string conversion; DefaultVatRatePercent precision 5,2)
- Modify: `Modules/Partners/Dtos/CustomerDtos.cs` (all four DTOs + requests), `CustomerService.cs` (validation: BE VAT mod-97 via new `Modules/Partners/Services/VatNumberValidator.cs` static class; Peppol shape `^\d{4}:.+`; rate 0–100; VatCountryCode via ICountryCodeValidator; audit unchanged pattern)
- Modify: `Modules/Orders/Services/TransportOrderService.cs` — enforce `CustomerReferenceRequired` on create/update (ValidationFailed outcome)
- Modify: `Modules/Invoicing/Services/InvoiceService.cs:197` — rate chain customer→settings→21; non-domestic treatment + null customer rate ⇒ 0
- Modify: `Data/ReferenceDataSeeder.cs` — customer categories seed: STD Standaard klant, KEY Key account, PROS Prospect, EENM Eenmalige klant, PART Partner, OA Onderaannemer, LEV Leverancier, INT Interne firma
- Migration: `CustomerVatAndPeppol`
- Tests: `Tests/Partners/VatNumberValidatorTests.cs` (valid BE incl. checksum, bad checksum, foreign passthrough, formatting normalization), `CustomerServiceTests` additions, `Tests/Orders/TransportOrderServiceTests` (reference required), `Tests/Invoicing/InvoiceServiceTests` (customer rate wins)

**Files (frontend):**
- Create: `src/features/master-data/components/LookupSelect.tsx` — SearchableSelect over `useLookupOptions(basePath)` + inline create Modal (name+code+description, code auto-slug from name, 409 handling) shown only when `hasPermission(managePermission)`; auto-select created option. Props: `{ id?, basePath, managePermission, value, onChange, placeholder?, createTitle }`
- Rewrite: `src/features/customers/components/CustomerForm.tsx` — FormSection layout (Algemeen / Contact / Adres / BTW & Peppol / Facturatievoorwaarden / Notities), LookupSelect for category, CountryCombobox ×2, VAT treatment select (6 labeled options), VAT-rate select 0/6/12/21/Aangepast(+numeric input), invoice language select, three requirement checkboxes with helper text, FormActions + UnsavedChangesGuard, active toggle with helper (edit mode)
- Modify: `CustomerDetailPage.tsx` (StatusBadges; new VAT/Peppol section; gate actions on `customers.edit/delete`), `CustomersPage.tsx` (gate create on `customers.create`), `NewCustomerPage.tsx`
- Modify: `src/features/transport-orders/components/TransportOrderForm.tsx` — customer requirement hints banner (PO/signed CMR/reference required) from selected customer detail

**Steps:** entity→DTO→service→validators→migration→seeder→invoice/order integration→backend tests→FE form rebuild→gating→FE verify→full verify→commit `feat(customers): peppol & vat profile, business categories, inline category create`.

### Sub-wave 4: Employee profile expansion

**Files (backend):**
- Modify: `Modules/Employees/Entities/Employee.cs` — implement `IAuditableEntity`; add fields (decision 7); `CountryCode` replaces `Country`; delete `EmployeeFunction.cs` usage (enum removed with join table in place)
- Create: `Modules/Employees/Entities/EmployeeJobFunction.cs` (`EmployeeId`, `JobFunctionId`; composite PK) + config; `Employee.JobFunctions` nav
- Modify: `EmployeeConfiguration.cs` (new columns, lengths; Iban 34, Bic 11, NationalRegisterNumber 15)
- Rewrite: `Modules/Employees/Dtos/EmployeeDtos.cs` — shared `PagedResult<T>`/`PageRequest`; list item gains `FunctionNames: IReadOnlyList<string>`, `DepartmentName?`; detail gains all new fields + `JobFunctionIds` + `DriverId: Guid?` (wave 5 fills usage) + confidential fields nullable
- Rewrite: `EmployeeService.cs` — filters (`isActive`, `jobFunctionId`, `departmentId`, `employmentStatus`), confidential redaction (`GetByIdAsync(id, includeConfidential)`), update ignores confidential fields when caller lacks permission (controller passes flag), job-function set replace (validate tenant lookups), audit records, validation via DomainValidationException (email shape, IBAN loose `^[A-Z]{2}[0-9]{2}[A-Z0-9]{10,30}$` when present, BIC 8/11, NRN 11 digits when present + modulo-97 Belgian check tolerant for pre-2000, country/language/nationality code existence)
- Modify: `EmployeesController.cs` — pass `includeConfidential = await authz(user, employees.view_confidential)`; same for update
- Migration: `EmployeeProfileAndJobFunctions` — add columns; create join table; data SQL: map `PrimaryFunction` → join rows via `job_functions.Code` per tenant (DriverB/C/CE→CHAUF, CraneOperator→KRAAN, WarehouseWorker→MAGM, Planner→PLAN, Dispatcher→DISP, OfficeWorker→ADMM, Mechanic→MONT, Other→none); map `Country` names→codes (België→BE, Nederland→NL, Duitsland→DE, Frankrijk→FR, Luxemburg→LU, Polen→PL, else NULL); drop `PrimaryFunction`, `Country`
- Modify: `Data/ReferenceDataSeeder.cs` — JobFunction seed adds CHAUF-B "Chauffeur B", CHAUF-C "Chauffeur C", CHAUF-CE "Chauffeur CE"
- Tests: `Tests/Employees/EmployeeServiceTests.cs` — confidential redaction, confidential preserved on update without permission, function replace, filters, IBAN/NRN validation, audit stamps set by interceptor

**Files (frontend):**
- Rewrite: `src/features/employees/` — list on shared kit (DataTable/FilterBar/ui-Pagination + function & department & status filters via SearchableSelect); delete `EmployeesTable.tsx`, `EmployeeFilters.tsx`, `components/Pagination.tsx`
- Rewrite: `EmployeeForm.tsx` — FormSections: Persoonlijk (name, birth date+place, nationality, NRN†, taal), Contact & adres (email, phone, mobile, address + CountryCombobox), Dienstverband (dates, status, department LookupSelect, contract type LookupSelect, functions = chips + SearchableSelect-to-add with inline create `job_functions.manage`, helper "Functies zijn HR-informatie en geven geen toegangsrechten; rechten beheer je bij Gebruikers."), Bank† (IBAN/BIC), Noodcontact, Notities. † sections render only with `employees.view_confidential`. FormField everywhere; FormActions; UnsavedChangesGuard.
- Modify: `EmployeeDetailPage.tsx` — shared `Tabs` (Profiel / Kwalificaties / Afwezigheden / Historie), `?tab=` query param support, deactivate via ConfirmDialog, StatusBadges (IsActive + employment-status badge "In dienst/Met verlof/Geschorst/Uit dienst"), gate actions on `employees.edit/deactivate`
- Create: `src/features/auditing/components/AuditHistoryPanel.tsx` `{ entityType, entityId }` → GET /api/audit-logs, gated `audit_logs.view` (reused by vehicle wave)

**Steps:** entity/join/config → DTOs/service/controller → migration (+db update) → seeder → tests → FE rebuild → verify → commit `feat(employees): full HR profile, multi job functions, confidential field permissions`.

### Sub-wave 5: Employee→Driver creation workflow

**Files:**
- Modify: `Modules/Employees/Dtos/EmployeeDtos.cs` — `CreateEmployeeRequest.DriverProfile: CreateEmployeeDriverProfile?` where `record CreateEmployeeDriverProfile(Guid? DriverCategoryId, string? Notes)`; `EmployeeDetailDto.DriverId`
- Modify: `EmployeeService.CreateAsync` — wraps employee+driver creation in `BeginTransactionAsync`; driver created via `IDriverService.CreateAsync`; failure rolls back both; detail lookup joins driver id (non-deleted)
- Modify: `Modules/Drivers/Services/DriverService.cs` / `IEmployeeService` DI as needed (avoid circular: EmployeeService depends on IDriverService — fine, Driver module doesn't depend on EmployeeService)
- Modify: `EmployeeService.SearchAsync` — `excludeDrivers: bool` filter (NOT EXISTS non-deleted driver)
- Modify (FE): `NewEmployeePage`/`EmployeeForm` — "Chauffeurprofiel" section: toggle "Deze medewerker is chauffeur" (auto-suggested when a selected function code starts with `CHAUF`; user-overridable) revealing category (LookupSelect `driver_categories.manage`) + notes; on save with driver → toast "Chauffeursprofiel {number} aangemaakt" + navigate `/employees/{id}?tab=kwalificaties`
- Modify (FE): `EmployeeDetailPage` Profiel tab — driver link card ("Chauffeursprofiel CH-0001 → bekijken") or create-driver button (`drivers.create`); when editing removes all CHAUF* functions while a driver profile exists → ConfirmDialog "Chauffeursprofiel deactiveren? Historiek blijft bewaard." → optional `PUT /api/drivers/{id}` isActive=false (never delete)
- Modify (FE): `NewDriverPage.tsx` — employee picker becomes SearchableSelect fed by `searchEmployees({ excludeDrivers: true })`
- Tests: atomic create (invalid driver category ⇒ no employee row), detail DriverId populated, excludeDrivers filter

**Steps:** DTO/service/transaction → controller passthrough → tests → FE workflow → verify → commit `feat(hr): one-flow employee-to-driver creation with atomic profile provisioning`.

### Sub-wave 6: Qualifications & expiry management

**Files (backend):**
- Modify: `Modules/Qualifications/Entities/EmployeeQualification.cs` — add `IssuingCountryCode: string?`; config + DTOs + service mapping; validate via ICountryCodeValidator
- Create: qualification document endpoints in `QualificationsController` — `POST api/employees/{eid}/qualifications/{id}/document` (multipart, pdf/jpg/jpeg/png, ≤10 MB → IFileStorageService, sets DocumentPath, audit), `GET .../document` (FileStreamResult), `DELETE .../document` (perm `employee_documents.delete` — wiring the dead code); service methods in `QualificationService`
- Modify: `Modules/Drivers/Services/DriverService.SearchAsync` — filters `availabilityStatus?`, `qualificationTypeId?`, `expiringWithinDays?` (any qual with expiry ≤ today+N and ≥ today), `hasExpired?`, `eligibleOnly?` (active && !blocked && no expired qual); controller query params
- Modify: `Modules/Employees/Services/EmployeeService.SearchAsync` — `qualificationTypeId?`, `qualificationStatus? (Expired|ExpiringSoon|Missing)`, `expiringWithinDays?`
- Modify: `Modules/Reporting/Services/DashboardService.cs` + DTO — `expiringQualifications30d`, `expiredQualifications` counts
- Migration: `QualificationIssuingCountry`
- Tests: upload validation (type/size), download roundtrip via fake storage? (LocalFileStorageService against temp dir), driver expiring/eligible filters, employee Missing filter, dashboard counts

**Files (frontend):**
- Rewrite: `src/features/employees/components/QualificationsTab.tsx` — shared Modal/ConfirmDialog/Badge (delete `QualificationStatusBadge`, tone map: Valid success, ExpiringSoon warning, Expired/Rejected danger, Pending info, Suspended neutral), status filter chips, expiry sort, upload/download/delete document actions, IssuingCountry via CountryCombobox in dialog
- Create: `src/features/qualifications/pages/QualificationsOverviewPage.tsx` (route `/qualifications`, sidebar "Kwalificaties" under Personeel) — 30/60/90 selector + expired table, links to employees; gated `employee_documents.view`
- Modify: `DriversPage.tsx` — filter row: category, availability, blocked, "vervalt binnen 30/60/90", eligibility toggle
- Modify: `EmployeesPage.tsx` — qualification filters; `DashboardPage` alert card
**Steps:** backend → tests → FE → verify → commit `feat(qualifications): issuing country, document upload, expiry filters and overview`.

### Sub-wave 7: Driver↔Vehicle synchronised assignment

**Files (backend):**
- Modify: `Modules/Drivers/Entities/Driver.cs` — drop `DefaultVehicleId`/`PreferredVehicleId`/`FixedVehiclePreference`; rename `DefaultTrailerId`→`FixedTrailerId`; config update
- Create: `Modules/Fleet/Services/IFleetAssignmentService.cs` + `FleetAssignmentService.cs`:
```csharp
public enum AssignmentKind { Fixed, Current }
public enum AssignmentOutcome { Success, NotFound, InvalidReference, Conflict }
public sealed record AssignmentConflict(Guid VehicleId, string VehicleLabel, Guid DriverId, string DriverName, AssignmentKind Kind);
public sealed record AssignmentResult(AssignmentOutcome Outcome, AssignmentConflict? Conflict);
Task<AssignmentResult> SetVehicleDriverAsync(Guid vehicleId, AssignmentKind kind, Guid? driverId, bool replaceExisting, CancellationToken ct);
Task<AssignmentResult> SetDriverVehicleAsync(Guid driverId, AssignmentKind kind, Guid? vehicleId, bool replaceExisting, CancellationToken ct);
```
  Core: tenant-validate both ends; `BeginTransactionAsync`; find other vehicle holding driver in `kind` → conflict unless replace (then clear); driver-side with occupied target slot → conflict unless replace; write, audit both vehicles (`assignment_changed`, old/new), commit; `DbUpdateException` (unique index) → Conflict.
- Modify: `VehiclesController` + `DriversController` — 4 endpoints (decision 6 routes `PUT api/vehicles/{id}/assignments/{fixed-driver|current-driver}`, `PUT api/drivers/{id}/assignments/{fixed-vehicle|current-vehicle}`; body `{ driverId?/vehicleId?, replaceExisting }`; perms `vehicles.edit` / `drivers.edit`; 409 + conflict payload)
- Modify: `VehicleService` — remove FixedDriverId/CurrentDriverId from `UpdateAsync`; `CreateAsync` pre-checks driver availability (conflict → DuplicateAssignment outcome → 409)
- Modify: `DriverService`/DTOs — expose `FixedVehicle`/`CurrentVehicle` `(Guid Id, string Label)?` resolved from vehicle side; `FixedTrailerId` editable
- Migration: `SynchronisedFleetAssignment` — copy driver.DefaultVehicleId → vehicle.FixedDriverId (WHERE vehicle slot empty), de-dupe both columns, drop old driver columns, rename trailer column, add filtered unique indexes `(TenantId, FixedDriverId)` / `(TenantId, CurrentDriverId)` WHERE col IS NOT NULL AND NOT IsDeleted
- Tests: `Tests/Fleet/FleetAssignmentServiceTests.cs` — set/clear both sides mirror; replace clears old vehicle atomically; conflict info without replace; tenant isolation; audit rows; unique-index backstop; VehicleService.Update no longer changes assignment

**Files (frontend):**
- Modify: `DriverDetailPage.tsx` — "Voertuigen" section: fixed + current vehicle rows with Wijzigen/Verwijderen → Modal (SearchableSelect over `/api/vehicles/options`) → 409 → ConfirmDialog met vervangen; explainer copy vast/actueel/ritplanning
- Modify: `VehicleDetailPage.tsx` — same from vehicle side (drivers options endpoint: reuse drivers list `pageSize=200 active`) — lands in Toewijzing tab in wave 8 (this wave: standalone section)
- Modify: vehicle edit form — remove driver selects; `NewVehiclePage` keeps them
**Steps:** entity/migration → service → endpoints → tests → FE both sides → verify → commit `feat(fleet): single-source driver-vehicle assignment with atomic replace flow`.

### Sub-wave 8: Vehicle status model + detail/edit UX

**Files (backend):**
- Modify: `Vehicle.cs`/`Trailer.cs` — enum members `Available, InUse, InMaintenance, OutOfService`; add `StatusReason: string?` (500); default Available
- Modify: `VehicleService`/`TrailerService` (create default, update StatusReason — cleared when Available), `PlanningConflictService` (InMaintenance/OutOfService = error, InUse = warning), `FleetDashboardService` (+labels), `Reporting/DashboardService`
- Migration: `FleetOperationalStatusModel` — SQL value remap (`Active`→`Available`; `Decommissioned`→`OutOfService` + `IsActive=false`) + new column ×2
- Tests: planning conflicts per status; dashboard counts; reason lifecycle
**Files (frontend):**
- Modify: `vehicles/types.ts`/`trailers/types.ts` label+tone maps (Beschikbaar success / In gebruik info / In onderhoud warning / Buiten dienst danger)
- Rewrite: `VehicleDetailPage.tsx` — Tabs: Overzicht (StatusBadges + kern + toewijzing + notes), Techniek, Toewijzing (wave-7 panel), Documenten, Onderhoud, Keuringen, Schade, Brandstof, Historie (AuditHistoryPanel); edit = full-parity form (all create fields + status + reason + active w/ helper) in FormSections + FormActions + guard
- Rewrite: `TrailerDetailPage.tsx` — same minus Toewijzing/Brandstof
- Modify: list pages' status labels
**Steps:** backend → migration → tests → FE rebuild → verify → commit `feat(fleet): distinct operational status model and tabbed vehicle detail with full edit parity`.

### Sub-wave 9: Transport-order permissions, cancel/export, default roles

**Files (backend):**
- Modify: `Modules/Identity/PermissionCodes.cs` — add `orders.cancel`, `orders.assign`, `orders.export`, `orders.manage` (+catalog entries, Dutch descriptions)
- Modify: `Modules/Identity/Authorization/RequirePermissionAttribute.cs` — `params string[]` any-of
- Modify: `TransportOrder.cs` + config — `CancellationReason: string?` (500)
- Modify: `TransportOrderService` — remove `Cancelled` targets from transition map; `CancelAsync(id, reason)` (allowed from Draft/Confirmed/Planned/InProgress; reason required; audit); export query method (filtered, cap 5000)
- Modify: `TransportOrdersController` — every endpoint lists specific code + `OrdersManage`; `POST {id}/cancel` (`orders.cancel|manage`); `GET export` → CSV (semicolon, UTF-8 BOM, `text/csv`) (`orders.export|manage`)
- Modify: `TripsController` — creating/updating a trip whose `OrderIds` change additionally requires `orders.assign` (inline check, pattern of override_restriction)
- Create: `Data/DefaultRoleDefinitions.cs` + `Data/DefaultRoleSeeder.cs` (per-tenant, create-if-missing-by-name, grant-on-create only; roles per decision 3 with permission sets: Planner = orders.* except delete/manage + planning view/create/edit + customers view/create/edit + locations.* + view-perms fleet/drivers/employees/absences + dashboard + lookups view; Dispatcher = planning view/create/edit + orders view/change_status/assign + driver_workflow.view + views + dashboard; Management = every `*.view` + dashboard + audit_logs.view + orders.export + absences.approve + planning.override_restriction; Accounting = invoices.* + orders.view/export + customers.view/edit + dashboard + reference views; Chauffeur = driver_workflow.* + absences.view/create; Klantportaal = orders.view) — registered in `Program.cs` next to PermissionCatalogSeeder (all envs)
- Migration: `OrderCancellationReason`
- Tests: cancel flow (status, reason stored, audit, invalid state), change_status refuses Cancelled, export CSV content + filters, RequirePermission any-of behavior (via seeded roles + PermissionAuthorizationService), DefaultRoleSeeder idempotency + doesn't overwrite tenant edits
**Files (frontend):**
- Modify: `TransportOrderDetailPage.tsx` — Annuleren button (`orders.cancel`) → Modal with required reason; transitions no longer offer Cancelled; show CancellationReason when cancelled
- Modify: `TransportOrdersPage.tsx` — Export CSV button (`orders.export`) (apiClient gains `getBlob`)
- Modify: `apiClient.ts` (+`getBlob`)
**Steps:** codes/attribute → service/controller → seeder → migration → tests → FE → verify → commit `feat(orders): granular permission model with cancel, export and seeded default roles`.

### Sub-wave 10: Final UX & security hardening

**Files (frontend):**
- Modify: `Sidebar.tsx` — nav items gain `permissions?: string[]` (any-of) filtering via `hasAnyPermission`; Stamgegevens filtered per lookup view permission; empty groups hidden; qualifications/absences entries included
- Modify: `lookupRegistry.ts` — add `viewPermission`/`managePermission` per resource; `LookupManager` gates create/edit/delete buttons
- Modify: users/roles pages — gate action buttons (`users.create/edit/block`, `roles.*`)
- Sweep: remaining `window.confirm` → ConfirmDialog in touched features; ensure every rebuilt form has required-indicators + guard + sticky actions
**Verification (full):**
- [ ] `dotnet test` (entire suite), `npm run test`, `npm run lint`, `npx tsc -b`, `npm run build`
- [ ] Live sweep: docker compose up, `dotnet ef database update`, run API, login as admin; curl: countries options; customer create with bad/good BE VAT; order create for reference-required customer (400 then ok); order cancel with reason; order export CSV; employee+driver one-flow create; driver fixed-vehicle assign → conflict → replace; vehicle status InMaintenance → planning validate shows conflict; qualification upload/download; verify seeded roles present; verify limited user (create planner user) sees filtered sidebar + 403s on forbidden API
- [ ] Commit `feat(ux): permission-aware navigation and master-data hardening` + memory update

## Self-Review notes

- Spec §1 permissions → wave 9 (naming mapped, backend-enforced, FE-gated, read-only preserved via view, cancel/delete/status separated, audit present).
- §2 categories → waves 3 (+SearchableSelect from wave 1); tenant-configurable via existing lookup CRUD; inline create with auto-select; no data loss (modal, form state kept).
- §3 VAT/Peppol → wave 3; controlled treatment dropdown separate from rate; BE-only strict validation; Peppol optional; no external integration.
- §4 statuses → waves 1 (component) + 8 (model); one lifecycle + separate operational; reasons; one shared component reused for customers/employees/drivers/vehicles/trailers.
- §5/§6 → waves 4–5; no duplication (Driver stays reference-only); functions ≠ roles helper; deactivation flow preserves history.
- §7 → wave 6 (+form restructure in 4); statuses valid/expiring/expired/missing (readiness) /revoked (suspended); filters incl. 30/60/90; dashboard alert; uploads.
- §8 → wave 7; both sides edit one relationship; atomic transactional replace; unique indexes; audits; concepts explained in UI.
- §9 → wave 8 (full-parity edit, tabs, single status treatment, history).
- §10 → wave 2 (global ISO list, EU flag, combobox everywhere, no tenant CRUD).
- §11 → wave 1 component + waves 2–8 adoption (countries, categories ×4, functions, departments, contract types, languages, nationalities via LookupSelect where present, drivers/vehicles pickers, locations).
- §12 → waves 1,3,4,8 (sections/tabs/columns/sticky/guard/required/inline validation).
- Type consistency spot-checks: `SearchableSelectOption.value:string` everywhere (lookup ids = Guid strings, countries = codes); `AssignmentKind` shared by service+controller; `PagedResult<T>` shared for employees after wave 4.
