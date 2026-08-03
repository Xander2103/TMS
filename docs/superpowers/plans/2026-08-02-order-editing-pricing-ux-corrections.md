# Order Editing & Pricing UX Correction Wave — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the goods-line unit persistence bug at the root, make manual price lines unambiguous (quantity×price vs fixed amount), surface order actions at the top, clarify commercial goods lines vs scanable colli, relax goods-description validation, give services a real add-flow with informational-line hygiene, and add order-level included loading/unloading-time overrides — all with regression tests.

**Architecture:** Single .NET 10 API (`TransportationService.Api`, module folders, hand-rolled validation in services, `AuditingSaveChangesInterceptor` soft-delete) + React 19/Vite SPA (`TransportationService.Web`, no state library, PUT-response-drives-state). All work continues on branch `nav-redesign`. Pricing flows through `PricingEngine.CalculateAsync` → `TransportOrderService.ApplyPricingAsync`; price lines persist as `TransportOrderPricingLine` with `OrderPriceLineKind` (Auto/AutoAdjusted/Manual/Proposed) and `LineKey` merge identity.

**Tech Stack:** .NET 10 + EF Core (SQLite in tests), xunit, React 19 + TypeScript + Vitest 4 + Testing Library.

## Global Constraints

- All existing tests must remain green; the ONE intentional behavior change (both-empty goods description now rejected, Task 4) updates `Create_WithoutGoodsDescription_Succeeds` explicitly and is reported as such.
- `dotnet build` must produce 0 new warnings; frontend `npm test`, `npm run lint`, `npm run build` (which is the typecheck: `tsc -b`) must pass. Run from `TransportationService.Web` for npm.
- Backend tests: `dotnet test TransportationService.Api.Tests\TransportationService.Api.Tests.csproj` (filter with `--filter "FullyQualifiedName~<Class>"` while iterating).
- Reuse existing entities/DTOs — no duplicate models. New columns via `dotnet ef migrations add <Name> --project TransportationService.Api` (tests use `EnsureCreated`, so migrations are for the real DB; do NOT apply them — repo convention is to note "not yet applied").
- All planner UI labels are hard-coded Dutch strings (no i18n on planner side). Use exact labels given per task.
- Tenant isolation: every new query filters on `_tenantContext.TenantId`; follow existing patterns in `TransportOrderService`.
- Audit convention: `_auditService.LogAsync` with purpose-built anonymous objects; reason required when touching non-Manual price lines (existing rule — keep).
- Commit per task with conventional-commit messages ending in `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Test harness idiom: each Orders test file has a private `Harness` record + `SeedAsync()`; call services directly; `h.Db.Context.ChangeTracker.Clear()` between create and update (see `Orders\TransportOrderServiceTests.cs:22-58`). Follow the host file's existing helpers (`Request(...)`, `Stop(...)`, `SeedRulesAsync(...)`) when writing new tests there.

---

## Root causes (established by inspection — reference for all tasks)

1. **Unit change "not persisted"** — write path is correct (`TransportOrderService.cs:974` normalizes and stores `QuantityUnitCode` identically on create and update). The read path drops it: the `CargoItemDto` projection at `TransportOrderService.cs:1084-1088` passes 22 positional args; the 23rd, `QuantityUnitCode` (optional, default `null`, `TransportOrderDtos.cs:190`), is omitted, so every GET/PUT response returns `quantityUnitCode: null`. The edit form re-seeds from the response (`TransportOrderForm.tsx:236`) and the next save writes `null` back — the loss becomes real in the DB on the second save.
2. **Cargo lines are wholesale-replaced** on every update (`TransportOrderService.cs:378-384`) with fresh Guids — orphaning `Package.CargoItemId` links and making "preserve unchanged lines" impossible. `request.CargoItems == null` silently wipes all lines.
3. **Summary vs structured lines**: order-level `Quantity`/`QuantityUnitCode` and `CargoItems` are independent; detail page shows only the order-level summary ("Aantal"). Pricing per-unit quantities already derive from cargo lines by unit code (`TransportOrderService.cs:1534-1547`).
4. **Manual services already exist in the domain** (`Create/UpdateTransportOrderRequest.Services` → `OrderServiceInput` → `TransportOrderServiceLine`), applied via order save; the edit tab is a checkbox list with per-basis quantity inputs but no clear add-flow, no note, no badges. No dedicated per-service endpoint exists (acceptable — keep the order-save model).
5. **Goods description**: order-level `GoodsDescription` is already optional on both sides (the backend validator receives it but never reads it; frontend hint says "Optioneel"). Per-line `Description` is hard-required (`TransportOrderService.cs:898-901`), and an order with NO description anywhere is currently accepted.
6. **Manual price line ambiguity**: `SaveOrderPriceLineRequest` accepts `Quantity`, `UnitPrice`, `Amount`; `ResolveAmount` (`TransportOrderService.cs:1841-1842`) = `Amount ?? Round(Q×UP, 2)` — explicit Amount always wins and contradictions are accepted verbatim. No `Unit` field exists on price lines. Frontend modal validates only the label.
7. **Included time**: lives only on `PricingAgreement` (contract) and the mutually-exclusive one-off fields. No order-level override of contract values, no rounding step, no minimum billable extra time (engine bills raw `minutes/60 × rate`, `PricingEngine.cs:948`).
8. **Zero €0 lines**: every unmet service condition emits an `Informational` €0 line (`PricingEngine.cs:725-743` etc.) which the detail price table renders like a normal invoice line.
9. **Order delete** (`TransportOrderService.cs:764-788`) soft-deletes order+stops only; cargo items and packages stay `IsDeleted = false` (invisible but orphaned).

---

### Task 1: Fix unit round-trip (read projection) + backend regression tests

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs:1084-1088`
- Test: `TransportationService.Api.Tests\Orders\TransportOrderServiceTests.cs`

**Interfaces:**
- Produces: `CargoItemDto.QuantityUnitCode` now populated in every detail/create/update response. All later tasks may rely on it.

- [ ] **Step 1: Write the failing test** — in `TransportOrderServiceTests.cs`, using that file's existing `SeedAsync`/`Request`/`Stop` helpers:

```csharp
[Fact]
public async Task Update_ChangedCargoUnit_RoundTripsThroughDetailDto()
{
    var h = await SeedAsync();
    var create = Request(h) with
    {
        CargoItems =
        [
            new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")
        ]
    };
    var created = await h.Service.CreateAsync(create);
    Assert.Equal("EUROPALLET", Assert.Single(created.Order!.CargoItems).QuantityUnitCode);

    h.Db.Context.ChangeTracker.Clear();
    var update = BuildUpdateFrom(created.Order!) with
    {
        CargoItems =
        [
            new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "COLLI")
        ]
    };
    var updated = await h.Service.UpdateAsync(created.Order!.Id, update);
    Assert.Equal("COLLI", Assert.Single(updated.Order!.CargoItems).QuantityUnitCode);

    h.Db.Context.ChangeTracker.Clear();
    var reloaded = await h.Service.GetByIdAsync(created.Order!.Id, CancellationToken.None);
    Assert.Equal("COLLI", Assert.Single(reloaded!.CargoItems).QuantityUnitCode);
}
```

