# Wave 1 — Dossier Foundation & UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan phase-by-phase. Phases end with green gates (backend tests, frontend tests, typecheck, lint, build).

**Goal:** Make the daily dossier experience visibly simpler: dossier as the central user concept, tenant-configurable operational activities, seconds-fast creation, actionable readiness, safer concurrency — while every existing TransportOrder, snapshot, invoice and audit trail stays valid.

**Architecture:** Evolve the existing `Modules/Dossiers` (`TransportDossier`) into the case container; keep `TransportOrder` as the unchanged operational execution record for transport-shaped work; add `ActivityType` (tenant config) + `DossierActivity` (thin operational layer that *references* orders, never duplicates them). Frontend: new fast-create + redesigned dossier page built from decomposed order-form sections, role-shaped navigation.

**Tech stack:** existing — .NET modular monolith, EF Core (Npgsql prod / SQLite tests), React 19 + Vite + Vitest, hand-rolled CSS.

## Global constraints (from master spec + gap analysis)

- NO parallel module (`DossiersV2` etc.); evolve `Modules/Dossiers` in place.
- Migrations additive only; historical orders/stop snapshots/pricing snapshots/invoices/PODs/audit stay byte-stable.
- Activity types are tenant data, never hardcoded enums in domain logic.
- No dynamic form builder; capability flags only.
- Order APIs and URLs keep working (EDI, portal, planning, packages all call them).
- Internal UI language stays Dutch; business language, not database language.
- Permissions: reuse existing codes where equivalent; new codes via role-template **v26**.
- Wave 1 does NOT include: sales codes (W2), document strategy (W9), storage clock (W5), tour proposals (W7), responsibility/charges (W6).

---

## 1. Existing TransportDossier — inventory and verdict

Verified implementation (`Modules/Dossiers/`):

| Aspect | Current state |
|---|---|
| Entity | `Entities/TransportDossier.cs:17` — `DossierNumber`, `Title`, `Description`, `CustomerId?`, `ResponsibleUserId?`, `Status` (`DossierStatus { Open, Closed }`), `ClosedAt`, `Notes`; base `AuditableTenantEntity` (tenant, audit stamps, soft delete) |
| Links | `DossierOrder` (`:33`) many-to-many to `TransportOrder`, filtered unique active pair `UX_dossier_orders_active_link`; `DossierRelation` (`:53`) directed dossier↔dossier (`FollowUp/Return/Claim/Replacement/Duplicate/Other`, self-link DB-blocked); `Incident.DossierId` (Incidents module) |
| Numbering | `DossierService.cs:502` — `TenantNumbering.SaveWithClaimedNumberAsync` from `TenantSettings.DossierNumberPrefix` (`DOS-`) / `DossierNumberNextValue` |
| Financials | `DossierFinancialSummaryDto` (`Dtos/DossierDtos.cs:46`): `AgreedOrderTotal`, `InvoicedTotal`, `EstimatedIncidentCost`, `ActualIncidentCost` — aggregated over linked orders/incidents |
| API | `Controllers/DossiersController.cs` → `/api/dossiers` CRUD + order-link/unlink + relations + close/reopen; guard `RequireOpen` (`DossierService.cs:494`) |
| Permissions | `dossiers.view`, `dossiers.manage` (already in all relevant role templates: planner, dispatcher, management, boekhouding) |
| Audit | `IAuditService.RecordAsync(EntityType: "Dossier", …)` on create/update/link/close |
| Frontend | `/dossiers` (`DossiersPage.tsx`), `/dossiers/:id` (`DossierDetailPage.tsx`) — list + detail with linked orders, incidents, relations, financial summary |
| Tenant behavior | Manual `TenantScoped()` per query + global tenant query filter + `TenantReferenceGuard` on inbound FKs |

**Why it is the right seed:** correct base class, correct numbering pattern, correct audit/tenant plumbing, an existing many-to-many to orders, existing permissions in the right role templates, and an existing financial rollup. Nothing about it contradicts the container model — it is only *incomplete*.

**Extended (Wave 1):** `CustomerReference`, `DossierDate`, `LegalEntityId`, `Version` (concurrency), `OriginTransportOrderId` (backfill idempotency marker), activities collection; `CustomerId` becomes required for *new* dossiers (stays nullable in DB for legacy rows).
**Unchanged:** numbering, `DossierOrder`, `DossierRelation`, incident links, financial summary, close/reopen, soft delete, permissions codes.
**Legacy naming kept internally:** entity stays `TransportDossier`, table `transport_dossiers`, EntityType `"Dossier"` — only user-facing copy changes.
**Must NOT be duplicated:** stops, cargo, pricing, documents, status history — those live on `TransportOrder` and are *referenced*, never copied.

## 2. Target relationship: Dossier / TransportOrder / Activity

Concrete answers (grounded in the verified model):

- **One dossier, many TransportOrders?** Yes. Each transport-shaped activity holds `LinkedTransportOrderId`; a redelivery is a second activity → second order in the same dossier. `DossierOrder` rows are auto-maintained for every activity-linked order so the existing financial rollup and list UIs keep working unchanged (single writer: `DossierActivityService`).
- **When is TransportOrder still the correct operational object?** Whenever work has stops/cargo/planning/scanning/POD/pricing — i.e. every activity whose type has `HasStops = true`. The order keeps its full lifecycle, EDI/portal intake, package generation, pricing snapshots. Wave 1 changes nothing inside `TransportOrderService` except the concurrency token and the dossier hook on create.
- **Storage-only dossier without TransportOrder?** Yes — dossier + `Storage` activity, zero orders. Operationally valid; readiness reports "geen verkooplijnen" as information. (Billing storage without an order arrives with Wave 2 sales lines + Wave 5 storage clock; Wave 1 does not fake an order.)
- **Crane work on site without a transport order?** Yes — same mechanism (`HasStops = false` type). Duration capture on the activity (`DurationHours`) feeds later KPI waves.
- **Distribution: one order or many?** Wave 1: one TransportOrder with 1 loading + N unloading stops (current model already supports this). Splitting per-delivery lands in Wave 7 (planning) if tour building requires it; the activity layer is deliberately agnostic (`LinkedTransportOrderId` stays correct either way).
- **Plateau accompanying crane transport?** Two activities; the plateau activity sets `LinkedActivityId` → crane activity (operational accompaniment) and no own order. KPI counts both independently (spec Scenario 3).
- **Existing order APIs preserved?** 100%: no route, DTO, or status change. One additive behavior: `TransportOrderService.CreateAsync` ensures a containing dossier (below).
- **Existing URLs preserved?** `/transport-orders`, `/transport-orders/:id` stay routable. `/transport-orders/:id` gains a header breadcrumb "Dossier DOS-xxxx" once the order is dossier-linked. `/dossiers/:id` becomes the primary work surface; nav demotes "Transportopdrachten" (see §14).
- **Historical orders through the new UX?** Via backfill (§3) every historical order is reachable from the dossier list; its dossier page shows the transport activity card + read-only order sections; deep order edits continue on the existing order page.
- **How does DossierActivity avoid becoming a second order model?** It stores *no* operational payload for transport work — only the reference. Its own fields are the minimal set every activity kind shares (§4). Rule of thumb enforced in review: if a field exists on `TransportOrder`, it may not be added to `DossierActivity`.

