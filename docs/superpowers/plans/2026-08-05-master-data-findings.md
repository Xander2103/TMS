# Master Data Stabilization Wave — Phase 1 Findings & Implementation Plan

> **For agentic workers:** execute phase-by-phase per the plan at the bottom; each phase = implement → targeted tests → review → fix → rerun → commit.

Status: audit COMPLETE (6 exploration agents, baseline suites). Baseline: backend 1703/1703 green; frontend baseline running.

## Findings report (spec §1, 14 points)

1. **Customer model**: `Customer : AuditableTenantEntity` (soft delete, no concurrency token); single inline address; general contact = Email/PhoneNumber/Website (no fax — do NOT add); memo = `Notes`; number from TenantSettings counter; unique (TenantId, CustomerNumber) filtered.
2. **Contact model**: `CustomerContact` with free-text Role + ContactDepartment lookup + single IsPrimary (code-demote only); no contact-type concept; id-preserving per-row endpoints gated `customers.edit`.
3. **Address/location model**: customer inline address for the HQ + separate `Location` module (`Location.CustomerId`), 13-type enum, free-text OpeningHours, partial instructions/restrictions; missing gate/access code/dock/route description/driver instructions/internal memo/height/weight/ADR/crane/forklift/handling minutes/windows/external ref/contact mobile/contact link.
4. **Addresses vs operational locations**: same entity (`Location`) — correct architecture; reuse and extend.
5. **Location fields**: see #3; `LocationService` already transactional; options endpoint already customer-scoped.
6. **Opening hours**: free text only (Location.OpeningHours max 500); structured hours exist only on Warehouse (single OpensAt/ClosesAt) with a DockPlanning "OutsideOpeningHours" conflict as pattern.
7. **Stop snapshot behavior**: NONE — live join everywhere (order detail, trips, driver app, operations); master edits rewrite history. Stops wholesale-replaced on update (ids change). Inline fields only used for free-address stops.
8. **Customer create/edit mismatch**: create-only InitialContact (single) + CustomerNumber; update-only IsActive; `PurchaseOrderPolicy` in no DTO and silently desynced by customer PUT (bool set, enum not).
9. **Contact create/edit mismatch**: create = 1 inline contact, edit = unlimited via modal panel; no repeater at create.
10. **Employee create/edit mismatch**: driver section id/label differ (chauffeursprofiel vs chauffeursgegevens); create posts driverCategoryIds[] but update DTO has no DriverProfile and edit panel exposes single category; notes/verlofsaldo placeholders at create (acceptable); qualifications inline at create vs self-saving tab at edit.
11. **Notes/history architecture**: EmployeeNote entity complete (pin/soft delete/audit/permissions); legacy Employee.Notes STILL WRITTEN by create/update contradicting docs; employee history = read-time projection over append-only audit_logs with write-time name resolution + masking — solid; customers have NO history endpoint and a too-narrow Updated diff.
12. **Permission/tenant risks**: contacts ride on customers.edit (OK); no sensitive-access-data permission for locations (needed for access codes); tenant filter global + hand-rolled Ensure*InTenantAsync per service; TenantReferenceGuard helper underused; role template v24 (docs stale at 23; v24 seeder test missing).
13. **Migration risks**: none pending — all 134 applied; manual `dotnet ef database update`; additive-only rule; SQLite EnsureCreated in tests (migrations not exercised there); audit_logs append-only trigger is Npgsql-only.
14. **Plan**: see Implementation plan section at bottom.

## Global stack facts

- Frontend has **no React Query** — hand-rolled `useState`/`useEffect` + reload tokens (`usePagedQuery`, per-hook `reload()`).
- SectionedForm primitive drives sectioned nav; `panel: true` sections hide the shared Save bar.

## Employee frontend (verified)

Files: `features/employees/pages/{EmployeesPage,NewEmployeePage,EmployeeDetailPage}.tsx`, `components/EmployeeForm.tsx` (shared create/edit, `mode` prop), `components/employeeSections.ts`.

Defects / gaps vs spec:

