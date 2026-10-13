# First-Class Pricing Module — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing Tarification engine into the full pricing module of the spec: reusable rate tables shared across customers, derived tables (NL = BE +30%), multidimensional brackets, a pricing management UI with templates + spreadsheet editing + Excel round-trip, warehouse/logistics service charges with automatic quantities, one-off order pricing with included loading/unloading time, line-level manual price editing with preserved originals, price status/locking, and combined-unit degression — without building a second engine.

**Architecture:** `PricingAgreement` **is** the spec's "rate table" (no new RateTable entity). We add: sharing + per-customer assignments with adjustments, derivation (base agreement + ordered modifiers), richer brackets, service calculation bases, order pricing-line persistence with manual-edit merge and status, and degression rules. One deterministic engine (`PricingEngine`) stays the single calculation path; all new layers produce explainable breakdown lines.

**Tech Stack:** .NET 10 API (EF Core 10 + Npgsql, additive migrations, `AuditableTenantEntity`, manual tenant filters, `IAuditService.RecordAsync`, `DomainValidationException`→ProblemDetails, ClosedXML), React 19/Vite/TS (hand-written api modules over `apiClient`, Vitest), xUnit + in-memory SQLite tests.

## Global Constraints

- Additive migrations only (`TransportationService.Api/Migrations`); never edit historical migrations; never destroy data.
- Tenant isolation is **manual per-query** (`.Where(x => x.TenantId == _tenantContext.TenantId)`) — every new query must filter; cross-tenant references throw `InvalidTenantReferenceException` or `DomainValidationException`.
- Permissions via `[RequirePermission(...)]` + `PermissionCodes` consts. New codes this wave: `orders.lock_price` (role upgrade **v14**), `tariffs.import` (role upgrade **v15**). Never hardcode role names.
- Audit every admin mutation via `IAuditService.RecordAsync(entityType, id, action, oldValues, newValues, ct)` (existing entity-type strings pattern).
- Money: `decimal`, rounding `decimal.Round(x, 2, MidpointRounding.AwayFromZero)` at line level (engine already does plain `decimal.Round(x, 2)` = banker's — **keep the existing behavior for existing lines; new code uses the same `decimal.Round(x, 2)` call for consistency**, documented in docs/pricing.md).
- Tariff/effective date = `order.OrderDate` (existing engine input; documented decision, spec §50).
- Dutch UI copy consistent with existing screens ("Tarieventabellen", "Prijsafspraken", "Staffels", "Toeslag", "Geldig van/tot").
- Never silently price €0; ambiguity is a blocking `ConfigurationError`, never an arbitrary pick.
- Engine ordering (canonical, documented, spec §33): **1)** base rule amount (incl. derivation modifiers in sequence) → **2)** combined-unit degression → **3)** assignment adjustment (±% / fixed) → **4)** agreement minimum/maximum → **5)** agreement surcharges → **6)** service options → **7)** one-off / proposed time charges → **8)** manual line edits → **9)** snapshot. Percentages compound multiplicatively; each layer is its own breakdown line.
- Frontend: no new styling framework; reuse `Modal`, `FormField`, inline-`onBlur` table editing pattern, `EmptyState`/`LoadingState`/`ErrorState`, `useToast`, permission gating via `useAuth()`.
- All existing tests keep passing; backend build has no new warnings; FE `tsc -b`, `eslint`, `vitest run`, `vite build` pass.

---

## Part A — Repository audit conclusions (gap analysis)

**Already exists (reuse, do not rebuild):** single engine `Modules/Tarification/Services/PricingEngine.cs` (deterministic specificity scoring customer=4/zone=2 + Priority, exact tie = blocking error); `PricingAgreement` (+Percent/Fixed surcharges, MinimumAmount, effective windows) + `PriceRule` (11 bases: PerUnit, QuantityBracket, WeightBracket, Hourly incl. `MinimumQuantity`+`QuantityRoundingStep`, Fixed, PerKm, PerPallet, PerTon, PerLoadingMeter, PerVolume, PerStop; `BaseAmount`, `MinimumAmount`, oversize billable factors) + `PriceRuleBracket` (open-ended + `PricePerExtraUnit`); `PricingZone` postcode zones; `ServiceOption` (Fixed/Percent/PerHour/PerStop) + `CustomerServiceOptionPrice` overrides (inherit/disable/minimum/dates); `ScheduledPriceAdjustment` ±% with preview/cancel (customer scope); order snapshot (`TransportOrderPricingSnapshot` + pricing/service lines) written on save only, whole-order manual override (`orders.override_price` + reason + user/time), invoice-from-snapshot; audit + permission + role-upgrade infra; ClosedXML import pattern (`CustomerImportService` + `CustomerImportDialog`); order stops with `StopExecution` actuals (`ArrivedAt`/`CompletedAt`/`DepartedAt`); `CargoItem.UnloadingStopId` (per-address grouping is feasible).

**Gaps to build (spec §):** shared/assignable tables + "used by X customers" (§5,7,16,35); derived tables + stacking + cycle detection (§9); multidimensional & per-next-unit brackets + maximum charge (§10.4, 28.3, §11-max); pricing UI area with templates + spreadsheet editing (§11,12,40); bulk adjustment v2: agreement scope, fixed deltas, rounding options, duplicate-as-new-version (§14); Excel round-trip for tariffs (§15); warehouse service bases + auto-apply + auto quantities + ADR condition (§17-19,51); one-off order pricing + included load/unload time + actual-time charges as proposals (§21,22,42-44); line-level manual pricing with preserved originals + free lines (§24,25); pricing status/locking + explicit recalculation (§26); combined-unit degression, weighted, scoped per delivery address (§28.2,29-31); circular/overlap validation sweep + docs (§38,58).

**Explicit non-goals / documented decisions:** no product-catalog stock conditions for services (no customer-goods inventory subsystem exists — only ADR flag conditions; documented integration point); no bracket-level zones ("zone × staffel" = one rule per zone, rendered as a matrix in the UI); no arbitrary expression trees; diesel surcharge stays invoicing-owned (informational line); whole-order override (`PriceIsManual`) remains for backward compatibility next to line-level editing.

---

