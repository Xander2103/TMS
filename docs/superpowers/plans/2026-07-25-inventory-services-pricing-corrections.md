# Inventory Units, Variants, Services & Pricing-Basis Corrections — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Managed stock-unit dropdown, direct variant editing with movement-audited stock, globally configured services/surcharges with customer overrides (explicit warning + source), one clear primary pricing basis in the rule editor and engine, effective services in transport orders — refactoring the existing subsystems, no parallel systems.

**Architecture:** Reuse `UnitType` as the single unit master (new `AllowForInventory` usage flag + `Inventory` category). Reuse the existing movement-ledger stock architecture (`StockMovement` + cached `CurrentStock` + `Version`) — only add label-only variants, per-variant thresholds and an inline editor. Extend `ServiceOption`/`CustomerServiceOptionPrice` into the global-default + customer-override model consumed by the engine and order UI. Constrain the engine so standalone rules never sum across bases (composites only via explicit agreements).

**Tech Stack:** unchanged (EF Core/Npgsql additive migrations, xUnit, React/Vitest).

## Global Constraints
- Additive migrations only; historical migrations untouched. Tenant isolation, permissions, audit (`IAuditService`) preserved. No new permission codes (reuse `unit_types.*`, `tariffs.*`, `issued_items.*`, `inventory.*`, `orders.*`) → roles stay v13.
- Warning copy (verbatim, spec §8): "Let op: wanneer u hier een waarde invult, wordt de algemene standaardregel voor deze klant overschreven." / disabled: "Let op: deze service is algemeen beschikbaar, maar wordt voor deze klant uitgeschakeld." Reset action: "Algemene waarde opnieuw gebruiken".
- Source labels (spec §19/20): "Klanttarief" / "Algemene standaard" (rename engine's "Klantprijs"/"Standaardtarief"; update tests).
- Explicit button text "+ Variant toevoegen", "+ Nieuwe eenheid toevoegen"/"Eenheden beheren".

## Phase A — Inventory units (managed dropdown)
- `UnitType.AllowForInventory` (bool, default false) + `UnitCategory.Inventory = 8`; migration `InventoryUnits` with one-time SQL backfill `AllowForInventory = true` for codes PIECE, BOX, KG (migration-time = truly once; user edits never re-overwritten).
- Seeder: add-if-missing stock units PAAR (paar), SET (set), ROL (rol), LITER (liter), METER (meter) with Category=Inventory, AllowForInventory=true, AllowForOrderEntry/Pricing=false.
- `GET /api/unit-types/inventory-options` (perms: IssuedItemsManageTemplates|InventoryView|InventoryManage|UnitTypesView|UnitTypesManage) → {id, code, name, symbol}.
- Master editor: AllowForInventory checkbox ("Bruikbaar als voorraadeenheid") + Inventory category label.
- `TemplateFormModal` Eenheid: dropdown from inventory-options (+ current free-text value as fallback option "Bestaande waarde: …"), plus permission-gated link "Eenheden beheren" → `/master-data/eenheden` (unit_types.manage|tariffs.manage). Template keeps storing the unit NAME string (snapshot; no FK migration needed).
- Tests: endpoint content+permissions, seeding/backfill idempotence, FE dropdown + persists + manage-action gating.

## Phase B — Variants: inline editor, label-only variants, per-variant threshold, auditable stock edits
- `SaveVariantRequest` + `Label` (string?), `LowStockThreshold` (int?); label-only variants allowed when template has no linked attributes (bypass ResolveVariantValues); label editable for valueless variants. `IssuedItemVariant.LowStockThreshold` (int?); notifier uses variant threshold ?? template threshold for variant movements. Migration `VariantThresholds`.
- FE `TemplateFormModal`: enabling "Varianten gebruiken" immediately shows the variant editor — rows (variantnaam, voorraad, drempel, actief) + "+ Variant toevoegen". Create mode: rows created after template POST (initial-stock movements). Edit mode: loaded from detail; stock change → `correctStock` (auditable Correction movement, reason field defaulted "Aanpassing via sjabloonformulier"); label edit (valueless variants), deactivate. Computed total read-only. Detail-page variant modal also supports label-only add when no attributes.
- Tests: backend label-only create/update/duplicate, threshold override notification, correction movement on stock edit; FE editor appears on enable, add Small 10/Medium 15, computed total, movements invoked.

## Phase C — Global services & surcharges config
- `ServiceOption` + `Description`, `InvoiceDescription`, `SelectableInOrders` (bool default true); `SurchargeKind` + `PerHour`, `PerStop` (agreement surcharges validated to Percent|Fixed only). Migration `ServiceOptionConfig`.
- Shared `ServiceOptionsEditor` (extracted from PricingSettingsPage tab, extended fields incl. pricing-method select "Vast bedrag / Percentage / Per uur / Per stop"), new page `/master-data/services` "Services & toeslagen" + nav entry (tariffs.view/manage); PricingSettingsPage tab reuses editor.
- Tests: CRUD incl. new fields/methods, inactive + not-selectable excluded from order-entry list, tenant isolation, permissions.

## Phase D — Customer service overrides
- `CustomerServiceOptionPrice`: `Value` → nullable, + `Disabled` (bool), `MinimumAmount` (decimal?), `InvoiceDescription` (string?), `EffectiveFrom/Until` (DateOnly?). Migration `CustomerServiceOverrides`.
- Engine/config resolution (date-aware vs tariff date): disabled → service unavailable/uncharged for that customer; effective value = override.Value ?? default; minimum applied; source = "Klanttarief" when an active override value exists else "Algemene standaard". `CustomerServiceOptionPriceDto` extended (kind, defaultValue, override fields, effectiveValue, source).
- Customer panel services table → per service: Algemene prijs / Klantoverride (input + uitschakelen toggle + datums) / Effectieve prijs / Bron + always-visible warning texts + "Algemene waarde opnieuw gebruiken".
- Tests: inherit, override, disable, dates respected, reset, warning text visible, engine uses effective value.

## Phase E — Order services UI + snapshot/invoice
- Order create/update: `Services: IReadOnlyList<OrderServiceInput>(ServiceOptionId, Quantity?)?` (supersedes ServiceOptionIds, which stays accepted); `TransportOrderServiceLine.Quantity` (decimal?); engine request `Services` w/ quantities; PerHour/PerStop amount = effectiveValue × quantity (missing quantity → informational "geef aantal op", no charge). Migration `OrderServiceQuantities`.
- Invoice line for a service uses override InvoiceDescription ?? option InvoiceDescription ?? name (frozen in NameSnapshot at save time — keep single snapshot field) and existing Amount.
- FE order form services: only active + selectable + not-disabled-for-customer; show effective price incl. "/uur", source ("Klanttarief"/"Algemene standaard"); quantity input for PerHour/PerStop (stops defaulted to unloading-stops − 1, editable).
- Tests: hourly service with quantity → snapshot + invoice line; customer-disabled hidden & uncharged; source shown; effective price loaded.

## Phase F — One primary pricing basis
- `PriceRuleBasis` + `PerLoadingMeter = 8`, `PerVolume = 9`, `PerStop = 10`; `PriceRule` + `MinimumQuantity` (decimal?), `QuantityRoundingStep` (decimal?). Request + `VolumeM3`, `LoadingMeters`, `StopCount`; ApplyPricingAsync passes order VolumeM3 + unloading-stop count. Migration `PricingBasisExtensions`.
- Engine: new order-measure bases (volume × rate, ldm × rate, stops via brackets-or-linear); Hourly (and PerUnit) quantity pipeline: roundUp to `QuantityRoundingStep`, then `max(qty, MinimumQuantity)`, then rate. **Standalone order-level rules: select ONE rule overall (max specificity → priority; exact tie → blocking configuration error) — never sum across bases.** Agreement components remain the only explicit composite.
- Rule editor: "Prijsbasis" select (Per eenheid / Volgens gewicht / Per uur / Per kilometer / Per laadmeter / Per volume / Per stop / Forfait) showing ONLY the relevant fields; Per eenheid gets a "Berekeningswijze" toggle (staffel ↔ per stuk); oversize block only for Per eenheid; hourly shows min-duur + afrondingsstap.
- Tests: each basis prices from its measure; two standalone rules with different bases → configuration error, not a sum; hourly 2u10 @ step 0.25 min 3 → 3 × rate; conditions (zone/oversize) still modify a single basis; FE: irrelevant fields hidden per basis.

## Phase G — Final verification
- Full backend build+tests, tsc, eslint, full vitest, production build, clean worktree, report.
