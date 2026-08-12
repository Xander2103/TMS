# Wave 4 — Warehouse Locations + Standalone Scanning Implementation Note

Scope (gap analysis §7 a-e; master spec warehouse part): the one-scan pipeline
(ScanService → BarcodeResolution → PackageScanProcessor → PackageEventWriter, append-only
custody) is KEPT and never forked — the wave gives it a location dimension and a trip-less
entry point.

## 1. WarehouseLocation hierarchy (gap a)

- New entity `WarehouseLocation` (WarehouseId FK, ParentId self-FK nullable, Code, Name,
  Kind string `Zone | Position`, IsActive, SortOrder). Simple two-level convention
  (warehouse → zone → position), but ParentId keeps it configurable without schema churn.
  Unique (TenantId, WarehouseId, Code) among non-deleted rows.
- CRUD in the existing `WarehouseService` on warehouse.manage (no new permission — the
  catalogue rule: reuse unless genuinely new capability); reads on warehouse.view +
  scanning.execute (scanners must list target locations).
- UI: locations panel on the warehouses page (per warehouse: tree/list, add zone/position,
  deactivate). No redesign — a drawer/section next to the existing dock config.

## 2. Package location projection (gap b)

- `Package.CurrentWarehouseLocationId` (nullable FK, projection — custody events stay the
  source of truth), `PackageEvent.WarehouseLocationId` + `ScanEvent.WarehouseLocationId`
  (nullable): every location-relevant event records where.

## 3. Standalone warehouse scans (gap c) + trip-less return check-in (gap e)

- `ScanEvent.TripId`/`TransportOrderStopId` become NULLABLE (additive migration — existing
  rows keep values; the ledger's semantics per row are unchanged). `TransportOrderId` stays
  required (resolved from the package).
- Append `ScanType.Received | Moved | Staged` (int enum — append at the END, values 5-7).
- New endpoint `POST /api/warehouse/scans` (scanning.execute): barcode + scanType
  (Received/Moved/Staged/Return) + warehouseLocationId (required for Moved, optional
  otherwise) + clientEventId idempotency — resolves via the SAME `PackageBarcodeService`,
  appends the SAME custody events via `PackageEventWriter`, writes the SAME ScanEvent ledger.
- Lifecycle mapping (PackageLifecycleMachine extended deliberately, with tests):
  Received: Created/Labelled → AwaitingLoading (arrival registration); already
  AwaitingLoading+ → no-op status, custody event + location update only.
  Moved/Staged: never a lifecycle change — custody event + location projection only
  (Staged is an operational marker with its own event type).
  Return (existing type, now trip-less): ReturnPending/DeliveryFailed/Refused →
  ReturnedToDepot, exactly like the trip-scoped return scan (gap e).
- Duplicate/unknown barcodes follow the existing warning semantics (recorded, never
  silently dropped).

## 4. Warehouse trace (gap d)

- `GET /api/warehouse/trace?barcode=…` — "where is X": package, current location, order,
  lifecycle, last 10 custody events (warehouse.view or scanning.view).
- `GET /api/warehouse/{id}/overview` — "what is here / what should have left / what waits":
  packages whose CurrentWarehouseLocationId is in the warehouse grouped per location, plus
  outbound lists derived from planned trips (today / tomorrow) whose orders have packages
  still at the warehouse.
- UI: "Magazijn — trace" page (scan/type a barcode → answer card) + overview tab on the
  warehouse page. Nav under Magazijn.

## Phases (each: full gates, focused commit)

1. **Schema + locations** — migration (warehouse_locations, projection/event columns,
   nullable trip/stop), entities/configs, WarehouseService location CRUD + endpoints,
   locations UI panel, tests.
2. **Standalone scans** — endpoint + processor path for Received/Moved/Staged/trip-less
   Return, lifecycle-machine extension, location projection updates, idempotency; tests
   (arrival, move, stage, return, duplicate, unknown barcode, wrong tenant).
3. **Trace + overview** — endpoints + pages, nav entry, docs (docs/warehouse-scanning.md),
   memory update.

Risks: ScanEvent nullability must not break existing per-trip tally queries (they filter by
TripId — a NULL row never matches, verified by the existing scan suites staying green);
lifecycle-machine adjacency changes are append-only and test-pinned.