1. **Over-blocking validation** (`EmployeeForm.tsx:156-176`): email, phoneNumber, dateOfBirth, employmentStartDate, street, houseNumber, postalCode, city are ALL hard-required. Spec §13: only first/last name (+ employee number/type if required) may block.
2. **No "Opslaan en nieuwe"** anywhere in codebase; no top save bar (FormActions supports `position="top"`, unused).
3. **Driver section id/label mismatch**: create `chauffeursprofiel`/"Chauffeursprofiel" vs edit `chauffeursgegevens`/"Chauffeursgegevens"; deep-link parity broken.
4. **Driver multi-category parity gap**: create posts `driverProfile.driverCategoryIds[]`; edit's DriverProfilePanel exposes a single `categoryId` — multi-category selection cannot be re-edited.
5. `panel: true` only on edit extras → create sections show Save bar, edit sections don't (inconsistent, by design for self-saving panels but ids differ).
6. Section error-badge map gaps: identityCardNumber, civilStatus, dimonaNumber, dependentChildren, employmentEndDate, mobilePhone, placeOfBirth + extra-section keys map to no section → server errors route nowhere.
7. `employmentEndDate` rendered in HR section instead of Dienstverband.
8. Emergency contact #1 marked visually required but never validated (OK per spec — must NOT be required; remove the visual `required`).
9. FIELD_LABELS only maps 5 fields; other server errors show bare paths.

Working well (verify, keep):
- Multiple notes panel complete: add/edit/pin/unpin/delete + ConfirmDialog + perms (`employee_notes.view/manage/pin`), endpoints `/api/employees/{id}/notes[...]`.
- History panel: category chips, old/new table, actor, Dutch labels resolved server-side, pagination 25.
- Driver toggle only on create; CHAUF* job-function auto-suggest (one-shot); non-drivers never hit driver validation.
- UnsavedChangesGuard; ValidationSummary with first-error section routing.

Test gaps: no validate() message tests beyond one, no driver-toggle tests, no `mode="edit"` prefill test, no create/edit parity test.

## Employee backend (verified)

Files: `Modules/Employees/{Entities,Services,Controllers,Dtos,Configurations}`; drivers in `Modules/Drivers`; qualifications in `Modules/Qualifications`; history read-model `EmployeeHistoryService` over append-only `audit_logs`.

Facts:
- Employee: no soft delete on entity; IsActive flag; number generated from TenantSettings prefix+counter via `TenantNumbering.SaveWithClaimedNumberAsync`; unique (TenantId, EmployeeNumber). Job title = JobFunction lookup m2m (no free-text). IsDriver derived from Driver row existence.
- Encryption: NRN + IdentityCardNumber only (`enc:v1`); Iban/Bic plaintext but permission-gated + masked in audit.
- No FluentValidation anywhere — hand-written service validation throwing `DomainValidationException` (Dutch, optional field path).
- Emergency contacts: wholesale-but-id-preserving sync (`SyncEmergencyContacts:590-672`); omitted collection → legacy pair fallback; legacy columns mirrored.
- Driver create requires nothing but EmployeeId; licence/Code95/medical are qualifications feeding eligibility, never blocking. Employee+driver+qualifications created in one transaction.
- History: full-snapshot audit entries, names resolved at write time, confidential masked at write AND read, category map incl. Notities, page-size cap 1000. Solid.
- Notes: full EmployeeNote entity (soft delete, pin) + endpoints + Dutch validation; audit actions Created/Updated/Deleted/Pinned/Unpinned. Solid.

Defects vs spec:
1. **Over-required create (root cause, both layers)**: `CreateEmployeeRequest` has 11 non-nullable positional params (FirstName, LastName, DateOfBirth, Street, HouseNumber, PostalCode, City, PhoneNumber, Email, EmploymentStartDate, EmploymentStatus) → implicit [Required] via NRT binding; plus `ValidateRequired` requires email. Spec §13: only first/last name may block. Fix: make all but names nullable in Create/Update DTOs, adjust entity columns (DateOfBirth, EmploymentStartDate, address, phone, email → nullable) + migration, keep email-format-when-supplied.
2. **Update DTO lacks DriverProfile** — driver categories set at create not editable via employee update; edit-side DriverProfilePanel only single categoryId (check drivers agent... actually DriverDtos: Update supports? categories join exists; frontend panel exposes single). Fix on driver panel/API for multi-categories.
3. **Legacy `Employee.Notes` still written** by Create/UpdateAsync despite XML doc saying read-only post-migration (migration 20260729234002 backfilled notes). Decide: stop writing legacy field (keep returning it read-only) to avoid dual sources.
4. EmployeeQualification: no CreatedBy/soft-delete, duplicate types allowed (minor).
5. Create/Update param order differs (positional hazard) — normalize when touching DTOs.

