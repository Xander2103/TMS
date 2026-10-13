# Wave 2 — Commercial Foundation Implementation Note

Scope (master spec Parts N/O/P/W/X/AD/BH; gap analysis §4 items 3-6, 11, 13): sales codes on
pricing objects with invoice snapshotting, allowed billing entities per customer with audited
override, customer language actually consumed, invoice grouping preferences, typed price
coverage + stale invalidation, and the invoice-readiness field the Wave-10 workspace will read.
Everything additive; no historical rewrite.

## 1. Sales codes / GL / VAT on pricing objects

- `SalesCategory` (Accounting) stays THE sales-code entity — no new parallel concept. Additive
  columns: `InvoiceDescriptionNl` (nullable; falls back to Name), `DefaultUnitCode` (nullable),
  `VatCategoryOverride` (nullable string, UNCL5305; null = customer treatment decides — the
  existing `VatTreatmentCatalog` chain stays authoritative).
- Additive `SalesCategoryId` (nullable FK) on `ServiceOption`, `PriceRule` and
  `PricingAgreement` (agreement value = default for its rules; rule wins; option wins for
  service lines). Admin UI: a SearchableSelect on the existing editors (ServiceOptionsEditor,
  RuleGridEditor, PricingTableWizard) — one field each, no redesign.
- Invoice-line creation resolves: line's explicit category → service option's → rule's →
  agreement's → the current 3-role fallback (Transport/Surcharge/Diesel). Resolution result is
  frozen exactly like today (`FreezeLedgerSnapshotsAsync` unchanged); masterdata edits never
  move history.
- `TransportOrderServiceLine` + `TransportOrderPricingLine` get nullable `SalesCategoryId`
  snapshot columns stamped at line creation so the dossier "Verkoop & prijs" section and later
  KPI waves can group without re-resolving.

## 2. Allowed legal entities + audited override

- New table `customer_allowed_legal_entities` (CustomerId, LegalEntityId, unique active pair).
  Empty set = all active entities allowed (backward compatible default — no tenant data
  migration needed).
- `CustomerService` validation: DefaultLegalEntityId must be in the allowed set when the set is
  non-empty. Customer form (Fiscaal & Peppol section): multi-select of active entities.
- New permission `dossiers.override_entity` (v27) required by PUT /api/dossiers/{id}/legal-entity
  and order `LegalEntityId` changes when the target differs from the customer default; reason
  field added to `ChangeDossierEntityRequest` (required on override; audited old→new+reason).
  `dossiers.manage` alone no longer suffices for cross-entity moves.
- Order/dossier create: inherited entity must be allowed; explicit request entity outside the
  allowed set → validation error naming the allowed entities.
- Invoice create (Wave 0 coherence check) additionally validates the invoice entity against the
  customer's allowed set.

## 3. Customer language consumed

- `InvoicePdfRenderer`: string catalog `InvoicePdfStrings` (nl/fr/en/de records — FACTUUR/
  FACTURE/INVOICE/RECHNUNG, all labels, date format per locale), selected by
  `Customer.InvoiceLanguageCode ?? Customer.DefaultLanguageCode ?? "nl"` snapshotted onto the
  invoice at creation (`Invoice.LanguageCode`, additive column, frozen like the seller snapshot).
- Invoice line descriptions: `SalesCategory.InvoiceDescriptionNl` Wave 2 ships NL-only content
  but the resolution goes through one `InvoiceTextResolver` so adding FR/DE columns later is a
  data change. Built-in message templates: add FR + EN rows to `BuiltInMessageTemplates` for the
  invoice/ETA/delivery kinds (the resolution chain already supports language — only content was
  missing); DE deferred until a German-speaking customer exists (chain falls back to nl).
- UBL: `Invoice.LanguageCode` does not affect UBL (IDs are language-neutral) — no change.

## 4. Invoice grouping preferences

- `Customer.InvoiceGrouping` (additive enum string: `PerDossier | Weekly | Monthly | ByReference
  | Manual`, default Manual = today's behavior). Editable in the customer Facturatie section.
- Wave 2 only stores + exposes it (NewInvoicePage shows a hint "Deze klant verwacht één factuur
  per dossier/week/maand"); the proposal engine that acts on it is Wave 10.

## 5. Typed coverage + stale invalidation

- `TransportOrderPricingSnapshot.CoverageStatus` (additive enum string `Full | Partial | None |
  NotApplicable`) computed wherever `CoverageJson` is written (worst entry wins; no entries +
  no expectation = NotApplicable). Backfill migration: one-time SQL derive from existing JSON
  (Npgsql `jsonb`-free: parse in a startup seeder like the dossier backfill, idempotent via a
  null-check — rows with json but null status).
- `TransportOrderPricingSnapshot.IsStale` (bool, default false): set true (never silently
  recalculated) when `PricingInputsChangedAsync` detects a change while status is
  Draft/Reviewed; cleared on explicit recalculation/save. Locked/Invoiced keep refusing edits
  (unchanged). UI: "Prijs verouderd — herbereken" chip on order + dossier price summary.
- `DossierReadinessService.pricing.*` switches from JSON parsing to the typed column; the
  attention-count query gains the pricing dimension (gap noted in Wave 1 closes).

## 6. Invoice readiness field

- Computed, persisted projection on TransportOrder: `InvoiceReadiness` (additive enum string
  `NotReady | ReadyForInvoice | ReviewRequired`), maintained by a new
  `InvoiceReadinessEvaluator` invoked on the existing transition points (order → Completed,
  pricing snapshot changes, POD creation) — deterministic + idempotent: Completed && coverage
  Full && !IsStale && (POD present for unloading stops when trip-executed) → ReadyForInvoice;
  Completed && anything missing → ReviewRequired with reason list persisted as
  `InvoiceReadinessReasons` (string, semicolon codes). Wave 10 builds the workspace on it;
  Wave 2 already shows the chip on the dossier price summary and filters
  `ListUninvoicedOrdersAsync` output with it (non-breaking: extra DTO field, no behavior gate).

## Order of implementation (phases, each gated + committed)

1. Schema migration (all additive columns/tables above) + coverage backfill seeder + tests.
2. Sales-code resolution chain + snapshots + admin UI fields + tests (golden: existing invoice
   tests byte-stable when no categories are configured on pricing objects).
3. Allowed entities + override permission v27 + validations + customer UI + tests (scenario 17).
4. Invoice language (PDF strings + Invoice.LanguageCode + template content) + tests (scenario 18
   subset: French invoice PDF labels; message template fr resolution).
5. Grouping preference storage + UI hint + tests.
6. Stale invalidation + typed coverage consumers + invoice readiness evaluator + tests
   (scenario 13: waiting time without price → ReviewRequired; scenario 14: clean dossier →
   ReadyForInvoice automatically).

Risks: touching `InvoiceService.CreateAsync` again (Wave 0 tests protect it); readiness
evaluator must not fire notifications (Wave 10 decides); backfill seeder ordering after
DossierBackfillSeeder in Program.cs.
