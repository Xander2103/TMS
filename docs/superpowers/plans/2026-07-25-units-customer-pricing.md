# Units, Customer Pricing & Automatic Order Pricing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One coherent units / customer-pricing / order-pricing architecture per the approved functional specification (`TransportationService_Functionele_Specificatie_Eenheden_en_Klanttarieven.docx`) + prompt clarifications: fully configurable unit master data, customer unit preferences (labels/EDI/Excel/favourites), versioned customer pricing agreements with deterministic rule selection, billable quantity, scheduled future price changes with bulk ±%, transparent breakdown, immutable snapshots, invoice from snapshot — and retirement of the legacy RateCard engine.

**Architecture:** Extend the existing `Tarification` module (PriceRule engine) into the single pricing system: add `PricingAgreement` grouping + new rule bases + priority/ambiguity handling + billable quantity; convert legacy `RateCard` data into agreements and delete the fallback; extend `UnitType` into a full unit master; extend `CustomerPreferredUnit` into full customer unit config; add `ScheduledPriceAdjustment` for future price versions (materialized as new effective-dated rules).

**Tech Stack:** .NET 8 API (EF Core + Npgsql, additive migrations, `AuditableTenantEntity`, `IAuditService.RecordAsync`), React/TypeScript (Vitest), xUnit backend tests.

## Global Constraints

- Additive migrations only; never edit historical migrations (`TransportationService.Api/Migrations`).
- Preserve tenant isolation (`TenantId` filters everywhere), permissions (`tariffs.view/manage`, `unit_types.view/manage`, `orders.override_price` — no new permission codes needed, so **no role-version bump**), audit logging (`IAuditService.RecordAsync` pattern as in `PricingAdminService`).
- Preserve historical data: orders, cargo, invoices, invoice lines, snapshots, rate cards (table stays; data converted, never deleted).
- No second parallel pricing engine: the legacy RateCard quote path is **removed** after data conversion.
- Never hardcode unit dimensions in logic (seed data may carry example defaults; logic reads the Unit record).
- Never silently price €0; never silently regenerate a unit code.
- Dutch UI copy consistent with existing screens.
- Tariff date = `order.OrderDate` (existing engine input; now surfaced explicitly in breakdown + snapshot).
- Monetary rounding: `decimal.Round(x, 2, MidpointRounding.AwayFromZero)` (matches RateCardService)."

---

## Part A — Repository audit conclusions (design input)

**Reusable as-is:** `PricingZone`/`PricingZoneArea` + postcode resolution; `ServiceOption`/`CustomerServiceOptionPrice`; `CustomerDieselSurcharge` + invoicing's `DieselSurchargeCalculator`; snapshot line entities `TransportOrderPricingLine`/`TransportOrderServiceLine`; `ApplyPricingAsync` orchestration incl. override guard (`orders.override_price`, reason, audit); invoice generation consuming `AgreedPrice` + service-line snapshots; `PricingAdminService` validation+audit pattern; permission + role infrastructure.

**Incomplete:** `UnitType` (no dimensions/physical defaults/category/decimals/symbol, no full master UI — only 2 flags on PricingSettingsPage); `CustomerPreferredUnit` (no label/EDI/Excel/favourite); breakdown lacks rule identity/tariff date/billable qty; snapshot lacks ch.21 header fields; cargo-line unit select not customer-grouped; no dimension autofill; EDI import ignores units.

**Structurally wrong (to refactor):**
1. **Two parallel engines** — `PricingEngine` falls back to legacy `RateCardService.QuoteAsync`. → Convert rate cards to `PricingAgreement`+`PriceRule` rows; delete fallback, `RateCardService`, `RateCardsController`, rate-card UI.
2. **First-match-wins rule pick** (`PickRule`) with no ambiguity detection. → Deterministic specificity scoring + explicit `Priority` + blocking configuration error on exact ties.
3. **No versioning container / future changes** — rules are standalone with windows but there's no grouped rate card identity in the breakdown, no scheduled adjustment workflow. → `PricingAgreement` + `ScheduledPriceAdjustment` materializing future rule versions.
4. **No billable quantity** — engine bills actual quantity only. → Oversize contract fields on `PriceRule` + per-line detail input.
5. Missing rule bases for legacy parity: `PerKm`, `PerPallet`, `PerTon` (+ `BaseAmount` per rule) — needed so converted rate cards keep pricing identically; km/ldm/stops/hour pricing otherwise flows through units (Kilometer/Laadmeter/Stop/Uur unit + PerUnit/QuantityBracket rules).