## Customer backend (verified)

Files: `Modules/Partners/{Entities,Dtos/CustomerDtos.cs,Services,Controllers,Configurations}`; locations in `Modules/Locations`.

Model facts:
- `Customer : AuditableTenantEntity` (soft delete, no concurrency token). Single inline address (Street/HouseNumber/PostalCode/City/CountryCode). Number gen via TenantSettings `KL-` prefix + counter (`TenantNumbering.SaveWithClaimedNumberAsync`). Unique (TenantId, CustomerNumber) filtered.
- `CustomerContact`: FirstName/LastName required, DisplayName, Nickname, free-text `Role`, `DepartmentId`→ContactDepartment lookup, Email/Phone/Mobile, PreferredLanguageCode, single `IsPrimary` (code-only demote in `DemoteOtherPrimaries`), IsActive, Notes. **No contact-type concept, no per-type primary, no DB constraint.** Contacts CRUD = per-row endpoints (id-preserving); NOT part of customer update. EF quirk: must Add via DbSet not navigation (`CustomerService.cs:436-439`).
- `Location : AuditableTenantEntity` in Modules/Locations, `CustomerId?` link. Fields: Code (required, tenant-unique), Name, `LocationType` enum (13 values: CompanySite, Depot, Warehouse, CustomerLocation, Terminal, LoadingLocation, UnloadingLocation, ParkingLocation, Office, RegisteredOffice, AdministrativeAddress, BillingAddress, ReturnsAddress), address, Lat/Lon, ContactName/Phone/Email, **OpeningHours = free text max 500**, Loading/Unloading/AccessInstructions, Access/Vehicle/TrailerRestrictions, AlfapassRequired, AppointmentRequired, IsActive, IsDefaultLoading/Unloading/BillingLocation (filtered unique per customer), Notes. **Missing vs spec: gate, access code, reception, dock, route description, driver instructions, internal memo, height/weight restriction, ADR allowed, crane required, forklift available, loading/unloading minutes, preferred window, earliest/latest arrival, external reference, contact mobile, link to CustomerContact, structured opening hours.** LocationService already uses transactions.
- No structured opening hours anywhere; Warehouse has single OpensAt/ClosesAt (TimeOnly) used by DockPlanningService "OutsideOpeningHours" conflict — pattern to copy.
- Validation: NO FluentValidation. Manual controller guards return `{message}` BadRequest (inconsistent!) + service `DomainValidationException` → ProblemDetails w/ errors dict (`DomainValidationExceptionFilter`).
- Customer module uses NO transactions (create customer+contact = one SaveChanges, audit = separate SaveChanges).

Defects:
1. **DTO parity**: Create-only: `InitialContact` (single!), CustomerNumber. Update-only: IsActive. `PurchaseOrderPolicy` enum in no DTO.
2. **PurchaseOrderPolicy desync bug**: customer form PUT sets `PurchaseOrderRequired` bool but never `PurchaseOrderPolicy`, while invoicing reads the enum; `CustomerBillingConfigService.SetPoPolicyAsync` sets both. Fix in `ApplyVatAndPeppolProfileAsync`.
3. **Audit whitelist too narrow**: `Updated` old/new diff covers only Name/IsActive/CategoryId/VatTreatment/VatNumber/PeppolEnabled/PeppolDeliveryPreference/BuyerReference — address/email/phone/bank/paymentterm/notes NOT captured.
4. No customer history endpoint (only generic /api/audit-logs gated audit_logs.view).
5. CustomerContactDto positional `IsActive` default-true hazard on omission.
6. Customer permissions: customers.view/create/edit/delete/deactivate/override_number/manage_surcharge/manage_po/manage_communication/import; locations.* separate (check infra report).

## Customer frontend (verified)

