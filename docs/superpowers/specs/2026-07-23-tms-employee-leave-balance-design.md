# TMS Employee Leave Balance (Verlofsaldo) — Design Spec

**Status:** Approved decisions captured; awaiting spec review before planning.
**Date:** 2026-07-23
**Depends on / relates to:** existing `Hr` absence subsystem, `Identity` permissions, `Auditing`, `Portal` self-service. Extends the existing seam `AbsenceReviewContextDto.UsedVacationDaysThisYear`.

## Goal

HR can manage how many leave days an employee receives per calendar year, per **balance type**, using **configurable leave types**. Not every absence reduces the same balance (or any balance). Employees/drivers may view — never edit — their own balance. Approved absences remain the source of truth for used days; used and pending days are **computed live**, never stored as mutable counters.

## Approved decisions (from brainstorming)

- **Pending reserves balance by default** (settable per tenant).
- **Negative balance blocked by default** (settable per tenant); HR/higher may override with a mandatory, audited reason.
- **Manual carry-over now**; the automated year-end batch + carry-over expiry are a documented follow-up (settings seam kept).
- **Self-service view in both** the portal and the employee's own record (read-only).
- **Absences carry a `LeaveTypeId`** (additive) → true per-type routing (ADV vs Wettelijk verlof deduct different balances).
- Default statutory entitlement **20 days**, overridable per employee/year/balance type.
- Used/pending days computed live from `Absence` records — no stored counters, no double-counting.

## Global constraints

- Reuse the existing absence architecture; **do not create a duplicate absence system**. `Absence` keeps its `AbsenceType` enum (planning colours, existing reporting) and gains a nullable `LeaveTypeId`.
- Backend: Controller → Service → `TransportationDbContext`; manual `TenantId` predicates; `AuditableTenantEntity` bases; `IAuditService.RecordAsync` after `SaveChangesAsync`; validation via the existing `DomainValidationException`/`OperationResult` patterns. No MediatR/FluentValidation.
- Permissions: add codes to `PermissionCodes` (+ `All`), seed to new tenants in `DefaultRoleDefinitions`, grant to existing tenants via a **new `UpgradeStep` v10** (`CurrentVersion = 10`).
- Migrations: additive only (`dotnet ef migrations add … --project TransportationService.Api`); never edit historical migrations.
- Frontend: no form library; controlled `useState`; reuse `src/components/ui` kit; permissions gate UI **and** the backend gates authoritatively.
- Decimal days everywhere (`decimal(6,2)`; 0.5 supported).

---

## Data model (new `Hr` entities)

### 1. `LeaveBalanceType` — configurable balance buckets (per tenant)
`Code`, `Name`, `Description?`, `IsActive`, `SortOrder` (+ tenant/audit). Unique `(TenantId, Code)`.
**Seeded defaults:** `WETTELIJK` (Wettelijk verlof), `ADV`, `RECUP` (Recuperatie), `ANCIENNITEIT` (Anciënniteitsdagen), `COMPENSATIE` (Compensatie). HR may add custom balances.

### 2. `LeaveType` — configurable leave/absence types (per tenant)
`Code`, `Name`, `Description?`, `IsActive`, `IsPaid`, `DeductsFromBalance`, `BalanceTypeId?` (FK; **required when `DeductsFromBalance`**), `RequiresApproval`, `AllowsHalfDays`, `RequiresReason`, `RequiresAttachment`, `VisibleInSelfService`, `Colour`, `SortOrder`, `AbsenceType` (the existing enum value this maps to, so planning/history/reporting keyed on `AbsenceType` keep working) (+ tenant/audit). Unique `(TenantId, Code)`.
**Seeded defaults** (Name → AbsenceType, deducts→BalanceType):
| Name | AbsenceType | Deducts | Balance | Half | Self-service | Paid |
|---|---|---|---|---|---|---|
| Wettelijk verlof | Vacation | yes | WETTELIJK | yes | yes | yes |
| ADV | Vacation | yes | ADV | yes | yes | yes |
| Anciënniteitsdagen | Vacation | yes | ANCIENNITEIT | yes | yes | yes |
| Recuperatie | PersonalLeave | yes | RECUP | yes | yes | yes |
| Compensatie | PersonalLeave | yes | COMPENSATIE | yes | yes | yes |
| Klein verlet | PersonalLeave | no | — | no | yes | yes |
| Onbetaald verlof | Unpaid | no | — | no | yes | no |
| Ziekte | Sick | no | — | yes | yes | yes |
| Tijdskrediet | Unpaid | no | — | no | yes | no |
| Opleiding | Training | no | — | no | yes | yes |
| Andere | Other | no | — | no | no | yes |

