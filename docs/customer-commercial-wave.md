# Customer & Commercial Quality Wave — audit & completion (2026-08-28)

Scope: commits `14be379 … 51fe598` (i18n baseline, sidebar/klantdetail, centraal
adresbestand, contactgerichte meldingen, prijs-UX + Excel-import, verkoopcategorie
als artikelbestand, klant-/entiteitswijziging) plus the audit follow-up in the
working tree on top of `51fe598`.

## Business rules and where they are enforced

| Rule | Enforced in |
|---|---|
| A. One physical address, many customer links; stop snapshots immutable | `CustomerLocationLink`, `CustomerAddressService`, `LocationService` (list/portal/duplicate all read the links); stops keep their own snapshot columns |
| B. "Wie ontvangt wat?" on the contact | `CustomerContactSubscriptionService` → `CustomerCommunicationRule`; `NotificationEventCatalog` routes every customer-facing order event through the rule with the primary contact as fallback |
| C. Excel import feeds the pricing engine | `PricingExcelService` writes `PriceRule`/`PriceRuleBracket` in a `PricingAgreement`; no second evaluator |
| D. Fiscal hierarchy | `InvoiceLineFiscalResolver.Resolve` (line override → sales-code classification → customer → tenant); `Inspect` only warns |
| E. Invoice language | `Invoice.LanguageCode` frozen at creation; `DescriptionFor` uses stored translations; generated wording via `InvoicePdfStrings` |
| F. Diesel base | `InvoiceLineFiscalResolver.DieselBase` over the generated lines' sales codes; diesel role excluded structurally |
| G. Customer change | `OrderCustomerChangeService` / `DossierCustomerChangeService` |
| H. Legal entity change | `TransportOrderService.ChangeLegalEntity*`, `DossierService.ChangeLegalEntityAsync` (cascades to linked orders) |

## Defects found by the audit and fixed

* **Diesel base was never applied** — `DieselBase` existed but the surcharge ran over
  `AgreedPrice`. Now per generated line by sales code; Transport/Supplements backfilled
  to `IncludeInDieselBase = true` (preserves the previous effective base).
* **Legacy `VatCategoryOverride` vs `VatTreatmentOverride`** — a legacy exemption category
  ("AE"/"K"/"G"/"E") is now the statutory classification (treatment, 0 %, category and legal
  text agree); "S"/"Z" only refine a domestic treatment; contradictions are refused on save.
* **Dutch wording on non-Dutch invoices** for generated lines (`uur`/`stops`, diesel label).
* **Ledger mappings per entity could never be saved** (new rows tracked as *Modified* →
  "0 rows affected") and accepted foreign-tenant/dangling ids. Now validated (tenant,
  existence, active), diffed instead of replaced, FKs `Restrict`, `CostCentre` bounded to 40.
* **Customer change left the order `Invoiced`** after releasing its concept lines (never
  invoiceable again); pricing snapshot not stale → readiness could say "ready" on the old
  customer's figures. Adjusted lines now become unconfirmed *proposals* instead of valid
  manual lines (no silent carry-over of customer A's negotiated amount).
* **Entity change refused any `Invoiced` order**, making its draft-release path unreachable;
  dossier-level entity change did not cascade to its orders (now one transaction + impact).
* **Communication**: retick after untick crashed (soft-deleted link vs unfiltered unique
  index); checkboxes were inert for all order events; admin-deactivated advanced rules were
  re-activated; multi-rule types could not be unticked; FE swallowed 403/500.
* **Address master**: quick-create ended in a 409 toast; hard-delete of another customer's
  link; `DuplicateAsync` without keys/links; portal on legacy `CustomerId`; `1/1` ≡ `11`.
* **Import**: unmapped columns wiped fields; RegelId-less re-import duplicated rules;
  failed imports never recorded; header read ignored row/sheet; invariant-first parsing.

## Migrations (apply in this order, forward-only)

1. `20260827215031_CentralAddressMaster` — additive; startup backfill fills keys/links.
2. `20260827231302_PricingImportProfilesAndHistory` — additive.
3. `20260827235535_SalesCodeFiscalMaster` — additive; **Down drops frozen fiscal snapshots**.
4. `20260828174850_CommercialWaveAuditFixes` — filtered unique index on rule contacts,
   FKs/indexes on ledger mappings, bounded text columns (guard raises if existing data is
   longer), `pricing_import_runs.Status/Error`, `IncludeInDieselBase` backfill for
   Transport/Surcharge roles.

Cautions: take a backup first (3 and 4 are not safely reversible); run the permission/role
seeders as a release step (they only run automatically in Development); the address
backfill runs on every boot and now also recomputes stale normalisation keys once.
