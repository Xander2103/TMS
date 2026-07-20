# 2026-07-20 Improvement Wave — Assessment & Phased Plan

Spec: the 33-area improvement prompt (validation/UX, customers/locations, personnel,
fleet, orders/cargo/packages, portal, dossiers, pricing, search, accounts, calendar).
Baseline verified 2026-07-20: backend build clean, 523/523 backend tests, 44/44 vitest,
tsc + eslint clean, tree clean at `5505e41`.

## 1. Repository assessment (what already exists)

Modular monolith `TransportationService.Api/Modules/*` + feature-sliced React SPA.
Already delivered in prior milestones (do NOT rebuild): auth/JWT, permission catalog
(`PermissionCodes.All` + `RequirePermissionAttribute` any-of), audit
(`IAuditableEntity` interceptor + `AuditLog` + `/api/audit-logs`), tenant numbering
(`TenantNumbering.SaveWithClaimedNumberAsync`), customers (VAT mod-97 validator,
block/unblock, contacts CRUD), locations module (optional `CustomerId`, types),
employees (NRN/IBAN/BIC validators in `EmployeePersonValidators`, confidential
redaction, driver profile), fleet (vehicles/trailers with FuelType incl. **Hybrid**,
separate EmissionClass, documents/maintenance/inspections/damage/fuel), orders
(guarded transitions, cancel with reason, stops, cargo lines), planning + conflict
engine, driver workflow (stop execution, POD versions), scanning + packages
(chain-of-custody `PackageEvent`, PDF labels PdfSharp+QRCoder, thermal+A4),
invoicing, dashboard, notifications, messaging outbox, EDI, calendar sync queue,
trip costing, KPI + XLSX export, employee portal (`/api/me`), shift planning.

## 2. Confirmed gaps (from 3-agent survey, 2026-07-20)

- **No per-field validation errors anywhere**: backend emits ProblemDetails
  `{title, detail, status}` (via `DomainValidationException` filter) or ad-hoc
  `{ message }` (result-object controllers); FE `apiClient` surfaces one string.
- FE kit lacks: BackButton, top+bottom FormActions, ValidationSummary, collapsible
  section; two divergent form-layout systems (kit `FormSection` vs bespoke
  `*-form-card` in vehicle/trailer/settings/order forms).
- Settings page: save top-only, no in-app nav guard.
- Customer: no activate/deactivate action (only edit-checkbox), no Locations tab,
  no initial-contact section on create, VAT error not field-level.
- Location: no default loading/unloading flags; no customer-detail integration;
  order form doesn't filter locations by customer or offer inline create.
- Personnel: NRN/IBAN inputs have no normalization/format/FE validation (backend
  validators exist); no qualifications on create.
- Vehicles/trailers: `Year` unvalidated; volume plain field (no auto-calc/override
  marker); vehicle create form lacks dimensions; no dedicated activate endpoints.
- No maintenance/inspection policy model (per-record intervals only).
- Orders: `GoodsDescription` required; no status history entity; no corrective
  rollbacks beyond Confirmed→Draft; no timeline UI; no Back action.
- CargoItem: description/barcode/qty only — no package type, weights, dims, ADR,
  stackable, stop links. Packages decoupled from cargo (manual create/bulk only,
  no `GeneratePackagesForOrder`, no reconciliation after scans).
- Label PDF: confirmed barcode/QR overlap risk (QR top-right vs full-width number
  line + full-width barcode band in `LabelRenderService.DrawLabel`).
- No capacity check in `PlanningConflictService` (data exists on Vehicle + orders).
- No customer portal: `User.CustomerId` exists but unused; `/api/me` is
  employee-only; `CustomerContact` has no user link.
- No token-based password reset / activation / forgot-password; no employee→user
  account creation flow; no function→role mapping.
- No user-to-user inbox (notifications are system-generated; messaging is an
  outbound email/SMS outbox).
- No dossier/incident entities; no tariff engine; no global search; no favorites/
  templates (except message/role templates); bulk = packages only.
- Dashboard alert cards missing: overdue maintenance, failed scans, missing POD,
  open incidents.
- Nationality/Language are tenant lookups (ISO codes in `Code`); global-seed
  pattern to mirror is `CountrySeeder`.
- Planning UI: day board + week grid; no day/week/month calendar, no ICS feed.

## 3. Reusable foundations

`DomainValidationException` + filter (extend with field errors), result-object
pattern (map to ProblemDetails), `PermissionCodes.All` + seeders, `AuditLog`,
`PackageEvent` (template for order timeline), `TenantNumbering` (new series =
2 columns + concurrency token), `SqliteTestDbContext` + factories + `TestClock`,
FE kit (FormSection/FormField/Tabs/SearchableSelect/ConfirmDialog/
UnsavedChangesGuard/StatusBadges), `LocationSelect`, label snapshot versioning.

## 4. Key risks

- Backward compat of error responses: keep `detail`/`message` populated; only ADD
  `errors` dictionary. FE keeps single-message fallback.
- `DefaultRoleSeeder` never re-syncs existing roles → new permissions must go
  through role-template version bump (CurrentVersion 3 → 4) for existing DBs.
- Order edit wholesale-replaces cargo items — cargo redesign must preserve
  CargoItemId stability once packages link to cargo (EF pitfall: AddRange new
  children explicitly).