### 3. `EmployeeLeaveBalance` — entitlement inputs, per employee/year/balance type
`EmployeeId`, `CalendarYear`, `BalanceTypeId`, `BaseEntitlementDays` (decimal), `CarryOverDays` (decimal, default 0) (+ tenant/audit). **Unique `(TenantId, EmployeeId, CalendarYear, BalanceTypeId)`** → prevents duplicate yearly records. Used/pending are **never** stored here. Changes to base/carry-over are audited (old/new + reason).

### 4. `LeaveBalanceAdjustment` — append-only manual-adjustment ledger
`EmployeeLeaveBalanceId` (FK), `Days` (+/− decimal), `Reason` (required), `Kind` (`Grant | Seniority | Correction | Override`) (+ tenant/audit; `CreatedByUserId`/`CreatedAt` = actor/timestamp). `ManualAdjustmentDays = Σ Days`. Never overwritten — corrections are new rows.

### 5. `LeaveEntitlementSettings` — per-tenant defaults (copy of `HrReminderSettings` pattern)
`DefaultAnnualEntitlementDays` (20), `PendingReservesBalance` (true), `AllowNegativeBalance` (false), `CarryOverEnabled` (true), `MaxCarryOverDays` (nullable). *(Carry-over expiry + year-end batch: deferred; seam noted.)*

### 6. `Absence` — additive change
Add nullable `LeaveTypeId` (FK → `LeaveType`). On create/edit the caller picks a `LeaveType`; the absence's `AbsenceType` is set from the chosen type's mapping. Existing rows (`LeaveTypeId = null`) are treated via a per-`AbsenceType` default type for computation. Per-request validation (`RequiresReason`, `RequiresAttachment`, `AllowsHalfDays`, `RequiresApproval`) is driven by the `LeaveType`.

---

## Computation (`LeaveBalanceService` + pure `LeaveDayCalculator`)

For `(employeeId, year, balanceType)`:
```
Base            = EmployeeLeaveBalance.BaseEntitlementDays   (or settings.DefaultAnnualEntitlementDays for the statutory balance when no row exists)
CarryOver       = EmployeeLeaveBalance.CarryOverDays
ManualAdj       = Σ LeaveBalanceAdjustment.Days
UsedApproved    = Σ day-count of Approved absences whose LeaveType.DeductsFromBalance && LeaveType.BalanceTypeId == balanceType, clipped to [Jan 1 … Dec 31], half-day aware
ReservedPending = same, for Requested + UnderReview
Remaining       = Base + CarryOver + ManualAdj − UsedApproved − (settings.PendingReservesBalance ? ReservedPending : 0)
```
`LeaveDayCalculator` reuses the existing calendar-day counting (`end − start + 1`, `PartDay` → 0.5 on single-day) for consistency, clipped to the calendar year. Non-deducting leave types (sickness, unpaid, training, klein verlet, tijdskrediet, andere) never affect any balance but still appear in planning/history.

## Over-request guard

On the **self-service** absence-create path (`PortalService`), for a `LeaveType` with `DeductsFromBalance`: if the request would push the linked balance below zero and `!AllowNegativeBalance` → validation error. HR-side creation (`AbsencesController`) is treated as authorised (unchanged, so existing absence tests stay intact); a negative outcome there is an audited `Override` adjustment. Overrides require `leave_balances.adjust` + reason and are audited.

## Permissions (new)

`leave_balances.view`, `leave_balances.manage` (set entitlement + carry-over + settings), `leave_balances.adjust` (add/remove adjustment days + negative override), `leave_balances.view_own`, `leave_types.manage` (configure leave types + balance types). **v10 grants:** `hr` → all; `management` → `view` (manage/adjust/leave_types.manage only if an admin explicitly grants); `chauffeur` + employee-facing common → `view_own`; `administrator` → automatic (catalog).

## API surface

