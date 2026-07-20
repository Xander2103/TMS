# Operational Enterprise Wave — Implementation Plan

> **STATUS 2026-07-21: COMPLETE.** All phases A–J implemented, tested and committed
> (ca8f745 … see git log). Documentation: docs/operations-architecture.md,
> planning-center.md, driver-app.md, profitability.md, warehouse-dock-planning.md,
> permission-matrix-operations.md.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This plan is executed in-session by the authoring agent; interface contracts are specified exactly, step-level code is authored at implementation time following the conventions locked in §Architecture Decisions.

**Goal:** Turn the administrative TMS into a daily operations platform: Dispatcher/Planning Center, Operation Control Center, mobile Driver App, Profitability, Warehouse/Dock Planning and shared productivity UX — all on the existing architecture.

**Architecture:** Modular monolith (controller → service → DbContext), hand-rolled services, custom `[RequirePermission]`, tenant scoping via explicit `TenantId` predicates, additive EF migrations. New capability is added as read models + targeted commands over existing aggregates (Trip, TransportOrder, Package/Scan, POD, Incident, TripCosting, Eta) — no parallel domains.

**Tech Stack:** .NET 10, EF Core + Npgsql, PostgreSQL 16, React 19 + react-router 7 + Vite 8 (no new runtime deps), Vitest, xUnit + in-memory SQLite.

## Global Constraints

- Preserve existing architecture; no rewrites, no duplicate business logic, no parallel order/trip/package/scan/incident/document/financial models.
- Backend is source of truth: all planning, conflict, scan, profitability and warehouse rules enforced server-side.
- Additive migrations only; never edit historical migrations; never reset the DB.
- Tenant scoping: every new entity inherits `AuditableTenantEntity`, sets `TenantId` explicitly, queries via `.Where(x => x.TenantId == _tenantContext.TenantId)`, config adds `HasQueryFilter(x => !x.IsDeleted)` + TenantId-first indexes.
- Permissions: constants in `PermissionCodes` (+ `All` tuple with Dutch description), enforced with `[RequirePermission]` (any-of), role templates evolved via `DefaultRoleUpgrades` versioned step (v8) + `DefaultRoleDefinitions` update for new tenants.
- Audit via `IAuditService.RecordAsync` with purpose-built anonymous objects (never secrets/tokens).
- Errors: `DomainValidationException` → ProblemDetails; business outcomes via per-module result records; Dutch user-facing messages.
- Frontend: no new runtime dependencies; lazy routes via the `lazyPage` idiom in `src/routes/AppRoutes.tsx`; colocated plain CSS with existing custom-property theme; request-key fetch idiom; ProblemDetails via `describeApiError`/`extractFieldErrors`.
- Verification gate after every phase: `dotnet build` (0 warnings), `dotnet test`, `npx tsc -b`, `npm run lint`, `npm test`, `npm run build` (no chunk > 500 kB warning).
- Do not reduce existing test coverage.

---

## Architecture Decisions (locked)

