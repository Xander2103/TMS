# Dossier & transport-order workflow — end-to-end audit

*Datum: 2026-08-30 · Branch `nav-redesign` @ `fb7c0fb` · Analyse-only (geen code gewijzigd) · Taal: rapport in het Engels, UI-termen in het Nederlands.*

**How this audit was produced.** Seven independent reviewers each inspected one dimension of the workflow (backend domain rules, frontend workflow/state, security & tenant isolation, pricing & finance, audit/history & data integrity, test coverage, UI/UX as a senior product designer). A second, independent pass (two fresh reviewers: backend cross-module; frontend cross-screen) then hunted for what the first pass missed, contradictions between modules, hidden coupling and state-combination edge cases, and challenged first-pass findings. All findings were reconciled, de-duplicated and re-ranked; the highest-impact claims were re-verified line-by-line by the coordinating reviewer. Every finding cites `file:line` on the commit above. Nothing in this report is inferred from names or tests alone.

**Source-report IDs** (B/F/S/P/A/T/U = pass 1; R2B/R2F = pass 2) are kept in each consolidated finding so the raw evidence trail stays traceable.

---

## 0. Wave 1 — resolution status (2026-08-30, branch `nav-redesign` fb7c0fb → ff3df70)

Wave 1 implemented the six production blockers below. Each finding keeps its original text; a **Resolution** block under its heading records the implementation. Full ledger, task reports and reviews: `.superpowers/sdd/2026-08-30-wave1-production-blockers/` (git-ignored); plan `docs/superpowers/plans/2026-08-30-wave1-production-blockers.md`.

| Finding | Status | Implementation (merge commits) | Regression tests |
|---|---|---|---|
| C-04 `Invoiced` missing from `Transitions` | **Resolved** | 8d7cb6e (bec68f5, 9a0ec73) | `Orders/OrderInvoicedStatusTests.cs` |
| C-01 stop-id churn breaks packages/executions/labels | **Resolved** (data repair for pre-fix rows: migration `PackageStopPinRepair`, unapplied) | 8d7cb6e, e861ad1, ff3df70 | `Orders/OrderUpdateIntegrityTests.cs`, `OrderStopPinEvidenceTests.cs`, `OrderStopReferenceCoverageTests.cs`, `Packages/PackageLabelTimeZoneTests.cs`, FE `cargoStopRemap.test.tsx` |
| C-02 customer/entity via plain PUT | **Resolved** | 8d7cb6e, ff3df70 | `Orders/OrderUpdateIntegrityTests.cs`, FE `generalSectionCommercialLock.test.tsx` |
| C-03 wall-clock vs UTC | **Resolved** for stop windows (one convention: UTC on the wire, tenant zone `TenantSettings.Timezone` on screen and in backend decisions); **partially** for `dock_appointments` (kept wall-clock, Wave 2) and the remaining browser-zone planning-board/ETA/"today" sites (Wave 2). Existing rows re-encoded by migration `StopWindowTenantZoneReencoding` (unapplied). | ea5f6b6, e861ad1, ff3df70 | `utils/__tests__/transportTime.test.ts`, `orderStopTimeRoundTrip.test.ts`, `stopWindowTimeZone.test.tsx`, `displayPreferencesProvider.test.tsx`, `Orders/StopOpeningHoursWarningTests.cs`, `OrderWeekendSurchargeTimeZoneTests.cs`, `Planning/PlanningProposalTests.cs` |
| H-06 sent invoice cancel loophole | **Resolved** (finalized = Sent/Paid or transmission past Queued; credit notes mirror original fiscal data; zero-line Send refused; duplicate ids refused; entity change releases the pricing snapshot). Residual: no re-invoice path after a credit note (P-05, Wave 2); no partial unique index on invoice lines (Wave 2). | 45cec45, ff3df70 | `Invoicing/InvoiceFinalizationGuardTests.cs` |
| H-14 portal exposure | **Resolved** (`TransportOrderDocument.CustomerVisible` default false — migration `OrderDocumentCustomerVisible`, unapplied; `Notes`/`CancellationReason`/history reasons removed from portal DTOs; rejection reason via `CustomerMessage`; inactive customers refused in every resolver; identity-class guard `PortalPermissionScope` on every evaluator incl. JWT/`/me`, notification recipients, write-time guard in `UserService`). Ruling: stop `Instructions`/`Reference` remain portal-visible (customer-originated). Residual: legacy `orders.view` stays on upgraded `klantportaal` roles (refused everywhere; run the detection query pre-deploy). | d1a0fdf, 902414f | `CustomerPortal/DeactivatedCustomerAccessTests.cs`, `Security/PortalIdentity*Tests.cs`, `Security/AccountSecurityServiceTests.cs`, `Authentication/PortalIdentityAuthPermissionTests.cs`, FE `orderDocumentsPanelVisibility.test.tsx` |

**Deploy prerequisites — all three migrations are additive/data-only and NOT applied to any database:** walk the manual PostgreSQL checklist for `StopWindowTenantZoneReencoding` and `PackageStopPinRepair` on a restored copy first (`docs/delivery/operations.md` §1.2b — server ≥ PG13, tenant-zone gates, per-row EDI query 4b, dry-run, audit-row reconciliation, rollback rehearsal); the Task 3 client and the re-encoding migration must ship in the same release; run the customer-linked-internal-user detection query (task-4 report, "Release notes") before deploy; existing order documents become internal until republished.

### 0.1 Migration validation on a production-data snapshot (2026-08-30, final)

The three Wave 1 migrations were validated twice with the same harness (restore → snapshot → pre-flight checklist §1.2b → `dotnet ef database update` → post-checks → rollback/re-apply → app startup → drop/restore recovery): first on a dump of the local dev database, then on the **pre-reset production dump** (`transportationservice-migration-test.dump`, 784,988 bytes) restored into the disposable local DB `transportation_service_migtest` (Docker `postgres:16.14`; production is 16.15). Production was never connected to; nothing was applied outside the disposable copy.

| Check | Production snapshot | Dev dry run |
|---|---|---|
| Pre-flight gates Q0–Q3b (PG ≥ 13, usable timezone, settings row, orphan tenants) | all pass | all pass |
| Q4/Q4b EDI-written windows | 0 (0 EDI messages) | 0 |
| Q5 dry-run shift distribution | only `−02:00:00` (9 rows) | only `−02:00:00` (10 rows) |
| Row counts before → after | stops 52→52, packages 15→15, orders 9→9, documents 0→0, audit_logs 331→333 (+1 counter row per data migration) | 50→50, 15→15, 8→8, 0→0, 401→403 |
| `StopWindowTenantZoneReencoding` counters | `converted 9, skippedEdi 0, dstInvertedWindowsRepaired 0, alreadyInvertedLeftUntouched 0` | `converted 10`, rest 0 |
| Window values shifted | 13 values (9 PlannedFrom, 4 PlannedTo), every one exactly −02:00; NULL pattern unchanged; wall clock preserved for every row (e.g. `2026-08-06 10:00+00` → `08:00+00` = 10:00 local); 0 inverted windows | 16 values, same properties |
| DST | no window straddles a DST switch in either dataset (gap/overlap paths covered by unit tests only) | same |
| `PackageStopPinRepair` counters | `repairedLoadingPins 2, repairedDeliveryPins 2, stillAmbiguous 2`; all repaired pins point to a live stop of the same order; FK re-validation OK; 0 hard-dangling references | identical |
| `OrderDocumentCustomerVisible` | column added, `NOT NULL DEFAULT false`, 0 rows affected (no documents in the snapshot) | identical |
| EF pending afterwards | none | none |
| Rollback (`Down` to `CommercialWaveAuditFixes`) → re-`Up` | windows restored byte-exact (0 diffs), column dropped, `Down` counter row written; re-apply reproduces the identical result (idempotent); `PackageStopPinRepair.Down` is the documented no-op | identical |
| Application startup against the migrated copy | HTTP 401 on `/api/company-settings/display` after ~4 s, app connected only to the disposable DB, no error/exception lines | identical |
| Drop → restore recovery | back to the original state (52/15/331, history top `CommercialWaveAuditFixes`, no column) | identical |

**The two intentionally unrepaired packages.** Both belong to **ORD-0006**, a *soft-deleted* Draft order with 0 live stops (10 soft-deleted stops) and 2 live packages that the pre-Wave-1 delete path left behind. There is no live stop to re-point to, so `PackageStopPinRepair` correctly leaves them and reports them as `stillAmbiguous`. They are invisible to scanning (their order is deleted); the delete-cascade gap that created them is audit finding H-03 (Wave 2).

**Classification**

| Migration | Verdict |
|---|---|
| `20260830113629_OrderDocumentCustomerVisible` | **SAFE FOR PRODUCTION** (additive; existing documents become internal until republished — intended) |
| `20260830133955_StopWindowTenantZoneReencoding` | **SAFE WITH CONDITIONS**: ship in the same release as the Task 3 client (true on `nav-redesign`); do not change `tenant_settings.Timezone` while a rollback is still possible; if EDI-created orders or DST-straddling windows appear before deploy, re-run Q4b/Q5 on the pre-deploy dump first |
| `20260830134439_PackageStopPinRepair` | **SAFE FOR PRODUCTION** (never guesses; leftovers are counted and explained) |

### 0.2 Wave 1 versus Wave 2 — what remains open

**Resolved in Wave 1:** C-01, C-02, C-03 (stop windows and all backend/frontend consumers listed in §0; the residual sites are itemised below), C-04, H-06, H-14, plus the second-pass corrections folded into them (cargo↔stop binding by position, evidence-based pin rules, pricing-snapshot release on entity change, zero-line Send guard, portal identity in JWT/notifications, driver/kiosk/dock/attendance clocks). All six carry a "Resolution (Wave 1)" block under their heading.

**Remaining Critical (Wave 2):** C-05 redelivery orders built outside the order use case; C-06 half-flush in `UpdateAsync` / two-commit audit writes (EDI, import, portal, dossier activity).

**Remaining High (Wave 2):** H-01 `Version` not an EF concurrency token, most endpoints versionless; H-02 cancel/delete/manual status ignore trip membership; H-03 dossier containment, delete cascade, startup backfill re-wrap; H-04 locked-price guard incomplete; H-05 invoice readiness/snooze advisory; H-07 pricing gated on snapshot only; H-08 notification link paths; H-09 server validation messages discarded; H-10 200-row pickers; H-11 audit diffs/timeline/dossier history; H-12 preview omits equipment flags; H-13 hidden manual-price field; H-15 cargo delete orphans packages; H-16 PDF/UBL/export rounding; H-17 EDI duplicate/replay; H-18 ETA GET side effects; H-19 CMR parties; H-20 unsaved-changes guard; H-21 order page dead end; H-22 header actions; H-23 trip auto-completion swallowed; H-24 hard-coded Dutch; H-25 stale sibling panels; H-26 stop chronology/ADR; H-27 execution never re-prices; H-28 side effects in controller / permission parity; H-29 test harness (WebApplicationFactory, Postgres); H-30 FE permission umbrella; H-31 template copies overrides; H-32 history duplicates on retry, TripOrder race; H-33 shared-context sweeps, GDPR; H-34 blob downloads / race loads. **H-06 residuals:** P-05 re-invoice path after a credit note; partial unique index on invoice base lines. **H-14 residual:** legacy `orders.view` grant on upgraded `klantportaal` roles (refused everywhere; detection query pre-deploy). **C-03 residuals:** `dock_appointments` + `DockPlanningPage` re-encoding; planning-board/ETA/"today" browser-zone sites (R2B-14 remainder).

Medium and Low findings (§6–§7) are all Wave 2+ unless named in §0.

---

## 1. Executive summary

The dossier/order backbone is in materially better shape than most systems of this size: a single `CreateAsync` use case serves UI, portal, EDI, Excel import and dossier activities; tenant isolation is closed twice (explicit predicates **and** a model-level global filter, verified — no IDOR found on any order/dossier endpoint); every endpoint carries `[RequirePermission]`; status history is interceptor-based and therefore complete; customer- and legal-entity changes are explicit, previewed, transactional and audited; stop address snapshots freeze master data correctly; pricing has a real merge-on-recalculate design with lock/confirm/reopen governance.

The problems are concentrated in **seams between modules** that were built in successive waves without re-closing the older paths:

1. **Two ways to do the same thing, with different rules.** The plain `PUT /transport-orders/{id}` still changes the customer (and legal entity) without any of the safeguards the dedicated change services enforce; pricing endpoints gate on the snapshot state but never on the order state; cancel/delete/status endpoints ignore trip membership while `correct-status` checks it; redelivery orders are built by hand in the incident module and skip cargo, pricing, validation, audit and dossier rules.
2. **Stop identity is not stable.** Every order save regenerates stop ids. Because deletes are soft, the `SetNull` FKs never fire and packages, executions, PODs, scans, ETAs and labels keep pointing at hidden rows. Editing a confirmed order — the most ordinary daily action — silently breaks scanning and labels for that order.
3. **Two time conventions.** The form serialises wall-clock stop times with a `Z` suffix; the detail page and every `formatDateTime` consumer convert them to browser-local time. A planner who types 08:00 reads 10:00 on the same order in summer. Backend planning/ETA decisions also compare UTC instants with local shift/opening hours.
4. **Optimistic concurrency is half-wired.** `Version` is not an EF concurrency token, and most mutation endpoints (status, cancel, delete, priority, all pricing, link/unlink, close/reopen) accept no version at all. Lost updates are silent, and the `Updated` audit row does not record the overwritten fields.
5. **Financial gates are advisory.** Invoice readiness (stale price, missing price, missing POD) is a badge, not a guard; the invoice builder pre-selects everything; a Sent (even Peppol-delivered) invoice can be cancelled without a credit note and the order re-invoiced; PDF, UBL and the accounting export round differently.
6. **A hard 500 on invoiced orders.** `Transitions` has no `Invoiced` key; `GET /api/transport-orders/{id}` on any invoiced order throws `KeyNotFoundException`. No test covers reading an invoiced order.
7. **UI ergonomics of the most-used screens.** The order form has no unsaved-changes guard, a 200-customer `<select>`, hides the manual-price field exactly when the engine asks for one, throws away server validation messages, and the detail page shows up to nine header buttons with `Annuleren` (order) next to `Verwijderen`. The order page is a dead end towards trips, invoices and incidents; notification links for orders point at a route that does not exist.

**Totals after reconciliation: 112 consolidated findings — 6 Critical, 34 High, 47 Medium, 25 Low** (from 156 raw pass-1 findings + 66 raw pass-2 findings, merged where they describe one root cause). Details and the full ranking are in §4–§7; the per-dimension views in §8–§15 slice the same findings by area.

**Production-rollout blockers** (details in §20): the invoiced-order 500 (C-04), stop-id churn breaking scanning/labels (C-01), customer change via plain update (C-02), the time-zone display mismatch (C-03), the invoice cancel-without-credit-note loophole (H-06) and portal document exposure (H-14).

---

## 2. Workflow map (as implemented)

### 2.1 Entities and containment

| Concept | Entity | Key facts (verified) |
|---|---|---|
| Dossier | `TransportDossier` (`Modules/Dossiers/Entities/TransportDossier.cs`) | `Open`/`Closed`; `CustomerId` optional (legacy); `Version` Guid (not an EF token); wrapper dossiers carry `OriginTransportOrderId` (filtered unique). |
| Activity | `DossierActivity` + tenant `ActivityType` (`HasStops`, `SupportsGoods`, …) | Transport-shaped activities reference exactly one order via `LinkedTransportOrderId` (not unique in DB). |
| Order | `TransportOrder` (`Modules/Orders/Entities/TransportOrder.cs`) | Statuses `Draft, Submitted, Confirmed, Planned, InProgress, Completed, Invoiced, Cancelled` (string). `Version` Guid (not an EF token). `InvoiceReadiness` projection (`NotReady/ReadyForInvoice/ReviewRequired` + reason codes). Pricing header fields (`AgreedPrice`, `CalculatedPrice`, `PriceIsManual`, `PricingSource` Contract/OneOff, included-time overrides, diesel override). |
| Stop | `TransportOrderStop` | Sequence, type, `LocationId` **plus** frozen address/contact/opening-hours snapshot; four raw window pairs (Planned/Requested/Confirmed/Earliest–Latest, UTC `DateTime`) plus the commercial `TimeRequirement` (`TimeOnly`, zone-less). |
| Goods | `CargoItem` (source of truth once present; header fields derived) | Id-preserving sync on update; barcode unique per order. |
| Pricing | `TransportOrderPricingSnapshot` (1 per order, `Draft/Reviewed/Locked/Invoiced`, `IsStale`, coverage), `TransportOrderPricingLine` (Auto/AutoAdjusted/Manual/Proposed, `LineKey` merge key, **no unique index**), `TransportOrderServiceLine` (engine-owned, rebuilt each calc) | |
| Link | `DossierOrder` (unique per pair only — an order may sit in N dossiers), `DossierRelation` | |

Every order created without `DossierId` gets an auto-wrap dossier + transport activity + link in the same save (`TransportOrderService.CreateAsync:314-379`). An order created **with** `DossierId` gets only the link — no activity, no customer check.

### 2.2 Order status machine

```
                 manual (POST /status, /bulk-status)          planning cascade (TripService)
Draft ──────────► Confirmed ◄──────────── Submitted           Confirmed ⇄ Planned (trip Draft⇄Planned)
  ▲                 │  ▲                     │ (portal)        Planned/Confirmed → InProgress (trip InProgress)
  └── Confirmed→Draft┘  │                     └──► Draft         InProgress → Completed (trip Completed)
                        │                                       Planned/InProgress → Confirmed (trip Cancelled)
Confirmed/Planned → InProgress → Completed  (manual, NO trip check)
Completed → Invoiced (InvoiceService.CreateAsync, draft invoice)  Invoiced → Completed (line dropped / invoice cancelled or deleted / customer or entity change)
Cancel: Draft, Submitted, Confirmed, Planned, InProgress → Cancelled (reason; NO trip check)
Correct (orders.correct_status, reason, refused on Planned/InProgress trip): Confirmed→Draft, Planned→Confirmed, InProgress→Confirmed, Completed→InProgress, Cancelled→Draft
Delete (soft): Draft, Cancelled (NO trip/package/dossier cleanup)
```

Source: `TransportOrderService.cs:32-63` (maps), `:978-1044` (change), `:1046-1095` (correct), `:1097-1151` (cancel), `:1229-1258` (delete); `TripService.cs:327-336, 697-704, 889-919`; `InvoiceService.cs:235-246, 482-487, 670-686, 1061-1076`. **`Transitions` has no `Invoiced` entry** (see C-04).

### 2.3 Edit gates per status (backend)

| Operation | Allowed when |
|---|---|
| `PUT /transport-orders/{id}` (full update, stops rebuilt) | Draft, Submitted, Confirmed (`:591`) — **no trip check**, customer/entity freely changeable |
| Stop execution plan, priority | not Completed/Invoiced/Cancelled |
| Legal entity (dedicated) | not Cancelled; refused on non-draft invoice; deviation needs `dossiers.override_entity` + reason |
| Customer (dedicated) | any status; refused on non-draft invoice; refused when a dossier owns the order |
| Pricing lines / recalc / confirm line / status / confirm / reopen | gated on **snapshot** status only (Locked/Invoiced refuse); order status ignored; no version token |
| Delete | Draft, Cancelled |

### 2.4 Pricing state machine

`Draft ⇄ Reviewed ⇄ Locked` (confirm = Locked, reopen = Draft, reason + `orders.lock_price`); `Invoiced` only via invoice creation, released to `Locked` when the invoice is cancelled/deleted or a line dropped. Every save of a Draft/Reviewed snapshot re-runs the engine and merges (Auto/Proposed rewritten, AutoAdjusted matched on `LineKey`, Manual kept, orphans → Manual). Locked/Invoiced snapshots refuse a **listed** set of inputs (`PricingInputsChangedAsync:2775-2838`): goods header, cargo lines, services, one-off fields, stop time requirements, whole-order override. **Not listed:** customer, order date (= tariff date), stop address/zone, unloading-stop count, crane/plateau/Moffett/return flags, activity type. `IsStale` is set only by the dedicated customer-change flow; there is no input fingerprint.

