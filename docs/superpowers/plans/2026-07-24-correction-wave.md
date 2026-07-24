# Correction Wave 2026-07-24 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Functional correction pass over inventory (categories/stock/variants/notifications), fleet forms + staged documents + maintenance precedence UI, customer detail tariffs, transport-order subnavigation + order documents, configurable unit/customer pricing engine, and self-service calendar notes + colours.

**Architecture:** Extend existing modules in place (Employees/inventory, Fleet, Partners, Tarification, Orders, Invoicing, Portal). All DB changes are additive EF Core migrations on PostgreSQL. Frontend reuses the `SectionedForm` primitive, `LookupSelect`/lookup master data, the client-side "prepared follow-up" staging pattern, and the existing notification service + producer-sweep dedup patterns.

**Tech Stack:** .NET 8 + EF Core (Npgsql), xUnit; React + TypeScript + Vite + Vitest; React Router data router.

## Global Constraints

- Tenant isolation via `AuditableTenantEntity` + query filters on every new entity; audit fields come free via the interceptor.
- All migrations additive; never drop/rename existing columns (`MinimumStock` stays in DB, only leaves the UI).
- Existing behaviour must not regress: manual `AgreedPrice` order flow and invoice `UnitPrice = AgreedPrice ?? 0` keep working when no pricing config exists; existing rate cards keep working and become the engine fallback.
- Switching internal form tabs must never trigger `UnsavedChangesGuard` (guard blocks pathname changes only; `useSectionNavigation` keeps tab state in `useState`).
- New permissions ship as new `DefaultRoleUpgrades` steps (v12, v13) — never amend an already-shipped version step.
- Dutch UI copy, following existing labels (e.g. "Lage-voorraadgrens", "Volgorde in lijst", "Categorieën beheren").
- Commit per task with conventional-commit messages; run affected backend/frontend tests before each commit.

## Key existing files (reference)

- Inventory: `Modules/Employees/Entities/IssuedItemTemplate.cs`, `IssuedItemVariant.cs`, `IssuedItemAttribute.cs`, `StockMovement.cs`, `EmployeeIssuedItem.cs`; services `IssuedItemService.cs`, `InventoryService.cs` (ledger primitive `ApplyMovement`, `ReceiveStockAsync`, `CorrectStockAsync`, `ResolveStockTargetAsync`); controllers `IssuedItemsController.cs`, `InventoryController.cs`; config `IssuedItemConfigurations.cs`. FE: `src/features/issued-items/` (`TemplateFormModal.tsx`, `IssuedItemTemplatesPage.tsx`, `IssuedItemTemplateDetailPage.tsx`, `IssuedItemsTab.tsx`, `issuedItemsApi.ts`, `inventoryApi.ts`), `src/features/employees/components/PreparedIssuedItemsEditor.tsx`.
- Notifications: `Modules/Notifications/Services/NotificationService.cs` (`INotificationService`, `NotifyPermissionHoldersAsync`, `NotificationTypeCatalog.Map`), `ExpiryNotificationHostedService.cs` + `ExpiryNotificationProducer.cs` (sweep + `AlreadyNotifiedAsync` LinkPath-`#id` dedup, 7-day window).
- Lookups: `Common/Lookups/LookupEntity` (Code/Name/Description/IsActive/SortOrder), `LookupControllerBase<T>`, FE `useLookupOptions`, `LookupSelect` (valueKey, managePermission), `src/features/master-data/lookupRegistry.ts`.
- SectionedForm: `src/components/ui/SectionedForm.tsx` (`SectionDef {id,label,optional?,hasError?,complete?,panel?,render}`), `useSectionNavigation.ts` (+`firstSectionWithError`), `FormSection.tsx`, `UnsavedChangesGuard.tsx`. Reference: `EmployeeForm.tsx`/`employeeSections.ts`, `CustomerForm.tsx`/`customerSections.ts`.
- Prepared staging (client-only): `src/features/employees/utils/preparedFollowUp.ts`, `PreparedDocumentsEditor.tsx`, `NewEmployeePage.tsx` (parent-first ordering + per-item retry via `CreateFollowUpDialog.tsx`).
- Fleet: `Modules/Fleet/Entities/{Vehicle,Trailer,FleetDocument,MaintenancePolicy,Inspection}.cs`; `MaintenancePolicyService.ResolveAsync` (asset→category→company precedence, tested); `FleetDocumentService` (metadata create → `AttachFileAsync` upload, storage category `fleet-documents`, `IFileStorageService`); controllers `VehiclesController`, `TrailersController`, `FleetDocumentsController`, `MaintenancePoliciesController`. FE: `src/features/vehicles/pages/{NewVehiclePage,VehicleDetailPage}.tsx`, trailers equivalents, `src/features/fleet-documents/components/FleetDocumentsPanel.tsx`, `src/features/maintenance-policies/`.
- Customers: `CustomerForm.tsx` sections incl. `tarieven` (currently renders only `CustomerBillingPanel` = diesel + PO), `CustomerDetailPage.tsx` tabs; rate cards module `Modules/Tarification/` (`RateCard`, `RateCardService.QuoteAsync`, `api/rate-cards`, perms `tariffs.view|manage`), FE `src/features/tarification/pages/RateCardsPage.tsx`, `api/rateCardsApi.ts`.
- Orders: `Modules/Orders/Entities/TransportOrder.cs` (`AgreedPrice`, `QuantityUnitCode`), `CargoItem.cs`; `TransportOrderForm.tsx` (~993 lines, one long form); `UnitType : LookupEntity` (`api/unit-types`, perms `unit_types.view|manage`, migration `20260721184002_UnitTypes`).
- Invoicing: `InvoiceService.CreateAsync` (line per order, `UnitPrice = AgreedPrice ?? 0`, diesel lines via `DieselSurchargeCalculator`).
- Portal/calendar: `MeController` (`api/me/planning`, self-scoped via `PortalService.MyEmployeeIdAsync`), `ShiftService.GetEmployeeScheduleAsync`/`BuildEntries`, `ScheduleEntryDto` (no colour), FE `PortalPlanningPage.tsx` (month view renders plain `<span className="portal-month-entry">`), `ScheduleChip.tsx`, `employee-planning.css` per-state colours, `LeaveType.Colour` (`Hr/Entities/LeaveType.cs:33`) never reaches the feed.
- Permissions: `Modules/Identity/PermissionCodes.cs`; role seeding `Data/DefaultRoleUpgrades.cs` (`CurrentVersion = 11`).