- `CustomerForm.tsx` (1190 lines) shared create/edit; SectionedForm `orientation="top"`; 10 sections: algemeen, contact (optional), adressen, contactpersonen (panel on edit), fiscaal, bank, facturatie, communicatie (panel), tarieven (panel), notities. Detail read-view uses classic Tabs (general/contacts/locations/communication/billing/messages). No Historiek.
- Create: single "Eerste contactpersoon" inline (6 fields) → `initialContact`; NO multi-contact repeater. Edit: `CustomerContactsPanel` DataTable + Modal, per-row endpoints. 
- Locations: `CustomerLocationsPanel` only in edit/detail; "+ Adres toevoegen" NAVIGATES AWAY to /locations/new?customerId=… (breaks flow); nothing at create; `LocationQuickCreateDialog` exists (used from order form picker).
- No Opslaan-en-nieuw; single bottom save bar; no top bar. Error mapping via problemDetails.ts + ValidationSummary + per-field FormField; first-error section routing works.
- No React Query; `useCustomer` reload-token; form state init once at mount (works because detail page early-returns while loading).
- Kit: Modal, ConfirmDialog, DataTable, FilterBar, Tabs, Badge, FormSection (columns), FormField, FormActions (supports top), UnsavedChangesGuard, SearchableSelect, CountryCombobox, LookupSelect, toasts. No Drawer.
- Tests: customerSectionedForm, contacts panel, peppol, pricing panels; NO tests for NewCustomerPage/CustomersPage/CustomerLocationsPanel.

## Order stops / snapshot (verified)

- `TransportOrderStop`: LocationId (FK, SetNull on location delete) + inline fallback fields (LocationName/Address/PostalCode/City/CountryCode) used ONLY when no master location; time windows ×4, AppointmentRequired/Reference, TimeRequirement kind/from/to, IncludedTimeMinutesOverride, Reference, Instructions, Access/Loading/UnloadingInstructions ("empty → master applies"). **No contact/phone on stop. No snapshot — live join in MapDetailAsync, TripExecutionService (instructions fallback + AppointmentRequired OR), OperationsOverviewService.** Master edits DO rewrite historical orders (spec §10 violation, root cause for Phase 7).
- Stops wholesale-replaced on every update (`RemoveRange(order.Stops)` + rebuild; soft-deleted trail; cargo remapped by index) → stop IDs change every save; POD/exceptions reference raw stops; several consumers read raw `stop.LocationName ?? City` → degraded labels for master-location stops (PodService, EtaService, ExecutionExceptionService, PackageService...).
- LocationSelect combobox already scopes options: active AND (CustomerId == cust OR CustomerId == null); tiny option payload; inline quick-create exists.
- No opening-hours validation on orders anywhere. Pricing consumes StopTimeRequirements; `StopTimeRequirementsChanged` detection compares Deleted vs new stop rows (relevant if switching to id-preserving sync).
- Snapshot precedent exists: invoicing snapshots, pricing snapshot.

## Key architecture decisions (draft, confirm before Phase 2)

1. **Reuse Location module** for customer locations & delivery addresses; extend entity additively (gate/access code/reception/dock/route description/driver instructions/internal memo/height/weight/ADR/crane/forklift/loading+unloading minutes/preferred window/earliest+latest arrival/external reference/contact mobile + optional CustomerContactId link). Access code sensitive → permission + masked audit.
2. **Structured opening hours**: new `LocationOpeningInterval` entity (LocationId, DayOfWeek, From, To, Note?); day w/o intervals = closed when any structured hours exist; keep legacy free-text field for display/fallback + migrate nothing destructively.
3. **Contact types**: add enum `CustomerContactType` (Algemeen, Planning, Facturatie, Magazijn, Directie, Operationeel, Overig) stored as string; IsPrimary becomes primary-per-type; partial unique index (TenantId, CustomerId, ContactType) WHERE IsPrimary AND NOT IsDeleted; existing primaries → type Algemeen.
4. **Nested create**: extend CreateCustomerRequest with `Contacts[]` (supersedes InitialContact, keep it working) and `Locations[]`; wrap in transaction; delegate location creation to LocationService.
5. **Stop snapshot**: add snapshot columns to stop (locationName/address incl. street split?, contactName/Phone/Mobile/Email, openingHours summary, gate, accessCode, dock, routeDescription, instructions, appointmentRequired, restrictions, loading/unloadingMinutes); server copies from Location on selection; re-copy endpoint/flag with audit; backfill migration freezes current live-join values for existing stops (display-identical → then frozen). Consider id-preserving stop sync (spec §18) — decide in Phase 7 after checking POD/execution FK impact.
6. **Employee minimal create**: relax entity + DTO nullability (DateOfBirth, EmploymentStartDate, address, phone, email); migration; keep email-format-if-supplied.
7. **Customer history**: slim CustomerHistoryService reusing employee-history projection pattern over audit_logs + widened Updated diff; Historiek tab on customer.
8. **Quick-entry UX**: merge algemeen+contact+contactpersonen+adressen into one "Klantgegevens" section (subsections via FormSection); contacts repeater at create + panel at edit; locations list + LocationQuickCreateDialog (extended) inline at create&edit; save top+bottom; "Opslaan en nieuwe klant".