**Data migration strategy:** columns are additive; legacy `rate_cards` rows stay untouched; a C# idempotent backfill (keyed by new `PricingAgreement.LegacyRateCardId`) converts each rate card → 1 agreement (+ surcharges) + component rules (Fixed base / PerKm / PerPallet / PerTon). Existing `price_rules` keep working unchanged (nullable `AgreementId`, `Priority=0` default preserves current selection semantics, extended by ambiguity detection).

---

## Part B — Target domain model

### B1. Unit master data (`Modules/Reference/Entities/UnitType.cs` — extend existing entity)

```csharp
public enum UnitCategory { Other = 0, Packaging = 1, Weight = 2, Volume = 3, Capacity = 4, Time = 5, Distance = 6, Commercial = 7 }
public enum UnitDimensionBehavior { Variable = 0, DefaultButOverridable = 1, Fixed = 2 }

public class UnitType : LookupEntity   // Code, Name, Description, IsActive, SortOrder inherited
{
    public bool AllowForOrderEntry { get; set; } = true;
    public bool AllowForPricing { get; set; } = true;
    public UnitCategory Category { get; set; } = UnitCategory.Other;
    public int Decimals { get; set; }                 // quantity decimals for order entry
    public string? Symbol { get; set; }               // optional symbol/abbreviation (display only)
    public UnitDimensionBehavior DimensionBehavior { get; set; } = UnitDimensionBehavior.Variable;
    public decimal? DefaultLengthCm { get; set; }
    public decimal? DefaultWidthCm { get; set; }
    public decimal? DefaultHeightCm { get; set; }
    public decimal? DefaultWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public decimal? DefaultVolumeM3 { get; set; }
    public decimal? DefaultLoadingMeters { get; set; }
    public decimal? DefaultPalletPlaces { get; set; }
}
```

- Code stays user-editable through the existing lookup CRUD; **backend never regenerates codes**. Frontend suggests a code from the name only while creating and only while the code field is untouched (uppercase, diacritics stripped, `[A-Z0-9_-]`, max 12). Validation: `^[A-Z0-9_-]{2,20}$` (normalize to uppercase), tenant-unique (existing lookup unique index).
- New endpoints on `UnitTypesController`: `GET /api/unit-types/master` (full DTO, perms UnitTypesView|Manage|TariffsView|Manage), `POST /api/unit-types/master`, `PUT /api/unit-types/{id}/master` (perms UnitTypesManage|TariffsManage; audited via `IAuditService` entity type `"UnitType"`). Existing `/settings` endpoints stay (FE migrates to master).
- Seeder (`ReferenceDataSeeder`): new-tenant seed + idempotent backfill (only fills fields still at defaults; never overwrites user edits): EUROPALLET → Packaging 120×80 DefaultButOverridable, BLOCKPALLET → Packaging 120×100 DefaultButOverridable, PALLET/CRATE/BOX/ROLLCONTAINER/CONTAINER/DRUM/PARCEL/COLLI/PIECE/DOCUMENT/OTHER → Packaging/Other Variable, KG/TON → Weight, LOADINGMETER → Capacity. **These are seed data, not logic.**
- Stamgegevens UI: new page `features/master-data/pages/UnitTypesPage.tsx` (route `/master-data/eenheden`, menu entry next to existing lookups) with full CRUD incl. dimensions, behaviour, physical defaults, flags, active, sort; also reused inside PricingSettingsPage 'Eenheden' tab (shared component `UnitTypeMasterEditor`).

### B2. Customer unit configuration (`CustomerPreferredUnit` — extend)

```csharp
public class CustomerPreferredUnit : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid UnitTypeId { get; set; }
    public int SortOrder { get; set; }
    public string? CustomerLabel { get; set; }   // customer-facing name override
    public string? EdiCode { get; set; }         // external EDI unit code for this customer
    public string? ExcelCode { get; set; }       // external Excel/import code
    public bool IsFavourite { get; set; } = true;
}
```

