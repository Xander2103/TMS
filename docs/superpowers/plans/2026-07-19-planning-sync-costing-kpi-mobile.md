# Personnel-Planning Sync · Trip Costing · KPI Dashboard · Mobile Readiness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline execution, this session — author = executor). Steps use checkbox (`- [ ]`) syntax for tracking. Each wave ends in a verified commit.

**Goal:** Trips automatically project into personnel planning; planning is clickable with conflict awareness; every trip gets an estimated/actual/final cost model with profitability; management gets a permission-protected KPI dashboard with XLSX reports; the portal becomes a mobile-app-ready role-based shell.

**Architecture:** Extend the modular monolith (.NET 10 + EF Core/Npgsql) and the hand-rolled React kit. New backend modules: `Modules/TripCosting`; extensions to `Modules/EmployeePlanning`, `Modules/Planning`, `Modules/Reporting`. One new backend package (ClosedXML for real XLSX). Zero new frontend dependencies. All schema changes are EF migrations; all writes audited via `IAuditService`; tenant isolation via explicit `TenantId` predicates (house pattern).

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + SqliteTestDbContext + TestClock(TimeProvider), React 19 + Vite 8 + TS 6, react-router 7 data router, plain CSS tokens, vitest.

## Global Constraints

- UI copy is **Dutch**. Permission codes in `area.action` snake_case.
- Backend enforcement authoritative; FE `hasPermission` is UX only.
- **The trip is the source of truth for trip-generated planning entries** — the entry has NO public write API; only `TripPlanningSyncService` mutates it, in the same `SaveChanges` as the trip mutation (atomic moves).
- Tenant isolation: every query `Where(x => x.TenantId == _tenantContext.TenantId)` (no global tenant filters in this codebase). Soft-delete via interceptor; never set audit stamps by hand.
- Audit: mutate → `SaveChangesAsync` → `_auditService.RecordAsync(...)` (audit saves itself). Anonymous payload objects only.
- Numbering claims via `TenantNumbering.SaveWithClaimedNumberAsync` (not needed for new entities here — none are numbered).
- New enums stored as strings (`HasConversion<string>()`), money `decimal` precision `(12,2)`, rates as specified per column.
- Migrations: `dotnet ef migrations add <Name> --project TransportationService.Api` from repo root; **not auto-applied** — `dotnet ef database update` against docker Postgres. Names: `PersonnelPlanningSync`, `PlanningConflictSettings`, `TripCostingFoundation`.
- Tests: xUnit, plain Assert, `SqliteTestDbContext` + `DevTenantContext` + `DevCurrentUserContext` + `TestClock`; FE vitest.
- Verification per wave: `dotnet build`, focused then full `dotnet test`, `npm run lint`, `npx tsc -b`, `npm run test`, `npm run build`. Kill stray API processes before builds (Get-CimInstance CommandLine match 'TransportationService.Api').
- Commit per verified wave, `feat(<area>): ...` + Co-Authored-By trailer. **Never push.**
- Do not touch `StartUp.txt`.

## Key design decisions (locked)

1. **Trip-generated planning entries are a dedicated entity `TripPlanningEntry`** (module EmployeePlanning), NOT overloaded `Shift` rows — manual shifts and trip entries stay structurally distinguishable; no Shift write path can touch them. Unique filtered index `(TenantId, TripId)` forbids duplicates. Row carries: TripId, EmployeeId, DriverId, SourceType (const "Trip"), TripNumber, Date, PlannedStart/End, ActualStart/End, VehicleSummary, RouteSummary, Status (mirrors TripStatus incl. Draft), Notes. Driver change updates EmployeeId on the same row inside the trip's SaveChanges → atomic move, history preserved via audit log.
2. **Sync hooks live inside TripService/TripExecutionService** (same DbContext/save): Create, Update (Draft-only edits incl. driver/date/time changes), ChangeStatus (Planned/InProgress/Completed/Cancelled → status + actuals; Cancelled preserves the row as Cancelled), Delete (soft-deletes the entry). No trip-restore endpoint exists → nothing to restore (documented). Driver removed while Draft → entry soft-deleted (audited). Actual times = min(ArrivedAt)/max(CompletedAt|DepartedAt) over the trip's StopExecutions, refreshed on stop terminal transitions and trip completion.
3. **Schedule grid gains a third source**: `ShiftService.GetScheduleAsync`/`GetEmployeeScheduleAsync` merge TripPlanningEntries. `ScheduleEntryState` gains `Trip` + `TripCancelled` (12 states). `ScheduleEntryDto` gains `TripId`, `SourceType` ("Shift"|"Absence"|"Trip"), `VehicleSummary`, `StatusLabel` (wave 1) and `ConflictSeverity?` + `ConflictNotes` (wave 2). Row `PlannedMinutes` stays shifts-only (no double counting; documented).
4. **Conflict severity is a shared 3-level enum** `ConflictSeverity { Information, Warning, Blocking }`. Trip-side `PlanningConflictService` keeps its resource rules, gains `DriverShiftOverlap` + `DriverTraining` codes, and `PlanningConflictDto` gains `Severity` (Blocking bool kept in sync for FE compat). Approved absence rules: Training type → severity from settings (default Warning); every other approved absence (incl. Sick) → Blocking. Employee-side conflicts (grid, shift-save gate) computed in EmployeePlanning from the same pairwise rules helper `ScheduleConflictRules` — one rule table, two consumers.
5. **Configurable severities** as two TenantSettings columns: `TrainingConflictSeverity`, `ShiftOverlapConflictSeverity` (strings "Information"|"Warning"|"Blocking", defaults "Warning"), editable in company settings UI. Shift create/update with a Blocking conflict → 409 + conflicts unless `override=true` AND caller holds new `employee_planning.conflict_override` (server re-check → 403). Warnings never block, always reported.
6. **Costing = new module `Modules/TripCosting`.** Entities:
   - `CostRateSet` (effective-dated tenant rate card; unique `(TenantId, EffectiveFrom)` filtered): FuelPricePerLitre(8,3), DefaultConsumptionLPer100Km(6,1), VehicleCostPerKm(8,2), VehicleCostPerHour(8,2), DriverCostPerHour(8,2), EmployerCostMultiplier(5,2)=1.35, MaintenanceCostPerKm(8,3), DepreciationPerDay(10,2), TrailerCostPerDay(10,2), EquipmentCostPerDay(10,2), DefaultTollPerTrip(10,2), OvertimeThresholdMinutesPerDay(int)=480, OvertimeRateMultiplier(5,2)=1.5, WaitingTimeCostPerHour(8,2), Co2KgPerLitreDiesel(6,3)=2.68, Co2KgPerLitreOther(6,3)=2.31. Resolution: newest `EffectiveFrom <= TripDate`; none → hard defaults (all zero rates except multipliers/CO2 → produces no cost lines, never crashes).
   - `TripCostLine`: TripId, Phase (`Estimated`|`Actual`), CostType enum (Fuel, Toll, DriverLabour, Overtime, WaitingTime, VehicleDistance, VehicleTime, Maintenance, Depreciation, Trailer, Equipment, Subcontractor, FerryTunnelParking, Manual, Correction), Description, Quantity(12,3), Unit (km|h|l|dag|forfait|stuk), UnitRate(12,4), Amount(12,2), Source ("Berekend"|"Handmatig"|"Tankbeurten"|"Correctie"), IsManualOverride, OverrideReason, CalculatedAt. Lines are snapshots — they store resolved quantities and rates; later rate-card changes never touch existing lines.
   - `TripCostSummary` (1:1 trip, unique filtered `(TenantId, TripId)`): EstimatedTotal, ActualTotal, ProjectedTotal, Revenue, FinalCost?, FinalRevenue?, IsFinalized, FinalizedAt?, FinalizedByUserId?. Recomputed by the service on every costing change; the KPI read model.
   - Trip gains `PlannedDistanceKm`, `PlannedEmptyKm`, `ActualDistanceKm`, `ActualEmptyKm` (all decimal?(8,1); planned editable Draft, actuals editable via costing endpoint `trip_costs.manage` until finalized). Vehicle gains `ConsumptionLPer100Km` (decimal?(6,1)).