## Part B — Target domain model & engine changes

### B1. Shared tables + assignments (Phase 1)

`PricingAgreement` additions:

```csharp
public bool IsShared { get; set; }                 // true = reusable table, applies only via assignments
public decimal? MaximumAmount { get; set; }        // cap on agreement subtotal (Phase 3 uses it too)
public Guid? BaseAgreementId { get; set; }         // Phase 2 (derivation)
public PricingAgreement? BaseAgreement { get; set; }
public List<PricingAgreementModifier> Modifiers { get; set; } = new();      // Phase 2
public List<PricingAgreementAssignment> Assignments { get; set; } = new();  // Phase 1
public int? IncludedLoadingMinutes { get; set; }   // Phase 6
public int? IncludedUnloadingMinutes { get; set; } // Phase 6
public int? IncludedCombinedMinutes { get; set; }  // Phase 6
public decimal? ExtraHourlyRate { get; set; }      // Phase 6
```

New entity (same file `Modules/Tarification/Entities/PricingAgreement.cs`):

```csharp
public class PricingAgreementAssignment : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }
    public PricingAgreement? Agreement { get; set; }
    public Guid CustomerId { get; set; }
    public decimal? PercentAdjustment { get; set; }   // -5 = 5% korting op de lijnen van deze tabel
    public decimal? FixedAdjustment { get; set; }     // vast bedrag per order bovenop de tabel
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string? Notes { get; set; }
}
```

**Semantics (documented):** `CustomerId != null` → private table (unchanged). `CustomerId == null && !IsShared` → company default for everyone (unchanged). `CustomerId == null && IsShared` → applies **only** to customers with an active assignment on the tariff date. Validation: `IsShared` requires `CustomerId == null`; an assignment requires the agreement to be shared; one active assignment per (agreement, customer) at a time (overlap = validation error).

**Engine precedence rework** (`SelectRule`): specificity tier `private = 2, assigned = 1, companyDefault = 0`; score = `tier * 4 + (ZoneId != null ? 2 : 0)`, then `Priority`, exact tie still blocks. Candidate loading adds: rules of shared agreements whose assignment (customer + tariff-date window) is active; shared-agreement rules without an assignment are excluded. Standalone rules keep tier private/companyDefault by `CustomerId`.

**Assignment adjustment application** (engine, agreement post-processing, before minimum): per engaged agreement with an active assignment: `PercentAdjustment` → line `"Klantafspraak {p:+0.##;-0.##}%"` = `Round(subtotal * p/100, 2)`; `FixedAdjustment` → line `"Klantafspraak vast bedrag"`. Then minimum/maximum top-up/cap, then surcharges (on the adjusted subtotal).

Endpoints (`PricingController`, all `TariffsManage`, list also `TariffsView`): `GET /api/pricing/agreements/{id}/assignments`, `PUT /api/pricing/agreements/{id}/assignments` (full replace list), plus `GET /api/pricing/agreements` gains `IsShared`, `CustomerCount` (active assignments), `CustomerNames` summary. Audit entity type `"PricingAgreementAssignment"`.

### B2. Derived agreements (Phase 2)

```csharp
public class PricingAgreementModifier : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }          // the derived agreement
    public int Sequence { get; set; }              // stacking order, applied ascending
    public string Name { get; set; } = string.Empty;   // "Nederland +30%", "Waddeneilanden +€75"
    public string? CountryCode { get; set; }       // optional condition: delivery country
    public Guid? ZoneId { get; set; }              // optional condition: resolved delivery zone
    public decimal? Percent { get; set; }          // +30 / -5 — of the running subtotal
    public decimal? FixedAmount { get; set; }      // +75.00
}
```

Rules: a derived agreement (`BaseAgreementId != null`) **may not have own PriceRules** (validation on rule create + agreement save). Base chain max depth 3; cycle detection on save (walk `BaseAgreementId` chain) → `DomainValidationException "Circulaire verwijzing tussen tarieventabellen"`; runtime guard: visited-set, on cycle → `ConfigurationError`. Engine: when a derived agreement is applicable (private/assigned/default via B1 semantics), its **effective rules are the base chain root's rules**; the base line amount is computed normally (labelled with the base rule), then per modifier (ascending `Sequence`, condition matches delivery country/zone; null condition = always) a separate breakdown line `Name` = `Round(runningSubtotal * Percent/100, 2)` or `FixedAmount`, tagged with the derived agreement. Modifier lines participate in the derived agreement's subtotal for assignment adjustment/minimum/surcharges. Endpoints: modifiers managed inside `PUT /api/pricing/agreements/{id}` payload (list, like surcharges). Audit via existing `"PricingAgreement"` entries.

### B3. Bracket power-ups + maximum charge (Phase 3)

`PriceRuleBracket` additions: `decimal? WeightToKg`, `decimal? VolumeToM3`, `decimal? LoadingMetersTo`. `PriceRule` additions: `decimal? MaximumAmount` and

```csharp
public enum BracketSelectionMode { Absolute = 0, PerNextUnit = 1 }
public BracketSelectionMode BracketMode { get; set; } = BracketSelectionMode.Absolute;
```

`BracketAmount` rework: a bracket matches when the quantity range matches **and** every filled cap holds (`request.WeightKg <= WeightToKg` etc.); among matches keep the existing "last by FromQuantity" then smallest caps first (order: `WeightToKg`, `VolumeToM3`, `LoadingMetersTo`, nulls last) so carrier tables ("kg tot / cbm tot / ldm tot / prijs") pick the tightest row. `PerNextUnit` mode: `amount = Σ_{i=1..floor(qty)} bracketPriceContaining(i)` + fractional remainder at the last unit's rate (1st pallet €60, 2nd €55, 3rd €50, 4th+ €45). `MaximumAmount` caps the rule amount after minimum (min wins if min > max is a validation error). `PricingAdminService` validation: From > To, negative price, overlapping same-dimension brackets, `PerNextUnit` requires gapless brackets from 1.

### B4. Pricing UI area + bulk adjustment v2 (Phase 4)