- Row existence = "customer unit configured". Global unit is never duplicated.
- `CustomerPricingConfigDto.PreferredUnits` extended with the new fields; save request becomes `IReadOnlyList<SaveCustomerUnitRequest>(UnitTypeId, SortOrder, CustomerLabel, EdiCode, ExcelCode, IsFavourite)` (replaces `PreferredUnitTypeIds`).
- EDI: `EdiService` cargo mapping resolves an incoming unit string → customer `EdiCode` (case-insensitive) → fallback global `UnitType.Code` → sets `QuantityUnitCode`.

### B3. Pricing agreements + rules (`Modules/Tarification/Entities`)

```csharp
public class PricingAgreement : AuditableTenantEntity
{
    public Guid? CustomerId { get; set; }          // null = company-wide
    public string Name { get; set; } = string.Empty;   // e.g. "Distributie België 2026-Q4"
    public string Currency { get; set; } = "EUR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? MinimumAmount { get; set; }    // minimum on the agreement subtotal
    public string? Notes { get; set; }             // optional commercial background
    public Guid? LegacyRateCardId { get; set; }    // conversion idempotency marker
    public List<PricingAgreementSurcharge> Surcharges { get; set; } = new();
}

public class PricingAgreementSurcharge : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SurchargeKind Kind { get; set; }        // Percent (of agreement subtotal) | Fixed
    public decimal Value { get; set; }
}
```

`PriceRule` additions (existing fields unchanged):

```csharp
public Guid? AgreementId { get; set; }        // null = standalone rule (current behaviour)
public int Priority { get; set; }             // explicit tie-breaker, higher wins; default 0
public decimal? BaseAmount { get; set; }      // added before the computed amount (e.g. km base cost)
// Billable-quantity contract fields (ch. 11): an item is oversized when it exceeds a threshold
public decimal? OversizeLengthCm { get; set; }
public decimal? OversizeWidthCm { get; set; }
public decimal? OversizeBillableFactor { get; set; }   // e.g. 2 => counts as 2 pallet places
```

`PriceRuleBasis` additions: `PerKm = 5` (UnitPrice × DistanceKm), `PerPallet = 6` (UnitPrice × PalletCount), `PerTon = 7` (UnitPrice × WeightKg/1000). These are order-measure bases with `UnitTypeId = null`, used by converted rate cards and configurable directly. km/hour/ldm/stop pricing via unit lines remains: PerUnit/Hourly/QuantityBracket rules on KM/HOUR/LOADINGMETER/STOP units (degressive stops = QuantityBracket with `PricePerExtraUnit`).

Tables: `pricing_agreements`, `pricing_agreement_surcharges`; `price_rules` gets nullable FK `agreement_id`, `priority` (default 0), `base_amount`, `oversize_length_cm`, `oversize_width_cm`, `oversize_billable_factor`.

### B4. Engine algorithm (`PricingEngine.CalculateAsync` rewrite)

Inputs (extended): `PriceCalculationLineInput(Guid UnitTypeId, decimal Quantity, IReadOnlyList<PriceCalculationLineDetail>? Details)` where `PriceCalculationLineDetail(decimal Quantity, decimal? LengthCm, decimal? WidthCm)`; request unchanged otherwise (Date = tariff date).