7. **Cost phases:** Estimated lines recalculable only while Draft/Planned. Actual lines recomputed from execution data (auto on trip completion + on demand), manual Actual lines (toll/ferry/parking/subcontractor/manual/correction) user-entered. **Projected total = per-CostType merge: actual if any actual line of that type exists, else estimated.** Finalize (explicit, `trip_costs.manage`, trip Completed/Cancelled): recompute actuals, freeze FinalCost + FinalRevenue on the summary, further mutation refused; reopen only via `trip_costs.override` (audited). Line override (`trip_costs.override`): replaces Amount with mandatory reason, flagged, audited.
8. **Formulas (estimate | actual):** distance D = PlannedDistanceKm | (ActualDistanceKm ?? Planned); duration H = (PlannedEnd−PlannedStart) | (ActualEnd−ActualStart ?? planned); consumption C l/100km = vehicle.ConsumptionLPer100Km ?? rate.Default | measured vehicle average (FuelService.ComputeDerived) ?? vehicle ?? default. Fuel: litres = D×C/100, amount = litres × FuelPricePerLitre — actual phase prefers Σ FuelTransaction.TotalAmount on (VehicleId, TransactionDate == TripDate) as source "Tankbeurten" when > 0. DriverLabour: H × DriverCostPerHour × EmployerCostMultiplier. Overtime (actual only): max(0, H − threshold) × DriverCostPerHour × (OvertimeRateMultiplier − 1) × EmployerCostMultiplier. Waiting (actual only): Σ per completed stop max(0, (CompletedAt−ArrivedAt) − handlingMinutes(settings per stop type)) → hours × WaitingTimeCostPerHour. VehicleDistance: D × VehicleCostPerKm. VehicleTime: H × VehicleCostPerHour. Maintenance: D × MaintenanceCostPerKm. Depreciation: 1 dag × DepreciationPerDay. Trailer (TrailerId set): 1 dag × TrailerCostPerDay. Equipment (vehicle HasCrane || HasRefrigeration): 1 dag × EquipmentCostPerDay. Toll estimate: DefaultTollPerTrip. Zero-rate ⇒ no line. Missing input (no distance / no times) ⇒ no line for that component. All amounts Round(2).
9. **Profitability:** Revenue = Σ `AgreedPrice ?? 0` of the trip's non-cancelled orders (snapshot into FinalRevenue at finalize). GrossProfit = Revenue − (Final ?? Projected ?? Estimated). Margin% = profit/revenue×100 (0 when revenue 0). Per-km/per-hour use actual-else-planned denominators (null when 0/unknown). **Multi-order allocation (documented + tested): each order contributes its own AgreedPrice; per-customer cost allocation = tripCost × orderRevenue/tripRevenue; if tripRevenue = 0 → equal split across orders.** Exposed only with `profitability.view`; cost DTOs only with `trip_costs.view` — the plain trip DTO never carries money.
10. **Permissions (new codes):** `employee_planning.conflict_override`, `trip_costs.view`, `trip_costs.manage`, `trip_costs.override`, `profitability.view`, `kpi.view`, `kpi.export`. Role templates: Planner += conflict_override, trip_costs.view; Management += all seven; Boekhouding += trip_costs.view, profitability.view, kpi.view, kpi.export; **new role `HR`** (employees view/create/edit, employee_planning view/manage + conflict_override, absences.* incl. approve, employee_documents.view, qualification_types.view, departments.view, job_functions.view, dashboard.view). Chauffeur/Klantportaal unchanged (no cost access). New-role templates reach existing DBs (seeder creates missing roles); permission additions to existing roles only land on fresh DBs — final role-matrix verification therefore runs against a rebuilt dev DB.
11. **KPI module** = `Modules/Reporting` extension: `KpiQueryService` (all aggregates server-side, bounded queries, zero-safe percentages) + `KpiController`: `GET /api/kpi/dashboard` (kpi.view) and `GET /api/kpi/trip-profitability` (profitability.view) with filters from/to (required, ≤ 366 days), customerId, driverId, vehicleId. Definitions in `docs/kpi-definitions.md`, each with a unit test. Deep links: financial KPIs → `/kpi/trips` (new trip-profitability report page); operational KPIs → existing filtered pages.
12. **XLSX via ClosedXML** (only new package). `KpiExportService`: `GET /api/kpi/export?report=<key>&filters` (kpi.export), 9 report keys, every workbook = data sheet + "Criteria" sheet (filters, generated-at, tenant, user), dates `dd-MM-yyyy` format, numeric cells numeric `#,##0.00`, strings written as `XLCellValue` text (never formulas) + explicit guard test for `=+-@` prefixes.
13. **Mobile readiness (no second backend):** `manifest.webmanifest` + generated 192/512 PNG icons + minimal app-shell service worker (network-only for `/api`, network-first navigations with cached-shell fallback, cache-first hashed assets; registered PROD-only), online/offline indicator (`useOnlineStatus` + banner, aria-live), `/portal` becomes a permission-filtered module launcher (driver/warehouse/employee tiles), touch-target audit (≥44px), shared `PhotoCaptureInput` (`capture="environment"`) adopted in exceptions. **No offline sync claimed anywhere.** Same API contracts.
14. **Non-goals / interpretations:** no notifications tab on employee detail (notifications are user-scoped, would leak another user's inbox — deep-link section satisfied by the bell page); no department/transport-type/order-type KPI filters (fields don't exist — documented); no chart library (stat tiles + deep links satisfy the spec); vehicle "available hours" = active vehicles × Mon–Fri workdays × 8h (documented, tested).

