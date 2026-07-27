# Corrections Wave 3 Implementation Plan (2026-07-27)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pricing usability (hourly grid, row-level bracket overrides, day/pallet-day quantities, service conditions, navigation), personnel notes + complete history, leave-category management, variant edit-flow fix, and tenant-specific ledger-account mappings with invoice snapshots.

**Architecture:** Extend the existing PricingEngine/rate-table stack, the two-layer audit system (interceptor stamps + `AuditService.RecordAsync` JSON before/after), the `LookupEntity`/master-data conventions and the invoice snapshot pattern. No parallel systems; every new permission goes through `DefaultRoleUpgrades` v16.

**Tech Stack:** .NET 10 + EF Core 10 + Npgsql, React 19 + Vite + vitest, xunit + in-memory SQLite harness, ClosedXML for exports.

## Global Constraints

- All user-facing labels/messages in Dutch, consistent with existing vocabulary (Tarieventabel, Staffelrij, Klantafwijking, Notities, Historiek, Boekhouding).
- Tenant filtering is explicit per query (`TenantId == _tenantContext.TenantId`); no reliance on global filters (soft-delete filter only).
- Money/quantities are `decimal`; new columns get explicit `HasPrecision`.
- Migrations additive only; never edit applied migrations; backfills via guarded SQL inside the migration.
- New permissions: constant + `All` tuple + `DefaultRoleDefinitions` + `DefaultRoleUpgrades` v16 step + `VersionN_...` seeder test + `[RequirePermission]` + frontend `hasPermission`.
- Audit via `IAuditService.RecordAsync(entityType, entityId, action, oldObj, newObj, ct)` with purpose-built anonymous objects (never raw entities, never secrets).
- Validation via `DomainValidationException(field, message)`; camelCase field paths.
- Backend zero new warnings; `dotnet test` green (baseline 1105); frontend `npm run test` (372), `npm run lint`, `npm run build` green.
- Baselines recorded 2026-07-27: backend 1105 passed; frontend 372 passed, build OK.

---

## Repository findings (inventory)

### Pricing
- Entities in `TransportationService.Api/Modules/Tarification/Entities/`. `PriceRule` already has `UnitPrice`, `BaseAmount`, `MinimumAmount`, `MaximumAmount`, `MinimumQuantity` (hourly min billable hours), `QuantityRoundingStep` (hourly round-up step), `Priority`, `ZoneId`, `EffectiveFrom/Until`, brackets.
- `PricingEngine.cs` (`Modules/Tarification/Services/PricingEngine.cs`): hourly rounding **then** minimum (`ComputeRuleAmount` :1061-1107); bracket matching `FindMatchingBracket` :1117; specificity `Score = Tier*4 + (zone?2:0)` then `Priority`; exact ties are blocking configuration errors.
- `PerDay`/`PerPalletDay` are `SurchargeKind`s on `ServiceOption`, computed in `FinalizeAsync` :748-761; quantity must be explicitly entered, otherwise informational "geef het aantal dagen/pallet-dagen op" line.
- Service conditions today: `ServiceOption.AutoApply` + `ServiceOption.OnlyForAdr` + per-customer `CustomerServiceOptionPrice` overrides. No product/category/warehouse conditions (docs/pricing.md §7 notes this explicitly).
- Excel round-trip (`PricingExcelService.cs`) already includes `Min. aantal` and `Afrondingsstap` columns.
- Frontend grid `RuleGridEditor.tsx` (601 l): inline onBlur editing, 18 columns — **missing** MinimumQuantity/QuantityRoundingStep columns. Full-object PUT via `ruleToInput`.
- Order pricing: `TransportOrderService.ApplyPricingAsync` merge-by-`LineKey`, snapshot + `OrderPricingStatus` lifecycle; `Locked`/`Invoiced` protected.