Add a private `BuildUpdateFrom(TransportOrderDetailDto d)` helper in this file that maps detail → `UpdateTransportOrderRequest` carrying stops AND cargo items (mirror the mapping in `OrderPricingLineTests.cs:74` but include `CargoItems` mapped from `d.CargoItems` with their `QuantityUnitCode`). This helper is reused in Tasks 2 and 4.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TransportationService.Api.Tests\TransportationService.Api.Tests.csproj --filter "FullyQualifiedName~Update_ChangedCargoUnit_RoundTripsThroughDetailDto"`
Expected: FAIL — the create assertion already fails (`QuantityUnitCode` is null in the response).

- [ ] **Step 3: Fix the projection** — `TransportOrderService.cs:1088`:

```csharp
                c.AdrRequired, c.AdrDetails, c.Stackable, c.Reference, c.LoadingStopId, c.UnloadingStopId,
                c.QuantityUnitCode))
```

- [ ] **Step 4: Run the test again** — Expected: PASS. Also run the whole Orders folder: `--filter "FullyQualifiedName~Orders"`.

- [ ] **Step 5: Commit** — `fix(orders): map QuantityUnitCode in CargoItemDto projection — unit changes survive reload`

---

### Task 2: Id-preserving cargo synchronization + cargo audit + null-means-unchanged

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Dtos\TransportOrderDtos.cs` (`CargoItemInput` — append `Guid? Id = null`)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (`UpdateAsync` cargo block :378-384, `BuildCargoItems` :950-993, update audit payload :333/:394)
- Modify: `TransportationService.Web\src\features\transport-orders\components\TransportOrderForm.tsx` (CargoFormRow gets `id`, payload sends it)
- Test: `TransportationService.Api.Tests\Orders\TransportOrderServiceTests.cs`

**Interfaces:**
- Consumes: `BuildUpdateFrom` helper from Task 1.
- Produces: `CargoItemInput.Id` (optional Guid) — matching lines are updated in place, ids stable; `request.CargoItems == null` on update now means "leave cargo unchanged" (empty list still clears). Frontend sends `id` for existing rows. Audit action `Updated` payload now includes `Cargo` old/new summaries `{ Description, ExpectedQuantity, QuantityUnitCode }`.

- [ ] **Step 1: Write failing tests** (same file):

```csharp
[Fact]
public async Task Update_MatchingCargoId_UpdatesInPlace_KeepsGuid_AndAuditsUnitChange()
{
    var h = await SeedAsync();
    var created = await h.Service.CreateAsync(Request(h) with
    {
        CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]
    });
    var lineId = created.Order!.CargoItems.Single().Id;
    h.Db.Context.ChangeTracker.Clear();

    var update = BuildUpdateFrom(created.Order!) with
    {
        CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null,
            QuantityUnitCode: "COLLI", Id: lineId)]
    };
    var updated = await h.Service.UpdateAsync(created.Order!.Id, update);

    Assert.Equal(lineId, Assert.Single(updated.Order!.CargoItems).Id); // id preserved
    var entity = await h.Db.Context.CargoItems.SingleAsync(c => c.Id == lineId);
    Assert.Equal("COLLI", entity.QuantityUnitCode);

    var audit = await h.Db.Context.AuditLogs
        .Where(a => a.EntityName == "TransportOrder" && a.Action == "Updated")
        .OrderByDescending(a => a.Id).FirstAsync();
    Assert.Contains("EUROPALLET", audit.OldValuesJson);
    Assert.Contains("COLLI", audit.NewValuesJson);
}

[Fact]
public async Task Update_NullCargoItems_LeavesExistingLinesUntouched()
{
    var h = await SeedAsync();
    var created = await h.Service.CreateAsync(Request(h) with
    {
        CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]
    });
    h.Db.Context.ChangeTracker.Clear();
    var updated = await h.Service.UpdateAsync(created.Order!.Id,
        BuildUpdateFrom(created.Order!) with { CargoItems = null });
    Assert.Single(updated.Order!.CargoItems); // not wiped
}

[Fact]
public async Task Update_EmptyCargoItems_ClearsLines()
{
    var h = await SeedAsync();
    var created = await h.Service.CreateAsync(Request(h) with
    {
        CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]
    });
    h.Db.Context.ChangeTracker.Clear();
    var updated = await h.Service.UpdateAsync(created.Order!.Id,
        BuildUpdateFrom(created.Order!) with { CargoItems = [] });
    Assert.Empty(updated.Order!.CargoItems);
}
```

`BuildUpdateFrom` must now also map `Id: c.Id` for each cargo line (update the Task 1 helper).

- [ ] **Step 2: Run — expect FAIL** (ids currently regenerate; null wipes; no cargo audit).

- [ ] **Step 3: Implement.** In `UpdateAsync`, replace the wholesale block (:378-384):

```csharp
// Cargo: id-matched in-place sync. null = leave unchanged (API contract); [] = clear.
var existingCargo = await _dbContext.CargoItems
    .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
    .OrderBy(c => c.Sequence)
    .ToListAsync(cancellationToken);
var cargoBefore = existingCargo
    .Select(c => new { c.Description, c.ExpectedQuantity, c.QuantityUnitCode }).ToList();
if (request.CargoItems is not null)
{
    var byId = existingCargo.ToDictionary(c => c.Id);
    var seen = new HashSet<Guid>();
    var sequence = 1;
    foreach (var input in request.CargoItems)
    {
        if (input.Id is { } id && byId.TryGetValue(id, out var entity))
        {
            ApplyCargoInput(entity, input, sequence++, order.Stops);
            seen.Add(id);
        }
        else
        {
            _dbContext.Add(BuildCargoItem(order.Id, input, sequence++, order.Stops));
        }
    }
    _dbContext.RemoveRange(existingCargo.Where(c => !seen.Contains(c.Id)));
}
```

Refactor `BuildCargoItems` (:950-993): extract the per-item property assignments into `private static void ApplyCargoInput(CargoItem target, CargoItemInput input, int sequence, List<TransportOrderStop> stops)` (sets every mutable field exactly as `BuildCargoItems` does today, including stop-index resolution and `VolumeM3` derivation); `BuildCargoItem` = `new CargoItem { Id = Guid.NewGuid(), TenantId = ..., TransportOrderId = orderId }` + `ApplyCargoInput`. `BuildCargoItems` (still used by Create) becomes a loop over `BuildCargoItem`. Note: `CargoItemsError` (:891-948) keeps validating inputs — unchanged here.

Build `cargoAfter` the same way after the sync and extend the existing Updated-audit old/new objects (:333 and :394-395) with `Cargo = cargoBefore` / `Cargo = cargoAfter`.

