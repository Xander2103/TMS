# Bedrijfsmiddelen (inventory) — integrity rules

Warehouse remediation 2026-10-18 (branch `remediation/warehouse-inventory`). "Magazijn" holds two products;
these rules cover **Inventory / Bedrijfsmiddelen** (company items issued to employees). Warehousing
(warehouses, docks, zones, customer colli) is unchanged.

## 1. History is never deleted (WH-B1)

Shared guard: `IssuedItemTemplateReferences` (Core `ReferenceReport` / `ReferencedRecordException`, 409
`common.record_referenced`, `canDeactivate = true`).

| Record | Refused when | Way out |
|---|---|---|
| Item (template) | stock on the item (`templateStock`), variants with stock (`stock`), movements, issued items (open or closed), reorder proposals | Deactivate ("Deactiveren" in the refusal dialog) |
| Variant | stock, movements, issued items, reorder proposals — not only current stock | Deactivate the variant |
| Attribute value (option) | used by a variant | Deactivate the value |

An unused item / variant / value can still be deleted. Historical views (movements, holders, employee rows,
the issue history) keep working on an inactive item.

## 2. Quantities are whole units (WH-H1, M2, M8)

Decision: the quantity domain is **integer**. Items are issued per piece, pair, set, box, roll — and meter,
liter, kilogram are counted as whole units too (a 1 l bottle, a 5 m roll). Fractions are not supported by the
model and are refused in Dutch, never coerced.

* One movement (receipt, issue, return, initial stock): 1 … 100.000 (`StockQuantities.MaxMovement`).
* A stock level (correction target, cached stock): −1.000.000 … 1.000.000 (`StockQuantities.MaxStock`).
* Every stock-changing command validates before mutating; `InventoryService.ApplyMovement` (the single
  arithmetic point) uses checked, bounded arithmetic — the cache can never wrap. A legacy wrapped stock can
  only be corrected back into range.
* 0 / −1 are refused (no `Math.Max(1, …)`); a decimal in JSON answers `"Geef een geheel getal in (geen
  decimalen of tekst)."` on the field (`DutchModelStateMessages`).

## 3. Serial identity (WH-H2)

Decision: the serial identity is created **at issue** (no asset register). Guarantees:

* `EmployeeIssuedItem.NormalizedSerialNumber` = upper-case, whitespace removed.
* Per tenant + item at most **one open (Issued) row** per normalized serial — service check (Dutch refusal that
  names the current holder) plus filtered unique index `UX_employee_issued_items_open_serial` (race-proof).
* A serial-required item cannot be issued without a serial; a serial is one piece (quantity 1).
* Return → reissue keeps the full lifecycle as separate rows with the same serial (searchable history).
* Migration `WarehouseInventoryIntegrity`: legacy open duplicates keep their typed serial; only the earliest
  keeps the normalized identity, the others are flagged "Dubbele houder" on the loans screen until returned.
* Free-text rows (no item) have no item identity and are not scoped.

## 4. Consumables (WH-M5)

`IssuedItemTemplate.IsConsumable` (stock-tracked, never return-required or serialized). Issuing it records an
issuance with status **Consumed** and a `Consumption` movement: stock drops, the employee history shows it, but
there is no holder, no open loan and no "Terugbrengen". Existing open rows are not rewritten. The flag is an
explicit choice in the item form ("Verbruiksartikel").

## 5. Stale stock (WH-M3)

A correction carries `ExpectedCurrentStock` (mandatory); the item form sends the stock only when it was
changed, plus the stock it was opened on. A moved stock answers 409 `common.concurrency_conflict` with
"De voorraad is intussen gewijzigd. Controleer opnieuw." — the dialog reloads the fresh figure.

## 6. Archived items (WH-M6)

An inactive item or variant refuses new receipts and issues (also reactivating a returned issuance). Returns of
goods issued before the archive and stock corrections stay possible.

## 7. Warehouse desk, loans and offboarding (WH-H3/H4/H5)

* `/inventory/issues` ("Uitgiftes & retours"): open issuances (filters overdue, must return, inactive employees,
  return due before) and searchable history (item, code, serial, employee); "Uitgeven" and "Retour verwerken".
  Search and filters live in the URL.
* Permissions: `inventory.issue`, `inventory.return` (role templates v39: Magazijn and HR). The employee picker
  (`GET /api/inventory/employee-options`) returns number, name and active status only — no HR access needed.
  The item catalogue stays with HR (`issued_items.manage_templates`, HR-wave decision).
* Offboarding: the deactivate dialog lists open return-required items ("Deze medewerker heeft nog X
  bedrijfsmiddelen in bezit.") — a warning, never a block, nothing is returned automatically; the count is in
  the offboarding summary.

## 8. Item code (WH-M1)

Every item has a visible code (`BM-0001` …, or typed), unique per tenant over all rows including deleted ones;
pickers show `code · name`. Names may repeat.

## 9. Dates and access

Default issue/return dates are the tenant's local day (`TenantTimeZone.TodayAsync`). `RequirePermission` runs
before model validation (403 before 400). The item page distinguishes "Geen toegang" (403) from "Bedrijfsmiddel
niet gevonden" (404). A brand-new item at stock 0 raises no "Niet op voorraad" alert until something moved.
