# Developer architecture — dossier-centric redesign (Waves 0–11 + completion wave)

*Last updated: 2026-08-12, branch `nav-redesign`. Covers commits `e07dca4` (Wave 0) … `1080dda`
(Wave 11: customer-portal POD summary + notification preferences), plus the follow-up
completion wave P0–P13 (`255a593` … `0ecad13`, §17).*

This document is the developer-facing map of what the redesign added and how the pieces fit.
Every claim below was verified against the code on this branch. It deliberately does not repeat
the functional module docs — see the cross-references per section, notably
[docs/dossiers.md](../dossiers.md), [docs/pricing.md](../pricing.md),
[docs/warehouse-scanning.md](../warehouse-scanning.md), [docs/storage.md](../storage.md),
[docs/problems.md](../problems.md), [docs/customer-portal.md](../customer-portal.md),
[docs/notifications.md](../notifications.md), [docs/permissions.md](../permissions.md),
[docs/peppol.md](../peppol.md) and [docs/security/](../security/).

All paths are relative to the repo root; the backend project is `TransportationService.Api`.

---

## 1. Dossier/activity architecture & relationship to TransportOrder

**Where:** `TransportationService.Api/Modules/Dossiers/` — entities in `Entities/TransportDossier.cs`,
`Entities/DossierActivity.cs`, `Entities/ActivityType.cs`; services `Services/DossierService.cs`,
`Services/DossierActivityService.cs`, `Services/ActivityTypeSeeder.cs`,
`Services/DossierReadinessService.cs`, `Services/DossierBackfillSeeder.cs`; mapping in
`Configurations/DossierConfigurations.cs`. Functional doc: [docs/dossiers.md](../dossiers.md).

**Model.** `TransportDossier : AuditableTenantEntity, IVersionedEntity` carries `DossierNumber`,
`Title`, `CustomerId?`, `LegalEntityId?`, `DossierDate?`, `Status` (`DossierStatus.Open|Closed`),
`OriginTransportOrderId?` and a Guid `Version` token. **Orders do not point at the dossier** —
`TransportOrder` has no `DossierId` column. The link is the join entity
`DossierOrder { DossierId, TransportOrderId }` (unique active link per pair via
`UX_dossier_orders_active_link`). A second, different pointer exists only on *wrapper* dossiers:
`TransportDossier.OriginTransportOrderId`, which doubles as the idempotency key
(`UX_transport_dossiers_origin_order`, unique where not null → at most one wrapper per order, ever).
Dossier↔dossier links use `DossierRelation` (`FollowUp|Return|Claim|Replacement|Duplicate|Other`)
with a DB check constraint against self-links.

**No order exists outside a dossier.** `TransportOrderService.CreateAsync`
(`Modules/Orders/Services/TransportOrderService.cs`, ~lines 235–349): when `DossierId` is given the
dossier must exist and be Open and a `DossierOrder` link is added; otherwise a wrapper dossier is
created in the *same* `SaveChanges` — title `"{OrderNumber} — {CustomerName}"`, plus one
`DossierActivity` typed by the tenant's system-default transport type. Order number and dossier
number are claimed together inside one `TenantNumbering.SaveWithClaimedNumberAsync` delegate, so a
counter-concurrency retry rebases both. `DossierBackfillSeeder.SyncAsync` (run at startup,
`Program.cs:637`) wraps pre-existing orders the same way, chunked at 200.

**DossierActivity is a mutable work-item, not an event log.** It represents "one piece of work
inside a dossier" (distribution, crane job, storage): `ActivityTypeId`, `Sequence`, `Label`,
`LinkedTransportOrderId?`, `LinkedActivityId?` (same-dossier accompaniment), `PlannedDate?`,
`DurationHours?`. It has no own status and deliberately duplicates nothing from `TransportOrder`
("A field that exists on TransportOrder must not be added here"). The append-only trail is the
audit log (`Modules/Auditing`), where all dossier actions record under
`EntityType = "TransportDossier"` (`Created`, `OrderLinked`, `LegalEntityChanged`,
`ActivityAdded`, …).

**ActivityType is tenant-managed catalogue data.** Behaviour is driven only by capability flags
(`HasStops`, `SupportsGoods`, `PlanningRelevant`, `WarehouseRelevant`, `AllowsDuration`,
`IsQuickStart`, `IsSystemDefaultTransport`), never by `Code` outside the seeder.
`ActivityTypeSeeder` seeds ten codes (`DISTRIBUTIE`, `DIRECT_TRANSPORT` (default), `KRAANTRANSPORT`,
`KRAANWERK`, `PLATEAU`, `OPSLAG`, `EXPRESS`, `HERLEVERING`, `POSITIONERING`, `OVERIG`) lazily
(first list/first order/backfill — no startup hook). Idempotency: existing codes read with
`IgnoreQueryFilters()` (a soft-deleted default is never resurrected), add-if-missing on `Code`,
tenant edits survive, and the default-transport flag is granted only when the tenant has no active
default yet. `UX_activity_types_default_transport` enforces exactly one active default per tenant.

**Key invariants**
- Closed dossiers are immutable (`RequireOpen` on update/link/unlink/entity change); closing
  refuses while open incidents exist.
- Activity type is immutable after creation; only `HasStops` types can own a `TransportOrder`;
  deleting an activity is blocked while its linked order is live (non-Draft/Cancelled).
- Every dossier/activity mutation bumps `dossier.Version`; a stale client token yields HTTP 409
  *carrying the current dossier body* (`DossierVersionConflictExceptionFilter`,
  `Modules/Dossiers/DossierVersionConflict.cs`). A null client token skips the check (legacy/EDI).
- `DossierReadinessService.EvaluateAsync` computes readiness issues on read, never persisted
  (codes like `route.order_missing`, `order.confirm.stops` (Blocking), `pricing.stale`).

**Extension points.** A new activity "type" is data, not code: `POST /api/activity-types` with
capability flags (permission `activity_types.manage`). Add to `ActivityTypeSeeder.Defaults` only if
it should ship to every tenant. New readiness rules go into `DossierReadinessService`.

## 2. Sales codes & commercial snapshots

**Where:** there is no `Modules/Commercial` directory — the commercial foundation (migration
`20260812001932_CommercialFoundation`) spans `Modules/Accounting` (the codes),
`Modules/Orders` (stamping + snapshots), `Modules/Invoicing` (consumption) and
`Modules/Partners` (`CustomerEntityPolicy`).

**Sales codes = `SalesCategory`** (`Modules/Accounting/Entities/LedgerAccount.cs`): `Code`, `Name`,
`SystemRole` (`None|Transport|Surcharge|Diesel` — at most one active category per non-None role),
`LedgerAccountId?` (null = unmapped → draft warning + export blocker), `InvoiceDescriptionNl`,
`DefaultUnitCode`, `VatCategoryOverride`. Nothing is hardcoded ("Transport = 700000" does not exist).