**Auto-wrap on order create:** `TransportOrderService.CreateAsync` gains an optional `DossierId` input; when absent (EDI, portal, legacy API clients, quick order create) it creates a wrapper dossier (customer, order date, customer reference, entity copied; one activity of the tenant's default transport type) in the same transaction. Portal/EDI flows therefore land in the dossier world with zero caller changes.

## 3. Migration & backfill strategy (all additive)

**Schema migrations (3):**
1. `ActivityTypes` — new table `activity_types` (§5) + per-tenant seed via existing seeder pattern (add-if-missing on `Code`, like `AccountingService.EnsureSeededAsync`).
2. `DossierActivities` — new table `dossier_activities` (§4) + new columns on `transport_dossiers`: `CustomerReference (text?)`, `DossierDate (date?)`, `LegalEntityId (uuid? FK legal_entities Restrict)`, `Version (uuid, default gen)`, `OriginTransportOrderId (uuid?, filtered unique index where not null)`; new column on `transport_orders`: `Version (uuid)`.
3. `DossierBackfill` — data migration, pure SQL, single transaction per tenant batch:
   - Scope: every non-deleted `transport_orders` row with **no** active `dossier_orders` link and **no** wrapper (`transport_dossiers.OriginTransportOrderId = order.Id`). Both exclusions make re-runs idempotent.
   - Creates per order: wrapper dossier (`DossierNumber` = prefix + running counter continued from `TenantSettings.DossierNumberNextValue`, which is bumped in the same statement; `Title` = `"{OrderNumber} — {Customer.Name}"`; `CustomerId`, `CustomerReference`, `LegalEntityId`, `DossierDate = OrderDate` copied; `Status` = Open when order status ∈ {Draft, Submitted, Confirmed, Planned, InProgress} else Closed with `ClosedAt = UpdatedAt`; `CreatedAt/UpdatedAt` = the order's own stamps; `CreatedByUserId` = order's `CreatedByUserId` (null-safe — stays null for system/EDI-created history); `OriginTransportOrderId` = order id), one `dossier_orders` link, one `dossier_activities` row of the tenant's seeded `TRANSPORT` type with `LinkedTransportOrderId`.
   - Orders already in a user-created dossier: untouched (existing link preserved; no activity fabricated — the dossier page renders directly-linked orders as its compat section).
   - **No per-row AuditLog** (thousands of noise rows); the migration is the record, and the dossier history panel labels these "Aangemaakt bij migratie" from `OriginTransportOrderId != null`.
   - Rollback: `Down` deletes `dossier_activities`, wrapper `transport_dossiers` (`OriginTransportOrderId IS NOT NULL`) and their `dossier_orders` rows, restores nothing else — safe because nothing existing was modified.
   - Retry safety: the filtered-unique `OriginTransportOrderId` index makes double-insert impossible even under a partially-applied crash.
- **Nothing rewritten:** orders, stops, snapshots, invoices, documents, PODs, audit rows are not touched by any statement.
- **Pre-flight (blocker check):** memory records earlier waves with never-applied migrations. Before writing migration 1, run `dotnet ef migrations list` against the target DB and reconcile; if drift exists that cannot be reconciled additively, STOP and report (genuine blocker per instructions).

## 4. DossierActivity domain model

```csharp
// Modules/Dossiers/Entities/DossierActivity.cs
public class DossierActivity : AuditableTenantEntity
{
    public Guid DossierId { get; set; }                 // FK transport_dossiers, Cascade
    public Guid ActivityTypeId { get; set; }            // FK activity_types, Restrict
    public int Sequence { get; set; }                   // 1..n within dossier, rebuilt on reorder
    public string? Label { get; set; }                  // optional free label ("Kraan Nexans site B"), max 200
    public Guid? LinkedTransportOrderId { get; set; }   // FK transport_orders, SetNull — the execution record for HasStops types
    public Guid? LinkedActivityId { get; set; }         // FK dossier_activities, SetNull — operational accompaniment (plateau→crane); same-dossier enforced in service
    public DateOnly? PlannedDate { get; set; }          // standalone activities only; HasStops types read dates from the order
    public decimal? DurationHours { get; set; }         // only when type.AllowsDuration; validated >= 0
    public string? Notes { get; set; }                  // max 2000
}
```
Config: table `dossier_activities`; indexes `(TenantId, DossierId)`, `(TenantId, ActivityTypeId)`, `(TenantId, LinkedTransportOrderId)` filtered not-null; global `!IsDeleted` filter like siblings.

**Deliberate exclusions (with reason):**
- **No `Status`.** Transport activities derive status live from the linked order (`TransportOrderStatus` → activity card badge); standalone activities in Wave 1 are descriptive (their execution models arrive in W5/W6/W7). Adding a parallel status now would create the two-status drift the spec forbids. Revisit only when a standalone activity gains its own execution flow.
- **No capability copies** (`PlanningRelevant` etc.): read from `ActivityType` at render time. Snapshot semantics are not needed — reclassifying a type intentionally reclassifies history for KPI purposes; if a later wave needs frozen KPI categories, the snapshot belongs in the KPI read model, not here.
- **No goods/stop/pricing columns** — referenced via the order. **No JSON payload column** — capability flags + the two optional scalars (`PlannedDate`, `DurationHours`) cover every Wave 1 representable case.

**Representability check (spec examples):** A Distribution → activity + order (N unloading stops). B Direct transport → activity + order. C Crane transport → activity + order (`CraneRequired` on order as today). D Crane work on site → activity, no order, `DurationHours`. E Plateau accompanying crane → activity, `LinkedActivityId` → C's activity. F Storage → activity, no order (W5 adds movements). G Redelivery → new activity + new order, plus `DossierRelation(FollowUp)` when it lives in a separate dossier.

## 5. ActivityType configuration

```csharp
// Modules/Dossiers/Entities/ActivityType.cs
public class ActivityType : AuditableTenantEntity
{
    public string Code { get; set; }          // unique per tenant (max 50), e.g. "KRAANWERK"
    public string Name { get; set; }          // Dutch display name, max 100
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Icon { get; set; }         // key from a curated frontend icon map (~12 lucide names); unknown → default icon
    public string? KpiCategory { get; set; }  // free grouping label for later KPI waves, max 50
    public bool HasStops { get; set; }        // true → activity is executed by a TransportOrder
    public bool SupportsGoods { get; set; }   // UI: show goods section on the dossier when any activity supports goods
    public bool PlanningRelevant { get; set; }
    public bool WarehouseRelevant { get; set; }
    public bool AllowsDuration { get; set; }  // UI: show DurationHours on the activity
    public bool IsQuickStart { get; set; }    // appears as a template tile on the New Dossier screen
    public int QuickStartOrder { get; set; }
    public bool IsSystemDefaultTransport { get; set; } // exactly one active per tenant; used by auto-wrap (§2)
}
```
Filtered unique indexes: `(TenantId, Code)` where not deleted; `(TenantId)` where `IsSystemDefaultTransport && IsActive && !IsDeleted` (mirrors `LegalEntity.IsDefault` pattern).

**Field classification (per instruction §5):**
- *Required in Wave 1:* Code, Name, IsActive, SortOrder, Icon, HasStops, SupportsGoods, PlanningRelevant, WarehouseRelevant, AllowsDuration, IsQuickStart, QuickStartOrder, IsSystemDefaultTransport, KpiCategory (cheap now, needed by KPI waves; storing it later would force reclassification of history).
- *Useful later (deferred, with target wave):* AllowsDistance (W3 pricing inputs), AllowsVehicleRequirement / AllowsEquipmentRequirement (W7 planning constraints), DefaultDocumentStrategy (W9), DefaultSalesCode (W2), readiness-requirement config (post-W1 hardening — Wave 1 readiness rules are code-level, §9).
- *Explicitly not built:* per-type custom fields/forms (spec Part G forbids a form-scripting platform).

## 6. Reference tenant defaults & multi-tenant story

Seeder (`Modules/Dossiers/Services/ActivityTypeSeeder.cs`, invoked lazily like `AccountingService.EnsureSeededAsync` — first read per tenant seeds): add-if-missing on Code, never resurrect deleted, never overwrite tenant edits.

| Code | Name | HasStops | Goods | Planning | Warehouse | Duration | QuickStart | Notes |
|---|---|---|---|---|---|---|---|---|
| DISTRIBUTIE | Distributie | ✓ | ✓ | ✓ | ✓ | — | ✓ (1) | |
| DIRECT_TRANSPORT | Direct transport | ✓ | ✓ | ✓ | — | — | ✓ (2) | `IsSystemDefaultTransport` |
| KRAANTRANSPORT | Kraantransport | ✓ | ✓ | ✓ | — | ✓ | ✓ (3) | |
| KRAANWERK | Kraanwerk ter plaatse | — | — | ✓ | — | ✓ | — | |
| PLATEAU | Plateau | — | — | ✓ | — | ✓ | — | |
| OPSLAG | Opslag | — | ✓ | — | ✓ | — | ✓ (4) | |
| EXPRESS | Express | ✓ | ✓ | ✓ | — | — | — | |
| HERLEVERING | Herlevering | ✓ | ✓ | ✓ | ✓ | — | — | |
| POSITIONERING | Positionering / lege rit | ✓ | — | ✓ | — | — | — | |
| OVERIG | Overig | — | — | — | — | ✓ | — | |

Another tenant (container/reefer/intermodal/waste/groupage) deactivates these rows and creates their own through the same settings UI — zero source changes, because **no domain logic ever matches on Code** except the seeder itself and `IsSystemDefaultTransport` resolution. A guard test greps the backend for hardcoded activity codes outside the seeder.

## 7. Templates / quick start

**Decision: no separate template entity in Wave 1.** A "template" is exactly an `ActivityType` with `IsQuickStart = true` plus the built-in "Leeg dossier" tile. That is precisely the spec's definition (template = default initial activity + defaults), avoids a parallel concept, and is tenant-managed through the same activity-type page from day one. If later waves need multi-activity templates or document-strategy presets, a `DossierTemplate` entity can compose activity types then (additive).

Behavior: choosing a tile on the create screen sets `activityTypeId` in the create request; "Leeg dossier" sends null. After creation the user adds more activities freely (`[+ Activiteit]`); no second dossier ever needed.

## 8. Minimal dossier creation (non-negotiable UX)

**Backend** — extend `POST /api/dossiers`:
```
CreateDossierRequest {
  Guid CustomerId;              // REQUIRED — only required field
  DateOnly? DossierDate;        // default: today (tenant clock)
  string? CustomerReference;    // max 100
  Guid? ActivityTypeId;         // null = empty dossier
  string? Title;                // default: "{Customer.Name} — {DossierDate:dd-MM-yyyy}"
}
→ 201 DossierDetailDto (incl. DossierNumber, Version, readiness[])
```
Validation (exhaustive — nothing else blocks):
1. Customer exists in tenant (`TenantReferenceGuard`) → 400 "De gekoppelde klant bestaat niet."
2. Customer `IsBlocked`/`!IsActive` → refuse new dossier (same rule as order intake): "Deze klant is geblokkeerd/inactief; er kunnen geen nieuwe dossiers aangemaakt worden."
3. `ActivityTypeId` (when given): exists, tenant-owned, `IsActive` → 400 otherwise.
4. `CustomerReference` ≤ 100 chars.
Entity inheritance: `Customer.DefaultLegalEntityId` → tenant default (`LegalEntities.IsDefault`) → null (legacy tenants without entities). Silent, audited in the create audit record. NOT required, NOT asked.
Explicitly NOT validated at create: goods, quantities, route, addresses, worksite, price, vehicle, driver, dimensions, weight, contact, delivery time, document type — none of these appear in the request DTO at all, which is the strongest guarantee.
Audit: existing `"Dossier"/Created` record, now including inherited entity + template code.

**Frontend** — new `features/dossiers/pages/NewDossierPage.tsx` (route `/dossiers/new`):
```
NIEUW DOSSIER
Klant *            [SearchableSelect — autofocus]
Klantreferentie    [input, optional]
Datum              [date, default vandaag]
Sjabloon           [tile grid: QuickStart types + "Leeg dossier"; single-select, default "Leeg dossier"]
                   [ Dossier aanmaken ]   ← the ONE primary action
```
Submit → navigate to `/dossiers/:id`. Enter in the customer field submits when valid. Total interaction for Scenario A (phone crane job): pick customer, click Kraanwerk, click create — under 10 seconds.

**Edge cases (explicit):**
- Customer has no default entity → tenant default inherited; dossier header shows the entity chip; no prompt.
- Customer allows multiple entities → Wave 1 has no allowed-entities list yet (Wave 2); the default is inherited and an authorized user changes it on the dossier header (gated `dossiers.manage`; audited old→new). Never a create-time question.
- "Leeg dossier" → dossier with zero activities; page shows the Activities section with only `[+ Activiteit]` and readiness info "Nog geen activiteit".
- Exact location unknown → simply not asked at create; the transport activity's route section shows "Nog te bepalen" until filled (readiness Warning, §9).

## 9. Readiness model (additive projections — NOT new status enum values)

New `Modules/Dossiers/Services/DossierReadinessService.cs`, pure computation on read (no persistence, no new `TransportOrderStatus` members):

```csharp
public record ReadinessIssue(
    string Code,          // stable, e.g. "route.unloading_missing"
    string Severity,      // "Info" | "Warning" | "Blocking"
    string Message,       // Dutch, user-facing, actionable
    string Section,       // dossier page anchor: "route" | "goederen" | "activiteiten" | "prijs" | "algemeen"
    string? Field,        // optional field key within the section
    string Stage);        // "Planning" | "Warehouse" | "Execution" | "Commercial" | "Invoice"
```

Conceptual stages established now (spec Part C), computed in Wave 1 vs deferred:
- **Draft**: always valid — creation never blocks (no rule emits at create beyond §8's four).
- **Planning readiness (Wave 1 rules):** per HasStops activity/order — `route.loading_missing` / `route.unloading_missing` (Warning: "Loslocatie is nog onbekend"), `route.date_missing` (Warning), `activity.none` (Info: "Nog geen activiteit"). Blocking only where the *existing* order Confirm gate already blocks (≥1 laad + ≥1 losstop) — surfaced as `order.confirm.stops` with the same text the gate uses, so the user sees it *before* pressing Confirm.
- **Commercial completeness (Wave 1 rules):** reuse the existing coverage snapshot — any coverage entry != "Full" → `pricing.incomplete` (Warning, message names the goods line); no orders and no coverage → `pricing.none` (Info).
- **Warehouse / Execution / Operational / Invoice readiness:** stages exist in the model (the `Stage` vocabulary) but no Wave 1 rules; W4/W6/W10 add producers without schema change.

Delivery: `GET /api/dossiers/{id}` embeds `readiness: ReadinessIssue[]`. Frontend renders the **Attention panel** (§11): one row per issue, `[Ga naar …]` button scrolling/opening the named section. The 13 blind order-form errors are replaced in the dossier flow by this mechanism plus per-field errors (§12); the legacy order form additionally adopts `ValidationSummary` + `firstSectionWithError` (already proven in `CustomerForm`).

## 10. Concurrency / versioning

Reuse the proven Trip pattern (`docs/operations-architecture.md:29` — explicit `Guid Version`, works on Npgsql + SQLite):

- **Entities:** `TransportDossier.Version` and `TransportOrder.Version` (Guid, initialized on create, bumped on every mutation by the service — not an EF concurrency token, exactly like `Trip`).
- **DTOs:** `DossierDetailDto.Version`, `TransportOrderDetailDto.Version`; every mutating dossier request (`UpdateDossierRequest`, activity add/update/remove/reorder, entity change) carries `Guid? Version`; `UpdateTransportOrderRequest` gains `Guid? Version`.
- **Server behavior:** `Version` present and ≠ current → HTTP 409 with the **current** detail DTO in the body (Trip's `Stale(current)` shape) so the client can rebase. `Version == null` → check skipped (legacy/EDI/portal clients keep working; they are single-writer flows).
- **Frontend:** dossier page stores the version, sends it on every save; on 409 shows a conflict banner: *"Dit dossier is intussen gewijzigd door {name?}. Uw wijzigingen zijn niet opgeslagen."* with `[Herladen]` (discard + refresh) and, for single-section edits where the conflicting fields don't overlap, `[Opnieuw proberen]` (rebase: reapply the section's local values onto the fresh DTO and resubmit — same as the planning board's retryFactory). No silent overwrite, ever.
- **Order form:** wholesale stop rebuild stays (changing it is out of scope), but the version gate now makes the last-write-wins race a visible 409 instead of silent loss.
- **Tests:** parallel-update 409 test per entity (mirror `TripServiceTests` concurrency cases), null-version compat test, rebase round-trip frontend test.

## 11. Dossier detail UX — textual wireframe (target implementation)

```
──────────────────────────────────────────────────────────────
Dossier DOS-2026-00318                        [Open]
Nexans NV · ref. ABC-458 · 11-08-2026 · Entiteit: Transp. BV ▾(perm)
Operationeel: In uitvoering     Prijs: ⚠ Onvolledig

[ + Activiteit ]                      [ Meer ▾ ]  ← Meer: sluiten,
                                         relaties, verwijderen, historiek
──────────────────────────────────────────────────────────────
AANDACHT                                  (hidden when empty)
⚠ Loslocatie is nog onbekend              [ Ga naar route ]
⚠ Wachttijd 1,5 u heeft geen prijs        [ Ga naar prijs ]
──────────────────────────────────────────────────────────────
ACTIVITEITEN
┌───────────────────────────────────────────────┐
│ 🚚 Direct transport      Bevestigd            │
│ Antwerpen → Luik · 2 europallets              │
│ [ Openen ]                                    │
├───────────────────────────────────────────────┤
│ 🏗 Kraanwerk ter plaatse   2,5 u              │
│ Gekoppeld: —                                  │
│ [ Openen ]                                    │
└───────────────────────────────────────────────┘
[ + Activiteit toevoegen ]
──────────────────────────────────────────────────────────────
ROUTE                    (only if any HasStops activity)
Laden                          Lossen
Nexans site Antwerpen          Nog te bepalen
12-08 · 07:00–08:00            12-08 · vóór 12:00
[ Route bewerken ]                     ← opens section drawer
──────────────────────────────────────────────────────────────
GOEDEREN                 (only if any SupportsGoods activity)
2 × Europallet
1 × Colli — Compressor
[ + Goederen ]  [ Meer details ]
──────────────────────────────────────────────────────────────
VERKOOP & PRIJS
€ 428,50        ⚠ 1 onderdeel vraagt aandacht
Transport               € 300,00
Kraanwerk               € 128,50
[ Prijsdetails ]  [ + Verkooplijn ]    ← details = existing
                                          breakdown, one click deeper
──────────────────────────────────────────────────────────────
DOCUMENTEN               (collapsed by default)
NOTITIES & HISTORIEK     (last note visible · [ + Notitie ] [ Historiek ])
──────────────────────────────────────────────────────────────
```

- **Always visible:** header (number, customer, reference, date, entity, the two status chips), Activities, Attention (when non-empty), Verkoop & prijs summary.
- **Contextual:** Route (any HasStops activity), Goederen (any SupportsGoods), portal-review panel (Submitted orders).
- **Collapsed:** Documenten, Historiek; advanced pricing/stop timing lives inside drawers behind "Meer details"/"Geavanceerd".
- **Editing model (§17 decision):** read page + **section drawers** with explicit Opslaan/Annuleren per section (right-side drawer ≥900px, full-screen sheet below). One coherent layout — no page swap. No autosave (the codebase has no safe autosave infra; self-saving panels exist but always behind an explicit button).
- **Primary actions:** exactly one per screen region — header `[+ Activiteit]`, per-issue `[Ga naar …]`, per-section one edit button. Status corrections, delete, relations live under `Meer ▾`.
- **Mobile:** single column, drawers become sheets, controls ≥44px (Wave 0 baseline), sections stack in the same order, sticky header with the two status chips.
- **Technical states hidden:** pricing lifecycle stays behind `Prijsdetails` (labels already masked); `Herberekenen`, line-kind badges, correct-status move under `Meer ▾`/details, permission-gated as today.

## 12. Field-reduction execution (from the completed 89-field audit)

Full audit table lives in the gap analysis addendum (89 internal fields vs 22 portal fields, classes A×12 / B×24 / C×10 / D×11 / E×13 / F×15). Wave 1 applies it as follows — **fields leaving the default workflow:**

- **Removed from default UI (derived/E):** header `quantity`, `quantityUnitCode`, `weightKg`, `volumeM3`, `palletCount` (already derived via `DeriveSummaryFromCargo`; shown read-only in the Goederen summary), stop-row `stopType` select (set by the add button), `earliestAllowed`/`latestAllowed` (expressed via Tijdseis; kept under Geavanceerd for round-trip), `requestedFrom/To` (read-only "Gevraagd door klant" display on portal orders; hidden otherwise), cargo `volumeM3` (computed), `weightPerUnitKg` (prefilled from unit master).
- **Moved behind Geavanceerd (C):** barcode, per-line pallets/reference/notes/stackable/manual-volume, appointment reference, per-line stop pinning (auto-shown only with >1 stop of that side — B), document sub-fields.
- **Prefilled from masterdata (D):** `appointmentRequired`, `includedTimeMinutesOverride` placeholder, access/loading/unloading instructions (location snapshot already carries them — the drawer shows the snapshot text with an "afwijken" affordance instead of four empty inputs), `craneRequired` (from location), dimensions (unit master, existing behavior), `legalEntityId` (customer default).
- **Hidden from dispatch (F):** diesel-override trio, `confirmedFrom/To` (stays on the existing StopExecutionPlan dialog), `refreshSnapshot` (kept as the existing confirm-dialog affordance), included-time/extra-time override quintet (behind "Contractafwijkingen", permission-gated), manual-price pair (behind Prijsdetails, `orders.override_price` as today).
- **Default route drawer per stop shows exactly:** Locatie (or vrij adres), Datum, Van, Tot, Tijdseis (+its one or two times when set), Referentie, Instructies — 7 visible controls (was ~27).
- **Component decomposition (hard requirement — no 2,616-line successor):** split `TransportOrderForm.tsx` into `features/transport-orders/components/sections/` — `RouteSection.tsx` (~450 lines incl. stop row), `GoodsSection.tsx` (~350), `ServicesSection.tsx` (~250), `PriceSection.tsx` (~300), `GeneralSection.tsx` (~150), shared `orderFormState.ts` (reducer + validation returning `{field, section, message}[]` consumed by `ValidationSummary`). The legacy form page composes all sections (unchanged behavior); the dossier drawers compose them one at a time. Budget: no new file >600 lines (lint-guarded by review, not tooling).
- **Bug noted during audit (fix in Wave 1, it's in touched code):** cargo-line volume labeled "per stuk" but summed without ×quantity in `cargoSummary` and `DeriveSummaryFromCargo` (`TransportOrderService.cs:1192`) — fix aggregation to `volume × expectedQuantity` with regression test, and align the client mirror.

## 13. Portal form as evidence

The portal creates valid orders with 18 fields (backend-verified same use case). Proven simplifications adopted: location-or-city as the only address requirement; requested window as plain datetime pair; cargo as description+quantity+type+weight+ADR; no services/pricing/documents at intake. Genuinely backoffice-only (kept, but placed per §12): customer picker (portal infers from session), entity chip, Tijdseis (commercial time promises drive surcharges), services selection, one-off pricing, manual price, prepared documents. Addable after creation (and therefore absent from the create screen): everything except customer/date/reference. Not cloned blindly: portal's silent cargo-row dropping and missing window-order validation are *not* imported; the internal flow keeps its validations but targets them at fields.

## 14. Navigation redesign (exact mapping from the 70-leaf inventory)

Target: 6 groups, ≤22 leaves for the largest role, configuration under Parameters. Full mapping (KEEP = same path):

| Current entry | Target group | Action | Visible to (unchanged permission unless noted) |
|---|---|---|---|
| /dashboard | **Vandaag** | KEEP (role-scoped tiles §16) | dashboard.view |
| /transport-orders | **Dossiers** | MOVE under Dossiers as secondary "Opdrachten (klassiek)" | orders.view |
| /dossiers | **Dossiers** (top item, new landing for dispatch) | KEEP; RootRedirect → /dossiers when dossiers.view | dossiers.view |
| /planning, /planning-center | **Planning** | MERGE label-wise: "Planbord" primary, "Ritlijst" secondary | planning.view |
| /operations | **Planning** → "Live opvolging" | MOVE | operations.view |
| /employee-planning | **Personeel** (unchanged) | KEEP | employee_planning.* |
| /dock-planning | **Magazijn** | KEEP | warehouse.* |
| /my-trips, /driver | driver shell only | HIDE from internal sidebar (drivers get the driver shell; dispatcher reaches via trip pages) | driver_workflow.view |
| /warehouse, /warehouses | **Magazijn** | RENAME: "Laden & scannen" / "Magazijnen (beheer)" | warehouse.view / manage |
| /incidents, /exceptions | **Vandaag** → "Problemen" (one entry each, adjacent) | MOVE | incidents.view / exceptions.view |
| /customers | **Klanten** | KEEP | customers.view |
| /invoices, /peppol | **Facturatie** | MOVE (new group) | invoices.view / peppol.view |
| /cost-rates, /pricing/tables, /settings/pricing | **Parameters** → Prijzen subgroup | MOVE | tariffs.*, trip_costs.* |
| /employees, /tasks, /absences, /qualifications, /inventory | **Personeel** | KEEP | unchanged |
| /fleet, /vehicles, /trailers, /tank-cards, /maintenance-policies | **Vloot** | KEEP | unchanged |
| /locations | **Klanten** → "Locaties" | MOVE (they are customer/operational masterdata) | locations.view |
| /inbox, /notifications | header bell + **Vandaag** | MOVE out of a dedicated group (bell exists) | all |
| /kpi, /profitability, /reports | **Rapportage** | MERGE into one group | kpi/profitability/reports.view |
| /edi, /integrations, /settings/notifications, /settings/escalations | **Parameters** → Koppelingen & meldingen | MOVE | unchanged |
| /users, /roles, /job-function-mappings, /settings, /settings/accounting, /settings/legal-entities, portal-admin pages | **Parameters** → Beheer | MOVE | unchanged |
| /settings/leave, /settings/hr-reminders, issued-item + task templates | **Parameters** → Personeel | MOVE | unchanged |
| /master-data/* (13 lookups) | **Parameters** → Stamgegevens | KEEP (one collapsed subgroup) | unchanged |
| NEW /settings/activity-types | **Parameters** → Stamgegevens | ADD | activity_types.view/manage (v26) |
| /messaging (orphan) | — | HIDE (route stays; nav never linked it) | — |

Resulting sidebar for a **dispatcher**: Vandaag (Dashboard, Problemen×2) · Dossiers (Dossiers, Opdrachten) · Planning (Planbord, Ritlijst, Live opvolging) · Magazijn (Laden & scannen, Dockplanning) · Klanten (Klanten, Locaties) · Personeel (…) — ~16 leaves instead of ~45. Nothing is deleted; deep links all keep working (paths unchanged except additions). `navConfig.test.ts` updated to the new tree; `NavFilter` stays (harmless).

## 15. The five planning surfaces — analysis & Wave 1 action

| Surface | Genuine use case? | Wave 1 action |
|---|---|---|
| /planning-center (Planbord) | Yes — THE dispatcher mutation surface | Primary "Planning" entry |
| /planning (Ritlijst) | Partially — read-only day list + trip detail host; duplicate of board's data | Keep as secondary leaf; candidate to fold into the board in W7 (board needs a list view first — do not build it now) |
| /operations | Yes — live monitoring ≠ planning | Keep, relabel "Live opvolging", group under Planning |
| /employee-planning | Yes — HR shifts, different permission family, different audience | Keep under Personeel; no change |
| /dock-planning | Yes — warehouse scheduling, different resource | Keep under Magazijn; no change |
Convergence target (W7): Planbord absorbs Ritlijst's list view; Operations stays separate. **No sixth surface is built in Wave 1** — the dossier page links *to* the board, never re-implements it.

## 16. Dashboard direction (Wave 1 scope)

Kill "everyone sees everything": introduce tile→permission+audience mapping in `features/dashboard/dashboardConfig.ts`.
- Dispatcher (planning.view): planning attention (unplanned orders w/ badges — existing endpoint), delivery problems (open exceptions), open incidents, dossiers met aandacht (new count from readiness of open dossiers — cheap: coverage != full OR no stops), vandaag geplande ritten.
- Warehouse (warehouse.view): today's loading trips, load-completeness, open package exceptions, returns in depot.
- Backoffice (invoices.view): pricing incomplete (coverage), completed-not-invoiced count (existing uninvoiced query), missing POD (operations counter), Peppol failures.
- Management (kpi.view): keeps the KPI tiles + link to /kpi.
Implementation: pure frontend regrouping of existing endpoints + one new lightweight count endpoint `GET /api/dossiers/attention-count`. The full exception-workspace dashboards (spec Part BB) remain W6/W10; Wave 1 only removes the 26-tile wall.

## 17. Read/edit model decision

**Chosen: read page + per-section edit drawers with explicit Save** (justified in §11). The full-page `SectionedForm` edit mode remains only on the legacy `/transport-orders/:id` page (unchanged) and for `/dossiers` it is never used. No aggressive autosave. `UnsavedChangesGuard` wraps each drawer.

## 18. UX baseline (builds on Wave 0)

Codified in `docs/frontend-ux-baseline.md` + tokens in `global.css`: control text 1rem (≥16px mobile); control min-height 44px; touch targets ≥44×44; field gap 16px, section gap 32px; labels 0.875rem/600, hints & errors 0.75rem; headings h1 32px (page — down from 56px on app pages), h2 20px (section) — dossier page adopts this scale, global h1 rule untouched until a later sweep; status chips = existing `Badge` tones, always icon+text (never color-only); warnings = `ValidationSummary`/attention-panel pattern (icon + text + action button); disabled = 0.6 opacity + `cursor: not-allowed`, read-only = borderless value text with label; breakpoints: drawers→sheets <900px, tables→cards on the dossier page <640px.

## 19. Exact backend changes

**New files** (all in `Modules/Dossiers/` unless noted):
- `Entities/ActivityType.cs`, `Entities/DossierActivity.cs`
- `Configurations/ActivityTypeConfiguration.cs`, `Configurations/DossierActivityConfiguration.cs`
- `Services/ActivityTypeService.cs` (+ `IActivityTypeService`), `Services/ActivityTypeSeeder.cs`, `Services/DossierActivityService.cs` (+ interface), `Services/DossierReadinessService.cs` (+ interface)
- `Controllers/ActivityTypesController.cs`
- `Dtos/ActivityTypeDtos.cs`, `Dtos/DossierActivityDtos.cs` (+ extend `DossierDtos.cs`)
**Modified:** `Entities/TransportDossier.cs` (new columns + `Activities` nav), `Services/DossierService.cs` (fast create, version bump, entity change w/ audit), `Controllers/DossiersController.cs` (new endpoints), `Modules/Orders/Entities/TransportOrder.cs` (`Version`), `Modules/Orders/Services/TransportOrderService.cs` (version gate; auto-wrap hook; volume-aggregation fix), `Modules/Orders/Dtos/TransportOrderDtos.cs` (`Version` in/out), `Modules/Identity/PermissionCodes.cs`, `Data/DefaultRoleDefinitions.cs`, `Data/DefaultRoleUpgrades.cs` (v26), `Data/TransportationDbContext.cs` (2 DbSets), 3 migrations (§3).

**Endpoints** (all tenant-scoped via context, `RequirePermission`, ProblemDetails errors, following existing routing conventions):

| Method & route | Purpose | Request → Response | Auth | Concurrency |
|---|---|---|---|---|
| GET `/api/activity-types` | list (incl. inactive for admins) | `?includeInactive` → `ActivityTypeDto[]` | `activity_types.view` OR `dossiers.view` (read needed by pickers) | — |
| POST `/api/activity-types` | create | `SaveActivityTypeRequest` (all §5 fields except system flags guarded) → `ActivityTypeDto` | `activity_types.manage` | — |
| PUT `/api/activity-types/{id}` | update | same → same; Code immutable after creation (400 otherwise) | `activity_types.manage` | — |
| DELETE `/api/activity-types/{id}` | soft-delete; 409 when in use by any activity | → 204 / 409 problem | `activity_types.manage` | — |
| POST `/api/dossiers` | fast create (§8) | `CreateDossierRequest` → 201 `DossierDetailDto` | `dossiers.manage` | — |
| GET `/api/dossiers/{id}` | detail incl. `activities[]`, `readiness[]`, `version` | → `DossierDetailDto` | `dossiers.view` | — |
| PUT `/api/dossiers/{id}` | header update (title/ref/date/notes/responsible) | `UpdateDossierRequest{…, Guid? Version}` → dto / 409(current) | `dossiers.manage` | Version |
| PUT `/api/dossiers/{id}/legal-entity` | entity change w/ audit old→new | `{Guid LegalEntityId, Guid? Version}` → dto / 409 | `dossiers.manage` | Version |
| POST `/api/dossiers/{id}/activities` | add activity (optionally creating a linked draft order for HasStops types via `CreateLinkedOrder: true`) | `SaveDossierActivityRequest{ActivityTypeId, Label?, PlannedDate?, DurationHours?, LinkedActivityId?, CreateLinkedOrder, Guid? Version}` → dto / 409 | `dossiers.manage` | Version |
| PUT `/api/dossiers/{id}/activities/{activityId}` | update | same minus type change (400: type immutable — delete+re-add) | `dossiers.manage` | Version |
| DELETE `/api/dossiers/{id}/activities/{activityId}` | remove; blocked (409) when linked order is not Draft/Cancelled | `{Guid? Version}` | `dossiers.manage` | Version |
| POST `/api/dossiers/{id}/activities/reorder` | resequence | `{Guid[] ActivityIds, Guid? Version}` | `dossiers.manage` | Version |
| GET `/api/dossiers/attention-count` | dashboard tile | → `{int Count}` | `dossiers.view` | — |
| (extend) PUT `/api/transport-orders/{id}` | existing update | + `Guid? Version` → 409(current detail) on mismatch | unchanged | Version |

Validators: inline service checks (house style — no FluentValidation in repo). Tenant: `TenantReferenceGuard.EnsureBelongsToTenantAsync` on every inbound FK (customer, activity type, linked order, linked activity, legal entity). Audit: `RecordAsync("Dossier"|"ActivityType", …)` on every mutation, entity-change explicitly old→new.

**Permissions (v26 delta):** new `activity_types.view` / `activity_types.manage` (granted: view → planner, dispatcher, management; manage → management). `dossiers.view/manage` reused as-is. No other additions (entity-override stays behind `dossiers.manage` until Wave 2 introduces `dossiers.override_entity` with the allowed-entities model).

## 20. Exact frontend plan

**New:**
- `features/dossiers/pages/NewDossierPage.tsx` (§8) + route `/dossiers/new`
- `features/dossiers/pages/DossierDetailPage.tsx` — **rewritten** to §11 (current 597-line version replaced; keeps compat rendering for directly-linked orders and incidents/relations under Meer/sections)
- `features/dossiers/components/` — `DossierHeader.tsx`, `AttentionPanel.tsx`, `ActivityCard.tsx` + `ActivityList.tsx`, `AddActivityDialog.tsx`, `SectionDrawer.tsx` (generic drawer shell + `UnsavedChangesGuard`), `DossierRouteSummary.tsx`, `DossierGoodsSummary.tsx`, `DossierPriceSummary.tsx`
- `features/dossiers/api/dossiersApi.ts` extensions + `activityTypesApi.ts`
- `features/settings/… /ActivityTypesPage.tsx` (CRUD, DataTable + drawer, icon picker from curated map) + route `/settings/activity-types`
- `features/transport-orders/components/sections/` — `GeneralSection.tsx`, `RouteSection.tsx`, `GoodsSection.tsx`, `ServicesSection.tsx`, `PriceSection.tsx`, `orderFormState.ts` (extracted from `TransportOrderForm.tsx`; §12 size budgets)
- `features/dashboard/dashboardConfig.ts` (§16)
- `docs/frontend-ux-baseline.md`
**Modified:** `TransportOrderForm.tsx` (becomes a thin composer over sections + `ValidationSummary` + field-targeted errors + version round-trip), `TransportOrderDetailPage.tsx` (dossier breadcrumb chip), `components/layout/nav/navConfig.ts` (+ its test) per §14, `routes/AppRoutes.tsx` (2 new routes), `DashboardPage.tsx` (config-driven tiles), `features/dossiers/pages/DossiersPage.tsx` (add "Nieuw dossier" primary button, show customer/date/readiness chip columns), `features/dossiers/types.ts`.
Component-boundary rule from §12 applies: no new file > 600 lines.

## 21–22. Permissions & tenant configuration tests

Covered in §19 (v26) and §23 rows 9/10/15; `ActivityType`/`DossierActivity` inherit `AuditableTenantEntity` → automatic global tenant filter; seeder is per-tenant; guard tests below prove isolation both read (list scoped) and write (`InvalidTenantReferenceException` on foreign `ActivityTypeId`).

## 23. Wave 1 test matrix

**Backend** (`Api.Tests/Dossiers/` new classes: `ActivityTypeServiceTests`, `DossierFastCreateTests`, `DossierActivityTests`, `DossierReadinessTests`, `DossierBackfillTests` (SQLite migration harness), `DossierConcurrencyTests`; extend `Orders/` with `OrderVersionTests`, `CargoVolumeAggregationTests`):
1. Minimal create: customer only → succeeds, number claimed, entity inherited, date defaulted. 2. Missing/foreign customer → 400 with Dutch message. 3. Customer default entity → inherited; no default → tenant default; no entities → null. 4. Multiple activities incl. reorder → sequences 1..n. 5. Crane + Plateau → two activities, `LinkedActivityId` set, same-dossier enforced (400 cross-dossier). 6. Storage-only dossier → valid, zero orders, readiness Info. 7. Empty dossier → valid, `activity.none` Info. 8. Unknown location → dossier + transport activity + draft order without stops saves; readiness Warning lists it. 9. Tenant isolation: tenant A cannot list/see B's activity types; using B's ActivityTypeId → `InvalidTenantReferenceException`; seeded defaults exist per tenant independently. 10. Inactive/deleted activity type on create/add → 400. 11. Historical compatibility: pre-existing order untouched by backfill fields; order timeline/status history byte-identical before/after migration (assert row counts + values). 12. Existing user-created dossier link → preserved; no wrapper created for its orders. 13. Backfill idempotency: run twice → identical state; order with wrapper → skipped; numbering continuous, no duplicates (unique index proven). 14. Concurrency: stale version → 409 carrying current DTO; null version → accepted; dossier and order both. 15. Permission enforcement: every new endpoint 403s without its code (mirror `Phase10SystematicSecurityTests` classification). 16. Full existing Orders + Dossiers + Invoicing suites stay green (regression gate each phase).

**Frontend** (Vitest, colocated `__tests__`):
1. NewDossierPage renders exactly 4 inputs + template tiles; 2. tile selection round-trips `activityTypeId`; 3. customer-only submit succeeds → navigates; 4. AddActivityDialog adds two activities to the list; 5. incomplete dossier saves (drawer save with empty route → no client block); 6. AttentionPanel `[Ga naar route]` scrolls/opens route section (anchor spy); 7–8. create flow contains no goods/address inputs (assert absence); 9. DossierDetailPage default render shows ≤ the §11 sections, no advanced controls (assert absent test-ids); 10. RouteSection "Geavanceerd" reveals the timing quadruple; 11. `/transport-orders/:id` still resolves and shows the dossier chip; 12. navConfig test: dispatcher permission set yields the §14 tree (≤16 leaves), hr/magazijn subsets correct; 13. dossier page renders single-column below 640px (container query/class assertion); 14. FormField/SearchableSelect style contract test asserts `font-size: 1rem` + `min-height: 44px` via computed class rules (jsdom stylesheet parse).

## 24. Manual UX acceptance (post-implementation, for the tester)

Scenario A (crane call): create in <10s, no blockers, dossier shows Kraanwerk card + attention list. B (2 europallets Antwerpen→Liège): fast create → transport drawer: 2 stops, 7 visible fields each, goods line "2 Europallet", price summary chip. C: crane + plateau both visible as cards, plateau linked. D: open a pre-migration order via old URL and via its wrapper dossier — identical data, working pricing/stops/history.

## 25. Definition of done

All 14 user-stated DoD bullets (§25 of the instruction) verified against: green §23 matrix, the §11 page implemented, §14 nav shipped, §8 creation timed, backfill applied on a prod-copy database without diffs to order/invoice tables (checksum comparison), and the §24 walkthrough executed.

## 26. Internal implementation phases (each ends: dotnet test + npm test + tsc + lint + build green, focused commit)

1. **Schema & entities** — migrations 1–2, entities, configs, DbSets, seeder; entity/config tests. (no UI)
2. **ActivityType API + settings UI** — service/controller/DTOs, permissions v26, `/settings/activity-types` page; tests rows 9/10/15 (types part).
3. **Backfill** — migration 3 + `DossierBackfillTests` + prod-copy dry-run script (scripts/), rows 11–13.
4. **Fast create + activities API** — DossierService/ActivityService/endpoints + auto-wrap hook in `TransportOrderService.CreateAsync`; NewDossierPage; rows 1–8 backend, 1–4 frontend.
5. **Concurrency** — Version columns (already in migration 2), service gates, DTO round-trip, 409 UX; row 14 + frontend rebase test.
6. **Order-form decomposition** — extract sections + `orderFormState.ts`, `ValidationSummary` adoption, volume-fix; legacy form pixel-behavior regression via existing `transportOrderSectionedForm.test.tsx` + new targeted-error tests.
7. **Dossier detail UX** — page rewrite, drawers, attention panel, readiness service + endpoint embedding; rows 5–10 frontend, readiness backend tests.
8. **Nav + dashboard** — navConfig tree, routes, dashboardConfig, attention-count endpoint; frontend row 12, nav tests.
9. **Hardening & walkthrough** — full regression suites, prod-copy checksum verification, §24 script, docs (`docs/dossiers.md` update), memory update.

Estimated relative effort: phases 6–7 are the heavy ones (~40% combined); 1–5 mechanical; 8–9 small.

## Risks

| Risk | Mitigation |
|---|---|
| Backfill on large order tables (locking, sequence gaps) | Pure-SQL batched per tenant, single claimed number range per tenant, dry-run script against prod copy in phase 3 |
| Un-applied earlier migrations on the target DB | Phase 0 pre-flight reconciliation; STOP on non-additive drift (genuine blocker) |
| Order-form decomposition regressions | Extract-only refactor first (no behavior change) guarded by existing form tests, THEN apply disclosure changes |
| Auto-wrap surprises EDI/portal flows | Hook is same-transaction + covered by EDI/portal create tests; wrapper creation failure fails the whole create (no half state) |
| Nav change disorients existing users | Paths unchanged; only grouping/labels move; command palette + NavFilter still find everything |
| Version gate breaks legacy API clients | `Version == null` skips the check by design; tested |
| Dossier list noise from thousands of wrappers | List defaults to Status=Open + readiness filters; wrappers of Completed orders arrive Closed |