Nav: new module **«Prijzen»** in `navConfig.ts` (perms `tariffs.view`/`tariffs.manage`): `Tarieventabellen` → `/pricing/tables`; `Prijsinstellingen` moves here (route stays `/settings/pricing`). New pages `src/features/tarification/pages/PricingTablesPage.tsx` (list: Naam, Type/samenstelling, Geldigheid, Status Actief/Toekomstig/Verlopen, «Gebruikt door N klanten», laatst gewijzigd) and `PricingTableDetailPage.tsx` (`/pricing/tables/:id`): header + warning "Deze tabel wordt gebruikt door N klanten." (§35), tabs/panels: **Regels** (spreadsheet-style inline table: row per rule/bracket, click-cell edit with onBlur save, add/duplicate/delete row, sticky header, unit/currency visible), **Afleiding** (base table + modifiers, Phase 2 UI), **Toeslagen**, **Klanten** (assignments: + Klant koppelen with ±%/vast bedrag), **Prijsaanpassing** (bulk v2), **Import/Export** (Phase 9). Creation wizard modal "Nieuwe tarieventabel" with template cards (§11): Uurtarief, Palletstaffel, Gewichtsstaffel, Laadmeter, Zone-tabel, Afstand (km), Vaste prijs, Gecombineerd, Leeg starten, Excel importeren — each pre-creates an agreement + prefilled rule skeletons.

Bulk adjustment v2 (backend): `ScheduledPriceAdjustment` additions: `Guid? AgreementId` (nullable `CustomerId` becomes agreement-scope alternative — exactly one of the two set), `decimal? AmountDelta` (XOR `Percent`; fixed ±€ on UnitPrice/BaseAmount/bracket prices), `decimal? RoundingStep` (null/0.01/0.05/0.10 — `Math.Round(value / step) * step` after adjustment), `string? BasisFilter` (comma-joined `PriceRuleBasis` names), `Guid? UnitTypeIdFilter`. New endpoints mirror the customer ones: `GET/POST /api/pricing/agreements/{id}/price-adjustments` + `/preview` + `/{adjustmentId}/cancel`. Plus **duplicate-as-new-version**: `POST /api/pricing/agreements/{id}/duplicate` body `(Name, EffectiveFrom, CloseSource: bool, Percent?, AmountDelta?, RoundingStep?)` → copies agreement + rules + brackets + surcharges + modifiers with new windows, optionally closes source `EffectiveUntil = EffectiveFrom.AddDays(-1)`, applies adjustment, returns new id. Audit `"PricingAgreement" duplicated`.

### B5. Warehouse/logistics services (Phase 5)

`SurchargeKind` additions (string-stored enum — additive values safe): `PerUnit = 4, PerOrderLine = 5, PerKg = 6, PerM3 = 7, PerLdm = 8, PerDay = 9, PerPalletDay = 10`. `ServiceOption` additions: `Guid? UnitTypeId` (required for PerUnit — which unit it counts), `bool AutoApply` (default false — engine adds it without manual selection), `bool OnlyForAdr` (default false). `CustomerServiceOptionPrice` addition: `bool? AutoApplyOverride` (null = inherit).

Engine changes: service resolution loads **selected options ∪ auto-apply options** (global `AutoApply` or customer override true, active, not disabled for customer, `OnlyForAdr` → only when `request.AdrRequired == true`; request gains `bool? AdrRequired`, `int? CargoLineCount`). Auto quantity per kind: `PerUnit` → Σ request line quantity of that `UnitTypeId` (0 → informational skip); `PerOrderLine` → `CargoLineCount`; `PerKg` → `WeightKg`; `PerM3` → `VolumeM3`; `PerLdm` → `LoadingMeters`; `PerDay`/`PerPalletDay` → entered quantity required (like PerHour today). Entered quantity always wins over the derived one. Labels: `"{name} ({qty:0.##} {unitLabel})"`. `ApplyPricingAsync` passes `AdrRequired = order.AdrRequired`, `CargoLineCount = cargoItems.Count(!IsDeleted)`. Agreement surcharges stay Percent|Fixed only (existing validation). FE: `ServiceOptionsEditor` gains the new kinds ("Per eenheid…", "Per orderlijn", "Per kg", "Per m³", "Per laadmeter", "Per dag", "Per pallet/dag"), unit select for PerUnit, checkboxes "Automatisch toepassen" and "Alleen bij ADR"; customer override panel gains auto-apply override; order form shows auto-applied services read-only with source "Automatisch (contract)".

### B6. One-off pricing + included time + proposed charges (Phase 6)

`TransportOrder` additions:

```csharp
public enum OrderPricingSource { Contract = 0, OneOff = 1 }
public OrderPricingSource PricingSource { get; set; } = OrderPricingSource.Contract;  // string-stored
public decimal? OneOffFixedAmount { get; set; }
public int? OneOffIncludedLoadingMinutes { get; set; }
public int? OneOffIncludedUnloadingMinutes { get; set; }
public int? OneOffIncludedCombinedMinutes { get; set; }
public decimal? OneOffExtraHourlyRate { get; set; }
public string? OneOffNotes { get; set; }
```

`PriceCalculationRequest` additions: `OneOffPricingInput? OneOff` (`record OneOffPricingInput(decimal FixedAmount, int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, int? IncludedCombinedMinutes, decimal? ExtraHourlyRate, string? Notes)`), `decimal? ActualLoadingMinutes`, `decimal? ActualUnloadingMinutes`. When `OneOff != null` the engine **skips rules/agreements entirely** (services still apply): line `"Eenmalige prijsafspraak"` (+ Notes in Source), then extra-time lines. Extra time (both one-off and contract-agreement included-time, same helper): combined mode when `IncludedCombinedMinutes != null` → `extra = max(0, actualLoad + actualUnload − included)`; else per-activity `max(0, actual − included)` per side; `amount = Round(extraMinutes / 60m * rate, 2)` (rate = `ExtraHourlyRate` required — missing rate with extra time → informational "geef uurtarief extra tijd op"). Extra-time lines are **Proposed** (`PriceBreakdownLine` gains `bool Proposed` — excluded from `Total`, shown with `TotalWithProposed` result field; spec §44: never silently invoiceable). Actuals: `ApplyPricingAsync` sums per stop type from `StopExecution` (`ArrivedAt`→`DepartedAt ?? CompletedAt`, minutes, only completed-ish statuses) via the order's stops; null when no executions. Actual vs billable both shown (§43) — actuals go in the line label ("Laden 1u00, inbegrepen 0u30 → 0,5 u extra"). FE order form Prijs section: radio "Prijsbron: ( ) Klantcontract (•) Eenmalige prijsafspraak" (spec §41), one-off fieldset (fixed price, included minutes separate/combined toggle, extra rate, notes), proposed lines rendered with badge "VOORSTEL" + confirm action (Phase 7 wires confirmation to line editing).