### 2.5 Frontend surfaces

| Route | Screen | Notes |
|---|---|---|
| `/dossiers`, `/dossiers/new`, `/dossiers/:id` | list (unpaged), fast-create, detail with header/attention/activities/route/goods/price + drawers | Drawers edit the **first** transport activity's order only; "Openen" on a transport card navigates to the order page |
| `/transport-orders`, `/transport-orders/new[?template=]`, `/transport-orders/:id` | list, create, detail with whole-page edit mode (7-section `TransportOrderForm`) + ~10 dialogs | No TanStack Query; hand-rolled `useState/useEffect`; sibling panels keyed on `orderId` only |
| `/klantportaal/*` | portal list/new/detail | Own DTOs; forced `Submitted`; no edit/cancel endpoint |
| `/planning-center`, `/planning/:id`, `/operations`, `/invoices/new`, `/invoice-control`, `/incidents/:id` | consumers of orders | Order shown as plain text, no links back; order page has no links forward |

Concurrency UX: dossier page and drawers implement 409-with-body + "Herladen" rebase; the standalone order form only shows a message and keeps the stale version.

### 2.6 What is verified solid (do not re-audit)

- Tenant resolution from JWT only, fail-closed; global `TenantId` filter on every `ITenantOwned` entity; all four `IgnoreQueryFilters` sites touching orders carry explicit tenant predicates; no raw SQL / `ExecuteUpdate` on orders (`Data/TransportationDbContext.cs:329-364`, `Tenancy/TenantContextMiddleware.cs:41-93`).
- Every dossier/order/import/portal/planning/invoicing endpoint has `[RequirePermission]`; no `[AllowAnonymous]` in scope; cross-entity ids in request bodies (customer, locations, dossier, entity, activity, order) are validated against the tenant; request DTOs contain no `TenantId/Status/OrderNumber/InvoiceId`.
- Privileged pricing actions are re-checked fail-closed inside the service (override, lock, unlock, confirm-incomplete, entity deviation).
- Order/dossier/invoice numbering is race-safe (concurrency-token counter + retry + filtered unique index).
- Status history interceptor catches every tracked status write (planning, invoicing, portal, corrections).
- Customer/entity change services are transactional with mandatory reasons and old→new audit.
- Migrations match the model (`has-pending-model-changes` clean).

---

## 3. What currently works well

- **One create use case for every channel** (UI, dossier activity, portal, EDI, Excel import) → intake gate, minimal-cargo rule, stop validation, pricing and auto-wrap apply uniformly.
- **Location snapshot on stops** with explicit, audited refresh; **cargo lines as source of truth** with id-preserving sync and derived header.
- **Server-driven affordances** (`allowedTransitions`, `allowedCorrections`, `canCancel`) — the status buttons cannot drift from the backend map.
- **Impact-preview dialogs** for customer and legal-entity change, with blocked reasons and mandatory reason text; dossier-level variants run in one transaction across all linked orders.
- **Pricing governance**: merge-on-recalc keeps manual/adjusted lines, `Proposed` lines for post-execution extras, confirm/reopen with permission + reason + audit, frozen invoice snapshots (seller/customer/VAT/ledger/sales code), credit notes copy from the credited document.
- **Dossier UX foundation**: 4-field fast create, attention panel with section jumps, drawers with dirty guard and 409 rebase, activity cards with capability flags.
- **Client validation** collects every error and jumps to the failing section; busy states disable every mutation button.
- **Documents**: tenant-prefixed storage keys, sanitized names, magic-byte and size checks, content-type derived server-side.
- **Test base**: ~275 real-SQLite service tests with real interceptors for orders/dossiers/pricing; strong coverage of validation, cargo sync, pricing engine/lines, customer/entity change, dossier containment, trip↔order propagation.

---

## 4. Critical findings

### C-01 Editing an order regenerates every stop id; packages, executions, PODs, scans, ETAs and labels keep pointing at soft-deleted stops — scanning and labels break after any edit of a confirmed order
> **Resolution (Wave 1):** Resolved. Stop identity is keyed on ids echoed by the client that belong to this order (`TransportOrderService.PlanStopSyncAsync`/`ApplyStopInput`: update in place; unknown/foreign ids = new; not echoed = soft-deleted). Removing or retyping a stop with executions/POD/real scans/custody events/live-trip ETAs is refused in every status; package pins are released/re-pinned only when no pinned package has a post-generation event. Cargo lines are remapped client-side on stop reorder (`useStopMutation`, shared by form and dossier drawers) and the server refuses re-linking a line whose evidenced packages are pinned elsewhere. Labels fall back for dangling pins; pre-fix dangling pins are repaired by migration `PackageStopPinRepair` where unambiguous. Commits 8d7cb6e, e861ad1, ff3df70.
- Severity: **Critical** · Type: confirmed bug · Sources: B-01, A-03, R2B-23, T-02
- Area: Orders ↔ Packages ↔ Scanning ↔ Operations
- Current behaviour: `UpdateAsync` does `_dbContext.RemoveRange(order.Stops)` and rebuilds via `BuildStops`, which always assigns `Id = Guid.NewGuid()` (`TransportOrderService.cs:726-741`, `:1640-1643`); the echoed client id is used only to carry the snapshot over (`:1717-1722`). Removal is a **soft** delete (`AuditingSaveChangesInterceptor.cs:51-58`), so the `SetNull`/`Cascade` FKs on `Package.LoadingStopId/DeliveryStopId` (`PackageConfigurations.cs:48-55`), `StopExecution`, `ProofOfDelivery`, `ScanEvent`, `StopEta`, `ExecutionException` never fire. Only cargo lines are relinked (`RelinkCargoToReplacedStops:1600`). Packages are generated at confirmation with stop pins (`PackageGenerationService.cs:123-124`); `PackageScanProcessor.cs:261-263, 351-353` compare the pin with the current stop id and raise `WrongStopPackage`. `PackageLabelService.cs:171-201` resolves pins through the soft-delete filter → blank sender/recipient in the frozen label snapshot. `ScanService` tallies per `TransportOrderStopId` (`:534-577`) so pre-edit scans stop counting. The order timeline reads stop history via current stop ids only (`TransportOrderTimelineService.cs:76-99`).
- Why it is a problem: editing a confirmed order (fix a typo, add a reference, change a window) is the most ordinary daily action; afterwards every collo of that order is rejected at loading/delivery, labels reprint blank, and stop execution history disappears from the timeline.
- Real-world scenario: order confirmed Monday (10 labels printed); customer calls Tuesday to move the delivery window; dispatcher saves; Wednesday the driver scans the first pallet → "staat gepland voor een andere laadstop — melding aangemaakt", for every pallet, every stop.
- Likely root cause: wholesale-replace design predates packages/execution; ids regenerated instead of reused; soft delete bypasses EF cascade semantics.
- Recommended solution: keep stop identity — reuse the echoed `Id` for existing stops (update in place like cargo), soft-delete only stops really removed, refuse deleting a stop with packages/executions. Stop-gap: relink packages/executions/ETAs/scans next to `RelinkCargoToReplacedStops`, and fall back to first-loading/last-unloading in the label service.
- Change needed in: backend (frontend already echoes ids)
- Business impact: operational stop of scanning for edited orders, false exceptions, blank labels, lost chain-of-custody evidence.
- Files: `TransportOrderService.UpdateAsync/BuildStops`, `PackageGenerationService`, `PackageScanProcessor`, `PackageLabelService`, `ScanService`, `TransportOrderTimelineService`; `PUT /api/transport-orders/{id}`.
- Tests: confirm → packages generated → update (instructions only) → package pins resolve to live stops; load scan succeeds; label snapshot has addresses; timeline still lists prior stop events; pre-edit scans still counted.

### C-02 The plain order update changes the customer (and legal entity) without any of the customer-change safeguards
> **Resolution (Wave 1):** Resolved. `UpdateAsync` refuses a different `CustomerId` or `LegalEntityId` (400, entity untouched, guards run before any mutation); the dedicated change services remain the only path. FE locks the selects in `mode='edit'` only (template copy stays free). Commits 8d7cb6e, ff3df70.
- Severity: **Critical** · Type: confirmed bug / contradictory rule · Sources: B-02, F-12, P-02, S-01, T-07, B-14
- Area: Orders — customer/entity selection after creation; pricing; dossiers; audit
- Current behaviour: `UpdateTransportOrderRequest.CustomerId` is mandatory and `UpdateAsync` assigns it unconditionally (`TransportOrderService.cs:613-615, 664`), only running the intake gate when it differs. None of `OrderCustomerChangeService.ApplyAsync` (`:96-201`) runs: no reason, no invalidation of Auto/AutoAdjusted lines, no `IsStale`, no draft-invoice release, no dossier-ownership guard (`:274-279`), no legal-entity re-resolution, no `CustomerChanged` audit. A **Locked** snapshot survives because `PricingInputsChangedAsync` (`:2775-2838`) does not compare `CustomerId`. `LegalEntityId` is likewise changed at `:698-724` without the reason/invoice checks of `ChangeLegalEntityAsync` (`:444-472`). The frontend keeps the customer `<select>` enabled in edit mode (`GeneralSection.tsx:484-505`) although the detail page comment says the opposite (`TransportOrderDetailPage.tsx:157-158`). The dossier side refuses exactly this bypass (`DossierService.UpdateAsync:368-381`).
- Why it is a problem: two contradictory rule sets for one commercially sensitive fact; a price confirmed for customer A is invoiced to customer B; dossier and order customers diverge silently; audit shows only a generic `Updated`.
- Real-world scenario: planner "fixes" a wrong customer in the header dropdown and saves; adjusted price lines from A's contract remain, A's default entity remains, the wrapper dossier still says A.
- Likely root cause: the sprint-6 customer-change flow was added next to the older full update without locking the field.
- Recommended solution: backend — refuse `request.CustomerId != order.CustomerId` (and entity changes) in `UpdateAsync` with the same message the dossier uses, or delegate to the change services; add `CustomerId` to `PricingInputsChangedAsync`. Frontend — disable the customer/entity selects on edit and link to "Klant wijzigen…" / "Entiteit wijzigen…".
- Change needed in: both
- Business impact: wrong-customer invoices at wrong tariff, broken dossier/order consistency, audit gap.
- Files: `TransportOrderService.UpdateAsync`, `UpdateTransportOrderRequest`, `GeneralSection.tsx`; `PUT /api/transport-orders/{id}`.
- Tests: `UpdateAsync_WithDifferentCustomer_IsRefused`; Locked snapshot + customer change → refused; FE: customer select disabled in edit mode.

### C-03 Stop times are serialised as wall-clock-with-`Z` and rendered as browser-local time — the same stop shows different hours on different screens
> **Resolution (Wave 1):** Resolved for stop windows; partial elsewhere (see §0). Convention: API stores/serialises UTC instants; `utils/dates.ts` (`toWireDateTime`/`fromWireDateTime`/`formatDateTime`/`formatTime`) converts to/from `TenantSettings.Timezone`, loaded by `DisplayPreferencesProvider` in all three shells (session-keyed cache); portal and kiosk use the same zone; backend `Common/TenantTimeZone` is used by opening-hours warnings, planning proposals, package labels, transport-document day and the weekend/holiday surcharge date; EDI parses `AssumeUniversal|AdjustToUniversal` into `RequestedFrom/To`. Existing rows re-encoded by migration `StopWindowTenantZoneReencoding` (per-tenant zone, EDI-written rows excluded per row, audit-log counters). Wave 2: `dock_appointments` + `DockPlanningPage`, planning-board/ETA/"today" browser-zone sites (R2B-14 remainder). Commits ea5f6b6, e861ad1, ff3df70.
- Severity: **Critical** · Type: confirmed bug · Sources: F-01, U-01, F-17, R2B-30, A-15, R2B-14
- Area: order form ↔ order detail ↔ dossier summary ↔ stop-plan dialog ↔ planning/ETA
- Current behaviour: the form writes `plannedFrom: \`${stop.date}T${time}:00Z\`` (`orderFormPayload.ts:140-157`; `StopExecutionPlanDialog.tsx:16`). The form and dossier summary read back positionally (`orderFormState.ts:766-769`, `DossierRouteSummary.tsx:13-16`) → 08:00. The detail page uses `formatWindow → formatDateTime` (`TransportOrderDetailPage.tsx:70-74`) which does `new Date(value)` + `getHours()` (`utils/dates.ts:66, 94`) → 10:00 in CEST. The portal sends `datetime-local` strings **without** `Z` (`CustomerPortalNewOrderPage.tsx:132-133`) and EDI parses with `DateTime.TryParse` and maps into `PlannedFrom/To` (`EdiService.cs:332-334, 521-525`) — three encodings of the same concept. Backend decisions also mix zones: `PlanningConflictService.cs:511-519` compares UTC trip times with local shift `TimeOnly`s; `PlanningProposalService.cs:762-767` compares UTC `RequestedFrom` with local opening hours; ETA mails print UTC `HH:mm` (`EtaService.cs:402,424,463`); "today" is the UTC day in `OperationsOverviewService.cs:46`, `DriverAppService.cs:79`, `PlanningBoardService.cs:42`; `OrderDate` defaults to the UTC day (`TransportOrderService.cs:270`, `DossierService.cs:181`) and the FE defaults `new Date().toISOString().slice(0,10)`.
- Why it is a problem: dispatchers tell customers the wrong slot; CMR/leveringsbon print the wrong time; conflict/opening-hour warnings are wrong by 1–2 h; night-shift orders get yesterday's date.
- Real-world scenario: planner enters unloading 08:00–10:00; order page shows 10:00–12:00; dispatcher phones the customer with the wrong slot; summer shift 06:00–14:00 vs trip 14:30 local is flagged as an overlap.
- Likely root cause: no tenant time-zone concept; wall clock tagged as UTC; display helpers assume true instants.
- Recommended solution: decide one convention (recommended: true UTC instants computed from a tenant time zone stored in `TenantSettings`), one `toWireDateTime/fromWireDateTime` pair in `utils/dates.ts` used by form, dialog, detail, dossier summary and portal; backend `ITenantClock` for `DateOnly/TimeOnly` conversions and formatting; EDI parse with `AssumeUniversal|AdjustToUniversal` and map to `RequestedFrom/To`.
- Change needed in: both
- Business impact: wrong appointment times, unjustified time surcharges, wrong planning warnings, off-by-one business dates.
- Files: `orderFormPayload.ts`, `orderFormState.ts`, `StopExecutionPlanDialog.tsx`, `TransportOrderDetailPage.tsx`, `DossierRouteSummary.tsx`, `utils/dates.ts`, `CustomerPortalNewOrderPage.tsx`, `EdiService.MapStops`, `PlanningConflictService`, `PlanningProposalService`, `EtaService`, `TransportOrderService.cs:270`.
- Tests: round-trip 08:00 → payload → DTO → detail table → form with `TZ=Europe/Brussels`; dossier summary equals detail; conflict evaluation with summer offset; `TimeProvider` at 23:30 UTC 28 Feb → `OrderDate = 1 Mar`.

### C-04 `Transitions` has no `Invoiced` key — reading or changing any invoiced order throws `KeyNotFoundException` (HTTP 500)
> **Resolution (Wave 1):** Resolved. `Transitions[Invoiced] = []`, `TryGetValue` at both readers, a reflection test pins map totality; `GET` on an invoiced order returns 200 with empty `allowedTransitions`. Commit 8d7cb6e.
- Severity: **Critical** · Type: confirmed bug · Source: B-13 (re-verified)
- Area: Orders status / detail mapping
- Current behaviour: `Transitions` (`TransportOrderService.cs:32-43`) lacks `Invoiced`. `ChangeStatusAsync` indexes `Transitions[order.Status]` at `:989` and `MapDetailAsync` at `:2003`; `GetByIdAsync` (`:194-199`) and every mutation result go through `MapDetailAsync`. `CorrectiveTransitions` correctly uses `TryGetValue` (`:1062, :2005`). No test maps the detail of an `Invoiced` order (only `CorrectStatus_OnInvoicedOrder_IsRefused` exists, `TransportOrderServiceTests.cs:1393`).
- Why it is a problem: `Invoiced` is set the moment a draft invoice is created; from then on the order detail page, portal detail and any status call return 500.
- Real-world scenario: back-office opens an invoiced order from the invoice → error page; portal customer opens a delivered, invoiced order → error.
- Likely root cause: `Invoiced` added for Phase 8 after the map was written.
- Recommended solution: add `[TransportOrderStatus.Invoiced] = []` (and use `TryGetValue` in both sites); add a reflection test that every enum member has a map entry.
- Change needed in: backend
- Business impact: blocked back-office and portal workflow for every invoiced order.
- Files: `TransportOrderService.Transitions/ChangeStatusAsync/MapDetailAsync`; `GET /api/transport-orders/{id}`.
- Tests: invoice an order → `GET /api/transport-orders/{id}` 200 with empty `allowedTransitions`; enum-coverage test.

### C-05 Redelivery orders are built by hand in the incident module: no cargo, no pricing, no validation, no audit, no activity, packages left on the original order, closed dossiers re-used
- Severity: **Critical** (High per finding; Critical combined, and it runs unattended in automatic mode) · Type: confirmed bug / missing business rule · Sources: B-03, A-10, P-11, R2B-04, R2B-05
- Area: Incidents → Orders → Packages → Dossiers → Pricing
- Current behaviour: `IncidentService.CreateRedeliveryAsync` news up a `TransportOrder` (`:762-808`): copies header + address quintet only (no contact/gate/access/opening-hours snapshot, no time requirements), **no `CargoItem`s**, no `ValidateAsync`, no `ApplyPricingAsync` (no snapshot, `AgreedPrice` null), no `DossierActivity`, no `TransportOrder/Created` audit, duplicated number generation (`:820-824`); links into whatever dossier it finds — including **Closed** ones (`:789-806`) bypassing `RequireOpen`; if none found, no dossier at all. Packages are only flipped to `RedeliveryPlanned` (`:810-818`) but keep `TransportOrderId` and stop pins on the original → `PackageScanProcessor.cs:172-181` rejects them on the redelivery trip (`WrongRoutePackage`), `TripPackageService` shows zero packages, departure readiness passes trivially, a second redelivery flips nothing. `FailedDeliveryService.cs:176-181` runs this unattended in `RedeliveryMode = Automatic`.
- Why it is a problem: the redelivery cannot generate packages on confirmation (no cargo), cannot be scanned, has no price (`pricing.none` → and with H-05 invoiceable at €0), is invisible to dossier readiness, and may land in a closed dossier.
- Real-world scenario: failed delivery at 16:00 → automatic redelivery next working day → driver scans the returned pallet at the customer → "hoort niet bij deze rit" High alert per attempt; billing prices it by hand or forgets it.
- Likely root cause: shortcut around `ITransportOrderService.CreateAsync`.
- Recommended solution: build a `CreateTransportOrderRequest` from the original (stops incl. `LocationId`, cargo lines, services, pricing source, `DossierId`) and call `CreateAsync`; add a tenant/customer "redelivery pricing" rule (free / full / fixed surcharge service); move un-delivered packages to the new order (re-pin by sequence/type, stage a custody event) or teach the processor to accept the original's packages; reopen (audited) or relate a closed dossier; block a second redelivery in a chain.
- Change needed in: backend
- Business impact: redeliveries unpriced, unscannable, partially invisible; alert noise; lost revenue.
- Files: `IncidentService.CreateRedeliveryAsync`, `FailedDeliveryService.HandleCoreAsync`, `PackageScanProcessor`, `TripPackageService`; `POST /api/incidents/{id}/redelivery`.
- Tests: failed stop on order with 3 cargo lines + priced snapshot → redelivery has 3 lines, snapshot, activity, dossier link, `Created` audit; confirm yields packages or moved originals scan OK; original in Closed dossier → refused/reopened+audited.

