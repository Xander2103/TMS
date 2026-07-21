# Business Feedback Wave (2026-07-21)

Reference documentation for the 20-task business-feedback wave. The implementation plan is
`docs/superpowers/plans/2026-07-21-business-feedback-wave.md`; this file records the delivered
capabilities, the seams they expose, the permission model, the migrations, the deliberate
limitations, and a manual smoke flow. Architecture is unchanged: modular monolith
(controller → hand-rolled service → `TransportationDbContext`), `[RequirePermission]`, explicit
`TenantId` predicates, additive EF migrations, Dutch UI, React 19 + react-router 7 (route-based
lazy loading). Backend stays the source of truth; every mutation re-validates server-side.

## Legal entities & invoice numbering

- **`LegalEntity`** (`Modules/Organization`) models each own billing company: legal/trading name,
  company + VAT + Peppol identifiers, address block, bank details, invoice format tokens
  (`{YYYY} {YY} {MM} {SEQ} {PREFIX}`), padding, footer, logo (real upload via `IFileStorageService`,
  category `legal-entities`), `IsActive`, exactly one `IsDefault` per tenant. A startup
  `LegalEntitySeeder` bootstraps one default entity per tenant from `TenantSettings`.
- **Active entity** is a per-user cosmetic selection (`UserLegalEntitySelection`,
  `GET/PUT api/me/active-legal-entity`) — it only pre-fills pickers; it never widens tenant scope.
- **Numbering** is per-entity, per-month: `InvoiceSequence(LegalEntityId, Year, Month, NextValue)`
  with `NextValue` as a concurrency token and a claim loop mirroring `TenantNumbering`. Preview
  via `GET api/invoices/next-number` does not claim. Cancelled/deleted invoices never release a
  number. Draft period changes re-issue and audit; manual override needs `invoices.override_number`
  + reason. Invoices freeze a seller snapshot (name/VAT/IBAN/address) and a customer fiscal
  snapshot at creation; later master-data edits never mutate Sent/Paid invoices.
- **Frontend**: Settings → "Eigen bedrijven" (list/create/edit/deactivate + logo), top-bar entity
  switcher, new-invoice entity + period picker with live number preview, override dialog.

## Customer master data

- **Numbering & import**: explicit `CustomerNumber` on create/edit (duplicate-blocked; changing an
  existing one needs `customers.override_number` + reason). `CustomerImportService` clones the
  package-import pattern (template/preview/commit, all-or-nothing, error workbook), permission
  `customers.import`. FE: import dialog + number field on the customer form.
- **Contacts & addresses**: `CustomerContact` gains display/nickname/mobile/department (new
  `ContactDepartment` lookup) / language / active flag. Addresses stay `Location`-based with new
  `LocationType` values (RegisteredOffice, AdministrativeAddress, BillingAddress, ReturnsAddress)
  and a filtered-unique `IsDefaultBillingLocation`.
- **Fiscal / bank / registry**: customer gains nickname, currency, IBAN/BIC/bank-name/account.
  IBAN validation is the shared `BankingValidators` (moved to `Common/Validation`, reused by the
  employee module — moved, not duplicated). `ICompanyRegistryProvider` is a seam; the default
  `NullCompanyRegistryProvider` returns `{ configured: false }`, and the FE "Opzoeken" button never
  overwrites fields without confirmation. `VatTreatmentCatalog` drives save-time validation
  (VAT-number-required per treatment, allowed rates, legal text). Permission `customers.manage_fiscal`.
- **Communication rules**: `CustomerCommunicationRule` + FK join to `CustomerContact` (no free-text
  recipients) + `CustomerCommunicationResolver` (the future-sender seam). Permission
  `customers.manage_communication`, FE tab "Communicatie".
- **Diesel surcharge & PO policy**: `CustomerDieselSurcharge` (percent, basis, presentation,
  rounding) + order-level override (reason mandatory, audited) + pure `DieselSurchargeCalculator`
  that appends per-order or aggregated invoice lines without inflating the base lines.
  `Customer.PurchaseOrderPolicy` (None/Optional/Required, backfilled from the old bool which stays
  in sync) + `CustomerPurchaseOrderNumber` effective-dated history. `Draft→Sent` is blocked when a
  required PO is missing. Permissions `customers.manage_surcharge` / `customers.manage_po`.