### B7. Line-level manual pricing + status/locking (Phase 7)

`TransportOrderPricingLine` additions:

```csharp
public enum OrderPriceLineKind { Auto = 0, AutoAdjusted = 1, Manual = 2, Proposed = 3 }  // string-stored
public OrderPriceLineKind Kind { get; set; } = OrderPriceLineKind.Auto;
public decimal? Quantity { get; set; }
public decimal? UnitPrice { get; set; }
public decimal? OriginalQuantity { get; set; }
public decimal? OriginalUnitPrice { get; set; }
public decimal? OriginalAmount { get; set; }
public string? AdjustReason { get; set; }
public Guid? RuleId { get; set; }
public Guid? ServiceOptionId { get; set; }
public string? LineKey { get; set; }   // stable merge key: "rule:{id}" | "service:{id}" | "manual:{guid}" | "modifier:{id}" ...
```

`TransportOrderPricingSnapshot` addition: `public enum OrderPricingStatus { Draft = 0, Reviewed = 1, Locked = 2, Invoiced = 3 }` + `Status` (string-stored, default Draft) + `decimal? LinesTotal`.

Behavior:
- `ApplyPricingAsync` merge instead of delete-all: engine lines get `Kind=Auto` + `LineKey`; existing `Manual` lines are preserved (re-appended, sequence after auto lines); existing `AutoAdjusted` lines match by `LineKey` → keep user's `Quantity/UnitPrice/Amount/AdjustReason`, refresh `Original*` from the fresh calculation; unmatched adjusted lines become `Manual` (source keeps rule name) so nothing silently disappears. Proposed lines regenerate as Proposed unless previously confirmed (confirmed = became `AutoAdjusted`/`Auto` via the confirm endpoint).
- **Status gate:** `Locked`/`Invoiced` → `ApplyPricingAsync` performs **no** recalculation and rejects pricing-field changes (`DomainValidationException "Prijs is vergrendeld"`); `Reviewed` → recalculation allowed but FE warns first. `Invoiced` is set by the existing invoicing flow when an invoice is generated for the order.
- `AgreedPrice` = manual whole-order override (unchanged) **else** Σ non-informational, non-proposed line amounts (`LinesTotal`).
- New endpoints (`TransportOrdersController`):
  - `PUT /api/transport-orders/{id}/pricing/lines` — body `IReadOnlyList<SaveOrderPriceLineRequest>(LineKey?, Label, Quantity?, UnitPrice?, Amount, AdjustReason?, Remove?)`; permission `OrdersOverridePrice`; editing an Auto line → `AutoAdjusted` (originals preserved); new `LineKey=null` → `Manual`; `Remove` on Auto line → line kept with `Amount=0` + `Kind=AutoAdjusted` + reason (audit trail, spec §24 "remove an automatically proposed line" without destroying the original) — hard-removed only for Manual lines. Audit `"OrderPricing", orderId, "lines_adjusted"` with old/new line values.
  - `POST /api/transport-orders/{id}/pricing/recalculate` — explicit recalc (spec §26), permission `OrdersEdit`, refuses when Locked/Invoiced.
  - `POST /api/transport-orders/{id}/pricing/status` — body `(Status)`; transitions Draft↔Reviewed (OrdersEdit), →Locked / Locked→Reviewed (`orders.lock_price`), Invoiced only via invoicing. Audit `"OrderPricing" status_changed`.
  - `POST /api/transport-orders/{id}/pricing/lines/{lineId}/confirm` — Proposed → Auto (charge becomes billable), permission OrdersEdit.
- New permission `PermissionCodes.OrdersLockPrice = "orders.lock_price"`; catalog seeder + `DefaultRoleUpgrades` **v14**: planner/management/boekhouding.
- FE order Prijs section: lines table becomes editable (AUTO/AANGEPAST/MANUEEL/VOORSTEL badges, spec §41), + Regel toevoegen (vrije lijn), per-line edit with reason, original values shown struck-through ("8 × €1,25 → 6 × €1,25"), status chip + Review/Lock buttons, "Herberekenen volgens huidig contract" button, "Bekijk berekeningsdetails" modal rendering the snapshot Explanation + diagnostics (spec §34).
- Invoicing: when generating the invoice, set snapshot `Status=Invoiced` (in `InvoiceService` where `AgreedPrice` is consumed).

### B8. Combined-unit degression (Phase 8)

New file `Modules/Tarification/Entities/CombinedUnitDiscount.cs`:

```csharp
public enum DegressionScope { Order = 0, DeliveryAddress = 1, Stop = 2 }   // string-stored

public class CombinedUnitDiscount : AuditableTenantEntity
{
    public Guid? CustomerId { get; set; }      // null = company-wide
    public Guid? AgreementId { get; set; }     // optional: only when this table is engaged
    public string Name { get; set; } = string.Empty;
    public DegressionScope Scope { get; set; } = DegressionScope.DeliveryAddress;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CombinedUnitDiscountUnit> Units { get; set; } = new();
    public List<CombinedUnitDiscountTier> Tiers { get; set; } = new();
}

public class CombinedUnitDiscountUnit : AuditableTenantEntity
{
    public Guid DiscountId { get; set; }
    public Guid UnitTypeId { get; set; }
    public decimal EquivalentFactor { get; set; } = 1m;   // Halve pallet = 0.5, Colli = 0.25
}

public class CombinedUnitDiscountTier : AuditableTenantEntity
{
    public Guid DiscountId { get; set; }
    public decimal FromCount { get; set; }
    public decimal? ToCount { get; set; }
    public decimal Percent { get; set; }       // -8 = 8% korting (negative) — store positive, apply as discount
}
```

