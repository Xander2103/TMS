# Transport Order Usability, Pricing Coverage & Stop Pricing Correction Wave — Inspection & Root-Cause Report

Date: 2026-08-04 · Branch: `nav-redesign` · Baseline: `bd7e551` (previous order/pricing UX wave complete, 66920f0..2bbb7c2)

This wave builds on the 2026-08-02 correction wave. The unit round-trip projection bug, id-preserving
cargo sync, optional descriptions, manual price-line modes, service add-flow, order-level included-time
overrides and Lading aggregation are already in place. The findings below describe the CURRENT state.

## Answers to the eight inspection questions

### 1. Why a changed goods-line unit could remain unsaved

Root cause (historical, **fixed** in commit `d29c62c`): the `CargoItemDto` projection passed 22
positional args and omitted the optional 23rd `QuantityUnitCode`, so every detail/PUT response returned
`quantityUnitCode: null`; the edit form re-seeded from the response and the second save persisted the
loss. Current state verified end-to-end: write (`ApplyCargoInput`, `TransportOrderService.cs:1121`),
id-preserving update sync (`:422-454`), read projection (`:1267`) all carry the code. Residual gaps:
customer-portal-created lines never receive a `QuantityUnitCode` (`CustomerPortalService.cs:220-223`)
and no catalog validation exists for unknown codes (silently price as nothing).

### 2. Why an old automatic price ("2 × Europallet") could survive while the order shows "2 Doos"

Four mechanisms, in order of likelihood:

1. **Cargo lines never drove the base price.** The engine input is built exclusively from the
   order-level `Quantity` + `QuantityUnitCode` pair (`TransportOrderService.cs:1432-1449`); cargo lines
   only feed oversize dimensions, degression groups and line count. Editing the goods lines to
   "2 Doos" while the header pair still says EUROPALLET re-emits the Europallet line legitimately.
   *This is the primary root cause and the reason for the goods source-of-truth phase.*
2. **Locked/Invoiced pricing silently ignores goods edits.** `PricingInputsChangedAsync`
   (`:1787-1824`) does not inspect quantity, unit, cargo, weight, volume, pallets or stops, so a
   goods change on a locked order saves fine and the frozen price stays without warning.
3. **AutoAdjusted orphan → Manual.** A user-corrected line whose rule no longer matches keeps its
   old label/amount verbatim as a Manual line (`:1596-1601`) and still counts in `LinesTotal`.
4. **AutoAdjusted with stable LineKey** keeps the user's label/quantity/amount when only quantities
   change (`:1552-1576`), by design.

Pure `Auto`/`Proposed` lines cannot go stale: they are deleted and rewritten on every
`ApplyPricingAsync` run (`:1543`), which runs unconditionally on create/update/recalculate.

### 3. Current source of truth

None — three independent, hand-entered stores that the backend never reconciles:

| Store | Authoritative for | Consumed by |
|---|---|---|
| Order-level `Quantity`/`QuantityUnitCode`/`WeightKg`/`VolumeM3`/`PalletCount` | the entire base price | `ApplyPricingAsync:1432-1449, 1503-1505` |
| `CargoItem` lines | content, oversize dims, degression groups, line count | `:1442-1448`, `BuildPricingGroupsAsync:1724` |
| `Package` rows (scanable colli) | scanning/POD/warehouse execution | scan pipeline only — never priced |

Drift risks: order qty vs cargo totals never cross-checked; pallets triple-tracked
(`order.PalletCount` prices, `CargoItem.PalletCount` is display-only, pallet packages are execution);
package regeneration is not triggered by cargo edits.

### 4. Base-rule selection with multiple goods lines

Per unit line independently (`PricingEngine.cs:192-246`): candidates filtered on `UnitTypeId` + zone,
ranked `AgreementTier*4 + zoneBonus*2` then `Priority`; an exact tie is a blocking configuration
error. No match ⇒ €0 "Geen tarief geconfigureerd voor {unit}" line (source `Ontbrekend`) +
`RequiresManualPrice`. Because the order side sends at most ONE line, multi-unit selection was only
reachable via the preview endpoint until this wave.

### 5. How services obtain their quantity

`FinalizeAsync` per `SurchargeKind` (`PricingEngine.cs:665-865`): explicit entered quantity always
wins; `PerUnit` (picking) falls back to the request's unit `Lines` for the option's unit type, then
cargo-derived `Groups`; `PerOrderLine` ← cargo line count; `PerKg`/`PerM3`/`PerLdm` ← order measures;
`PerHour`/`PerStop`/`PerDay`/`PerPalletDay` require entered quantities (PerPalletDay = pallets × days
via `EffectiveServiceQuantity`, `TransportOrderService.cs:1339-1346`). Unknown quantity ⇒ informational
€0 prompt line, never a charge.