1. **Structured conflicts — extend, don't replace.** `PlanningConflictDto` gains optional fields: `Category` (new enum `ConflictCategory { Resource, Availability, Qualification, Capacity, Timing, Equipment, Document, Data }`), `RelatedEntityType` (string), `RelatedEntityId` (Guid?), `OverrideAllowed` (bool), `RequiredPermission` (string?), `SuggestedAction` (string?). Existing `Code`/`Blocking`/`Description`/`Severity` untouched; `ConflictSeverity` keeps its 3 values (spec's "MissingData" maps to `Information` + `Category=Data`; "Overrideable" is the `OverrideAllowed` flag, not a severity). All rules in `PlanningConflictService` are annotated in place.
2. **Conflict overrides become persistent.** New entity `ConflictOverride : AuditableTenantEntity` (`EntityType`, `EntityId`, `ConflictCodes` (csv), `Reason`, `OccurredAt`; actor comes from CreatedByUserId). Written inside the same SaveChanges as the trip status change when `Override=true` (reason becomes mandatory), surfaced in trip timeline. Permission stays `planning.override_restriction`.
3. **Optimistic concurrency: explicit `Guid Version` token** on `Trip` and `DockAppointment` (`IsConcurrencyToken`, service bumps on every mutation, mutation requests carry `Version`; mismatch → 409 with fresh state). Works identically on Npgsql and the SQLite test harness (`xmin` would not).
4. **Operational alerts = deduped projection.** `OperationalAlert : AuditableTenantEntity` (`Severity` (`AlertSeverity { Information, Warning, Critical }`), `Category` (string), `Source`, `Title`, `Message`, `LinkPath`, `RelatedEntityType/Id`, `DedupeKey`, `Status` (`AlertStatus { Active, Acknowledged, Resolved }`), `AcknowledgedByUserId/At`, `ResolvedByUserId/At`, `AssignedUserId`). `AlertSyncService.SyncAsync` recomputes alert conditions (delayed trips via StopEta, missing POD, open critical incidents/exceptions, overdue maintenance/inspections, expiring documents) and UPSERTS by `(TenantId, DedupeKey)` — refresh never duplicates; conditions gone → auto-resolve. Invoked inline by the operations overview endpoint (cheap, bounded, idempotent).
5. **Realtime = controlled polling.** No SignalR exists; the SPA already polls unread counts. Operations page polls its projection every 30 s with the request-key idiom; recovery = the same full projection fetch. Documented as deliberate.
6. **Location model without fake GPS.** `TripPositionDto` (`Source` enum `LocationSource { LiveGps, LastKnownGps, ScanLocation, StopLocation, PlannedLocation, Unavailable }`, `Latitude/Longitude?`, `Timestamp?`, `Description`). Resolver order: latest `PackageEvent`/POD GPS (ScanLocation) → last completed stop's location coords (StopLocation) → next planned stop coords (PlannedLocation) → `Unavailable`. LiveGps/LastKnownGps reserved for future telematics; never synthesized.
7. **ETA reuses `Modules/Eta` as-is** (StopEta + heuristic + `DispatcherOverride` + history). Operations exposes planned vs `CurrentEta` vs actual + `Source` + `Status`; manual override stays visibly `DispatcherOverride`.
8. **Idempotency for driver/offline actions**: reuse per-aggregate keys, not a generic middleware. Scans already have `ScanEvent.ClientEventId`. Stop-status transitions, POD finalize and driver incident create gain `Guid? ClientRequestId` persisted on their target rows (`StopStatusHistory.ClientRequestId`, `ProofOfDelivery.ClientRequestId`, `Incident.ClientRequestId`) with filtered unique index per tenant; replay returns current state as success.
9. **Favorites/recents/pins = one table.** `UserResourceLink : AuditableTenantEntity` (`UserId`, `Kind` enum `{ Favorite, Recent, Pinned }`, `EntityType` (string, closed catalog), `EntityId`, `Label` cache (display only, refreshed on touch), `Route`, `SortOrder`, `TouchedAt`). Unique `(TenantId, UserId, Kind, EntityType, EntityId)`. Self-scoped endpoints (`/api/me/resource-links`), auth-only like `MeController` — no new permission needed; permission recheck happens because display resolves via the search-hit style permission-gated lookup, and dangling links are dropped server-side.
10. **Driver app lives in the existing SPA** under `/driver` with its own mobile-first layout shell (`DriverLayout`), reusing auth, apiClient, ScanPanel, scanQueue. Offline: generalize `scanQueue` into `actionQueue` (localStorage, per-user namespaced key `ts.actionQueue.v1.<userId>`, cleared on logout) for stop transitions, POD, incidents, message acks; every queued action carries `clientRequestId`.
11. **Profitability = read models over existing financial data.** New `Modules/Profitability` with `ProfitabilityQueryService`: revenue split (Agreed = `TransportOrder.AgreedPrice`; RateCard = tarification calc; Invoiced = invoice lines by `TransportOrderId`; Paid = invoiced where `Invoice.Status == Paid`), costs from `TripCostLine` (phase → Actual/Estimated, source → Allocated for rate-based lines), `Missing` flags when a cost type has no line. Corrections reuse `TripCostType.Manual/Correction` lines through `TripCostingService` (already audited + permissioned `trip_costs.override`); no invoice mutation ever. Groupings: trip, order, customer, driver, vehicle, period — all server-side, bounded by date range (≤ 366 days).
12. **Warehousing = new module, Location reused.** `Modules/Warehousing`: `Warehouse` (LocationId FK — no address duplication, `IsActive`, `OpeningTime/ClosingTime` per weekday json — simple `OpeningHours` string per existing Location convention + structured `OpensAt/ClosesAt` TimeOnly pair, contact fields), `Dock` (WarehouseId, Code, Name, `OperationTypes` flags (Loading/Unloading), `AllowsAdr`, `Refrigerated`, `MaxVehicleLengthM/HeightM`, `IsActive`, Notes), `DockAppointment` (WarehouseId, DockId?, TripId?, TransportOrderId?, VehicleId?, TrailerId?, DriverId?, `OperationType`, `PlannedStart/End`, `ArrivedAt/StartedAt/CompletedAt`, `Status` enum `{ Planned, Expected, Arrived, Waiting, AssignedToDock, InProgress, Completed, Cancelled, NoShow }`, Priority, Remarks, `Version` token). Transition machine `DockAppointmentStatusMachine` (static map, same style as `StopStatusMachine`). Warehouse scans reuse the one-scan-pipeline; the dashboard derives scan progress from existing `ScanEvent`/`PackageEvent` data — **no second package lifecycle**.
13. **TransportOrder gains `Priority`** (`OrderPriority { Low, Normal, High, Urgent }`, default Normal, additive column) — needed by unplanned-work panel, inline edit and dock queue.
14. **Command palette/shortcuts**: client-side command registry (`src/config/commands.ts`) merged into the existing CommandPalette (permission-aware via `hasAnyPermission`), central `useShortcuts` hook + `ShortcutProvider` with single window listener, registry-driven; `?` opens help modal.
15. **Notification gap fixed**: driver notified on assign/reassign/unassign of a Planned trip and on reschedule/vehicle/trailer change (typed codes `trip_assigned`, `trip_changed`, `trip_unassigned`), deduped per save by (user, type, trip).

