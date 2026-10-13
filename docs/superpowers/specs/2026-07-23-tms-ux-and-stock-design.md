# TMS UX & Stock/Inventory — Design

Date: 2026-07-23
Status: Approved for planning

## Goal

Two cohesive improvements to the existing TransportationService application, delivered as two
sub-projects so the frontend-light UX work ships and can be reviewed before the larger stock build:

1. **Sub-project 1 (UX):** section-based navigation for the employee and customer create/edit forms,
   a grouped Peppol control, and a repositioned personnel-planning legend.
2. **Sub-project 2 (Stock):** a greenfield asset/inventory subsystem — generic per-template attributes,
   variants, stock movements, stock-aware issuance, UI, permissions, migrations, and tests.

### Hard constraints (from the work order)

- Do **not** redesign unrelated modules. Reuse existing models, permissions, validation, audit logging,
  file storage and design-system components. Do **not** duplicate the create/edit forms or business logic.
- Additive migrations only; never edit historical migrations. Existing templates, issued items, Peppol
  fields, form values and planning events must be preserved. New template stock features default to
  disabled so existing rows are unaffected.
- Backend permission enforcement is mandatory. Audit the listed lifecycle events. Never audit file contents.

## Existing architecture (established by exploration)

- Backend: ASP.NET Core modular monolith, EF Core / Npgsql. **Controller → Service (interface+impl) → DbContext.**
  No MediatR/CQRS, no FluentValidation — validation is inline, throwing `DomainValidationException`.
- Frontend: React + Vite + TypeScript. **No form library** — hand-rolled controlled `useState` forms with
  `validate()`, `getFieldError`/`FieldErrors` from `api/problemDetails.ts`, `ValidationSummary`,
  `UnsavedChangesGuard`. Shared form kit in `src/components/ui/` (`FormField`, `FormSection`, `FormActions`,
  `Button`, `SearchableSelect`, `Tabs`, `Modal`, `DataTable`, …).
- `EmployeeForm.tsx` (565 lines) and `CustomerForm.tsx` (833 lines) are **already single shared components**
  with a `mode: 'create' | 'edit'` prop, built from stacked `<FormSection>` blocks. Create adds sections;
  edit is a tabbed detail page (`?tab=` query param) whose collection tabs are independently self-saving panels.
- Permissions: `PermissionCodes.cs` — `module.action` snake_case constants + an `All` catalog. Enforced by
  `[RequirePermission(...params codes)]` (any-of). Seeded via `PermissionCatalogSeeder` +
  `DefaultRoleDefinitions`/`DefaultRoleSeeder` with idempotent `DefaultRoleUpgrades`.
- Audit: explicit `IAuditService.RecordAsync(entityType, entityId, action, old, new, ct)` with purpose-built
  anonymous objects; plus an automatic stamping interceptor. Tenant isolation is **explicit** per query
  (`Where(x => x.TenantId == _tenantContext.TenantId)`), no global filter. Selective optimistic concurrency
  via `Guid Version` + `.IsConcurrencyToken()`.
- Issued items today: `IssuedItemTemplate` (Name, Category, ApplicableJobFunctionCodes CSV, DefaultQuantity,
  RequiresSerialNumber, RequiresReceivedDate, ReturnRequired, IsActive, SortOrder) and `EmployeeIssuedItem`
  (EmployeeId, TemplateId?, NameSnapshot, CategorySnapshot, Status{NotIssued,Issued,Returned,Missing,Damaged},
  IssuedDate?, Quantity, SerialNumber, Notes, IssuedByUserId, ReturnedDate, ReturnCondition,
  ReceivedBackByUserId). **No stock/variant/size concept exists anywhere.**
- Peppol: two separate nullable columns on `Customer` (`PeppolId`, `PeppolScheme`) with backend validation
  (scheme = exactly 4 digits; id must not embed a `:`; scheme requires an id). `ICompanyRegistryProvider`
  is the lookup seam (returns Peppol fields in `CompanyRegistryResult`); only `NullCompanyRegistryProvider`
  is registered. No migration is needed to "group" the columns — grouping is a presentation change.
- Planning: `EmployeePlanningPage.tsx` renders an employees×days grid + optional list view; `<ScheduleLegend/>`
  (collapsible `<details>` in `ScheduleChip.tsx`) is currently rendered **below** the grid. Colours/icons come
  from `types.ts` (`SCHEDULE_STATE_*`). No day/week/month toggle — period is 1/2/4 weeks + a list toggle.

## Decisions (confirmed)

- **Hybrid edit integration.** The section-nav shell wraps the whole form. Core scalar-field sections share the
  one existing submission (create + edit). Collection/upload sections **embed the existing self-saving panels**
  unchanged (documents, issued-items, qualifications, driver profile, contacts, locations, communication,
  billing) — no business-logic duplication.