Request addition: `IReadOnlyList<PriceCalculationGroup>? Groups` — `record PriceCalculationGroup(string GroupKey, string GroupLabel, IReadOnlyList<PriceCalculationGroupUnit> Units)`, `record PriceCalculationGroupUnit(Guid UnitTypeId, decimal Quantity)`. `ApplyPricingAsync` builds groups from non-deleted `CargoItem`s by `UnloadingStopId` (label = stop city/address; scope Stop/DeliveryAddress identical grouping key = unloading stop; DeliveryAddress groups stops with identical address string); fallback: single group from the order-level unit line. Engine, after base lines + before assignment adjustments: select the most specific applicable discount (customer > company; agreement-linked requires that agreement engaged; equal tie → ConfigurationError); per group: `equivalent = Σ qty × factor` over configured units; find tier (`FromCount ≤ equivalent ≤ ToCount`); discount line per group: `"{Name} {GroupLabel}: {equivalent:0.##} eenheden → -{Percent:0.##}%"`, amount = `-Round(eligibleBase * Percent/100, 2)` where eligibleBase = Σ base-line amounts of the group's eligible unit lines (proportional attribution: group's share = group eligible quantity / total eligible quantity per unit). Admin CRUD: `GET/POST/PUT/DELETE /api/pricing/combined-discounts` (TariffsManage; audit `"CombinedUnitDiscount"`). FE: panel on PricingTableDetailPage (agreement-linked) + customer tab section "Combinatiekortingen" (customer-scoped): eligible units + factors, tiers, scope select ("Hele order / Per leveradres / Per stop").

### B9. Excel export/import (Phase 9)

New `Modules/Tarification/Services/PricingExcelService.cs` (ClosedXML, follows `CustomerImportService` shape) + `PricingImportController.cs`:
- `GET /api/pricing/agreements/{id}/export` → XLSX: sheet "Tarieven": columns `RegelId | Naam | Basis | Eenheid | Zone | Prioriteit | Van | Tot | Gewicht tot (kg) | Volume tot (m³) | Ldm tot | Prijs | Prijs per extra | Eenheidsprijs | Basisbedrag | Minimum | Maximum | Geldig van | Geldig tot` (one row per bracket, rule fields repeated; bracket-less rules = one row). `RegelId` = rule Guid (stable round-trip key, spec §15 — never row position).
- `POST /api/pricing/agreements/{id}/import/preview` (multipart) → `PricingImportPreviewDto`: `RowsFound, RowsValid, Warnings[], Errors[] (row + message: onbekende eenheid/zone/basis, ontbrekende prijs, Van > Tot, ongeldig percentage/datum, dubbele identieke regel), Added[], Updated[] (old→new per field), Removed[]` (rules whose RegelId missing from file — only listed, removal requires checkbox). Nothing is written on preview.
- `POST /api/pricing/agreements/{id}/import/commit` body `(FileToken or re-upload, Mode: UpdateAgreement | DuplicateAsNewVersion(Name, EffectiveFrom), ApplyRemovals: bool)` — transactional; audit `"PricingAgreement" imported` with counts.
- Permission `PermissionCodes.TariffsImport = "tariffs.import"` (export needs TariffsView; preview/commit TariffsImport) + `DefaultRoleUpgrades` **v15**: management/boekhouding.
- FE `PricingImportDialog.tsx` modeled on `CustomerImportDialog.tsx` (template = export of current table; preview table with Toevoegen/Wijzigen/Verwijderen/Fout badges; commit mode radios) wired into PricingTableDetailPage Import/Export panel.

### B10. Hardening, validation sweep, docs (Phase 10)

- `GET /api/pricing/agreements/{id}/validate` → list of warnings/errors: overlapping brackets, overlapping rule windows with equal specificity+priority (future ambiguity), gap warnings in brackets, derived-chain issues, assignments outside agreement validity. Surfaced in PricingTableDetailPage as a "Controle" banner.
- `docs/pricing.md` (Dutch-adjacent style like `operations-architecture.md`, English fine per existing docs): architecture, ERD-style entity list, calculation execution order (the canonical §33 list from Global Constraints), precedence tiers + scoring, versioning/effective dates, assignments/overrides, derivation, one-off pricing, warehouse services, degression, Excel round-trip, manual adjustments + audit, snapshot/locking lifecycle, worked examples incl. scenario 21 (€100 → NL +30% → klant -5% = €123.50) and the pallet/NL example of spec §23.
- Update `docs/permission-matrix-operations.md` if it lists order/tariff permissions; memory file update; final verification + report.

---

## Part C — Phased tasks

Work on branch `nav-redesign` (house pattern: waves commit directly here; worktree isolation not needed — clean tree). Each phase: failing tests first where practical, migration inspected (additive only), `dotnet build` + `dotnet test` green, FE `tsc`/`eslint`/`vitest`/`build` green when FE touched, one commit per phase (conventional message listed).

### Phase 1 — Shared tables & customer assignments
**Files:** modify `Modules/Tarification/Entities/PricingAgreement.cs` (+IsShared, +MaximumAmount, +Assignments; B1), `Configurations/PricingConfigurations.cs` (table `pricing_agreement_assignments`, decimal (7,3)/(12,2), index (AgreementId, CustomerId)), `Data/TransportationDbContext.cs` (DbSet), `Dtos/PricingDtos.cs` (agreement DTO + assignment DTOs + CustomerCount), `Services/PricingAdminService.cs` (validation + assignment CRUD + audit), `Services/PricingEngine.cs` (candidate loading incl. assignments, tier scoring, assignment adjustment lines), `Controllers/PricingController.cs` (assignment endpoints); migration `PricingAgreementAssignments`; tests `Tests/Tarification/PricingAssignmentTests.cs` (new).
- [ ] Entities + configuration + migration (inspect SQL, additive)
- [ ] Admin service: IsShared validation, assignment CRUD (overlap per customer = validation error), agreement list DTO with CustomerCount/CustomerNames, audit records
- [ ] Engine: tier-based scoring (private 8/10 > assigned 4/6 > default 0/2 with zone +2), shared-agreement candidate filter via active assignment on tariff date, assignment ±%/fixed lines before minimum, maximum cap after minimum
- [ ] Tests: **S3** two customers share one table (no row duplication — assert single agreement id in both breakdowns); assignment window respected by tariff date; -5% assignment produces exact line (€105 → -€5.25); fixed adjustment; **S6** customer-scoped rule (3 pallets €99) beats shared-table row (€105) for that customer only; shared agreement without assignment does not apply; private still beats assigned, assigned beats company default (incl. zone crossings); exact tie still blocks; tenant isolation
- [ ] FE (minimal, full UI in Phase 4): `pricingApi.ts` types + assignment calls; `CustomerUnitPricingPanel` "Prijsafspraken" table shows shared tables assigned to the customer with adjustment and "Gedeelde tabel" badge
- [ ] Build + tests + FE checks green → commit `feat(pricing): reusable shared rate tables with per-customer assignments and adjustments`