### Personnel / leave
- `Employee.Notes` (single scalar, max 2000) **is persisted** on create (`EmployeeService.cs:178`) and update (:277) and returned in the detail DTO (:482). The detail page never renders it, and audit payloads omit it → the reported "note invisible" bug.
- Employee audit payloads are partial (`{FirstName, LastName, EmploymentStatus, DepartmentId, IsActive}`); most field changes never appear in history. `QualificationService.UpdateAsync` records **no** audit at all.
- History UI = generic `AuditHistoryPanel` on `/api/audit-logs?entityType=Employee&entityId=...`; raw JSON strings, raw user GUIDs, no child entities.
- Leave: `LeaveType` + `LeaveBalanceType` are already tenant-scoped master data (`Modules/Hr/Entities/`), with `LeaveConfigController` CRUD (no DELETE, no reorder), permission `leave_types.manage`, lazy per-tenant seeding (`LeaveDefaults`). Days only (no hours anywhere in Modules/Hr). `EmployeeLeaveBalance` + append-only `LeaveBalanceAdjustment(Days, Reason, Kind)`.
- Frontend absences screens use **hardcoded** `ABSENCE_TYPES` constants instead of the LeaveType master data.

### Variants (issued-item templates, "Bedrijfsmiddelen")
- `IssuedItemVariant`/`IssuedItemVariantValue` under `Modules/Employees/`. Detail endpoint returns variants.
- Bugs found:
  1. `IssuedItemTemplateDetailPage.tsx:234-240` edit payload omits `lowStockThreshold` → `UpdateVariantAsync` (:535) nulls it (silent data wipe).
  2. Detail-page variant modal never loads/edits the threshold.
  3. `UpdateVariantAsync` (:521-525) does delete-all + re-add of variant values; soft delete + **unfiltered** unique index `(TenantId, VariantId, AttributeDefinitionId)` (`IssuedItemConfigurations.cs:95`) → unique violation on PostgreSQL for attribute-backed variants. SQLite tests don't cover that path.
  4. `TemplateFormModal.tsx:280` doesn't render the variants editor when variants were enabled in this same save.

### Invoicing / accounting
- `InvoiceLine` = `InvoiceId, TransportOrderId?, Sequence, Description, Quantity, UnitPrice, VatRatePercent` — **no category concept, no ledger anything** in the entire backend.
- Invoice generation (`InvoiceService.CreateAsync`) structurally knows: base transport line per order, service lines (from `TransportOrderServiceLine`), diesel lines (`DieselSurchargeCalculator`), manual lines.
- No invoice export of any kind exists ("clean extension point" per `Invoice.cs:16`). ClosedXML export precedents: Profitability, KPI, PricingExcel.
- Seller-snapshot pattern at Send (`ApplySnapshots`, frozen after Sent) is the model to copy for ledger snapshots.

### Cross-cutting
- Permissions: `PermissionCodes.cs` constants + `All` catalog; `DefaultRoleUpgrades.CurrentVersion = 15`; seeder tests assert exact version + grants.
- Audit: `AuditLog(EntityType, EntityId, Action, OldValuesJson, NewValuesJson, UserId, Timestamp)`; read via `/api/audit-logs` (permission `audit_logs.view`).
- Master data: `LookupEntity` stack (backend generic service + frontend `lookupRegistry`).
- Tests: 143 backend files (1105 tests) on in-memory SQLite; 79 frontend vitest files (372 tests).

### Architectural risks
1. **Bracket-row overrides vs. bracket replacement:** `ApplyRule` and Excel import fully replace bracket rows, so overrides must key on the *range values* (FromQuantity/ToQuantity + caps), not on bracket IDs.
2. **SQLite tests miss PG-only constraint bugs** (variant unique index) — fix must include the filtered index migration.
3. **History read-time diffs:** old audit entries are partial; the projection must render them gracefully (fallback) and only new entries get full field coverage. Old entries are never rewritten.
4. **No product master for order cargo:** service conditions can only bind to entities that exist (ADR flag, warehouses/locations, inventory categories where linkable). Phase 5 verifies what's linkable before design; explicit order input remains the fallback.
5. **Blocking invoice Send on missing mappings would break existing flows** — validation is warning-based at draft; the hard gate lives on the new accounting export.

---

## Phase 2 — Hourly fields in the rate-table grid