### C-06 `UpdateAsync` flushes a half-applied update mid-flow and has no transaction; audit rows are written in a second save everywhere
- Severity: **Critical** for data integrity (High per source) · Type: confirmed bug · Sources: A-02, A-17, A-12, B-16, R2B-25, R2B-29, R2B-31
- Area: Persistence / transactions / audit atomicity
- Current behaviour: `UpdateAsync` mutates the tracked order (`:640-670`) and, when the entity changes, calls `_auditService.RecordAsync("LegalEntityChanged")` at `:722`; `AuditService.RecordAsync` ends with `SaveChangesAsync` (`AuditService.cs:55`) → header fields + new entity + bumped `Version` are committed before stops (`:730`), cargo (`:743-770`) and pricing (`:775`) are processed; a subsequent `ApplyPricingAsync` failure (locked snapshot, missing override permission) returns 4xx with the DB half-updated and no `Updated` audit. Every other service saves first and audits in a second `SaveChanges` (e.g. `:1014-1017`, `:1123-1126`), so a failure between them leaves an unaudited change. Same two-commit shape: dossier-activity order creation (`DossierActivityService.cs:104-114, 270-284`), portal submit (`CustomerPortalService.cs:228-240`, plus a duplicate planner notification), EDI processing (`EdiService.cs:252-282` — failure after the order commit marks the message Failed and **replay creates a second order**), Excel import (`OrderImportService.cs:310-346` — batch row saved only at the end; retry after disconnect duplicates reference-less rows).
- Why it is a problem: inconsistent orders after a failed save; orders that exist without their audit row; duplicate orders on replay/re-upload.
- Real-world scenario: dispatcher moves an order to another entity and corrects goods while the price is Locked → 400; the entity change is already persisted; retry now 409s (once versions are enforced) or silently diverges.
- Likely root cause: `IAuditService.RecordAsync` performing its own `SaveChanges` on the shared context; orchestration across services without a unit of work.
- Recommended solution: make `RecordAsync` **stage** only (the status-history interceptor already proves the pattern) so the caller's single `SaveChanges` commits both; wrap multi-service flows (`UpdateAsync`, dossier activity + order, portal create+submit, EDI process, import row+batch) in `BeginTransactionAsync` with the execution strategy; EDI: refuse replay when `ResultEntityId` is set; import: insert the batch row first (`Processing`), refuse re-upload while processing.
- Change needed in: backend
- Business impact: data inconsistency, audit gaps, duplicate orders and wrapper dossiers.
- Files: `AuditService`, `TransportOrderService.UpdateAsync:580-830`, `DossierActivityService`, `CustomerPortalService.CreateOrderAsync`, `EdiService.ProcessAsync/ReplayAsync`, `OrderImportService.ImportAsync`.
- Tests: update with entity change + locked pricing → DB unchanged after 4xx; audit failure injection → primary change rolled back; EDI exception after commit → replay refused; import cancelled mid-way → batch row exists, re-upload refused.

---

## 5. High-priority findings

### H-01 `Version` is not an EF concurrency token and most mutation endpoints accept no version — silent lost updates; standalone form has no rebase
- Severity: High · Type: confirmed bug · Sources: A-01, P-06, T-10, F-05, F-10, B-18
- Current behaviour: neither `TransportOrderConfiguration.cs` nor `DossierConfigurations.cs` calls `.IsConcurrencyToken()` (Trip does, `TripConfiguration.cs:27`); the check is an in-memory compare only in `UpdateAsync:600`, `ChangeLegalEntityAsync:428`, `DossierService.RequireVersionAsync:569`. No version on status, correct, cancel, delete, priority, execution plan, all six pricing endpoints, link/unlink, close/reopen, relations. Two overlapping recalculations duplicate Auto lines (no unique `(order, LineKey)` index, `OrderPricingConfigurations.cs:258`) and `AgreedPrice` then doubles on the next line edit. FE: the standalone form shows "herlaad de pagina" on 409, keeps the stale version and ignores the 409 body (`TransportOrderForm.tsx:313-320`) while the dossier drawers rebase correctly; the dossier edit modal closes on 409 and discards typed input (`DossierDetailPage.tsx:246-250`).
- Why: two planners on one order (office + dispatch) is normal; the last write wins with no trace, and the `Updated` audit does not carry the overwritten fields (H-11).
- Scenario: A edits stop 2's window while B edits goods lines; B's save rebuilds stops from B's older form; A's window is gone. Or planner and accountant both press "Herbereken" → 2× Auto lines.
- Root cause: `IVersionedEntity` introduced for the central bump without the EF mapping; version echo wired only for update.
- Solution: `IsConcurrencyToken()` on both entities (+ migration); accept `Version` on every mutating endpoint and set `OriginalValue` before saving; one filter mapping `DbUpdateConcurrencyException` → 409 with current state; filtered unique index on `(TenantId, TransportOrderId, LineKey)`; FE: adopt the 409 body, banner with "Herladen / edits behouden" in the form and keep dossier modals open on conflict.
- Change: both · Impact: lost edits on money-bearing fields, doubled prices.
- Files: `TransportOrderConfiguration`, `DossierConfigurations`, `TransportOrderService` (all mutators), `TransportOrdersController`, `DossiersController`, `TransportOrderForm.tsx`, `TransportOrderDetailPage.tsx`, `DossierDetailPage.tsx`.
- Tests: two contexts update the same order → second throws; `POST /status` with stale version → 409; parallel recalcs → one Auto line per key; FE 409 → banner, "Herladen" replaces order and version.

### H-02 Cancel, delete and manual status changes ignore trip membership; cancelled/deleted orders stay on trips, packages stay live
- Severity: High · Type: missing business rule · Sources: B-04, B-05, S-04, R2B-07, R2B-22, R2B-24
- Current behaviour: `CancelAsync` accepts Planned/InProgress and touches nothing else (`:46-47, :1112`); `TripExecutionService.LoadExecutionStopsAsync:462-475` loads stops of every linked order regardless of status; `Transitions` allow Confirmed→Draft and Confirmed/Planned→InProgress without a trip check (`:989-993`), so a Draft order can ride a Planned trip; `DeleteAsync` (Draft **or Cancelled**) never looks at `TripOrders` → phantom link makes `TripService.ReorderOrdersAsync:350-357` fail forever, counts include it, auto-complete ignores its stops. Packages of cancelled/deleted orders stay scannable and count towards departure readiness (`TripPackageService.cs:116-124`); warehouse receive and dock appointments never consult order state. Only `CorrectStatusAsync:1070-1077` has the trip guard.
- Why: the driver executes cancelled work; trips become un-editable; readiness never completes.
- Scenario: customer cancels at 07:30, trip Planned for 08:00; dispatcher cancels then deletes "to clean up"; driver app still shows the stop; drag-and-drop reorder fails with "De volgorde moet exact de opdrachten van de rit bevatten".
- Root cause: cancel/delete/transition map predate planning; only the correction flow was retrofitted.
- Solution: apply the correction-flow trip guard to cancel, delete, Confirmed→Draft and any manual move into InProgress (or cascade: soft-delete `TripOrder`, cancel unscanned packages, cancel open dock appointments, alert the trip); exclude Cancelled/deleted orders in `LoadExecutionStopsAsync`, readiness and `ReorderOrdersAsync`; `ValidateOrdersAsync` should refuse releasing a trip with non-Confirmed orders.
- Change: backend (FE banner optional) · Impact: wasted trips, wrong PODs, stuck trips, wrong storage billing.
- Files: `TransportOrderService.CancelAsync/DeleteAsync/ChangeStatusAsync`, `TripService`, `TripExecutionService`, `TripPackageService`, `PackageScanProcessor`, `WarehouseScanService`, `DockPlanningService`.
- Tests: cancel order on Planned trip → refused or trip stops exclude it; cancel+delete on Planned trip → refused; trip Planned with Draft order → refused; cancel with packages → packages Cancelled, scan blocked.

### H-03 Dossier ↔ order containment is unguarded: cross-customer links, multi-dossier membership, dangling links after delete, invisible linked-only orders, and the startup backfill re-wraps unlinked orders
- Severity: High · Type: missing business rule / confirmed bug · Sources: B-06, S-10, A-08, T-06, B-17, B-08, B-09, S-07, A-04, R2B-01
- Current behaviour: `CreateAsync(DossierId)` checks existence + Open only (`:239-253`) and creates no activity; `LinkOrderAsync` (`DossierService.cs:631-669`) checks existence + duplicate only — no customer match, no "already in another dossier", no version, no dossier version bump; unique index is per pair (`DossierConfigurations.cs:102-105`); `UnlinkOrderAsync` can leave an order in no dossier. Financials sum `AgreedPrice` of all linked orders per dossier (`:785-793`) → double counting across dossiers. `DeleteAsync` soft-deletes order/stops/cargo only: `DossierOrder` links, `DossierActivity.LinkedTransportOrderId`, pricing rows, service lines, documents (+files), packages remain active (`:1229-1258`); `ListAsync` counts links without joining orders (`:114`). Readiness iterates activities only (`DossierReadinessService.cs:44-66`) so an order linked via "Koppel opdracht" (no activity) or left behind after `DossierActivityService.DeleteAsync:184-208` is never evaluated. `DossierBackfillSeeder` runs on **every boot** (`Program.cs:693`) and treats "no active link and no wrapper" as pre-wave (`:47-52`) → any order ever unlinked (or whose activity was deleted) gets a phantom wrapper dossier — Closed if the order is terminal — after the next deploy.
- Why: the dossier is documented as "the commercial authority for its linked orders"; none of the invariants are enforced at the two entry points; ghost dossiers and wrong counts appear after deploys; dossier-level customer change can move an order of another customer.
- Scenario: user links ORD-0455 (customer B) into DOS-0102 (customer A) → A's total includes B; later "Klant wijzigen" on DOS-0102 reports it "left on other customer". Planner unlinks ORD-0812 to relink elsewhere, gets interrupted; night deploy; DOS-0250 "ORD-0812 — Acme" appears Closed.
- Root cause: link API predates customer-on-dossier and activities; delete predates dossiers; seeder predicate too broad.
- Solution: refuse link/create when customers differ (adopt when dossier has none); one active dossier per order (filtered unique on `TransportOrderId`, move semantics) or explicit `IsPrimary` excluded from financials; `LinkOrderAsync` creates a transport activity; unlink refuses the last link (or re-wraps explicitly); `DeleteAsync` cascades the soft delete (links, activity pointer, pricing, services, documents + files, packages, wrapper dossier); readiness over `DossierOrders ∪ activities`; scope the backfill to a cut-off date / one-shot flag.
- Change: backend (FE picker pre-filters by customer already) · Impact: wrong dossier financials, cross-customer contamination via dossier bulk ops, phantom dossiers, wasted numbers.
- Files: `TransportOrderService.CreateAsync/DeleteAsync`, `DossierService.LinkOrderAsync/UnlinkOrderAsync/ListAsync/BuildFinancialsAsync`, `DossierActivityService.DeleteAsync`, `DossierReadinessService`, `DossierBackfillSeeder`, `DossierConfigurations`.
- Tests: link other-customer order → 400; link already-wrapped order → 400/move; unlink last → refused; delete order → link gone, activity unlinked, wrapper closed/removed, readiness reports `route.order_missing`, financials exclude it; unlink then `SyncAsync` twice → zero new dossiers.

### H-04 Locked price silently survives changes to zone, tariff date, unloading-stop count, equipment flags and activity type
- Severity: High · Type: missing business rule · Sources: P-03, P-16
- Current behaviour: `PricingInputsChangedAsync` (`:2775-2838`) ignores `OrderDate` (tariff date, `PricingEngine.cs:105-166`), stop address/postal code/country (zone resolution `:42-46`; `:2883-2905` compares only time requirements), unloading-stop count (`:2417`), `Crane/Moffett/Plateau/IsReturnMovement` (service conditions `:760-763`) and the dossier activity type (`:2123-2140`, `:2411`); dossier link/unlink never recalculates or flags. `docs/pricing.md:401-403` documents the stop exception as intended, contradicting §13.3 and the code for goods.
- Why: these inputs change the engine result as much as quantities do; the price stays green "Bevestigd".
- Scenario: confirmed Brussels→Antwerp (zone 1) re-routed to Liège (zone 3), crane switched on; invoice goes out at zone-1 price without crane supplement.
- Root cause: guard list built per wave rather than from the engine request contract.
- Solution: derive the guard from `PriceCalculationRequest` (compare all inputs); either refuse like goods or set `IsStale` + drop confirmation on Locked; recalc/flag on activity link changes.
- Change: backend (FE `isStale` banner exists) · Impact: systematic under/over-billing on re-routed confirmed orders.
- Files: `TransportOrderService.PricingInputsChangedAsync`, `DossierActivityService`, `DossierService.Link/UnlinkOrderAsync`.
- Tests: Locked + postal-code / `OrderDate` / `CraneRequired` change → refused or stale; activity link with own tariff → stale/recalculated.

### H-05 Invoice readiness and snooze are advisory only; the invoice builder pre-selects unready orders and fails open on load errors
- Severity: High · Type: missing business rule · Sources: P-01, R2F-08, R2F-22, T-08, R2B-03
- Current behaviour: `InvoiceService.ListUninvoicedOrdersAsync:147-160` / `CreateAsync:237-249` select any Completed order not on a live invoice; `InvoiceReadiness`, `IsStale`, coverage, `pod.missing`, `AgreedPrice == null` (→ €0 base line, `:425`) and `InvoiceSnoozeUntil` are never checked (snooze is applied by one workspace only, `InvoiceControlService.cs:104-128`, and set directly in `InvoicesController.Snooze:83-94` with no status/date/reason rules). FE `NewInvoicePage.tsx:128` ticks every order incl. `ReviewRequired`/`NotReady`; reasons only in a hover `title` (`:347-355`); load failures render "no orders" and "PO not required" (`:130-132, 151-153`); `InvoiceControlPage.tsx:44-46` shows a permanent spinner on error.
- Why: the readiness engine exists precisely to stop wrong invoices, and nothing enforces it at the only point that matters.
- Scenario: dossier customer changed (stale, `AgreedPrice` null) → trip completed → month-end batch → order invoiced at €0 and sent via Peppol.
- Root cause: readiness added as a projection; builder "select all" convenience.
- Solution: `CreateAsync` refuses `pricing.stale`/`AgreedPrice is null`/snoozed orders and requires an explicit, audited acknowledgement for other `ReviewRequired` reasons; move snooze into the order service (Completed only, future date, reason, version, cleared on invoicing) and exclude snoozed orders from the uninvoiced list; FE default-select `ReadyForInvoice` only, inline reasons, confirm dialog listing unready orders, real error states with retry.
- Change: both · Impact: under-billing, wrong-tariff invoices, credit-note churn.
- Files: `InvoiceService.CreateAsync/ListUninvoicedOrdersAsync`, `InvoicesController.Snooze`, `NewInvoicePage.tsx`, `InvoiceControlPage.tsx`.
- Tests: `InvoiceCreate_NonCompletedOrStaleOrder_IsRefused` (Theory); snoozed order absent from list; snooze on Draft → InvalidState; FE default selection + confirm dialog; rejected promise renders error.

### H-06 A Sent (even Peppol-delivered) invoice can be cancelled without a credit note and the orders re-invoiced; credit notes are re-frozen from live data and never touch the order
> **Resolution (Wave 1):** Resolved. `InvoiceService`: an invoice is finalized when `Status ∈ {Sent, Paid}` or any Peppol transmission is past `Queued`; `Sent → Cancelled` removed from the map, cancel/delete refuse finalized documents with the credit-note hint (orders and pricing snapshots stay `Invoiced`); credit notes copy every `*Snapshot` field and `InvoiceLineMirror` keeps mirrored lines out of both Freeze methods (also for legacy lines); zero-line Send refused; duplicate order ids refused; `OrderPricingSnapshotRelease` shared by invoice release and legal-entity change (snapshot back to `Locked`); customer/entity change blocks only on Sent/Paid invoices. Residual (Wave 2): P-05 re-invoice path after a credit note; partial unique index on base lines. Commits 45cec45, ff3df70.
- Severity: High · Type: missing business rule / confirmed bug · Sources: P-04, P-05, R2B-19, R2B-20
- Current behaviour: `Transitions[Sent] = [Paid, Cancelled]` (`InvoiceService.cs:27`), no guard in `ChangeStatusAsync:768-880`; `ReleaseOrdersAsync:1056-1101` returns orders to Completed; only *Queued* Peppol transmissions are cancelled (`:1034-1041`); the Cancelled invoice can then be deleted (`:904`). Credit-note lines carry `TransportOrderId = null` (`:1305`) and copy only code/category/rate, not the `*Snapshot` fields (`:1304-1319`); Send runs `FreezeLedgerSnapshotsAsync`/`FreezeSalesCodeSnapshotsAsync` for every kind (`:846-865`) overwriting VAT rate/category from the **live** sales code (`:1208-1209`); zero-line drafts (produced by design after customer/entity change) can be Sent; `CreateAsync` does no `Distinct()` on order ids (`:239-248`) → an order billed twice; `ChangeLegalEntityAsync` releases draft lines but never resets `pricingSnapshot.Status` from `Invoiced` (`:464-472`) → every pricing endpoint refuses the order afterwards; customer/entity change block on `Status != Draft` which includes `Cancelled` invoices.
- Why: a legally issued number disappears, double billing, credit notes booked on a different account/rate than the original, stranded orders.
- Scenario: accountant fixes a sent invoice via "Annuleren" + re-invoice → customer holds two Peppol invoices; June invoice on 700100/S21, code remapped, August credit note on 700200/exempt.
- Root cause: cancel designed for drafts before Peppol/credit notes; Send path unaware of `Kind`; no shared release helper.
- Solution: block Sent→Cancelled once transmitted (require "Creditnota aanmaken"); on credit-note Sent release the credited orders (`Invoiced → Completed`, snapshot Locked) and record `CreditedByInvoiceId`; copy all snapshot fields and skip both Freeze methods for credit notes; refuse Sent with zero lines; `Distinct()` + partial unique index on base lines; shared `ReleaseOrderFromInvoice`; block only on `Sent|Paid`.
- Change: both (FE relabels/hides cancel) · Impact: double billing, fiscal errors, audit gaps.
- Files: `InvoiceService.ChangeStatusAsync/DeleteAsync/CreateAsync/CreateCreditNoteAsync`, `TransportOrderService.ChangeLegalEntityAsync`, `OrderCustomerChangeService`, `InvoiceDetailPage.tsx`.
- Tests: Sent + Delivered → cancel refused; credit note Sent → order Completed, dossier nets it; remap between invoice and credit note → snapshots kept; duplicate ids → Invalid; entity change on draft-invoiced order → snapshot Locked.

### H-07 Pricing mutations gate on the snapshot only — orders without a snapshot can be re-priced in any status, a manual line overwrites a legacy agreed price, incident charges mutate invoiced orders
- Severity: High · Type: confirmed bug · Sources: B-07, S-05, P-15, R2B-06, P-08
- Current behaviour: `SaveOrderPriceLinesAsync:2992-2997`, `RecalculateOrderPricingAsync:3193-3198`, `ConfirmOrderPriceLineAsync:3458-3463`, `SetOrderPricingStatusAsync` refuse only when a snapshot exists with Locked/Invoiced; `order.Status` (Completed/Invoiced/Cancelled) and invoice lines are never consulted. Snapshot-less orders (pre-engine, redelivery, engine returned null) let `RecomputeLinesTotalAndAgreedPriceAsync:2972-2975` set `AgreedPrice = linesTotal` → adding a €25 line to a legacy €850 order makes it €25, even when Invoiced. `IncidentService.ApplyApprovedChargeAsync:675-719` appends a Manual line and raises `AgreedPrice` on Completed/Invoiced orders without status, version, order audit or override permission — but never when `PriceIsManual` (charge silently unbilled, P-08).
- Why: post-hoc changes to invoiced revenue; silent loss of legacy prices; lost recharges.
- Scenario: back-office adds "wachttijd" to an invoiced legacy order → dossier and KPI revenue show €25; damage recharge approved on a fixed-price order → never invoiced.
- Root cause: pricing lifecycle designed around the snapshot; "no snapshot" branch considered only for readiness.
- Solution: gate all pricing mutations on `order.Status` (refuse Invoiced/Cancelled; Completed needs elevated permission); keep legacy `AgreedPrice` (`legacy + linesTotal`) or refuse manual lines on snapshot-less orders (backfill a snapshot for every order); incident charges on terminal/manual orders become "handmatig toevoegen aan volgende factuur" notes surfaced in invoice control.
- Change: backend · Impact: revenue misstatement, audit disputes.
- Files: `TransportOrderService` pricing methods, `IncidentService.ApplyApprovedChargeAsync`, `InvoiceControlService`.
- Tests: Invoiced order without snapshot + manual line → 400; legacy 850 + 25 → 875 or refusal; manual line on Cancelled → InvalidState; charge on `PriceIsManual` → note + visible in control.