1. Resolve zone (unchanged).
2. Load candidate rules: tenant + active + effective at date + (CustomerId null or match) + (agreement null OR (agreement active + effective at date + customer null/match)). Include agreement.
3. **Unit lines** — per request line, candidates with `UnitTypeId == line.UnitTypeId` and `ZoneId null or == zone`. Specificity score = `(CustomerId != null ? 4 : 0) + (ZoneId != null ? 2 : 0)`. Sort by score desc, `Priority` desc. If the top (score, priority) group has >1 rule → **configuration error**: breakdown line `Conflicterende tariefregels voor {unit}: {names}`, `RequiresManualPrice = true`, `ConfigurationError` set; skip line. Else compute:
   - Billable quantity: if rule has oversize fields and line has Details: `billable = Σ(d.Quantity × (oversized(d) ? factor : 1)) + max(0, line.Quantity − Σ d.Quantity)`; oversized(d) = `(OversizeLengthCm != null && d.LengthCm > OversizeLengthCm) || (OversizeWidthCm != null && d.WidthCm > OversizeWidthCm)`. Else billable = actual. When billable ≠ actual add informational explanation line (`Buitenmaat: {n} × {unit} telt als {billable} ({factor}× palletplaatsen)` style).
   - Amount by basis using **billable** quantity (PerUnit/Hourly/QuantityBracket; WeightBracket uses `WeightKg`; Fixed ignores qty) + `BaseAmount` + per-rule `MinimumAmount`.
   - Breakdown line: label `{qty} × {unitName}{zoneSuffix}`, source `{agreement.Name › }{rule.Name}`, plus `RuleId/RuleName/AgreementId/AgreementName/ActualQuantity/BillableQuantity` fields.
4. **Order-level rules** (`UnitTypeId == null`, basis ∈ {Fixed, PerKm, PerPallet, PerTon, WeightBracket}):
   - Agreement-grouped: choose applicable agreement (customer-specific beats company-wide; >1 equally specific applicable agreements with order-level rules → configuration error). Apply **all** its order-level rules; a rule whose measure is missing (e.g. DistanceKm null) adds informational line `"{name}: overgeslagen (geen afstand gekend)"`.
   - Standalone: per basis, same specificity/priority/ambiguity selection as unit lines.
5. **Agreement post-processing**: per agreement with contributing lines: subtotal of its non-informational lines → if `MinimumAmount` > subtotal, add line `Minimumtarief {agreement.Name}` for the difference; then surcharges: Percent → `subtotal × value/100`, Fixed → value (source `Toeslag {agreement.Name}`).
6. Service options + diesel: unchanged.
7. **No tariff at all** (no rule matched anywhere and lines exist): `RequiresManualPrice = true`; first breakdown line `Geen geldig tarief gevonden voor deze order` + `Diagnostics` list (klant, tariefdatum, eenheid+aantal per line, gewicht, palletplaatsen, leverpostcode/zone). Never returns a usable total of 0.
8. Result (extended): `TariffDate`, `ConfigurationError?`, `Diagnostics`, per-line rule/agreement/billable info, existing fields intact. `Total` only meaningful when `!RequiresManualPrice`.

`PriceBreakdownLine` extended with optional `RuleId`, `RuleName`, `AgreementId`, `AgreementName`, `ActualQuantity`, `BillableQuantity` (defaults null — existing constructor calls stay valid).

### B5. Legacy RateCard conversion + retirement

- Idempotent C# backfill `RateCardConversionService` run from the startup seeding pipeline (same place ReferenceDataSeeder-style backfills run): for each `RateCard` without a `PricingAgreement.LegacyRateCardId == card.Id`: create agreement (CustomerId, Name, Currency, EffectiveFrom/Until, MinimumAmount, Notes) + surcharge rows + rules: `BaseAmount > 0` → Fixed "Basisbedrag" (UnitPrice = BaseAmount); `PerKmRate` → PerKm "Kilometertarief"; `PerPalletRate` → PerPallet "Palletprijs"; `PerTonRate` → PerTon "Tonprijs" (all UnitTypeId null, AgreementId set, EffectiveFrom/Until copied).
- Then delete: fallback block in `PricingEngine`, `IRateCardService`/`RateCardService`, `RateCardsController`, `rateCardsApi.ts`, `RateCardsPage.tsx`, `CustomerRateCardsPanel.tsx`, related routes/menu/tests (replaced by conversion tests). `RateCard` entity + tables remain (data preservation, no drop).

### B6. Scheduled price adjustments