## New permissions (constants + catalog + v8 role step)

- `operations.view`, `operations.manage_alerts` (ack/resolve/assign)
- `warehouse.manage` (master data), `warehouse.schedule` (appointments), `warehouse.conflict_override`
- `profitability.export`
- Reused instead of new: planning.* (planning center), `orders.assign`, `planning.override_restriction` (planning conflict override), driver app = `driver_workflow.*` + `scanning.*` + `pod.*` + `exceptions.create` + `incidents.*`(create via driver endpoint uses `driver_workflow.execute`), profitability view = existing `profitability.view` + `trip_costs.view`, corrections = `trip_costs.override`, ETA override = existing Eta gating.
- Role template updates (v8): planner/dispatcher + `operations.view`, `operations.manage_alerts`, `warehouse.schedule`; management + `operations.view`, `profitability.export`; magazijn + `warehouse.manage`, `warehouse.schedule`, `warehouse.conflict_override`, `operations.view`; boekhouding + `profitability.export`.

## New migrations (all additive)

1. `OperationalFoundations`: `transport_orders.Priority` (text, default 'Normal'); `trips.Version` (uuid, default gen); `operational_alerts`; `conflict_overrides`; `user_resource_links`; `stop_status_history.ClientRequestId` + filtered unique idx; `pods.ClientRequestId` + idx; `incidents.ClientRequestId` + idx.
2. `Warehousing`: `warehouses`, `docks`, `dock_appointments` (+ indexes `(TenantId, WarehouseId, PlannedStart)`, `(TenantId, DockId, PlannedStart)`, `(TenantId, Status)`).

---

## Phase order, tasks and gates

Each phase ends: build + full backend tests + FE typecheck/lint/test/build green → commit.