### H-08 Six notification producers link orders to `/orders/{id}` or `/portal/orders/{id}` — routes that do not exist (404)
- Severity: High · Type: confirmed bug · Sources: R2F-20, R2B-32 (re-verified)
- Current behaviour: `TransportOrderService.cs:397, 1039, 1146`, `CustomerPortalService.cs:259`, `CustomerMessageService.cs:152`, `OrderPortalReviewService.cs:142` set `LinkPath` to `/orders/{id}` / `/portal/orders/{id}`; `AppRoutes.tsx` defines `/transport-orders/:id` and `/klantportaal/orders/:id` only; the bell navigates verbatim (`NotificationBell.tsx:80-82`) → `NotFoundPage`.
- Why: the notifications that start portal intake ("opdracht ingediend", "bericht van klant") land on 404.
- Scenario: dispatcher clicks the bell after a portal submission → "Pagina niet gevonden".
- Root cause: route renamed; producers hand-typed; no route-existence test.
- Solution: one `OrderLinks.Detail(id)` helper; redirect routes `/orders/:id` and `/portal/orders/:id` as a safety net; test that every `LinkPath` prefix exists in the route table.
- Change: both · Impact: portal intake unnoticed; trust in notifications.
- Files: listed above, `AppRoutes.tsx`.
- Tests: link-path route existence test; FE redirect test.

### H-09 Server validation messages on order save are thrown away; ~12 backend rules are invisible to the user
- Severity: High · Type: confirmed bug · Sources: F-02, T-21
- Current behaviour: `TransportOrderForm.handleSubmit` maps any 400 to the fixed "controleer de invoer" (`TransportOrderForm.tsx:313-320`); `ApiError.fieldErrors` are discarded. The backend returns specific Dutch messages the client does not validate: blocked/inactive customer, customer-reference-required (`:1318-1332`), confirmed order losing loading/unloading stop (`:622, :1616`), cargo stop-link rules (`:1420-1440`), negative weights (`:1415`), negative included time (`:1353`), override permission (`:2450`), one-off contradictions (`:861/:915`), unknown location. Controller returns bare `{message}` (`TransportOrdersController.cs:354-356`), not ProblemDetails with `errors` (dossier endpoints do). The dossier drawers show `err.message`.
- Why: the user is told to check inputs the form never flags.
- Scenario: customer has "klantreferentie verplicht"; planner saves without one → generic message, no red field.
- Root cause: early generic mapping never revisited.
- Solution: use `localizeApiError` for 400s now; promote customer-reference-required and the confirmed-order stop rule to client validation; longer term return ProblemDetails with `errors` from `TransportOrdersController.Handle` and feed `ValidationSummary.fieldErrors`.
- Change: both · Impact: daily friction, support load.
- Files: `TransportOrderForm.tsx`, `TransportOrdersController.Handle`, `TransportOrderService.ValidateAsync`.
- Tests: `onSubmit` rejects with `ApiError(400, message)` → message rendered; field errors mapped to fields; double click → one submit.

### H-10 Customer pickers are a 200-row `<select>`/pre-loaded list — customer 201+ cannot be chosen; incident/trip/link pickers capped at 100
- Severity: High · Type: confirmed bug (scalability) · Sources: F-07, U-02, R2F-05, R2F-15
- Current behaviour: `searchCustomers({ pageSize: 200 })` feeds a native `<select>` in `GeneralSection.tsx:485-505` (`useOrderFormData.ts:46`), a `SearchableSelect` in `DossierDetailPage.tsx:163` and `NewDossierPage.tsx:40`, and `NewInvoicePage.tsx:68`; `LinkOrderDialog` 100 orders (`DossierLinkDialogs.tsx:868`); incident order picker `pageSize:100` unsearched (`IncidentDetailPage.tsx:189-192`); trip page order picker 100 Confirmed (`TripDetailPage.tsx:221-225`). `CustomerSearchPicker` (server search) already exists.
- Why: blocks order intake for larger tenants; the symptom looks like missing data; redeliveries cannot be linked for older orders.
- Scenario: tenant with 350 customers; "Van Roey NV" cannot be picked; planner creates a duplicate placeholder customer.
- Root cause: lookups reuse list endpoints with fixed page size.
- Solution: reuse `CustomerSearchPicker`/async `SearchableSelect` in the order form, both dossier forms, invoice builder; server-searched order pickers for link/incident/trip.
- Change: frontend · Impact: blocked intake, data duplication.
- Files: `GeneralSection.tsx`, `useOrderFormData.ts`, `DossierDetailPage.tsx`, `NewDossierPage.tsx`, `DossierLinkDialogs.tsx`, `IncidentDetailPage.tsx`, `TripDetailPage.tsx`, `NewInvoicePage.tsx`.
- Tests: 250 mocked customers → the 250th selectable via search.

### H-11 Audit entries are mostly "new-only" or partial; manual price override has no audit action; stop/address/date diffs are never recorded; order timeline hides pricing/document/dossier/trip events; dossiers have no history view
- Severity: High · Type: missing business rule · Sources: A-05, A-06, A-07, R2B-09
- Current behaviour: `Updated` (`:646-654, :789-795`) records ~8 fields; invisible: stop addresses/dates/windows/instructions, `OrderDate`, `CustomerReference`, quantities, ADR/crane/plateau/Moffett/return, priority, notes, one-off fields, `AgreedPrice`/`PriceIsManual`/reason, services. Override (`:2570-2574`) has no dedicated audit. Dossier `Updated`/`ActivityUpdated` pass `old = null`; reorder unaudited; link/unlink never touch the order's audit. `TransportOrderTimelineService` reads `EntityType == "TransportOrder"` only (`:49-52`), labels 4 actions, passes `Detail = null` (`:56`); pricing (7 actions), documents (5), dossier link, trip assignment, incidents are not merged; planning-driven status flips carry no reason/trip id (`TripService.cs:334, 702, 903-913`). Dossier "Historiek" scrolls to notes (`DossierDetailPage.tsx:286-290`).
- Why: "who changed the delivery address / date / agreed price, from what" cannot be answered; disputes and invoicing complaints cannot be reconstructed.
- Scenario: "we ordered Gent, you delivered Antwerpen" → two `Updated` rows by two users, both `StopCount = 2`.
- Root cause: hand-picked anonymous audit objects; read model built incrementally.
- Solution: change-tracker-derived diff (`{property: [old,new]}` + child add/remove) for whitelisted entities at `SaveChanges` (also fixes C-06 atomicity), excluding `AccessCode`; `PriceOverridden` action; `PendingStatusChangeReason = "Rit {number}: …"` in `TripService`; timeline merges `OrderPricing`, documents, dossier link, trip and incident rows and renders `detail`; `GET /api/dossiers/{id}/timeline` + panel (or remove the menu item).
- Change: both · Impact: non-defensible audit trail; support load.
- Files: `TransportOrderService`, `DossierService`, `DossierActivityService`, `TripService`, `TransportOrderTimelineService`, `OrderTimelinePanel.tsx`, `DossierDetailPage.tsx`.
- Tests: stop address change → audit `Stops[1].Address: [A,B]`; override → `PriceOverridden` row; timeline includes `price_confirmed`, `FileAttached`, `OrderLinked`; every audit action has a label.

### H-12 Live price preview ignores crane/plateau/Moffett/return flags and is not sequenced; preview failures look like "nothing entered"; no price feedback outside the Prijs tab
- Severity: High · Type: confirmed bug / UX · Sources: R2F-01, R2F-02, F-11, U-16
- Current behaviour: `useOrderPricePreview` sends only `adrRequired` (`useOrderFormData.ts:192-219`); `PricePreviewInput` has no equipment fields and they are absent from `previewKey` (`:151-160`) although the backend DTO accepts them (`PricingDtos.cs:377-380`) and the save path sends them (`orderFormPayload.ts:1166-1169`). Responses are not versioned/cancelled (`:187-223`). Any preview error → `setPreview(null)` → "Vul klant, aantal en eenheid in…" (`PriceSection.tsx:313-317`). The sticky footer shows no total.
- Why: the quoted preview differs from the saved price; a slow earlier response overwrites the newer; an outage is misread as user omission.
- Scenario: planner ticks "Kraan", preview stays €180, saved price €230 (Kraantoeslag conditioned on Crane).
- Root cause: preview hook not extended when P6 dimensions were added; debounce without cancellation.
- Solution: add the four flags to the preview input and key; request-id/AbortSignal; `{status, error}` state with "Prijs kon niet berekend worden — opnieuw proberen"; "Totaal (voorlopig)" in the sticky bar; contract test preview ≡ save.
- Change: frontend (+ contract test backend) · Impact: wrong quotes, distrust of the preview.
- Files: `useOrderFormData.ts`, `pricingApi.ts`, `PriceSection.tsx`, `TransportOrderForm.tsx`.
- Tests: toggling `craneRequired` changes the request; out-of-order responses → last wins; rejection renders the error, not the placeholder.

### H-13 "Afgesproken prijs" input is hidden exactly when the engine asks for a manual price
- Severity: High · Type: confirmed bug · Source: U-06
- Current behaviour: `PriceSection.tsx:327` condition `(priceIsManual || (!preview && !canOverridePrice) || (!preview && canOverridePrice))` ≡ `priceIsManual || !preview`; when the preview returns `requiresManualPrice` (a preview object exists) the notice says "vul een handmatige prijs in" (`:301-312`) but the field is not rendered unless the user has `orders.override_price` and ticks the override checkbox.
- Why: the screen instructs an action it does not allow; orders saved priceless never become invoice-ready.
- Scenario: new customer without tariffs; planner without override right cannot enter the agreed €450.
- Root cause: condition intended as `priceIsManual || !preview || preview.requiresManualPrice`.
- Solution: show the field for `requiresManualPrice`; keep the reason field for the override case; backend: entering a price when the engine returned nothing is not an override (`OneOff`/legacy semantics, already permission-free).
- Change: frontend · Impact: orders stuck at "Onvolledig".
- Files: `PriceSection.tsx`.
- Tests: preview `requiresManualPrice: true` without override permission → `#to-price` rendered.

### H-14 Every uploaded order document is automatically visible in the customer portal; internal notes and planner-typed reasons are exposed to customers; deactivated customers keep portal access; legacy portal roles may hold `orders.view`
> **Resolution (Wave 1):** Resolved — see §0 for the classification ruling and residuals. Commits d1a0fdf, 902414f.
- Severity: High · Type: security risk · Sources: R2B-27, S-12, R2B-26, S-14
- Current behaviour: `TransportOrderDocument` has no visibility flag (`TransportOrderDocument.cs:22-36`); `PortalDocumentService.cs:71` lists every document of the customer's orders (POD and invoice attachments **are** gated). `PortalOrderDetailDto.Notes` maps `TransportOrder.Notes` — the same field staff edit (`CustomerPortalDtos.cs:48`); `CustomerPortalService.cs:375-378, 410` return every status-history `Reason` and `CancellationReason` the planner typed. `MyCustomerAsync:88-93` ignores `Customer.IsActive`. `DefaultRoleUpgrades.cs:121-124` deliberately leaves the legacy `orders.view` on `klantportaal` roles and there is no identity-class guard, so a customer-linked user with that grant can call `GET /api/transport-orders` and read every customer's orders.
- Why: damage photos, "klant betaalt slecht", "chauffeur ziek, verkeerd ingepland" become customer-visible; cross-customer confidentiality within a tenant.
- Scenario: dispatcher uploads "schadefoto's chauffeur.pdf" → customer downloads it from /klantportaal/documenten; planner cancels with "klant heeft openstaande facturen" → customer reads it.
- Root cause: portal aggregation added without per-document/per-field visibility; non-destructive role upgrades.
- Solution: `CustomerVisible` on documents (default false / true for delivery note & CMR) with an upload toggle; separate `CustomerRemarks` and `CustomerFacingReason`; `IsActive` check in the portal resolver; fail-closed policy denying non-`customer_portal.*` permissions to customer-linked users + one-off upgrade removing internal codes from portal roles.
- Change: both · Impact: data leak to customers, reputational/legal.
- Files: `TransportOrderDocument`, `PortalDocumentService`, `CustomerPortalService`, `RequirePermissionAttribute`/`PermissionAuthorizationService`, `DefaultRoleUpgrades`.
- Tests: `Other` document invisible by default; correction reason absent from portal DTO; inactive customer → 403; customer-linked user with `orders.view` → 403 on internal endpoints.

### H-15 Removing a cargo line after package generation orphans its packages; re-adding the line duplicates them
- Severity: High · Type: confirmed bug · Source: R2B-21
- Current behaviour: cargo removal is a soft delete (`:764`), so `Package.CargoItemId` `SetNull` never fires (`PackageConfigurations.cs:44-47`); `PackageGenerationService.GenerateForOrderAsync:66-78` loads live lines only and looks packages up per live line id; packages of a deleted line stay `IsMandatory` and block trip readiness (`TripPackageService.cs:116-124`); a re-added line has a new id → fresh packages.
- Why: "replace 10 pallets by 8" yields 18 active mandatory packages and double labels; departure gate blocks on phantoms.
- Scenario: order confirmed, 10 labels printed, customer calls "8 pallets", planner edits the line, regenerates → 18 packages.
- Root cause: soft delete + generator ignoring deleted lines.
- Solution: in `GenerateForOrderAsync` also load packages whose `CargoItemId` is not among live lines and cancel unscanned ones; or cancel in `UpdateAsync` when a line is removed.
- Change: backend · Impact: blocked departures, label waste, wrong counts.
- Files: `PackageGenerationService`, `TransportOrderService.UpdateAsync`.
- Tests: generate → delete line → generate → cancelled packages, no duplicates.

### H-16 PDF/DTO totals round ToEven, Peppol UBL rounds AwayFromZero, the accounting export computes VAT per line — three payable amounts for one invoice
- Severity: High · Type: confirmed bug · Sources: R2B-17, R2B-18
- Current behaviour: `InvoiceTotals.cs:26-27` (and `InvoiceService.cs:125-130`, `InvoicePdfService.cs:95-98`) use `Math.Round(x, 2)` (= ToEven); `UblDocumentBuilder.cs:260` uses `AwayFromZero`; `AccountingExportService.cs:115-116` rounds VAT per line while invoice/UBL round per rate group.
- Why: 10.50 × 21 % = 2.205 → 2.20 (PDF) vs 2.21 (UBL); 3 × 10.50 → export 6.60 vs invoice 6.62; payment reconciliation and VAT returns off by cents per invoice.
- Scenario: three service lines of 10.50 → customer's Peppol `PayableAmount` ≠ PDF total.
- Root cause: two rounding helpers; export written independently.
- Solution: one `InvoiceMath.Round2` (AwayFromZero) for totals, list/detail, PDF, UBL, export and dashboards; export one VAT row per (invoice, category, rate) from `InvoiceTotals`.
- Change: backend · Impact: legal-document inconsistency, rejected/late payments, bookkeeping mismatch.
- Files: `InvoiceTotals`, `UblDocumentBuilder`, `InvoicePdfService`, `AccountingExportService`.
- Tests: midpoint fixture parity: PDF total == UBL `PayableAmount` == export VAT sum.

### H-17 EDI: a corrected resend of a Failed message is classified as Duplicate; failure after the order commit + replay creates a second order; times land in `PlannedFrom/To`
- Severity: High · Type: confirmed bug · Sources: R2B-28, R2B-29, R2B-30
- Current behaviour: duplicate predicate excludes only `Status == Duplicate` and matches on hash **or** `ExternalReference` (`EdiService.cs:100-107`) → after any Failed/DeadLettered/stuck-Received message the partner can never resend that id; `ReplayAsync:137-167` reprocesses the broken payload only. `ProcessAsync:252-282` commits the order via `CreateAsync`, then saves Processed + audit; any exception → Failed although `ResultEntityId` is set; replay calls `CreateAsync` again (no per-reference dedupe, unlike import). Stop times parsed with `DateTime.TryParse` (no styles) and mapped positionally to `PlannedFrom/To` (`:332-334, 521-525`).
- Why: EDI unusable after any failure; duplicate orders and wrapper dossiers; partner windows appear as planned windows.
- Scenario: order 4711 fails on a bad location code; partner corrects and resends → "Duplicaat"; operator replays a half-processed message → two orders.
- Root cause: dedupe by reference regardless of outcome; post-commit work outside the unit of work; positional DTO mapping.
- Solution: exclude Failed/DeadLettered/Received from the predicate; same reference + different hash = amendment; Processed + audit in the same save/transaction; refuse replay when `ResultEntityId` set; parse `AssumeUniversal|AdjustToUniversal`; map to `RequestedFrom/To` by name.
- Change: backend · Impact: partner integration reliability, duplicate orders, wrong planning inputs.
- Files: `EdiService.IngestAsync/ProcessAsync/ReplayAsync/MapStops`.
- Tests: Failed then corrected resend → processed; exception after commit → replay refused; payload with offset → `RequestedFrom` set, `PlannedFrom` null.