## Cross-cutting infrastructure (verified)

- Permissions: `PermissionCodes.cs` catalog + `PermissionCatalogSeeder` + `RequirePermissionAttribute` (any-of). Role templates: `DefaultRoleDefinitions` + `DefaultRoleUpgrades.CurrentVersion = 24` + `role_template_states`; pattern = add VersionNN step + `DefaultRoleSeederTests.VersionNN_…` fact. `Phase8SupplyChainTests` asserts every code enforced (update `ServiceSideEnforcedCodes` for service-side gates). Stale: docs/permissions.md says v23; no Version24 test.
- Existing codes: customers.view/create/edit/delete/deactivate/import/override_number/manage_fiscal/manage_communication/manage_surcharge/manage_po; locations.view/create/edit/delete; employees.view/create/edit/deactivate/view_confidential/anonymize; employee_notes.view/manage/pin; employee_documents.*; contact_departments.view/manage. Contacts = customers.edit; employee history = employees.view.
- Tenancy: global AND-ed query filter (`ApplyGlobalTenantFilters`, null tenant = open for seeders); TenantId set explicitly in services; `TenantReferenceGuard.EnsureBelongsToTenantAsync` helper (only 4 call sites) + hand-rolled Ensure*InTenantAsync convention; `InvalidTenantReferenceException` → 400 "Ongeldige referentie". `IgnoreQueryFilters()` bypasses tenant+soft-delete both.
- Audit: `AuditService.RecordAsync` (never raw entities; purpose-built anonymous objects); append-only PG trigger (Npgsql only); `MaskConfidential` pattern in EmployeeService (`•••{suffix}`); AuditingSaveChangesInterceptor stamps + soft-deletes.
- Error contract: `DomainValidationException(field, message)` (camelCase paths like `stops[0].city`) → ProblemDetails Title "Validatiefout" + `errors` dict; frontend `problemDetails.ts` normalize/extract/getFieldError. Some controllers still return ad-hoc `BadRequest(new {message})` — normalize when touched.
- Backend tests: xUnit + in-memory SQLite + real services (`SqliteTestDbContext`, `EnsureCreated`, `CreateContextForTenant` for cross-tenant); ~1626 facts/210 files; baseline 1703 pass. Run: `dotnet test TransportationService.Api.Tests/TransportationService.Api.Tests.csproj`.
- Frontend: vitest + jsdom + testing-library, nl-BE pinned; 139 test files; scripts: `npm test`, `npm run lint`, `npx tsc -b`, `npm run build`.
- Migrations: 134 files, ALL applied to live dev DB (docker up); manual `dotnet ef database update --project TransportationService.Api`; no auto-migrate.
- Seeding: Development-only block in Program.cs (MasterDataSeeder tenant `dev`, PermissionCatalogSeeder, ReferenceDataSeeder, DefaultRoleSeeder, LegalEntity/ExpiryPolicy/IssuedItemTemplate seeders, DevAdminSeeder admin@dev.local/Admin123!). Demo-data seeder to be added in same gated block (idempotent, marker-based).

## Implementation plan (phases → commits)

**Phase 2 — Customer backend parity + audit + history** (commit `feat(customers): …`)
- `CustomerDtos.cs`: add `Contacts: List<CustomerContactInput>?` to Create (keep InitialContact working; InitialContact folded in service); expose `purchaseOrderPolicy`?? NO — keep billing panel authoritative; instead fix desync: `ApplyVatAndPeppolProfileAsync` maps bool→enum (`None`/`Always`) only when the enum is at its bool-equivalent value, mirroring SetPoPolicyAsync inverse; verify with test.
- Wrap CreateAsync in transaction (customer + contacts + audit) per §18.
- Widen `Updated` audit diff: address, email, phone, website, invoiceEmail, paymentTermDays, languages, bank (masked iban), notes-presence.
- New `CustomerHistoryService` + `GET /api/customers/{id}/history` (customers.view) reusing employee-history projection (categories: Klant, Contactpersonen, Locaties); Dutch labels.
- Tests: contacts-on-create (multiple), transaction rollback, audit diff coverage, history endpoint, tenant isolation.

