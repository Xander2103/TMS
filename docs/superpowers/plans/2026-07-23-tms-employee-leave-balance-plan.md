# Employee Leave Balance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (inline). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Implement the configurable leave-type / balance-type leave-balance subsystem from spec `5c8e62c`.

**Architecture:** New `Hr` entities (`LeaveBalanceType`, `LeaveType`, `EmployeeLeaveBalance`, `LeaveBalanceAdjustment`, `LeaveEntitlementSettings`) + additive `Absence.LeaveTypeId`. Used/pending days are computed live from `Absence` records per balance type by a pure `LeaveDayCalculator`; `LeaveBalanceService` composes entitlement + carry-over + adjustments − used − reserved-pending. HR + self-service controllers; `Verlofsaldo` employee tab, portal card, and settings UI. Additive migration + idempotent v10 role upgrade + seeded defaults.

**Tech Stack:** ASP.NET Core + EF Core (Npgsql), xUnit + SQLite in-memory tests; React 19 + TS + Vite, Vitest.

## Global Constraints

- Controller → Service → `TransportationDbContext`; manual `TenantId` predicates (`Where(x => x.TenantId == _tenantContext.TenantId)`); `AuditableTenantEntity` bases (audit stamps set by interceptor — never in services); `IAuditService.RecordAsync(entityType, entityId, action, old, new, ct)` AFTER `SaveChangesAsync`; validation via `DomainValidationException`.
- Enums stored as strings (`HasConversion<string>()`); decimals `decimal(6,2)`; `DateOnly`.
- Permissions added to `PermissionCodes` (const + `All`), seeded to new tenants in `DefaultRoleDefinitions`, granted to existing via a new `UpgradeStep(10, …)` with `CurrentVersion = 10`.
- Additive migration only: `dotnet ef migrations add <Name> --project TransportationService.Api`. Never edit historical migrations.
- Backend tests in `TransportationService.Api.Tests/Hr` using `SqliteTestDbContext`, `DevTenantContext`, `DevCurrentUserContext`, `TestClock`. Run with `-p:UseAppHost=false --output C:/tmp/tms-test-out` (dev server locks the normal bin).
- Frontend gates: `npx tsc -b --noEmit`, `npm run lint`, `npm test`, `npm run build`.

---

## Commit 1 — Data model, migration, seeding

**Files (create):** `Modules/Hr/Entities/LeaveBalanceType.cs`, `LeaveType.cs`, `EmployeeLeaveBalance.cs`, `LeaveBalanceAdjustment.cs`, `LeaveEntitlementSettings.cs`; `Modules/Hr/Configurations/{LeaveBalanceType,LeaveType,EmployeeLeaveBalance,LeaveBalanceAdjustment,LeaveEntitlementSettings}Configuration.cs`; `Modules/Hr/Services/LeaveDefaults.cs` (seeded default codes). **Modify:** `Modules/Hr/Entities/Absence.cs` (+`Guid? LeaveTypeId`), `AbsenceConfiguration.cs` (map LeaveTypeId + index), `Data/TransportationDbContext.cs` (DbSets). **Migration:** `dotnet ef migrations add LeaveBalances`.

**Entities:**
- `LeaveBalanceType : AuditableTenantEntity` — `Code`(30), `Name`(100), `Description?`(500), `IsActive`(=true), `SortOrder`.
- `LeaveType : AuditableTenantEntity` — `Code`(30), `Name`(100), `Description?`(500), `IsActive`, `IsPaid`, `DeductsFromBalance`, `BalanceTypeId?`, `AbsenceType`(enum), `RequiresApproval`, `AllowsHalfDays`, `RequiresReason`, `RequiresAttachment`, `VisibleInSelfService`, `Colour`(20), `SortOrder`.
- `EmployeeLeaveBalance : AuditableTenantEntity` — `EmployeeId`, `CalendarYear`(int), `BalanceTypeId`, `BaseEntitlementDays`(decimal), `CarryOverDays`(decimal=0).
- `LeaveBalanceAdjustment : AuditableTenantEntity` — `EmployeeLeaveBalanceId`, `Days`(decimal), `Reason`(500), `Kind`(enum `Grant|Seniority|Correction|Override`).
- `LeaveEntitlementSettings : AuditableTenantEntity` — `DefaultAnnualEntitlementDays`(decimal=20), `PendingReservesBalance`(=true), `AllowNegativeBalance`(=false), `CarryOverEnabled`(=true), `MaxCarryOverDays`(decimal?).

**Config:** unique `(TenantId, Code)` on the two type tables; unique `(TenantId, EmployeeId, CalendarYear, BalanceTypeId)` on `EmployeeLeaveBalance`; enums→string; decimals `HasPrecision(6,2)`; `HasQueryFilter(!IsDeleted)`; FK `EmployeeLeaveBalance→LeaveBalanceType`, `LeaveBalanceAdjustment→EmployeeLeaveBalance (Cascade)`, `Absence.LeaveTypeId→LeaveType (Restrict, nullable)`.