**Stamping (stage 1, at price calculation).** `TransportOrderService` (~line 2196):
price lines resolve `PriceRule.SalesCategoryId → PricingAgreement.SalesCategoryId → null`;
service lines take `ServiceOption.SalesCategoryId` only (documented as "wins over rule/agreement
codes"). The resolved id is written to `TransportOrderPricingLine.SalesCategoryId` /
`TransportOrderServiceLine.SalesCategoryId` — **deliberately without an FK** so history stays
byte-stable whatever happens to the category later (`Modules/Orders/Entities/TransportOrderPricing.cs`).

**Consumption (stage 2, at invoice-line build).** `InvoiceService.CreateAsync`
(`Modules/Invoicing/Services/InvoiceService.cs`, ~350–450): the base transport line takes the
stamped code **only when unanimous** across the order's non-informational, non-proposed lines
(`stamped is [var single]`), otherwise falls back to the `Transport` role category ("a mix stays on
the role — one aggregate line cannot represent two codes"). Service lines fall back to `Surcharge`,
diesel lines always use `Diesel`; manual lines take the caller's explicit code, whose
`DefaultUnitCode` fills a missing `UnitCode` (else `C62`). End-to-end tests:
`TransportationService.Api.Tests/Invoicing/SalesCodeResolutionTests.cs`.

**Commercial snapshots** (house rule: snapshot at creation, freeze at send):
- `TransportOrderPricingSnapshot` (one per order): tariff date, zone, agreement names, coverage
  (`CoverageStatus` Full|Partial|None|NotApplicable, worst wins), `IsStale`, and `Status`
  (`Draft→Reviewed→Locked→Invoiced`; `Invoiced` is set only by invoice generation).
- `TransportOrderPricingLine`: frozen rule/agreement names, `ActualQuantity`/`BillableQuantity`,
  `Kind` (Auto/AutoAdjusted/Manual/Proposed), `LineKey` merge key, `SalesCategoryId`.
- `Invoice` seller/customer snapshot (`SellerName`, `SellerVatNumber`, `SellerIban`,
  `CustomerVatTreatment`, `VatLegalText`, `LanguageCode`): copied at creation, refreshed while
  Draft via `ApplySnapshots`, never mutated after Sent; skipped for credit notes (they mirror the
  credited document). `InvoiceLine` ledger/VAT snapshots freeze at send
  (`FreezeLedgerSnapshotsAsync`).

**`CustomerEntityPolicy`** (`Modules/Partners/Services/CustomerEntityPolicy.cs`) — a deliberately
*static* class (no ctor changes in the four consumers) enforcing the customer ↔ issuing-legal-entity
binding via `CustomerAllowedLegalEntities`. **Empty allowed-set = no restriction** (backward
compatible). Invoked at invoice create (`InvoiceService.cs:307`), order create/update with explicit
entity (`TransportOrderService.cs:656`), dossier entity move (`DossierService.cs:414`) and customer
save (`CustomerService.cs`, which also keeps `Customer.DefaultLegalEntityId` inside a non-empty set).
One invoice = one issuing entity: mixed order entities are a validation error, never silently
switched.

**Extension point.** A new sales-code carrier level = add a nullable `SalesCategoryId` to the
carrier entity and extend `ResolveLineSalesCategory` (`TransportOrderService.cs:2196`; ordering =
precedence). A new structural fallback = new `SalesCategorySystemRole` member + a
`CategoryForRole(...)` call in `InvoiceService.CreateAsync`.

## 3. Pricing engine & generalization (zones, per-km, conditions, snapshots)

**Where:** `Modules/Tarification/Services/PricingEngine.cs` (`IPricingEngine.CalculateAsync`),
entities in `Modules/Tarification/Entities/` (`PricingZone.cs`, `TenantHoliday.cs`,
`ServiceOption.cs`), admin in `Services/PricingAdminService.cs`. Full functional model:
[docs/pricing.md](../pricing.md). Migration `20260812071320_PricingGeneralization` added exactly:
`price_rules.OriginZoneId`, table `tenant_holidays`, and `transport_orders.DistanceKm` +
`LoadingMeters`.

**Zones (origin/destination).** `PricingZone` + `PricingZoneArea` (country default `"BE"`, postal
ranges). `ResolveZoneAsync` compares numerically when code and bounds parse as int, otherwise
ordinal string comparison (handles NL `"1234 AB"`); zones scanned by `SortOrder` then `Code`, first
match wins. Destination = last unloading stop, origin = first loading stop (resolved in
`TransportOrderService` ~2046–2051). Origin match: `rule.OriginZoneId == null ||` order's origin
zone equals it.

**Rule selection** (`SelectRule`) — the load-bearing specificity score (P6 added the activity
dimension; legacy rules keep their exact relative order):

```csharp
int Score(RuleCandidate candidate) => candidate.Tier * 8
    + (candidate.Rule.ActivityTypeId is not null ? 4 : 0)
    + (candidate.Rule.ZoneId is not null ? 2 : 0)
    + (candidate.Rule.OriginZoneId is not null ? 1 : 0);
```

Tier: customer-private agreement = 2, shared+assigned = 1, company default = 0. Activity beats
both zone dimensions; destination zone beats origin zone; the max within-tier bonus (4+2+1=7)
stays below one tier step (8). An exact tie is a **blocking configuration error**, never an
arbitrary pick. (Note: [docs/pricing.md](../pricing.md) §4 still shows the pre-Wave-3 formula
without the origin `+1` and activity `+4` bonuses — see §18.)

**PerKm — two independent mechanisms**, both fed by `order.DistanceKm`:
1. `PriceRuleBasis.PerKm` order-level rules: `(BaseAmount ?? 0) + (UnitPrice ?? 0) * km`; a null
   distance emits an informational "overgeslagen (geen afstand gekend)" line instead of a silent €0.
2. `SurchargeKind.PerKm` service options (Wave 3 "Maut as sales concept"): quantity = entered
   quantity if > 0, else `DistanceKm`; explicit 0 is treated as unknown.

**Holiday conditions.** `TenantHoliday` (`Date`, `Name`; unique per tenant+date) exists purely to
drive time surcharges. `ServiceConditionKind.Holiday` matches when any in-scope stop's planned date
(`PlannedFrom ?? PlannedTo`) is in the tenant's holiday set; only dates the stops mention are
loaded. Condition combination rule: rows of the same `Kind` are OR'ed, different kinds AND'ed;
no rows = applies always. Admin endpoints: `GET/POST /api/pricing/holidays`, `DELETE …/{id}`.

**Snapshot merge.** Engine output is merged into `TransportOrderPricingLine` rows by `LineKey`
(`rule:{id}`, `service:{id}`, `agreement:{id}:{disc}`, `extratime:…`, `oneoff`, `diesel`,
`manual:{guid}`) — never delete-all-rewrite; orphaned adjusted lines convert to `Manual` instead of
disappearing. `DistanceKm`/`LoadingMeters` are registered pricing inputs
(`PricingInputsChangedAsync`): changing them against a Locked/Invoiced price is refused; against
Draft/Reviewed it marks the snapshot stale.

**Key invariants:** never a silent €0 (missing tariff → `RequiresManualPrice` + diagnostics);
informational/proposed lines never count toward `Total`; at most one agreement applies
included-time per measured minutes; physical quantities are never altered (oversize contracts touch
only `BillableQuantity`); a shared agreement never prices without a date-active assignment.

**Extension point (new condition kind):** add a `ServiceConditionKind` member, a case in
`RowMatches` (~line 741), load reference data next to the holiday load (~line 727), and only if
non-stackable add it to the competition array (~line 783). Do not fake product-based conditions
with loose category names (documented warning in `ServiceOption.cs`).

## 4. Warehouse locations & scanning (incl. standalone scans, idempotency)

**Where:** scan pipeline in `Modules/Scanning/Services/WarehouseScanService.cs` (note: Scanning
module, not Warehousing) + `Modules/Scanning/Controllers/WarehouseScansController.cs`
(`POST /api/warehouse/scans`, permission `scanning.execute`); locations and read models in
`Modules/Warehousing/` (`Entities/WarehouseLocation.cs`, `Services/WarehouseAdminService.cs`,
`Services/WarehouseTraceService.cs`, `Controllers/WarehousesController.cs`). Functional doc:
[docs/warehouse-scanning.md](../warehouse-scanning.md).

**Locations.** `WarehouseLocation`: `WarehouseId`, `ParentId?` (self-reference), `Code`, `Kind`
("Zone"|"Position" — display convention only), unique `(TenantId, WarehouseId, Code)` filtered on
not-deleted. `WarehouseAdminService` enforces max **two levels** (a position cannot hang under a
position), same-warehouse parentage, uppercased codes, and refuses deletion while child positions
or projected packages exist. The location *list* endpoint also accepts `scanning.execute` so
scanners can pick targets.

**Standalone scans.** Migrations `20260812074930` + `20260812080131` made `scan_events.TripId`,
`TransportOrderStopId` and `TransportOrderId` nullable and added `WarehouseLocationId` columns to
`scan_events`/`package_events` plus `packages.CurrentWarehouseLocationId`. A standalone scan always
carries `TripId = null`; trip-scoped counts filter on `TripId` and are unaffected.

**Pipeline (`SubmitAsync`).** Accepted types: `Received | Moved | Staged | Return`. Unknown
barcodes and unexpected statuses are never dropped — they become warning ledger rows
(`ScanResult.UnexpectedItem` / outcome `"UnexpectedStatus"`). `Moved`/`Staged` never change
lifecycle status; `Received`/`Return` may, but only through `PackageLifecycleMachine.IsAllowed`.
`Package.CurrentWarehouseLocationId` is a projection ("physical reality wins" — updated whenever a
location was scanned, and since the completion wave **cleared by `StorageClockInterceptor` on every
leave event**, so goods on a vehicle never keep showing a warehouse location — §5, §17 P0); the
append-only `PackageEvent` trail is the source of truth.

**Idempotency.** Key = `(TenantId, ClientEventId)`:
- app-level pre-check returns replay feedback from the stored row;
- unique partial index on `scan_events` (`ScanEventConfiguration.cs`):
  `HasIndex(TenantId, ClientEventId).IsUnique().HasFilter("\"ClientEventId\" IS NOT NULL AND \"IsDeleted\" = false")`;
- the race loser catches `DbUpdateException` and treats the stored row as authoritative.

Same `ClientEventId` therefore produces exactly one ledger row, ever — the same offline-replay
contract the driver app uses.

**Trace/overview.** `WarehouseTraceService` is fully read-only: `TraceAsync(barcode)` composes
barcode registry → package projection → order/customer → location/warehouse → last 10 package
events; `GetOverviewAsync(warehouseId, today)` buckets projected packages by
"should have left today" / "waits for tomorrow" from Planned/InProgress trips.

**Extension points:** new scan kinds go into the `ScanType` outcome switch; lifecycle transitions
into `PackageLifecycleMachine`. Note `IPackageEventWriter.Stage(...)` has no location parameter —
`WarehouseScanService` stamps `custodyEvent.WarehouseLocationId` manually afterwards.

## 5. Storage stays & billing derivation (interceptor pattern)

**Where:** `Modules/Warehousing/Entities/StorageStay.cs`,
`Services/StorageClockInterceptor.cs`, `Services/StorageBillingService.cs`; migration
`20260812081943_StorageStays`. Functional doc: [docs/storage.md](../storage.md). Tests:
`TransportationService.Api.Tests/Warehousing/StorageClockTests.cs`.

**Model.** `StorageStay`: `PackageId`, `WarehouseId`, `WarehouseLocationId?`, `InAt`, `OutAt?`,
`InPackageEventId?`/`OutPackageEventId?` (FK-less traceability). One stay = one handling unit —
there is no quantity field; pallet-days derive per package. DB invariant
`UX_storage_stays_open_per_package`: unique `(TenantId, PackageId)` filtered
`"OutAt" IS NULL AND "IsDeleted" = false` → at most one *open* stay per package, DB-enforced.

**Open side is explicit.** `WarehouseScanService.SubmitAsync` opens a stay only on non-warning
`Received`/`Return` scans with a known warehouse (`location.WarehouseId ?? request.WarehouseId`),
stamping `InAt = now`, `InPackageEventId = custodyEvent.Id`; an existing open stay only gets its
location refreshed.

**Close side is centralized in an interceptor.** `StorageClockInterceptor : SaveChangesInterceptor`
overrides `SavingChangesAsync` and watches newly `Added` `PackageEvent`s whose type is in
`LeaveEvents = [LoadScan, ReturnLoaded, RedeliveryLoaded, ReturnedToSender, Cancelled]`. For each it
finds the package's open stay — change-tracker first, then DB — and sets
`OutAt = leave.OccurredAt`, `OutPackageEventId = leave.Id`. Because it is an interceptor, **every
current and future leave path closes the clock** without each call site knowing about storage, and
the close lands in the same transaction as the leave event. The same hook also nulls
`Package.CurrentWarehouseLocationId` (completion wave P0): goods that physically left must never
show a warehouse location, and a later failed delivery on the road cannot resurrect one — only a
real warehouse scan sets it again. Registration (`Program.cs:131–144`):
singleton, third in the interceptor chain after `AuditingSaveChangesInterceptor` and
`OrderStatusHistoryInterceptor` (audit stamps land first). History is frozen: corrections close and
reopen; a closed stay is never rewritten.

**Billing derivation.** `StorageBillingService.ComputeAsync(customerId, from, to)`
(`GET /api/customers/{id}/storage`) is read-only: overlap filter
`InAt < periodEnd && (OutAt == null || OutAt > periodStart)` with `periodEnd` exclusive
(`to + 1 day`); per stay `Math.Ceiling(days)` clamped to the period — **started days count as
whole days**, matching the manual pallet-day convention; open stays run to `periodEnd`. Output
breaks down per order and per warehouse plus an open-stay count. Explicit day counts entered on an
order always win; the result feeds the existing "Per dag" / "Per pallet/dag" service types
(see [docs/storage.md](../storage.md)).

**Extension point:** add a `PackageEventType` to `LeaveEvents` for new leave semantics; open-side
changes belong in the scan pipeline.

## 6. Problems/responsibility/redelivery & charge flow into invoicing

**Where:** `Modules/Incidents/Services/IncidentService.cs`, `Entities/Incident.cs`,
`Controllers/IncidentsController.cs`; migration `20260812083503_ProblemsResponsibilityCharge`
(8 additive columns on `incidents`). Functional doc: [docs/problems.md](../problems.md). Tests:
`TransportationService.Api.Tests/Incidents/IncidentChargeAndRedeliveryTests.cs`.

**Responsibility.** `Incident.ResponsibleParty` is a validated **string**, not an enum:
`"Customer" | "Own" | "Driver" | "Supplier"`, anything else silently falls back to `"Unknown"`
(default). Distinct from `ResponsibleUserId` (the assigned internal owner, which drives the
`incident_assigned` notification). `ResponsibilityNotes` holds the rationale.

**Charge flow (two-step, approval-gated).**
1. `ProposeChargeAsync` — requires amount > 0, description, and
   `ResponsibleParty == "Customer"` ("internal costs stay internal"); sets
   `ChargeDecision = "Proposed"`. Controller permission: `incidents.manage`.
2. `DecideChargeAsync` — **fail-closed service-side check** of `problems.approve_charge`
   (a null permission service or user = denied; registered in
   `Phase8SupplyChainTests.ServiceSideEnforcedCodes`, see §14). Only a `"Proposed"` decision can be
   decided; `"Approved"` is final.

On approve with a linked order, the charge becomes a **`TransportOrderPricingLine`**
(`Kind = Manual`, `Source = "Manueel"`, `AdjustReason = "Incident: {title}"`,
`LineKey = "manual:{guid}"` so recalculation preserves it) — *not* a direct invoice line. Unless
the order price is manual, `order.AgreedPrice` and the snapshot's `LinesTotal` are bumped, and
`InvoiceReadinessEvaluator.EvaluateAsync` re-runs. The invoice then picks the charge up through
`AgreedPrice` (the base transport line is `AgreedPrice − serviceTotal`, see §11). When the order's
pricing snapshot is `Locked`/`Invoiced`, no line is created — the approval is still recorded and
the invoice-control workspace surfaces it as a pending charge (§11).

**Redelivery.** `CreateRedeliveryAsync` (`POST /api/incidents/{id}/redelivery`, permission
`orders.create|manage`): one redelivery per incident, requires a linked order. Copies cargo scalars
(`GoodsDescription`, quantities, `WeightKg`, `DistanceKm`, `LoadingMeters`, ADR/crane flags,
`LegalEntityId`) and the stop skeleton; stamps `CustomerReference = "HERLEVERING {orderNumber}"`;
copies **no** price fields or packages, and dates the new order to the next working day
(`BusinessDayCalculator.NextWorkingDay`: weekends + `tenant_holidays` skipped — completion wave
P4, which also added the failed-stop auto-incident and `TenantSettings.RedeliveryMode`, §17).
The new Draft order joins the **same dossier**
(wrapper-by-origin → earliest `DossierOrder` link → `incident.DossierId`), original packages flip
to `RedeliveryPlanned` where the lifecycle machine allows, and the number is claimed via
`TenantNumbering.SaveWithClaimedNumberAsync`.

**Unified problem list.** `ListProblemsAsync` (`GET /api/problems`) merges open `Incident`s (with
responsibility + charge state) and open `ExecutionException`s into one sorted view.

**Invariants:** only customer-responsibility problems can be charged; approval is a separate
audited right; a locked/invoiced price never gets an automatic line; the incident status machine is
`New → InProgress/Resolved/Cancelled`, resolve requires a resolution text, reopening keeps it.

## 7. Distribution planning proposals

**Where:** `Modules/Planning/Services/PlanningProposalService.cs`; endpoint on
`Modules/Planning/Controllers/TripsController.cs` (~lines 64–71). See also
[docs/planning-center.md](../planning-center.md).

A proposal is **read-only and non-persisted** — no entity, no table, no status.
`IPlanningProposalService.GetProposalsAsync(date, ct)` is the whole interface:

1. Candidates: `Confirmed` orders with `OrderDate <= date` not on any non-cancelled trip.
2. Each candidate's last unloading stop resolves to a `PricingZone` (the *pricing* zone concept is
   reused, same numeric/ordinal postal-range matching as §3); unzoned orders bucket under
   "Ongezoneerd", sorted last.
3. Within a zone: overdue orders first (`OrderDate < date`), then postal code as a route-proximity
   proxy.
4. The heuristic must explain itself: every proposal carries Dutch `Explanations`, and every
   dropped candidate lands in `Excluded` with a reason — no silent drops. Since the completion
   wave (P10) each order also carries per-order constraint notes (ADR, crane, plateau, Moffett,
   requested window, delivery-location opening hours) and each proposal a capacity signal vs the
   largest active vehicle (§17).

**There is no accept/reject endpoint.** Accepting a proposal = `POST /api/trips` with the
proposal's order ids, so all existing conflict/permission machinery applies (`planning.create`,
plus `orders.assign|manage` when order ids are attached). Rejecting = ignoring it. The
`AcceptProposalRequest` DTO in that file is vestigial and referenced nowhere — do not treat it as
an API contract.