---

# PHASE A — INVENTORY

### Task A1: Asset categories as master data (spec 1.1 + 1.2)

**Files:**
- Create: `Modules/Employees/Entities/IssuedItemCategory.cs` (`public class IssuedItemCategory : LookupEntity {}`)
- Create: `Modules/Employees/Configurations/IssuedItemCategoryConfiguration.cs` (table `issued_item_categories`, unique (TenantId, Code) filtered `!IsDeleted`, mirror another lookup config)
- Create: `Modules/Employees/Controllers/IssuedItemCategoriesController.cs` — `[Route("api/issued-item-categories")]`, extends `LookupControllerBase<IssuedItemCategory>`; read perms any-of `issued_items.view|inventory.view|issued_items.manage_templates`; write perm `inventory.manage` (follow the exact base-class pattern used by `UnitTypesController`).
- Modify: `IssuedItemTemplate.cs` — add `public Guid? CategoryId { get; set; }` (keep `Category` string as legacy/snapshot).
- Modify: `IssuedItemConfigurations.cs` — FK CategoryId → issued_item_categories, `OnDelete(Restrict)`.
- Modify: `TransportationDbContext.cs` — `DbSet<IssuedItemCategory> IssuedItemCategories`.
- Modify: `IssuedItemService.cs` — `SaveIssuedItemTemplateRequest` gains `Guid? CategoryId`; on save resolve category (tenant-checked), set `CategoryId` + sync `Category` string to category Name (snapshot); `IssuedItemTemplateDto` gains `CategoryId`; `ListTemplatesAsync` gains optional `Guid? categoryId` filter param, plumbed through `IssuedItemsController` `GET /api/issued-item-templates?categoryId=`.
- Migration: `IssuedItemCategories` — new table + `CategoryId` column + backfill SQL: insert distinct `(TenantId, Category)` from `issued_item_templates` (non-deleted) as categories (Code = slug of name, Name = Category, IsActive = true), then set `CategoryId` by joining on name. Verify actual column names from the generated migration/other tables before writing the SQL.
- FE Modify: `TemplateFormModal.tsx` — replace free-text Categorie input with `LookupSelect` (basePath `/api/issued-item-categories`, managePermission `inventory.manage` → gives "+ Categorieën beheren" affordance); rename Volgorde label to "Volgorde in lijst" with hint "Bepaalt de volgorde in het overzicht en in keuzelijsten."; REMOVE the Minimumvoorraad input (field is inert; keep API field optional).
- FE Modify: `IssuedItemTemplatesPage.tsx` — category filter becomes a select fed by `useLookupOptions('/api/issued-item-categories')` filtering on categoryId (server param), falling back to legacy string match for templates without CategoryId.
- FE Modify: `lookupRegistry.ts` — register issued-item-categories so the master-data settings page can manage them (create/edit/deactivate/sort). Check registry entry shape and mirror it.
- FE Modify: `issuedItemsApi.ts` — `IssuedItemTemplate.categoryId`, `IssuedItemTemplateInput.categoryId`, list param.

**Tests:**
- Backend new `Employees/IssuedItemCategoryTests.cs`: CRUD via lookup service incl. tenant isolation (two tenants, list only own), deactivate hides from active list, template save resolves + snapshots name, list filter by categoryId.
- FE: extend `issuedItemTemplatesPage.test.tsx` — category select rendered from lookup data, filter narrows rows; TemplateFormModal shows "Volgorde in lijst" and no "Minimumvoorraad".

- [ ] Write failing backend tests → run → implement entity/config/controller/service changes → migration → run tests → FE changes + FE tests → commit `feat(inventory): asset categories as tenant master data + category filter`

### Task A2: Real stock field via ledger (spec 1.3)

**Files:**
- Modify: `IssuedItemService.cs` — `SaveIssuedItemTemplateRequest` gains `int? Stock` and `string? StockCorrectionReason`. In create: if `StockTrackingEnabled && !VariantsEnabled && Stock > 0` → after template row added, call inventory ledger (inject `IInventoryService` or reuse its internal ApplyMovement via a new `IInventoryService.InitializeOrCorrectAsync(template, variant:null, targetQuantity, reason)` helper): first movement = `InitialStock`. In update: if Stock provided and differs from current → `Correction` movement with required reason (validation error `stockCorrectionReason` when missing). Never touch stock when `VariantsEnabled`. One SaveChanges (caller-owned, matching existing ledger contract).
- Modify: `InventoryService.cs` — add `Task ApplyTemplateStockTargetAsync(IssuedItemTemplate template, int targetQuantity, string? reason, CancellationToken ct)` public method wrapping existing resolve/apply logic (no SaveChanges inside).
- FE Modify: `TemplateFormModal.tsx` — non-variant stock block becomes:
  Voorraadbeheer [x] → `Voorraad` (number, current value prefilled from `currentStock`/`totalAvailable` on edit), `Lage-voorraadgrens` (hint: "Waarschuwing wanneer de voorraad deze grens bereikt."), `Eenheid`, `Opslaglocatie`. When editing and Voorraad differs from the loaded value → show required "Reden voor correctie" input. When `variantsEnabled` → hide Voorraad entirely and show read-only "Totale voorraad: N (som van varianten)".
- FE Modify: `issuedItemsApi.ts` — input fields `stock?`, `stockCorrectionReason?`.

**Tests:**
- Backend extend `Employees/InventoryStockTests.cs`: create with Stock=25 → InitialStock movement, CurrentStock 25; update 25→30 with reason → Correction +5 with `ResultingStock=30` and prior movement intact; update without reason → validation error; variant template ignores Stock.
- FE new `templateFormModal.test.tsx`: shows Voorraad field for non-variant; correction reason appears on change; hidden + computed-sum text when variants enabled.

- [ ] TDD as above → commit `feat(inventory): template stock initialization/correction through the stock ledger`

### Task A3: Variant configuration workflow + computed totals (spec 1.5)