**Seeding:** `LeaveDefaults` exposes the 5 balance types + 10 leave types (per spec table). A tenant-scoped `EnsureSeededAsync` in `LeaveBalanceService` (called lazily) inserts any missing default code for the tenant — never overwrites existing. Existing absences keep `LeaveTypeId = null`; computation maps null → the default LeaveType for the absence's `AbsenceType`.

- [ ] Write entity classes + configs + DbSets + Absence change.
- [ ] `dotnet ef migrations add LeaveBalances --project TransportationService.Api`; verify snapshot builds.
- [ ] Test `LeaveModelTests`: entity round-trip via `SqliteTestDbContext`; unique `(employee,year,balanceType)` throws on duplicate; `Absence.LeaveTypeId` persists. Run filtered.
- [ ] Commit `feat(hr): leave-balance data model + additive migration + seeded defaults`.

## Commit 2 — LeaveDayCalculator + LeaveBalanceService

**Files (create):** `Modules/Hr/Services/LeaveDayCalculator.cs`, `LeaveBalanceService.cs` (+`ILeaveBalanceService`), `Modules/Hr/Dtos/LeaveBalanceDtos.cs`. **Modify:** `Program.cs` (register service).

**`LeaveDayCalculator` (pure/static):** `decimal CountDaysInYear(DateOnly start, DateOnly end, AbsencePartDay part, int year)` → clip `[start,end]` to `[Jan1,Dec31]`; if the (clipped) range is a single day and `part != FullDay` → `0.5`, else inclusive day count (`end - start + 1`).

**`ILeaveBalanceService`:**
```csharp
Task EnsureSeededAsync(CancellationToken ct);
Task<EmployeeLeaveBalanceDto> GetForEmployeeAsync(Guid employeeId, int year, CancellationToken ct);
Task SetEntitlementAsync(Guid employeeId, int year, Guid balanceTypeId, decimal baseDays, decimal carryOver, CancellationToken ct); // manage
Task AddAdjustmentAsync(Guid employeeId, int year, Guid balanceTypeId, decimal days, string reason, LeaveAdjustmentKind kind, CancellationToken ct); // adjust
Task<IReadOnlyList<LeaveAdjustmentDto>> GetAdjustmentsAsync(Guid employeeId, int year, Guid balanceTypeId, CancellationToken ct);
Task<LeaveAvailabilityResult> CheckRequestAsync(Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end, AbsencePartDay part, CancellationToken ct); // over-request guard
```
`EmployeeLeaveBalanceDto` = year + rows per active balance type: `{ balanceTypeCode, name, base, carryOver, adjustments, approvedUsed, pendingReserved, remaining, pendingReserves }`. Used/pending computed by summing `Absence` (Approved / Requested+UnderReview) whose resolved `LeaveType.DeductsFromBalance && BalanceTypeId == row` via `LeaveDayCalculator`. `remaining = base + carryOver + adjustments − approvedUsed − (settings.PendingReservesBalance ? pendingReserved : 0)`. Carry-over capped by `MaxCarryOverDays`. All mutations audited.

- [ ] Test `LeaveDayCalculatorTests`: full-day inclusive count; half-day single day = 0.5; range clipped to year; cross-year split.
- [ ] Test `LeaveBalanceServiceTests`: entitlement create + get; add/deduct adjustment; approved Vacation reduces WETTELIJK; ADV reduces ADV not WETTELIJK; sick/unpaid reduce nothing; pending reserve on/off; negative-balance `CheckRequestAsync` blocks; tenant isolation.
- [ ] Implement; run filtered tests.
- [ ] Commit `feat(hr): leave-day calculator + leave-balance service (computation, adjustments, availability)`.

## Commit 3 — Permissions + idempotent v10 role upgrade

**Files (modify):** `Modules/Identity/PermissionCodes.cs` (+`LeaveBalancesView/Manage/Adjust/ViewOwn`, `LeaveTypesManage` consts + `All`), `Data/DefaultRoleDefinitions.cs` (grant to hr/management/chauffeur/CommonView), `Data/DefaultRoleUpgrades.cs` (`UpgradeStep(10,…)`, `CurrentVersion = 10`).

- [ ] Add 5 codes + `All` entries.
- [ ] hr → all 5; management → `LeaveBalancesView`; chauffeur → `LeaveBalancesViewOwn`; `CommonViewPermissions` += `LeaveBalancesViewOwn`.
- [ ] `UpgradeStep(10, "Leave balance 2026-07-23", { hr: [view,manage,adjust,view_own,leave_types.manage], management: [view], chauffeur: [view_own] })`; bump `CurrentVersion = 10`.
- [ ] Test `DefaultRoleSeederTests` (extend): re-running the seeder is idempotent; hr gains manage; chauffeur gains view_own; running twice doesn't duplicate.
- [ ] Commit `feat(identity): leave-balance permissions + v10 role upgrade`.

