# TMS Redesign — Current-State Analysis & Gap Report (2026-08-11)

Verified against the repository on branch `nav-redesign` (HEAD `39b5763`). This is the PART BX
deliverable of the master redesign specification: analysis first, implementation after review.

---

## 1. Current architecture understanding

Modular monolith: `TransportationService.Api` (.NET, 43 modules under `Modules/<Domain>/{Entities,Configurations,Services,Controllers,Dtos}`, ~141 EF migrations, PostgreSQL prod / SQLite tests) + `TransportationService.Web` (React 19, Vite, 671 files, no UI library, hand-rolled CSS, 164 test files) + `TransportationService.Api.Tests`.

Core facts that shape the redesign:

- **`TransportOrder` is the operational center** (`Modules/Orders/Entities/TransportOrder.cs`), with two independent status tracks: operational `TransportOrderStatus` (Draft→Confirmed→Planned→InProgress→Completed→Invoiced, + Submitted/Cancelled, with an audited corrective-transition map) and commercial `OrderPricingStatus` (Draft→Reviewed→Locked→Invoiced) living on `TransportOrderPricingSnapshot`. Status history is written by an EF interceptor regardless of write path.
- **A `TransportDossier` already exists** (`Modules/Dossiers`) — but as a *thin optional annotation*: DOS-number, title, customer, Open/Closed, many-to-many `DossierOrder` links, dossier↔dossier relations, incident attachment, financial rollup. It has no activities, goods, pricing, or operational state. Ownership currently points Order→Dossier; the spec inverts this.
- **Stops** carry a mature location-snapshot pattern (frozen contact/opening-hours/gate/access-code copies, explicit refresh, audited) and four time-window pairs plus a commercial `StopTimeRequirementKind`. Stops are **wholesale rebuilt on every order update** and the order graph has **no optimistic-concurrency token** (Trips and DockAppointments do: explicit `Guid Version`).
- **Goods** are already soft-required: quantity+unit OR a cargo line OR a free-text description suffices. Loading+unloading stops are only required at *Confirm*, not create.
- **Commercial/physical split exists**: `CargoItem` (commercial quantity line) vs `Package` (scannable handling unit with barcode registry, internal Crockford-base32 barcodes, external customer barcodes, relabel-with-history).
- **Scan pipeline is event-sourced and mature**: append-only `PackageEvent` (30 types) + `ScanEvent` ledger, `PackageLifecycleMachine` (19 states incl. the full return chain `DeliveryFailed/Refused → ReturnPending → ReturnLoaded → ReturnedToDepot → RedeliveryPlanned`), idempotent via `ClientEventId`, deduped exception creation. All scanning is **trip+stop scoped** — no standalone warehouse scan.
- **Planning**: drag-drop board (`planning-center`), 22-code conflict engine with tenant-configurable severities and audited overrides, version-token 409-rebase protocol. **No tour proposals, no zone grouping** (pricing has `PricingZone` postcode ranges; planning never reads them). `Trip.TripDate` is a single DateOnly.
- **Driver app**: stop state machine with arrival-bridge, mandatory reasons for failure/skip, per-user offline action queue, immutable versioned POD (signature, photos, frozen package summary, corrections as new versions). Actual arrival/departure/waiting durations are captured but feed nothing back into estimates.
- **ETA module exists** but the registered provider is `NoRouteEstimationProvider` — production ETA is a flat 30-minute heuristic; customers see an ETA only via a one-shot "Late" outbox message; the portal exposes no delivery window.
- **Pricing engine is powerful** (`Modules/Tarification/Services/PricingEngine.cs`, 1660 lines): 11 rule bases, multi-dimension brackets, shared/derived agreements with modifiers, degression, scheduled adjustments, min/max at three levels, service options with conditions, coverage computation (Full/Partial/None per goods line) frozen as `CoverageJson`, confirm gate requiring `orders.confirm_incomplete_price` + reason for incomplete coverage, per-line merge-on-recalc with `LineKey` and adjust-reasons, Excel round-trip.
- **Invoicing**: multi-order invoices, per-legal-entity numbering sequences, credit notes, ledger + VAT snapshot freeze at Send, accounting export blocked on unfrozen lines, full provider-neutral Peppol stack (UBL BIS 3.0, dispatcher, HMAC-authenticated webhooks, incoming queue). Readiness is implicit (= order Completed & not invoiced); no exception workspace.
- **Cross-cutting**: 252 permissions (catalog `PermissionCodes.cs`, role templates v25, idempotent seeder), ~329 audit call sites with human-readable history projections and write-time masking, three-layer tenant isolation (global query filter + per-query scoping + `TenantReferenceGuard`), outbox messaging with per-customer/language template resolution, notification rules catalog, trilingual customer portal (NL/FR/EN) while the internal app is deliberately Dutch-only with no i18n keys.