### Phase A — Foundations (commit `feat(operations): shared operational foundations`)
- [ ] Conflict DTO enrichment + `ConflictCategory` + annotate all `PlanningConflictService` rules (category, related entity, override metadata). Frontend `PlanningConflict` type extended (optional fields — backward compatible).
- [ ] `ConflictOverride` entity + write path in `TripService.ChangeStatusAsync` (reason mandatory when overriding) + timeline surfacing + audit. Tests: override with/without permission, reason required, row persisted.
- [ ] `Trip.Version` concurrency: bump on every trip mutation; `UpdateTripRequest`/targeted commands carry `Version`; stale → outcome `Conflict` → HTTP 409 `{ conflicts: null, staleVersion: true, current: TripDetailDto }`. Tests: stale write rejected, fresh write bumps.
- [ ] `TransportOrder.Priority` + validation + DTOs + audit on change. Tests.
- [ ] `OperationalAlert` entity + `IAlertService` (list/ack/resolve/assign) + `AlertSyncService` (rules: trip delayed >15m via StopEta, missing POD on completed stops, open Critical incidents, open package exceptions, overdue maintenance/inspection, expiring fleet documents) with dedupe-key upsert + auto-resolve. Tests: dedupe on double sync, ack lifecycle, tenant isolation, permission filtering.
- [ ] `UserResourceLink` + `MeResourceLinksController` (`GET/PUT/DELETE /api/me/resource-links`, `POST /api/me/resource-links/touch` for recents; favorites/pins ordered). Tests: user-scoped, tenant-scoped, recents capped (25), dangling links dropped.
- [ ] Idempotency columns (`ClientRequestId`) on StopStatusHistory/ProofOfDelivery/Incident + replay short-circuits in `TripExecutionService`, `PodService`, `IncidentService`. Tests: duplicate ClientRequestId returns original result, no double rows.
- [ ] Migration `OperationalFoundations`; inspect generated SQL.
- [ ] Permissions batch 1 (`operations.*`) + catalog + `DefaultRoleUpgrades` v8 (all new codes in one step) + `DefaultRoleDefinitions` update. Seeding test updated.

### Phase B — Planning Center backend (commit `feat(planning): planning board read models and targeted commands`)
- [ ] `PlanningBoardService` (`Modules/Planning/Services`): `GetBoardAsync(from,to)` → `PlanningBoardDto { Days, Trips: PlanningBoardTripDto[] (id, number, date, window, driver/vehicle/trailer summaries, orderCount, stopCount, weight/volume vs capacity, status, conflictCounts by severity, version) }`; `GetUnplannedOrdersAsync(filter)` → paged `UnplannedOrderDto` (number, customer, first/last stop summary, requested window, cargo totals, colli count, priority, status, warning badges: missing data / appointment required / ADR); `GetResourcesAsync(from,to)` → drivers (availability from absences+trips, licence/medical/qualification warnings via QualificationStatus, fixed vehicle), vehicles (operational status, capacity, equipment, ADR, maintenance/inspection urgency, fixed driver), trailers (same). All single-query projections, no N+1 (validated by test over ≥3 trips), bounded ≤ 31 days.
- [ ] Targeted trip commands in `TripService` (all: version-checked, conflict-validated with structured results, audited, driver-notified, planning-entry synced, costing restaged): `AssignOrdersAsync(tripId, orderIds, version)` / `RemoveOrderAsync`, `AssignDriverAsync/AssignVehicleAsync/AssignTrailerAsync(tripId, id?, version, override?, overrideReason?)`, `RescheduleAsync(tripId, tripDate, plannedStart/End, version, …)`, `ReorderOrdersAsync(tripId, orderedOrderIds, version)`. Allowed on Draft + Planned (Planned requires re-validation; blocking conflicts require override permission + reason → ConflictOverride row). Endpoints under `api/trips/{id}/...` with `[RequirePermission]` (`planning.edit` + `orders.assign` where orders change).
- [ ] Conflict evaluation for hypothetical assignment: `POST api/trips/{id}/validate-assignment` (body: candidate driver/vehicle/trailer/orders) → structured conflicts without mutation (drag-over feedback).
- [ ] Tests: each command happy path, double-booking block, override path, version conflict, notification emitted exactly once per affected driver, tenant isolation, no-N+1 board test, unplanned filter matrix.

### Phase C — Planning Center frontend (commit `feat(planning): /planning-center dispatcher workspace`)
- [ ] Feature `src/features/planning-center/` (api, types, components: `UnplannedPanel`, `BoardTimeline` (day/3-day/week, CSS grid timeline, trips as blocks with conflict badges), `ResourcesPanel` (tabs Chauffeurs/Voertuigen/Opleggers, pinned-first via resource-links), `TripInspector` drawer, `ConflictDialog` (structured list + override reason flow), `AssignBar`). Native HTML5 drag-and-drop with keyboard alternative (select order → "Plan in rit…" action); every drop calls backend, on rejection restores state and shows exact conflicts; only affected data refetched (request-key per zone).
- [ ] Route `/planning-center` (lazy), sidebar entry (planning.view), saved filter state in localStorage (`ts.planningCenter.filters`).
- [ ] Vitest: board time-math utils, conflict grouping meta, DnD payload encoding, filter reducer.