**Files:**
- Modify: `InventoryService.cs` — new `Task<IReadOnlyList<IssuedItemVariantDto>> GenerateVariantsAsync(Guid templateId, GenerateVariantsRequest request, CancellationToken ct)`; `record GenerateVariantsRequest(IReadOnlyList<GenerateVariantsDimension> Dimensions);` `record GenerateVariantsDimension(Guid AttributeDefinitionId, IReadOnlyList<Guid> OptionIds);` — ensures template attributes set, builds the cartesian product of selected options, skips combinations that already exist (match on the set of (AttributeDefinitionId, OptionId)), creates `IssuedItemVariant` (+`IssuedItemVariantValue`s, Label = values joined " / ") with stock 0.
- Modify: `InventoryController.cs` — `POST /api/issued-item-templates/{id}/variants/generate` (perms `InventoryManage|IssuedItemsManageTemplates`).
- FE Modify: `IssuedItemTemplateDetailPage.tsx` — in the Variants section add "Varianten genereren" button → dialog: per template attribute a checkbox list of its options (+ shortcut to add new options inline via existing option endpoints) → calls `generateVariants`; per-variant row keeps stock + Laag badge; header shows "Totale voorraad: N (berekend)".
- FE Modify: `TemplateFormModal.tsx` — enabling Varianten shows explainer "Configureer maten/uitvoeringen en voorraad per variant na het opslaan." ; after CREATE with variantsEnabled, the templates page navigates straight to the detail page variants tab (pass state/query `?tab=variants`).
- FE Modify: `inventoryApi.ts` — `generateVariants(templateId, dimensions)`.
- Detail page gets tab-aware deep link (`?tab=` via `useSearchParams`, mirroring VehicleDetailPage) — groundwork for A5.

**Tests:**
- Backend extend `InventoryStockTests.cs` (or new `VariantGenerationTests.cs`): generate 2×2 → 4 variants with correct labels/values; regenerate with one extra option → only missing combos added; tenant isolation (template of other tenant → not found); issuing variant template without variantId still rejected (existing behaviour guard).
- FE: detail-page test for generate dialog happy path (mock API), and computed total shown.

- [ ] TDD → commit `feat(inventory): cartesian variant generation workflow + computed stock totals`

### Task A4: Low-stock notifications (spec 1.4)

**Design:** emission on threshold crossing at mutation time (not a checkbox that does nothing, not sweep-only). New permission `inventory.low_stock_alerts` ("Ontvangt meldingen bij lage voorraad") targets recipients via `NotifyPermissionHoldersAsync`; per-user category preference (existing) allows opt-out.

**Files:**
- Modify: `PermissionCodes.cs` — `InventoryLowStockAlerts = "inventory.low_stock_alerts"` + catalog entry (module `inventory`).
- Modify: `DefaultRoleUpgrades.cs` — new v12 step: hr + magazijn get `inventory.low_stock_alerts`; bump `CurrentVersion = 12`.
- Modify: `NotificationService.cs` — `NotificationTypeCatalog.Map` add `["inventory_low_stock"] = (NotificationCategory.System, NotificationSeverity.Warning)`.
- Create: `Modules/Employees/Services/LowStockNotifier.cs` — `ILowStockNotifier { Task NotifyIfCrossedAsync(IssuedItemTemplate template, IssuedItemVariant? variant, int previousQuantity, int newQuantity, CancellationToken ct); }` — fires only when `template.StockTrackingEnabled`, threshold set, `previous > threshold && new <= threshold`; dedup: skip if an unarchived, undeleted `inventory_low_stock` notification with `LinkPath` ending `#<templateId or variantId>` exists within the last 7 days (same query shape as `ExpiryNotificationProducer.AlreadyNotifiedAsync`); message includes template/variant label + current qty + threshold; `LinkPath = "/settings/issued-item-templates/{templateId}?tab=stock#{id}"`; recipients `NotifyPermissionHoldersAsync(InventoryLowStockAlerts, ...)`. Tenant safety comes from the service running inside the tenant-scoped request.
- Modify: `InventoryService.ApplyMovement` call sites (`InventoryService` receipts/corrections, `IssuedItemService.ApplyStockTransitionAsync`) — capture previous quantity, after mutation invoke notifier (still before SaveChanges is fine — notification rows join the same unit of work, matching how producers use the ChangeTracker).
- Threshold basis: non-variant → template `CurrentStock` vs `LowStockThreshold`; variant → variant `CurrentStock` vs template `LowStockThreshold` (matches existing "Laag" badge semantics).
- FE: none required beyond existing notifications UI; the permission appears automatically in role management via the catalog.

**Tests (new `Employees/LowStockNotificationTests.cs`):** issue that crosses threshold → notification for permission holder only (user without perm gets none); second crossing within window → no duplicate; receipt back above then crossing again after window → notifies; variant-level crossing references variant; other tenant unaffected.

- [ ] TDD → commit `feat(inventory): low-stock notifications on threshold crossing (perm v12, deduped)`

### Task A5: Inventory overview + detail tabs (spec 1.6)

**Files:**
- FE Modify: `IssuedItemTemplateDetailPage.tsx` — restructure into `Tabs` (pattern from `VehicleDetailPage`): `algemeen` (settings summary + edit button opening TemplateFormModal), `voorraad` (stock summary, receipt/correction buttons, Lage-voorraadgrens), `varianten` (only when variantsEnabled; A3 content), `houders` ("Huidige houders" table), `bewegingen` ("Voorraadhistoriek" movements table). Tab syncs to `?tab=` (already added in A3).
- FE Modify: `IssuedItemTemplatesPage.tsx` — ensure columns: Naam, Categorie, Voorraadstatus badge (OK/Laag/Uit voorraad/—), Voorraad (or computed variant sum), Actief, plus search + category filter (A1) + bestaande stock filter. Row click → detail page.
- FE tests: extend detail-page test for tab rendering + holders/movements fetch per tab.

- [ ] Implement → tests → commit `feat(inventory): tabbed template detail + purposeful overview`

---

# PHASE B — FLEET FORMS, STAGED DOCUMENTS, MAINTENANCE PRECEDENCE

### Task B1: Vehicle + trailer sectioned create/edit forms (spec 2)