**Extension points:** new grouping dimensions or exclusion rules slot directly into
`GetProposalsAsync`; the DTO already carries free-form explanation lists.

## 8. ETA subsystem & shift threshold

**Where:** `Modules/Eta/Services/EtaService.cs`, `Entities/StopEta.cs` (`StopEta`,
`StopEtaHistory`, `EtaSource`, `EtaStatus`), `Controllers/EtaController.cs`,
`Services/IRouteEstimationProvider.cs`; migration `20260812085739_EtaShiftThreshold`. Tests:
`TransportationService.Api.Tests/Eta/EtaHistoryAndThresholdTests.cs`.

**Computation (`RecalculateTripAsync`).** A cursor starts at `now + trip.ManualDelayMinutes` and
walks pending stops in route order. Travel time comes from `IRouteEstimationProvider`
(production default `NoRouteEstimationProvider` → 30-minute heuristic; `EtaSource` records
`Provider` vs `Heuristic`). Handling time per stop: measured location average from the last 90 days
of `StopExecutions` (≥3 samples, clamped 5–240 min) wins over
`TenantSettings.DefaultLoading/UnloadingMinutes`. Status vs the stop's latest bound
(`LatestAllowed ?? ConfirmedTo ?? PlannedTo`): `Late` past it, `AtRisk` within 15 minutes, else
`OnTime`. Dispatcher overrides (`EtaSource.DispatcherOverride`) are sticky — recalculation leaves
them untouched and continues *from* them until explicitly cleared. Note that
`GET /api/trips/{tripId}/eta` recalculates and writes — it is not a pure read.