```csharp
public enum ScheduledAdjustmentStatus { Scheduled = 0, Cancelled = 1 }   // "actief" = Scheduled && EffectiveDate <= today (computed)

public class ScheduledPriceAdjustment : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal Percent { get; set; }                 // +4.00 / -2.50
    public ScheduledAdjustmentStatus Status { get; set; } = ScheduledAdjustmentStatus.Scheduled;
    public string? Reason { get; set; }
    public List<ScheduledPriceAdjustmentRule> Rules { get; set; } = new();
}

public class ScheduledPriceAdjustmentRule : AuditableTenantEntity
{
    public Guid AdjustmentId { get; set; }
    public Guid SourcePriceRuleId { get; set; }
    public Guid CreatedPriceRuleId { get; set; }
    public DateOnly? SourceOriginalEffectiveUntil { get; set; }   // restore on cancel
}
```

Workflow (service `PriceAdjustmentService`, endpoints on `PricingController`):
- `POST /api/customers/{customerId}/price-adjustments/preview` body `(EffectiveDate, Percent, RuleIds?: Guid[])` (null RuleIds = all active+future-effective rules of the customer, incl. agreement rules). Returns per rule: name, unit, field-by-field old→new (UnitPrice, BaseAmount, MinimumAmount, brackets Price/PricePerExtraUnit) rounded AwayFromZero 2dp. Rules already ending before EffectiveDate are excluded.
- `POST /api/customers/{customerId}/price-adjustments` same body + `Reason?` → materializes now: per source rule: clone → `EffectiveFrom = EffectiveDate`, `EffectiveUntil = source.EffectiveUntil`, adjusted amounts + cloned brackets; set `source.EffectiveUntil = EffectiveDate.AddDays(-1)`. Persist adjustment + rule links. Audit `RecordAsync("ScheduledPriceAdjustment", id, "created", …)`. Validation: EffectiveDate > today; Percent ≠ 0, |Percent| ≤ 100; source rule must start before EffectiveDate (rules starting on/after it are rejected from scope with a clear message).
- `GET /api/customers/{customerId}/price-adjustments` → list with computed display status (Gepland / Actief / Geannuleerd).
- `POST /api/customers/{customerId}/price-adjustments/{id}/cancel` — only while `EffectiveDate > today` and Status == Scheduled: delete created rules, restore each source's `EffectiveUntil`, Status = Cancelled, audit. (Edit = cancel + create new, UI flow.)
- Price history is automatic: superseded rules keep their closed windows; customer detail shows past/current/future versions.

### B7. Snapshot header + order integration

New entity `TransportOrderPricingSnapshot` (`Modules/Orders/Entities/TransportOrderPricing.cs`, table `order_pricing_snapshots`, 1:1 by `TransportOrderId` unique index):

```csharp
public class TransportOrderPricingSnapshot : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }
    public DateOnly TariffDate { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? ZoneCode { get; set; }
    public string? ZoneName { get; set; }
    public string? AgreementNames { get; set; }     // "; "-joined
    public string? UnitSummary { get; set; }        // "3 × Europallet (factureerbaar 3)"
    public decimal? CalculatedTotal { get; set; }
    public decimal? OverrideAmount { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverriddenByUserId { get; set; }
    public DateTime? OverriddenAtUtc { get; set; }
    public string? Explanation { get; set; }        // human-readable multiline calculation text
}
```

`TransportOrderPricingLine` gains nullable `RuleName`, `AgreementName`, `ActualQuantity`, `BillableQuantity` columns. `ApplyPricingAsync`: builds line `Details` from cargo items whose `QuantityUnitCode` matches the order unit (meters → cm ×100); writes/replaces the snapshot header on every save (same lifecycle as lines — snapshots only move on explicit save, historical orders untouched); when override active fills override fields (user id + UtcNow). Detail DTO exposes the snapshot header. Invoicing continues to consume `AgreedPrice` + service lines (unchanged, verified by tests).

### B8. Frontend UX

**Stamgegevens → Eenheden** (`UnitTypesPage.tsx` + shared `UnitTypeMasterEditor.tsx`): table (Code, Naam, Categorie, Afmetingen, Gedrag, Order/Tarief flags, Actief, Volgorde) + create/edit modal: name; code (suggested live from name until touched, editable, uppercase); category select; decimals; symbol; dimension behaviour select (Variabel / Standaard, aanpasbaar / Vast); L/B/H cm; standaardgewicht/max gewicht kg; volume m³; laadmeters; palletplaatsen; beide flags; actief; sorteervolgorde. Deactivate/reactivate via actief toggle.