**Files:**
- Create: `src/features/vehicles/components/VehicleForm.tsx` + `vehicleSections.ts`; `src/features/trailers/components/TrailerForm.tsx` + `trailerSections.ts` — mirror `CustomerForm` architecture: props `{ mode: 'create'|'edit', initial, isSubmitting, submitError, serverFieldErrors, onSubmit, onCancel, extraSections?, editPanels? }`, `useSectionNavigation`, `firstSectionWithError`, `<UnsavedChangesGuard when={dirty && !isSubmitting} />`, `SectionedForm`.
- Vehicle sections: `algemeen` (InternalNumber, LicensePlate, CategoryId, Brand, Model, OwnershipType, OperationalStatus+StatusReason edit-only), `registratie` (Vin, Year, FirstRegistrationDate), `capaciteit` "Capaciteit & afmetingen" (GVW, Payload, L/B/H, Volume+manual toggle with existing `computeVolumeM3`, LoadingMeters, AxleCount), `techniek` (FuelType, EmissionClass, RequiredLicenceCode, OdometerKm, Consumption, HasCrane/HasRefrigeration/HasTailLift/AdrSuitable), `documenten` (panel: create → `PreparedFleetDocumentsEditor` (B2); edit → `FleetDocumentsPanel`), `onderhoud` "Onderhoud & keuringen" (panel: edit → `MaintenancePolicySummary` (B3) + existing panels link; create → info text "Na aanmaken worden onderhoud en keuringen ingepland volgens het geldende beleid."), `toewijzing` (FixedDriverId), `notities`.
- Trailer sections: `algemeen` (incl. Vin/Year/FirstRegistrationDate folded in), `capaciteit` (CapacityKg, dims, LoadingMeters, AxleCount), `techniek` (HasRefrigeration, AdrSuitable), `documenten`, `onderhoud`, `notities`.
- Modify: `NewVehiclePage.tsx`, `NewTrailerPage.tsx` — swap long form for the new components (keep mutation logic).
- Modify: `VehicleDetailPage.tsx`, `TrailerDetailPage.tsx` — edit mode renders the new form with `editPanels`; view-mode tabs unchanged.
- Section field-key maps drive `hasError` badges exactly like `CUSTOMER_SECTION_FIELD_KEYS`.

**Tests (FE):** new `vehicleSectionedForm.test.tsx` + `trailerSectionedForm.test.tsx`: sections render, switching tabs preserves entered values, no `UnsavedChangesGuard` dialog on tab switch (assert blocker not triggered — mirror `employeeSectionNavRegression.test.tsx`), first-error routing on submit with missing required field.

- [ ] TDD → commit `feat(fleet): sectioned vehicle & trailer create/edit forms`

### Task B2: Staged documents during vehicle/trailer creation (spec 3)

**Files:**
- Create: `src/features/fleet-documents/utils/preparedFleetDocs.ts` — types `PreparedFleetDocument { key, file: File, documentType: FleetDocumentType, customTypeName?, title/documentNumber?, issueDate?, expiryDate?, notes? }`; `uploadPreparedFleetDocuments(owner: {kind:'vehicle'|'trailer', id: string}, docs): Promise<FollowUpResult[]>` — per doc: create metadata via existing nested POST, then `AttachFileAsync` upload endpoint; per-item ok/error results, never throws (mirror `preparedFollowUp.ts`).
- Create: `src/features/fleet-documents/components/PreparedFleetDocumentsEditor.tsx` — `{ value, onChange }`, real `<input type="file">` (accept .pdf/.jpg/.jpeg/.png, 10 MB client hint), fields Titel/Documentnummer, Documenttype select (existing `FleetDocumentType` labels incl. LeasingContract, TachographCalibration, Insurance, Registration, TechnicalInspection, CraneInspection, Other), Uitgiftedatum, Vervaldatum, Notities.
- Modify: `NewVehiclePage.tsx`/`NewTrailerPage.tsx` — hold `preparedDocs` state; create parent FIRST, then run uploads; partial failures open a retry dialog (reuse/generalize `CreateFollowUpDialog` — move it to `src/components/ui/CreateFollowUpDialog.tsx` re-exported from the employees path to avoid breaking imports). Failed creation → nothing uploaded → no orphans (ordering guarantees it, same as employees).

**Tests (FE):** `newVehiclePreparedDocs.test.tsx`: staged doc uploads after successful create (assert order: create → doc POST → file upload); create failure → no document calls; one failed upload → retry dialog rerunning only failures. Mirror `newEmployeePreparedDocs.test.tsx`.

- [ ] TDD → commit `feat(fleet): staged document upload during vehicle/trailer creation`

### Task B3: Maintenance precedence surfacing + asset overrides (spec 4)

**Files:**
- Modify: `MaintenancePoliciesController.cs` — new `GET /api/maintenance-policies/effective?assetKind=Vehicle|Trailer&assetId=<guid>` (perm any-of `MaintenancePoliciesView|VehiclesView|TrailersView`): loads asset (tenant-checked) for its CategoryId, returns `EffectivePoliciesDto { EffectivePolicyDto? Maintenance; EffectivePolicyDto? Inspection; }` with `record EffectivePolicyDto(Guid PolicyId, MaintenancePolicyLevel Level, string SourceLabel, int? IntervalMonths, int? IntervalKm, int WarningDays, string? Description)`; `SourceLabel` computed server-side: "Bedrijfsstandaard" / `"Overgenomen van categorie {name}"` / "Specifieke regel voor voertuig" (or "…voor oplegger"). Uses existing `ResolveAsync` — no precedence duplication.
- Modify: `MaintenancePolicyService.cs` — small helper to build the DTO; no behaviour change to `ResolveAsync`.
- Create: `src/features/maintenance-policies/components/MaintenancePolicySummary.tsx` — props `{ assetKind, assetId, categoryId }`; shows the two effective rules with source labels; when user has `maintenance_policies.manage`: "Afwijkende regel instellen" opens a dialog (IntervalMonths, IntervalKm vehicle-only, WarningDays, Description) that POSTs a policy with VehicleId/TrailerId set (Kind selectable Maintenance/Inspection); when an asset-level rule exists: "Gebruik opnieuw categorie-/bedrijfsstandaard" button → DELETE that policy (confirm dialog).
- Modify: `MaintenancePoliciesPage.tsx` — no functional change beyond making the "Geldt voor" level labels use the same label helper (consistency).
- Wire panel into vehicle/trailer detail "onderhoud" tab and the B1 form `onderhoud` section (edit mode).
- FE api: `maintenancePoliciesApi.ts` — `getEffectivePolicies(assetKind, assetId)`.

