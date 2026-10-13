# Wave 3 — Pricing Generalization Implementation Note

Scope (gap analysis §6 items b/c/g/h; master spec pricing part): the engine is KEPT — every
change below feeds it better inputs or appends configuration dimensions. Golden protection:
the existing PricingEngine/Order pricing suites run unchanged; a null new input must price
byte-identically to today.

## 1. Order distance & loading-meter inputs (gap b)

- The engine already accepts `PriceCalculationRequest.DistanceKm` and `.LoadingMeters` and
  already implements `PerKm`/`PerLoadingMeter` bases + `LoadingMetersTo` bracket caps — the
  ORDER simply never supplies them (hardcoded null at the call site).
- Additive columns `TransportOrder.DistanceKm` (decimal?, km, 2 dec) and
  `TransportOrder.LoadingMeters` (decimal?, ldm, 2 dec). Create/Update requests + detail DTO +
  order form (Goederen section, next to weight/volume — progressive, optional).
- Both become pricing inputs: fed into the engine request; added to
  `PricingInputsChangedAsync` (locked-price refusal + stale marking work automatically).

## 2. Origin zone / O-D dimension (gap c)

- `PriceRule.OriginZoneId` (nullable FK to pricing_zones, additive). The engine resolves the
  ORIGIN zone from the first loading stop (request gains `OriginCountryCode`/`OriginPostalCode`,
  wired from the order like the delivery pair) with the same postal-range mechanism.
- Candidate filter: a rule with `OriginZoneId` only matches when the origin zone resolves to
  it; specificity scoring counts origin-zone specificity like destination (destination stays
  the stronger tiebreaker: score += 2 for destination, += 1 for origin).
- Rule grid: "Van zone" column (select); DTOs/save additive. Agreement modifiers stay
  destination-only (unchanged).

## 3. Maut as a sales-side concept (gap g)

- Append `SurchargeKind.PerKm` (string-stored, append-safe): service amount = value ×
  `DistanceKm` (order-level input from §1); quantity = the km. Auto-apply + customer override +
  invoice description all reuse the existing option mechanics — "Maut-toeslag" is simply a
  PerKm option, mirroring how diesel is a percentage concept.
- Engine service computation + coverage/billable display; editor kind dropdown + value label
  ("Standaardprijs per km (€)"). No coupling to the fleet Maut cost data (that stays trip
  cost); sales-side is deliberately configuration-driven.

## 4. Holiday calendar for time surcharges (gap h)

- New entity `TenantHoliday` (Id, TenantId, Date unique-per-tenant, Name) + CRUD on
  tariffs.manage (it exists purely to drive time surcharges) under Prijzen-instellingen.
- Append `ServiceConditionKind.Holiday`: matches when the stop's requirement/planned date is a
  tenant holiday (same StopScope semantics as Weekend). Engine evaluation loads the tenant's
  holiday dates once per calculation.
- Editor: kind dropdown gains "Feestdag"; holidays settings panel (date + name, list +
  delete) on the pricing settings page.

## Phases (each: dotnet test + npm test + tsc + lint + build green, focused commit)

1. **Schema + order inputs** — migration (DistanceKm/LoadingMeters, OriginZoneId,
   tenant_holidays), entities/configs, order DTO/request plumbing, engine feed, stale/lock
   coverage, order form fields; PerKm/PerLdm firing tests + golden null-input test.
2. **O/D zone** — engine origin resolution + filter + scoring, admin DTO/UI, tests
   (origin-specific rule wins only for matching origin; destination tiebreak preserved).
3. **PerKm service kind + holiday calendar** — enum appends, engine service computation +
   holiday condition, TenantHoliday CRUD + settings UI, editor updates, tests (Maut option
   auto-applied × km; holiday surcharge fires on configured date, not on ordinary days).

Risks: `PriceRuleBasis.PerKm` order-level rules double-charging with a PerKm service — they
already coexist for PerStop (rule base + service) and follow the same "rule prices transport,
service prices supplement" separation; scoring change must keep every existing
zone-specificity test green (golden suite).