### Phase 2 — Derived tables (NL = BE +30%)
**Files:** modify `PricingAgreement.cs` (+BaseAgreementId, +Modifiers per B2), `PricingConfigurations.cs` (`pricing_agreement_modifiers`), DbSet, `PricingDtos.cs`, `PricingAdminService.cs` (modifier CRUD in agreement save, cycle/depth validation, no-own-rules validation), `PricingEngine.cs` (base-chain resolution + modifier lines + runtime cycle guard), `PricingController.cs`; migration `DerivedPricingAgreements`; tests `Tests/Tarification/DerivedAgreementTests.cs` (new).
- [ ] Entities + migration + admin validation (cycle → "Circulaire verwijzing tussen tarieventabellen"; derived agreement with own rules rejected; depth > 3 rejected)
- [ ] Engine per B2 (modifier lines ascending Sequence, condition on delivery country / resolved zone, running-subtotal percent base)
- [ ] Tests: **S4** BE 1 pallet €50 + NL agreement (+30%, CountryCode NL) → €65 total with two explainable lines; **S5** new BE version €55 → NL €71.50 without touching NL config; stacking Waddeneilanden zone +€75 after +30%; modifier condition not matching → no line; cycle A→B→A rejected on save; runtime guard tolerates pre-existing bad data; derived + assignment -5% ordering (base → modifiers → assignment): 100 → 130 → -6.50 = **123.50** (**S21** documented order); tenant isolation
- [ ] Commit `feat(pricing): derived rate tables with stacked country/zone modifiers and cycle detection`

### Phase 3 — Multidimensional & per-next-unit brackets, maximum charge
**Files:** modify `PriceRule.cs` (+MaximumAmount, +BracketMode; bracket +WeightToKg/VolumeToM3/LoadingMetersTo), `PricingConfigurations.cs`, `PricingDtos.cs`, `PricingAdminService.cs` (validation per B3), `PricingEngine.cs` (`BracketAmount` rework + max cap); migration `BracketDimensions`; tests extend `Tests/Tarification/PricingEngineV2Tests.cs`.
- [ ] Entity/config/migration + admin validation (min > max rejected; PerNextUnit gapless-from-1; overlap detection includes dimension caps)
- [ ] Engine: multi-dim match (tightest caps win), PerNextUnit summation with fractional remainder, MaximumAmount cap after minimum
- [ ] Tests: carrier table (kg tot 100/cbm tot 0.5/ldm tot 0.2 rows) picks tightest matching row; quantity+weight combined bracket; **28.3** 1st €60/2nd €55/3rd €50/4th+ €45 → 4 pallets = €210; **S20** €1.50/km × 50 = €75 → minimum €150 (existing) + max cap test (calculated 900, max 500 → 500); agreement MaximumAmount caps subtotal
- [ ] FE: rule editor (CustomerUnitPricingPanel modal) gains max amount, bracket mode toggle ("Absoluut / Per volgende eenheid"), bracket dimension columns when filled
- [ ] Commit `feat(pricing): multidimensional brackets, per-next-unit pricing and maximum charges`

### Phase 4 — Pricing management UI + bulk adjustment v2
**Files:** BE: modify `ScheduledPriceAdjustment.cs` (+AgreementId, +AmountDelta, +RoundingStep, +BasisFilter, +UnitTypeIdFilter), `PriceAdjustmentService.cs` (agreement scope + fixed delta + rounding + filters; validation exactly-one-scope, XOR percent/delta), `PricingController.cs` (agreement adjustment endpoints + `POST /api/pricing/agreements/{id}/duplicate` per B4), migration `AdjustmentScopeV2`; tests extend `Tests/Tarification/PriceAdjustmentTests.cs` + new `AgreementDuplicationTests.cs`. FE: new `features/tarification/pages/PricingTablesPage.tsx`, `PricingTableDetailPage.tsx`, `components/PricingTableWizard.tsx`, `components/RuleGridEditor.tsx` (inline spreadsheet-style rules/brackets grid), `components/AgreementAdjustmentPanel.tsx`; `navConfig.ts` module «Prijzen»; routes in `AppRoutes.tsx`; `pricingApi.ts` extensions; tests `__tests__/pricingTablesPage.test.tsx`, `__tests__/ruleGridEditor.test.tsx`.
- [ ] BE adjustment v2 + duplicate endpoint + migration + tests: +€5 on hourly rates only (BasisFilter=Hourly); -2% rounding to €0.05 (46.79 → 46.80); agreement-scope preview/confirm/cancel; duplicate-as-new-version copies everything, closes source, applies +3.5% (values asserted), audit rows
- [ ] Nav + list page (used-by count, validity status badges Actief/Toekomstig/Verlopen/Concept=IsActive false)
- [ ] Wizard with 9 template cards pre-creating agreement + skeleton rules per template (exact prefills: Uurtarief → 1 Hourly rule; Palletstaffel → QuantityBracket w/ 4 empty brackets; Gewichtsstaffel → WeightBracket; Laadmeter → bracket w/ LoadingMetersTo column visible; Zone-tabel → one QuantityBracket rule per active zone; Afstand → PerKm; Vaste prijs → Fixed; Gecombineerd → empty w/ column chooser; Leeg; Excel → opens Phase 9 dialog stub disabled until Phase 9)
- [ ] Table detail: RuleGridEditor (click-cell edit/onBlur save per house pattern, add/duplicate/delete row, sticky header, Tab moves cell focus), shared-usage warning banner, assignments panel (+ Klant koppelen modal with ±%/vast), modifiers panel, surcharges panel, adjustment panel (percent/fixed, rounding select, scope filters, preview Oud|Nieuw|Verschil, bevestigen), duplicate-version modal
- [ ] FE tests: grid edits save; wizard creates hourly table under a minute path (template → name → save); assignment link flow; adjustment preview renders old→new; permission gating (tariffs.view read-only)
- [ ] Commit `feat(pricing): pricing area with rate-table management UI, templates, grid editing and bulk adjustments v2`