---

### Wave 1 — Trip → personnel planning sync

**Files (backend):**
- Create: `Modules/EmployeePlanning/Entities/TripPlanningEntry.cs`
- Create: `Modules/EmployeePlanning/Configurations/TripPlanningEntryConfiguration.cs` (table `trip_planning_entries`; unique `(TenantId, TripId)` filtered `"IsDeleted" = false`; index `(TenantId, EmployeeId, Date)`; enum/status string; soft-delete filter)
- Create: `Modules/EmployeePlanning/Services/ITripPlanningSyncService.cs` + `TripPlanningSyncService.cs`
- Modify: `Data/TransportationDbContext.cs` (+DbSet `TripPlanningEntries`)
- Modify: `Modules/Planning/Services/TripService.cs` (hooks in Create/Update/ChangeStatus/Delete + audit after save)
- Modify: `Modules/Planning/Services/TripExecutionService.cs` (actuals refresh on terminal stop transitions)
- Modify: `Modules/EmployeePlanning/Services/ShiftService.cs` (`GetScheduleAsync` + `GetEmployeeScheduleAsync` merge trips)
- Modify: `Modules/EmployeePlanning/Dtos/ShiftDtos.cs` (`ScheduleEntryState` + `Trip`/`TripCancelled`; `ScheduleEntryDto` + TripId/SourceType/VehicleSummary/StatusLabel)
- Modify: `Program.cs` (DI)
- Migration: `PersonnelPlanningSync`
- Test: `Tests/EmployeePlanning/TripPlanningSyncTests.cs` (+ adjust `ShiftServiceTests` for DTO shape)

**Entity (complete):**
```csharp
public class TripPlanningEntry : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DriverId { get; set; }
    public string SourceType { get; set; } = "Trip";
    public string TripNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public string? VehicleSummary { get; set; }   // "V-0001 · 1-ABC-123"
    public string? RouteSummary { get; set; }     // "Antwerpen → Rotterdam"
    public TripStatus Status { get; set; } = TripStatus.Draft;
    public string? Notes { get; set; }
}
```

**Sync service interface (produces):**
```csharp
public enum TripPlanningSyncAction { None, Created, Updated, Moved, Cancelled, Removed }
public sealed record TripPlanningSyncResult(TripPlanningSyncAction Action, Guid? EntryId,
    Guid? PreviousEmployeeId, Guid? EmployeeId);
public interface ITripPlanningSyncService
{
    // Stages entry mutations on the shared DbContext. NEVER saves. Caller saves + audits.
    Task<TripPlanningSyncResult> ApplyAsync(Trip trip, CancellationToken ct);
    // Refreshes ActualStart/End from StopExecutions (no save).
    Task<TripPlanningSyncResult> ApplyActualsAsync(Trip trip, CancellationToken ct);
}
```
Rules in `ApplyAsync`: no DriverId → soft-delete existing entry (Removed). Trip soft-deleted → soft-delete entry. Otherwise resolve `EmployeeId` via Drivers (tenant-scoped), load entry by TripId (ignore query filters? no — soft-deleted entry + re-add driver ⇒ un-delete the same row via `IgnoreQueryFilters()` to respect the unique index), upsert all projected fields (RouteSummary = first loading city → last unloading city over ordered orders/stops; VehicleSummary from Vehicle), Moved when EmployeeId changed, Cancelled action when Status becomes Cancelled. `TripService` calls: Create (inside the numbering callback so TripNumber lands on the entry too), Update, ChangeStatus (+`ApplyActualsAsync` on Completed), Delete; each followed by existing `SaveChangesAsync`/`SaveWithClaimedNumberAsync` then `_auditService.RecordAsync("TripPlanningEntry", entryId, action, ...)` when Action != None. `TripExecutionService.TransitionCoreAsync`: after stop save when stop is terminal → `ApplyActualsAsync` + save (piggyback existing save).

**Grid merge:** load `TripPlanningEntries` for employees+range → per day emit entry: State = Status == Cancelled ? TripCancelled : Trip; Label = TripNumber; StartTime/EndTime = (Actual ?? Planned) mapped to TimeOnly (null-safe); WorkLocation = RouteSummary; VehicleSummary; StatusLabel = Dutch status label ("Concept", "Gepland", "Bezig", "Afgerond", "Geannuleerd"). Draft trips included.