- **Reuse Locaties** for the customer "Adressen" section (inline primary address + embedded
  `CustomerLocationsPanel`, which already provides additional addresses, types and billing/loading/delivery
  defaults). No new address entities.
- **Generic attribute system** for variants (no hardcoded Size/Colour/Model columns).
- **Delivery:** frontend-light areas (sub-project 1) first as a reviewable increment; stock subsystem
  (sub-project 2) second.

---

## Sub-project 1 — Form navigation, Peppol, planning legend

### 1a. `SectionedForm` shell — `src/components/ui/`

Reusable, form-library-agnostic. Contract:

```ts
interface FormSectionDef {
  id: string;
  label: string;
  optional?: boolean;
  render: () => ReactNode;        // section body
  fieldKeys?: string[];           // which server error keys belong here (for error routing)
  isComplete?: (state) => boolean;// subtle "done" marker
  panel?: boolean;                // embedded self-saving panel (no shared-submit participation)
}
```

Behaviour:
- Horizontal, horizontally-scrollable subnav rendered **directly below the page title**; one section body
  visible at a time. All form state stays lifted in the parent form component, so switching sections never
  loses entered values (hidden section DOM can unmount safely — state is not in the inputs).
- Error badge on any section whose `fieldKeys` intersect current `fieldErrors`; on failed submit, auto-navigate
  to the first such section. Subtle completion marker via `isComplete`. Optional sections carry no required styling.
- Sticky Save/Cancel action area (reuses `FormActions`); panel sections hide the shared Save (they self-save).
- Selected section persisted in a `?section=` URL query param (via `useSearchParams`), local-state fallback.
- Accessibility: `role="tablist"/"tab"/"tabpanel"`, `aria-selected`, roving `tabindex`, arrow-key navigation.
- Responsive: below a breakpoint the subnav collapses to a native `<select>` section switcher.

Split into small pieces: `SectionedForm.tsx`, `SectionNav.tsx` (desktop tabs), `SectionSelect.tsx` (mobile),
plus a `useSectionNavigation` hook (URL sync + error routing). Unit-tested in isolation.

### 1b. Employee form

New `features/employees/components/sections/` with one focused component per section, driven by a single shared
`employeeSections` config used by **both** create and edit:

| Section | Content | Kind |
|---|---|---|
| Algemeen | personal info, contact, address | shared-submit |
| Dienstverband | start/end dates, status, department, contract type, job functions | shared-submit |
| HR | civil status, dependent children, DIMONA, ID-card no., NRN, bank (confidential-gated) | shared-submit |
| Noodcontacten | repeatable emergency contacts | shared-submit |
| Chauffeursprofiel | driver toggle, categories, operational data | create: shared-submit; edit: embed `DriverProfilePanel` |
| Kwalificaties | qualification editor | create: inline list; edit: embed existing panel |
| Documenten | employee-document upload | edit: embed `EmployeeDocumentsTab`; create: "available after creation" |
| Bedrijfsmiddelen | issued items | edit: embed `IssuedItemsTab`; create: prepared list (sub-project 2) |
| Notities | internal notes | shared-submit |

Confidential gating (`employees.view_confidential`) preserved exactly. The detail page keeps its tabs but the
Profiel tab now renders the sectioned form; edit-only panels are reachable both as tabs and as embedded sections.

### 1c. Customer form

`customerSections` config, both modes:

| Section | Content | Kind |
|---|---|---|
| Algemeen | name, nickname, legal name, number, category, general contact, preferred language | shared-submit |
| Adressen | inline primary address + embed `CustomerLocationsPanel` (edit) | mixed |
| Contactpersonen | embed `CustomerContactsPanel` (edit) / first-contact block (create) | panel |
| Fiscaal & Peppol | VAT no., company no., VAT treatment/percent/country, **grouped Peppol**, VAT notes | shared-submit |
| Bank | IBAN, BIC, bank name, account, currency | shared-submit |
| Facturatie | billing email/language, payment terms, default entity, reference/PO/signed-note policies | shared-submit |
| Communicatie | embed `CustomerCommunicationPanel` (edit) | panel |
| Tarieven & toeslagen | embed `CustomerBillingPanel` (diesel surcharge, PO policy) (edit) | panel |
| Notities | internal notes | shared-submit |

Fiscal gating (`customers.manage_fiscal`) preserved.

### 1d. Grouped Peppol control

- `PeppolFieldGroup` component: Scheme `<select>` + Participant-ID input + a status chip with states
  **auto-retrieved / manual / not-found / not-validated**. Scheme and ID are visually one control.