### Phase 5 — Warehouse/logistics service charges
**Files:** BE: modify `Entities/ServiceOption.cs` (+UnitTypeId/AutoApply/OnlyForAdr; CustomerServiceOptionPrice +AutoApplyOverride), `RateCard.cs` (`SurchargeKind` new values), `PricingConfigurations.cs`, `PricingDtos.cs` (+AdrRequired, +CargoLineCount on request; service DTOs), `PricingEngine.cs` (auto-apply union + auto quantities per B5), `PricingAdminService.cs` (validation: PerUnit requires UnitTypeId; agreement surcharges still Percent|Fixed), `Orders/Services/TransportOrderService.cs` (pass AdrRequired + CargoLineCount); migration `ServiceCalculationBases`; tests `Tests/Tarification/WarehouseServiceTests.cs` (new). FE: `ServiceOptionsEditor.tsx` (+kind options/unit select/flags), customer override panel (+auto-apply override), `TransportOrderForm.tsx` services section (auto-applied read-only rows).
- [ ] Entities + migration + validation + DTO plumbing
- [ ] Engine auto-apply + per-kind quantity derivation + labels + entered-quantity precedence
- [ ] Tests: **S7** picking €1.25 PerUnit(Colli), order 3 colli auto-applied → €3.75; **S8** PAL UIT €4.50 PerUnit(Pallet), 5 pallets → €22.50; **S9** picking €1.50 PerOrderLine (3 lines) + PAL UIT €5 (4 pallets) + administratie €3 Fixed auto → €4.50+€20+€3; OnlyForAdr charged only when AdrRequired; customer AutoApplyOverride false suppresses; disabled-for-customer never auto-applies; entered quantity overrides derived (8→6 → €7.50 base for S16); PerKg/PerM3/PerLdm from order measures; PerDay needs entered quantity (informational otherwise); tenant isolation
- [ ] FE editor + order form + panel updates with tests
- [ ] Commit `feat(pricing): warehouse service charges — calculation bases, auto-apply contracts and ADR condition`

### Phase 6 — One-off order pricing + included time
**Files:** BE: modify `Orders/Entities/TransportOrder.cs` (B6 fields), `Orders/Configurations/*` (string enum + precisions), `Orders/Dtos/TransportOrderDtos.cs` (save/detail DTOs), `TransportOrderService.cs` (one-off validation: OneOff requires FixedAmount ≥ 0; actuals from StopExecution; pass OneOff + actual minutes), `Tarification/Dtos/PricingDtos.cs` (+OneOff, +actual minutes, +Proposed flag, +TotalWithProposed), `PricingEngine.cs` (one-off path + included-time helper per B6, also applied for engaged agreements with included-time fields), `PricingAgreement` DTO/admin (included-time + extra rate fields editable); migration `OneOffOrderPricing`; tests `Tests/Orders/OneOffPricingTests.cs` (new) + extend `Tests/Tarification/PricingEngineV2Tests.cs` (included time). FE: `TransportOrderForm.tsx` Prijs section (pricing-source radio, one-off fieldset, proposed badge), `PricingTableDetailPage`/agreement editor (included time fields), types.
- [ ] Entities + migration + DTOs + engine one-off path + included-time helper + Proposed lines + StopExecution actual collection
- [ ] Tests: **S10** one-off €850 no contract → €850, snapshot stored, `PricingSource=OneOff`; regular customer with contract + one-off order → contract untouched, order priced €850 (spec §22); **S11** €450 + 30/30 included, actual 60/30, €75/u → proposed extra €37.50, `Total` 450, `TotalWithProposed` 487.50; **S12** combined 60 min, actual 45+45 → extra 30 min €37.50; no actuals → no proposed lines; extra time without rate → informational; agreement-level included time on Fixed-basis contract order; snapshot keeps one-off after later tariff changes (**S18** variant)
- [ ] FE: source radio + fieldset + VOORSTEL badge + agreement fields, tests in `transportOrderSectionedForm.test.tsx`
- [ ] Commit `feat(orders): one-off order pricing with included loading/unloading time and proposed time charges`

### Phase 7 — Line-level manual pricing, status & locking
**Files:** BE: modify `Orders/Entities/TransportOrderPricing.cs` (line + snapshot fields per B7), `Orders/Configurations/OrderPricingConfigurations.cs`, `TransportOrderService.cs` (merge-on-recalc, status gate, LinesTotal→AgreedPrice), `TransportOrdersController.cs` (4 new endpoints per B7), `TransportOrderDtos.cs`, `Identity/PermissionCodes.cs` (+OrdersLockPrice), `Data/PermissionCatalogSeeder.cs`, `Data/DefaultRoleUpgrades.cs` (**v14**: planner/management/boekhouding + orders.lock_price), `Invoicing/Services/InvoiceService.cs` (set snapshot Invoiced); migration `OrderPricingLinesV2`; tests `Tests/Orders/OrderPricingLineTests.cs` (new) + extend `OrderPricingTests.cs` + `Identity/DefaultRoleSeederTests.cs` (v14). FE: `TransportOrderForm.tsx`/`TransportOrderDetailPage.tsx` pricing lines editor + status chip + lock/review/recalculate actions + calculation-details modal; `transportOrdersApi.ts`.
- [ ] Entities + migration + merge logic + endpoints + permission v14 + invoicing hook
- [ ] Tests: **S16** auto picking 8×€1.25 adjusted to 6 → €7.50, `Original*` = 8/€1.25/€10 preserved, audit row with old/new; **S17** free manual line €35 survives recalculation; auto line "removed" → Amount 0 AutoAdjusted with reason, original kept; recalc refreshes originals of adjusted lines; Locked blocks recalc + edits (ProblemDetails); Reviewed allows recalc; lock needs `orders.lock_price` (403 without); Invoiced set on invoice generation and blocks everything; AgreedPrice = lines total when no whole-order override; whole-order override still works (back-compat); **S18** invoiced order total unchanged after tariff change + recalc attempt; role v14 upgrade grants; tenant isolation
- [ ] FE: editable lines with kind badges, originals struck through, reason prompts, status flow with confirm dialogs, details modal; tests
- [ ] Commit `feat(orders): line-level price editing with preserved originals, pricing status and locking`