**Customer detail → Tarieven & toeslagen** (view + edit mode) — panels:
- `CustomerUnitsPanel` (rework of unit part of CustomerUnitPricingPanel): rows per configured unit: naam (global), klantbenaming, EDI-code, Excel-code, favoriet ★ toggle, volgorde ↑↓, verwijderen; + "Eenheid toevoegen" from global list.
- `CustomerTariffsPanel`: **Actuele prijzen** (agreements + rules effective today: naam, eenheid, berekeningswijze, zone, waarde-samenvatting, geldig van/tot); **Toekomstige prijzen** (EffectiveFrom > today); **Historiek** (collapsed; EffectiveUntil < today); rule editor extended with agreement select, priority, base amount, oversize fields, new bases.
- `CustomerPriceAdjustmentsPanel`: list (datum, ±%, status Gepland/Actief/Geannuleerd, reden, annuleren-knop) + wizard "Nieuwe prijsaanpassing": percent (+/−), effective date, scope (alle actieve tarieven / selectie), reden, **Preview** table `€45,00 → €46,80` per regel, bevestigen.
- Existing service options editor + `CustomerBillingPanel` (diesel/PO) stay in this tab.

**Transport order form**: shared `UnitSelect` component used by BOTH the order-level unit field and cargo-line unit fields: optgroup "EENHEDEN VAN KLANT …" (customer units, favourites ★ first, customer sort, customer label shown) then "ANDERE EENHEDEN" (remaining active units); switching customer re-ranks safely (selection preserved if still valid). On cargo-line unit change: dimension autofill from unit defaults (cm→m): Fixed → set + readonly; DefaultButOverridable → prefill empty fields; Variable → untouched; also prefill weight/volume defaults when empty. Prijs tab: shows Tarief (agreement), Tariefdatum, Zone, per line rule name + billable note ("3 werkelijk / 4 facturteerbaar"), config-error alert (Conflicterende tariefregels …), no-tariff alert "Geen geldig tarief gevonden voor deze order" + diagnostics; existing manual-override UI (permission + reason) and "Manueel aangepast" state unchanged.

---

## Part C — Phased tasks

### Phase 1: Unit master data (backend + migration + seeder)
**Files:** modify `Modules/Reference/Entities/UnitType.cs`, `Modules/Reference/Configurations/UnitTypeConfiguration.cs` (column precisions), `Modules/Reference/Controllers/UnitTypesController.cs` (+ master DTO/endpoints + audit), `Data/ReferenceDataSeeder.cs` (seed + idempotent backfill); new migration `UnitMasterData`; tests `TransportationService.Api.Tests/Reference/UnitTypeTests.cs` (extend).
- [ ] Entity + enums + configuration (decimal(10,2) dims etc.)
- [ ] `GET/POST/PUT /api/unit-types/master` with code validation `^[A-Z0-9_-]{2,20}$`, uppercase normalize, tenant-unique conflict → validation error, audit RecordAsync("UnitType", …); code never auto-changed on rename
- [ ] Seeder defaults/backfill (only fills untouched fields)
- [ ] `dotnet ef migrations add UnitMasterData` + inspect SQL (additive only)
- [ ] Tests: create custom unit w/ dims; edit code; duplicate code rejected; 120×100 and 100×100 pallets both configurable; dimension behaviours persisted; tenant isolation; permissions (403 without manage); backfill idempotent + non-destructive
- [ ] Build + tests green → commit `feat(units): configurable unit master data (dimensions, categories, physical defaults)`

### Phase 2: Customer unit configuration (backend + EDI)
**Files:** modify `Modules/Tarification/Entities/ServiceOption.cs` (CustomerPreferredUnit fields), `Modules/Tarification/Dtos/PricingDtos.cs`, `Modules/Tarification/Services/PricingAdminService.cs` (save/load config), `Modules/Edi/Services/EdiService.cs` (unit resolution); migration `CustomerUnitConfig`; tests `Tarification/CustomerUnitConfigTests.cs` (new), `Edi` tests (extend).
- [ ] Entity fields + migration + config (string lengths)
- [ ] DTO/save rework (`SaveCustomerUnitRequest`), audit unchanged pattern
- [ ] EDI cargo unit resolution (customer EdiCode → global code fallback)
- [ ] Tests: label/EDI/Excel/favourite/sort round-trip; same global unit shared by 2 customers with different config; EDI code resolves per customer; global-code fallback; tenant isolation
- [ ] Commit `feat(customers): customer unit configuration (labels, EDI/Excel codes, favourites)`