**Tests:**
- Backend extend `Fleet/MaintenancePolicyTests.cs`: effective endpoint returns asset override when present, category rule after override deleted, company default when no category rule; labels correct; category/default writes never mutate asset-level rows (regression guard).
- FE `maintenancePolicySummary.test.tsx`: renders source labels; reset button only when asset-level rule; manage controls hidden without permission.

- [ ] TDD → commit `feat(fleet): effective maintenance/inspection rule endpoint + asset override UI`

---

# PHASE C — CUSTOMER DETAIL (spec 5)

### Task C1: Tarieven & toeslagen on customer detail + form

**Files:**
- Create: `src/features/customers/components/CustomerRateCardsPanel.tsx` — customer-scoped rate-card management: list (`listRateCards(customerId)`), create/edit/delete dialogs reusing the field set from `RateCardsPage` (extract shared dialog into `src/features/tarification/components/RateCardDialog.tsx` and reuse in both places), plus "Prijs berekenen" quote helper scoped to this customer. Gated: render only with `tariffs.view`; mutations with `tariffs.manage`; link "Alle tarievenkaarten" → `/rate-cards?customerId=`.
- Modify: `CustomerDetailPage.tsx` — add view-mode tab `tarieven` "Tarieven & toeslagen" rendering `CustomerRateCardsPanel` + `CustomerBillingPanel` (diesel + PO stay together with the rate cards; keep existing `billing`/"Facturatie" tab focused on invoice settings or fold — decision: rename existing `billing` tab to "Tarieven & toeslagen" and include the rate-cards panel above `CustomerBillingPanel`, so no duplicate tab). "Communicatie" tab already exists — verify and leave.
- Modify: `CustomerForm.tsx` — edit-mode `tarieven` section panel now renders `CustomerRateCardsPanel` above `CustomerBillingPanel` via `editPanels.tarieven` composition in `CustomerDetailPage`.
- Later (Phase D3) this panel also hosts unit pricing config; build it as a stack of sub-panels.

**Tests:** FE `customerRateCardsPanel.test.tsx`: lists customer's cards, hides mutations without `tariffs.manage`, hidden entirely without `tariffs.view`; detail-page test asserting the tab exists and renders both panels.

- [ ] TDD → commit `feat(customers): rate cards & surcharges surfaced on customer detail/edit`

---

# PHASE D — UNIT + CUSTOMER PRICING MODEL (spec 7)

### Task D1: UnitType pricing metadata + management page

**Files:**
- Modify: `Modules/Reference/Entities/UnitType.cs` — add `public bool AllowForOrderEntry { get; set; } = true;` `public bool AllowForPricing { get; set; } = true;` `public string? Category { get; set; }` (free grouping label, max 40).
- Modify: `UnitTypeConfiguration.cs` + migration `UnitTypePricingFields` (additive columns, defaults true).
- Modify: `UnitTypesController.cs`/lookup DTO path — expose the three fields on list/save (extend the lookup DTO for this controller only; if `LookupControllerBase` is too rigid, add dedicated GET/PUT actions alongside).
- FE: extend the unit-types management UI (registry entry or dedicated settings block) with checkboxes "Beschikbaar bij orderinvoer", "Beschikbaar voor prijsafspraken", Categorie field. Order form dropdowns filter on `allowForOrderEntry`.

**Tests:** backend `Reference/UnitTypeTests.cs` extend: fields persist, list filter respects active; FE smoke test for the settings fields.

- [ ] TDD → commit `feat(pricing): unit type order-entry/pricing metadata`

### Task D2: Zones, price rules, engine (backend core)

**Files:**
- Create `Modules/Tarification/Entities/PricingZone.cs`:
```csharp
public class PricingZone : AuditableTenantEntity {
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<PricingZoneArea> Areas { get; set; } = new();
}
public class PricingZoneArea : AuditableTenantEntity {
    public Guid ZoneId { get; set; }
    public string CountryCode { get; set; } = "BE";
    public string PostalCodeFrom { get; set; } = "";
    public string PostalCodeTo { get; set; } = "";
}
```
- Create `Modules/Tarification/Entities/PriceRule.cs`:
```csharp
public enum PriceRuleBasis { PerUnit, QuantityBracket, WeightBracket, Hourly, Fixed }
public class PriceRule : AuditableTenantEntity {
    public Guid? CustomerId { get; set; }        // null = company default rule
    public Guid? UnitTypeId { get; set; }        // null allowed only for Fixed
    public PriceRuleBasis Basis { get; set; }
    public Guid? ZoneId { get; set; }            // optional zone dimension
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? UnitPrice { get; set; }      // PerUnit / Hourly rate / Fixed amount
    public decimal? MinimumAmount { get; set; }
    public List<PriceRuleBracket> Brackets { get; set; } = new();
}
public class PriceRuleBracket : AuditableTenantEntity {
    public Guid PriceRuleId { get; set; }
    public decimal FromQuantity { get; set; }    // qty or kg depending on Basis
    public decimal? ToQuantity { get; set; }     // null = open-ended
    public decimal Price { get; set; }           // bracket price
    public decimal? PricePerExtraUnit { get; set; } // open-ended increments
}
```
- Create `Modules/Tarification/Entities/ServiceOption.cs` + customer price:
```csharp
public class ServiceOption : AuditableTenantEntity {
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";       // e.g. "Levering vóór 08:00", "Laadklep", "ADR"
    public SurchargeKind Kind { get; set; }      // reuse Tarification SurchargeKind Percent|Fixed
    public decimal DefaultValue { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
public class CustomerServiceOptionPrice : AuditableTenantEntity {
    public Guid CustomerId { get; set; }
    public Guid ServiceOptionId { get; set; }
    public decimal Value { get; set; }           // customer-specific override of DefaultValue
}
```
- Create `Modules/Tarification/Entities/CustomerPreferredUnit.cs` (`CustomerId`, `UnitTypeId`, `SortOrder`).
- Create configurations for all (tables `pricing_zones`, `pricing_zone_areas`, `price_rules`, `price_rule_brackets`, `service_options`, `customer_service_option_prices`, `customer_preferred_units`; unique filtered indexes: zone (TenantId,Code); service option (TenantId,Code); preferred unit (TenantId,CustomerId,UnitTypeId); option price (TenantId,CustomerId,ServiceOptionId)). DbSets in context. Migration `PricingRules`.
- Create `Modules/Tarification/Services/PricingEngine.cs` (`IPricingEngine`):
```csharp
public record PriceCalculationLineInput(Guid UnitTypeId, decimal Quantity);
public record PriceCalculationRequest(Guid CustomerId, DateOnly Date,
    IReadOnlyList<PriceCalculationLineInput> Lines,
    string? DeliveryCountryCode, string? DeliveryPostalCode,
    decimal? WeightKg, decimal? DistanceKm, int? PalletCount,
    IReadOnlyList<Guid> ServiceOptionIds);
public record PriceBreakdownLine(string Label, decimal Amount, string Source, bool Informational = false);
public record PriceCalculationResult(IReadOnlyList<PriceBreakdownLine> Lines,
    decimal Total,                 // excl. informational lines (diesel)
    decimal TotalWithInformational,
    string Currency, string? ZoneCode, string? ZoneName, bool RequiresManualPrice);
public interface IPricingEngine { Task<PriceCalculationResult> CalculateAsync(PriceCalculationRequest request, CancellationToken ct); }
```
  Algorithm: resolve zone from delivery country+postal (numeric compare when both parse, else ordinal, against `PricingZoneArea` ranges). Per line: candidate `PriceRule`s active+effective for unit, customer-specific preferred over company default (`CustomerId == null`), zone-specific (matching resolved zone) preferred over zone-less; compute per Basis (QuantityBracket: containing bracket price, open-ended bracket adds `PricePerExtraUnit * (qty - FromQuantity)`; PerUnit: `UnitPrice*qty`; Hourly: `UnitPrice*qty` (qty = hours); WeightBracket: bracket on `WeightKg`; Fixed: `UnitPrice`), apply rule `MinimumAmount`. Unmatched line → breakdown line "Geen tarief geconfigureerd voor {unit}" Amount 0 + `RequiresManualPrice=true`. If NO rules matched any line and a `RateCard` exists for the date → delegate to `IRateCardService.QuoteAsync` (labels prefixed "Tarievenkaart:"), preserving the legacy engine as fallback. Then service options: customer price if configured else default (Percent = % of running subtotal, Fixed = amount). Then diesel surcharge from `CustomerDieselSurcharge` config (enabled → percent of subtotal) appended as `Informational` line labelled "Dieseltoeslag (wordt op factuur toegevoegd)" — kept OUT of `Total` because `InvoiceService` already adds diesel lines at invoicing (no double count).
