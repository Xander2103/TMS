# Order-form field audit — internal (≈89 fields) vs portal (18 fields)

Addendum to `2026-08-11-tms-redesign-gap-analysis.md`; execution guidance in
`docs/superpowers/plans/2026-08-11-wave1-dossier-foundation-plan.md` §12.
Source: `TransportOrderForm.tsx` (2,616 lines) + `CustomerPortalNewOrderPage.tsx`, verified against
`TransportOrderService.DeriveSummaryFromCargo` (lines 1158–1202) and the DTOs.

Classes: **A** primary (normal flow) · **B** contextual (only when relevant) · **C** advanced ("Meer details") ·
**D** masterdata (prefill from customer/location/unit master) · **E** duplicate/derived (remove from default editing) ·
**F** technical (hide from normal dispatch).

## Algemeen (7 controls)

| Field | Class | Note |
|---|---|---|
| Klant (customerId) | A | required; only hard-required create field |
| Klantreferentie | A | honour `customerReferenceRequired` hint |
| Opdrachtdatum | A | defaults today |
| Facturerende entiteit | D | `Customer.DefaultLegalEntityId` chain resolves it |
| Dieseltoeslag afwijking (checkbox + % + reden) | F ×3 | audited financial exception |

## Route & stops (27 controls per stop row; default 2 rows)

| Field | Class | Note |
|---|---|---|
| Stoptype select | E | set by the +Laadstop/+Losstop button |
| Locatie (LocationSelect) | A | one pick fills address + snapshot |
| Naam / Adres / Postcode / Plaats / Land (vrij adres) | B ×5 | trigger: no master location; Plaats required then |
| Datum / Van / Tot | A ×3 | the normal "when" |
| Referentie | B | per-stop customer ref |
| Tijdseis (kind + from + to) | B ×3 | commercial promise; drives surcharges |
| Afspraak verplicht | D | `Location.AppointmentRequired` / `DeliveryByAppointmentOnly` |
| Afspraakreferentie | B | trigger: appointment required |
| Instructies | B | order-specific driver note |
| Gevraagd van/tot | E/C ×2 | meaningful only on portal orders → read-only display |
| Bevestigd van/tot | F ×2 | belongs to StopExecutionPlan dialog (post-planning) |
| Vroegst/Uiterste toegelaten | E ×2 | restatement of Tijdseis After/Before; advanced round-trip only |
| Inbegrepen tijd override | D | `Location.DefaultLoading/UnloadingMinutes` already snapshotted |
| Toegangs-/Laad-/Losinstructies | D ×3 | verbatim `Location` columns, already snapshotted |
| refreshSnapshot / reorder / collapse | F | maintenance affordances |

Default drawer per stop after Wave 1: **7 visible controls** (Locatie-of-adres, Datum, Van, Tot, Tijdseis, Referentie, Instructies).

## Goederen — header (8) + cargo line (21 per row)

Header: Omschrijving **A** (satisfies minimal-cargo rule alone); Aantal/Eenheid/Gewicht/Volume/Paletten **E ×5**
(overwritten by `DeriveSummaryFromCargo` once lines exist → read-only summary); ADR header **E** (should follow
`any(line.adr)`; manual only for line-less orders); Kraan vereist **D** (`Location.CraneRequired`); legacy `quantityUnit` **F**.

Cargo line: Omschrijving **A**; Verwacht aantal **A** (required >0); Eenheid (code) **A** (pricing matches on it);
Totaal gewicht **A** (feeds weight tariffs — move out of the `<details>`); Verpakkingstype **E** (collapse into unit code);
Eigen typenaam **B** (Other); Laad-/Losstop pinning **B ×2** (only >1 stop of that side); Barcode **C** (validated unique);
Paletten/Referentie/Opmerkingen/Stapelbaar/Handmatig-volume **C ×5**; ADR + details **B ×2**;
Gewicht per stuk **D/E** (`UnitTypeMaster.DefaultWeightKg` or C9÷C3); L/B/H **D ×3** (unit master, Fixed locks);
Volume per stuk **E** (L×B×H); legacy `quantityUnit` **F**.

**Bug found:** line volume is labeled "per stuk" but summed **without ×expectedQuantity** in both `cargoSummary`
(client) and `DeriveSummaryFromCargo` (`TransportOrderService.cs:1192`) while weight sums the *total* field.
33 pallets × 2 m³ → header 2 m³. Fix in Wave 1 phase 6 (aggregation ×quantity + client mirror + regression test).
Related: nothing cross-checks `totalWeightKg` = `weightPerUnitKg` × `expectedQuantity`.

## Services & toeslagen (~14)

Auto-applied rows + Berekeningswijze: **F** (display). Dienst-toevoegen select: **B**. Kind-driven quantity inputs
(uur/stops/dagen/pallets): **B** (PerStop prefilled `#losstops − 1`; pallets prefilled from header → **E**);
pallet-dagen product: **E** (S5×S6, manual correction only); notitie: **C**;
inbegrepen-/extratijd-overrides ×5: **F** (contract deviations, behind "Contractafwijkingen").

## Prijs (11)

Prijsbron radio: **C** (default Contract). One-off cluster (bedrag, tijdmodus, laden/lossen/totaal-minuten, uurtarief,
notities): **B ×7** (trigger: OneOff; bedrag required then). Handmatige prijs + reden: **F ×2** (permission `orders.override_price`);
Afgesproken prijs: **C**.

## Samenvatting

Notities: **A**. Rest read-only.

## Roll-up

| Class | A | B | C | D | E | F |
|---|---|---|---|---|---|---|
| Count | 12 | 24 | 10 | 11 | 13 | 15 |

## Portal form (18 fields) — the working proof

Keeps only: referentie, datum, goederenomschrijving, opmerkingen; per stop locatie-of-adres(5), gevraagd van/tot,
referentie, instructies; per cargoregel omschrijving, aantal, type, gewicht, ADR. Omits all services/pricing/documents/
advanced timing — and produces valid orders through the same backend use case.
Not to be imported from the portal: silent dropping of blank cargo rows; missing from/to window-order validation.

## Duplication map (drives §12 of the Wave 1 plan)

1. Header ↔ cargo lines: 6 derived pairs (`DeriveSummaryFromCargo`), plus ADR not derived (drift possible).
2. Stop timing: one "when" asked five ways (Planned, Tijdseis, Requested, Confirmed, Earliest/Latest) with five
   separate error messages; only Tijdseis drives surcharges.
3. Included/extra time at three levels with identical labels (stop / order-contract / one-off): 11 numeric inputs
   for two concepts; `includedTimeSourceLabel` exists only to explain which won.
4. `appointmentRequired`, ADR, `palletCount`, and the three instruction fields each exist 2–3 times across
   header/line/location; location versions are already snapshotted.