### Phase 3: Pricing agreements + rule model extensions
**Files:** new `Modules/Tarification/Entities/PricingAgreement.cs`; modify `PriceRule.cs` (new fields + bases), `Data/TransportationDbContext.cs` (DbSets), new configurations, `PricingDtos.cs`, `PricingAdminService.cs` (agreement CRUD + extended rule CRUD + validation: oversize fields require factor ≥ 1 & at least one threshold, priority int, agreement tenant/customer consistency), `PricingController.cs` (`GET/POST /api/pricing/agreements`, `PUT/DELETE /api/pricing/agreements/{id}`, rules accept new fields); migration `PricingAgreements`; tests `Tarification/PricingAgreementTests.cs`.
- [ ] Entities + migration (inspect SQL) + DTOs + CRUD + audit ("PricingAgreement")
- [ ] Tests: agreement CRUD + validation + tenant isolation; rule with agreement/priority/base/oversize persists
- [ ] Commit `feat(pricing): pricing agreements, rule priority, base amount + order-measure bases`

### Phase 4: Engine rewrite (deterministic precedence, billable qty, agreements, diagnostics)
**Files:** rewrite `Modules/Tarification/Services/PricingEngine.cs` per B4; extend `PricingDtos.cs` (line details, result fields, breakdown-line fields); tests `Tarification/PricingEngineTests.cs` (extend heavily).
- [ ] Implement algorithm B4 (keep existing public surface compatible; `IRateCardService` dependency still present until Phase 5)
- [ ] Tests: quantity bracket picks €105 not 3×€50; per-unit with minimum (2×22→60); weight bracket; hourly; fixed; zone rule wins; customer beats company; priority breaks specificity ties; **exact tie → configuration error naming both rules**; billable oversize 1 actual → 2 billable (160×120 vs threshold 125×85) and physical quantity unchanged; per-rule minimum; base amount; PerKm/PerPallet/PerTon; agreement minimum + percent/fixed surcharges; missing measure → informational skip; no tariff → RequiresManualPrice + "Geen geldig tarief gevonden" + diagnostics content; Klant A ≠ Klant B price for identical shipment; effective-window versioning (old date → old price, new date → new price)
- [ ] Commit `feat(pricing): deterministic explainable engine — precedence, ambiguity, billable quantity, agreements`

### Phase 5: RateCard conversion + legacy retirement
**Files:** new `Modules/Tarification/Services/RateCardConversionService.cs` + startup hook; delete fallback in engine, `RateCardService.cs`, `RateCardsController.cs`; FE delete `rateCardsApi.ts`, `RateCardsPage.tsx`, `CustomerRateCardsPanel.tsx`, routes/menu refs, `customerRateCardsPanel.test.tsx`; adapt `RateCardServiceTests.cs` → `RateCardConversionTests.cs`; keep entity + tables.
- [ ] Conversion service (idempotent via LegacyRateCardId) + tests: card converts to agreement+rules with identical quote for a sample order (base+pallet+ton+surcharge+minimum); re-run adds nothing; tenant isolation
- [ ] Remove fallback & legacy service/controller/UI/tests; fix compile
- [ ] Commit `refactor(pricing): convert legacy rate cards to pricing agreements; single engine`