**Phase 3 — Contact types + primary-per-type + multi-contact UI** (commit)
- `CustomerContact.ContactType` enum string (Algemeen default, Planning, Facturatie, Magazijn, Directie, Operationeel, Overig) + migration + partial unique index (TenantId, CustomerId, ContactType) WHERE IsPrimary AND NOT IsDeleted; service demotes within type; validation message on conflict.
- Deactivate-vs-delete rule: block hard delete when referenced (communication rules, portal accounts?) → Dutch message + deactivate path; audit ContactDeactivated.
- Frontend: contacts repeater at create (temp-key rows, posted via create `contacts[]`); edit panel gains type column/filter + primary-per-type badge; ContactDialog gains type select.
- Tests backend (primary conflict, dedupe, deactivate) + frontend (repeater add/remove preserves rows, dialog).

**Phase 4 — Location domain extension** (commit)
- `Location` additive fields: HouseNumber split? (Address currently Street/HouseNumber? — Location has Street/HouseNumber/… verify), `ExternalReference`, `ContactMobile`, `CustomerContactId?` (SetNull), `Gate`, `AccessCode` (sensitive), `ReceptionPoint`, `Dock`, `RouteDescription`, `DeliveryByAppointmentOnly` (bool), `HeightRestrictionM decimal?`, `WeightRestrictionT decimal?`, `AdrAllowed bool?`, `CraneRequired bool`, `ForkliftAvailable bool`, `DriverInstructions`, `InternalMemo`, `DefaultLoadingMinutes int?`, `DefaultUnloadingMinutes int?`, `PreferredWindowFrom/To TimeOnly?`, `EarliestArrival/LatestArrival TimeOnly?` + migration + DTO/service parity + Code optional (auto-generate `LOC-` when blank).
- Delete/deactivate rules: block delete when referenced by stops (Dutch message per spec §8), offer deactivate; inactive hidden from options endpoint.
- Duplicate action (copy w/o code); search endpoint filters (name/code/city/postal/country/type/active).
- Permission `locations.view_sensitive` (access code) + v25 role step + Version25 seeder test + Phase8 map + docs/permissions.md (fix stale 23→25).
- AccessCode masked in audit (`•••`), excluded from list DTOs, gated in detail DTO.

**Phase 5 — Opening hours structured** (commit)
- `LocationOpeningInterval` entity (Id, TenantId, LocationId, DayOfWeek 1-7, From, To, Note?) + config (index TenantId+LocationId) + migration; DTOs nested on location create/update/detail; validation from<to + no overlap per day; legacy free-text kept as `OpeningHoursNote`? — keep existing `OpeningHours` string as-is (display fallback), no destructive change.
- `IOpeningHoursEvaluator` domain service: given intervals + local DateTime → InsideHours/BeforeOpening/AfterClosing/ClosedDay + relevant windows (reusable for orders).
- Frontend: compact weekly editor component (per-day rows, closed toggle, multiple intervals, copy-Monday, all-weekdays-same) used in location form; tests.