- Naive DateTimes → normalize to UTC (Npgsql timestamptz).
- `.gitignore` `/packages/` trap: check `git status --untracked-files=all` for new
  folders.
- Migrations must be additive; never modify applied migrations.

## 5. Phasing (spec order, adjusted to repo reality)

- **P1 Validation & form foundation**: backend field-error ProblemDetails
  (`errors: {field: [messages]}` on DomainValidationException + result-object
  mapping helper); attach field names in Vat/Iban/Nrn validators; FE: ApiError
  `fieldErrors`, path normalization (nested + indexed), `ValidationSummary`,
  `BackButton`, FormActions top+bottom placement, collapsible `FormSection`;
  mapping tests both sides.
- **P2 Settings + customers + locations**: settings bottom actions + nav guard;
  customer activate/deactivate endpoints (+confirm/audit/permission), inactive
  customers blocked from NEW orders (mirror blocked-customer rule); optional
  collapsible initial contact on create (same model/validation, one transaction);
  VAT field-level FE mirror; Location default-loading/unloading flags (one default
  per customer per kind, filtered unique index) + customer Locations tab (list/
  add/edit/deactivate/defaults) + order-form customer-scoped location selection
  with defaults + inline create (same use case).
- **P3 Personnel**: FE NRN/IBAN normalization/format mirroring backend rules,
  field-level errors; optional collapsible qualifications on create (existing
  qualification model, single transaction); safe dev test values documented.
- **P4 Fleet**: construction-year ≤ current year (dynamic, both sides, year-proof
  tests); dedicated activate/deactivate endpoints + UI confirm + audit; volume
  auto-calc from L×W×H with manual-override flag (`VolumeIsManual`) shared FE
  logic vehicles/trailers; vehicle form dimensions; collapsible technical/docs on
  create; **maintenance policy model**: `MaintenancePolicy` (tenant default +
  vehicle-type/trailer-type + per-asset override; time/mileage/warning-days),
  precedence asset > type > company documented + tested; idempotent initial due
  planning + dashboard/fleet warnings.
- **P5 Orders**: `GoodsDescription` optional (validation+DTO+migration to drop
  NOT NULL); Back actions; `TransportOrderStatusHistory` (old/new/user/timestamp/
  reason) + corrective transitions with `orders.correct_status` permission +
  reason + safety guards (no rollback past invoiced/POD without correction flow);
  timeline read model `/api/transport-orders/{id}/timeline` projecting AuditLog +
  status history + package events + POD/invoice/assignment events; timeline UI;
  **cargo redesign**: PackageType reference data (seeded, tenant-extensible),
  CargoItem gains packageTypeId, quantities, per-unit/total weight, L/W/H,
  volume (auto+override), ADR flag/details, stackable, references, remarks,
  loading/unloading stop links (validated same order + ordering), migration
  preserving existing lines.
- **P6 Packages**: idempotent `GeneratePackagesForOrder` on confirm (per cargo
  item × quantity, links package→cargo item, sequence "n of m", no regen dupes);
  reconciliation workflow when quantities change after scans (never delete
  scanned); label redesign (reserved QR column, no overlap, hierarchy per spec,
  A4 + thermal from one snapshot model, mapping tests).
- **P7 Capacity check**: PlanningConflictService capacity codes (weight/volume/
  pallets; skip unconfigured measures, mark incomplete data), tenant policy
  warn/block, totals in conflict payload, tests (fits/exceeds/unknown/incomplete/
  override/multi-item).
- **P8 Customer portal**: portal surface scoped by `User.CustomerId` (derive from
  auth, never from client), customer order intake reusing TransportOrderService
  use cases (Submitted/AwaitingReview initial status added to transition map),
  location selection restricted to own customer + inline create, documents,
  planner review queue.
- **P9 Accounts**: optional "create user account" from employee/contact (explicit
  opt-in), activation tokens (single-use, hashed, expiring), forgot-password +
  admin reset, first-login forced password change, function→role mapping config
  (suggested roles, admin confirms, manual vs automatic tracked, audited).
- **P10 Calendar + inbox**: driver/planning day/week/month calendar views, ICS
  feed (token-secured) via integration abstraction; internal inbox (user-to-user/
  role/group messages, unread badge, permission `messages.send`), reusing
  notification infra where sensible.
- **P11 Dossiers**: minimum viable `TransportDossier` (DOS- series, links to
  orders/trips/invoices/documents/notes, financial summary), `Incident` module
  (typed, severity, impacts, links, statuses New/InProgress/Resolved/Cancelled),
  `DossierRelation` entity (typed, no self/dupes), dashboard cards (open
  incidents, missing POD, failed scans, overdue maintenance).
- **P12 Platform**: tariff engine architecture + first vertical slice (customer
  rate card: base + per-km/pallet/weight + surcharges with effective dates and
  explanation lines), reporting contract, document-generation layer alignment,
  Ctrl+K global search endpoint + palette, favorites/recents/templates, bulk
  actions on orders, nationalities/languages global seed + searchable selectors,
  customer wizard, partner-type proposal doc.

Each phase: own commit(s); backend build + tests, FE tsc/lint/vitest/build green
before commit; migrations inspected; permission additions wired through catalog +
role templates.