**History is append-only.** `StopEtaHistory` (`Eta`, `Source`, `Status`, `RecordedAt`,
`ChangedByUserId`) gets one row per real change (≥1 minute drift or status change) via
`RecordChangeAsync`; rows are never updated or deleted. `StopEta` itself is unique per
`(TripId, TransportOrderStopId)`.

**Shift threshold (Wave 8).** `TenantSettings.EtaShiftNotifyMinutes` (nullable int; the migration
adds just this column). In `RecordChangeAsync`, mutually exclusive with the became-late path:
- ETA just became `Late` → internal `eta_changed` notification to `planning.edit` holders **and** a
  customer `MessageKinds.EtaUpdate` outbox message.
- Otherwise, if the threshold is set and the stop already had history, the previous ETA is the
  newest **persisted** history row (the new row is staged but unsaved — deliberate), and a shift of
  `>= threshold` minutes queues the customer message even while still on time.
- `null` threshold = pre-Wave-8 behaviour only (opt-in per tenant).

Customer message idempotency key: `eta_update:{stopId}:{eta:yyyyMMddHHmm}` — minute granularity
dedupes same-minute recomputes. The threshold is exposed through the company-settings API/UI since
the completion wave (P8), which also completed the lifecycle: trip start seeds ETAs and queues
`driver_en_route` per customer order, and recalculation runs on stop arrive/complete/skip as well
as on status transitions (§17).

## 9. Notifications & messaging profiles

**Where:** `Modules/Messaging/` (`Entities/OutboxMessage.cs` — also `MessageKinds`,
`MessagingProfile`, `MessageTemplate`; `Services/MessageOutboxService.cs`, `MessageDispatcher.cs`,
`OutboxDispatcherHostedService.cs`, `NotificationEventService.cs`, `NotificationEventCatalog.cs`)
and `Modules/Notifications/` (in-app `Notification`, `NotificationService`,
`EscalationPolicyService`, `NotificationMaintenanceHostedService`). Functional doc:
[docs/notifications.md](../notifications.md) (two layers: in-app vs outbound).

**Outbox pattern (at-least-once).** Producers only queue via
`IMessageOutboxService.QueueAsync(MessageRequest)`: duplicate check on the unique
`(TenantId, IdempotencyKey)` index; owner (`Customer` or `Employee`) resolved to address/language;
suppressions (all channels off, kind disabled) are **written as `Suppressed` rows with the
reason** — the audit trail of what was deliberately not sent. Dispatch is exclusively
`MessageDispatcher.DispatchPendingAsync` driven by `OutboxDispatcherHostedService`
(every 30 s, batches of 50): 5 attempts, exponential backoff 5/10/20/40 min; permanent failure
raises a Critical `customer_notification_failed` in-app notification to `orders.manage` holders and
at most **one** channel fallback (fallbacks never chain). One-time-credential bodies
(`MessageKinds.CarriesOneTimeCredential`, currently `PortalUserInvited`) are scrubbed from durable
storage once delivery is decided. The dispatcher runs tenant-agnostic (no request scope → the
global tenant filter is open) and stamps `TenantId` explicitly. Providers are fail-closed:
`DevelopmentSinkProvider` in Development only, `SmtpEmailProvider` when configured, otherwise
`Unconfigured*Provider`.

**MessagingProfile** is per owner `(OwnerType, OwnerId)` — customer or employee: channel toggles,
address overrides, `EnabledKindsJson` (null = all kinds), `PreferredLanguage`, quiet hours
(midnight-spanning windows handled), fallback channel. **Absent row = email on, defaults.**
Since the completion wave (P0) a customer's `EnabledKinds` list is interpreted strictly against
`MessageKinds.CustomerConfigurable` (8 kinds, the same set the portal preference screen shows):
kinds outside it — invites, replies, order acceptance — are **never** suppressed by the list.
Admin: `GET/PUT /api/messaging/profiles/{ownerType}/{ownerId}` (`messaging.manage`).

**Templates** resolve `(customer, lang) → (customer, nl) → (tenant, lang) → (tenant, nl) →
built-in` (`MessageTemplates.cs`, nl/fr/en).

**Redesign-relevant `MessageKinds`:** `eta_update`, `delay`, `driver_en_route`, `pod_available`,
`order_pod_available`, `delivery_completed`, `invoice_sent`, `portal_user_invited`, plus the
catalog-linked `order_*` family named 1:1 with `NotificationEventCatalog` event keys.

**In-app lifecycle.** `NotificationService.AddIfEnabledAsync` is the single choke point:
per-user category opt-out is honoured **unless severity is Critical**; `DedupeKey` suppresses while
an unresolved same-key row exists (including same-batch `ChangeTracker` checks). Escalation is
sweep-driven (`EscalationPolicyService` + inventory/task sweeps), not a notification state machine.
Retention: expired → archived → soft-deleted after 180 days, every 6 hours.