- Backend: add `PeppolSchemeCatalog` (mirrors `VatTreatmentCatalog`) — authoritative `{code, label, countryCode}`
  list — exposed at `GET /api/customers/peppol-schemes`, replacing the frontend's hardcoded datalist.
- Registry lookup (`ICompanyRegistryProvider` via existing `POST /api/customers/registry-lookup`) populates
  **both** fields and marks them auto-retrieved; where the provider returns validated data the scheme is inferred
  automatically. Overwriting existing manual (non-empty) values requires an explicit confirm — no silent overwrite.
  Never invent a Peppol ID; never scrape unofficial sources (seam only). Manual fallback stays for users with the
  fiscal permission.
- Validation: reuse/extend the existing backend combination validation (scheme 4 digits, id present, id has no
  scheme prefix); optionally cross-check scheme against the catalog. Manual Peppol changes are audited via the
  existing fiscal-change audit path (verify Peppol is captured; add a dedicated note if not).
- Tests: auto-populate both fields, not-found state, combination validation, no-silent-overwrite, manual override.

### 1e. Planning legend reposition

Move `<ScheduleLegend/>` so the page order is: title/description → period controls + filters → legend
(collapsible, trigger+summary above the calendar) → grid → list. Preserve all colours/types/icons; works for the
grid and list views. Test asserts legend precedes the calendar body in the DOM.

---

## Sub-project 2 — Stock / assets / issuance (greenfield, additive)

### Data model

**Extend `IssuedItemTemplate`** (all new columns default safe → existing rows unchanged):
`Description?`, `Unit?`, `Notes?`, `StockTrackingEnabled` (default **false**), `VariantsEnabled` (false),
`AllowNegativeStock` (false → block issuance when insufficient), `LowStockThreshold?`, `MinimumStock?`,
`StorageLocation?`, `CurrentStock` (int, default 0 — cached aggregate used only when the template has no variants).
Existing `RequiresSerialNumber`, `ReturnRequired`, `DefaultQuantity`, `IsActive`, `SortOrder` are reused
(the "Serienummer verplicht" / "Retour verplicht" toggles map to existing flags). Note: size tracking is not a
separate flag — a "size" is simply an attribute named "Maat"; `VariantsEnabled` governs the whole system.

**New — generic attribute system (no hardcoded attribute columns; attributes are reusable master data):**
- `IssuedItemAttributeDefinition`: `Id`, `TenantId`, `Name`, `AllowCustomValues` (bool), `IsShared` (bool —
  `true` = reusable master attribute available to any template; `false` = template-specific), `SortOrder`,
  `IsActive`, audit. Tenant-level attribute *definitions* (e.g. Maat, Kleur, Opslag, Schoenmaat, Model, Generatie)
  reusable across templates.
- `IssuedItemAttributeOption`: `Id`, `AttributeDefinitionId`, `Value`, `SortOrder`, `IsActive`, audit. Reusable
  predefined values for a definition (Maat → XS/S/M/L/XL; Opslag → 64 GB/128 GB/256 GB; Model → MC3300/MC3400).
  Admins add custom values here at any time.
- `IssuedItemTemplateAttribute`: `Id`, `TemplateId`, `AttributeDefinitionId`, `SortOrder`. Join selecting which
  attribute definitions a template uses. A template reuses existing shared definitions (and their values) or
  references a template-specific (`IsShared=false`) definition when a bespoke attribute is needed.
- `IssuedItemVariant`: `Id`, `TemplateId`, `Label` (generated display label from its values, e.g. "M / Zwart"),
  `CurrentStock` (cached), `IsActive`, `SortOrder`, `Guid Version` (concurrency token), tenant + audit.
- `IssuedItemVariantValue`: `Id`, `VariantId`, `AttributeDefinitionId`, `AttributeNameSnapshot`,
  `AttributeOptionId?`, `Value`. Carries the concrete combination (option reference when chosen from master
  values, or a free `Value` when `AllowCustomValues`). A variant is a set of these.

Reuse rule: templates reference shared master definitions/values where practical (Size, Colour, Shoe size,
Storage, Model…), and only create template-specific definitions when the attribute is genuinely bespoke.

**Stock ownership:** stock belongs to the **variant** when the template has variants, otherwise to the template's
cached `CurrentStock`. `StockMovement` is the **source of truth**; `CurrentStock` is a cache mutated only inside
the movement transaction.

**New `StockMovement`:** `Id`, `TemplateId`, `VariantId?`, `Quantity` (signed), `MovementType` enum
(`InitialStock, Purchase, Correction, Issue, Return, Damaged, Lost, Disposed, Transfer`), `Reason?`, `Notes?`,
`EmployeeId?`, `PerformedByUserId`, `Timestamp`, `ResultingStock`, tenant + audit.