### 6. How Reviewed/Locked works today

Single field `TransportOrderPricingSnapshot.Status` (`Draft/Reviewed/Locked/Invoiced`) — no at/by
columns (audit log only). Transitions `TransportOrderService.cs:1369-1376`; touching Locked either
way needs `orders.lock_price`; Draft↔Reviewed needs `orders.edit`. Locked blocks line saves,
recalculation, proposal confirmation and (some) pricing-input changes with
`PricingLockedMessage`. UI exposes the technical vocabulary directly: "Markeer gecontroleerd",
"Vergrendel prijs", "Ontgrendel" (`TransportOrderDetailPage.tsx:588-612`), statuses "Concept /
Gecontroleerd / Vergrendeld / Gefactureerd" (`types.ts:246-251`). Total price appears only in the
table footer and as a fact row — never near the header.

### 7. Stop time windows

Four pairs + bounds on `TransportOrderStop` (`TransportOrder.cs:186-196`): `Planned*` (planner),
`Requested*` (customer ask; only pair the portal writes), `Confirmed*` (commitment; editable via the
execution-plan endpoint), `EarliestAllowed`/`LatestAllowed` (hard bounds; ETA deadline), plus
`AppointmentRequired`/`AppointmentReference` (persisted, audited, but consumed by nothing beyond the
detail DTO). Display precedence `Confirmed ?? Planned ?? Requested`; deadline
`LatestAllowed ?? ConfirmedTo ?? PlannedTo`. **No time window feeds pricing.** The form shows 2 of 8
datetime inputs by default; the rest sit behind a `<details>`.

### 8. Surcharge model to extend for time-based conditions

**`ServiceOption` + `ServiceOptionCondition`** (`ServiceOption.cs:50-64`) — the enum
`ServiceConditionKind` (today only `Warehouse`) is documented as deliberately extensible; evaluation
semantics already exist (same-kind rows OR, different kinds AND, no rows = always;
`PricingEngine.cs:656-713`); `AutoApply` + per-customer overrides give automatic, customer-scoped,
tenant-isolated surcharges; a `Percent` kind correctly applies to `subtotalBeforeServices`.
Missing plumbing: `PriceCalculationRequest` carries no stop times/appointment flags — they must be
passed like `WarehouseIds` is. Priority/most-specific selection among competing time conditions does
not exist yet and is added in this wave. Runners-up rejected: `PricingAgreementSurcharge` has no
condition columns; `PricingAgreementModifier` only applies to derived rate tables.

## Wave decisions

- **Goods source of truth:** when cargo lines exist they become primary — the engine receives one
  unit line per distinct `QuantityUnitCode` aggregated from cargo lines; order summary fields are
  derived (weight/volume/pallets summed; qty+unit only when all lines share one unit, else null) and
  no longer independently editable in the UI. No lines ⇒ legacy order-level behavior unchanged.
- **Validation:** an order is valid with (qty>0 + unit) OR ≥1 valid cargo line OR a general
  description. New message: "Vul minstens een hoeveelheid en eenheid in, voeg een goederenlijn toe
  of beschrijf de goederen." (Intentional change: descriptionless orders with cargo become valid.)
- **Coverage:** engine reports per-unit-line coverage (`Volledig/Gedeeltelijk/Niet geprijsd`);
  services never masquerade as base pricing; order-level warning when any line lacks a base rule.
- **Confirmation UX:** visible workflow Nog te bevestigen → Bevestigd (maps to Locked +
  `ConfirmedAtUtc/ConfirmedByUserId` on the snapshot); "Prijs aanpassen" = unlock with required
  reason; confirm blocked on incomplete coverage unless override permission + reason + audit.
  Technical Draft/Reviewed/Locked stays underneath; Reviewed remains reachable for API compatibility.
- **Time-based surcharges:** new `ServiceConditionKind` members (StopTimeBefore, StopTimeAfter,
  AppointmentRequired, Weekend) with `StopType` + `TimeOfDay` payload columns; evaluated against the
  new per-stop time requirements; competing Before/Before (or After/After) matches resolved by
  option priority, then most-specific time; equal ⇒ configuration error; no stacking by default.
- **Stop time requirements:** explicit per-stop `TimeRequirementKind` (None/Before/After/Window) +
  `TimeRequirementFrom/To` — user-friendly layer feeding surcharges; advanced windows untouched.
- **Included time:** stop-level override added; resolution stop → order → contract (company default
  does not exist as infrastructure — recorded limitation).