### Phase 6: Order integration + snapshot header
**Files:** modify `Modules/Orders/Entities/TransportOrderPricing.cs` (+snapshot entity, line fields), `Configurations/OrderPricingConfigurations.cs`, `TransportOrderService.cs` (`ApplyPricingAsync`: details from cargo, snapshot header, override user/time), order detail DTO + controller; migration `OrderPricingSnapshotHeader`; tests `Orders/OrderPricingTests.cs` (extend).
- [ ] Entity + migration + apply logic + DTO
- [ ] Tests: snapshot header created on save (tariff date, zone, unit summary, explanation); override stores user/timestamp/original amount; historical snapshot unchanged after later rule change (existing test extended to header); cargo dims flow into billable quantity end-to-end; invoice still consumes snapshot (existing invoicing tests still green)
- [ ] Commit `feat(orders): pricing snapshot header + billable quantity from cargo dimensions`

### Phase 7: Scheduled price adjustments (backend)
**Files:** new `Modules/Tarification/Entities/ScheduledPriceAdjustment.cs`, `Services/PriceAdjustmentService.cs`; `PricingController.cs` endpoints (B6); DbSets + configurations; migration `ScheduledPriceAdjustments`; tests `Tarification/PriceAdjustmentTests.cs`.
- [ ] Entities + migration + service + endpoints + audit
- [ ] Tests: +4% preview values & rounding (45→46.80, 70→72.80, 90→93.60, 72→74.88); −2.5%; confirm creates future rules & closes source windows (source until = date−1); engine uses old price before date, new on/after; current+history preserved; cancel restores windows & deletes future rules; cannot cancel once active; validation (past date, 0%, |%|>100); audit rows; tenant isolation
- [ ] Commit `feat(pricing): scheduled future price adjustments with bulk percentage workflow`

### Phase 8: Customer detail commercial overview (frontend)
**Files:** rework `features/customers/components/CustomerUnitPricingPanel.tsx` → `CustomerUnitsPanel.tsx` + `CustomerTariffsPanel.tsx` + `CustomerPriceAdjustmentsPanel.tsx`; `CustomerDetailPage.tsx` tab render (view+edit); `features/tarification/api/pricingApi.ts` (types + agreements + adjustments + unit master); tests `customerPricingPanels.test.tsx`.
- [ ] Panels per B8 + api client
- [ ] Tests: units panel edits label/EDI/Excel/favourite/sort; tariffs grouped current/future/history; adjustment wizard preview shows old→new and confirms; cancel button; permission gating (tariffs.view/manage)
- [ ] Commit `feat(customers): commercial overview — units, tariffs current/future/history, price adjustments`

### Phase 9: Master-data unit UX + order form UX (frontend)
**Files:** new `features/master-data/pages/UnitTypesPage.tsx` + `UnitTypeMasterEditor.tsx` + route/menu; `PricingSettingsPage.tsx` 'eenheden' tab → shared editor; new `features/transport-orders/components/UnitSelect.tsx`; modify `TransportOrderForm.tsx` (grouped selects both levels, dimension autofill, Prijs tab: tariff date/agreement/billable/config-error/no-tariff alerts); tests: `unitTypesPage.test.tsx`, extend `transportOrderSectionedForm.test.tsx`.
- [ ] Unit master page (code suggestion: derived from name until code touched; never re-suggest on edit)
- [ ] UnitSelect + autofill + breakdown UI
- [ ] Tests: suggested code editable; customer units + favourites first, others below; other units selectable; dimensions autofill by behaviour (Fixed readonly, Default prefilled-editable); customer switch re-ranks; breakdown shows rule/zone/billable; config-error + no-tariff alerts; override still gated by `orders.override_price`
- [ ] Commit `feat(web): unit master data UI + grouped order unit selector with autofill`

### Phase 10: Final verification
- [ ] `dotnet build` (0 warnings-as-errors), full `dotnet test`
- [ ] `npx tsc --noEmit`, `npx eslint`, full `npx vitest run`, `npm run build`
- [ ] Worktree clean, commit list, final report per prompt §28

---

## Part D — Spec/test coverage self-check

Prompt §27 matrix → Phase: master-data units (P1/P9), customer units (P2/P8), order unit selector (P9), pricing methods & precedence & billable & ambiguity & no-tariff (P4), rate-card parity (P5), future changes (P7/P8), transport order auto-pricing/override/snapshot (P6), invoice-from-snapshot (P6, existing invoicing tests). Spec chapters 1–21 all mapped; ch.9 "three primary pricing models to be fixed later" → covered by the general basis model (no extra work needed now).