**Files:** `TransportationService.Web/src/features/tarification/components/RuleGridEditor.tsx` (+ css), `__tests__/ruleGridEditor.test.tsx`; verify `pricingApi.ts` `PriceRuleInput` includes `minimumQuantity`/`quantityRoundingStep`.

- [ ] Confirm `SavePriceRuleRequest`/`PriceRuleDto` already expose `minimumQuantity` + `quantityRoundingStep` (backend); add to frontend types + `ruleToInput` if missing.
- [ ] Add grid columns `Min. uren` and `Afronding (u)` rendered **only** when `basis === 'Hourly'` (other rows show `—`), same inline-onBlur pattern; extend header so non-hourly rules aren't cluttered (single combined header `Uren (min / stap)` or two narrow columns shown conditionally — keep table stable: render dash for non-hourly).
- [ ] Inline validation: negative values rejected client-side; API errors land in `rowErrors`.
- [ ] Frontend tests: hourly rule shows the two inputs, non-hourly shows dash; blur saves payload containing both fields; existing tests stay green.
- [ ] Backend: add PricingEngine test for "round 3.60 → 3.75 then min" ordering if not covered (`Hourly_RoundsUpPerStartedInterval_AndAppliesMinimumDuration` exists — verify the exact 2.5h→4h and 3.6h→3.75h examples from the spec, add missing cases).
- [ ] Docs: pricing.md hourly section update. Commit.

## Phase 3 — Row-level bracket overrides

**New entity** `PriceRuleBracketOverride : AuditableTenantEntity` in `Modules/Tarification/Entities/`:
`Guid PriceRuleId` (the shared/base rule), `Guid CustomerId`, `decimal FromQuantity`, `decimal? ToQuantity`, `decimal Price`, `decimal? PricePerExtraUnit`, `decimal? WeightToKg`, `decimal? VolumeToM3`, `decimal? LoadingMetersTo`, `DateOnly? EffectiveFrom`, `DateOnly? EffectiveUntil`, `string? Notes`.
Match = exact row identity (FromQuantity, ToQuantity, caps) on the winning rule. Unique index `(TenantId, PriceRuleId, CustomerId, FromQuantity, ToQuantity coalesced, WeightToKg?, VolumeToM3?, LoadingMetersTo?)` filtered `IsDeleted = false`; overlapping effective windows for the same row = validation error at save + blocking config error at calc time (never silently win).

- [ ] Entity + configuration + DbSet + migration `PriceRuleBracketOverrides`.
- [ ] Engine: in bracket amount computation, when the winning candidate rule is **not** customer-private, look up date-valid overrides for `request.CustomerId` on that rule; if exactly one matches the found bracket row identity → use override price (+PricePerExtraUnit for open-ended); >1 → configuration error "Conflicterende klantafwijkingen…"; source label "Klantafwijking". Load overrides once per calculation (single query).
- [ ] Admin API: `GET/PUT/DELETE api/pricing/rules/{ruleId}/bracket-overrides` (list per rule incl. customer names; save validates row identity exists on the rule, dates, non-negative price; delete = soft). Audit Created/Updated/Deleted. Permissions TariffsView/TariffsManage.
- [ ] UI (RuleGridEditor expanded bracket rows): per bracket row show badge `Standaard`; beneath it one row per override with badge `Klantafwijking` + customer name + price + remove (`Afwijking verwijderen` restores inherited); action `Klantafwijking…` opens dialog (customer SearchableSelect + price + optional dates).
- [ ] UI (CustomerUnitPricingPanel): "Actuele prijzen" section marks overridden bracket rows with `Klantafwijking` badge; link "Beheer klantafwijkingen" to the shared table.
- [ ] Tests (backend): shared 1/2/3/4+ with override on 3 → 50/80/99/125; effective dates; zone rule; conflicting overrides blocked at save and at calc; remove restores; snapshot protection (existing order snapshot untouched); tenant isolation; override ignored when a private customer rule wins outright.
- [ ] Frontend tests: badges render, create/remove override flows. Docs. Commit.

## Phase 4 — Per-day / per-pallet-day order quantities