## 2. Existing features that already solve parts of the specification

| Spec part | Already implemented |
|---|---|
| C (incomplete info) | Goods soft-requirement; stops only checked at Confirm; portal orders land as `Submitted` for review |
| E (four concepts) | CargoItem vs Package split is exactly "goods vs handling units"; sales lines (`TransportOrderPricingLine`/`ServiceLine`) are separate entities |
| B (inbound channels) | EDI (payload + SHA-256 hash retained, dedupe, dead-letter + replay), portal, manual all converge on the **same** `TransportOrderService.CreateAsync` |
| O (entities) | `LegalEntity` with VAT/IBAN/sequences/Peppol identity; `Customer.DefaultLegalEntityId`; resolution chain request→customer→tenant default |
| Q (locations) | Full operational location master + frozen stop snapshots with audited refresh |
| AD/AE (coverage) | Coverage per goods line + confirm gate + reason-required incomplete confirmation; technical Draft/Reviewed/Locked already masked in UI labels |
| AF (override) | Whole-order override (permission + reason + snapshot of original) and per-line adjust with frozen originals; both audited |
| AH/AI (handling units, events) | Package + barcode registry + append-only event history + lifecycle projection |
| AX (return detail) | `ReturnPending` (still on vehicle) vs `ReturnedToDepot` (physically scanned) — exactly the spec's required distinction |
| AT (POD) | Immutable versioned POD with signature/photo/frozen summaries, portal-visible |
| AS (driver UX) | Mobile driver shell, big-action stop workflow, offline queue, idempotent replays |
| X (GL snapshots) | Ledger + VAT category frozen at Send; export refuses unfrozen lines |
| BG (invoice prep) | Multi-order selection per customer, PO resolution, credit notes |
| BL/BM | Deep audit + 252-permission catalog with versioned role templates |
| BK (notification prefs) | Tenant `NotificationRule` + per-customer narrowing overrides + language chain in outbox |

## 3. Conflicting / duplicate current concepts

1. **"Dossier" name collision**: `TransportDossier` (commercial case) vs HR "personeelsdossier" (employee-file completeness, `EmployeeCompletenessService` + HR reminder settings). Zero coupling; UI language must disambiguate.
2. **`ServiceOption` ≠ activity**: it is a pricing supplement (surcharge kinds), yet is the closest thing to "services". Storage today *is* a `SurchargeKind` (PerDay/PerPalletDay) — an activity modeled as a billing artifact.
3. **Two unit-price vocabularies**: `PriceRuleBasis` (11 values) and `SurchargeKind` (11 values) overlap (PerStop, PerKg/PerTon, PerLdm, PerM3, Hourly/PerHour); `SurchargeKind` still lives in legacy `RateCard.cs`.
4. **Two problem models**: `ExecutionException` (trip-anchored, photos, CustomerActionRequired) vs `Incident` (free-floating, costs). Neither has responsibility attribution; neither feeds charges.
5. **"Inventory" means employee-issued items** (`Modules/Employees`, `/inventory`, `inventory.*` permissions) — not cargo, not customer storage. New storage features must not reuse these names.
6. **Five planning surfaces**: `/planning`, `/planning-center`, `/operations`, `/employee-planning`, `/dock-planning`; plus `TripPlanningEntry` which is an HR board projection, not a planning entity.
7. **Read/edit IA split**: orders, customers, employees present two unrelated layouts (stacked read page vs `SectionedForm` edit; tab labels ≠ section labels). Two tab primitives (`Tabs` vs `SectionNav`).
8. **Dual-maintained fields**: `PurchaseOrderRequired` bool vs `PurchaseOrderPolicy` enum; `Proposed` bool vs `Kind` on price lines.
9. **`/warehouse` (loading floor) vs `/warehouses` (facility master)** one nav line apart; "portaal" used for both employee self-service and customer portal.

## 4. Data-model gaps