Frontend: in `TransportOrderForm.tsx` add `id: string | null` to `CargoFormRow`, seed `id: c.id` at :227-236 (new rows `id: null`), and send `id: cargo.id` in the payload map at :684-712. Add `id?: string | null` to the cargo input type in `types.ts` (the `CargoItemInput`-shaped member around :345+).

- [ ] **Step 4: Run all Orders + CombinedUnitDiscountOrderTests + portal tests** — Expected: PASS (portal create path uses `BuildCargoItems`, untouched behavior).

- [ ] **Step 5: Frontend check** — `npm test` (expect green; existing form tests don't assert payload ids) and `npm run build`.

- [ ] **Step 6: Commit** — `fix(orders): id-preserving cargo sync, null=unchanged contract, cargo audit old/new`

---

### Task 3: Order actions at top + delete confirmation + delete cascade hygiene

**Files:**
- Modify: `TransportationService.Web\src\features\transport-orders\pages\TransportOrderDetailPage.tsx` (:350-383 header, :681-692 bottom stays, :784-793 dialog)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (DeleteAsync :764-788)
- Test: `TransportationService.Api.Tests\Orders\TransportOrderServiceTests.cs`, `TransportationService.Web\src\features\transport-orders\pages\__tests__\orderDetailActions.test.tsx` (new)

**Interfaces:**
- Consumes: existing `editable`/`deletable` gates (:333-336), `ConfirmDialog`, `PageHeader` action slot.
- Produces: nothing downstream.

- [ ] **Step 1: Backend failing test** — order delete soft-deletes cargo:

```csharp
[Fact]
public async Task Delete_SoftDeletesCargoItems()
{
    var h = await SeedAsync();
    var created = await h.Service.CreateAsync(Request(h) with
    {
        CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "COLLI")]
    });
    h.Db.Context.ChangeTracker.Clear();
    await h.Service.DeleteAsync(created.Order!.Id, CancellationToken.None);
    var cargo = await h.Db.Context.CargoItems.IgnoreQueryFilters()
        .SingleAsync(c => c.TransportOrderId == created.Order!.Id);
    Assert.True(cargo.IsDeleted);
}
```

(Adapt the `DeleteAsync` call to its real signature in the file.) Run — expect FAIL.

- [ ] **Step 2: Implement backend** — in `DeleteAsync`, before `_dbContext.Remove(order)`:

```csharp
var cargo = await _dbContext.CargoItems
    .Where(c => c.TenantId == _tenantContext.TenantId && c.TransportOrderId == order.Id)
    .ToListAsync(cancellationToken);
_dbContext.RemoveRange(cargo); // interceptor converts to IsDeleted = true
```

Run test — PASS. (Packages/price lines stay as-is: price lines are only reachable through the order; packages have their own lifecycle — document as accepted behavior.)

- [ ] **Step 3: Frontend — top actions.** In the `PageHeader` action node (:350-383) prepend, before the status controls:

```tsx
{editable && !editing && (
  <Button onClick={() => setEditing(true)} disabled={busy}>Bewerken</Button>
)}
{deletable && (
  <Button variant="danger" onClick={() => setConfirmingDelete(true)} disabled={busy}>Verwijderen</Button>
)}
```

Reuse the exact same state/handlers as the bottom buttons (:681-692) — if the bottom buttons use inline handlers, lift them so top and bottom share `startEdit`/`setConfirmingDelete`. Keep the bottom block. The header action container (`.page-header-action`) already wraps on small screens; verify `flex-wrap: wrap` is set in the page-header CSS and add it if missing.

- [ ] **Step 4: Frontend — delete dialog copy.** Update the `ConfirmDialog` (:784-793) title/body:

```tsx
title={`Transportopdracht ${order.orderNumber} verwijderen?`}
```
Body text:
```
Deze actie kan niet ongedaan worden gemaakt. De opdracht van {order.customerName} wordt verwijderd; gekoppelde goederenlijnen, prijsregels en conceptplanning worden behandeld volgens de bestaande domeinregels.
```

- [ ] **Step 5: Frontend test** — new `orderDetailActions.test.tsx` (copy the fixture/fetch-mock scaffolding from `orderPricingLines.test.tsx`):
  - renders "Bewerken" and "Verwijderen" **twice** (header + bottom) for a Draft order with `orders.edit` + `orders.delete`;
  - hides both header buttons without those permissions;
  - hides Bewerken for status `Completed` (not editable);
  - clicking header "Verwijderen" shows a dialog containing the order number and customer name.

- [ ] **Step 6: Run** `npm test`, `npm run lint`, `npm run build` — PASS.

- [ ] **Step 7: Commit** — `feat(orders): top-level Bewerken/Verwijderen, richer delete confirm, cargo soft-delete on order delete`

---

### Task 4: Goods-description validation (at least one description somewhere)

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Entities\CargoItem.cs:20` (`string Description` → `string? Description`)
- Modify: `TransportationService.Api\Modules\Orders\Configurations\CargoItemConfiguration.cs` (drop IsRequired on Description)
- Modify: `TransportationService.Api\Modules\Orders\Dtos\TransportOrderDtos.cs` (`CargoItemInput.Description` → `string?`; `CargoItemDto.Description` → `string?`)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (`CargoItemsError` :898-901, `ValidateAsync` :818+)
- Modify: `TransportationService.Api\Modules\Packages\Services\PackageGenerationService.cs` (description fallback for labels)
- Migration: `dotnet ef migrations add CargoDescriptionOptional --project TransportationService.Api`
- Modify: `TransportationService.Web\src\features\transport-orders\components\TransportOrderForm.tsx` (hint + submit validation)
- Test: `TransportationService.Api.Tests\Orders\TransportOrderServiceTests.cs`

**Interfaces:**
- Produces: rule "order needs ≥1 description: `GoodsDescription` OR any cargo line description". Cargo line `Description` nullable everywhere. Display fallback for a description-less line: `"{ExpectedQuantity} × {unit}"`.

- [ ] **Step 1: Failing tests:**

```csharp
[Fact]
public async Task Create_LineDescriptionOnly_Succeeds_GeneralOptional()
{
    var h = await SeedAsync();
    var result = await h.Service.CreateAsync(Request(h) with
    {
        GoodsDescription = null,
        CargoItems = [new CargoItemInput("2 europallets onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")]
    });
    Assert.True(result.Succeeded);
}

[Fact]
public async Task Create_GeneralDescriptionOnly_WithDescriptionlessLine_Succeeds()
{
    var h = await SeedAsync();
    var result = await h.Service.CreateAsync(Request(h) with
    {
        GoodsDescription = "Gemengde goederen",
        CargoItems = [new CargoItemInput(null, null, 4, null, null, QuantityUnitCode: "COLLI")]
    });
    Assert.True(result.Succeeded);
}

[Fact]
public async Task Create_NoDescriptionAnywhere_IsRejected()
{
    var h = await SeedAsync();
    var result = await h.Service.CreateAsync(Request(h) with
    {
        GoodsDescription = null,
        CargoItems = [new CargoItemInput(null, null, 4, null, null, QuantityUnitCode: "COLLI")]
    });
    Assert.False(result.Succeeded);
    Assert.Contains("omschrijving", result.Error!, StringComparison.OrdinalIgnoreCase);
}
```

Run — expect FAIL (second/third: per-line description currently required; also compile changes needed for `null` description).

- [ ] **Step 2: Implement backend.**
  - `CargoItem.Description` → `string?`; config drops `.IsRequired()` (keep max 300).
  - `CargoItemsError`: delete the per-line required check (:898-901).
  - `ValidateAsync`: the `goodsDescription` parameter finally gets used — add (both create and update call paths pass the request's cargo items already or extend the signature to receive them):

```csharp
var hasAnyDescription = !string.IsNullOrWhiteSpace(goodsDescription)
    || cargoItems?.Any(c => !string.IsNullOrWhiteSpace(c.Description)) == true;
if (!hasAnyDescription)
{
    return "Geef minstens één omschrijving van de goederen op: algemeen of op een goederenlijn.";
}
```

  - `ApplyCargoInput`/`BuildCargoItem`: `Description = Trim(input.Description)` (nullable-safe).
  - `PackageGenerationService`: where line description feeds package/label text, fall back to `$"{line.ExpectedQuantity:0.##} × {line.QuantityUnitCode ?? line.UnitTypeLabel ?? "stuks"}"` when `Description` is null/blank.
  - **Intentional test update**: `Create_WithoutGoodsDescription_Succeeds` asserted an order with no description anywhere is accepted. Rename to `Create_WithoutGoodsDescription_ButWithDescribedLine_Succeeds` and give it one cargo line with a description (or a filled general description if it had no lines). This is the one deliberate behavior change of the wave.
  - Migration `CargoDescriptionOptional` (alter column nullable).

- [ ] **Step 3: Run full backend suite** — everything green (fix any other test that created description-less orders by adding a description).

- [ ] **Step 4: Frontend.** In `TransportOrderForm.tsx`:
  - General field hint (:997) becomes: `"Optioneel wanneer de goederen hieronder per lijn worden beschreven."` No asterisk (there is none today).
  - Per-line description field: label stays `Omschrijving`, add hint `"Optioneel als de algemene omschrijving is ingevuld."`, remove any `required` attribute.
  - Submit guard in the payload/validate path (:624 area): if `!goodsDescription.trim() && !cargoItems.some(c => c.description.trim())` → show error `"Geef minstens één omschrijving van de goederen op: algemeen of per goederenlijn."` and abort submit.
  - Cargo row description display fallbacks anywhere `.description` is rendered on detail page: `item.description ?? `${item.expectedQuantity} × ${unitLabel(item.quantityUnitCode, item.quantityUnit)}``.
  - `types.ts`: cargo description fields → `string | null`.

- [ ] **Step 5: Frontend test** (in `transportOrderSectionedForm.test.tsx`): submitting with empty general description and one line description succeeds (fetch mock called); with both empty shows the error and does not call the API.

- [ ] **Step 6: Run** backend suite + `npm test && npm run lint && npm run build`.

- [ ] **Step 7: Commit** — `feat(orders): description required once, not twice — line descriptions optional, both-empty rejected`

---

### Task 5: Manual price line backend — Unit field + contradiction guard

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Entities\TransportOrderPricing.cs` (add `public string? Unit { get; set; }` to `TransportOrderPricingLine`)
- Modify: `TransportationService.Api\Modules\Orders\Configurations\OrderPricingConfigurations.cs` (max length 30)
- Modify: `TransportationService.Api\Modules\Orders\Dtos\TransportOrderDtos.cs` (`SaveOrderPriceLineRequest` + `Unit`; `OrderPricingLineDto` + `Unit`, `ServiceOptionId`)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (`SaveOrderPriceLinesAsync` :1706-1838, `ResolveAmount` :1841, detail projection :1094-1098)
- Migration: `dotnet ef migrations add PricingLineUnit --project TransportationService.Api`
- Test: `TransportationService.Api.Tests\Orders\OrderPricingLineTests.cs`

**Interfaces:**
- Produces: `SaveOrderPriceLineRequest(string? LineKey, string Label, decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? AdjustReason, bool Remove = false, string? Unit = null)`; `OrderPricingLineDto` gains `string? Unit = null, Guid? ServiceOptionId = null` (appended optional params — existing positional constructions keep compiling). Validation: Q×UP vs Amount contradiction → `DomainValidationException`.

- [ ] **Step 1: Failing tests** in `OrderPricingLineTests.cs` (use its harness + `SaveOrderPriceLinesAsync` call pattern from `S17_FreeManualLine_...` :148):

```csharp
[Fact]
public async Task ManualLine_QuantityTimesUnitPrice_ComputesAmount_AndStoresUnit()
{
    // arrange order via existing harness…
    var lines = new List<SaveOrderPriceLineRequest>
    {
        new(null, "Extra handling", Quantity: 3, UnitPrice: 1.25m, Amount: null, AdjustReason: null, Unit: "COLLI")
    };
    // act: SaveOrderPriceLinesAsync
    // assert: created line Amount == 3.75m, Unit == "COLLI", Kind == Manual
}

[Fact]
public async Task ManualLine_ContradictoryAmount_IsRejected()
{
    var lines = new List<SaveOrderPriceLineRequest>
    {
        new(null, "Extra handling", Quantity: 2, UnitPrice: 10m, Amount: 50m, AdjustReason: null)
    };
    // assert: DomainValidationException with message containing "aantal × eenheidsprijs"
}

[Fact]
public async Task ManualLine_FixedAmount_WithoutQuantity_IsAccepted()
{
    var lines = new List<SaveOrderPriceLineRequest>
    {
        new(null, "Manual handling", Quantity: null, UnitPrice: null, Amount: 10m, AdjustReason: null)
    };
    // assert: Amount == 10m, Quantity/UnitPrice/Unit null
}
```

- [ ] **Step 2: Run — FAIL** (no Unit param; contradiction accepted today).

- [ ] **Step 3: Implement.**
  - Entity + config + migration (`Unit`, nvarchar 30, nullable).
  - DTO changes as in Interfaces (append-only, defaulted).
  - In `SaveOrderPriceLinesAsync`, before resolving each line's amount (both new-line :1741 and update :1807 paths):

```csharp
if (line.Quantity is { } q && line.UnitPrice is { } up && line.Amount is { } a
    && Math.Round(q * up, 2) != Math.Round(a, 2))
{
    throw new DomainValidationException(
        "Het totaalbedrag komt niet overeen met aantal × eenheidsprijs. Laat het bedrag leeg of corrigeer de waarden.");
}
if (line.Quantity is <= 0)
{
    throw new DomainValidationException("Aantal moet groter zijn dan nul.");
}
```

  (Match the file's existing failure style — if it returns result objects instead of throwing, return the equivalent validation failure.)
  - Persist `Unit = NormalizeUnitCode(line.Unit)` on new manual lines and on updates when provided.
  - Detail projection (:1094-1098): append `l.Unit, l.ServiceOptionId` to the `OrderPricingLineDto` construction (they are the new trailing optional params — pass them explicitly).

- [ ] **Step 4: Run** OrderPricingLineTests + full Orders + Tarification filters — green (existing calls pass `Amount` without Q+UP or Q+UP without Amount, so the guard doesn't trip).

- [ ] **Step 5: Commit** — `feat(pricing): unit on manual price lines, reject contradictory qty×price vs amount`

---

### Task 6: Manual price-line modal — Berekeningswijze UX

**Files:**
- Modify: `TransportationService.Web\src\features\transport-orders\pages\TransportOrderDetailPage.tsx` (add-modal :891-932, `handleAddLine` :264-277, edit-modal :795-855)
- Modify: `TransportationService.Web\src\features\transport-orders\types.ts` (`SaveOrderPriceLineInput` + `unit`; price line type + `unit`, `serviceOptionId`)
- Modify: `TransportationService.Web\src\features\transport-orders\api\transportOrdersApi.ts` (pass-through only if typed)
- Test: `TransportationService.Web\src\features\transport-orders\pages\__tests__\orderPricingLines.test.tsx`

**Interfaces:**
- Consumes: Task 5 API (`unit` accepted, contradiction rejected server-side).
- Produces: modal state `addMode: 'perUnit' | 'fixed'`.

- [ ] **Step 1: Failing tests** (extend `orderPricingLines.test.tsx`):
  - modal shows a "Berekeningswijze" radio group with options "Berekenen op basis van aantal en eenheidsprijs" and "Vast bedrag";
  - in per-unit mode: fields Omschrijving, Aantal, Eenheid, Eenheidsprijs visible; Totaalbedrag rendered read-only and equal to `3 × €1,25 = €3,75` when quantity=3, unitPrice=1.25; submit payload contains `{quantity: 3, unitPrice: 1.25, unit: 'COLLI', amount: null}`;
  - in fixed mode: Aantal/Eenheidsprijs/Eenheid absent; Totaalbedrag editable; payload `{amount: 10, quantity: null, unitPrice: null}`;
  - Reden remains optional (submit without it succeeds).

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Implement the add-modal.** Replace the field block (:907-930):

```tsx
<FormField label="Omschrijving" htmlFor="add-line-label">
  <input id="add-line-label" value={addLabel} maxLength={300}
    onChange={(e) => setAddLabel(e.target.value)} />
</FormField>
<FormField label="Berekeningswijze" htmlFor="add-line-mode">
  <div role="radiogroup" className="tof-radio-row" id="add-line-mode">
    <label><input type="radio" checked={addMode === 'perUnit'}
      onChange={() => setAddMode('perUnit')} /> Berekenen op basis van aantal en eenheidsprijs</label>
    <label><input type="radio" checked={addMode === 'fixed'}
      onChange={() => setAddMode('fixed')} /> Vast bedrag</label>
  </div>
</FormField>
{addMode === 'perUnit' && (
  <>
    <FormField label="Aantal" htmlFor="add-line-qty">
      <input id="add-line-qty" type="number" min="0.01" step="any" value={addQuantity}
        onChange={(e) => setAddQuantity(e.target.value)} />
    </FormField>
    <FormField label="Eenheid" htmlFor="add-line-unit" hint="Optioneel.">
      <UnitSelect id="add-line-unit" value={addUnit} onChange={setAddUnit}
        units={unitOptions} preferredUnits={[]} />
    </FormField>
    <FormField label="Eenheidsprijs (€)" htmlFor="add-line-price">
      <input id="add-line-price" type="number" step="any" value={addUnitPrice}
        onChange={(e) => setAddUnitPrice(e.target.value)} />
    </FormField>
    <FormField label="Totaalbedrag" htmlFor="add-line-total">
      <input id="add-line-total" readOnly value={computedTotalDisplay} />
    </FormField>
  </>
)}
{addMode === 'fixed' && (
  <FormField label="Totaalbedrag (€)" htmlFor="add-line-amount">
    <input id="add-line-amount" type="number" step="any" value={addAmount}
      onChange={(e) => setAddAmount(e.target.value)} />
  </FormField>
)}
<FormField label="Reden" htmlFor="add-line-reason" hint="Optioneel.">
  <input id="add-line-reason" value={addReason} onChange={(e) => setAddReason(e.target.value)} />
</FormField>
```

With `const computedTotal = addMode === 'perUnit' ? round2(parseNum(addQuantity) * parseNum(addUnitPrice)) : null` (`null`-safe; display `—` until both filled). `UnitSelect` needs the unit options on this page: load them with the same `useLookupOptions('/api/unit-types')` hook used by the form (import it). `handleAddLine` validation: label required (existing); per-unit mode requires quantity > 0 and unitPrice ≥ 0 (`"Geef een aantal en eenheidsprijs op."`); fixed mode requires amount (`"Geef een totaalbedrag op."`). Payload per mode as in Step 1. Contradictions are structurally impossible (amount never sent in per-unit mode).

- [ ] **Step 4: Edit-modal consistency** (:795-855): when both `editQuantity` and `editUnitPrice` are non-empty, make the Bedrag input read-only showing the computed product and send `amount: null`; otherwise leave it editable (fixed-amount adjustments unchanged).

- [ ] **Step 5: Run** `npm test && npm run lint && npm run build` — PASS.

- [ ] **Step 6: Commit** — `feat(pricing-ui): manual line modal with explicit berekeningswijze, computed read-only total, unit`

---

### Task 7: Price table — Berekening column, badges, informational-line hygiene

**Files:**
- Modify: `TransportationService.Web\src\features\transport-orders\pages\TransportOrderDetailPage.tsx` (table :497-560)
- Modify: `TransportationService.Web\src\features\transport-orders\types.ts` (:217-229 badge labels)
- Test: `TransportationService.Web\src\features\transport-orders\pages\__tests__\orderPricingLines.test.tsx`

**Interfaces:**
- Consumes: `OrderPricingLineDto.unit`, `.serviceOptionId`, `.informational` (Task 5), `.quantity`, `.unitPrice`.

- [ ] **Step 1: Failing tests:**
  - table headers exactly: Omschrijving, Type, Berekening, Bedrag, Acties;
  - an Auto line with `serviceOptionId` shows badge `DIENST`; without → `AUTO`; `AutoAdjusted` → `OVERRIDE`; `Manual` → `MANUEEL`; `Proposed` → `VOORSTEL`;
  - a line with quantity=3, unit='COLLI', unitPrice=1.25 shows Berekening `3 COLLI × € 1,25`; a Manual line with only amount shows `Vast bedrag`; an Auto line without quantity/unitPrice shows `—`;
  - an `informational: true` line does NOT render a table row; it renders in a "Niet toegepast" list below the table with its label (which carries the engine reason, e.g. "Pipeline picking: geen Colli op deze order").

- [ ] **Step 2: Run — FAIL.**

- [ ] **Step 3: Implement.**
  - `types.ts` badge map: `AutoAdjusted: 'OVERRIDE'` (was `AANGEPAST`); add a computed helper `lineBadge(line) => line.kind === 'Auto' && line.serviceOptionId ? 'DIENST' : ORDER_PRICE_LINE_KIND_LABELS[line.kind]`.
  - Split lines: `const invoiceLines = order.pricingLines.filter(l => !l.informational)`, `const notApplied = order.pricingLines.filter(l => l.informational)`.
  - Table header adds `<th>Berekening</th>` and names the action column `Acties`.
  - Calculation cell:

```tsx
function calculationLabel(line: OrderPricingLine): string {
  if (line.quantity != null && line.unitPrice != null) {
    const unit = line.unit ? ` ${line.unit}` : ''
    return `${line.quantity.toLocaleString('nl-BE')}${unit} × ${money(line.unitPrice)}`
  }
  return line.kind === 'Manual' ? 'Vast bedrag' : '—'
}
```

  - Below the table:

```tsx
{notApplied.length > 0 && (
  <div className="to-price-not-applied">
    <h3>Niet toegepast</h3>
    <ul>{notApplied.map(l => <li key={l.lineKey ?? l.label}>{l.label}</li>)}</ul>
  </div>
)}
```

  - Footer total unchanged (informational lines are €0 and already excluded from `linesTotal` semantics by the engine).

- [ ] **Step 4: Run** frontend gates — PASS (update any existing assertions on `AANGEPAST`).

- [ ] **Step 5: Commit** — `feat(pricing-ui): calculation column, DIENST/OVERRIDE badges, informational lines out of the invoice table`

---

### Task 8: Services & toeslagen tab — explicit add-flow with badges and note

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Entities\TransportOrderPricing.cs` (`TransportOrderServiceLine` + `public string? Note { get; set; }`)
- Modify: `TransportationService.Api\Modules\Orders\Dtos\TransportOrderDtos.cs` (`OrderServiceInput` + `string? Note = null`; `OrderServiceLineDto` + `string? Note = null`)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (service-line write :1462-1479, read :1113-1117, recalc reconstruction :1887-1893 — carry Note through)
- Migration: `dotnet ef migrations add ServiceLineNote --project TransportationService.Api`
- Modify: `TransportationService.Web\src\features\transport-orders\components\TransportOrderForm.tsx` (:1276-1432)
- Test: backend `Orders\OrderPricingTests.cs`; frontend `transportOrderSectionedForm.test.tsx`

**Interfaces:**
- Consumes: existing `availableServiceOptions` (:375-377), per-kind quantity inputs (:1353-1426), engine informational lines.
- Produces: tab structure with three groups + "+ Dienst of toeslag toevoegen" flow; `OrderServiceInput.Note` persisted on `TransportOrderServiceLine` and surviving recalculation.

- [ ] **Step 1: Backend failing test** (`OrderPricingTests.cs`, reuse its service-option harness):

```csharp
[Fact]
public async Task ManualService_Note_PersistsAndSurvivesRecalculation()
{
    // create order with Services = [new OrderServiceInput(pickingId, Quantity: 3, Note: "Afgesproken met klant")]
    // assert: service line Note == "Afgesproken met klant"
    // recalculate; assert Note still present
}
```

Run — FAIL. Implement Note on entity/DTO/write/read/recalc + migration. Run — PASS.

- [ ] **Step 2: Frontend failing tests:**
  - tab renders group headings "Automatisch toegepast", "Handmatig geselecteerd", and a button "+ Dienst of toeslag toevoegen";
  - auto-applied rows show badge `AUTO`; manually ticked services show badge `MANUEEL` and a "Verwijderen" control;
  - clicking "+ Dienst of toeslag toevoegen" opens a panel with a service `<select>` (only not-yet-selected available options), a read-only "Berekeningswijze" label per the option's kind (e.g. `Per colli`, `Per dag`, `Per pallet-dag`, `Vast bedrag`, `Percentage`), kind-appropriate quantity inputs (reuse existing PerHour/PerStop/PerDay/PerPalletDay blocks), an optional "Notitie" input, and a live "Prijsindicatie" (unit value × entered quantity from the option/customer price data already loaded);
  - informational not-applied services from the current preview render as "Niet toegepast" with their reason text inside this tab.

- [ ] **Step 3: Implement the tab restructure.** Reorganize :1276-1432 into:
  1. `Automatisch toegepast` — existing read-only rows (:1282-1299) + `<Badge>AUTO</Badge>` per row.
  2. `Handmatig geselecteerd` — currently-ticked services rendered as rows (name, kind label, quantity inputs inline as today, note input, `MANUEEL` badge, "Verwijderen" button that unticks).
  3. `+ Dienst of toeslag toevoegen` — button; when open, a selection panel as tested in Step 2; "Toevoegen" moves the option into the selected set with its quantities/note. Keep the underlying state model (selected service ids + per-service quantity/palletCount/dayCount) and extend it with `note`; payload adds `note` per service.
  4. `Niet toegepast` — filter the live preview's informational service lines (label match `serviceOptionId` or `lineKey` prefix `service:`) and list label text.
  Kind → label map (put next to the tab): `{ Percent: 'Percentage', Fixed: 'Vast bedrag', PerHour: 'Per uur', PerStop: 'Per stop', PerUnit: 'Per eenheid', PerOrderLine: 'Per orderlijn', PerKg: 'Per kg', PerM3: 'Per m³', PerLdm: 'Per laadmeter', PerDay: 'Per dag', PerPalletDay: 'Per pallet-dag' }`.

- [ ] **Step 4: Run** backend + frontend gates — PASS.

- [ ] **Step 5: Commit** — `feat(services): explicit add-flow with badges, notes on service lines, niet-toegepast list`

---

### Task 9: End-to-end — adding a Colli line makes per-unit services apply

**Files:**
- Test only: `TransportationService.Api.Tests\Orders\OrderPricingTests.cs`

- [ ] **Step 1: Write the test** (this is the acceptance proof for spec §7's closing requirement; it should PASS already thanks to Tasks 1-2 — if it fails, fix forward):

```csharp
[Fact]
public async Task AddingColliLine_OnUpdate_MakesPerUnitAutoServiceApply()
{
    // harness with an auto-apply PerUnit service option bound to unit type COLLI (see WarehouseServiceTests PerUnit seeding)
    // 1. create order WITHOUT colli lines → assert pricing contains the informational "geen Colli" €0 line (Informational == true)
    // 2. update the order adding CargoItemInput(..., ExpectedQuantity: 3, QuantityUnitCode: "COLLI")
    // 3. assert: a non-informational service line exists with BillableQuantity == 3 and Amount == 3 × configured value
    // 4. assert: the informational "geen Colli" line is gone
}
```

- [ ] **Step 2: Run; fix forward if red; run full backend suite.**
- [ ] **Step 3: Commit** — `test(pricing): per-unit service applies after adding a colli line on update`

---

### Task 10: Order-level included loading/unloading time overrides — backend

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Entities\TransportOrder.cs` (5 new nullable fields)
- Modify: `TransportationService.Api\Modules\Orders\Dtos\TransportOrderDtos.cs` (Create/Update requests + detail DTO, appended optional params)
- Modify: `TransportationService.Api\Modules\Orders\Services\TransportOrderService.cs` (assign on create/update; audit; `PricingInputsChangedAsync` :1592-1624; build `PriceCalculationRequest` :1307-1320)
- Modify: `TransportationService.Api\Modules\Tarification\Dtos\PricingDtos.cs` (`PriceCalculationRequest` + `IncludedTimeOverrides`)
- Modify: `TransportationService.Api\Modules\Tarification\Services\PricingEngine.cs` (`ComputeExtraTimeLines` :883-928, `AddExtraTimeLine` :932-950)
- Migration: `dotnet ef migrations add OrderIncludedTimeOverrides --project TransportationService.Api`
- Test: `TransportationService.Api.Tests\Orders\OneOffPricingTests.cs` (contract-agreement harness exists there: `ContractAgreement_WithIncludedCombinedTime_...` :601)

**Interfaces:**
- Produces on `TransportOrder`:

```csharp
public int? IncludedLoadingMinutesOverride { get; set; }
public int? IncludedUnloadingMinutesOverride { get; set; }
public decimal? ExtraTimeHourlyRateOverride { get; set; }
public int? ExtraTimeRoundingStepMinutes { get; set; }
public int? ExtraTimeMinimumBillableMinutes { get; set; }
```

- `PriceCalculationRequest` gains `IncludedTimeOverrideInput? IncludedTimeOverrides` where `public sealed record IncludedTimeOverrideInput(int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, decimal? ExtraHourlyRate, int? RoundingStepMinutes, int? MinimumBillableMinutes);`
- Resolution order implemented: **order override → agreement value** (stop-level deferred — recorded limitation). Effective extra minutes per activity: `raw = max(0, actual − effectiveIncluded)`; if `raw > 0`: `rounded = RoundingStepMinutes is { } step and > 0 ? ceil(raw / step) * step : raw`; `billedMinutes = max(rounded, MinimumBillableMinutes ?? 0)`; amount = `Round(billedMinutes / 60m × effectiveRate, 2)`. Proposal (`Proposed: true`) and confirm workflow unchanged.

- [ ] **Step 1: Failing tests** (`OneOffPricingTests.cs`, mirroring the contract-agreement extra-time harness at :601):

```csharp
[Fact] // order override wins over contract included minutes
public async Task OrderOverride_IncludedLoadingMinutes_ReducesOrRemovesExtraTimeProposal()
// contract: 30 min included, actual 50 → proposal for 20 min.
// set IncludedLoadingMinutesOverride = 60 → reprice → NO extra-time proposal.

[Fact]
public async Task OrderOverride_RoundingAndMinimum_ApplyToExtraTime()
// contract: 30 min included, rate €60/h, actual 47 → raw 17 min.
// order: RoundingStep 15, Minimum 30 → ceil(17/15)*15 = 30 → billed 30 min → €30.00 proposal.

[Fact]
public async Task OrderOverride_Reset_ReturnsToContractValues()
// set override, reprice, clear override (null), reprice → proposal equals the original contract-based amount.

[Fact]
public async Task LockedPricing_RejectsIncludedTimeOverrideChange()
// lock pricing, then UpdateAsync changing IncludedLoadingMinutesOverride → DomainValidationException (PricingLockedMessage).

[Fact]
public async Task IncludedTimeOverride_Change_IsAudited_WithOldAndNew()
// update override 30 → 60; assert Updated audit OldValuesJson contains 30 / NewValuesJson contains 60 for the field.
```

- [ ] **Step 2: Run — FAIL** (fields don't exist).

- [ ] **Step 3: Implement.**
  - Entity fields + migration; validation in `ValidateAsync`: all five non-negative when provided (`"Afwijkende laad-/lostijdwaarden mogen niet negatief zijn."`).
  - Create/Update requests + detail DTO: append the five fields as optional params; assign in create (:229 area) and update (:338 area).
  - Audit: include the five fields in the Updated old/new anonymous objects.
  - `PricingInputsChangedAsync`: add the five fields to the compared set.
  - `ApplyPricingAsync` builds `IncludedTimeOverrides` into the `PriceCalculationRequest` when any field is non-null.
  - Engine: in `ComputeExtraTimeLines`, effective included minutes = `request.IncludedTimeOverrides?.IncludedLoadingMinutes ?? agreement.IncludedLoadingMinutes` (same for unloading; combined branch uses agreement combined minus per-activity overrides only when overrides are null — if either per-activity override is set while the agreement is combined-mode, treat the overrides as switching to per-activity mode with the non-overridden activity falling back to 0 included; document this in a code comment as the resolution rule). Effective rate = `overrides?.ExtraHourlyRate ?? agreement.ExtraHourlyRate`. Apply rounding/minimum in `AddExtraTimeLine` per the Interfaces formula.
  - One-off orders (`PricingSource == OneOff`) ignore these overrides (one-off has its own included-time fields) — reject the combination in `ValidateAsync`: `"Laad-/lostijdafwijkingen gelden alleen bij contractprijzen; gebruik de eenmalige prijsvelden."`

- [ ] **Step 4: Run** OneOffPricingTests + full backend suite — green, 0 new warnings.

- [ ] **Step 5: Commit** — `feat(pricing): order-level included-time overrides with rounding step and minimum billable time`

---

### Task 11: Included-time UI — "Laad- en lostijd" section with source/inheritance

**Files:**
- Modify: `TransportationService.Api\Modules\Tarification\Dtos\PricingDtos.cs` + `PricingEngine.cs` (preview result gains effective-included-time info)
- Modify: `TransportationService.Web\src\features\transport-orders\components\TransportOrderForm.tsx` (Services & toeslagen tab, new section)
- Modify: `TransportationService.Web\src\features\transport-orders\types.ts` (form input + detail types + preview type)
- Test: backend `Tarification\PricingEngineTests.cs` (result info); frontend `transportOrderSectionedForm.test.tsx`

**Interfaces:**
- Consumes: Task 10 fields on create/update requests and detail DTO.
- Produces: `PriceCalculationResult` gains `IncludedTimeInfoDto? IncludedTimeInfo` = `record IncludedTimeInfoDto(int? IncludedLoadingMinutes, int? IncludedUnloadingMinutes, int? IncludedCombinedMinutes, decimal? ExtraHourlyRate, string Source)` with `Source` ∈ `"Contract"` | `"Order"` | `"Geen"` — filled from the winning extra-time agreement + applied overrides.

- [ ] **Step 1: Backend failing test** (`PricingEngineTests.cs`): calculate with an agreement carrying included time → `result.IncludedTimeInfo.Source == "Contract"` and minutes match; with order overrides in the request → `Source == "Order"` and override minutes returned. Implement in `FinalizeAsync`/extra-time winner selection (:456-485 knows the winning agreement). Run — PASS.

- [ ] **Step 2: Frontend failing tests:** Services & toeslagen tab renders a "Laad- en lostijd" section showing `Inbegrepen laadtijd: 30 minuten` + `Bron: Klantcontract` (from the preview info) and a button "Afwijken voor deze order"; clicking it reveals inputs (Inbegrepen laadtijd, Inbegrepen lostijd, Uurtarief extra tijd, Afronding (minuten), Minimum extra tijd (minuten)); once any override is set the source line reads `Bron: Afwijking op order` and a "Terugzetten naar contractwaarde" button clears all five to null.

- [ ] **Step 3: Implement** the section at the end of the Services tab content (:1426 area). Five controlled string states seeded from `order?.included…Override` fields; payload maps `'' → null`. Effective display: override value when set, else preview's `includedTimeInfo` values, else `—` with `Bron: Geen contractwaarde`. Disable the whole section when `pricingSource === 'OneOff'` with hint `"Bij een eenmalige prijsafspraak gebruik je de eenmalige laad- en lostijdvelden."`

- [ ] **Step 4: Run** frontend gates + backend suite — PASS.

- [ ] **Step 5: Commit** — `feat(pricing-ui): laad- en lostijd section with effective value, source and order override`

---

### Task 12: Lading aggregation, per-line pallet count, commercial-vs-scanable clarity

**Files:**
- Modify: `TransportationService.Api\Modules\Orders\Entities\CargoItem.cs` (+ `public decimal? PalletCount { get; set; }`), `CargoItemConfiguration.cs`, `TransportOrderDtos.cs` (`CargoItemInput`/`CargoItemDto` + `decimal? PalletCount = null`), `TransportOrderService.cs` (`ApplyCargoInput` + projection)
- Migration: `dotnet ef migrations add CargoPalletCount --project TransportationService.Api`
- Modify: `TransportationService.Web\src\features\transport-orders\pages\TransportOrderDetailPage.tsx` (:412-433 Lading section)
- Modify: `TransportationService.Web\src\features\transport-orders\components\TransportOrderForm.tsx` (:1041-1080 heading/hint, pallet input per line)
- Test: backend `TransportOrderServiceTests.cs` round-trip; frontend detail test

**Interfaces:**
- Consumes: `CargoItemDto.QuantityUnitCode` (Task 1), id-preserving sync (Task 2).
- Produces: detail "Lading" shows per-unit aggregation when cargo lines exist.

- [ ] **Step 1: Backend failing test:** create order with lines `2 EUROPALLET (PalletCount 2)` and `4 COLLI` → detail DTO lines carry `PalletCount`; round-trips through update. Implement entity/DTO/migration/mapping. Run — PASS.

- [ ] **Step 2: Frontend failing test:** detail page with cargoItems `[2 EUROPALLET, 4 COLLI, 1 BOX]` renders in the Lading section a list "2 Europallet", "4 Colli", "1 Box" (unit display via `unitLabel`); with zero cargo items falls back to the current order-level "Aantal" row.

- [ ] **Step 3: Implement frontend.**
  - Detail Lading section (:418-421): when `order.cargoItems.length > 0` replace the single "Aantal" `<dd>` with:

```tsx
<div>
  <dt>Lading</dt>
  <dd>
    <ul className="to-lading-list">
      {aggregateCargo(order.cargoItems).map(({ unit, total }) => (
        <li key={unit}>{total.toLocaleString('nl-BE')} {unit}</li>
      ))}
    </ul>
  </dd>
</div>
```

  with `aggregateCargo` grouping by `unitLabel(item.quantityUnitCode, item.quantityUnit)` (fallback group `'stuks'`) and summing `expectedQuantity`. Keep "Aantal" row only when there are no cargo lines.
  - Form: heading :1043 `"Goederenlijnen (scanbaar)"` → `"Goederenlijnen"` with paragraph hint under it: `"Commerciële hoeveelheden voor inhoud en prijs. Scanbare colli worden bij bevestiging per lijn gegenereerd en zijn een apart begrip."` Button label stays `+ Goederenlijn`. Add optional per-line field `Paletten` (numeric, next to Gewicht/Volume) bound to `palletCount`.
  - `types.ts`: cargo types + `palletCount: number | null`.

- [ ] **Step 4: Run** both suites + lint + build — PASS.

- [ ] **Step 5: Commit** — `feat(orders): lading aggregation per unit, per-line pallet count, commercial vs scanable copy`

---

### Task 13: Full verification & wave report

- [ ] **Step 1:** `dotnet build TransportationService.slnx` → confirm 0 new warnings (compare against the known NU1903 baseline in memory/known-issues).
- [ ] **Step 2:** `dotnet test TransportationService.Api.Tests\TransportationService.Api.Tests.csproj` → all green; record exact counts.
- [ ] **Step 3:** In `TransportationService.Web`: `npm test`, `npm run lint`, `npm run build` → all green; record counts.
- [ ] **Step 4:** `git log --oneline` the wave's commits; verify each task committed separately.
- [ ] **Step 5:** Write the end-of-wave report (root cause per bug, entities/DTOs changed, migrations added — NOT applied, API changes, components changed, validation changes, pricing behavior, audit behavior, exact test results, commits in order, remaining limitations: stop-level included-time override deferred; packages/price lines not soft-deleted on order delete by design; portal cargo shape still unit-code-less).

---

## Self-review notes

- Spec §2 → Tasks 5-6; §3 → Task 3; §4 → Tasks 1-2; §5 → Tasks 2, 4, 12; §6 → Task 4; §7 → Tasks 8-9 + 7 (zero lines); §8 → Tasks 10-11; §9 → Task 7; §10 tests are embedded per task; §11 criteria mapped 1:1 (criterion 4 = Task 3, 5 = Task 1, 18 = Task 10 lock test, 19-20 = Task 13).
- Type consistency: `CargoItemInput.Id` (Task 2) is consumed by `BuildUpdateFrom` (Task 1 helper, updated in Task 2); `OrderPricingLineDto.Unit`/`ServiceOptionId` (Task 5) consumed by Tasks 6-7; `IncludedTimeOverrideInput` (Task 10) consumed by Task 11's preview info.
- Known intentional test change: `Create_WithoutGoodsDescription_Succeeds` (Task 4) — the only green-test exception, mandated by spec §6.