**Extension point (new message kind):** (1) `const` in `MessageKinds` **and** `MessageKinds.All`;
(2) built-in templates; (3) optionally a `NotificationEventInfo` in `NotificationEventCatalog`
(admin-routable, token allowlist enforced) and a `NotificationTypeCatalog` mapping (unmapped keys
fall back to `(General, Info)` silently); (4) produce via `INotificationEventService.PublishAsync`
(rule-routed) or `QueueAsync` with a stable idempotency key — **after** the business save, never
inside the same transaction; (5) if portal-toggleable, add it to
`CustomerPortalService.PortalNotificationKinds`.

## 10. Document generation (PDFsharp pattern, font configuration gotcha)

**Where:** `Modules/Orders/Services/TransportDocumentRenderer.cs` (static, pure),
`Modules/Orders/Services/TransportDocumentService.cs` (data assembly),
`Modules/Orders/Controllers/TransportDocumentsController.cs`.

**Pattern.** The renderer is a static class over an immutable snapshot record
(`TransportDocumentSnapshot(Kind, OrderNumber, Seller, Customer, Stops, Lines, …)`) — same shape as
`InvoicePdfRenderer` (`Modules/Invoicing`), `IssuedItemAcknowledgementRenderer`
(`Modules/Employees`) and `LabelRenderService` (`Modules/Packages`). `Kind` is a plain string
`"DeliveryNote" | "Cmr"`; CMR mode switches the title, CMR box numbering and renders 3 signature
boxes instead of 2. `RenderBatch` produces one `PdfDocument` with a page per snapshot (used for
per-trip batches in route order). Generated PDFs are **streamed, never persisted** — stored/uploaded
files are a separate concern (`TransportOrderDocumentService` + `IFileStorageService`, opaque
`DocumentPath`).

**The font gotcha (do not reorder these fields).** From `TransportDocumentRenderer.cs:34`:

```csharp
// MUST be the first static field: initializers run in textual order, and the XFont fields
// below need the font source configured first (same pattern as InvoicePdfRenderer).
private static readonly bool FontsConfigured = ConfigureFonts();

private static bool ConfigureFonts()
{
    PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    return true;
}

private static readonly XFont Title = new("Arial", 16, XFontStyleEx.Bold);
```

C# runs static field initializers in declaration order, so `FontsConfigured` must stay textually
before every `XFont` field, or the fonts construct before the font source is configured. Second
half of the gotcha: **no `IFontResolver` is registered anywhere in the repo** — all four renderers
rely on `UseWindowsFontsUnderWindows` + "Arial". Linux/container hosting requires a
`GlobalFontSettings.FontResolver` first (`LabelRenderService.cs` carries the marker comment).

**Endpoints:** `GET /api/orders/{id}/documents/{kind}` (`orders.view|manage`) and
`GET /api/trips/{id}/documents/{kind}` (`planning.view`); accepted kinds `"cmr"` and
`"delivery-note"` (anything else in `NormalizeKind` falls back to DeliveryNote; the controller
400s unknown kinds first). Seller = the order's legal entity, else the tenant default entity.

**Extension point (new document kind):** extend `IsKnownKind` + `NormalizeKind`, branch in
`TransportDocumentRenderer.AppendPage`, set the filename prefix in the service. For *stored*
document types, append to the `TransportOrderDocumentType` enum (string-stored, appending is safe).

## 11. Invoice readiness & invoice control grouping

**Where:** `Modules/Orders/Services/InvoiceReadinessEvaluator.cs` and
`Modules/Invoicing/Services/InvoiceControlService.cs` (+ `InvoiceService.cs`, `InvoiceNumberService.cs`).

**Readiness projection.** `InvoiceReadinessEvaluator` is a static class with one method that
**mutates the tracked order** (`order.InvoiceReadiness`, `InvoiceReadinessReasons`; caller saves —
return type is void). Non-`Completed` orders are `NotReady`. For completed orders, reason codes
accumulate: `pricing.none` (no snapshot and no `AgreedPrice`), `pricing.coverage.partial|none`,
`pricing.stale`, and `pod.missing` (only when the order ran on a completed trip — a
directly-completed order never demands a POD; `TripService` passes `tripExecutedOverride` because
its own status change is unsaved at that point). Zero reasons → `ReadyForInvoice`, else
`ReviewRequired`. It is deterministic and idempotent, never a `TransportOrderStatus` member, and
fires no notifications. Call sites: order complete/update paths, `PodService`, `TripService`,
`IncidentService` (charge approval).

**Invoice control workspace.** `InvoiceControlService.GetAsync` returns
`(Proposals, NeedsReview, PendingCharges)`. It **reads the readiness projection and never
recomputes it**; candidates are `Completed` orders not on any non-cancelled invoice. Grouping key
(`Customer.InvoiceGrouping` — a validated *string*, not an enum: `PerDossier | Weekly | Monthly |
ByReference | Manual`, default `Manual`):

```csharp
"PerDossier" => dossier ?? "Zonder dossier",
"Weekly"     => $"Week {ISOWeek.GetWeekOfYear(date…)}",
"Monthly"    => $"Maand {date:MM-yyyy}",
"ByReference"=> reference is { Length: > 0 } ? $"Referentie {reference}" : "Zonder referentie",
_            => "Klaar voor facturatie",
```

grouped under `{ CustomerId, …, Label }` — the customer is always part of the key; grouping only
subdivides within a customer. The workspace **creates nothing**: accepting a proposal calls the
existing invoice-create endpoint with the proposal's order ids. `PendingCharges` surfaces approved
incident charges whose order price was already locked/invoiced (§6).

**Invoice creation chains** (`InvoiceService.CreateAsync`): issuing entity = explicit choice →
customer default → tenant default, then order-entity uniformity + `CustomerEntityPolicy`; VAT rate,
currency, language and PO number each have their own documented fallback chains. Lines: one base
transport line per order (`UnitPrice = AgreedPrice − serviceTotal`), one line per snapshotted
service line (frozen descriptions), manual lines, diesel lines. In the same save, orders and their
pricing snapshots flip to `Invoiced`. Numbering: per-`(LegalEntity, Year, Month)`
`InvoiceSequence` with optimistic-concurrency retries (`InvoiceNumberService`, format tokens
`{PREFIX}{YYYY}{YY}{MM}{SEQ}`); counters only move forward — a cancelled invoice never releases
its number.

**Extension point (new grouping mode):** add the literal to `CustomerService.ApplyInvoiceGrouping`
and a switch arm in `InvoiceControlService.GroupKey`; no migration needed (plain string column).

## 12. Customer portal security model (customer scoping, PortalResult, CustomerVisible)

**Where:** `Modules/CustomerPortal/` — `Services/CustomerPortalService.cs`,
`PortalDocumentService.cs`, `PortalInvoiceService.cs`, `PortalDashboardService.cs`;
`Dtos/CustomerPortalDtos.cs`; `Controllers/CustomerPortalController.cs`. Functional doc:
[docs/customer-portal.md](../customer-portal.md). Wave 11 (POD summary + notification
preferences) lives here.

**PortalResult pattern** (`CustomerPortalDtos.cs`):

```csharp
public enum PortalOutcomeKind { Success, NoCustomerLink, NotFound, ValidationFailed }
public record PortalResult<T>(PortalOutcomeKind Outcome, T? Value, string? Error = null) where T : class;
```