### Phase D — Operation Control Center (commit `feat(operations): /operations live control center`)
- [ ] `Modules/Operations`: `OperationsOverviewService.GetOverviewAsync()` → active trips (status InProgress + today's Planned) with current/next stop, position (Decision 6), ETA/delay (Eta module), latest scan, discrepancy counts, missing-POD flags; alert queue (from `AlertService`); exception/incident/maintenance summaries. `OperationsController` (`api/operations/overview`, `api/operations/alerts` + `POST alerts/{id}/acknowledge|resolve|assign`), perms `operations.view`/`operations.manage_alerts`. Alert sync inline (Decision 4).
- [ ] Frontend `/operations`: active-trip table with delay/ETA badges (source-labelled: "heuristiek"/"handmatig"), stop progress, alert queue with ack/resolve, exception panels linking to existing pages, 30 s polling, map-ready coordinates panel (list, no map lib). Tests: delay meta, alert grouping.
- [ ] Backend tests: projection correctness, delayed detection, ack permission, dedupe, tenant isolation.

### Phase E — Driver app core (commit `feat(driver): mobile driver shell and execution flow`)
- [ ] `DriverLayout` (bottom tab bar: Vandaag, Rit, Scan, Berichten, Meer) + routes `/driver`, `/driver/trip/:id`, `/driver/stops/:stopId`, `/driver/messages`, `/driver/incidents/new`, `/driver/documents`, `/driver/profile`. Reuses `TripExecutionService`/my-trips APIs; home shows current trip, next stop, ETA, vehicle/trailer, pending actions, unread count, offline status.
- [ ] Backend additions: `GET api/my/dashboard` (driver home projection), `GET api/my/documents` (scoped: fleet documents of assigned vehicle/trailer for active trips + order/stop instructions; nothing else), driver incident create endpoint `POST api/my/incidents` (`driver_workflow.execute`, links validated to own trips, ClientRequestId idempotent, dispatcher notification via `NotifyPermissionHoldersAsync(operations.view)`).
- [ ] Trip execution flow: check-in/confirm (existing status machine), loading (ScanPanel reuse), stop arrival→operation→POD→complete (existing transitions; no jumps — enforced already by `StopStatusMachine`), trip complete. Signature: reuse existing POD finalize UI parts from my-trips; photos via existing upload paths.
- [ ] Tests: own-trip access only (cross-driver blocked), status jump rejected, document scoping, incident idempotency.

### Phase F — Driver offline (commit `feat(driver): offline action queue with idempotent sync`)
- [ ] `src/features/driver/actionQueue.ts`: generalized queue (kinds: stopTransition, podFinalize, incidentCreate, messageAck) with `clientRequestId`, ordered replay, retry/backoff, per-user storage key, cleared on logout (`authStorage.clearTokens` hook), unsynced badge in DriverLayout. ScanQueue remains for scans (already idempotent).
- [ ] Cache active-trip snapshot for offline read (localStorage, per-user, size-bounded, cleared on logout). No tenant-wide caching; documents not cached (only instructions text).
- [ ] Vitest: queue ordering, replay idempotence, logout cleanup, offline reducer.

### Phase G — Profitability (commit `feat(profitability): trip/order/customer margin analysis`)
- [ ] `Modules/Profitability`: `ProfitabilityQueryService` with `GetTripProfitabilityAsync(from,to,filters)`, `GetGroupedAsync(dimension: Customer|Driver|Vehicle|Order|Period)`, `GetTripExplanationAsync(tripId)` (line-level: revenue sources, cost lines with phase/source, data-quality flags Missing/Estimated). Revenue resolver per Decision 11. Margin/9 KPIs per spec (`revenue`, cost split known/estimated/missing, margin, margin %, €/km, cost/stop, cost/colli, utilization from planned vs actual hours). XLSX export via existing ClosedXML reporting conventions (`profitability.export`).
- [ ] Frontend `/profitability`: ranking tables (trips, customers, vehicles), margin trend by week, data-quality badges ("geschat", "ontbrekend"), explanation drawer per trip, corrections deep-link to existing trip-costing page (reuse, not duplicate). Tests: margin meta, quality badges.
- [ ] Backend tests: revenue source precedence, actual vs estimated split, missing-data indicator, grouping correctness, tenant isolation, permission filtering (view vs view-sensitive: cost detail requires `trip_costs.view`).

### Phase H — Warehouse & docks (commit `feat(warehouse): warehouses, docks and dock planning`)
- [ ] Entities per Decision 12 + `DockAppointmentStatusMachine` + `WarehousePlanningService` (CRUD warehouses/docks with `warehouse.manage`; appointments with `warehouse.schedule`: create/update/move/status; validations: overlap per dock, dock type/operation compatibility, inactive dock/warehouse, outside opening hours, missing vehicle for Arrived+, insufficient duration (<15m), ADR/refrigeration compatibility; blocking overridable with `warehouse.conflict_override` + reason → ConflictOverride reuse). `Version` concurrency. Queue = appointments in Arrived/Waiting ordered by arrival.
- [ ] `WarehouseDashboardService`: today's expected/waiting/in-progress/completed/delayed/no-show, dock utilization %, scan progress per appointment (derived from ScanEvents of the linked trip/order), discrepancy counts.
- [ ] Migration `Warehousing`.
- [ ] Frontend `/warehouses` (master data CRUD, existing DataTable/form kit) + `/dock-planning` (docks-as-rows timeline, DnD reschedule calling backend, queue panel, conflict dialog reuse). Tests both sides: transitions, overlap conflict, opening hours, no-show, utilization math, tenant isolation.

### Phase I — Productivity & UX (commit `feat(ux): command palette, shortcuts, favorites and inline edit`)
- [ ] Favorites/recents/pins wired: star buttons on detail pages (customers, orders, trips, drivers, vehicles, trailers, dossiers, locations, reports), server recents recorded on detail visits (replaces localStorage recents in palette; keeps last 12 shown), pinned-first ordering in planning-center resource panel + SearchableSelect consumers.
- [ ] CommandPalette v2: command registry (navigate + create actions per spec list, permission-aware), grouped results (Commands, Favorieten, Recent, search hits), fuzzy-ish subsequence matching client-side for commands only.
- [ ] `ShortcutProvider` + `useShortcuts`: Ctrl/Cmd+K, `g p|o|d|i|f|w` sequences, `/`, `?` help dialog, Esc unified; disabled in inputs; registry-driven help. No scattered listeners (CommandPalette listener migrates in).
- [ ] Inline edit: `InlineEditField` component (loading, error, a11y, Esc/Enter) applied to: order priority (list+detail), incident responsible user, dock appointment remarks/time (via existing endpoints), trip notes. Backend validation already present; audit significant changes (priority via existing order audit).
- [ ] Vitest: registry permission filtering, shortcut sequence matcher, inline-edit component behavior.

### Phase J — Hardening + docs + final regression (commit `docs(operations): operational wave documentation` + fixes)
- [ ] Full regression: backend suite, FE typecheck/lint/test/build, bundle check (route-lazy new pages; verify no >500 kB chunk), migration idempotence check (`dotnet ef migrations script` inspect).
- [ ] Docs (in `docs/`): `operations-architecture.md` (alerts, polling, location/ETA truthfulness, conflicts/overrides, concurrency), `planning-center.md` (read models, DnD mutation flow, shortcuts), `driver-app.md` (workflow, offline/idempotency, security), `profitability.md` (calculation + actual/estimated semantics), `warehouse-dock-planning.md` (domain, transitions, conflict rules), `permission-matrix-operations.md` (new codes, role template v8, upgrade steps), update `frontend-code-splitting.md`.
- [ ] Final report per spec structure.

## Product-owner questions (defaults chosen, flagged for review)
- Continuous GPS tracking: **not implemented** (no telematics data exists; location model is scan/stop-derived). Flagged as commercial next step.
- Blocking-conflict override: **allowed** with `planning.override_restriction`/`warehouse.conflict_override` + mandatory reason + audit (existing product pattern).
- Estimated costs visible to planners: **visible only with `trip_costs.view`/`profitability.view`**; planner template does not gain cost visibility by default.
- Customers reserving dock appointments: **not exposed** to the portal in this wave.