**Extend `EmployeeIssuedItem`:** add `VariantId?` (SetNull soft ref) + `VariantSnapshot` (frozen label) so history
survives later attribute/variant edits (mirrors the existing Name/Category snapshot pattern).

### Services

- `IStockService`: `RecordMovementAsync(...)` creates the movement, updates the owning cached `CurrentStock`
  atomically (guarded by the variant `Version`), computes `ResultingStock`, and audits per movement type.
  Helpers: `GetAvailableAsync(templateId, variantId?)`, receipt, correction (reason required), initial stock.
- Issuance integrates into the existing `IssuedItemService` (no duplicate issuance path):
  - When `StockTrackingEnabled`: require variant selection if the template has variants; block issuance when
    stock is insufficient unless the actor holds `inventory.override_negative_stock` (or the template allows
    negative stock); create `EmployeeIssuedItem` + `Issue` movement + variant/name snapshots in **one transaction**.
  - When stock tracking is off: behaves exactly as today — **no inventory mutation**.
  - Return: update issued-item status; condition ∈ {good, damaged, lost, disposed}; **good** optionally returns
    to usable stock (`Return` movement), damaged/lost/disposed **never** auto-return (they record
    `Damaged`/`Lost`/`Disposed` movements instead).
  - Employee-create issuance is *prepared* client-side and committed only after the employee row succeeds,
    inside the existing create transaction, so a failed create consumes no stock.

### Permissions (reuse conventions)

Keep `issued_items.view`, `issued_items.manage`, `issued_items.manage_templates`. Add `inventory.view`,
`inventory.manage`, `inventory.adjust`, `inventory.override_negative_stock`. Register in `PermissionCodes`,
seed into `DefaultRoleDefinitions`, enforce via `[RequirePermission]`.

### Audit

Record: template created/changed, stock-tracking enabled/disabled, variant added/archived, stock receipt,
stock correction, item issued, item returned, item marked damaged/lost/disposed, Peppol data manually changed.
Purpose-built anonymous objects only — never file contents.

### UI

- **Template overview** (settings): columns for name, category, stock-managed, variants/sizes, total available,
  low-stock warning, serial-required, return-required, active. Filters: category, active/inactive, stock-managed,
  low-stock. Stock controls do **not** live in the table.
- **Template detail/edit view:** configure the template + toggles (related inputs shown only when their toggle is
  on), manage attributes/options and variants, view current stock, add stock, correct stock (reason required),
  view movement history, and see which employees currently hold the item.
- **Employee "Bedrijfsmiddelen" section:** currently-issued items; returned items via filter; issue flow (choose
  template → variant/size only when required → quantity → serial only when required → available-stock preview →
  confirm) with issued date + return requirement; return flow with condition + notes.

### Migrations

Additive only. New tables (`issued_item_attribute_definitions`, `issued_item_attribute_options`,
`issued_item_template_attributes`, `issued_item_variants`, `issued_item_variant_values`, `stock_movements`)
+ additive columns on `issued_item_templates` and
`employee_issued_items`. Safe defaults (stock/variants disabled). Historical issued items remain visible;
snapshots preserve historically relevant template/variant labels.

## Testing

**Frontend (sub-project 1):** switching sections preserves values; create and edit use the same grouping;
validation opens the correct section; optional sections don't block save; mobile section selector works;
permission-restricted fields stay hidden; Peppol id+scheme grouped; provider result populates both;
no-provider/not-found; no silent overwrite; manual override validation; legend renders above the calendar and
stays correct across supported views.

**Backend (sub-project 2):** stock-disabled item issued without inventory mutation; stock-enabled issue creates a
movement; insufficient stock blocked (and overridable with permission); variant selection required only when
enabled; custom attribute-option creation; variant-specific stock; issue reduces stock; good return increases
usable stock; damaged return does not; employee-create failure consumes no stock; tenant isolation; permissions;
concurrency protection (variant `Version`).

**Commands per sub-project:** `dotnet build` + full backend tests; frontend TypeScript + ESLint + full frontend
tests + production build.

## Out of scope / non-goals

- No real external Peppol/registry integration (seam + null provider only; no scraping, no invented IDs).
- No new customer address entities (reuse Locaties). No day/week/month planning view (does not exist today).
- No redesign of unrelated modules; no rewrite of the existing self-saving panels.

## Deliverables

Logical commits; a final report covering: employee nav, customer nav, Peppol, planning legend, asset-template
changes, attribute/variant model, stock management, stock movements, issuance workflow, permissions, migrations,
tests + exact totals, commit list, remaining limitations, final commit hash, and confirmation of a clean worktree.
