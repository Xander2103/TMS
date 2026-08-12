# Wave 5 — Storage Implementation Note

Scope (gap analysis §7f + §6d; master spec storage part): a movement-based storage clock per
handling unit, pallet-day/month derivation INTO the existing pricing kinds (PerDay/
PerPalletDay services already exist with manually entered quantities — Wave 5 supplies the
quantities from reality), and handling IN/OUT auto-sales via the existing warehouse-
conditioned auto-apply services.

## 1. Storage clock (StorageStay)

- New entity `StorageStay` (PackageId, WarehouseId, WarehouseLocationId nullable, InAt,
  OutAt nullable, InPackageEventId, OutPackageEventId nullable). One OPEN stay per package
  max (filtered unique index on OutAt IS NULL).
- Maintained by the existing pipelines — never a second bookkeeping path:
  - OPEN on the Wave-4 `Received` scan (WarehouseScanService) and on the trip-scoped
    `ReturnedToDepot` custody transition.
  - CLOSE on `LoadScan`/`RedeliveryLoaded`/`ReturnLoaded` custody events (package leaves)
    and on `ReturnedToSender`/`Cancelled` (administratively gone).
  - Idempotent: opening while open only updates the location; closing while closed = no-op.
- Historical stays are frozen rows — corrections append (close + reopen), never rewrite.

## 2. Pallet-day derivation into pricing

- `IStorageBillingService.ComputeAsync(customerId, from, to)`: per package → overlap of
  [InAt, OutAt ?? now) with the period, in DAYS (ceiling per started day, the industry norm
  used by the existing PerPalletDay semantics); aggregated per order and per customer:
  total pallet-days + per-warehouse breakdown.
- `GET /api/customers/{id}/storage?from=&to=` (warehouse.view of tariffs.view) — the numbers
  the operator enters today by counting manually; the Wave-10 proposal engine will consume
  the same service. The order form's PerDay/PerPalletDay day-count inputs stay authoritative
  (explicit wins) — this endpoint feeds the operator the REAL number.

## 3. Storage overview + handling auto-sales

- Storage tab on the trace/voorraad page: open stays per warehouse (aantal colli, sinds,
  klant/order) + a period query per customer.
- Handling IN/OUT auto-sales: ALREADY covered by warehouse-conditioned auto-apply service
  options (Wave 2026-08-04 §16 + PerUnit kinds); Wave 5 verifies the combination with a
  test (Received scan → order touches warehouse → auto-applied handling service prices) and
  documents the configuration recipe in docs/storage.md.

## Phases

1. **Schema + clock** — StorageStay + pipeline hooks + tests (open/close/idempotent/reopen).
2. **Derivation + surface** — billing service + endpoint + storage tab + docs; tests
   (period overlap incl. open stays, per-order aggregation, handling recipe).