- Create `Modules/Tarification/Controllers/PricingController.cs`:
  - `POST api/pricing/preview` → `CalculateAsync` (perms any-of `OrdersCreate|OrdersEdit|OrdersManage|TariffsView`)
  - `GET/POST/PUT/DELETE api/pricing/zones[...]` (view `TariffsView`, write `TariffsManage`)
  - `GET/POST/PUT/DELETE api/pricing/rules?customerId=` (same perms)
  - `GET/POST/PUT/DELETE api/service-options[...]` (same perms)
  - `GET/PUT api/customers/{customerId}/pricing-config` → `CustomerPricingConfigDto { IReadOnlyList<CustomerPreferredUnitDto> PreferredUnits; IReadOnlyList<CustomerServiceOptionPriceDto> OptionPrices; }` (read `CustomersView|TariffsView`, write `TariffsManage`).
  DTOs in `Modules/Tarification/Dtos/PricingDtos.cs`.

**Tests (new `Tarification/PricingEngineTests.cs` + `PricingAdminTests.cs`):** quantity brackets (1→€50, 2→€85, 3→€115, 5 with open bracket + per-extra); zone resolution picks zone rule over zone-less and Z3 example totals €145+25+12.50 informational diesel; weight bracket; hourly (3 uur × €75); fixed; customer-specific beats company default; effective-date windows; fallback to rate card when no rules; RequiresManualPrice on unmatched unit; service option customer override; percent option on subtotal; tenant isolation (rules of tenant B invisible); zone/rule/service-option CRUD validation (bracket overlap rejected, PostalCodeFrom ≤ To).

- [ ] TDD → commit `feat(pricing): zones, price rules, service options + explainable pricing engine (additive migration)`

### Task D3: Customer pricing configuration UI

**Files:**
- Create `src/features/tarification/api/pricingApi.ts` — types + calls for zones, rules, service options, customer pricing config, preview.
- Create `src/features/customers/components/CustomerUnitPricingPanel.tsx` — inside customer "Tarieven & toeslagen" tab (C1 stack): preferred units multi-select with ordering; per-unit price rules list for this customer (create/edit dialog: Basis select with dynamic fields — brackets editor rows From/To/Prijs/Extra per eenheid, zone select, effective window, minimum); customer service-option prices table (option, standaardprijs, klantprijs). All writes `tariffs.manage`.
- Create `src/features/tarification/pages/PricingSettingsPage.tsx` (route `/settings/pricing`, nav under settings with `tariffs.manage`): tabs Zones / Bedrijfsregels (CustomerId null) / Diensten & toeslagen (service options master data).
- Routes + navConfig entries.

**Tests (FE):** `customerUnitPricingPanel.test.tsx` (preferred units save, rule dialog per basis renders correct fields), `pricingSettingsPage.test.tsx` (zones CRUD smoke, options list).

- [ ] TDD → commit `feat(pricing): customer unit/pricing configuration UI + pricing settings page`

### Task D4: Order pricing snapshot + override permission (backend)