Backend: `TransportOrderServiceLine` gains `decimal? DayCount`, `decimal? PalletCount` (persist inputs; `Quantity` stays the billable quantity). `SaveOrderServiceSelection`/DTOs extended; recalculation reuses stored values. When Kind=PerDay: quantity := DayCount when provided. PerPalletDay: quantity := PalletCount × DayCount unless the user manually set Quantity (manual wins; flag by comparing provided quantity vs product — store what UI sends; UI computes product but allows editing the result).

- [ ] Migration + DTO/service plumbing (`TransportOrderService` service-selection paths + `RecalculateOrderPricingAsync` rebuild).
- [ ] Order UI: in the order form's services picker and in the Prijs section, PerDay services get input `Aantal dagen`; PerPalletDay gets `Pallets`, `Dagen`, computed read-then-editable `Pallet-dagen` (= product, transparent: "4 pallets × 12 dagen = 48"); unrelated kinds unchanged; locked/invoiced disables inputs.
- [ ] Tests: backend — explicit day quantity produces amount; pallet×days auto product; manual correction persists through recalc; missing values keep informational line; locked order rejects changes. Frontend — fields render only for the right kinds, payload carries values.
- [ ] Docs. Commit.

## Phase 5 — Service conditions

First: targeted verification of what an order/service can actually bind to (cargo→product links, warehouse/location references, customer-owned stock concept). Design (per current knowledge): new table `service_option_conditions` — `ServiceOptionId`, `ConditionKind` enum (`AdrOnly` migrates conceptually but stays as existing bool; kinds: `ProductCategory`, `Product`, `Warehouse`, `CustomerOwnedStock` as available), `ReferenceId`. Evaluation: conditions grouped by kind → OR within kind, AND across kinds; documented. `OnlyForAdr` remains the ADR condition (no duplication). If a kind's referenced data cannot be linked to orders today, that kind is not offered (documented limitation) — no speculative warehouse features.

- [ ] Verify linkable entities; finalize kinds; entity + config + migration.
- [ ] Engine: extend auto-apply eligibility + explicit-selection informational messaging ("alleen van toepassing bij …") using request context; request DTO gains the minimal fields needed (e.g. warehouse id) only if orders carry them.
- [ ] Admin UI in ServiceOptionsEditor: "Voorwaarden" — default "Alle orders", multi-select per available kind with clear Dutch help text.
- [ ] Tests: match/non-match per kind, AND/OR combination, tenant isolation, no-condition = applies normally.
- [ ] Docs (evaluation semantics + examples). Commit.

## Phase 6 — Pricing navigation cleanup

- [ ] Cross-links: shared table header → "Gebruikt door N klanten" links to Klanten tab (exists as banner; make consistent), customer pricing panel → link to source shared table; rule grid → link "Diensten & toeslagen beheren" → /settings/pricing?tab=diensten; empty states + one-line intro per tab.
- [ ] Consistent labels audit (Tarieventabel/Prijsregel/Staffel/Klantafwijking) and helper texts; no new mega-screen.
- [ ] Frontend tests for changed components. Commit.

## Phase 7 — Personnel note visibility