- `GET /api/employees/{id}/leave-balance?year=` → per-balance-type computed DTO (`view`).
- `PUT /api/employees/{id}/leave-balance` set base entitlement + carry-over for a balance type/year (`manage`, audited; carry-over capped by `MaxCarryOverDays` when set).
- `POST /api/employees/{id}/leave-balance/adjustments` add +/− adjustment w/ reason + kind (`adjust`, audited); `GET …/adjustments` history.
- `GET/PUT /api/hr/leave-settings` (`view`/`manage`).
- `GET/POST/PUT /api/leave-types`, `/api/leave-balance-types` (`view` read / `leave_types.manage` write).
- `GET /api/me/leave-balance?year=` (self-scoped `MeController`, `leave_balances.view_own`).
- Absence create/update DTOs gain optional `leaveTypeId`; self-service lists only `IsActive && VisibleInSelfService` types.

## Frontend

- **HR — `LeaveBalanceTab`** on `EmployeeDetailPage`, section **between Documenten and Bedrijfsmiddelen** (employee nav order: Algemeen, Dienstverband, HR, Noodcontacten, Chauffeursprofiel, Kwalificaties, Documenten, **Verlofsaldo**, Bedrijfsmiddelen, Notities). Year selector; per balance type: Toegekend / Overdracht / Aanpassingen / Goedgekeurd opgenomen / In aanvraag (with explicit "gereserveerd" indicator) / Resterend; "Jaarrecht instellen", "Saldo aanpassen" (+ reason), adjustment history. Gated by `leave_balances.view/.manage/.adjust`.
- **Employee create:** the Verlofsaldo section shows "beschikbaar na aanmaken" (entitlements need an `EmployeeId`).
- **Self-service:** read-only "Mijn verlofsaldo" card in the portal (`/portal` tile + `getMyLeaveBalance`) and a read-only panel on the employee's own record.
- **Settings:** `LeaveType` + `LeaveBalanceType` management pages (gated by `leave_types.manage`) with the full field set.
- **Absence create/edit:** a `LeaveType` selector (self-service filtered), driving reason/attachment/half-day/approval requirements.

## Business rules

One balance per employee/year/balance type (unique index); decimal days; approved reduces / rejected+cancelled don't (natural — computed from `Approved`); pending reserves per setting; over-request blocked unless `AllowNegativeBalance` or audited override; year transitions never auto-invent carry-over (manual); non-deducting types never reduce statutory leave; inactive leave types not selectable; self-service sees only `VisibleInSelfService` types.

## Audit

Record (actor, timestamp, old/new where relevant, reason): entitlement set/changed, carry-over changed, adjustment added, override used, negative-balance approved, leave-type/balance-type created/changed.

## Testing

**Backend (`Tests/Hr`, `SqliteTestDbContext` + `DevTenantContext`/`DevCurrentUserContext`):** entitlement create; unique employee/year/balance-type; add/deduct; half-days; statutory leave deducts the statutory balance; ADV deducts the ADV balance; sickness/unpaid don't reduce annual leave; pending reserve on↔off per type; inactive leave type not selectable; self-service sees only self-service-visible types; employee can only view own; employee cannot edit; HR can edit; management only with permission; negative-balance block + override w/ reason; tenant isolation; carry-over; concurrency (unique-index race); audit written.
**Frontend:** `LeaveBalanceTab` render/roles; portal card; self-view read-only; year selection; leave-type selector filtering.
**Gates:** backend `dotnet build` + full tests; frontend tsc + eslint + vitest + build. Additive migration only.

## Staged as follow-up (out of scope for the first implementation)

Automated year-end carry-over batch + carry-over expiry (settings seam present); bulk entitlement-set-for-all-employees; per-`LeaveType` colour rendering in the planning grid beyond storing the colour.

## Commit plan (logical)

1. Backend: balance/leave types + settings + `EmployeeLeaveBalance` + adjustment ledger + `Absence.LeaveTypeId` + migration + seeding (tests).
2. Backend: `LeaveBalanceService` computation + `LeaveDayCalculator` (tests).
3. Backend: permissions + v10 role upgrade.
4. Backend: controllers (HR + settings + types) and self-service `/api/me/leave-balance` + over-request guard.
5. Frontend: HR `LeaveBalanceTab` (employee nav section) + APIs.
6. Frontend: self-service portal/own-record card.
7. Frontend: settings management for leave/balance types.