**Files:**
- Modify: `PermissionCodes.cs` — `OrdersOverridePrice = "orders.override_price"` + catalog; `DefaultRoleUpgrades.cs` v13: management (+planner if template exists) get it; `CurrentVersion = 13`.
- Modify: `Modules/Orders/Entities/TransportOrder.cs` — add `public decimal? CalculatedPrice { get; set; }` `public bool PriceIsManual { get; set; }` `public string? PriceOverrideReason { get; set; }`; new child `TransportOrderPricingLine : AuditableTenantEntity { TransportOrderId, int Sequence, string Label, decimal Amount, string Source, bool Informational }` and `TransportOrderServiceLine : AuditableTenantEntity { TransportOrderId, Guid? ServiceOptionId, string NameSnapshot, SurchargeKind Kind, decimal Value, decimal Amount }` (+configs, DbSets). Migration `OrderPricingSnapshot`.
- Modify: `TransportOrderService` save path — request gains `IReadOnlyList<Guid> ServiceOptionIds`, `bool PriceIsManual`, `decimal? ManualPrice`, `string? PriceOverrideReason`. On save: run `IPricingEngine.CalculateAsync` when customer + any priceable input present; snapshot breakdown lines + service lines (replace children); `CalculatedPrice = result.Total`; if `PriceIsManual` → require `orders.override_price` (via `IPermissionAuthorizationService`) + reason, `AgreedPrice = ManualPrice`; else `AgreedPrice = CalculatedPrice ?? AgreedPrice (legacy manual entry when no engine result)`. Snapshots are never recomputed for historical orders (only on explicit save), satisfying "historical orders do not change".
- Modify: order DTOs to expose breakdown lines, service lines, `calculatedPrice`, `priceIsManual`, `priceOverrideReason`.
- Modify: `InvoiceService.CreateAsync` — per order: base line `UnitPrice = (order.AgreedPrice ?? 0) - Σ(order service line amounts)`; one invoice line per `TransportOrderServiceLine` (Description = NameSnapshot, Quantity 1, UnitPrice = Amount); diesel behaviour unchanged. Orders without service lines behave exactly as today (regression-critical).

**Tests:** extend `Orders/TransportOrderServiceTests.cs`: save computes snapshot + sets AgreedPrice from engine; manual override without permission → forbidden/validation; with permission + reason → AgreedPrice = manual, `PriceIsManual` true; legacy path (no rules, no rate card) keeps manual AgreedPrice untouched; snapshot survives master-data tariff change (edit rule, reload order → lines unchanged). Extend `Invoicing/InvoiceServiceTests.cs`: order with service lines → separate invoice lines and base excludes them; order without → single line as before.

- [ ] TDD → commit `feat(orders): pricing snapshot, service lines and authorized manual override (perm v13)`

### Task D5: Order form pricing integration (frontend; lands inside E2's tab structure)

**Files:**
- Modify: `TransportOrderForm.tsx` (+ new `orderSections.ts`) — see E2 for tab layout. Pricing-specific work:
  - Unit dropdowns (order + cargo lines): customer's preferred units first (from `GET /api/customers/{id}/pricing-config`), separator, "Andere eenheden tonen" toggle revealing all active `allowForOrderEntry` units.
  - "Services & toeslagen" tab: checkbox list of active service options with effective (customer or default) price shown; selected options appear with amounts.
  - "Prijs" tab: live breakdown via `POST /api/pricing/preview` (debounced on relevant inputs: customer, lines, delivery stop postal code, weight, options) rendering `PriceBreakdownLine`s incl. zone name, "Berekend totaal", informational diesel line, and the manual-override block: checkbox "Handmatige prijs" (visible only with `orders.override_price`), amount + verplichte reden. Explanation text for how the amount was calculated comes from the labels/sources.
  - Samenvatting tab shows the final price + service selections.

**Tests (FE new `transportOrderPricing.test.tsx`):** preferred units listed first + toggle reveals rest; preview call renders breakdown lines; override controls hidden without permission; selecting service option adds line.

- [ ] TDD → commit `feat(orders): order entry uses customer units + live pricing breakdown`

---

# PHASE E — TRANSPORT ORDER FORM RESTRUCTURE + ORDER DOCUMENTS (spec 6, 3-adjacent)

### Task E1: Order documents (backend + staged upload)

**Files:**
- Create `Modules/Orders/Entities/TransportOrderDocument.cs`:
```csharp
public enum TransportOrderDocumentType { CustomerDeliveryNote, DeliveryNote, Cmr, Other }
public class TransportOrderDocument : AuditableTenantEntity {
    public Guid TransportOrderId { get; set; }
    public TransportOrderDocumentType DocumentType { get; set; }
    public string? CustomTypeName { get; set; }
    public string Title { get; set; } = "";
    public string? DocumentPath { get; set; }   // storage key
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public DateOnly? IssueDate { get; set; }
    public string? Notes { get; set; }
}
```
- Config + DbSet + migration `OrderDocuments`. Storage category `"order-documents"` via `IFileStorageService`.
- Create `Modules/Orders/Services/TransportOrderDocumentService.cs` + `Modules/Orders/Controllers/TransportOrderDocumentsController.cs` mirroring FleetDocuments exactly: `GET/POST api/transport-orders/{id}/documents`, `PUT/DELETE api/order-documents/{id}`, `POST/GET/DELETE api/order-documents/{id}/document` (10 MB, .pdf/.jpg/.jpeg/.png). Perms: view `orders.view|orders.manage`; mutate `orders.edit|orders.create|orders.manage`.
- FE: `src/features/transport-orders/api/orderDocumentsApi.ts`, `components/OrderDocumentsPanel.tsx` (edit mode, self-saving; list + upload + download + delete), `components/PreparedOrderDocumentsEditor.tsx` + `utils/preparedOrderDocs.ts` (create mode staging, parent-first + retry — same pattern as B2).

**Tests:** backend new `Orders/TransportOrderDocumentTests.cs` (CRUD, tenant isolation, file attach/open/delete path guarded, order-not-found); FE staging test mirroring B2.

- [ ] TDD → commit `feat(orders): order documents with real file storage + staged upload on create`

### Task E2: Transport order form subnavigation

**Files:**
- Modify: `TransportOrderForm.tsx` — wrap existing field groups in `SectionedForm` sections via new `src/features/transport-orders/components/orderSections.ts`:
  1. `algemeen` — klant, klantreferentie, orderdatum, prioriteit, facturerende entiteit, customer-requirement hints, dieseltoeslag-afwijking details.
  2. `route` "Route & stops" — the entire stops section.
  3. `goederen` — goods description, aantal/eenheid/gewicht/volume/paletten/ADR/kraan + goederenlijnen (scanbaar).
  4. `services` "Services & toeslagen" — D5 options block (placeholder text before D5 lands: selected delivery options).
  5. `documenten` — create: `PreparedOrderDocumentsEditor`; edit: `OrderDocumentsPanel` (panel: true).
  6. `prijs` — D5 breakdown + override (until D5: current Afgesproken prijs field moves here).
  7. `samenvatting` — read-only recap of all sections (klant, stops count + first/last city, goods totals, services, price) + submit guidance.
  Field-key map per section for `hasError` badges + `firstSectionWithError` on submit. Keep ALL existing state/props; the refactor is layout-only. Add `<UnsavedChangesGuard when={dirty && !isSubmitting} />` if the form lacks it (tab switches stay internal state → never triggers).