1. **No operational activity model.** No ActivityType entity; crane = boolean `CraneRequired`; "plateau" appears nowhere; distribution is implied; storage is a surcharge. → new `ActivityType` (tenant-config, capability flags) + `DossierActivity`.
2. **Dossier is not a container.** `TransportDossier` lacks customer-mandatory, billing entity, activities, readiness. Must be *evolved* (not replaced) into the case container; orders become the transport-activity execution record beneath it.
3. **No sales-code layer**: `ServiceOption`/`PriceRule`/`PricingAgreement` carry no `SalesCategoryId`/VAT/GL; attribution happens only at invoice-line creation from 3 hardcoded `SalesCategorySystemRole` values.
4. **No customer allowed-entities list**; no override permission/audit for entity choice on order/dossier.
5. **Invoice can mix legal entities** — `InvoiceService.CreateAsync` never reads `TransportOrder.LegalEntityId`. Correctness bug; fix early.
6. **No invoice-readiness state, no grouping preference** on Customer, no exception workspace concept.
7. **No document strategy**: `TransportOrderDocumentType` enum exists (CustomerDeliveryNote/DeliveryNote/Cmr/Other) but is upload-only — no generation, no per-customer/activity defaults, no batch runs, no printed/generated tracking.
8. **No warehouse storage locations** (no zone/area/position; `Package` has no location FK), **no inbound receipt scan type** (`ScanType` = Load/Unload/Return/Depot only), **no storage clock** (PerPalletDay quantities hand-typed).
9. **No responsibility, charge rules, or redelivery generation**: redelivery is a package status flip; no follow-up stop/order is created; problems never touch invoicing.
10. **Pricing inputs missing on orders**: no `DistanceKm`/`LoadingMeters` fields → `PerKm`/`PerLoadingMeter` bases are unreachable from real orders; zone is delivery-side only (no O/D matrix); no holiday calendar.
11. **Coverage is a JSON blob** (`CoverageJson`) — not queryable/filterable; no stale-price flag (Locked prices refuse edits; Draft prices silently recalc).
12. **No planning-readiness gate** (badges are advisory), no tour proposal inputs (zones unused by planning), single-day `Trip.TripDate`.
13. **Localization**: `Customer.InvoiceLanguageCode` is write-only; invoice PDF hardcoded Dutch; built-in message templates Dutch-only; messaging has no attachment support (no e-mail invoice delivery despite `InvoiceEmail`/`PeppolDeliveryPreference` fields).
14. **No version tokens on the order/dossier graph** (house pattern exists on Trip/DockAppointment).
15. **ETA**: no real route provider; portal DTOs carry no delivery window; actuals never feed estimates.

## 5. UX gaps (PART BV format)