### H-18 ETA GET has write side effects and customer e-mails with no driver scoping or trip-status gate; ETA override never checks stop ∈ trip
- Severity: High · Type: security risk · Sources: R2B-12, R2B-13
- Current behaviour: `GET /api/trips/{tripId}/eta` allows `DriverWorkflowView` (`EtaController.cs:22-23`) and calls `RecalculateTripAsync`, which persists `StopEta` + history and queues customer e-mails with a minute-granular dedupe key (`EtaService.cs:177-184, 357-391, 427`); no `trip.DriverId == current driver` restriction (contrast `TripExecutionController.cs:43-45`), no `trip.Status` check. `OverrideStopEtaAsync:219-253` loads the stop by id + tenant, inserts `tripId` verbatim (FK accepts another tenant's trip id), stages notifications before the null-trip failure.
- Why: any driver reads colleagues' trips; a polling app on a cancelled trip mails the customer every minute; cross-tenant reference rows.
- Scenario: driver tablet polls `/eta` every 30 s on a trip that was cancelled and re-planned → customer receives "nieuwe verwachte aankomst" mails continuously.
- Root cause: GET doubles as recalculation trigger; scoping copied from planning, not from the driver guard.
- Solution: drivers must own the trip (`LoadGuardedAsync`); short-circuit for `Status ∉ {Planned, InProgress}`; persistence/messaging only at transition points; membership check in override.
- Change: backend · Impact: customer spam, data exposure across drivers.
- Files: `EtaController`, `EtaService.RecalculateTripAsync/ReportDelayAsync/OverrideStopEtaAsync`.
- Tests: driver B reads/reports on A's trip → 403; GET on Cancelled trip → no rows/mails; stop of trip A with tripId B → 404, nothing staged.

### H-19 Generated CMR fills box 1 with the carrier's legal entity and box 2 with the paying customer, and prints internal order notes
- Severity: High · Type: confirmed bug · Source: R2B-35
- Current behaviour: `TransportDocumentService` snapshots `Seller = LegalEntity`, `Customer = order.Customer`; `TransportDocumentRenderer.cs:109` labels them "1. Afzender / Expéditeur" and "2. Geadresseerde / Destinataire"; no carrier box; `order.Notes` rendered as "Opmerkingen" (`TransportDocumentService.cs:858`).
- Why: on a CMR box 1 = consignor (loading party), box 2 = consignee (unloading party), box 16 = carrier; every CMR for a customer shipping to its own customers names the wrong receiver and leaks internal notes.
- Scenario: customer ships to its customers; every CMR names the wrong receiver.
- Root cause: delivery-note layout reused for CMR.
- Solution: consignor/consignee from first loading / last unloading stop, add carrier block, print stop `Instructions`/`Reference` instead of `Notes`.
- Change: backend · Impact: legally incorrect transport documents.
- Files: `TransportDocumentService`, `TransportDocumentRenderer`.
- Tests: CMR snapshot parties derived from stops; notes absent.

### H-20 The order form has no dirty tracking or unsaved-changes guard; Enter in any text field submits the entire 7-section form
- Severity: High · Type: UX issue · Sources: F-04, U-04, R2F-10, R2F-12
- Current behaviour: no `UnsavedChangesGuard` in `features/transport-orders/**` (drawers have one, `SectionDrawer.tsx:50`); Cancel is `setEditing(false)` with no confirmation (`TransportOrderDetailPage.tsx:747`); breadcrumb/sidebar/Ctrl+K navigate away silently. `<form onSubmit>` at `TransportOrderForm.tsx:457` has no Enter guard → on the new-order page Enter in a stop reference creates the order (city required only when no location). Conversely reason/price dialogs have inputs outside any `<form>` so Enter does nothing. Edit mode/drawers/dialogs are not in the URL; a refresh mid-edit drops everything.
- Why: the longest form in the system can be lost with one click; accidental half-filled drafts.
- Scenario: planner fills 4 stops and 6 goods lines, gets a call, clicks "Planning" in the sidebar — gone.
- Root cause: form predates the shared guard; state spread over ~50 hooks.
- Solution: `dirty = buildSubmitPayload(values) !== initialPayload` (memoised) → `<UnsavedChangesGuard when={dirty && !saving} />` + confirm on Cancel; ignore Enter on single-line inputs inside `SectionedForm` (or Ctrl+Enter); wrap dialog bodies in `<form>`; accept `?section=`/`?edit=1` on mount.
- Change: frontend · Impact: lost work, draft pollution, keyboard efficiency.
- Files: `TransportOrderForm.tsx`, `NewTransportOrderPage.tsx`, `TransportOrderDetailPage.tsx`.
- Tests: dirty + navigate → confirm dialog; Enter in stop reference does not submit; Enter in reason dialog submits.

### H-21 The order page is a dead end towards trips, invoices and incidents; consumers show order numbers as plain text; Planned orders offer no "why can't I edit / unplan first"
- Severity: High · Type: UX issue · Sources: R2F-03, R2F-14, R2F-04, R2F-05, U-12, R2F-11
- Current behaviour: `TransportOrderDetailDto` carries only `DossierId/DossierNumber` (`TransportOrderDtos.cs:134-135`) — no trip, invoice or incident links; the page has no incident section or "Incident melden"; `editable` requires Draft/Confirmed and is simply absent otherwise (`:506-507`); "Venster" edits times only. Reverse: `TripDetailPage.tsx:553`, `TripInspector.tsx:275-303`, `IncidentChargePanel.tsx:148-151` render order numbers as text; unplanned cards show no requested date and cannot open the order (`UnplannedPanel.tsx:136-163`). Dossier "Openen" navigates to the order page whose back/breadcrumb return to the classic list (`:550-551`); the dossier's cached first order refreshes only on `dossier.version`.
- Why: "where is my truck / was it invoiced / was there a redelivery" — the core questions on a customer call — cannot be answered from the order; address changes after planning require knowing a hidden rule and another screen.
- Scenario: customer calls about TO-2026-0412; planner sees "Gepland" and nothing else; customer moves the delivery the evening before; planner sees no Bewerken and assumes it is forbidden.
- Root cause: order DTO predates trips/invoices/incidents; links added on the other entities only.
- Solution: extend the DTO with `trip {id, number, date, driver, status}`, `invoices[]`, `incidents[]`, `originIncidentId`; a "Uitvoering & facturatie" card; disabled "Bewerken" with reason + "Haal van rit …"; links in TripInspector/TripDetail/IncidentChargePanel; date + "Openen" on unplanned cards; "Incident melden" with `?transportOrderId=`; `?from=dossier:<id>` breadcrumb and stale-cache refresh.
- Change: both · Impact: time per customer call, wrong-order edits, missed incidents.
- Files: `TransportOrderDtos.cs`, `TransportOrderService.MapDetailAsync`, `TransportOrderDetailPage.tsx`, `TripInspector.tsx`, `TripDetailPage.tsx`, `IncidentChargePanel.tsx`, `UnplannedPanel.tsx`, `IncidentDetailPage.tsx`, `DossierDetailPage.tsx`.
- Tests: DTO projection tests; render tests for the card/links/disabled state; breadcrumb origin test.

### H-22 Order detail header shows up to nine flat action buttons with several primaries; "Annuleren" (order status) sits next to "Verwijderen"
- Severity: High · Type: UX issue · Sources: U-07, U-19
- Current behaviour: `TransportOrderDetailPage.tsx:555-616` renders Leveringsbon, CMR, Bewerken, Verwijderen (danger), one primary per `allowedTransitions`, Annuleren, Status corrigeren, Gebruik als sjabloon; Bewerken/Verwijderen repeat at the bottom with another variant (`:1121-1132`); the cancel dialog dismiss says "Terug" (`:1144`) while every other dialog says "Annuleren".
- Why: no hierarchy; a destructive status action shares its label with the universal dismiss verb.
- Scenario: dispatcher wants to confirm, hits "Annuleren" thinking it dismisses something.
- Root cause: each wave appended a button.
- Solution: one primary (next transition), secondary "Bewerken", "Documenten ▾" split, "Meer ▾" menu (dossier header pattern) for Opdracht annuleren / Status corrigeren / Kopiëren / Verwijderen; remove the bottom bar; label "Opdracht annuleren".
- Change: frontend · Impact: fewer mis-clicks on destructive actions.
- Files: `TransportOrderDetailPage.tsx`, `transport-orders.css`.
- Tests: exactly one primary per status; destructive actions in the menu.

### H-23 Trip auto-completion from the driver app is fire-and-forget; a `Stale`/`InvalidState` result leaves trip and orders InProgress with no retry
- Severity: High · Type: confirmed bug · Source: R2B-11
- Current behaviour: `TripExecutionService.cs:398-403` calls `_tripService.ChangeStatusAsync(Completed)` after two saves and discards the result; the driver gets 200; every stop is terminal so nothing re-triggers it.
- Why: orders never reach Completed → readiness never evaluated → invoicing blocked; no driver-side "complete trip" action.
- Scenario: planner edits the trip (Version bump) while the driver completes the last stop → `Stale` swallowed → trip stuck.
- Root cause: business-critical transition treated as side effect.
- Solution: surface the result (409/422 or "completion pending" flag), or one transaction for stop transition + trip completion; explicit "Rit afsluiten" driver action.
- Change: both · Impact: stuck trips, delayed invoicing.
- Files: `TripExecutionService.TransitionStopAsync`, `TripService.ChangeStatusAsync`, driver app.
- Tests: stop completion with stale trip version → response indicates trip not completed.

### H-24 Order detail page, PriceSection, SummarySection, document-strategy/timeline/portal-review panels are hard-coded Dutch although complete nl/fr/en keys exist
- Severity: High (Medium for NL-only tenants) · Type: confirmed bug · Sources: U-03, F-16
- Current behaviour: literals throughout `TransportOrderDetailPage.tsx` (214…1573), `PriceSection.tsx`, `SummarySection.tsx`, `OrderDocumentStrategyPanel.tsx`, `OrderTimelinePanel.tsx`, `OrderPortalReviewPanel.tsx`; `locales/*/transportOrders.json:360-407, 441-644` already contain the keys; labels drifted ("Gebruik als sjabloon" vs "Kopiëren naar nieuwe opdracht"); two hard-coded time-requirement badge maps (`TransportOrderDetailPage.tsx:82-95` vs `orderFormState.ts:730`).
- Why: FR/EN users see Dutch on the most important screen; drift between screens.
- Solution: wire `t()`; extend the missing-keys test with a "no hard-coded Dutch in these files" lint; reuse `timeRequirementBadge`.
- Change: frontend · Impact: multilingual promise; consistency.
- Tests: render with locale `fr` → no Dutch fixture strings.

### H-25 Sibling panels on the order page go stale after actions (timeline, packages, document strategy, messages)
- Severity: High · Type: confirmed bug · Source: F-03
- Current behaviour: every mutation does `setOrder(updated)`; `OrderTimelinePanel:668-680`, `OrderPackagesPanel:82-91`, `OrderDocumentStrategyPanel:37-49` fetch once per `orderId`; confirming generates packages server-side (`TransportOrdersController.cs:173-178`) but the panel still says no colli; the historiek does not show the action just performed.
- Why: users refresh the page to trust what they see; "it didn't work" retries.
- Solution: `key={order.version}`/refresh token on the panels; structurally, TanStack Query with `['order', id, …]` keys invalidated by prefix.
- Change: frontend · Files: `TransportOrderDetailPage.tsx:221-232, 1057-1112`, panels.
- Tests: after `changeTransportOrderStatus` resolves, timeline fetched again.

### H-26 No stop chronology / route-shape validation; opening hours advisory only; ADR header not derived from lines; negative header quantities silently clamped
- Severity: High (Medium each) · Type: missing business rule · Sources: B-10, T-01, B-15
- Current behaviour: `ValidateAsync:1336-1382` validates each stop in isolation; no rule that loading precedes unloading in sequence or time, that unloading date ≥ loading date, or that ≥1 stop exists on create; `ConfirmationError:1616-1624` only requires one of each type; cargo index check only when both indexes given (`:1440-1443`); opening hours only warn on `PlannedFrom/To` (`:1826-1872`), never on `TimeRequirement`, never block. FE `validateOrderForm` (`orderFormState.ts:997-1079`) has no cross-stop rule; `stops = []` produces no error. `DeriveSummaryFromCargo:1477-1526` derives everything but `AdrRequired` (portal derives it client-side, EDI/import don't); `AdrDetails` never required; header negatives clamped to 0 (`:274-281, :668-675, :3555`) while line negatives are rejected.
- Why: nonsense routes reach planning and pricing (first loading/last unloading, `:2317-2326`); ADR line with header false → no ADR surcharge, unqualified driver; `-12` silently becomes `0`.
- Scenario: Excel import with swapped dates confirmed by bulk; EDI partner sends an ADR line, header stays false.
- Solution: order-level rule (monotonic `PlannedFrom/RequestedFrom` by sequence, first stop Loading, last Unloading, ≥1 stop), mirrored client-side with field paths; `AdrRequired = header || lines.Any(ADR)`; require `AdrDetails` when ADR; reject negatives; block `TimeRequirement` windows entirely outside opening hours at Confirm.
- Change: both · Impact: planning errors, safety/compliance, lost ADR surcharges.
- Files: `TransportOrderService.ValidateAsync/ConfirmationError/DeriveSummaryFromCargo`, `orderFormState.ts`.
- Tests: unloading before loading → 400; `[Unloading, Loading]` → 400 on confirm; no stops → 400; header ADR false + ADR line → `adrRequired` true; negative weight → 400; FE twin tests.

### H-27 Execution actuals never trigger re-pricing; POD can be finalised on unexecuted stops and satisfies readiness from any trip; the commercial `TimeRequirement` is ignored outside Orders
- Severity: High (Medium each) · Type: missing business rule · Sources: P-07, R2B-15
- Current behaviour: actual minutes are read only inside `ApplyPricingAsync` (`:2325, :3490-3535`) → `Proposed` extra-time lines appear only after someone presses "Herbereken"; trip completion / POD finalize only re-evaluate readiness (`TripService.cs:914-919`); no `pricing.execution_review` reason. `PodService.cs:73-94` checks tenant/driver/stop∈trip only; `InvoiceReadinessEvaluator.cs:84-89` matches PODs by order regardless of trip; `TimeRequirement/From/To` have no reference outside `Modules/Orders`; late detection uses `LatestAllowed ?? ConfirmedTo ?? PlannedTo` (`TripExecutionService.cs:307`).
- Why: the most common post-execution revenue (waiting time) is systematically missed; a POD from a cancelled trip satisfies a re-run; "leveren vóór 10:00" (surcharged) delivered at 15:00 raises nothing.
- Solution: on trip completion/POD finalize auto-recalculate Draft/Reviewed snapshots (proposals only) or set a blocking readiness reason; "herberekening aanbevolen" flag on Locked; POD requires `trip.Status == InProgress` and an Arrived/Unloading/Completed execution; readiness scoped to completed trips; derive an effective bound from `TimeRequirementTo` on the planned date (tenant zone) in execution, ETA and alerts.
- Change: both · Impact: recurring under-billing, unenforced commercial promises.
- Files: `TripService`, `PodService`, `InvoiceReadinessEvaluator`, `TripExecutionService`, `EtaService`, `InvoiceControlService`.
- Tests: dwell > included → readiness ReviewRequired + Proposed line; POD from cancelled trip does not satisfy readiness; Before-10:00 requirement drives late detection.

### H-28 Portal review "Reject" cancels without `orders.cancel`; "Accept", corrections, cancellations, planning and invoicing flips emit no EDI feedback and generate no packages (side effects live in one controller)
- Severity: High (Medium each) · Type: security risk / design · Sources: S-03, R2B-10, A-11, S-02
- Current behaviour: EDI feedback and package generation live in `TransportOrdersController.ChangeStatus/BulkChangeStatus` (`:168-178, :203-208`) so `OrderPortalReviewService` (`:83-94`), `CancelAsync`, `CorrectStatusAsync`, `TripService`, `InvoiceService`, `OrderCustomerChangeService` skip them; Reject → `CancelAsync` under `orders.change_status` only; dossier activity `create-order` creates orders with `dossiers.manage` only (no `orders.create`, `DossiersController.cs:116-146`). Bulk loops per item without a batch marker; generation failure after commit leaves a Confirmed order without packages and no audit.
- Why: partner's last known state stays "Confirmed" after cancellation; portal-accepted orders have no packages; role model bypass.
- Solution: emit EDI feedback + package generation from a single post-commit hook keyed on the status-history row (or domain event) with the reason; check `orders.cancel` in the Reject branch and `orders.create` on activity order creation; batch id in bulk audits; make generation idempotent + audit failures.
- Change: backend · Files: `TransportOrdersController`, `OrderPortalReviewService`, `DossiersController`, `EdiService.QueueOutboundStatusAsync`, `PackageGenerationService`.
- Tests: cancel EDI order → outbound "Cancelled" row; portal accept → packages; Reject without `orders.cancel` → 403; `create-order` without `orders.create` → 403.

### H-29 Test harness proves neither the tenant filter nor any HTTP-level permission/409 contract for orders/dossiers; failed-delivery → order status is untested; no controller tests exist
- Severity: High · Type: maintainability/design concern · Sources: T-14, T-15, T-18, T-27, T-03, T-28
- Current behaviour: every order/dossier test builds `SqliteTestDbContext()` without a tenant id (filter off, `SqliteTestDbContext.cs:23-45`); isolation is proven only via the services' own `Where`; the only real foreign-tenant order test is `InvoiceLegalEntityCoherenceTests.cs:161-182`; `Phase10SystematicSecurityTests` proves attributes exist, not which code or a runtime 403; no `WebApplicationFactory` (no public entry point, `AuthRateLimitingTests.cs:17`); `StopExecutionTransitionTests.cs:301-313` and `TripExecutionServiceTests.cs:145-158` assert trip status only after Failed stops (order silently becomes Completed) and build the SUT without `FailedDeliveryService`; SQLite hides FK/cascade/Postgres semantics (`PRAGMA foreign_keys` off, `EnsureCreated`).
- Why: a service that forgets `TenantId ==`, a wrong permission code, a 400-instead-of-409 regression or a failed delivery invoiced as Completed all pass the suite.
- Solution: `CrossTenantFixture` with two real tenants + Theory over every order/dossier mutation; H1-filter tests via `CreateContextForTenant` for orders/dossiers/lines/links; `public partial class Program {}` + `TmsApiFactory` with permission matrix and 409-body tests; wire `FailedDeliveryService` into the execution SUT and assert order status/readiness; `PRAGMA foreign_keys=ON` + a Postgres smoke lifecycle + migrations-vs-model test.
- Change: backend tests · Impact: defence in depth unverified.
- Tests: see §16 top-15.

### H-30 Frontend permission gating ignores the `orders.manage` umbrella; several buttons are gated on the wrong permission or hidden although the API allows the action
- Severity: Medium-High → High for role integrity · Type: missing business rule · Sources: F-06, S-15, F-13
- Current behaviour: `hasPermission('orders.edit')` / `'orders.delete'` (single lookup, `AuthContext.tsx:77`) at `TransportOrderDetailPage.tsx:507, 509, 632, 651` and `TransportOrderForm.tsx:65-66`, while every endpoint accepts `orders.manage`; "Voorstel bevestigen" gated on `override_price` (`:926-931`) but the endpoint needs `orders.edit` (`Controller:325`); "Bewerken" hidden for `Submitted` although the backend allows it (`:591`) — the planner must move the order to Draft (customer-visible churn) to fix a postal code; delete-document shown to `orders.create`-only users → 403; no route-level guards.
- Solution: `can(code)` helper expanding module umbrellas or `hasAnyPermission([code,'orders.manage'])` consistently; gate line-confirm on `orders.edit|manage`; include `Submitted` in `editable`; expose `allowedActions` on the DTO like `allowedTransitions`.
- Change: frontend (+ DTO) · Tests: user with only `orders.manage` sees Bewerken/Verwijderen/Klant wijzigen; Submitted + `orders.edit` shows Bewerken.

### H-31 Template ("Kopiëren") mode carries the source order's manual price, override reason, entity, diesel override, one-off pricing, ids and version into the new order
- Severity: High · Type: UX issue / confirmed bug · Sources: R2B-38, F-18
- Current behaviour: `NewTransportOrderPage.tsx:20-27, 69-71` passes the full detail; `TransportOrderForm.tsx:78-139` seeds `agreedPrice`, `legalEntityId`, `priceIsManual`; `orderFormPayload.ts:82-127` sends override/diesel/one-off/version/stop ids; backend honours `priceIsManual` on create (`:2569-2575`).
- Why: copying last year's invoiced order yields a draft priced at last year's override with last year's reason, skipping today's tariff.
- Solution: blank price/override/diesel/one-off/version/ids/customer reference in template mode; re-resolve the entity from the customer default.
- Change: frontend · Tests: template payload has no override fields or ids.

### H-32 Duplicate `TransportOrderStatusHistory` rows on number-claim retries; no DB uniqueness on `TripOrder` (one-active-trip is a check-then-insert race)
- Severity: High (Medium each) · Type: confirmed bug · Sources: R2B-02, R2B-08
- Current behaviour: `InvoiceService.CreateAsync` sets `Invoiced` then saves inside `InvoiceNumberService.ClaimAsync` (`:48-80`), which retries `SaveChangesAsync` on `DbUpdateConcurrencyException` **and** any `DbUpdateException` without clearing the tracker; `OrderStatusHistoryInterceptor` adds a row per attempt (rows from the failed attempt stay `Added`). `TripOrder` has only non-unique indexes (`TripConfiguration.cs:57-58`); `ValidateOrdersAsync` reads `AsNoTracking` (`TripService.cs:979-985`), insert later, no transaction.
- Why: immutable history polluted at month-end; two dispatchers can put one order on two trips → both drivers see it, contradictory status.
- Solution: interceptor skips when an `Added` row for (order, From, To) already exists; narrow the retry catch; partial unique index on active `trip_orders(TransportOrderId)` + `DbUpdateException` → 409.
- Change: backend · Tests: forced concurrency exception → one history row; concurrent assign → exactly one succeeds.

### H-33 Task/expiry sweeps share one `DbContext` across tenants; GDPR anonymisation is non-transactional and never reaches order-side PII
- Severity: High (Medium each; cross-tenant side effect) · Type: confirmed bug · Sources: R2B-33, R2B-34
- Current behaviour: `TaskSweepService.cs:104-157` iterates tenants on one scoped worker; per-tenant exceptions leave staged rows that the next tenant's save flushes; `ExpiryNotificationHostedService.cs:662-666` has no per-tenant try/catch. `DataSubjectService.cs:157-196` hard-deletes and removes files before the identity save at `:221` (no transaction); scope is employee-only — `ProofOfDelivery.RecipientName`, execution driver names, message/notification bodies untouched.
- Solution: one scope per tenant or `ChangeTracker.Clear()` in the catch; wrap anonymisation in a transaction, delete files after commit, extend the field list.
- Change: backend · Tests: tenant A throws → B processed, no A rows; failure at final save → nothing deleted.

### H-34 Binary downloads (CMR, leveringsbon, documents, invoice PDF) bypass the API client's 401 refresh; dossier/incident pages can render record A under URL B
- Severity: Medium-High (daily) · Type: confirmed bug · Sources: R2F-24, R2F-21
- Current behaviour: `transportDocumentsApi.ts:40-41`, `orderDocumentsApi.ts:52,68`, `invoicesApi.ts:167`, `invoiceAttachmentsApi.ts:50,75`, `customerPortalApi.ts:412` call `fetch` directly with the raw access token; only `apiClient.ts:89-105` refreshes → first "print CMR" after a break fails with a generic toast. `DossierDetailPage.tsx:118-136` and `IncidentDetailPage.tsx:161-174` `load()` have no id/mounted guard (the order page does, `:200-218`) → slower response wins, subsequent saves send the wrong `version`/id pair.
- Solution: `apiClient.fetchBlob` reusing the refresh path; guard loads with `{id, data}` keying or AbortController.
- Change: frontend · Tests: 401-then-refresh for blob helper; out-of-order resolution test.

---

## 6. Medium-priority findings

Each entry: **ID · title** — sources · type · current behaviour (file:line) · why/scenario · solution · change in · tests.

- **M-01 Dossier route/goods/price sections show only the first linked order; mixed navigation models on the dossier page** — F-15, U-11, U-12 · UX/missing rule · `DossierDetailPage.tsx:139-141, 189-195, 355-404`, `DossierPriceSummary.tsx:19-53` · multi-leg dossiers hide the second order; "Route bewerken" edits the wrong order without saying which; "Openen" navigates while other cards open drawers · one Route/Goederen block per order-backed activity with the order number in the heading (or a switcher); same open behaviour for all cards; back-to-dossier breadcrumb · frontend · dossier with two transport activities renders two route blocks.
- **M-02 Dossier close/reopen is decoupled from the order lifecycle; closed dossiers do not block order work or planning; wrapper dossiers never close** — B-12, R2B-16 · missing rule · `DossierService.CloseAsync:575-606` checks open incidents only; no dossier check in `TransportOrderService` except create; `TripService.ValidateOrdersAsync` never looks at dossiers; only the backfill sets Closed · thousands of "open" wrapper dossiers after a year; closed dossiers get planned · refuse Close with non-terminal orders; auto-close wrappers on terminal status; refuse edit/plan when every containing dossier is Closed (or auto-reopen audited); trip-state dimension in readiness · backend · close with InProgress order → 400; order Completed → wrapper Closed.
- **M-03 Dossier financial summary counts cancelled orders, draft/cancelled invoices and ignores credit notes; profitability/dashboard count Draft invoices** — P-09, R2B-19 · confirmed bug · `DossierService.BuildFinancialsAsync:785-793`, `DossierPriceSummary.tsx:23-42`, `ProfitabilityQueryService.cs:143-201`, `DashboardService.cs:79,97` · header figure used as commercial total is inflated · exclude Cancelled orders, restrict to Sent/Paid invoices of `Kind = Invoice`, subtract credit notes via `CreditedInvoiceId`; grey out cancelled orders · both · cancelled excluded; draft invoice excluded; credit note netted.
- **M-04 Invoice lines are built from engine service amounts, not the edited price lines (base line absorbs the difference, can go negative); draft-invoice line edits and dropped lines diverge from the order without trace** — P-13, P-14, P-10 · confirmed bug · `InvoiceService.cs:425-455, 591-704, 658-681` · zeroed crane line still invoiced at €250 with "Transport €-250"; accountant rounds a line down, order still says confirmed price; dropping one line releases the whole order (and strands it: surviving lines keep excluding it from the uninvoiced list) · generate invoice lines from non-informational pricing lines; require a reason + `InvoicedDeviation` on order-backed line edits; release only when no kept line remains · backend (+FE) · zeroed service line not invoiced; drop one of two lines → order stays Invoiced.
- **M-05 Any save or recalculation by a user without `orders.override_price` fails once an order carries a manual override** — P-12 · UX/rule · `ApplyPricingAsync:2441-2458`, `RecalculateOrderPricingAsync:3211-3212`, FE echoes `priceIsManual` · dispatcher adding a stop instruction gets "geen rechten om de prijs te overschrijven" and unticks the override · require the permission only when the override is set/changed · backend · non-privileged update keeping override succeeds.
- **M-06 Bulk status dropdown offers statuses the backend can never apply; ids beyond 100 silently dropped; first error only** — F-08, U-27, T-11 · missing rule · `TransportOrdersPage.tsx:570-581`, `TransportOrdersController.cs:188-223` (`Take(100)`) · "Geannuleerd" on 15 orders → 15 failures, one error line; 120 selected → 20 skipped · restrict targets to transition-map values (or per-selection intersection); per-row failure modal; move loop into the service returning skipped count · both · dropdown excludes Cancelled/Planned/Invoiced/Submitted; >100 reported.
- **M-07 Deactivated locations selectable; blocked/inactive customers reachable via "Klant wijzigen"; inactive service options silently dropped; customer delete not blocked by orders/dossiers** — B-11, T-12, A-09 · missing rule · `ValidateAsync:1384-1394` (existence only), `OrderCustomerChangeService.LoadAsync:224-230`, `DossierCustomerChangeService.LoadAsync:129-137`, `PricingEngine.cs:700-703`, `CustomerService.DeleteAsync:469-480` (location delete guards, customer delete does not; `TransportDossier.CustomerId` has no FK) · credit-blocked customer gets work through the back door; a €45 "Kooiaap" line disappears on recalculation; deleting a customer with 30 un-invoiced orders breaks invoicing · `IsActive` on new/changed `LocationId`; intake gate in both change services; engine reports "service inactive" instead of dropping; refuse customer delete when referenced; FK dossier→customer · backend · change to blocked customer → 400; inactive service → coverage error; delete customer with order → refused.
- **M-08 Numeric entry: comma decimals dropped or blanked; controlled `Number()` invoice lines cannot type "0.5"; number inputs change on mouse-wheel** — R2F-07 · confirmed bug · `orderFormPayload.ts:1157-1202`, `orderFormState.ts:1047,1057` (`Number()`), vs `numberOrNullFrom` for cargo; `NewInvoicePage.tsx:394-405`; all `type="number"` · nl-BE users type "1250,5" → weight null → weight-based pricing skipped · shared `parseDecimal` honouring the tenant separator; `type="text" inputMode="decimal"` or normalise on blur; `onWheel` blur · frontend · comma input in every payload builder.
- **M-09 Detail tables (stops 11 columns, goods 9, pricing) have no scroll container; form rows overflow between 900–1100 px** — F-09, U-09 · UX · `transport-orders.css:38-100`, `TransportOrderDetailPage.tsx:975-987`, `transport-order-form.css:212-217` · 13" laptops/split screens clip the "Venster" column · merge Gevraagd/Bevestigd into one "Vensters" cell, compact `formatWindow`, `overflow-x:auto`, `min-width:0` · frontend · no body horizontal scroll at 1024 px.
- **M-10 List ergonomics: no sorting wired, no date/customer filters although the API supports them, filters reset on back navigation, double fetch on mount; dossier list unpaged and blank while loading; search cannot find by city/stop reference; rows are not links** — U-14, U-13, F-14, R2F-06, U-35 · UX · `TransportOrdersPage.tsx:32-34, 110-113, 115-164`, `DossiersPage.tsx:27-38, 85-90`, `TransportOrderService.cs:141-146` · dispatcher working through 30 orders loses the filter after each one; "the delivery to Genk tomorrow" is unsearchable · URL-synced filters/sort, date/customer filters, `sortKey`s, `extra` option instead of the effect, paged dossier endpoint + `usePagedQuery`, search on stop city/location/reference, `<Link>` cells · both · URL round-trip; one fetch on mount; backend search by city.
- **M-11 Stop dates never defaulted/propagated; removing stops/goods lines has no confirmation while address refresh does; section tabs never show error/filled markers; two editors for the same stop-plan data; whole-page edit mode for small changes; form density (legacy header fields + lines + overrides on the daily path)** — U-15, U-22, U-05, U-17, U-08, U-26 · UX (efficiency) · `orderFormState.ts:131`, `RouteSection.tsx:173-187`, `GoodsSection.tsx:458-467`, `TransportOrderForm.tsx:330-449`, `TransportOrderDetailPage.tsx:743-757, 1034`, `StopExecutionPlanDialog.tsx` · six identical date entries on a 6-drop order; a filled stop vanishes on a mis-click; "Opslaan mislukt" twice for the same order · default stop date from previous stop/order date; confirm only non-empty rows (or undo toast); set `hasError/filled` from `clientErrors`; open the form at a section from per-section "Bewerken"; rename "Venster" and route to the form when editable; goods lines as the only input with a quick row, overrides behind one "Afwijkingen" disclosure · frontend · adding a Losstop pre-fills the date; both tabs show "!".
- **M-12 Modal has no focus trap or initial focus; dropdowns clipped inside modals; price-line dialog autofocuses the last field; "Meer ▾" menu without outside-click close; tiles without arrow keys** — U-20, U-21, U-18, U-29, U-33 · confirmed bug/polish · `Modal.tsx:421-437`, `Modal.css:1076,1110`, `SearchableSelect.tsx:220`, `TransportOrderDetailPage.tsx:1299`, `DossierHeader.tsx:170-192`, `NewDossierPage.tsx:127-157` · Tab leaves "Klant wijzigen" into the page; keyboard-heavy price control suffers · focus trap + restore in `Modal` (~20 lines); portal the listbox; autofocus Aantal; outside-click/arrow handling; roving tabindex · frontend · Tab wraps; focus restored on close.
- **M-13 Silent catches in reference-data loading and multi-request pages; operations poll toasts on every tick and after leaving; portal list couples context + orders with `Promise.all`** — R2F-18, R2F-23, R2F-27 · missing feedback · `useOrderFormData.ts:44-98, 221`, `TransportOrderDetailPage.tsx:190-194, 680`, `DossierHeader.tsx:658-670`, `OperationsPage.tsx:39-58`, `CustomerPortalOrdersPage.tsx:26-40` · a 403 on `unit-types` yields a form with no units/services and no warning → order saved without the customer's standard supplements; ops board shows stale "all clear" · collect load failures into one notice; `allSettled`; `lastRefreshedAt` banner; guard the error path · frontend · rejected `listServiceOptions` → visible notice.
- **M-14 Order form downloads the full customer record (IBAN, VAT notes, internal notes, all contacts) to read four flags; dead `recentItems` localStorage writes; `/packages` palette command 404s** — R2F-25, R2F-26 · security (data minimisation) · `useOrderFormData.ts:90-94`, `CustomerDtos.cs:26-106`, `hooks/recentItems.ts`, `config/commands.ts:86` · every order creator receives bank/memo data client-side · `GET /api/customers/{id}/order-intake` or strip fiscal fields unless `customers.manage_fiscal`; delete `recentItems`; fix the command · both · permission-scoped projection test.
- **M-15 Reviewed price silently recalculated by a normal save; two prices on one page (`agreedPrice` fact vs snapshot total); readiness reasons not surfaced on the order/dossier pages; success feedback is a 4-second toast** — P-17, U-10, P-21, U-28 · UX · `ApplyPricingAsync:2669-2670`, `TransportOrderDetailPage.tsx:795-802, 439, 463`, `DossierPriceSummary.tsx:18` · a checked price changes unnoticed; €450 in one place, €512,30 in another; blockers discovered at invoicing · reset Reviewed → Draft on save with toast (or drop Reviewed); remove `Prijs` from the Lading facts; readiness badge + reasons on order price section and dossier summary; "Laatst berekend om …" · both · save on Reviewed → Draft; only one total outside the lines table.
- **M-16 Terminology drift across dossier/order/pricing/invoicing/manual; dossier summaries format data differently; raw enum/ISO in dossier legacy rows and Ctrl+K; portal ETA in browser locale** — U-25, U-23, R2F-16, R2F-17, R2F-19, B-19 · UX/polish · see §10 terminology table · "Prijsregel / Prijslijn / Verkooplijn / Vrije regel", "Services & toeslagen" vs "Dienst of toeslag", "Paletten" vs "Pallets", two price-status vocabularies, `2026-08-30 · InProgress` on the dossier, "InProgress" in Ctrl+K, "8/30/2026, 2:15:00 PM" in the French portal · one glossary applied to JSON + hard-coded strings; shared formatters/labels; structured search DTO; `Submitted => "Ingediend"` in the timeline · both · glossary lint test.
- **M-17 Portal intake: minimal client validation, generic server messages, `countryCode` hard-coded, `Number(x) || 1` coercion; portal create is two commits with a duplicate planner notification** — F-17, R2B-25 · UX/bug · `CustomerPortalNewOrderPage.tsx:106-158`, `CustomerPortalService.cs:228-249` · customer learns "vul minstens een hoeveelheid…" only after submit; every portal order = two notifications; a failure between saves leaves a customer-visible Draft nobody reviews · mirror minimal-cargo/window rules client-side; initial-status parameter on `CreateAsync` (one save, one event) · both · portal create → single save, single event.
- **M-18 Order-document delete removes the file before the DB commit and hard-deletes the row (no soft-delete filter); document audit not linked to the order; downloads unaudited** — A-14, R2B-36, S-11 · confirmed bug · `TransportOrderDocumentService.cs:102-118, 143-153, 970-977` · a failed save leaves a row pointing at a vanished file; a signed CMR on an invoiced order can vanish; GDPR access log incomplete · soft-delete first, delete file after commit; block delete on Invoiced; include `TransportOrderId` in the audit payload; audit downloads · backend · save failure → file present; delete → row retained with `IsDeleted`.
- **M-19 Dock appointments and failed-stop packages are not coupled to the order lifecycle; drivers can read any package timeline** — R2B-24 · missing rule · `DockPlanningService.cs:484-488, 110-121`, `TripExecutionService.cs:383-387`, `PackageLifecycleMachine.cs:24-29`, `PackagesController.cs:81-88` · cancelled orders block docks at peak hour; failed deliveries strand `Loaded` packages; drivers see back-office names of any package · cancel open appointments on order cancel; stage `DeliveryFailed`; scope the timeline for drivers · backend · cancel order → appointment cancelled.
- **M-20 Unknown/foreign `ServiceOptionId`s silently accepted; duplicate service/price lines in one request untested; manual price lines accept negative/unbounded amounts** — S-06, T-09, P-20 · input trust · `ToEngineSelectionsAsync:2168-2186`, `SaveOrderPriceLinesAsync:3137-3155` · stale service id after deletion saves "successfully"; no threshold review for large discounts; `AgreedPrice` can go below zero · `InvalidReference` when counts differ; reject duplicate `LineKey`/service ids; tenant max deviation % with a second permission; refuse `LinesTotal < 0` · backend · random service id → 422; duplicate key → 400.
- **M-21 `OrderDate`/`DossierDate` default to the UTC day; `TimeOnly` requirements are zone-less next to UTC windows; diesel % uses order date on the order and invoice date on the invoice; tariff changes never flag Draft snapshots** — A-15, P-18, P-19 · missing rule · `TransportOrderService.cs:270`, `DossierService.cs:181`, `PricingEngine.cs:1105-1114` vs `DieselSurchargeCalculator.cs:19-22`, `PriceAdjustmentService.cs:304-351` · night-shift order dated yesterday; quoted diesel % ≠ billed; frozen-vs-current invisible · tenant time zone (see C-03); one diesel reference date; calculation fingerprint on the snapshot + stale marking · backend · `TimeProvider` at 23:30 UTC → next-day `OrderDate`; adjustment created → affected draft flagged.
- **M-22 `TransportOrderStatusHistory` outside the global tenant filter; no write-side tenant fence; status-history cascade-deletes with the order; schema hygiene (missing lengths/precision/FKs, non-unique activity↔order, no `LineKey` uniqueness); string-literal status comparisons** — S-08, S-09, A-13, A-16, R2B-37 · design · `TransportOrderStatusHistory.cs:9-12`, `AuditingSaveChangesInterceptor.cs:41-72`, `TransportOrderStatusHistoryConfiguration.cs:20-23`, `TransportOrderConfiguration.cs:16-46`, `DossierConfigurations.cs:82-84`, `InvoiceControlService.cs:113-144` · a future reader that forgets the predicate leaks history rows; forgotten `TenantId` → invisible rows; hard delete erases the trail; import creates two activities for one order · implement `ITenantOwned` on the history; stamp/validate `TenantId` on `Added` entities; `Restrict` + DB-level immutability; lengths/precision/FK/unique indexes; shared constants · backend · model test: every entity with `TenantId` has a filter; foreign `TenantId` inside a request throws.
- **M-23 Dossier entity dialog always shows "Reden"; "+ Verkooplijn" does the same as "Prijsdetails"; location quick-create unavailable from the dossier Route drawer; after "+ Activiteit" the user still has to find and open the order; manual explains what the UI does not** — U-30, U-24, U-31, U-32, U-34 · UX · `DossierHeader.tsx:247-259`, `DossierPriceSummary.tsx:45-53`, `RouteDrawer.tsx:119-130`, `AddActivityDialog.tsx:59-66`, manual §2.1/§3.3/§4.3/§11.1 · users justify a non-deviation; a button promises to add a line and opens a page; new address on a dossier → leave to Locaties · mirror the order dialog; remove/deep-link the button; host `LocationQuickCreateDialog` in the drawer; open the Route drawer after adding a transport activity; derived title preview, hint under "Herberekenen", rename "Uiterlijk" · frontend (+docs) · default entity hides the reason; drawer opens after add.
- **M-24 Customer detail cannot start or list orders/dossiers; new-order/new-dossier accept no `?customerId=` prefill** — R2F-13 · efficiency · `CustomerDetailPage.tsx` (no links), `NewTransportOrderPage.tsx:805-806` (only `?template=`) · customer-first calls re-enter the customer in a 200-row select · support `?customerId=`; "Opdrachten/Dossiers" card with "Nieuwe opdracht" on the customer page · frontend · prefill tests.
- **M-25 Duplicated form-state logic between the standalone form and the dossier drawers; dead API export; 409 handling and unit-autofill exist twice** — F-16 · maintainability · `orderDrawerState.ts:157-237` vs `TransportOrderForm.tsx:77-189, 262-285`, `GoodsDrawer.tsx:37-148`, `transportOrdersApi.ts:170` · drift already exists (`plateauRequired ?? false`) · single `orderValuesFromDetail` initialiser + reducer; move `applyUnitToCargoRow` to `orderFormState.ts`; delete `setOrderPricingStatus` · frontend · round-trip equality test.

---

## 7. Low-priority / polish findings

- **L-01 No idempotency key on order creation; mutation endpoints have no rate limit** — B-18, S-13 · double click/portal retry creates two orders and two wrapper dossiers; per-user rate-limit policy + optional `Idempotency-Key` · backend.
- **L-02 Price summary block inherits the warning-paragraph style in its most common state** — R2F-09 · `.to-price-summary-warning` (`transport-orders.css:189-194`) is the paragraph style; captions turn red for every unconfirmed order · rename the modifier · frontend.
- **L-03 Legal-entity change via full update skips reason and dossier sync (Rule H)** — B-14 · folded into C-02; listed separately because the dossier/order entity mismatch persists even after the customer fix · backend.
- **L-04 Dead/contradictory code and rules enforced in one path only** — B-20 · `DossierService` "orderservice niet beschikbaar" as a business block (`:541-544`); `DossierActivity.PlannedDate` not rejected for `HasStops`; readiness `HasDate` = `PlannedFrom != null` only; `Close/Reopen/AddRelation` ignore `Version` although docs claim otherwise · clean-ups · backend.
- **L-05 Order-document by-id endpoints, import with foreign customer/profile, dossier link with foreign order, bulk-status with foreign ids, cross-tenant `DossierId` on create — all correctly guarded but untested** — S-(e), T-15 · add to the cross-tenant Theory · tests.
- **L-06 Mass-assignment contract test missing** — T-17 · request records currently expose no `Status/TenantId/OrderNumber`; add a reflection test so future DTO additions cannot regress · tests.
- **L-07 Reflection into private pricing methods; direct entity mutation in tests; frontend mock duplication** — T-29, T-30 · `OneOffPricingTests.cs:157-191`, `CombinedUnitDiscountOrderTests.cs:106-108`; every page test re-declares 6–8 stubs · route through public API after C-01 (stable stop ids); shared `renderOrderDetail` + `createApiMock().rejectWith(ApiError)` · tests.
- **L-08 Interceptors under real `SaveChanges` asserted only for manual paths; `ChangedByUserId` always null in tests** — T-20 · add `StatusHistoryAssertions` to trip/invoice/incident tests; fake `IHttpContextAccessor` test · tests.
- **L-09 Transaction rollback / atomicity of `CreateAsync` untested** — T-19 · failing `IPricingEngine`/entity policy → zero rows, tracker clean · tests.
- **L-10 Edit mode/drawers not deep-linkable; no `?section=`** — R2F-12 · support cannot link "open the price section of TO-…" · read-only `?section=`/`?edit=1` on mount · frontend.
- **L-11 Trip page "add order" picker (100 Confirmed, no search) defines "plannable" differently from the planning center** — R2F-15 · reuse the unplanned endpoint or link to the planning center · frontend.
- **L-12 Delete on the order page has no busy guard (double DELETE)** — F-18 · pass `busy` to `ConfirmDialog` · frontend.
- **L-13 Status-filter reload hack double-fetches on mount** — U-35 · use `usePagedQuery`'s `extra` · frontend.
- **L-14 Success feedback toast-only; stale-price warning disappears silently after recalculation** — U-28 · "Laatst berekend om …" + highlight · frontend.
- **L-15 New-dossier: no breadcrumbs/cancel, derived title not previewed; dossier list has no breadcrumbs; `<h1>`/`<h2>` inconsistency** — U (walkthrough) · frontend polish.
- **L-16 Documentation drift** — R2B-39 · `docs/pricing.md` rule-score formula (tier×4 vs code tier×8 + activity 4 + zones), §8 vs §13.3 on Locked-price edits, pricing-status map and `orders.lock_price` missing from `docs/permissions.md`, "no holiday calendar" (exists), missing condition kinds; `docs/dossiers.md` omits close preconditions/read-only effects, claims quick-start-only fast-create tiles (code accepts any active type), readiness list lacks `pricing.stale`, omits `PUT {id}/customer`; field-audit proposal superseded; readiness/snooze documented only in `developer-architecture.md` · docs.
- **L-17 `PriceSection` renders a manual price input without `orders.override_price` in the no-preview case; delete-document button for create-only users** — S-15 · BE safe; FE quirk · frontend.
- **L-18 Number *gaps* (not duplicates) on non-concurrency exceptions inside the numbering retry** — R2B-(c) · acceptable; document · —.
- **L-19 Internal `Notes` printed on delivery documents** — part of H-19 · —.
- **L-20 `DossierService.AddRelationAsync` has no `RequireOpen`** — S-(b) · decide whether closed dossiers may gain relations · backend.
- **L-21 Customer-change preview-only tests; vacuous assertions; `ListForExport` filter not discriminated; `Update_TimeWindowEndBeforeStart` never calls Update** — T-§3 · tighten the listed tests · tests.
- **L-22 Order-number concurrency untested for orders/dossiers** — T-13 (design verified race-safe) · add same-pattern test · tests.
- **L-23 Address picker / blocked-customer warning never exercised in FE tests (`LocationSelect` mocked away)** — T-26 · stub that fires `onChange` with a location · tests.
- **L-24 Portal order list capped at 200 unpaged** — R2B-26 · paginate · both.
- **L-25 Global search subtitle is the raw English status; categories hard-coded Dutch** — R2F-17 · structured DTO · both.

---

## 8. Backend findings (index)

Critical: C-01 stop-id churn · C-02 customer via PUT · C-04 `Invoiced` KeyNotFound · C-05 redelivery outside use case · C-06 partial flush / two-commit audit.
High: H-01 concurrency token · H-02 trip membership ignored · H-03 dossier containment + backfill · H-04 locked guard incomplete · H-05 readiness/snooze advisory · H-06 invoice cancel / credit notes · H-07 pricing gated on snapshot only · H-08 notification link paths · H-15 cargo delete orphans packages · H-16 rounding · H-17 EDI · H-18 ETA · H-19 CMR parties · H-23 trip auto-completion · H-26 chronology/ADR · H-27 actuals/POD/time requirement · H-28 side effects in controller / permission parity · H-32 history duplicates / TripOrder uniqueness · H-33 sweeps / GDPR.
Medium: M-02, M-03, M-04, M-05, M-06 (bulk), M-07, M-17 (portal create), M-18, M-19, M-20, M-21, M-22. Low: L-01, L-03, L-04, L-16, L-18, L-20.

## 9. Frontend findings (index)

Critical: C-03 time-zone (FE half). High: H-01 (409 rebase), H-08 (routes), H-09 server messages lost, H-10 pickers, H-12 preview, H-13 hidden price field, H-20 unsaved guard/Enter, H-21 dead-end page, H-22 header, H-24 i18n, H-25 stale panels, H-30 permission gating, H-31 template mode, H-34 blob downloads/race loads. Medium: M-01, M-06, M-08, M-09, M-10, M-11, M-12, M-13, M-14, M-15, M-16, M-17, M-23, M-24, M-25. Low: L-02, L-10–L-15, L-17, L-25.

### 9.1 Frontend ↔ backend comparison (explicit answers to the audit questions)

| Question | Answer |
|---|---|
| Is every important frontend validation also enforced server-side? | Yes for everything the client validates (customer required, stop location/city, window ordering, time-requirement completeness, cargo qty > 0, barcode uniqueness, manual-price reason, one-off amount). The gap is the **other direction** (H-09): ~12 server rules have no client counterpart and their messages are discarded. Neither side validates stop chronology (H-26). |
| Are there backend rules the frontend does not explain? | Yes: confirmed order must keep ≥1 loading + ≥1 unloading; customer-reference-required; blocked customer on create; cargo stop-link rules; negative values; override permission on save of an overridden order (M-05); trip membership for corrections; edit gate for Planned orders (H-21); snapshot lock reasons on save. |
| Buttons/actions that appear possible but the API rejects? | Bulk status targets Cancelled/Planned/Invoiced/Submitted (M-06); "Voorstel bevestigen" for `override_price`-only users (H-30); delete-document for create-only users; "+ Verkooplijn" that just navigates (M-23); any action on an **invoiced** order (C-04, 500). |
| Actions the API allows that the frontend correctly tries to prevent? | Customer/entity change through `PUT` (C-02); pricing lines on Cancelled/Invoiced/snapshot-less orders (H-07); cancel/delete/manual status on trip (H-02); editing `Submitted` orders (allowed by BE, hidden by FE — intended, should be enabled); `create-order` without `orders.create` (H-28); linking cross-customer orders (H-03; the FE picker pre-filters by customer). |
| Are permission rules identical? | Endpoint codes match, but the FE ignores the `orders.manage` umbrella in five places and gates line-confirm on the wrong code (H-30); no route-level guards (BE enforces). |
| Are statuses interpreted consistently? | Enums match (order, pricing, coverage, dossier). Labels come from three namespaces (orders, dashboard, portal) plus raw enums on the dossier legacy rows and in Ctrl+K (M-16). `Planned` means different things for orders and trips (documented drift). |
| Is pricing state represented consistently? | Enum strings match; `isStale` banner and coverage warning exist; but the dossier price chip uses a different vocabulary and condition than the order badge, `agreedPrice` and `linesTotal` both appear, readiness reasons are absent on order/dossier pages (M-15, M-16), and the preview omits inputs the engine uses (H-12). |

---

## 10. UI/UX findings (senior product-designer view)

Classification of every UX-relevant finding (one class each):

| Class | Findings |
|---|---|
| **functional bug** | C-03 time display; C-04 invoiced 500; H-08 404 links; H-10 picker cap; H-12 preview; H-13 hidden price field; H-24 i18n; H-25 stale panels; H-34 downloads/race; M-08 numeric input; M-10 (dossier list blank); M-12 (focus trap); L-02 warning style; L-13 double fetch |
| **confusing UX** | H-22 header/"Annuleren"; H-21 (Planned orders, dead end); M-01 first-order-only + mixed navigation; M-15 two prices / Reviewed; M-16 terminology; M-23 (Reden always shown, "+ Verkooplijn"); H-09 generic error |
| **visual polish issue** | M-09 tables; M-12 (clipped dropdowns, autofocus, menu, tiles); M-16 (raw enum/ISO, portal locale); L-15 breadcrumbs/headings |
| **unnecessary complexity** | M-11 (whole-page edit, two stop editors, form density); L-10 no deep links; L-11 trip picker |
| **missing feedback** | H-20 unsaved guard; M-13 silent catches; M-15 readiness reasons/toast-only; L-14; H-25 |
| **missing shortcut/efficiency opportunity** | M-10 lists (sort/filter/persist/links); M-11 (stop date defaults, section markers); M-24 customer-first entry; H-21 (open order from planning card, incident from order); M-23 (quick-create in drawer, open drawer after activity); H-20 (Enter policy) |

### 10.1 Terminology consistency table (concept → labels found → where)

| Concept | Labels found | Where |
|---|---|---|
| Transport order | Transportopdracht, Opdracht, order, Order, "Opdrachten (klassiek)" | `transportOrders.json`, `dossiers.json`, `invoices.json`, `navigation.json:47`, `CommandPalette.tsx:22` |
| Price line | Prijsregel, Prijslijn, Verkooplijn, Vrije regel, Vrije prijsregel, Handmatige lijn, factuurlijn | detail 849/1237/1341, `customerChange.impact.*`, `dossiers.json price.*`, manual §4.4, `services.description` |
| Price status | Nog te bevestigen / Bevestigd / Onvolledig / Gefactureerd **vs** ⚠ Onvolledig / ✓ In orde | `priceStatus.*` vs `dossierDisplay.ts:35-43` |
| Line kind | AUTO / AANGEPAST / MANUEEL / VOORSTEL vs "(handmatig)", "Handmatige prijs" | `lineKind.*` vs `summary.manualSuffix` |
| Goods | Goederen, Lading, Goederenlijnen, Lijn n, Colli, orderlijn | detail 760-776, form tab, `serviceKind.PerOrderLine`, timeline |
| Pallet | Paletten vs Pallets, pallet-dagen | `goods.pallets` vs `services.pallets` |
| Services | "Services & toeslagen" (tab) vs "Dienst of toeslag", "Diensten" | `form.sections.services` |
| Stop time concepts | Gepland, Tijdseis, Gevraagd, Bevestigd, Vroegst toegelaten, Uiterste tijdstip, "Venster", "Uitvoeringsplan"; manual says "Uiterlijk" | stops table, `route.*`, `stopPlan.*`, manual §3.3 |
| Cancel vs back | "Annuleren" (dismiss), "Annuleren" (order status), "Terug", "Sluiten" | detail 599/1144/1488 |
| Copy order | "Gebruik als sjabloon" (code) vs "Kopiëren naar nieuwe opdracht" (nl json) vs "Sjabloon" (dossier tiles) | detail 613, json 452 |
| History | Historiek, Notities & historiek, Timeline | detail 1110, `dossiers.detail.notesTitle` |
| Invoicing entity | Facturerende entiteit, Entiteit, Klantstandaard | `general.legalEntity`, `dossiers.header.entity` |

### 10.2 Top 10 UX improvements (daily time saved / errors prevented)

1. C-03 one time convention (wrong slots on CMR/leveringsbon, contradictions between screens).
2. H-10 server-searched customer/order pickers (unblocks large tenants; slowest field of the most-used form).
3. H-13 show "Afgesproken prijs" when the engine asks for it (orders no longer stuck at Onvolledig).
4. H-20 unsaved-changes guard + Enter policy on the order form (largest source of lost work).
5. H-22 reorganise the detail header (one primary, "Meer ▾", "Opdracht annuleren").
6. H-21 links to trip/invoice/incident on the order page + disabled "Bewerken" with reason + back-to-dossier.
7. M-01 honest multi-order dossier page (order number in section titles, one block per order, consistent open behaviour).
8. M-10 URL-persisted filters, sort, date/customer filters, paged dossier list, city search.
9. M-11 default stop dates from the previous stop, confirm non-empty deletions, section error markers.
10. H-12 + M-15 live total in the sticky footer, honest preview errors, readiness reasons on the order/dossier pages; then H-24/M-16 wire i18n and settle the glossary.

---

## 11. Pricing / invoicing findings (index)

C-02 (locked price kept across customer change) · H-04 locked guard incomplete · H-05 readiness/snooze advisory · H-06 invoice cancel / credit notes / duplicate ids / stranded snapshot · H-07 snapshot-only gate, legacy price overwrite, incident charges · H-12 preview ≠ save · H-13 hidden manual price · H-16 rounding · H-27 actuals never re-priced · H-31 template copies overrides · M-03 dossier summary · M-04 invoice lines vs price lines · M-05 override permission on save · M-15 Reviewed/two prices/readiness · M-20 negative/duplicate lines · M-21 diesel date / stale tariffs.

### 11.1 Invoice boundary matrix (edit × invoice state, as implemented)

| Edit | No invoice (Completed) | Draft invoice | Sent / Paid | Peppol-delivered | Credit note exists |
|---|---|---|---|---|---|
| Order header/stops/cargo (`PUT`) | Blocked (status) | Blocked | Blocked | Blocked | Blocked |
| Price lines / recalc / confirm line | **Allowed** | Blocked via snapshot — **not** for snapshot-less orders (H-07) | Blocked | Blocked | Blocked |
| Whole-order override | Allowed | Blocked | Blocked | Blocked | Blocked |
| Incident charge → order | Allowed | Refused (note) — **applied anyway when no snapshot** (H-07) | same | same | same |
| Customer change (dedicated) | Allowed | Allowed; lines released | Blocked | Blocked | Blocked (also on **Cancelled** invoices, H-06) |
| Legal-entity change | Allowed | Allowed; **snapshot stays Invoiced** (H-06) | Blocked | Blocked | Blocked |
| Status correction | Allowed | Blocked | Blocked | Blocked | Blocked |
| Invoice line edit | — | Allowed, **no sync to order** (M-04) | Blocked | Blocked | Blocked |
| Invoice cancel | — | Allowed → released | **Allowed → released, re-invoiceable** (H-06) | **Allowed** (only Queued transmissions cancelled) | Allowed |
| Credit note | — | n/a | Full copy; **re-frozen from live data at Send**; order stays Invoiced | same | second blocked |
| Cancel order | Allowed (Draft…InProgress) | Blocked | Blocked | Blocked | Blocked |

---

## 12. Audit / history findings (index)

C-06 audit written in a second save / half flush · H-11 diff content, override audit, timeline coverage, dossier history, planning reasons · H-32 duplicate history rows on retry · M-18 document audit not linked, downloads unaudited · M-22 history cascade / outside tenant filter · L-08 interceptor assertions. Verified good: interceptor coverage of every status write; forensic fields (IP, correlation id) stamped centrally; no `AccessCode` in any order audit payload.

## 13. Security / tenant-isolation findings (index)

**No cross-tenant IDOR found** on any dossier/order/document/link/bulk/import/portal endpoint (double fence verified). Findings: H-14 portal document/notes/reason exposure + legacy `orders.view` on portal roles · H-18 ETA scoping/side effects · H-28 permission parity (reject without `orders.cancel`, `create-order` without `orders.create`) · H-30 FE gating · M-14 customer over-fetch (IBAN/notes) · M-19 driver package timeline scoping · M-20 silent foreign service ids · M-22 history outside filter, no write-side fence · L-01 rate limiting/idempotency · L-05/L-06 missing security tests.

## 14. Data-integrity / concurrency findings (index)

C-01 stop-id churn · C-05 redelivery packages · C-06 transactions · H-01 versions · H-02 phantom TripOrders/packages · H-03 dangling links, backfill re-wrap, delete cascade · H-15 cargo/package orphans · H-32 history duplicates, TripOrder uniqueness · H-33 shared context sweeps, GDPR · M-07 customer delete, inactive master data · M-18 file-before-commit · M-21 dates · M-22 schema hygiene · L-01 idempotency.

---

## 15. Missing business cases (checklist from the brief → status)

| Case | Status | Finding |
|---|---|---|
| Loading date later than unloading date / invalid stop chronology | **Not enforced** anywhere | H-26 |
| Same-day transport | Allowed (fine); no compaction in display | M-09 |
| Multi-stop transport | Supported; no route-shape rule; dossier shows first order only | H-26, M-01 |
| Cargo not required for certain types | Minimal-cargo rule (description **or** line) applies uniformly; `ActivityType.SupportsGoods` not consulted by order validation | (design note) |
| Missing addresses | Enforced (location or city) both sides | ✓ |
| Reused/shared addresses | Snapshot model correct; inactive locations selectable | M-07 |
| Changing customer after creation / after pricing | **Unguarded via PUT** | C-02 |
| Changing legal entity | Dedicated flow correct; PUT bypass; snapshot left `Invoiced` | C-02, H-06 |
| Changing addresses after pricing | Locked price silently kept | H-04 |
| Changing cargo after pricing | Refused on Locked ✓; Draft recalculated ✓ | ✓ |
| Adding/removing services after pricing | Refused on Locked ✓; inactive options dropped silently | M-07 |
| Stale pricing snapshots | Flag only on customer change; invoiceable | H-04, H-05, M-21 |
| Duplicate price lines | No unique key; concurrent recalcs duplicate | H-01, M-20 |
| Manual overrides becoming stale | No detection; override survives everything | M-21, H-04 |
| Orders already assigned to a trip | Edit/cancel/delete unguarded | H-02, C-01 |
| Partially executed orders | Trip cancel reverts to Confirmed regardless of executed stops; POD from any trip counts | H-27 |
| Failed delivery | Order silently Completed via trip auto-complete; untested | H-27, H-29 |
| Redelivery | Built by hand; unpriced, unscannable | C-05 |
| Cancellation | No trip/package/dock/readiness cascade | H-02, M-19 |
| Deleting linked objects | Delete leaves links/pricing/documents/packages/TripOrders | H-03, H-02 |
| Deactivated master data still referenced | Locations/customers/services partially unguarded | M-07 |
| Duplicate submission / double clicks | Buttons disabled ✓; no idempotency key; delete confirm no busy | L-01, L-12 |
| Concurrent editing / stale FE state | Version half-wired; FE dead end; race loads | H-01, H-34 |
| Direct API manipulation | Customer via PUT, pricing on terminal orders, cancel on trip, cross-customer link | C-02, H-07, H-02, H-03 |
| IDOR / tenant leakage | None found | ✓ |
| Permission mismatches FE/BE | Umbrella, line-confirm, Submitted edit, reject/cancel, create-order | H-30, H-28 |
| Financially unsafe edits after invoicing | Sent-invoice cancel, snapshot-less pricing, incident charges, draft-line edits | H-06, H-07, M-04 |
| Missing audit entries | Override, stop diffs, reorder, planning reasons | H-11 |
| Audit entries without old → new | `Updated`, dossier `Updated`, activities | H-11 |
| Inconsistent terminology | Extensive | M-16 |

---

## 16. Test coverage gaps

**Well covered** (keep): order create/update validation (8 minimal-cargo variants, intake gates, foreign customer/location, windows, time requirements), cargo id sync, status machine incl. corrections and immutable history, pricing engine/lines/lock/confirm/one-off/day quantities/combined discounts, customer & entity change (order + dossier, transactional), dossier foundation (auto-wrap, templates, 409 with body), trip↔order propagation, invoice↔order coupling, portal review, notifications; FE: sectioned form validation and payload, pricing-line flows, dossier 409 banner, change dialogs.

**Weak tests** (T-§3): `ListForExport_ReturnsFilteredRows` (filter not discriminated), `Update_TimeWindowEndBeforeStart_FailsValidation` (never calls Update), random-id tenant tests in `OrderPricingLineTests`, preview-only customer-change tests, vacuous `DossierLegalEntityChangeTests:137`, `Contains("30")` audit assertion, `tripExecutedOverride:true` bypass, `TripBatch…InRouteOrder` (page count only), `IncidentServiceTests` nonexistent-Guid "tenant" tests, failed-stop tests asserting trip only, FE static-presence tests, CSS-regex tests.

**Implementation-detail tests**: 13 `OneOffPricingTests` and `CombinedUnitDiscountOrderTests` reflect into private methods; `InvoiceControlTests` seed readiness strings; notification tests use synthetic order contexts.

**Missing** — business edge cases (T-01…T-13), security (T-14…T-17), integration (T-18…T-20), frontend interaction (T-21…T-26), infra (T-27…T-30): all mapped to findings above. No test file exists for `NewTransportOrderPage`, `TransportOrdersPage`, `StopExecutionPlanDialog`, `OrderDocumentsPanel`, `OrderTimelinePanel`, `UnitSelect`, `RouteDrawer`, `GoodsDrawer`, `ActivityDrawer`, `DossierHeader`, `DossierLinkDialogs`, `DossiersPage`.

### 16.1 Top 15 regression tests to add

| # | Test | Asserts |
|---|---|---|
| 1 | `Orders_EveryMutation_OnForeignTenantOrder_ReturnsNotFound` (Theory over all mutators, real tenant-B order) | NotFound and no tenant-B row changed |
| 2 | `H1Filter_TransportOrdersAndDossiers_HideForeignTenant_WithoutExplicitWhere` | `CreateContextForTenant(A)` sees zero B rows for orders/dossiers/links/lines |
| 3 | `GET_Order_Invoiced_Returns200_WithEmptyTransitions` + enum-coverage test | C-04 |
| 4 | `Update_ConfirmedOrder_KeepsStopIds_AndPackagePins_AndLabelAddresses` | C-01 |
| 5 | `UpdateAsync_WithDifferentCustomerOrEntity_IsRefused`; Locked + customer change → refused | C-02 |
| 6 | `PUT_Order_StaleVersion_Returns409_WithCurrentBody` + `Cancel/ConfirmPricing_WithStaleVersion_ReturnsConflict` + two-context lost-update test | H-01 |
| 7 | `Endpoint_PermissionMatrix_Returns403ForMissingCode` (WebApplicationFactory) | H-29, H-28 |
| 8 | `FailedUnloadingStop_ThroughTripExecutionService_CreatesIncident_OrderNotCompleted_ReadinessReview` | H-27, C-05 |
| 9 | `Redelivery_ThroughCreateAsync_HasCargoPricingActivityDossierAudit_AndPackagesScannable` | C-05 |
| 10 | `Cancel/Delete_OrderOnPlannedTrip_IsRefused_OrDetachesTripAndPackages` | H-02 |
| 11 | `Delete_SoftDeletesPricingLines_ServiceLines_Documents_DossierLink_TripOrder_Packages` + `Backfill_DoesNotRewrapUnlinkedOrder` | H-03 |
| 12 | `InvoiceCreate_NonCompletedOrStaleOrSnoozedOrder_IsRefused` (Theory) + `SentInvoice_WithDeliveredTransmission_CannotBeCancelled` + rounding parity fixture | H-05, H-06, H-16 |
| 13 | `LinkOrder_OfOtherCustomerOrAnotherDossier_IsRefused` + `Create_UnloadingPlannedBeforeLoading_IsRejected` | H-03, H-26 |
| 14 | FE `order form: surfaces server messages/field errors, blocks double submit, sends version, rebases on 409, keeps 08:00 through payload→detail (TZ=Europe/Brussels)` | H-09, H-01, C-03 |
| 15 | FE `order detail lifecycle: transitions/cancel/correct/delete adopt the returned order; panels refresh; manage-only user sees actions; requiresManualPrice shows the price field` | H-25, H-30, H-13 |

---

## 17. Findings from independent review pass 2

New in pass 2 (not found by any pass-1 reviewer): startup backfill re-wrap (H-03/R2B-01), history duplicates on retry (H-32), snooze not enforced (H-05/R2B-03), redelivery packages unscannable + closed dossier (C-05/R2B-04-05), phantom `TripOrder` after delete (H-02/R2B-07), `TripOrder` uniqueness race (H-32), planning status changes without reason (H-11/R2B-09), driver-app auto-completion swallowed (H-23), ETA security (H-18), UTC-vs-local **decisions** in planning/ETA (C-03/R2B-14), POD/time-requirement gaps (H-27/R2B-15), PDF/UBL/export rounding (H-16), credit-note re-freeze + zero-line send + duplicate order ids (H-06/R2B-19-20), cargo-delete package orphans (H-15), label/scan tallies after edit (C-01/R2B-23), dock/failed-stop/driver-timeline coupling (M-19), portal two-commit + duplicate notification (M-17), portal reason/notes/inactive exposure (H-14/R2B-26), **portal document exposure** (H-14/R2B-27), **EDI duplicate/replay/time mapping** (H-17), import batch bookkeeping (C-06/R2B-31), notification 404 links (H-08), shared-context sweeps and GDPR (H-33), **CMR party mapping** (H-19), hard-deleted documents (M-18), string-literal statuses (M-22), template copies overrides (H-31), doc drift (L-16); frontend: preview omits equipment flags and is unsequenced (H-12), order page dead end and consumer screens without links (H-21), numeric/comma handling (M-08), invoice builder fail-open (H-05), unguarded `load()` races (H-34), blob downloads bypass refresh (H-34), Enter policy (H-20), customer over-fetch (M-14), silent catches/poll toasts (M-13), customer-first entry (M-24), cross-screen status/date/number formatting matrix (M-16).

Pass-1 items corrected or re-scoped by pass 2 (accepted after re-check):
- **T-04 "order removed from trip after partial execution"** is not reachable via the API (removal requires Draft/Planned trips; stop transitions require InProgress). The multi-trip half stands as the schema gap (H-32).
- **T-13 order-number concurrency** — design is race-safe; only "untested" remains (L-22); redelivery inline copy folded into C-05.
- **F-13/F-17 portal-vs-planner concurrent edit** — no portal edit/cancel endpoint exists; the gap is the missing feature, not a token bug (kept in H-30 as "enable editing Submitted").
- **A-11 package generation without transaction as duplicate source** — generation is idempotent per live line; the real duplicate source is cargo-id churn (H-15). A-11's batch/side-effect concern stays in H-28.
- **P-10** understated: after a dropped base line the order is stranded, not released (folded into M-04).
- **S-03 Accept TOCTOU** — no exploit; only controller-side side effects skipped (H-28). **S-14** — search has no weakness of its own; exposure is the `orders.view` grant (H-14).
- **U-14 / U-05 / F-05** — wiring gaps, not missing capabilities (`DataTable` sorts, `SectionNav` markers, drawers rebase); re-worded in M-10, M-11, H-01.
- **B-01** fix location: relink belongs in `UpdateAsync` next to `RelinkCargoToReplacedStops` (C-01).

---

## 18. Recommended implementation order

**Phase 0 — stop the bleeding (days, no schema change)**
1. C-04 add `Invoiced` to `Transitions` + `TryGetValue` + enum test.
2. H-08 fix six `LinkPath`s + redirect routes.
3. H-13 price-field condition; H-09 use `localizeApiError`; H-30 `hasAnyPermission` + `Submitted` editable.
4. C-02 refuse customer/entity change in `UpdateAsync` + disable the selects (fast guard; delegation later).
5. H-06 block Sent→Cancelled once transmitted; refuse zero-line Send; `Distinct()` order ids.
6. H-14 filter portal documents to delivery note/CMR (temporary rule) and stop exposing `Notes`/reasons; `IsActive` in the portal resolver.
7. H-12 add equipment flags to the preview; H-25 `key={order.version}` on panels.

**Phase 1 — stop identity, transactions, concurrency (1–2 sprints; migrations)**
8. C-01 stable stop ids (update in place) + relink packages/executions/labels; H-15 cancel packages of removed lines.
9. C-06 `AuditService` stage-only + single commit; transactions around `UpdateAsync`, dossier-activity order creation, portal create+submit, EDI, import.
10. H-01 `IsConcurrencyToken` + version on every mutator + one 409 filter + `LineKey` unique index; FE rebase in the standalone form.
11. H-32 interceptor idempotency on retry; `TripOrder` partial unique index.

**Phase 2 — lifecycle coupling (1–2 sprints)**
12. H-02 trip guard on cancel/delete/manual transitions + package/dock cascade; H-23 surface trip auto-completion result.
13. C-05 redelivery through `CreateAsync` + package hand-over + redelivery pricing rule + closed-dossier rule.
14. H-03 dossier containment invariants, delete cascade, readiness over links, backfill cut-off.
15. H-28 post-commit hook for EDI feedback + package generation; permission checks on reject/create-order.
16. H-17 EDI dedupe/replay/time mapping; H-31 template stripping.

**Phase 3 — money (1 sprint)**
17. H-05 readiness + snooze enforced in `CreateAsync`; FE default selection + reasons + error states.
18. H-04 locked guard from the engine request; H-07 order-status gate on pricing + legacy price rule + incident charge notes.
19. H-16 one rounding helper; export VAT by group. H-06 credit-note snapshots/release helper/block on Sent|Paid only.
20. M-04 invoice lines from price lines; M-03 dossier/profitability sums; M-05 override permission semantics.

**Phase 4 — time and audit (1 sprint)**
21. C-03 tenant time zone + one wire format; backend `ITenantClock` at the planning/ETA/ops sites; EDI/portal encodings.
22. H-11 change-tracker diff audit + `PriceOverridden` + timeline merge + dossier timeline; H-11 planning reasons.
23. H-27 execution → re-pricing/readiness; POD gating; `TimeRequirement` in late detection.

**Phase 5 — UX for daily use (parallel track, FE-only, can start in Phase 0)**
24. H-10 pickers; H-20 unsaved guard + Enter policy; H-22 header; H-21 links/DTO extension (needs BE) ; M-01 dossier multi-order; M-10 lists; M-11 form ergonomics; M-08 numeric input; H-24/M-16 i18n + glossary; M-12 focus trap.

**Phase 6 — hardening and tests**
25. H-29 harness: cross-tenant fixture, H1-filter tests, `WebApplicationFactory` permission matrix + 409 contract, Postgres smoke, `PRAGMA foreign_keys`; H-18 ETA scoping; H-33 sweeps/GDPR; M-22 schema hygiene; M-07 master-data guards; L-16 docs.

---

## 19. Quick wins (≤ 1 day each, high value)

- C-04 `Transitions[Invoiced]`.
- H-08 notification link paths (+ redirect routes).
- H-13 `PriceSection` condition.
- H-09 `localizeApiError` in `TransportOrderForm`.
- H-30 `hasAnyPermission([code,'orders.manage'])`; `Submitted` editable; line-confirm gate.
- H-12 preview flags + request-id.
- H-25 `key={order.version}` on sibling panels.
- H-31 strip override/ids in template mode.
- C-02 guard in `UpdateAsync` + disable selects (full delegation later).
- H-06 block Sent→Cancelled after transmission; `Distinct()`.
- H-14 hide non-delivery documents, `Notes` and reasons from the portal.
- L-13 `extra` option instead of the reload effect; L-12 `busy` on delete confirm; L-02 CSS class rename.
- M-10 wire `sortKey`s (DataTable already supports it); M-11 default stop date from previous stop.
- H-22 "Opdracht annuleren" label + move destructive actions into a "Meer ▾" menu (dossier pattern exists).

## 20. Larger architectural improvements

1. **Stable child identity + explicit cascade policy** for the order aggregate (stops, cargo, packages, executions, documents, links, trip links): update-in-place with soft delete only for removed rows; one `SoftDeleteCascade` per aggregate; DB `Restrict` where evidence must survive.
2. **Unit of work per request**: `AuditService` stages only; ambient transaction with execution strategy; post-commit domain events for EDI feedback, package generation, notifications, readiness — so every caller (controller, portal, planning, invoicing, incidents) gets the same side effects.
3. **Optimistic concurrency end-to-end**: EF token, version on every mutation DTO, one 409 filter, FE rebase pattern extracted from the dossier drawers into a shared hook; TanStack Query with keyed invalidation to replace hand-rolled state.
4. **One order use case for every creation path** (redelivery, EDI, import, portal, dossier activity) with an initial-status/source parameter and an idempotency key.
5. **Tenant time zone** as a first-class setting with `ITenantClock`, one wire format, and formatting helpers shared by API documents, e-mails, planning decisions and the SPA.
6. **Pricing input fingerprint** on the snapshot (engine request hash + rule/version ids) driving `IsStale` uniformly (customer, address, date, equipment, activity, tariff changes, execution actuals) instead of per-wave guard lists; invoice lines generated from price lines.
7. **Readiness as a gate**: `InvoiceReadiness`, snooze, POD-from-completed-trip and stale price enforced in `InvoiceService.CreateAsync` with explicit, audited overrides; credit notes that release orders; one rounding helper across PDF/UBL/export.
8. **Dossier as real container**: one primary dossier per order (unique index), link/create validated on customer, readiness over links, lifecycle coupling (auto-close wrappers, closed = no planning), a dossier timeline, and multi-order sections in the UI.
9. **Change-tracker audit diffs** for whitelisted aggregates with a rich, merged order/dossier timeline — the single largest improvement to dispute handling.
10. **Test harness**: `WebApplicationFactory` with seeded roles, cross-tenant fixture with the H1 filter on, Postgres smoke suite, shared FE render/mocks helper that can reject with `ApiError`.

---

## Appendix A — Finding count by severity

| Severity | Count | IDs |
|---|---|---|
| Critical | 6 | C-01 … C-06 |
| High | 34 | H-01 … H-34 |
| Medium | 47 | M-01 … M-25 (25 consolidated entries covering 47 source findings) |
| Low | 25 | L-01 … L-25 |
| **Total consolidated** | **112** | from 222 raw findings (156 pass 1 + 66 pass 2), de-duplicated |

## Appendix B — Source-report index → consolidated ID

B-01→C-01 · B-02→C-02 · B-03→C-05 · B-04/B-05→H-02 · B-06→H-03 · B-07→H-07 · B-08/B-09→H-03 · B-10→H-26 · B-11→M-07 · B-12→M-02 · B-13→C-04 · B-14→L-03/C-02 · B-15→H-26 · B-16→C-06 · B-17→H-03 · B-18→L-01 · B-19→M-16 · B-20→L-04 · F-01→C-03 · F-02→H-09 · F-03→H-25 · F-04→H-20 · F-05→H-01 · F-06→H-30 · F-07→H-10 · F-08→M-06 · F-09→M-09 · F-10→H-01 · F-11→H-12 · F-12→C-02 · F-13→H-30 · F-14→M-10 · F-15→M-01 · F-16→M-25 · F-17→M-17 · F-18→H-31/L-12 · S-01→C-02 · S-02/S-03→H-28 · S-04→H-02 · S-05→H-07 · S-06→M-20 · S-07→H-03 · S-08/S-09→M-22 · S-10→H-03 · S-11→M-18 · S-12→H-14 · S-13→L-01 · S-14→H-14 · S-15→H-30/L-17 · P-01→H-05 · P-02→C-02 · P-03→H-04 · P-04/P-05→H-06 · P-06→H-01 · P-07→H-27 · P-08→H-07 · P-09→M-03 · P-10→M-04 · P-11→C-05 · P-12→M-05 · P-13/P-14→M-04 · P-15→H-07 · P-16→H-04 · P-17→M-15 · P-18/P-19→M-21 · P-20→M-20 · P-21→M-15 · A-01→H-01 · A-02→C-06 · A-03→C-01 · A-04→H-03 · A-05/A-06/A-07→H-11 · A-08/A-09→H-03/M-07 · A-10→C-05 · A-11→H-28 · A-12→C-06 · A-13→M-22 · A-14→M-18 · A-15→M-21/C-03 · A-16→M-22 · A-17→C-06 · T-01→H-26 · T-02→C-01/H-02 · T-03→H-27/H-29 · T-04→H-32 · T-05→H-03 · T-06→H-03 · T-07→C-02 · T-08→H-05 · T-09→M-20 · T-10→H-01 · T-11→M-06 · T-12→M-07 · T-13→L-22 · T-14/T-15/T-18/T-27/T-28→H-29 · T-16→H-14 · T-17→L-06 · T-19→L-09 · T-20→L-08 · T-21→H-09 · T-22→§16 #15 · T-23→M-01 · T-24→H-30 · T-25→M-06 · T-26→L-23 · T-29/T-30→L-07 · U-01→C-03 · U-02→H-10 · U-03→H-24 · U-04→H-20 · U-05→M-11 · U-06→H-13 · U-07→H-22 · U-08→M-11 · U-09→M-09 · U-10→M-15 · U-11/U-12→M-01 · U-13/U-14→M-10 · U-15→M-11 · U-16→H-12 · U-17→M-11 · U-18→M-12 · U-19→H-22 · U-20/U-21→M-12 · U-22→M-11 · U-23→M-16 · U-24→M-23 · U-25→M-16 · U-26→M-11 · U-27→M-06 · U-28→L-14 · U-29→M-12 · U-30/U-31/U-32→M-23 · U-33→M-12 · U-34→M-23 · U-35→L-13 · R2B-01→H-03 · R2B-02→H-32 · R2B-03→H-05 · R2B-04/05→C-05 · R2B-06→H-07 · R2B-07→H-02 · R2B-08→H-32 · R2B-09→H-11 · R2B-10→H-28 · R2B-11→H-23 · R2B-12/13→H-18 · R2B-14→C-03 · R2B-15→H-27 · R2B-16→M-02 · R2B-17/18→H-16 · R2B-19/20→H-06 · R2B-21→H-15 · R2B-22→H-02 · R2B-23→C-01 · R2B-24→M-19 · R2B-25→M-17 · R2B-26/27→H-14 · R2B-28/29/30→H-17 · R2B-31→C-06 · R2B-32→H-08 · R2B-33/34→H-33 · R2B-35→H-19 · R2B-36→M-18 · R2B-37→M-22 · R2B-38→H-31 · R2B-39→L-16 · R2F-01/02→H-12 · R2F-03/04/05→H-21 · R2F-06→M-10 · R2F-07→M-08 · R2F-08→H-05 · R2F-09→L-02 · R2F-10→H-20 · R2F-11→H-21 · R2F-12→L-10 · R2F-13→M-24 · R2F-14→H-21 · R2F-15→L-11 · R2F-16/17/19→M-16 · R2F-18→M-13 · R2F-20→H-08 · R2F-21→H-34 · R2F-22→H-05 · R2F-23→M-13 · R2F-24→H-34 · R2F-25/26→M-14 · R2F-27→M-13.