Controller mapping (`Handle<T>`/`HandleFile`): `Success` → 200, `NoCustomerLink` → **403**,
`NotFound` → **404 body-less**, `ValidationFailed` → 400. The semantic split is deliberate:
**403 means exactly one thing — the authenticated user has no customer link.** A resource belonging
to another customer returns `NotFound`, never 403 ("other customers' orders are indistinguishable
from non-existent ones"). `where T : class` means value-type payloads get wrapper records.

**Customer scoping.** The customer is always derived from the authenticated `User.CustomerId`
(join to `Customers` within the tenant); no endpoint accepts a client-supplied customer id.
Reference validation is scoped the same way (e.g. `SubmitOrderAsync` counts owned locations and
rejects on any mismatch), and portal order intake reuses the one internal
`ITransportOrderService.CreateAsync` use case — the portal only forces the customer id,
`AgreedPrice: null` ("pricing is internal; never portal-supplied") and the `Submitted` landing
status.

**Trimmed by construction** (`TrimAsync`): only `CustomerVisibleStatuses` appear on the timeline
(`Invoiced` and internal steps never), only customer-visible execution exceptions, no prices, no
internal planning. `ExpectedDeliveryEta` comes from the order's unloading-stop `StopEta`s (§8).

**POD summary + `CustomerVisible`.** `CustomerVisible` is a bool on exactly two entities:
`ProofOfDelivery` (default true) and `ExecutionException`. The Wave 11 POD summary selects only
`IsCurrent && CustomerVisible` and projects `PortalPodSummaryDto(DeliveredAt, RecipientName,
Outcome)` — deliberately no files, notes or coordinates. The document list exposes only the POD
signature (`SignaturePath != null`); POD photos are deliberately excluded. **File endpoints
re-verify ownership + `CustomerVisible` + `IsCurrent` at download time — a list result is never a
capability.**

**Notification preferences (Wave 11).** No new entity: a projection over `MessagingProfile`
(`OwnerType = Customer`), created lazily on first save.
`GET/PUT /api/customer-portal/notification-preferences` (`customer_portal.view`). Input is
filtered against the `PortalNotificationKinds` allowlist (order confirmation, time window,
en-route, ETA, delay, delivered, POD, invoice); language restricted to nl/fr/en.

**Extension checklist for a new portal endpoint:** service method returns `PortalResult<T>`;
first statement resolves the customer (prefer the `c.IsActive` variant, see §18); every query
filters tenant **and** resolved customer; foreign ids mismatching → `NotFound()`; controller is a
one-liner through `Handle(...)` with a `customer_portal.*` permission; byte-serving endpoints
re-check visibility before reading storage.

## 13. Tenant configuration & isolation

**Where:** `TransportationService.Api/Data/TransportationDbContext.cs`,
`Common/Persistence/TenantQueryFilter.cs`, `Modules/Tenancy/TenantContextMiddleware.cs`,
`Modules/Tenancy/Entities/TenantSettings.cs`, `Common/Persistence/TenantNumbering.cs`. See also
[docs/security/](../security/) (the security sprint hardening).

**Global tenant filter ("H1").** `OnModelCreating` → `ApplyGlobalTenantFilters` builds, for every
non-owned root entity implementing `ITenantOwned`, the filter
`CurrentTenantFilterId == null || e.TenantId == CurrentTenantFilterId`, AND-composed with any
already-declared filter (typically soft delete) via `ReplacingExpressionVisitor`.
`CurrentTenantFilterId` reads `ITenantQueryFilterAccessor` live per query; the HTTP accessor pulls
the tenant from `HttpContext.Items`, set by `TenantContextMiddleware` (authenticated principal
always wins; dev impersonation headers only in Development with explicit config; never a fallback
to a default tenant). The **null path is the documented bypass**: background jobs, seeders and the
outbox dispatcher run tenant-agnostic and must stamp/filter `TenantId` explicitly.
`IgnoreQueryFilters()` bypasses **both** the tenant fence and soft delete — every such call site
must carry its own tenant predicate.

**Soft delete & audit stamps.** `AuditingSaveChangesInterceptor` flips `Deleted` →
`Modified` + `IsDeleted/DeletedAt/DeletedByUserId` for `ISoftDeletable`, stamps
`IAuditableEntity` fields (Created* is write-once), and bumps `IVersionedEntity.Version` (§15).
Soft-delete filters are declared per entity configuration; uniqueness that must ignore deleted rows
uses filtered indexes (`HasFilter("\"IsDeleted\" = false")`).

**String-stored enums.** 134 `HasConversion<string>()` sites, always with `HasMaxLength`. Client
input must be parsed via `Common/EnumParsing.cs` (`ParseDefinedOrThrow`) — `Enum.TryParse` accepts
`"7"` and would persist literal garbage into a string column. Appending enum members is safe;
renaming is not.

**TenantSettings** is one row per tenant (numbering prefixes+counters, defaults, module flags,
`EtaShiftNotifyMinutes`). It is *not* `ITenantOwned` — services filter it explicitly.

**Numbering.** `TenantNumbering.SaveWithClaimedNumberAsync(dbContext, settings, assignNumber, ct)`:
every `*NextValue` counter on `TenantSettings` is an optimistic-concurrency token
(`TenantSettingsConfiguration`); on `DbUpdateConcurrencyException` the settings entity is reloaded
and the delegate re-claims a fresh number (max 3 attempts). The delegate must contain *all*
claiming logic (the order+wrapper-dossier path claims both numbers in one delegate). Test:
`TransportationService.Api.Tests/Hardening/NumberingConcurrencyTests.cs`.

**Adding a tenant-owned entity:** derive from `AuditableTenantEntity` (filter applied
automatically), declare `HasQueryFilter(x => !x.IsDeleted)`, TenantId-first indexes, filtered
unique indexes. The architecture guard tests in
`TransportationService.Api.Tests/Security/Phase3TenantIsolationTests.cs` fail the build for
non-conforming entities (missing TenantId column, missing query filter, …).

## 14. Permissions & role template versioning

**Where:** `Modules/Identity/PermissionCodes.cs` (authoritative catalog),
`Data/PermissionCatalogSeeder.cs`, `Data/DefaultRoleDefinitions.cs`, `Data/DefaultRoleSeeder.cs`,
`Data/DefaultRoleUpgrades.cs`; guard test
`TransportationService.Api.Tests/Security/Phase8SupplyChainTests.cs`. Functional doc:
[docs/permissions.md](../permissions.md).

**Fail-closed optional permission services.** Services that enforce a permission internally take
`IPermissionAuthorizationService?` and `ICurrentUserContext?` as optional ctor parameters
(defaulted to null so unit tests construct them without DI), and the gate is written so **null
means denied**:

```csharp
// Fail-closed: no wired authorization service means NO override rights.
var allowed = _permissionService is not null && userId is { } uid
    && await _permissionService.UserHasPermissionAsync(uid, PermissionCodes.X, ct);
```

Redesign examples: `DossierService.ChangeLegalEntityAsync` (`dossiers.override_entity`, plus a
mandatory reason for non-default targets), `TransportOrderService.UpdateAsync` entity gate (same
code; no reason required — see §18), `IncidentService.DecideChargeAsync`
(`problems.approve_charge`).

**Role template versioning.** `DefaultRoleUpgrades.CurrentVersion = 28`. Each `UpgradeStep` lists
only *newly introduced* grants per role template; `DefaultRoleSeeder` applies steps above a
tenant's recorded `RoleTemplateState.AppliedVersion` exactly once, add-if-missing — nothing is ever
removed, and a grant the tenant deleted afterwards stays deleted. **Historical steps are never
edited** (retired codes in old steps are skipped by the seeder). Redesign steps:

| Version | Wave | Grants |
|---|---|---|
| 26 | Dossier foundation — tenant-configurable activity types | `planner`, `dispatcher`: `activity_types.view`; `management`: `activity_types.view` + `activity_types.manage` |
| 27 | Commercial foundation — entity override becomes a separate audited right | `management`, `boekhouding`: `dossiers.override_entity` |
| 28 | Problems — charging the customer is approval-gated | `management`, `boekhouding`: `problems.approve_charge` |

**Service-side enforcement registration.** `Phase8SupplyChainTests` reflects over every
controller's `[RequirePermission]` attributes and asserts that **every catalog permission is
checked somewhere**. Permissions enforced inside a service (not via attribute) must be registered
in `ServiceSideEnforcedCodes`, a `Dictionary<string, string>` where the value must name the real
enforcement site (e.g. `[PermissionCodes.ProblemsApproveCharge] = "IncidentService.DecideChargeAsync
approval gate (Wave 6 §2)"`). Frontend-only gating is not acceptable
(`NoPermission_IsOnlyFrontendGated` asserts that set is empty).

**Checklist for a new permission:** (1) `const` + catalog tuple in `PermissionCodes.All`;
(2) `[RequirePermission]` on a controller **or** an entry in `ServiceSideEnforcedCodes` naming the
site; (3) a new `DefaultRoleUpgrades` step with the next version number + bump `CurrentVersion`
(never edit an existing step — `DefaultRoleSeederTests` pins the version bookkeeping);
(4) optionally add to `DefaultRoleDefinitions` for newly created tenants.

## 15. Audit & concurrency (Version tokens)

**Where:** `Common/Abstractions/IVersionedEntity.cs`,
`Common/Persistence/AuditingSaveChangesInterceptor.cs`, `Modules/Auditing/`
(`AuditLog`, `AuditService`), `Modules/Orders/Services/OrderStatusHistoryInterceptor.cs`.

**Version tokens are Guids, not `xmin`.** `IVersionedEntity { Guid Version }` is maintained
centrally: the auditing interceptor sets `Version = Guid.NewGuid()` on every `Modified` entry
(soft deletes included, since they were flipped to Modified), so no mutation path can forget the
bump. Exactly two entities implement it — `TransportOrder` and `TransportDossier`, both added by
the redesign (`20260811214114_DossierActivityFoundation`). Enforcement is an **explicit
application-level compare**, not an EF concurrency token: a mismatching client token yields
HTTP 409 *carrying the current state* so the client rebases (Trip pattern); a **null client token
skips the check** (legacy/EDI/portal callers). Pre-existing entities (`Trip`, `DockAppointment`,
`EmployeeTask`, issued-item entities) keep their DB-enforced `IsConcurrencyToken()` versions with
manual bumps. The token also serves as a replay guard (`NegativeStockGuard`: confirmations must
carry the current version).

**Audit log.** `AuditService.RecordAsync(entityType, entityId, action, oldValues, newValues)`
serializes to `AuditLog` (append-only, plain class outside the tenant filter — the controller
filters explicitly), stamping the real client IP (post `UseForwardedHeaders`) and correlation id.
Masking is a call-site discipline, not an automatic layer: never pass raw entities containing
secrets or blobs — callers pass purpose-built anonymous objects.

**Interceptor-driven append-only history:** `OrderStatusHistoryInterceptor` writes an immutable
`TransportOrderStatusHistory` row on every tracked status change regardless of which module caused
it (consuming the order's transient `PendingStatusChangeReason`/`IsCorrection`);
`StorageClockInterceptor` (§5) is the same pattern for storage stays. Other append-only tables:
`StopStatusHistory`, `StopEtaHistory`, `PackageEvent`, `PeppolTransmissionEvent`.

**Testing.** `TransportationService.Api.Tests/TestSupport/SqliteTestDbContext.cs` is the canonical
harness: in-memory SQLite with the connection held open, **real production interceptors** wired
(auditing, order-status history, storage clock), schema from `EnsureCreated()`. The ctor's
`ambientTenantId` toggles the H1 filter (null mimics background/system scope);
`CreateContextForTenant(tenantId)` opens a second, tenant-scoped context over the same database to
assert isolation. Postgres-style filtered indexes work unchanged under SQLite, so partial unique
constraints (e.g. one open stay per package) are enforceable in tests. No provider-specific shims
exist anywhere in the API project.

## 16. Migrations added by the redesign (list, order, additive nature)

Location: `TransportationService.Api/Migrations/`. This section lists the redesign's original
eight migrations; the completion wave added six more, documented in §17. All eight are
**additive**: no `Up()` contains
a `DropColumn`, `DropTable`, `DropIndex`, `DropForeignKey` or `RenameColumn`. The only
`AlterColumn`s are NOT NULL → NULL relaxations on `scan_events` (non-destructive going up; the
`Down()` direction is lossy).

| # | Migration | What it adds |
|---|---|---|
| 1 | `20260811214114_DossierActivityFoundation` | Tables `activity_types`, `dossier_activities`; `transport_orders.Version` + `transport_dossiers.Version` (Guid tokens), dossier columns (`CustomerReference`, `DossierDate`, `LegalEntityId`, `OriginTransportOrderId`); filtered unique indexes incl. `UX_transport_dossiers_origin_order` and `UX_activity_types_default_transport` |
| 2 | `20260812001932_CommercialFoundation` | `SalesCategoryId` on 5 tables (service_options, pricing_agreements, price_rules, order_service_lines, order_pricing_lines); `transport_orders.InvoiceReadiness(+Reasons)`; `sales_categories` extras (`DefaultUnitCode`, `InvoiceDescriptionNl`, `VatCategoryOverride`); `order_pricing_snapshots.CoverageStatus`/`IsStale`; `invoices.LanguageCode`; `customers.InvoiceGrouping`; table `customer_allowed_legal_entities` |
| 3 | `20260812071320_PricingGeneralization` | `price_rules.OriginZoneId` (FK Restrict), table `tenant_holidays`, `transport_orders.DistanceKm` + `LoadingMeters` |
| 4 | `20260812074930_WarehouseLocationsAndStandaloneScans` | Table `warehouse_locations`; `scan_events.TripId`/`TransportOrderStopId` → nullable; `WarehouseLocationId` on scan/package events; `packages.CurrentWarehouseLocationId` |
| 5 | `20260812080131_StandaloneScanOrderNullable` | `scan_events.TransportOrderId` → nullable (completes standalone scans) |
| 6 | `20260812081943_StorageStays` | Table `storage_stays` incl. `UX_storage_stays_open_per_package` (unique open stay per package, DB-enforced) |
| 7 | `20260812083503_ProblemsResponsibilityCharge` | 8 columns on `incidents`: `ResponsibleParty`, `ResponsibilityNotes`, `ChargeDecision`, `ChargeAmount`, `ChargeDescription`, `ChargeDecidedByUserId`, `ChargeDecidedAt`, `LinkedRedeliveryOrderId` |
| 8 | `20260812085739_EtaShiftThreshold` | `tenant_settings.EtaShiftNotifyMinutes` (nullable int; null = feature off) |

All new tables carry the standard tenant + audit + soft-delete tail, TenantId-first indexes and
filtered unique indexes. Backfill notes: `transport_orders.Version` defaults to the zero Guid until
first modification; `InvoiceReadiness` defaults to `''` until `InvoiceReadinessEvaluator` first
runs; `DossierBackfillSeeder` wraps pre-existing orders at startup.

## 17. Completion wave (P0–P13)

*Commits `255a593` … `0ecad13` (backend; one frontend commit lands separately). Six additive
migrations, all applied (table at the end of this section).*

**P0 — two projection/suppression fixes.** `StorageClockInterceptor` now also clears
`Package.CurrentWarehouseLocationId` on every leave event (§5): goods that left on a vehicle
never keep a warehouse location, and only a real warehouse scan sets one again.
`MessageKinds.CustomerConfigurable` (8 kinds) became the single source of truth for customer
preference suppression: a customer's `EnabledKinds` list only governs those kinds, so system
mail can no longer be silenced by a portal preference save (§9 — the former limitation is gone).

**P1–P3 — document strategy.** `Customer.DocumentStrategy` (`GenerateOwn|CustomerDocument|
PerOrder`) + `TransportOrder.DocumentPreference` (`Own|CustomerDocument|NoneRequired|null`,
`PUT api/orders/{id}/documents/preference`). `DocumentStrategyResolver`
(`Modules/Orders/Services/DocumentStrategyResolver.cs`, static) resolves precedence:
**order override > customer default > `TenantDocumentRule` rows (by `Priority`, first row whose
cross-border/ADR/activity-type criteria all match) > built-in defaults** (ADR→CMR,
cross-border→CMR, else delivery note). Undecided (`PerOrder` without an order choice) counts as
missing info and is never auto-printed. The order detail shows the resolved decision with its
reason. Customer+date batch: `GET api/customers/{id}/documents/preview?date=` (counts per kind +
per-order reasons) and `GET api/customers/{id}/documents/{kind}?date=&orderIds=` (merged PDF);
trip batches now skip customer-document/none orders. Admin UI: `/settings/document-rules`.

**P4 — redelivery automation.** `TenantSettings.RedeliveryMode` (`Manual|Propose|Automatic`,
company settings). A `Failed` stop auto-creates exactly one incident per stop (idempotent via a
unique index on `incidents.SourceStopId`) linked to order/trip/customer/dossier; `Propose` sets
`RedeliverySuggested` ("Herlevering aanbevolen"); `Automatic` creates the redelivery order
immediately. All redelivery orders date to the next working day
(`BusinessDayCalculator.NextWorkingDay`, weekends + `tenant_holidays` skipped).

**P5 — charge policies.** `IncidentChargePolicy` (customer? × incident type? × mode
`Never|Propose|Auto` + default amount), admin at `/settings/charge-policies`
(`problems.approve_charge`). Resolution is most-specific-first; a policy fires once, when
responsibility lands on `"Customer"`. `Auto` books the pricing line through the same mechanics as
manual approval (audited, reversible until the price locks); `Never` also blocks manual
proposing. Internal responsibility can still never be charged.

**P6 — pricing dimensions.** `PriceRule.ActivityTypeId` with the new byte-stable specificity
score `Tier*8 + activity*4 + destZone*2 + originZone*1` (§3). Order flags
`PlateauRequired`/`MoffettRequired`/`IsReturnMovement`; new `ServiceConditionKind` members
`Crane|Plateau|Moffett|ReturnMovement|ActivityType`.

**P7 — scan-driven service quantities.** `ServiceOption.QuantitySource`
(`Ordered|ScannedIn|ScannedOut|Picked|PalletDays`): handling-in/out/picking count actual
distinct-package scan events; `PalletDays` follows the storage clock (§5). Entered quantities
always win; recalculation stays idempotent through the `LineKey` merge; no scans → informational
line, never a silent €0.

**P8 — ETA lifecycle completed.** Trip start seeds ETAs and queues one `driver_en_route`
customer message per trip+order (idempotent); recalculation runs on stop arrive/complete/skip as
well as on transitions; `EtaShiftNotifyMinutes` is exposed in the company-settings API/UI.
Built-in FR/EN templates added for `order_accepted`/`order_rejected`/`order_info_requested`.

**P9 — sensitive-communication review.** New `OutboxStatus.AwaitingReview`;
`NotificationRule.RequiresReview` (nullable — null falls back to the catalog's
`DefaultRequiresReview`; damage/failed-delivery/delay default to review). Only customer-owned
mail is ever held. `POST api/messaging/outbox/{id}/release|reject` (`messaging.manage`) + a
review tab in the notification admin. Producers wired: `order_damage_registered` (damage
incident with a linked order) and `order_failed_delivery` (failed stop).

**P10 — planning proposal constraints.** Per-order constraint notes (ADR, crane, plateau,
Moffett, requested window, delivery-location opening hours) and a per-proposal capacity signal vs
the largest active vehicle (§7). New trip-level blocking rule: an ADR order requires a driver
with a valid ADR(-named) qualification.

**P11 — activity KPI.** `GET /api/kpi/activities` (`ActivityKpiService`, `Modules/Reporting`):
per-activity-type rows (count, linked orders, revenue, redeliveries), per-`KpiCategory` rollup
and pallet-days; crane and plateau inside one dossier count separately. "Activiteiten" section on
the KPI page.

**P12 — invoice snooze.** `TransportOrder.InvoiceSnoozeUntil`/`InvoiceSnoozeReason`;
`PUT /api/invoice-control/orders/{orderId}/snooze`. Snoozed orders leave proposals and the review
list but stay visible in a dedicated "Uitgesteld" section; the workspace gained proposal-level
order-selection checkboxes.

**P13 — Excel order import.** `Modules/OrderImport` + `/order-imports` page.
`OrderImportProfile` ("Generiek v1" seeded per tenant; column mapping stored as JSON), dry-run
validation, SHA-256 duplicate-file refusal (app-level check — the `(TenantId, Sha256)` index is
deliberately non-unique), per-row errors and reference-dedupe skips. **Row isolation:** each row
is processed independently — a failing row records its error without aborting the batch. Rows are
created through the normal `ITransportOrderService.CreateAsync`, so every imported order gets the
wrapper-dossier guarantee (§1).

**Migrations (all additive, all applied)** — continuing the §16 numbering:

| # | Migration | What it adds |
|---|---|---|
| 9 | `20260812170208_DocumentStrategy` | `customers.DocumentStrategy`, `transport_orders.DocumentPreference`, table `tenant_document_rules` (+priority index) |
| 10 | `20260812171230_RedeliveryAndChargePolicy` | `tenant_settings.RedeliveryMode`; `incidents.SourceStopId` (unique filtered index) + `RedeliverySuggested`; table `incident_charge_policies` |
| 11 | `20260812172239_PricingDimensions` | `price_rules.ActivityTypeId`; `transport_orders.PlateauRequired`/`MoffettRequired`/`IsReturnMovement` |
| 12 | `20260812173308_ServiceQuantitySource` | `service_options.QuantitySource` |
| 13 | `20260812174522_OrderImport` | Tables `order_import_profiles`/`order_import_batches`/`order_import_rows`; `notification_rules.RequiresReview` (nullable) |
| 14 | `20260812175816_InvoiceSnooze` | `transport_orders.InvoiceSnoozeUntil`/`InvoiceSnoozeReason` |

## 18. Known limitations / deferred items

Verified in code on this branch; roughly ordered by impact. (Two former entries were resolved by
the completion wave: `EtaShiftNotifyMinutes` now has a company-settings UI, and portal
notification preferences can no longer suppress non-portal message kinds — see §17 P0/P8.)

1. **PDFsharp fonts are Windows-only.** No `IFontResolver` is registered; all four renderers rely
   on `GlobalFontSettings.UseWindowsFontsUnderWindows` + "Arial". Linux/container hosting requires
   a font resolver first (§10).
2. **Manual-price orders don't auto-absorb approved charges.** When `order.PriceIsManual`,
   `IncidentService.DecideChargeAsync` creates the pricing line but deliberately does not bump
   `AgreedPrice`, so the invoice's base line will not include the charge automatically (§6).
3. **Entity-gate asymmetry.** `DossierService.ChangeLegalEntityAsync` requires a mandatory reason
   for non-default targets; the `TransportOrderService.UpdateAsync` gate checks the same
   `dossiers.override_entity` permission but requires no reason (§14).
4. **Portal customer resolution is inconsistent about `Customer.IsActive`.**
   `PortalDocumentService`/`PortalInvoiceService` require an active customer;
   `CustomerPortalService.MyCustomerAsync` and `PortalDashboardService` do not — a deactivated
   customer loses documents/invoices but still resolves for orders/dashboard (§12).
5. **`docs/pricing.md` §4 is stale**: the specificity formula there predates the origin-zone `+1`
   and activity-type `+4` bonuses and `tenant_holidays` (§3).
6. **Zone deletion guard misses origin references.** `PricingAdminService.DeleteZoneAsync` checks
   only `PriceRules.ZoneId`; a zone used solely as `OriginZoneId` passes the app guard and fails on
   the DB `Restrict` FK as a raw `DbUpdateException` instead of a friendly validation error (§3).
7. **`MessagingProfile.ExtraRecipientsJson` is inert** — documented as "each queued separately"
    but read by neither `MessageOutboxService` nor `MessageDispatcher`. Likewise
    `MessageTemplate.BodyHtml` is sanitized on save but not consumed by outbound rendering (§9).
8. **`AcceptProposalRequest` is a vestigial DTO** in `PlanningProposalService.cs`, referenced
    nowhere — not an API contract (§7). The proposal candidate window also has no lower date bound:
    arbitrarily old confirmed orders keep surfacing, flagged `Overdue`.
9. **`IPackageEventWriter.Stage(...)` has no location parameter**; the scan service stamps
    `WarehouseLocationId` on the custody event manually afterwards (§4). Scan replay feedback
    returns `LocationCode` as null (only the id is echoed).
10. **Trip-bound depot returns don't open the storage clock** until the package is received at the
    warehouse station, because the trip event doesn't know the warehouse (documented in
    [docs/storage.md](../storage.md), §5).
11. **`Incident.ResponsibleParty` accepts unknown values silently**, falling back to `"Unknown"`
    without a validation error (§6).
12. **Document kinds are untyped strings** (`"DeliveryNote"`/`"Cmr"` across renderer, service and
    controller); `NormalizeKind` silently maps unknown values to DeliveryNote after the
    controller-side allowlist (§10).
13. Pre-existing deferred hardening items (frontend lint, NU1903, OPS checklist) are tracked
    outside this document — see the memory/known-issues notes and
    [docs/security/operational-checklist.md](../security/operational-checklist.md).