| # | CURRENT | WHY CONFUSING | TARGET | APPROACH |
|---|---|---|---|---|
| 1 | `TransportOrderForm.tsx` — 2,616 lines, 77 fields, 7 sections, 2 pre-created stops × ~25 fields, 13 sequential blocking errors rendered as one string (10 of them don't navigate to the offending section) | All flexibility at once; errors point nowhere; portal proves 22 fields suffice | Seconds-fast dossier create (customer/date/reference/template) + progressive disclosure on the dossier page | New create screen; move stop timing quadruple + advanced goods behind "Advanced"; adopt `ValidationSummary`+`firstSectionWithError` pattern from CustomerForm |
| 2 | Order read page = 9 stacked panels + up to 8 header buttons + 10 modals; edit swaps to a different 7-section layout | Same entity, two IAs; technical price-lifecycle buttons primary | One dossier page: header (status pair, primary actions) + activity cards + sections; price = total + state + one "Details" | Redesign detail page around activities; demote Herberekenen/lifecycle buttons to an actions menu |
| 3 | Sidebar: 11 modules, ~72 leaf entries, with an in-menu search box | Menu needs a search box to be usable | Role-shaped nav: Vandaag (exceptions), Dossiers, Planning, Magazijn, Facturatie, Stamgegevens/Beheer | Rework `navConfig.ts`; fold 5 planning surfaces; keep deep pages reachable, not top-level |
| 4 | Dashboard: 26 KPI tiles for every role | No prioritization; not exception-driven | PART BB "TODAY" exception dashboard (counts → worklists) | New dashboard fed by readiness/exception services |
| 5 | VehicleDetail 12 tabs, Trailer 9, Employee 9+10, Customer 7+7 mismatched read/edit labels | Tab overload; labels shift per permissions | Grouped sections, shared asset shell for vehicle/trailer, aligned read/edit | Consolidate on `SectionedForm`-style IA for read too |
| 6 | Raw enum leaks: `DossierDetailPage.tsx:288`, `DriverIncidentsPage.tsx:133`, `CustomerPortalOrderDetailPage.tsx:121` (customer-facing), `DriverProfilePanel.tsx:396`, 2 adjustment panels | English database language in NL UI, even for customers/drivers | Dutch labels everywhere | Reuse existing `*_STATUS_LABELS` maps — six one-line fixes |
| 7 | `FormField` inputs 15px in an 18px document; no spacing/control tokens | The literal "small fields" complaint | Consistent larger controls | Single-file `FormField.css` + tokens in `global.css` |
| 8 | Tarieven tab stacks 5 self-saving panels (incl. 1,313-line grid) on customer detail | Densest screen in product shown to non-pricing users | Pricing summary + link into Prijzen module | Move engine internals one click deeper |

## 6. Pricing gaps (delta only — engine is kept)

Generalize, do not rewrite: (a) sales-code/GL/VAT layer on pricing objects with snapshot-at-invoice; (b) order-level `DistanceKm`/`LoadingMeters` inputs so PerKm/PerLdm fire; (c) origin zone / O-D dimension; (d) storage bases fed by movement data (Wave 5); (e) typed, queryable coverage status on the order (keep `CoverageJson` for detail); (f) stale-price invalidation flag when pricing inputs change post-confirm (today: refuse-or-silent-recalc); (g) Maut as a sales-side concept (exists only as trip cost); (h) holiday calendar for time surcharges; (i) localized invoice descriptions.

## 7. Warehouse/scanning gaps (delta only — pipeline is kept)

(a) `WarehouseLocation` hierarchy (warehouse→zone→position, configurable, simple); (b) `Package.CurrentWarehouseLocationId` projection + location on events; (c) new scan types `Received`/`Moved`/`Staged` and a **standalone warehouse scan endpoint** (not trip-scoped) reusing `ScanService`/`PackageScanProcessor` — never fork the pipeline; (d) warehouse trace answers ("where is X", "what should have left", "what waits for tomorrow"); (e) return check-in without trip context; (f) storage clock: IN/OUT movement rows per handling unit driving pallet-day/month computation into pricing.

## 8. Invoicing gaps

(a) Entity-mixing validation (bug fix); (b) allowed-entities per customer + audited override; (c) invoice-readiness status computed from operational completion + coverage + POD + open problems; (d) invoice-control exception workspace; (e) grouping preferences (per dossier/weekly/monthly/by reference) + proposal engine; (f) invoice language actually consumed (PDF templates, line descriptions, UBL); (g) e-mail invoice delivery with attachment support in messaging; (h) document generation (delivery note/CMR) + batch runs; (i) charge lines originating from problems (approval-gated).

## 9. Migration risks

1. **Parallel-system trap**: the spec forbids DossiersV2. Mitigation: *evolve* `Modules/Dossiers` (add billing entity, activities, readiness) and keep `TransportOrder` as the transport-activity execution record; auto-create a wrapping dossier for existing/new plain orders (additive backfill migration).
2. **Stops wholesale-rebuild + no version tokens**: adding activities on top of last-write-wins editing risks silent loss; introduce `Guid Version` on order/dossier using the proven Trip pattern before deep UI redesign.
3. **Status-model coupling**: planning, EDI outbound, packages, invoicing all key off `TransportOrderStatus`; readiness states must be *additive* projections, not new enum values, to keep EDI/API compatibility.
4. **Historical stability**: pricing snapshots, invoice/ledger snapshots, POD versions, audit are all frozen-by-design — activity/sales-code additions must never rewrite them (follow the snapshot-at-creation house rule).
5. **Enum extension safety**: order/package enums stored as strings (append-safe); `PackageLifecycleMachine`/`StopStatusMachine` adjacency tables must be extended deliberately with tests.
6. **Terminology switch to "Dossier"** across a Dutch-only, key-less frontend (~600 files of literals): confine Wave 1 renames to nav + order/dossier surfaces; don't attempt a global sweep.
7. **Permission catalog**: reuse `dossiers.view/manage`, `orders.*`, `warehouse.*`, `scanning.*`; new codes only for genuinely new capabilities (e.g. `dossiers.override_entity`, `problems.approve_charge`) via role-template version bumps (next: v26).
8. **Pending migrations**: memory records several past waves with migrations "not yet applied" — verify actual DB state before adding new ones.
9. **`TransportOrderService.cs` is a 3,029-line monolith** — activity logic must land in new services (`DossierReadinessService` etc.), not grow this file.

## 10. Recommended waves (adjusted from PART BS)

- **Wave 0 (small, immediate)**: invoice entity-mixing validation bug fix; the six enum-leak label fixes; `FormField.css` sizing. Low risk, high trust-building.
- **Wave 1 — Dossier foundation + UX**: ActivityType config (tenant-managed, capability flags) + `DossierActivity`; evolve `TransportDossier` (billing entity, template-based fast create); order↔activity compatibility bridge; fast-create screen; redesigned dossier page with activity cards; nav restructure; version tokens on dossier/order.
- **Wave 2 — Commercial foundation**: sales codes + GL/VAT on pricing objects with invoice snapshotting; allowed billing entities + audited override; invoice language consumed; grouping preferences; typed coverage status + invalidation flag.
- **Wave 3 — Pricing generalization**: order distance/ldm inputs; O/D zone dimension; Maut sales side; holiday calendar; keep engine behavior under golden tests.
- **Wave 4 — Warehouse locations + standalone scanning**: location hierarchy, Received/Moved scan types, warehouse scan surface, trace page, trip-less return check-in.
- **Wave 5 — Storage**: movement-based storage clock, pallet-day/month derivation into pricing, handling IN/OUT auto-sales.
- **Wave 6 — Problems + redelivery** (driver/POD already strong, so the old Wave 6 shrinks into this): responsibility attribution, problem workflow statuses, charge decision rules (auto/propose/never + audit), linked redelivery creation, unify Exception/Incident UX.
- **Wave 7 — Distribution planning**: planning-readiness service, zone reuse for grouping, tour proposal heuristic (transparent, constraint-explaining), multi-day consideration.
- **Wave 8 — ETA + communication**: real route provider seam usage, historical stop-duration estimates, customer-window messaging with thresholds, localized templates, portal ETA.
- **Wave 9 — Documents**: document strategy config, delivery-note/CMR generation, batch generation workspace.
- **Wave 10 — Invoice control**: readiness engine, exception workspace, invoice proposals honoring grouping, correction flows.
- **Wave 11 — Portal**: request creation on dossier model, tracking + ETA + POD, notification preferences surface.

(Documents moved after problems/planning because batch generation depends on document strategy + planning readiness; driver wave absorbed into problems wave since POD/driver flows already exist.)

## 11. Wave 1 — expected files/modules to change

Backend — evolve, no new parallel modules:
- `Modules/Dossiers/`: `Entities/TransportDossier.cs` (+`DossierActivity.cs`, `ActivityType.cs` new files), `Configurations/`, `Services/DossierService.cs` (+ new `DossierReadinessService.cs`), `Controllers/DossiersController.cs`, `Dtos/DossierDtos.cs`
- `Modules/Orders/`: `Entities/TransportOrder.cs` (Version token, DossierId adoption), `Services/TransportOrderService.cs` (create-path bridge: order create → ensure dossier + transport activity), `Configurations/TransportOrderConfiguration.cs`, `Controllers/TransportOrdersController.cs`
- `Modules/Identity/PermissionCodes.cs`, `Data/DefaultRoleUpgrades.cs` (v26), `Data/DefaultRoleDefinitions.cs`
- `Data/TransportationDbContext.cs`, new additive migrations (ActivityTypes, DossierActivities, dossier columns, backfill dossiers for existing orders, Version tokens)
- Seed: activity-type defaults for reference tenant via settings/master-data seeder

Frontend:
- `features/dossiers/`: new `NewDossierPage.tsx` (fast create + templates), redesigned `DossierDetailPage.tsx` (header + activity cards + sections), `api/`
- `features/transport-orders/`: `TransportOrderForm.tsx` (progressive disclosure pass), `TransportOrderDetailPage.tsx` (integration with dossier context), `types.ts`
- `components/layout/nav/navConfig.ts` (+ tests), `routes/AppRoutes.tsx`
- New settings page for activity types under `/settings` or master-data registry (`features/master-data/lookupRegistry.ts` or dedicated page)

## 12. Wave 1 — tests required

Backend (`Api.Tests`):
- ActivityType CRUD + tenant isolation + permission guards; capability-flag validation
- Dossier fast-create: minimal input (customer+date), template applies default activities, billing entity inheritance chain, DOS numbering unchanged
- Backfill/bridge: existing orders remain valid; creating an order without a dossier auto-wraps; timeline/status history untouched
- Readiness checks: draft dossier with missing goods/location is valid but reports readiness gaps
- Version-token 409 behavior on dossier/order mutations (mirror `TripService` tests)
- Regression: full existing Orders suite (confirmation gate, pricing merge, EDI create path, portal submit) stays green
- Role seeder v26 upgrade test (pattern: `DefaultRoleSeederTests.Version25_...`)

Frontend (Vitest):
- New dossier create page: create with only customer+date; template selection
- Dossier detail: activity cards render per capability flags; only relevant sections shown
- Nav config test update (`navConfig.test.ts`); sectioned-form regression tests for the order form pass
- Enum-label regression for the touched pages

Verification per phase: `dotnet test`, `npm test`, `npm run typecheck` (tsc), `npm run lint`, `npm run build` — plus the fresh-DB smoke pattern used in previous waves.

---

## PART BU condensed matrix

| Requirement | Existing implementation | Module/entity | Reusable? | Change required | Migration? | Risk | Wave |
|---|---|---|---|---|---|---|---|
| Dossier as central case | Thin `TransportDossier` + `DossierOrder` | Dossiers | Yes — evolve | Container semantics, billing entity, fast create, backfill | Yes (additive) | Medium | 1 |
| Activity model | None (booleans, ServiceOption is pricing-only) | — | New | `ActivityType` + `DossierActivity` + capability flags | Yes | Medium | 1 |
| Templates | `?template={orderId}` prefill only | transport-orders FE | Partial | Dossier templates = default activities + defaults | Yes | Low | 1 |
| Incomplete-info creation | Goods soft-required; stops checked at Confirm | Orders | Yes | Move remaining blockers to readiness checks | No | Low | 1 |
| Sales codes + GL | `SalesCategory` (3 system roles), invoice-time only | Accounting/Invoicing | Partial | Code layer on ServiceOption/PriceRule + snapshot | Yes | Medium | 2 |
| Legal-entity safety | Default entity + sequences; **no mixing validation** | Organization/Invoicing | Yes | Allowed list, override perm, invoice validation | Yes | Low | 0/2 |
| Customer language output | Fields exist, unread; Dutch PDFs/templates | Partners/Invoicing/Messaging | Partial | Template localization + consume `InvoiceLanguageCode` | No | Medium | 2/8 |
| Pricing dimensions | 11 bases, zones (delivery-only), brackets, modifiers | Tarification | Yes | O/D zones, km/ldm inputs, storage bases, invalidation | Yes | Medium | 3 |
| Price coverage | Full/Partial/None + confirm gate (JSON blob) | Tarification/Orders | Yes | Typed queryable status + stale flag | Yes | Low | 2 |
| Handling units + scan | Package/event pipeline, lifecycle machine | Packages/Scanning | Yes | Location projection, new scan types, standalone surface | Yes | Medium | 4 |
| Warehouse locations | Warehouse→Dock only (scheduling) | Warehousing | Partial | Storage hierarchy + move scans | Yes | Medium | 4 |
| Storage clock | Manual PerPalletDay entry | Tarification | Partial | Movement-based accrual feeding pricing | Yes | High | 5 |
| Driver + POD | Complete (offline, versioned POD) | Planning/Pod | Yes | Minor polish only | No | Low | 6 |
| Problems + responsibility | 2 models, no responsibility/charges | Exceptions/Incidents | Partial | Responsibility, charge rules, redelivery creation | Yes | Medium | 6 |
| Tour proposals | None; zones unused by planning | Planning (+Tarification zones) | Partial | Readiness svc + proposal heuristic | Yes | High | 7 |
| ETA | Heuristic 30-min, provider seam ready | Eta | Yes | Provider + actuals feedback + customer windows | No | Medium | 8 |
| Batch documents | None (upload-only types) | Orders | New | Strategy config + generation + batch runs | Yes | Medium | 9 |
| Invoice control | Implicit readiness, manual grouping | Invoicing | Partial | Readiness engine + workspace + proposals | Yes | Medium | 10 |
| Portal | Track/submit/docs/invoices, no ETA | CustomerPortal | Yes | ETA, dossier model, preferences | No | Low | 11 |