- [ ] `EmployeeDetailPage` profile tab: read-only "Notities" card (note text, `UpdatedAt`, resolved `UpdatedByUserId` name where available) — shown above/beside the form or in view mode; keep single-scalar model (no new notes system).
- [ ] Include `Notes` in create/update audit payloads (as part of Phase 8's full snapshots).
- [ ] Tests: create-with-note → detail returns note (backend exists? add if missing); frontend: note visible after load. Commit (may merge with Phase 8 commit stream).

## Phase 8 — Complete personnel history

Backend:
- [ ] `EmployeeService`: audit full before/after snapshots on update (all meaningful scalar fields + emergency contacts + job functions as name lists; exclude confidential values → mask as "•••" changed-indicator when `NationalRegisterNumber`/`Iban`/`Bic`/`IdentityCardNumber` change), and full snapshot on create.
- [ ] `QualificationService.UpdateAsync`: add missing audit with before/after.
- [ ] Leave entitlement: ensure `SetEntitlementAsync`/`AddAdjustmentAsync` audits carry `{balanceType, year, before, after, difference, unit: "dagen", reason}`.
- [ ] New endpoint `GET /api/employees/{id}/history?page=&pageSize=` (permission: `employees.view` + honours confidential permission for masked fields): aggregates AuditLog rows for the employee **and** its children (qualifications, documents, issued items, absences, leave balances/adjustments, driver profile, emergency contacts) by collecting child ids (IgnoreQueryFilters to include soft-deleted) then querying `(EntityType, EntityId)` pairs; projects each row into `{timestamp, userName (resolved), action, category (Dutch section), changes: [{field (Dutch label), before, after}]}` by diffing Old/New JSON; ID fields resolved to names (department, contract type, leave type, balance type, qualification type); booleans → Ja/Nee; dates `dd/MM/yyyy`; enum values → existing Dutch label maps; unknown/legacy payloads fall back to raw key/value rendering. Field-label dictionary lives server-side next to the service.
- [ ] Frontend: `EmployeeHistoryPanel` (replaces generic panel on the historiek tab): grouped per save (one card per audit row), newest first, header "27/07/2026 14:32 — Gewijzigd door X", rows Veld/Voor/Na, category chip (Profiel/Kwalificaties/Documenten/Afwezigheden/Verlofsaldo/Bedrijfsmiddelen), pagination.
- [ ] Tests: one-field change; multi-field one-save; child entity changes appear; entitlement grant shows before/after/difference/reason; actor+timestamp; no secrets in payloads; tenant isolation; permission gate. Frontend panel tests. Docs. Commit.

## Phase 9 — Leave category management

- [ ] Backend `LeaveConfigService`: add `DELETE api/leave-types/{id}` + `DELETE api/leave-balance-types/{id}` — blocked with exact message "Categorie '<naam>' is al gebruikt en kan niet worden verwijderd. Je kunt de categorie wel deactiveren." when referenced (absences reference LeaveTypeId; balances/adjustments/leave-types reference BalanceTypeId); unused → soft delete. Sort order editable (already a field — expose in dialogs + optional up/down). Audit delete attempts (blocked ones too: action `DeleteBlocked`).
- [ ] Permission: reuse existing dedicated `leave_types.manage` (already exists — documented decision; no new permission needed).
- [ ] Inactive semantics verified: excluded from new-registration selects, still rendered on historical records.
- [ ] Frontend `LeaveSettingsPage`: delete buttons w/ ConfirmDialog + blocked-message toast; sort controls; description field.
- [ ] Frontend absences: replace hardcoded `ABSENCE_TYPES` selects with active LeaveTypes from API (create/edit flows); historical display uses stored type/leave-type name.
- [ ] Tests: delete unused OK, delete used blocked (exact message), deactivate hides from selects, still readable historically, permission, tenant isolation, audit. Commit.

## Phase 10 — Variant edit-flow fix

- [ ] Migration: recreate unique index on `issued_item_variant_values` as filtered (`"IsDeleted" = false`) — matches invoice pattern.
- [ ] `UpdateVariantAsync`: merge values in place (update existing row per AttributeDefinitionId, add new, soft-delete removed) instead of delete-all+re-add; ignore `InitialStock` explicitly documented; only overwrite `LowStockThreshold` when the request provides it? No — keep DTO semantics but fix the callers; the wipe is frontend-caused. Backend keeps explicit null = clear (documented), frontend sends current value.
- [ ] Frontend `IssuedItemTemplateDetailPage`: variant editor state includes `lowStockThreshold` (loaded from variant, editable field, sent in payload).
- [ ] Frontend `TemplateFormModal`: after enabling variants on an existing template, save then show editor (reload template) — or render editor immediately post-save; minimal: auto-reopen/refresh so variants are manageable without closing.
- [ ] Tests (backend): create with values; detail returns variants; update one value (attribute-backed!) preserves others; add; remove; unchanged untouched; duplicate combination rejected; tenant isolation; threshold preserved on update-with-threshold. Frontend: edit modal prefills threshold + payload includes it. Commit.

## Phase 11 — Ledger accounts + sales categories

New module `Modules/Accounting/`:
- [ ] `LedgerAccount : AuditableTenantEntity` — `AccountNumber` (req), `Name` (req), `ExternalCode?`, `Description?`, `IsActive`; unique `(TenantId, AccountNumber)` filtered `IsDeleted = false`; config + DbSet + migration.
- [ ] `SalesCategory : AuditableTenantEntity` — `Code`, `Name`, `SystemRole` enum (`None|Transport|Surcharge|Diesel`) (invoice generation categorises automatically by role), `LedgerAccountId?` (the mapping), `IsActive`, `SortOrder`; unique `(TenantId, Code)` filtered; at most one active category per non-None SystemRole per tenant (validated). Lazy per-tenant seed (LeaveBalanceService pattern): Transport (`SystemRole.Transport`), Supplementen (`Surcharge`), Diesel (`Diesel`), Verkoop europallets, Diverse verkoop binnenland, Diverse verkoop buitenland.
- [ ] Services + controllers: `api/ledger-accounts` CRUD (+options), `api/sales-categories` CRUD + mapping PUT; validation (inactive account not newly assignable; account delete blocked when mapped/snapshotted → deactivate); audit all mutations incl. mapping old→new account.
- [ ] Permissions: new `AccountingSettingsView` (`accounting.view`) + `AccountingSettingsManage` (`accounting.manage`); catalog, `boekhouding` + `management` templates, **v16** upgrade step + seeder test.
- [ ] Config-health endpoint: `GET api/accounting/health` → unmapped active categories.
- [ ] Frontend: new page `/settings/accounting` ("Boekhouding") — ledger accounts table CRUD, mapping table `Verkoopcategorie | Grootboekrekening | Status` with account select, health banner "Geen grootboekrekening ingesteld voor '…'". Nav entry under Beheer/Instellingen. Tests. Commit.

## Phase 12 — Invoice snapshot + accounting export

- [ ] `InvoiceLine` additions: `SalesCategoryId?` + snapshots `SalesCategoryNameSnapshot?`, `LedgerAccountId?`, `LedgerAccountNumberSnapshot?`, `LedgerAccountNameSnapshot?`. Migration.
- [ ] `InvoiceService`: on line creation assign category by structure (base line→Transport role, service lines→Surcharge, diesel→Diesel, manual→null/user-selected); Draft updates may change manual-line category; at **Send** freeze snapshots from the then-current mapping (like `ApplySnapshots`); Sent lines never re-derived.
- [ ] Draft warnings: invoice detail DTO exposes per-line `ledgerWarning` ("Geen grootboekrekening ingesteld voor 'Diesel'. Configureer deze bij Bedrijfsinstellingen → Boekhouding.").
- [ ] Accounting export: `GET api/invoices/export/accounting?from=&to=` (Sent/Paid invoices in window) → XLSX (ClosedXML, Profitability pattern): per line invoice number/date/customer/description/category/ledger number/name/net/VAT; **blocks** (DomainValidationException listing offenders) when any included line lacks a snapshotted ledger account. Permission `accounting.view` (or export-specific check).
- [ ] Frontend: invoice detail — category display + selector for manual lines (Draft), warning badges; InvoicesPage header action "Boekhoudexport" (month/period picker dialog, downloads file).
- [ ] Tests: snapshot at send; mapping change afterwards does not alter historical invoice; export uses snapshot; export blocked on missing; inactive account not assignable; warnings at draft; tenant isolation; permissions. Docs (`docs/accounting.md` or section). Commit.

## Phase 13 — Cross-cutting closure

- [ ] Full backend suite + frontend test/lint/build; zero new warnings check.
- [ ] Documentation sweep: pricing.md (hourly grid, overrides, day quantities, conditions, navigation), new/updated personnel-history + leave + variants + accounting docs with concrete examples.
- [ ] Whole-branch independent review (subagent) → fix Critical/Important findings → re-run tests.
- [ ] Final implementation report (per spec §15). Commit(s).

## Acceptance mapping
Spec §14 items 1–10 → Phases 2–6; 11–16 → 7–8; 17–21 → 9; 22–24 → 10; 25–31 → 11–12; 32–38 → 13.