## Invoicing entity validation & attachments

- Customer `DefaultLegalEntityId` → order `LegalEntityId` → invoice requirement; sending
  hard-validates an active same-tenant entity (Dutch: "Deze factuur heeft geen geldige
  facturerende entiteit en kan niet worden verzonden.").
- `InvoiceAttachment` upload/download/delete (pdf/xml/xlsx/xls/csv/jpg/jpeg/png, ≤10 MB) with an
  `IncludeWhenSending` flag (internal by default). Permissions `invoice_attachments.view/manage`.
  FE: "Bijlagen" panel on the invoice detail (pre-Sent).

## HR expansion

- **Employee**: DIMONA number, identity-card number (confidential — redacted like NRN/IBAN), civil
  status, dependent children; multiple `EmployeeEmergencyContact` rows (legacy single pair
  backfilled to priority 1 and kept in sync); employment end date surfaced.
- **Driver categories**: `DriverDriverCategory` join (multi), primary mirrored on
  `Driver.DriverCategoryId`. Licence categories remain qualifications; the eligibility engine is
  unchanged.
- **Employee documents**: `EmployeeDocument` entity (category, expiry, notes, archive) with
  upload/download/replace/archive. Sensitive categories (ID front/back, medical, contract) require
  `employee_documents.view_sensitive`. FE tab "Documenten" (with camera-capture attribute).
- **Reminders & expiry policies**: `HrReminderSettings` (birthday/seniority/employment-end) +
  `ReminderDispatchLog` (authoritative dedupe) + `HrReminderProducer` on the existing 6 h expiry
  sweep. Shared `ExpiryReminderPolicy` (per qualification / fleet-document / employee-document /
  tachograph target) with seeded defaults drives recipient routing and repeat windows. Permission
  `hr_settings.manage`.
- **Issued items ("Bedrijfsmiddelen")**: `IssuedItemTemplate` (Settings-managed, seeded) +
  `EmployeeIssuedItem` (status NotIssued/Issued/Returned/Missing/Damaged, snapshots frozen so
  template edits never rewrite history). Server-rendered NL/FR acknowledgement PDF at
  `GET api/employees/{id}/issued-items/document`. Permissions `issued_items.view/manage/manage_templates`.
  FE: employee "Bedrijfsmiddelen" checklist + Settings → "Bedrijfsmiddelen (sjablonen)".

## Driver profile merge

Driver profiles now live inside the personnel dossier — there is no standalone driver screen. The
driver profile (readiness, inline edit, block/unblock, assignment slots, fixed trailer,
qualifications, delete) is a reusable `DriverProfilePanel` rendered in the employee
"Chauffeursprofiel" tab. `/drivers` redirects to the personnel "Chauffeurs" view (`EmployeeSearch`
gained a `hasDriverProfile` filter plus blocked/availability columns); `/drivers/:id` resolves the
driver and redirects to its tab. `/drivers/new?employeeId=` is kept, reached from the employee
detail. Permissions are unchanged (`drivers.*` still gate the panel actions).

## Fleet compliance

- **Maut**: vehicles and trailers gain axle count (0–12) and loading metres; `EmissionClass` gains
  the full Euro 0–7 range. Effective toll axle count = vehicle + coupled trailer (documented, not
  calculated — no Maut cost engine).
- **Vehicle required licence**: `Vehicle.RequiredLicenceCode` (B/C1/C/CE, null = no check) feeds the
  planning eligibility rules committed with the driver-licence checks.
- **Document uploads**: `FleetDocumentsController` gained
  `POST/GET/DELETE api/fleet-documents/{id}/document` (pdf/jpg/png, ≤10 MB); documents gained an
  issuing-authority field; new `FleetDocumentType` values `LeasingContract`, `TachographCalibration`.
- **Tachograph**: `TachographCalibration` per vehicle (technical fields, status
  Valid/ExpiringSoon/Overdue, certificate attachment); overdue surfaces in the planning conflict
  engine. Permissions `tachograph.view/manage`.
- **Leasing**: `LeasingContract` on a vehicle or trailer (exactly-one owner). Financial fields
  (monthly amount, km allowance, end-of-contract mileage) are redacted without `fleet_finance.view`;
  all mutations require `fleet_finance.manage`.
- **KPI pages**: `FleetKpiService` returns quality-flagged metrics
  (`GET api/vehicles/{id}/kpi`, `api/trailers/{id}/kpi`). No fabricated numbers — unavailable
  sources render "—" with an explanation; trailers exclude fuel KPIs.
- **Frontend**: Maut + required-licence fields on the vehicle/trailer forms, fleet-document upload
  UI, "Tachograaf"/"Leasing"/"KPI" tabs on the detail pages.

## Order units

`UnitType` is a managed tenant lookup (seeded with stable codes matching the package enum). Orders
and cargo lines gained `QuantityUnitCode`; the migration maps distinct legacy free-text values onto
codes and leaves unmapped values in `QuantityUnit` as a fallback. FE: the free-text unit input is
replaced by a code-valued `LookupSelect` (permission-gated inline create, `unit_types.manage`); the
order detail resolves the code to its managed name and falls back to legacy text.

## Package label redesign

Server-rendered only (no FE). `LabelSnapshot` was extended additively (sender/recipient blocks,
dates/times, sequence ints, PO, volume, COD, logo) so old snapshots re-render, and
`LabelRenderService` gained a horizontal Thermal 100×150 (landscape) layout with a full-width
Code128, plus the A4 8-up grid using the same block renderer.

## Permissions (role templates v9)

New codes: `legal_entities.view/manage`, `invoices.override_number`,
`invoice_attachments.view/manage`, `customers.import/override_number/manage_fiscal/
manage_communication/manage_surcharge/manage_po`, `contact_departments.view/manage`,
`employee_documents.view_sensitive`, `issued_items.view/manage/manage_templates`,
`hr_settings.manage`, `fleet_finance.view/manage`, `tachograph.view/manage`,
`unit_types.view/manage`. Grants are applied both to `DefaultRoleDefinitions` (new tenants) and via
the add-only `DefaultRoleUpgrades` v9 step (existing tenants): boekhouding gets the invoicing/
customer-finance codes; management gets fleet-finance + tachograph + communication; HR gets
sensitive documents + issued items + reminder settings; planner/dispatcher and magazijn get the
relevant lookups.

## Migrations (all additive)

`LegalEntities`, `InvoiceNumbering`, `CustomerContactsAndAddressRoles`, `CustomerFiscalAndBankData`,
`CustomerCommunicationRules`, `DieselSurchargeAndPoPolicy`, `InvoiceEntityValidationAndAttachments`,
`EmployeeHrAndDriverCategories`, `EmployeeDocuments`, `HrRemindersAndExpiryPolicies`, `IssuedItems`,
`FleetMautAndDocumentUploads`, `TachographAndLeasing`, `UnitTypes`. Data backfills (emergency
contacts, driver categories, PO policy, unit codes, invoice period) run inside their migrations with
guarded `INSERT ... SELECT` / `UPDATE`. No historical migration was edited.

## Deliberate limitations

- No real email/Peppol transmission — the outbox is a dev sink; communication rules are a resolver
  seam only.
- Diesel surcharge is a percentage strategy with an explanatory `FormulaDescription`; a customer's
  Excel formula maps later onto the `IDieselSurchargeCalculator` seam. Dossier-level surcharge/PO
  ride order aggregation.
- No Maut cost calculation — only the input data (axles, loading metres, emission class).
- Company-registry lookup returns "not configured" until a provider is wired.
- Camera capture is the HTML `capture` attribute, not a bespoke API.

## Manual smoke flow

1. Log in as `admin@dev.local` / `Admin123!`.
2. Settings → Eigen bedrijven: confirm the seeded default entity; create a second entity and switch
   via the top-bar selector.
3. New invoice: pick the entity + an earlier period, confirm the previewed number, send, and confirm
   the seller snapshot is frozen.
4. Customer: import a klanten workbook, set a required PO policy, add a diesel surcharge, and confirm
   `Draft→Sent` is blocked without a PO.
5. Personnel: open an employee, use the Documenten / Bedrijfsmiddelen tabs, download the
   acknowledgement PDF; open a driver via the "Chauffeurs" view and confirm `/drivers/:id` redirects
   into the dossier.
6. Vehicle: fill Maut fields + required licence; add a tachograph calibration and a leasing contract
   (amounts hidden without `fleet_finance.view`); open the KPI tab and confirm unavailable metrics
   render "—".
7. Order: pick a managed unit type; print a package label and confirm the horizontal layout.