## Commit 4 — Controllers, HR endpoints, self-service, over-request guard

**Files (create):** `Modules/Hr/Controllers/LeaveBalancesController.cs` (`api/employees/{employeeId}/leave-balance`), `LeaveTypesController.cs` (`api/leave-types`, `api/leave-balance-types`), `LeaveSettingsController.cs` (`api/hr/leave-settings`). **Modify:** `Modules/Portal/Controllers/MeController.cs` (+`GET leave-balance`), `Modules/Portal/Services/PortalService.cs` (self balance + self-request guard), `Modules/Hr/Controllers/AbsencesController.cs`/`AbsenceService.cs` DTOs (+optional `leaveTypeId`; derive `AbsenceType`; per-type validation).

Endpoints gated: GET balance → `LeaveBalancesView`; PUT entitlement → `LeaveBalancesManage`; POST adjustment / override → `LeaveBalancesAdjust`; settings GET → `EmployeesView`, PUT → `LeaveBalancesManage`; types read → `EmployeesView`, write → `LeaveTypesManage`; `me/leave-balance` self-scoped `[Authorize]` + `LeaveBalancesViewOwn`.

- [ ] Tests (`LeaveBalancesEndpointTests` / service-level): employee-only-own via PortalService; HR can edit; over-request blocks on self path unless negative allowed / override; existing absence tests still pass.
- [ ] Implement; run Hr + Portal test suites.
- [ ] Commit `feat(hr): leave-balance HR + self-service endpoints + absence leave-type + over-request guard`.

## Commit 5 — HR Verlofsaldo tab + APIs (frontend)

**Files (create):** `Web/src/features/leave-balance/api/leaveBalanceApi.ts`, `types.ts`, `components/LeaveBalanceTab.tsx` (+ `SetEntitlementDialog`, `AdjustBalanceDialog`), `leave-balance.css`, `__tests__/leaveBalanceTab.test.tsx`. **Modify:** `EmployeeDetailPage.tsx` (add `verlofsaldo` tab between documenten and bedrijfsmiddelen), `EmployeeForm`/`NewEmployeePage` extra section order (Verlofsaldo placeholder "beschikbaar na aanmaken" on create).

- [ ] Tests: renders rows per balance type; year selector; HR sees Jaarrecht/Saldo buttons with `manage`/`adjust`; read-only without.
- [ ] Implement; `tsc`+`lint`+ vitest (feature).
- [ ] Commit `feat(leave-balance): HR Verlofsaldo tab + management dialogs`.

## Commit 6 — Self-service balance card (frontend)

**Files (create):** `Web/src/features/portal/components/MyLeaveBalanceCard.tsx`, `__tests__/myLeaveBalanceCard.test.tsx`; `getMyLeaveBalance` in `portalApi.ts`. **Modify:** portal dashboard/`modules.ts` (tile), employee own-record read-only panel.

- [ ] Tests: read-only (no edit controls); shows entitlement/carry-over/adjustments/approved/pending/remaining.
- [ ] Implement; frontend gates.
- [ ] Commit `feat(leave-balance): self-service balance card (portal + own record)`.

## Commit 7 — Settings UI + absence leave-type selector (frontend)

**Files (create):** `Web/src/features/leave-balance/pages/LeaveTypesSettingsPage.tsx`, `LeaveBalanceTypesSettingsPage.tsx` (+ routes, gated `leave_types.manage`). **Modify:** absence create/edit form (leave-type selector, self-service filtered to active+visible, driving reason/attachment/half-day requirements).

- [ ] Tests: settings CRUD forms render/validate; absence form lists only active(+visible) types.
- [ ] Implement; frontend gates.
- [ ] Commit `feat(leave-balance): settings UI for leave/balance types + absence leave-type selector`.

## Final verification

- [ ] Backend: `dotnet build` + full `dotnet test`.
- [ ] Frontend: `tsc -b --noEmit` + `lint` + `test` + `build`.
- [ ] Report: commits, migration name, totals, results, remaining limitations, final hash, clean worktree.

## Self-review (coverage)

Spec §data-model → C1; computation → C2; permissions → C3; API + over-request + absence LeaveTypeId → C4; HR UI → C5; self-service → C6; settings + selector → C7. Business rules (decimal, pending reserve, approved-reduces, rejected/cancelled-don't, negative block + override, own-only, statutory/ADV/recup separate balances, sick/unpaid don't reduce statutory, LeaveTypeId source of truth, history preserved, tenant isolation) → C1–C4 tests. Deferred (documented): year-end carry-over batch + expiry; bulk entitlement; planning-grid colour rendering.