- `NewTransportOrderPage.tsx` gains `preparedOrderDocs` state (E1).

**Tests (FE new `transportOrderSectionedForm.test.tsx`):** all seven tabs render; values survive tab switches; unsaved guard not triggered by tab switch; submit with missing klant routes to `algemeen` with error badge.

- [ ] TDD → commit `feat(orders): transport order form restructured into sectioned subnavigation`

---

# PHASE F — CALENDAR (spec 8 + 9)

### Task F1: Personal calendar notes (backend) + colour flow

**Files:**
- Create `Modules/Portal/Entities/PersonalCalendarNote.cs`:
```csharp
public class PersonalCalendarNote : AuditableTenantEntity {
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = "";        // max 120
    public string? Description { get; set; }       // max 1000
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool AllDay { get; set; } = true;
    public string Colour { get; set; } = "#2563eb"; // validated against PALETTE
}
```
  `public static class CalendarNotePalette { public static readonly IReadOnlyList<string> Colours = ["#2563eb","#16a34a","#ea580c","#9333ea","#0891b2","#db2777","#ca8a04","#64748b"]; }`
- Config (table `personal_calendar_notes`, index (TenantId, EmployeeId, Date)) + DbSet + migration `PersonalCalendarNotes`.
- Modify: `PortalService.cs` — `ListMyCalendarNotesAsync(from,to)`, `CreateMyCalendarNoteAsync(SaveCalendarNoteRequest)`, `UpdateMyCalendarNoteAsync(id, request)`, `DeleteMyCalendarNoteAsync(id)` — all resolve `MyEmployeeIdAsync` and scope strictly to that employee (update/delete of another employee's note → not found). Palette + title validation server-side. `GetMyPlanningAsync` merges note entries into the returned days.
- Modify: `ShiftDtos.cs` — `ScheduleEntryDto` gains `string? Colour = null, Guid? NoteId = null`; `ScheduleEntryState` gains `Note`; FE type union gains `'Note'`, sourceType `'Note'`.
- Modify: `ShiftService.BuildEntries` — load `LeaveType` colours for the absences' `LeaveTypeId`s (single dictionary query) and set `Colour` on absence-derived entries (null-safe fallback to existing CSS class colours). Trip/shift entries keep `Colour = null`.
- Modify: `MeController.cs` — `GET/POST api/me/calendar-notes`, `PUT/DELETE api/me/calendar-notes/{id}` (auth-only, self-scoped like siblings), plus `GET api/me/calendar-notes/palette` (or ship palette as FE constant — decision: FE constant + server validation, no endpoint).

**Tests (backend new `Portal/PersonalCalendarNoteTests.cs`):** CRUD self-scoped; employee B cannot update/delete A's note (not found); invalid colour rejected; notes appear in `GetMyPlanningAsync` with State Note + colour; leave entry carries LeaveType colour; user without employee link → 404 path. Extend `EmployeePlanning/ShiftServiceTests.cs` for the colour join.

- [ ] TDD → commit `feat(portal): personal calendar notes + leave-type colour in schedule feed`

### Task F2: Calendar colours in month/week/list + note UI

**Files:**
- Modify: `ScheduleChip.tsx` — when `entry.colour` set, apply inline `style={{ background: colour + '22', borderColor: colour }}` (keep label/icon: never colour-only); add `compact` prop for month cells; state `note` gets CSS class + 📝 icon and label = note title.
- Modify: `employee-planning.css` — `.schedule-chip-note` default styling + `.schedule-chip.compact` (smaller padding/font, ellipsis).
- Modify: `PortalPlanningPage.tsx` —
  - Month view: replace plain `<span className="portal-month-entry">` with `<ScheduleChip compact entry={...}/>` (first 2 + "+N"); week and list views get colour automatically via chip change.
  - Note CRUD: "Notitie toevoegen" button + clicking an empty day opens `PersonalNoteDialog` (new component `src/features/portal/components/PersonalNoteDialog.tsx`): Titel, Omschrijving, Datum, Hele dag checkbox / Start-Eind tijd, colour swatch picker from the 8-colour palette (radio swatches with labels, not free input). Clicking an own note chip opens edit/delete.
  - API: `src/features/portal/api/calendarNotesApi.ts`.
- Legend (`ScheduleLegend`) gains "Persoonlijke notitie".

**Tests (FE new `portalPlanningCalendar.test.tsx`):** month view renders chips with inline colour for leave (colour from DTO) and note; note dialog creates via API and palette limits choices; edit/delete own note; labels still rendered (accessibility).

- [ ] TDD → commit `feat(portal): coloured month/week calendar + personal note management`

---

# PHASE G — FINAL VERIFICATION (spec 11)

- [ ] `dotnet build` (API) — 0 errors.
- [ ] `dotnet test TransportationService.Api.Tests` — full suite green; record exact count.
- [ ] `npx tsc -b` (Web) — 0 errors.
- [ ] `npx eslint .` per repo config — record result (frontend lint debt is a known pre-existing item; new code must be clean).
- [ ] `npx vitest run` — full suite green; record exact count.
- [ ] `npm run build` — production build success.
- [ ] `git status` clean; list commit hashes + migrations (`IssuedItemCategories`, `UnitTypePricingFields`, `PricingRules`, `OrderPricingSnapshot`, `OrderDocuments`, `PersonalCalendarNotes`); note migrations not yet applied to any shared DB.
- [ ] Update memory files (wave summary).

## Self-review notes

- Spec 1.1–1.6 → A1–A5; spec 2 → B1; spec 3 → B2 (+E1 for orders); spec 4 → B3; spec 5 → C1; spec 6 → E2; spec 7 → D1–D5; spec 8 → F1/F2; spec 9 → F1/F2; spec 10 satisfied by exploration reports; spec 11 → per-task tests + Phase G.
- Diesel double-count guard: engine marks diesel `Informational`, excluded from `Total`/`AgreedPrice`; invoicing keeps sole ownership of diesel lines.
- Regression guards named explicitly: legacy manual AgreedPrice path, invoice single-line path, existing variant-required issuance, existing precedence resolver untouched.
- Order of execution: A → B → C → D1..D4 → E1 → E2 → D5 → F → G (D5 depends on both D4 endpoints and E2 layout).