**Phase 6 — Customer quick-entry UX** (commit)
- CustomerForm restructure: section 1 `klantgegevens` "Klantgegevens" containing FormSections Bedrijfsgegevens / Algemene contactgegevens / Contactpersonen / Locaties & adressen; keep fiscaal/bank/facturatie/communicatie/tarieven/notities (+ new historiek on detail); update CUSTOMER_SECTION_FIELD_KEYS; no horizontal scroll.
- Locations at create: staged-locations repeater (prepared pattern like employees' preparedFollowUp) or nested create (backend `Locations[]` on CreateCustomerRequest delegating to LocationService in same transaction) — prefer nested create; edit: inline LocationEditorDialog (extended quick-create) instead of navigate-away.
- Save actions top+bottom (`FormActions position="top"`), `Opslaan en nieuwe klant` (create mode), same for employees Phase 8.
- Frontend tests: minimal create, multi-contact create, multi-location create, save-and-new.

**Phase 7 — Stop snapshot** (commit)
- Stop snapshot columns: ContactName, ContactPhone, ContactEmail, OpeningHoursSummary, Gate, Dock, RouteDescription, AppointmentRequired(already), Restrictions summary, DefaultLoading/UnloadingMinutes, + populate existing LocationName/Address/PostalCode/City/CountryCode for master-location stops (stop treating as "fallback only"); `SnapshotTakenAt`, `LocationVersionNote`? Keep minimal per spec list. AccessCode NOT copied to stop — resolved live with permission at trip-doc time? Spec §10 says snapshot access code too — copy but expose only via gated endpoints; decide at implementation with masking in DTOs.
- Copy server-side in BuildStops when LocationId set and (new stop or LocationId changed or explicit `resnapshot` flag); carry over snapshot on unchanged stops during wholesale rebuild (match by previous stop Id passed through StopInput.Id? add optional Id to StopInput and preserve — do NOT switch to full id-preserving sync in this wave; carry snapshot via previous-row lookup).
- Backfill migration: for stops with LocationId, copy current master values (display-identical freeze).
- Re-copy: `POST /api/transport-orders/{id}/stops/{stopId}/resnapshot`... stops are rebuilt wholesale → instead a `RefreshSnapshot` flag on StopInput + UI action w/ warning; audit.
- UI: "Overgenomen van klantlocatie {name}" indicator + "Opnieuw overnemen" + warning; collapsed stop shows snapshot name.
- Opening-hours warning: evaluator vs planned times → warnings list in detail DTO + order form display; non-blocking.
- Live-join removal: MapDetailAsync/TripExecutionService/Operations fall back to snapshot first, live join only for legacy nulls; degraded-label consumers (Pod/Eta/etc.) now read populated LocationName.
- Tests: snapshot copy, master edit does not change old order, re-copy updates only targeted order, warning cases (before/after/closed/multi-interval).

**Phase 8 — Employee minimal create + form parity** (commit)
- Entity/migration: DateOfBirth, EmploymentStartDate → nullable; Street/HouseNumber/PostalCode/City/PhoneNumber/Email already string-nullable? make DTO params nullable regardless; keep EmploymentStatus default Active.
- Service `ValidateRequired`: only first/last name; email format when supplied; employment date range check (end ≥ start when both).
- Frontend validate(): drop email/phone/dob/address/startdate requirements; keep format checks when supplied; fix section-field-key gaps; unify driver section id `chauffeursgegevens` both modes; move employmentEndDate to Dienstverband; remove visual `required` on emergency contact; extend FIELD_LABELS.
- Save top+bottom + "Opslaan en nieuwe werknemer".
- Driver edit parity: extend DriverProfilePanel/driver update DTO to multi-categories (DriverCategoryIds) matching create.
- Tests: minimal create (name-only), driver toggle, edit prefill, parity.

**Phase 9 — Notes/history verification + legacy notes** (commit)
- Stop writing legacy Employee.Notes on create/update (keep returning + one-time backfill already done); frontend passthrough removal; verify notes flows with tests (exists largely) + pin badge on dashboard unaffected.
- Customer Historiek tab (CustomerHistoryService from Phase 2) UI.

**Phase 10 — Validation audit sweep** (commit): customer create requires name only (+ unique number, valid email/VAT when supplied); location: name required, address rules only when structured address entered, coordinates optional; normalize ad-hoc BadRequest({message}) in touched controllers to DomainValidationException; inline Dutch everywhere.

**Phase 11 — Permissions/tenancy/audit review** (commit): nested-payload tenant tests (foreign contactId in location link, foreign customerId on location, foreign locationId on stop); role v25 test; audit events for location ops incl. masked access code.

**Phase 12 — Dev demo seeder** (commit): `DevDemoDataSeeder` in Data/, Development-only, idempotent (marker customer number prefix `DEMO-`), 5 customers (contacts 2-4, locations 3-5 mixed types w/ opening hours + operational fields + 1 inactive), 10 employees (mix roles/drivers/notes/inactive); run via `dotnet run --project TransportationService.Api -- --seed-demo` or auto after DevAdminSeeder — choose arg-triggered, document in docs + StartUp.txt.

**Phase 13 — Full regression**: backend suite, frontend test/lint/tsc/build, zero new warnings.

**Phase 14 — Smoke evidence + docs + final report**: API-driven smoke of §25 (Customer A/B, Employee A/B, order check) against running dev stack; docs/customers-master-data.md (+ permissions.md, personnel docs updates); final report.


