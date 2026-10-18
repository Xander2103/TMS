# Remediation integration 2026-10-18 — Core + Warehouse + Exceptions

Branch `integration/core-warehouse-exceptions`, started from `remediation/core-master-integrity` @ `44e3d1c1`
(authoritative). Warehouse (`remediation/warehouse-inventory`) and Exceptions (`remediation/exception-management`)
were both built from the older Core head `85f0a9e9` and were cherry-picked one commit at a time:

| Source | Commits (in order) |
|---|---|
| Warehouse | `cd001c50` `f899d25d` `18dc3741` |
| Exceptions | `107e4a9f` `72f524d8` `00ff273c` `b21b8b6b` |

The integrated branch differs from `44e3d1c1` in exactly the union of the files the two branches touched; every
line those branches added is present, except the deliberate resolutions below.

## Decisions

**Offboarding — Core stays authoritative.** `EmployeeOffboardingService` (triggers, residual-access sweep,
driver-only deactivation, fixed/current vehicle + fixed trailer release, attendance shutdown, session revocation,
audit reasons, reactivation safeguards) is unchanged. Warehouse's edit to the old static `EmployeeOffboarding`
was not taken; its WH-H5 count is appended to Core's summary as `OutstandingEquipment` and recorded in the
`Deactivated` audit. The HR dialog reads the dedicated `GET /api/inventory/employees/{id}/outstanding` and shows
code, name, serial, issue and due date next to Core's consequences. The warning never blocks and never returns
anything; the Magazijn processes the real return later in *Uitgiftes & retours*.

**Asset catalogue = HR/Admin; operations = Magazijn.** Magazijn receives, corrects, issues, returns and sees loans
(`inventory.issue`, `inventory.return`) through the safe employee picker (number, name, active). It has no
`employees.*`, `users.view`, `absences.view_medical`, `issued_items.manage_templates` or `inventory.manage`.

**Role-template ladder.** Both branches claimed step 39. Final: **v38** Core master data → **v39** Warehouse
(Magazijn + HR: `inventory.issue`, `inventory.return`) → **v40** Exceptions (Planner + Dispatcher:
`problems.create_redelivery`). `DefaultRoleUpgrades.CurrentVersion = 40`. Versions are unique and contiguous
(guarded by `UpgradeLadder_HasUniqueContiguousVersions_WarehouseIs39_ExceptionsIs40`).

**Charge decision.** `POST /api/incidents/{id}/charge/decide` requires `problems.approve_charge` only (Management,
Boekhouding — since v28). Boekhouding does not hold, and does not need, `incidents.manage`.

**Redelivery.** `POST /api/incidents/{id}/redelivery` accepts `orders.create`, `orders.manage` or the controlled
`problems.create_redelivery` (Planner, Dispatcher). Management holds none of these (accepted Exception policy,
unchanged) and gets 403.

**DossierService keeps both fixes.** The default dossier date is `TenantTimeZone.TodayAsync` (Core MD-M3) and the
detail's incidents come from `ProblemScope.ForDossierAsync` / `ProblemScope.Incidents` (Exceptions EX-04).
Guarded by `Integration_MergedDossierService_TenantLocalDate_AndFullProblemScope`, which fails if either fix is
reverted.

**Migrations.** No reconciliation migration was needed; the auto-merged model snapshot equals the runtime model.
Execution order around the remediation:

```
20261018123754_ExecutedIdentitySnapshots          Core
20261018143508_WarehouseInventoryIntegrity        Warehouse
20261018144827_ExceptionManagementRemediation     Exceptions
20261018145336_IssuedInvoiceUblNoteSnapshot       Core
20261018155812_IncidentDossierScopeBackfill       Exceptions
```

A database already on Core (`…145336` applied) applies the three later-picked migrations out of timestamp order;
this was verified on PostgreSQL. Run `scripts/preflight-master-data.sql` before deploying, as for Core.

## Verification (isolated stack PG 5441 / API 5028 / Vite 5182)

| Check | Result |
|---|---|
| Fresh database, all 190 migrations | PASS |
| Upgrade from a pre-remediation copy (181 → 190) and from a Core-deployed copy (187 → 190) | PASS |
| Preflight on both upgrade copies | no conflicts |
| `has-pending-model-changes` | no changes |
| Role upgrade on PostgreSQL 38 → 40, restart idempotent | PASS |
| Offboarding + outstanding equipment (browser) | PASS 30/30 |
| Magazijn permissions + desk (browser) | PASS 22/22 |
| Exception → Incident → Damage → Fleet, confirmation hold/manual (browser) | PASS 25/25 |
| Dossier date + order-only / trip-only incident scope | PASS 9/9 + backend test |
| Redelivery permissions and content | PASS 22/22 |
| Boekhouding charge approval without `incidents.manage` | PASS 10/10 |
| Core smokes (issued CMR/LB/invoice/UBL byte-identical to pre-integration copies, concept stamp, unit/customer reference guards, end-date access, PO toggle, parallel numbering) | PASS 15/15 + numbering PASS |
| Warehouse smokes | PASS 11/11 |
| Exception smokes (incl. customer isolation 404) | PASS 12/12 |

Evidence scripts and screenshots: `~/Documents/TMS-acceptance-evidence/integration-20261018`.

## Known residuals (not introduced by the integration)

- The redelivery date still takes "today" from the UTC date (`IncidentService.CreateRedeliveryAsync`), as it did
  on Core `44e3d1c1`; Core's MD-M3 fix covered new orders and dossiers only. A failed delivery registered between
  local midnight and 02:00 would plan the redelivery one day early.
- A damage report created from an incident or afwijking is dated with the UTC date of the report instant.