**Files (frontend):**
- Modify: `src/features/employee-planning/types.ts` (12 states + new entry fields + labels/icons: Trip → "Rit"/🚚, TripCancelled → "Rit geannuleerd"/🚫)
- Modify: `src/features/employee-planning/components/ScheduleChip.tsx` + `employee-planning.css` (`.schedule-chip-trip` bg #bfdbfe border #1d4ed8; `.schedule-chip-tripcancelled` muted #e5e7eb/#6b7280 + line-through label; richer `title`)
- Modify: `src/features/employee-planning/pages/EmployeePlanningPage.tsx` (trip chips navigate to `/planning/{tripId}` when `hasPermission('planning.view')`; never open the shift modal)
- Modify: `src/features/portal/pages/PortalPlanningPage.tsx` (renders trip chips; no nav — wave 3 adds portal links)
- Modify: `src/features/employee-planning/__tests__/scheduleMeta.test.ts` (12 states)

**Steps:**
- [ ] Entity + config + DbSet + migration `PersonnelPlanningSync`; `dotnet ef database update`.
- [ ] Sync service + TripService/TripExecutionService hooks + DI.
- [ ] Schedule merge + DTO extensions; fix compile fallout in tests.
- [ ] Tests: create-with-driver creates entry (fields projected, audit row); create-without-driver creates none; assign-driver-later (Draft update) creates; driver change moves same row (same Id, audit Moved); date/time change updates; cancel → status Cancelled, row kept; delete → entry soft-deleted; complete → actuals from StopExecutions; duplicate forbidden (second ApplyAsync updates, row count 1; unique index verified); re-add driver after removal un-deletes same row; tenant isolation (other-tenant driver invisible); schedule grid shows Trip entry with sourceType/vehicle; portal self-schedule includes own trips.
- [ ] Full backend suite + FE lint/tsc/test/build.
- [ ] Commit `feat(planning): automatic trip-to-personnel-planning synchronisation with linked entries`.

### Wave 2 — Conflict engine with severities + shift-save gate

**Files (backend):**
- Create: `Common/Scheduling/ConflictSeverity.cs` (`Information, Warning, Blocking`)
- Create: `Modules/EmployeePlanning/Services/ScheduleConflictRules.cs` — static pairwise rule table + `Parse(string?)` for settings
- Modify: `Modules/Tenancy/Entities/TenantSettings.cs` + config (+`TrainingConflictSeverity`, `ShiftOverlapConflictSeverity`, string(16), defaults "Warning") + `CompanySettingsService`/DTOs + SettingsPage UI (two selects, "Planning" section)
- Modify: `Modules/Planning/Dtos/TripDtos.cs` (`PlanningConflictDto` + `ConflictSeverity Severity`; codes + `DriverShiftOverlap`, `DriverTraining`)
- Modify: `Modules/Planning/Services/PlanningConflictService.cs` — approved Training absence → configured severity; approved other absences stay Blocking; new shift-overlap rule (work/standby shift same employee overlapping trip window or same date when times missing → configured severity); every existing rule maps Blocking→Severity.Blocking / warning→Warning
- Modify: `Modules/EmployeePlanning/Services/ShiftService.cs` — `GetScheduleAsync` computes per-entry conflicts (pairwise over the day's trips/shifts/absences; both sides flagged, max severity + Dutch notes); `CreateAsync`/`UpdateAsync` gain conflict evaluation: Blocking → outcome `Conflict` + conflicts list unless `Override` allowed (new request fields `Override`, service param `bool canOverride` from controller permission check → not allowed = 403 pattern like trips); Warnings returned in the OK result
- Modify: `Modules/EmployeePlanning/Dtos/ShiftDtos.cs` (`ScheduleEntryDto` + `ConflictSeverity? ConflictSeverity`, `IReadOnlyList<string> ConflictNotes`; `ShiftOperationResult` + Conflicts; requests + Override)
- Modify: `Modules/EmployeePlanning/Controllers/EmployeePlanningController.cs` (409 + conflict payload; override permission re-check `employee_planning.conflict_override`)
- Modify: `Modules/Identity/PermissionCodes.cs` (+`employee_planning.conflict_override`), `Data/DefaultRoleDefinitions.cs` (Planner/Management/HR get it — HR role added wave 6; here Planner+Management)
- Migration: `PlanningConflictSettings`
- Test: `Tests/EmployeePlanning/ScheduleConflictTests.cs`, extend `Tests/Planning/PlanningConflictServiceTests.cs`

**Files (frontend):**
- Modify: `ScheduleChip.tsx`/`employee-planning.css` — conflict marker: `⚠` badge overlay (yellow #facc15 border) + conflict notes in tooltip; legend entry "Conflict"
- Modify: `EmployeePlanningPage.tsx` shift modal — on 409 show conflict list + "Toch plannen (override)" checkbox only with `employee_planning.conflict_override`; warnings shown inline non-blocking
- Modify: `src/features/planning/pages/TripDetailPage.tsx` — conflict badges render severity (Blocking danger / Warning warning / Information info)
- Modify: settings page for the two severity selects

**Steps:**
- [ ] Severity enum + rules helper + settings columns/migration/UI + db update.
- [ ] Trip engine updates; schedule conflicts; shift gate + controller + permission code.
- [ ] Tests: approved leave vs trip = Blocking both sides; approved sick = Blocking; training absence default Warning, Blocking when configured; overlapping trips same driver = Blocking; manual shift vs trip = Warning default/configurable; shift create over blocking conflict → Conflict outcome, override + permission succeeds, override without permission → denied; grid entries carry severity+notes; trip Draft→Planned blocked by blocking shift-overlap only when configured Blocking; severity strings parse safely (bad value → Warning).
- [ ] Full suites + FE checks.
- [ ] Commit `feat(planning): three-level schedule conflict engine with configurable severities and override flow`.

### Wave 3 — Clickable planning, deep links, employee-detail tabs

**Files (frontend):**
- Modify: `EmployeePlanningPage.tsx` — employee name cell → `<Link to={'/employees/'+row.employeeId}>` when `hasPermission('employees.view')`; absence chips → navigate `/employees/{employeeId}?tab=afwezigheden&absenceId={id}` (employees.view) — else no-op; shift chips for view-only users open a read-only shift details modal (reuse modal, disabled inputs); trip chips → `/planning/{tripId}` (planning.view)
- Modify: `ScheduleChip.tsx` — chips become `<button>`/`<a>` with full aria-label: "{label}, {type}, {start}–{eind}, status {status}, {vehicle/route}, {conflict}" (focusable, Enter/Space)
- Modify: `src/features/employees/pages/EmployeeDetailPage.tsx` — tabs become: `profiel`, `planning` (NEW), `kwalificaties`, `afwezigheden`, `ritten` (NEW, only when DriverId set + planning.view), `historiek`; `?absenceId=` support in afwezigheden tab (scroll+highlight row)
- Create: `src/features/employees/components/EmployeePlanningTab.tsx` — 4-week single-employee schedule (reuse `getSchedule(from,to,undefined,employeeId)` list view + ScheduleChip + legend + conflicts), gated `employee_planning.view` (else hidden)
- Create: `src/features/employees/components/EmployeeTripsTab.tsx` — trip history table via `listTrips({from: today−90d, to: today+30d, driverId})` → link `/planning/:id`; needs driver id from employee detail DTO
- Modify: `src/features/absences/components/AbsencesTab.tsx` (highlight + scroll on absenceId)
- Modify: `src/features/employees/types.ts`/api if DriverId missing on detail DTO (backend: EmployeeDetailDto already has DriverId)
- Modify: `PortalPlanningPage.tsx` — own trip chips link to `/my-trips/{tripId}` when `driver_workflow.view`; absence chips → `/portal/absences`
- Test: `src/features/employee-planning/__tests__/scheduleNavigation.test.tsx` (chip renders link per permission; no link without), extend scheduleMeta test

**Backend:** none beyond wave 1/2 DTOs (verify `TripExecutionController` GET execution allows dispatcher — yes; `/planning/:id` enforces planning.view → unauthorized deep link = 403 ProblemDetails; document in tests: existing behaviour).

**Steps:**
- [ ] Grid links + read-only shift modal + chip a11y upgrade.
- [ ] Employee detail: planning tab, ritten tab, absence highlight, tab wiring.
- [ ] Portal links.
- [ ] FE tests + full checks (lint/tsc/test/build) + backend suite untouched-green.
- [ ] Commit `feat(planning): clickable personnel planning with employee deep links and detail tabs`.

### Wave 4 — Trip costing foundation (backend)

**Files:**
- Create: `Modules/TripCosting/Entities/CostRateSet.cs`, `TripCostLine.cs`, `TripCostSummary.cs` (+enums `TripCostPhase`, `TripCostType` in entity files)
- Create: `Modules/TripCosting/Configurations/` (3 configs; tables `cost_rate_sets`, `trip_cost_lines` (index `(TenantId, TripId, Phase)`), `trip_cost_summaries` (unique filtered `(TenantId, TripId)`))
- Create: `Modules/TripCosting/Services/ICostRateService.cs` + `CostRateService.cs` (CRUD + `GetForDateAsync(DateOnly)` resolution + validation: rates ≥ 0, unique EffectiveFrom)
- Create: `Modules/TripCosting/Services/ITripCostingService.cs` + `TripCostingService.cs`
- Create: `Modules/TripCosting/Dtos/TripCostingDtos.cs`
- Create: `Modules/TripCosting/Controllers/CostRatesController.cs` (`GET/POST/PUT/DELETE api/cost-rates` — view: trip_costs.view, mutate: trip_costs.manage)
- Create: `Modules/TripCosting/Controllers/TripCostingController.cs`:
  - `GET api/trips/{id}/costing` (trip_costs.view) → lines+totals (+profitability only when caller has profitability.view — controller passes flag)
  - `POST api/trips/{id}/costing/recalculate-estimate` (trip_costs.manage; Draft/Planned only)
  - `POST api/trips/{id}/costing/recalculate-actual` (trip_costs.manage; InProgress/Completed)
  - `POST api/trips/{id}/costing/lines` (manual line; trip_costs.manage)
  - `PUT api/trips/{id}/costing/lines/{lineId}/override` (amount+reason; trip_costs.override)
  - `DELETE api/trips/{id}/costing/lines/{lineId}` (manual lines only; trip_costs.manage)
  - `PUT api/trips/{id}/costing/actuals` (ActualDistanceKm/ActualEmptyKm; trip_costs.manage)
  - `POST api/trips/{id}/costing/finalize` (trip_costs.manage) / `POST .../reopen` (trip_costs.override)
- Modify: `Modules/Planning/Entities/Trip.cs` + `TripConfiguration` (4 distance columns), `Modules/Fleet/Entities/Vehicle.cs` + config (+ConsumptionLPer100Km) + Vehicle DTOs/service mapping + vehicle form field (FE next wave), Trip DTOs/requests (planned distances on create/update) + `TripService` mapping
- Modify: `Modules/Planning/Services/TripService.cs` — estimate auto-recalc after Create/Update and on Draft→Planned; `TripExecutionService`/`TripService.ChangeStatusAsync` → actual recalc on Completed (via `ITripCostingService.StageRecalculationAsync(trip, phase)` staged + saved in the same flow; keep summary fresh)
- Modify: `Modules/Identity/PermissionCodes.cs` (+trip_costs.view/manage/override, profitability.view), `DefaultRoleDefinitions` (per decision 10 minus HR)
- Modify: `Program.cs` (DI), `Data/TransportationDbContext.cs` (3 DbSets)
- Migration: `TripCostingFoundation`
- Test: `Tests/TripCosting/CostRateServiceTests.cs`, `Tests/TripCosting/TripCostingServiceTests.cs`

**Service interface (produces):**
```csharp
public interface ITripCostingService
{
    Task<TripCostingDto?> GetAsync(Guid tripId, bool includeProfitability, CancellationToken ct);
    Task<CostingOperationResult> RecalculateAsync(Guid tripId, TripCostPhase phase, CancellationToken ct);
    Task<CostingOperationResult> AddManualLineAsync(Guid tripId, AddCostLineRequest request, CancellationToken ct);
    Task<CostingOperationResult> OverrideLineAsync(Guid tripId, Guid lineId, OverrideCostLineRequest request, CancellationToken ct);
    Task<CostingOperationResult> DeleteLineAsync(Guid tripId, Guid lineId, CancellationToken ct);
    Task<CostingOperationResult> UpdateActualsAsync(Guid tripId, UpdateTripActualsRequest request, CancellationToken ct);
    Task<CostingOperationResult> FinalizeAsync(Guid tripId, CancellationToken ct);
    Task<CostingOperationResult> ReopenAsync(Guid tripId, CancellationToken ct);
    // staged (no save) — called by TripService inside its own save cycle:
    Task StageRecalculationAsync(Trip trip, TripCostPhase phase, CancellationToken ct);
}
```
`TripCostingDto`: TripId, TripNumber, TripStatus, IsFinalized, FinalizedAt, EstimatedTotal, ActualTotal, ProjectedTotal, FinalCost, Lines (per line: id, phase, costType, description, quantity, unit, unitRate, amount, source, isManualOverride, overrideReason, calculatedAt), Actuals (distances), Profitability? (Revenue, GrossProfit, MarginPct, RevenuePerKm, CostPerKm, RevenuePerHour, CostPerHour, PerOrder allocations list). Recalc replaces only non-manual, non-overridden calculated lines of that phase; overridden calculated lines survive recalc untouched.

**Steps:**
- [ ] Entities/configs/DbSets/migration + db update.
- [ ] CostRateService + controller + validation tests.
- [ ] Costing engine (estimate + actual builders per decision 8) + manual/override/finalize/reopen + summary recompute + trip hooks + permissions + DI.
- [ ] Tests: estimated fuel (consumption chain vehicle→default), labour (multiplier), vehicle km/h, maintenance, depreciation, trailer, equipment, toll default; no-input ⇒ no line; zero rates ⇒ empty estimate; actual fuel prefers Tankbeurten sum; overtime + waiting from stop executions (TestClock timeline); projected per-type merge; manual line + delete; override stores reason + survives recalc; finalize freezes (rate change + recalc refused post-final; reopen restores); rate resolution by EffectiveFrom incl. historical preservation (change rate after calc → lines unchanged); estimate recalc refused after InProgress; tenant isolation; driver-permission matrix covered wave 10 live.
- [ ] Full backend suite; FE untouched-green.
- [ ] Commit `feat(costing): trip cost model with effective-dated rates, estimated/actual phases and final snapshots`.

### Wave 5 — Profitability + costing/config frontend

**Files (frontend):**
- Create: `src/features/trip-costing/types.ts` + `api/tripCostingApi.ts` + `components/TripCostingPanel.tsx` (+css)
- Modify: `TripDetailPage.tsx` — "Kosten & rendement" section (trip_costs.view): phase tabs Geschat/Werkelijk, lines table (€ via `euro()`), totals bar (Geschat / Werkelijk / Geprojecteerd / Definitief), buttons: Herbereken schatting (Draft/Planned + manage), Herbereken werkelijk (manage), + Kostenregel (manage; modal type/description/qty/unit/rate), override dialog (override perm; amount + mandatory reason), actuals editor (werkelijke km / lege km), Afronden/Heropenen; profitability card (profitability.view): Omzet / Kosten / Winst / Marge% / €-per-km / €-per-uur + per-order allocation table
- Modify: `src/features/planning/pages/TripDetailPage.tsx` trip form fields (Draft): geplande afstand km / lege km; `src/features/planning/types.ts`
- Create: `src/features/trip-costing/pages/CostRatesPage.tsx` — route `/cost-rates`, sidebar "Kostentarieven" (Instellingen group, `trip_costs.manage`): effective-dated rate cards list + create/edit form (all rates, helper text, EffectiveFrom date, EU number inputs)
- Modify: `src/features/vehicles` form/detail (+`Verbruik (l/100km)`), `AppRoutes.tsx`, `Sidebar.tsx`
- Test: vitest `tripCostingMeta.test.ts` (cost type labels complete; margin formatter zero-safe)

**Steps:**
- [ ] API layer + panel + trip form + vehicle consumption + rates page + routes/sidebar/permission gating.
- [ ] FE tests + lint/tsc/test/build; backend suite green.
- [ ] Commit `feat(costing): trip cost & profitability UI with effective-dated rate configuration`.

### Wave 6 — KPI backend + HR role

**Files:**
- Create: `Modules/Reporting/Services/IKpiQueryService.cs` + `KpiQueryService.cs`, `Modules/Reporting/Dtos/KpiDtos.cs`, `Modules/Reporting/Controllers/KpiController.cs`
- Create: `docs/kpi-definitions.md` (every formula from decision 11 + spec §10, incl. zero-denominator behaviour and revenue/cost source per KPI)
- Modify: `PermissionCodes.cs` (+kpi.view, kpi.export), `DefaultRoleDefinitions.cs` (Management/Boekhouding updates + **HR role template**)
- Test: `Tests/Reporting/KpiQueryServiceTests.cs`, `Tests/Data/DefaultRoleSeederTests.cs` (HR created)

**DTO (produces):** `KpiDashboardDto` fields: `RevenueToday, RevenuePeriod, ProfitToday, ProfitPeriod, AverageMarginPct, ProfitPerTrip, TripCount, VehicleUtilisationPct, TotalKm, EmptyKm, EmptyKmPct, FuelLitres, FuelCost, Co2Kg, OpenDamageCount, DeliveryReliabilityPct, OnTimeArrivalPct, AvgEtaDeviationMinutes, FailedDeliveries, PartialDeliveries, OpenExceptions, CostOverrunTripCount, AvgCostOverrunPct, TopCustomers (CustomerId, Name, Revenue, AllocatedCost, Profit, MarginPct)[≤10], KmPerDriver (DriverId, Name, Km, Hours)[≤10]` — plus `TripProfitabilityRowDto(TripId, TripNumber, TripDate, DriverName, VehicleNumber, CustomerNames, Revenue, EstimatedCost, ProjectedCost, FinalCost, Profit, MarginPct, TotalKm, EmptyKm, Status, IsFinalized)` for `GET /api/kpi/trip-profitability`.

**Definitions implemented exactly as documented:** revenue = Σ order AgreedPrice on non-cancelled trips in range (today = TripDate == today); cost = FinalCost ?? ProjectedTotal ?? EstimatedTotal from summaries; utilisation = Σ trip hours (actual else planned, skip unknown) / (active vehicle count × Mon–Fri days in range × 8h) × 100 (vehicle filter → that vehicle only); empty-km% = ΣEmpty/ΣTotal×100; reliability = terminal-`Completed` unloading StopExecutions / unloading stops on non-cancelled trips ×100; on-time = ArrivedAt ≤ (ConfirmedTo ?? RequestedTo ?? PlannedTo), stops with no window excluded; ETA deviation = avg minutes (ArrivedAt − newest StopEtaHistory.Eta with RecordedAt ≤ ArrivedAt), stops without history/arrival excluded; CO2 = litres × factor(FuelType: Diesel → Co2KgPerLitreDiesel, else Other; Electric/Hydrogen → 0); overrun% per trip = (final−estimated)/estimated×100 when both > 0. Every ratio zero-denominator-safe (→ 0 / null, tested). Filters compose (customer via TripOrder→order.CustomerId; revenue restricted to matching orders, cost allocated per decision 9).

**Steps:**
- [ ] DTOs + query service + controller + permissions + roles + docs.
- [ ] Tests: deterministic seeded scenario (2 trips, known rates/summaries/stops/ETA history) asserting every KPI number; zero-data tenant → all zeros no crash; each filter; tenant isolation; HR role seeded with expected codes.
- [ ] Full suites.
- [ ] Commit `feat(kpi): management KPI read model with documented tested definitions and HR role`.

### Wave 7 — KPI dashboard frontend

Load the **dataviz skill** before building the tiles.

**Files (frontend):**
- Create: `src/features/kpi/{types.ts, api/kpiApi.ts, pages/KpiDashboardPage.tsx, pages/KpiTripsPage.tsx, components/KpiCard.tsx, kpi.css}`
- Modify: `AppRoutes.tsx` (`/kpi`, `/kpi/trips`), `Sidebar.tsx` ("KPI's" — kpi.view; under Rapportage group with Dashboard)
- Test: `src/features/kpi/__tests__/kpiMeta.test.tsx` (card renders value + link; pct formatting; zero-safe)

**Page:** FilterBar — period presets (Vandaag / Deze week / Deze maand / Aangepast from–to) + SearchableSelects customer/driver/vehicle (options endpoints exist); sections Financieel / Vloot & km / Uitvoering; each `KpiCard` (label, value, hint, optional tone) deep-links: financial + km cards → `/kpi/trips?from&to&...` (report page, profitability.view; card hidden without underlying data permission — financial cards need profitability.view else hidden); damage → `/fleet`; exceptions → `/exceptions`; reliability/on-time/ETA → `/kpi/trips`; fuel → `/tank-cards`. `/kpi/trips` = DataTable of `TripProfitabilityRowDto` with same filters, row → `/planning/:id`, totals footer.

**Steps:**
- [ ] Invoke dataviz skill; build page/cards/report/table per its guidance (stat tiles, accessible, dark-mode aware).
- [ ] Wire routes/sidebar/permissions; FE tests; all FE checks; backend green.
- [ ] Commit `feat(kpi): management KPI dashboard with filters and drill-down report`.

### Wave 8 — XLSX exports

**Files:**
- Modify: `TransportationService.Api.csproj` (+`ClosedXML` latest stable)
- Create: `Modules/Reporting/Services/IKpiExportService.cs` + `KpiExportService.cs` (9 builders reusing `IKpiQueryService` row queries; shared `WriteCriteriaSheet`; helper `SetText` guarding `=+-@\t` prefixes though ClosedXML stores strings as text — belt-and-braces documented)
- Modify: `KpiController.cs` — `GET api/kpi/export?report=trip-profitability|customer-profitability|vehicle-utilisation|driver-hours|empty-km|fuel|co2|eta-performance|delivery-reliability&from&to&customerId&driverId&vehicleId` (kpi.export) → FileContentResult xlsx, filename `kpi-{report}-{yyyyMMdd}.xlsx`
- Modify: FE `KpiDashboardPage`/`KpiTripsPage` — "Exporteren (Excel)" select+button (kpi.export), blob download like orders CSV
- Test: `Tests/Reporting/KpiExportServiceTests.cs` (open workbook back via ClosedXML: sheet names, criteria sheet fields incl. generated timestamp + filters, row counts match query, numeric cells are numeric, date format `dd-MM-yyyy`, a customer named `=HYPERLINK(...)` lands as text cell not formula, tenant isolation, unknown report key → 400)
- [ ] Steps: package + service + endpoint + FE buttons + tests + full suites.
- [ ] Commit `feat(kpi): nine real XLSX exports with criteria metadata and formula-injection safety`.

### Wave 9 — Mobile app readiness

**Files (frontend):**
- Create: `public/manifest.webmanifest` (name "Transport Portaal", short_name "Portaal", start_url "/portal", display "standalone", background/theme colors from tokens, icons 192/512 png + favicon.svg any)
- Create: `public/icons/icon-192.png`, `icon-512.png` (generated via scratchpad node script — simple truck-glyph tile, committed binaries)
- Create: `public/sw.js` (versioned caches; install: precache `/` + `/index.html` + manifest; fetch: `/api/` network-only; navigations network-first→cached shell; hashed `/assets/` cache-first; activate cleans old caches)
- Modify: `index.html` (manifest link, theme-color, apple-touch-icon), `src/main.tsx` (PROD-only SW registration)
- Create: `src/hooks/useOnlineStatus.ts` + `src/components/layout/OfflineBanner.tsx` (aria-live="polite", "Offline — wijzigingen zijn niet mogelijk tot de verbinding terugkeert"); mounted in `AppLayout`
- Rewrite: `src/features/portal/pages/PortalDashboardPage.tsx` → role-based module launcher: tile grid (≥44px targets) filtered by permission/link: Mijn ritten (driver_workflow.view), Mijn planning, Verlof aanvragen, Kwalificaties, Meldingen, Uitzonderingen (exceptions.view), Scannen & laden (scanning.view → /my-trips), Profiel; keeps personal dashboard stats block
- Create: `src/components/ui/PhotoCaptureInput.tsx` (label+input `accept="image/*" capture="environment"`, preview, ≥48px target) adopted in the exceptions photo upload form
- Modify: portal CSS touch-target audit (44px min interactive heights)
- Test: `src/features/portal/__tests__/portalLauncher.test.tsx` (tiles filtered by permissions; driver sees Mijn ritten, plain employee doesn't)

**Steps:**
- [ ] Manifest/icons/SW/registration/offline banner (no offline-sync claims anywhere in copy).
- [ ] Launcher + PhotoCaptureInput + touch targets.
- [ ] FE tests + lint/tsc/test/build (`vite build` output includes sw + manifest; verify dist).
- [ ] Backend suite green (untouched).
- [ ] Commit `feat(portal): installable mobile-ready app shell with role-based launcher and offline indicator`.

### Wave 10 — Full verification, role matrix, docs

- [ ] Rebuild dev DB from zero: stop API; `docker compose up -d`; drop/recreate `transportation_service`; `dotnet ef database update` (all migrations from scratch — proves fresh-DB path); start API (seeders create catalog incl. new codes, default roles incl. HR **with** new permissions, dev admin).
- [ ] Smoke script `smoke-milestone.mjs` in scratchpad (admin login pattern): seed via API — customer, location, employee+driver (+user account w/ password via admin endpoint), HR/Planner/Management/plain-Employee users with role assignment; orders with AgreedPrice; rate card; trip lifecycle: create draft w/ driver (→ entry appears in schedule), reassign driver (entry moves), set planned distance/times, plan (estimate lines appear), approve overlapping leave for second driver + assert Blocking conflict on assignment, execute stops (driver account), complete (actuals + entry actual times), manual toll line, override with reason, finalize (frozen totals), rate change → totals unchanged; KPI dashboard values match deterministic expectations; XLSX export downloads + opens (content-type + size + criteria row via unzip check of sharedStrings for marker); conflict override flow on shift; deep-link 403 matrix: Employee→/api/trips/{id} 403, Employee→costing 403, Driver→costing 403 + profitability 403, HR→employee-planning 200 + costing 403, Planner→conflicts 200 + kpi 403, Management→kpi/export/profitability 200; portal self-scope: employee sees only own planning entries.
- [ ] Full: backend build 0 warnings, `dotnet test` all green, `npm run lint`, `npx tsc -b`, `npm run test`, `npm run build`.
- [ ] `git status` clean (plan doc committed wave 1 or here), logical commit chain intact; memory files updated.
- [ ] Final report per spec §15 (15 numbered sections).

## Self-Review

- Spec §1 sync → wave 1 (all listed fields incl. tenant/source type/trip id/actuals/vehicle/route; rules: create/assign-later/move-atomic/date-update/cancel-preserve/delete-consistent/complete-actuals/tenant/audit/concurrency-unique-index/no-duplicates; distinguishable via separate entity + SourceType + states).
- §2 conflicts → wave 2 (sources: trips/leave/sick/training/manual shifts/unavailable(=Other absence)/overlapping trips; surfaced on trip planning (engine), personnel grid, driver assignment (trip detail), employee detail (wave 3 planning tab), HR overview (=grid + absence review context exists); severities 3-level; examples mapped; no silent overwrite = 409+override).
- §3 clickable → waves 1+3 (names→employee detail; chips per source; tab deep links: overview/planning/absences/qualifications/driver profile(link card + ritten)/documents(kwalificaties)/trip history; notifications excluded — decision 14; permission-trimmed links; hover/focus details incl. conflict).
- §4 visual states → waves 1–2 (12 states + conflict marker; icon+label+tooltip+legend+contrast; trip blue, cancelled muted; colours per suggestion where non-clashing — documented deviation for existing shift statuses).
- §5 cost model → wave 4 (all 15 component types incl. subcontractor/ferry via manual kinds; line fields complete incl. override+audit).
- §6 calculations → wave 4 (estimate inputs incl. planned empty km informational, consumption chain, rates; actual incl. fuel records, waiting, overtime, completed stops; three totals separated; snapshots + effective dates → historical preservation tested).
- §7 profitability → waves 4–5 (all 10 measures; multi-order allocation defined+tested; permissions incl. drivers excluded).
- §8 configuration → waves 4–5 (all listed knobs incl. CO2 factors + overtime rules + effective dates).
- §9 dashboard → waves 6–7 (filters date/customer/driver/vehicle; dept/type documented out; all KPIs mapped — revenue/profit today+period, margin, per-trip, per-customer, utilisation, empty km + %, km/driver, fuel, fuel cost, CO2, damage, reliability, on-time, ETA deviation, failed, partial, exceptions, overruns; cards deep-link; backend read models).
- §10 definitions → wave 6 (docs/kpi-definitions.md + unit tests incl. zero denominators).
- §11 export → wave 8 (9 XLSX reports, real ClosedXML, filters, permission, tenant isolation, injection guard, EU formats, timestamp, criteria sheet).
- §12 mobile → wave 9 (one backend; role modules per permissions; mobile routes exist (/portal, /my-trips); large targets; minimal typing (pickers); camera capture component; PWA manifest+SW; app shell; online/offline indicator; no offline-sync claims; same API).
- §13 permissions → waves 2/4/6 + matrix wave 10 (all nine codes exist or added; role checks live-verified).
- §14 tests → distributed per wave, each listed case owned by a named test file.
- §15 verification → wave 10.
- Type consistency: `ConflictSeverity` shared Common enum used by both DTO families; `TripStatus` reused on entry; `ScheduleEntryDto` shape identical BE record ↔ FE type; costing DTO names match FE api layer; `KpiDashboardDto` fields match FE types file.