### Phase 8 — Combined-unit degression
**Files:** BE: new `Modules/Tarification/Entities/CombinedUnitDiscount.cs` (B8), config + DbSets, `PricingDtos.cs` (+Groups, discount DTOs), `PricingAdminService.cs` (CRUD + validation: ≥1 unit, tiers non-overlapping, factor > 0), `PricingController.cs` (`/api/pricing/combined-discounts`), `PricingEngine.cs` (group discount pass per B8), `TransportOrderService.cs` (build Groups from cargo per unloading stop); migration `CombinedUnitDiscounts`; tests `Tests/Tarification/CombinedUnitDiscountTests.cs` (new). FE: `components/CombinedDiscountsPanel.tsx` on table detail + customer tab; api + tests.
- [ ] Entities + migration + CRUD + engine grouping/discount lines + order group building
- [ ] Tests: **S13** eligible Euro/Blok/Colli, tiers 2-3→5%, 4-5→8%, 6+→10%; order 1 Euro+1 Blok+2 Colli one address → 4 units → -8% on eligible lines (exact amount asserted); **S14** factors Euro 1/Half 0.5/Colli 0.25 → 1+0.5+0.5 = 2.0 reaches 2-unit tier; **S15** Antwerpen 3 + Mechelen 2 with scope DeliveryAddress → separate tiers (not 5 combined); scope Order combines; customer-scoped beats company; agreement-linked only when engaged; equal-specificity tie blocks; tenant isolation
- [ ] FE panel (units + factors + tiers + scope) with tests
- [ ] Commit `feat(pricing): combined-unit degression with equivalent factors and delivery-address grouping`

### Phase 9 — Excel export & import
**Files:** BE: new `Modules/Tarification/Services/PricingExcelService.cs`, `Controllers/PricingImportController.cs`; `PermissionCodes.TariffsImport` + catalog + `DefaultRoleUpgrades` **v15** (management/boekhouding); tests `Tests/Tarification/PricingExcelTests.cs` (new; build workbook in-memory with ClosedXML, round-trip). FE: new `components/PricingImportDialog.tsx` (modeled on `CustomerImportDialog.tsx`), export button, wizard "Excel importeren" card wiring; api + tests.
- [ ] Export per B9 (RegelId column, per-basis columns, bracket rows)
- [ ] Import preview (counts, per-row errors: onbekende eenheid/zone/basis, ontbrekende prijs, Van > Tot, ongeldige datum/percentage, dubbele regel; Added/Updated old→new/Removed) — no writes on preview
- [ ] Import commit: UpdateAgreement + DuplicateAsNewVersion modes, ApplyRemovals opt-in, transactional, audited
- [ ] Tests: export→reimport unchanged = 0 changes; modified price → Updated with old→new; new row without RegelId → Added; missing row → Removed only with ApplyRemovals; each validation error case; commit into new version leaves source untouched; permission (403 without tariffs.import); role v15; tenant isolation
- [ ] FE dialog + flows + tests
- [ ] Commit `feat(pricing): rate-table Excel export and validated round-trip import`

### Phase 10 — Hardening, docs & final verification
**Files:** BE: `PricingAdminService.ValidateAgreementAsync` + `GET /api/pricing/agreements/{id}/validate`; tests extend. Docs: new `docs/pricing.md` (B10 contents), update memory. FE: Controle banner on table detail.
- [ ] Validation endpoint + banner + tests (overlap/ambiguity/gap/derivation warnings)
- [ ] `docs/pricing.md` per B10 incl. worked examples and the canonical calculation order
- [ ] Full verification: `dotnet build` (no new warnings), `dotnet test` (all), `npx tsc -b --noEmit`-equivalent via `npm run build`, `npx eslint .`, `npx vitest run`
- [ ] Commit `feat(pricing): configuration validation, pricing documentation and hardening`
- [ ] Final implementation report (spec §64): architecture, entities, migrations, endpoints, FE pages, precedence, tests, results, commits, decisions

---

## Part D — Spec coverage self-check

Spec §5/7/16/35 → P1+P4; §6/14 → existing windows + P4 (duplicate-as-version, rounding, scopes); §8 → P1 (customer rule override precedence, S6); §9 → P2; §10.1-10.3/10.5-10.9/10.11 → existing (S1/S2/S19/S20 already covered by existing tests; S20-max in P3); §10.4/10.12/§13 → P3+P4 (grid columns); §11/12/40 → P4; §15 → P9; §17-19/51 → P5; §20 → existing; §21 → P6; §22 → P6 (S10); §23/34 → existing breakdown + P7 details modal; §24/25 → P7 (S16/S17); §26 → P7; §27 → existing + P6/P7 (S18); §28.1 → existing brackets; §28.2 → P8 single-unit tiers; §28.3 → P3; §29/30/31 → P8 (S13/S14/S15); §32 → existing + P5; §33 → Global Constraints canonical order + P2 test (S21 = €123.50); §36 → P7 (v14) + P9 (v15); §37 → every phase audits; §38 → per-phase validation + P10 sweep; §39 → P1/P8 customer tab; §41 → P6/P7; §42/43/44 → P6 (StopExecution actuals, actual-vs-billable labels, Proposed lines); §45 → existing invoice-from-snapshot + P7 Invoiced status; §46 → Global Constraints; §50 → OrderDate documented; §52 scenarios S1-S21 all mapped above; §57 tests per phase; §58 → P10.
