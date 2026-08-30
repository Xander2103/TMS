# Task 1 report — C-04 + C-02 + C-01 (order/domain integrity)

Branch: `worktree-agent-a6d0ccc14e7d087c5` — commit `bec68f5`.

> **Worktree note (read first).** The worktree was created off `main` (commit `919af52`,
> "Enhance README"), which is 347 commits behind and diverged from the branch the wave actually
> targets. `TransportOrderService.cs` there was 671 lines; the brief's line references
> (`:989`, `:2003`, `:1640`) only make sense against `nav-redesign` (`fb7c0fb`, 3558 lines).
> I reset my branch to `nav-redesign` before starting (working tree was clean, nothing lost) and
> did all work on top of `fb7c0fb`. Anyone merging this must merge into `nav-redesign`, not `main`.
>
> **Report location note.** The agent is sandboxed to the worktree and cannot write into the
> shared checkout, so this file lives at
> `.claude/worktrees/agent-a6d0ccc14e7d087c5/.superpowers/sdd/2026-08-30-wave1-production-blockers/task-1-report.md`
> instead of the shared `.superpowers/...` path. Copy it across if the coordinator needs it there.

---

## 1. Validation of each finding against the code

### C-04 — CONFIRMED, exactly as described

- `TransportOrderStatus` (`Modules/Orders/Entities/TransportOrder.cs:6-18`) has 8 members:
  Draft, Confirmed, Planned, InProgress, Completed, **Invoiced**, Cancelled, Submitted.
- `Transitions` (`TransportOrderService.cs:32-43` pre-fix) had 7 entries — **Invoiced missing**.
- `ChangeStatusAsync:989` did `Transitions[order.Status].Contains(target)` (raw indexer).
- `MapDetailAsync:2003` did `stops, cargoItems, Transitions[order.Status],` (raw indexer).
- `CorrectiveTransitions` was already read with `TryGetValue` at `:1062` and `:2005`, so only the
  `Transitions` map was exposed.

**Root cause.** `Invoiced` is written by the invoicing module directly on the entity (the entity's
own doc comment says "not reachable via the manual transition map"), but the two readers of the
map assumed total coverage. `MapDetailAsync` is on the read path of *every* order endpoint that
returns a detail DTO, so the exception is not confined to status changes: a plain
`GET /api/transport-orders/{id}` on any invoiced order throws `KeyNotFoundException` → 500.
`Cancelled` was present only by luck (it was added as a terminal entry when cancelling moved to
its own action).

**Fix.** Added `[TransportOrderStatus.Invoiced] = []` and converted both call sites to
`TryGetValue` (defence in depth — the map entry alone would have sufficed, but the next status
member added by another module would reintroduce the same class of bug).

### C-02 — CONFIRMED, and slightly wider than described

- `UpdateAsync:664` (pre-fix): `order.CustomerId = request.CustomerId;` — an unconditional
  assignment. No reason, no pricing re-evaluation, no dossier guard, no invoice guard, and the
  `Updated` audit entry recorded the new customer as if it were an ordinary field edit.
- `UpdateAsync:698-724` (pre-fix): the legal-entity block *did* carry a `DossiersOverrideEntity`
  permission check and a `LegalEntityChanged` audit entry, but **not** the rest of what
  `ChangeLegalEntityAsync:423-486` does: the `BlockedReason` guard (order sits on a sent/booked
  invoice), the mandatory reason for a deviating entity, the release of draft invoice lines on a
  concept of the old entity, and the `Invoiced → Completed` hand-back when lines are released. So
  the header edit was a strictly weaker path to the same state change.
- `ValidateAsync`'s `enforceCustomerIntake: request.CustomerId != order.CustomerId` shows the
  original author was aware the header edit could switch customers; it gated only the
  blocked/deactivated intake rule, nothing financial.
- Only one caller exists: `TransportOrdersController.Update` (`PUT /api/transport-orders/{id}`,
  `[RequirePermission(OrdersEdit, OrdersManage)]`) — permission unchanged.

**Fix.** Two guards immediately after the status/version checks (mirroring
`DossierService.UpdateAsync:367-383`), both returning `Invalid` (400) with a Dutch message naming
the dedicated flow. The two mutations were then removed rather than left as dead branches.

**Decision — should `UpdateTransportOrderRequest` keep `CustomerId`?**
**Kept, as a must-echo field.** Removing it would be a breaking wire change for EDI/portal/legacy
callers and for the `Version`-less "legacy client" path the DTO explicitly supports, and a
`Guid` positional parameter cannot be dropped without touching every construction site. A
*different* value is never silently ignored — it is refused with a 400 that names the right flow,
which is the behaviour the brief asked for. `LegalEntityId` stays nullable with "null = leave
unchanged" semantics, so a client that never sends it is unaffected.

**Decision — add `CustomerId` to `PricingInputsChangedAsync`? NO — verified unreachable.**
After the guard, the only writers of `TransportOrder.CustomerId` in the codebase are
`CreateAsync` (no snapshot exists yet, so the locked-price branch cannot run) and
`OrderCustomerChangeService.ApplyAsync`, which owns its own pricing invalidation. `UpdateAsync`
no longer assigns it at all, and `ApplyPricingAsync` is only reached from `UpdateAsync`,
`CreateAsync` and the recalculation entry points. Adding `CustomerId` to the change detector
would be dead code that reads as though a bypass still exists. Same reasoning for
`LegalEntityId`, which was never a pricing input.

### C-01 — CONFIRMED, and the blast radius is larger than the brief's list

Pre-fix `UpdateAsync:726-741` removed *every* stop and rebuilt the set via `BuildStops:1640-1643`
with `Id = Guid.NewGuid()`. `AuditingSaveChangesInterceptor.Stamp` converts a `Deleted`
`ISoftDeletable` into `Modified` + `IsDeleted = true`, so the DELETE never reaches the database
and no `OnDelete(SetNull)` FK action ever fires. Everything pinned to a stop id therefore kept
pointing at a row that every consumer's query filter hides.

Entities carrying a stop reference (grepped for `Guid` stop FKs across `Modules/`):

| Entity | Column | Nullable | Effect of the old behaviour |
|---|---|---|---|
| `Planning.StopExecution` | `TransportOrderStopId` | **no** | orphaned execution row, unresolvable |
| `Pod.ProofOfDelivery` | `TransportOrderStopId` | **no** | POD detached from its stop |
| `Eta.StopEta` | `TransportOrderStopId` | **no** | ETA promises orphaned |
| `Scanning.ScanEvent` | `TransportOrderStopId` | yes | scan history detached |
| `Exceptions.ExecutionException` | `TransportOrderStopId` | yes | exception loses its stop |
| `Packages.Package` | `LoadingStopId` / `DeliveryStopId` | yes | pins dangle (label/scan fall back) |
| `Packages.PackageEvent` | `TransportOrderStopId` | yes | package trail loses its stop |
| `Incidents.Incident` | `SourceStopId` | yes | incident loses its source stop |
| `Orders.CargoItem` | `LoadingStopId` / `UnloadingStopId` | yes | already patched by the old `RelinkCargoToReplacedStops` |

Only `CargoItem` had a workaround. The other eight had none — the brief named six of them; I
found `PackageEvent` and `Incident.SourceStopId` as well.

**Root cause.** "Stop identity" was never modelled. `TransportOrderStopInput.Id` existed but was
used *only* to decide whether to carry the location snapshot over (`NeedsFreshSnapshot`); it did
not identify a row. Combined with soft delete, every edit — even one that changed a single
instruction line — silently detached every operational reference on the order.

---

## 2. Stop identity rules implemented

Implemented in the new `TransportOrderService.SyncStopsAsync` (replacing the wholesale rebuild).

**PRESERVED — an input echoing an `Id` that belongs to *this* order is the same stop.**
The entity is updated in place via the extracted `ApplyStopInput` (every client-expressible
field: sequence, type, location, address quintet, the four window pairs, appointment, time
requirement, included-minutes override, reference and the four instruction fields). Its `Id`
survives, so every reference in the table above stays resolvable. Reasoning: this is the only
rule that makes soft delete safe — the row the references point at is the row that keeps
existing.

*Snapshot carry-over is byte-for-byte the previous behaviour.* Before the input is applied I take
a detached copy (`CaptureStopSnapshot`) of exactly the fields `CarryOverSnapshot` reads, then
`ClearLocationSnapshot` puts the row in the state a freshly built one would have been in, then
the unchanged `NeedsFreshSnapshot` / `ApplyLocationSnapshot` / `CarryOverSnapshot` logic runs
(now factored into `ResolveStopSnapshotsAsync`, shared with the create path). The clear step
matters: a stop switching from a master location to a free address must lose its snapshot, which
the old code got for free by building a new row.

**ADDED — an input with no `Id`, or with an `Id` this order does not own, is a new stop.**
It always receives `Guid.NewGuid()`; the client-supplied id is *never* reused. A client sending a
stop id of another order or another tenant therefore gets a new stop and cannot adopt, hijack or
re-parent someone else's row (tested). A duplicate echo of the same id claims it once — the
second occurrence becomes a new stop rather than aliasing the first.

**REMOVED — an existing stop no input echoes is soft-deleted**, *unless* refused below. Legacy
clients that do not echo ids therefore still get the old wholesale replacement, which keeps EDI /
portal / older API callers working; they simply do not get the protection.

**REFUSED — removal of a stop that is still operationally referenced.** I split references into
two classes rather than using one flat rule:

- **Hard references** — `StopExecution`, `ProofOfDelivery`, `StopEta`, `ScanEvent`,
  `ExecutionException`, `PackageEvent`, `Incident.SourceStopId`. Removal is refused **in every
  editable status, including Draft/Submitted**. Reasoning: each of these records something that
  actually happened at that stop; detaching them destroys the operational trail, and the first
  three carry a non-nullable FK, so there is no honest "release the link" option — it would be
  outright corruption. This is *stricter* than the brief's "unless the order is Draft/Submitted",
  and deliberately so: if an execution or POD exists, the order is physically bound whatever its
  status column says.
- **Package pins** (`Package.LoadingStopId` / `DeliveryStopId`) — refused when the order is
  `Confirmed` (the brief's rule). While the order is `Draft`/`Submitted` the removal is allowed
  and the pins are actively **released to null**, which is the documented fallback in `Package`'s
  own contract ("a null pin falls back to the order's loading/unloading stops at scan time").
  Reasoning: these are explicitly best-effort links, not event records, and package generation is
  not status-gated so a Draft order can legitimately carry regenerable packages.

Message (Dutch, matching neighbouring ones):
`"Stop {n} is al operationeel in gebruik (colli, uitvoering, aflevering of scans) en kan niet meer worden verwijderd."`

**REFUSED — `StopType` change on a referenced stop.** *Decision, as the brief asked:* a type
change is a replacement in identity terms, so it is refused when the stop carries **any**
reference (hard or package pin), and allowed otherwise. Reasoning: the packages and executions
hanging off a loading stop describe a load; silently turning it into a delivery would keep the
rows attached but make them mean something else — worse than either preserving or replacing.
On an unreferenced stop a retype is an ordinary correction and stays free.
Message: `"Het type van stop {n} kan niet meer worden gewijzigd: er hangen al colli, uitvoeringen of scans aan deze stop."`

**Cargo relinking** — `RelinkCargoToReplacedStops` (which unconditionally overwrote both links)
became `RelinkCargoToSurvivingStops`, which only repairs a **dangling** link: one whose stop was
removed, or (legacy id-less clients) replaced wholesale. A link to a preserved stop is left
exactly as it was, so the "no-op for preserved stops" requirement holds. I also treat a
*role mismatch* as dangling — if an unreferenced stop was retyped, a cargo line still pointing at
it as its loading stop is repaired rather than left semantically wrong.

**Sequence renumbering** creates no phantom rows: the final list is built index-aligned with the
inputs and `Sequence = i + 1` is assigned to the *same* entity, so a reorder is an UPDATE of two
rows, not a delete+insert pair.

**Version bump and audit unchanged.** Removals still go through `_dbContext.RemoveRange`, so the
interceptor still soft-deletes them and still stamps a fresh `Version`; preserved stops now get a
`Version` bump as `Modified` entities (previously the new row carried an initializer token — same
net effect for a client). The `Updated` audit entry keeps its old→new shape including
`StopCount` and the `StopRequirements` summary, and the per-stop `StopSnapshotRefreshed` entries
keep their index alignment with `request.Stops` because the final stop list is still built in
input order.

**One knock-on fix that was mandatory, not optional.** `StopTimeRequirementsChanged` (a pricing-
input detector that refuses edits on a locked price) derived its "before" state from the change
tracker's `Deleted` stop entries — i.e. it silently depended on the delete-and-reinsert bug. With
in-place updates it would have found zero deleted rows and returned `false` for *every* stop
change, letting a locked/invoiced price drift away from the stop time requirements that priced
it. It now reads `OriginalValues` from `Deleted`/`Modified`/`Unchanged` entries and current
values from everything except `Deleted`, with an explicit `before.Count == 0` guard for the
create path.

---

## 3. Files changed

Backend:
- `TransportationService.Api/Modules/Orders/Services/TransportOrderService.cs` — all three fixes.
- `TransportationService.Api/Modules/Orders/Services/ITransportOrderService.cs` — `UpdateAsync`
  doc comment (it said "wholesale stop replacement", now actively wrong).
- `TransportationService.Api/Modules/Orders/Dtos/TransportOrderDtos.cs` —
  `TransportOrderStopInput.Id` doc comment (it said "stops are still wholesale-rebuilt on update").

Frontend:
- `TransportationService.Web/src/features/transport-orders/components/sections/GeneralSection.tsx`
- `TransportationService.Web/src/locales/{nl,fr,en}/transportOrders.json` — two new keys,
  `general.customerLockedHint` and `general.legalEntityLockedHint` (the i18n completeness test
  requires all three locales).

Tests:
- `TransportationService.Api.Tests/Orders/OrderInvoicedStatusTests.cs` (new)
- `TransportationService.Api.Tests/Orders/OrderUpdateIntegrityTests.cs` (new)
- `TransportationService.Api.Tests/Orders/TransportOrderServiceTests.cs` (updated, see below)
- `TransportationService.Web/src/features/transport-orders/components/__tests__/generalSectionCommercialLock.test.tsx` (new)

### Files touched outside the brief's list, with reasons

1. **`TransportationService.Api.Tests/Partners/OrderAndInvoiceEntityGuardTests.cs`** —
   `OrderUpdate_ToNonDefaultEntity_RequiresTheOverrideRight` asserted the exact behaviour C-02
   removes (plain update moves the entity once the override right is held). It could not stay.
   Rewritten to the new invariant: the header edit refuses outright and the DB is unchanged, then
   the same two halves of the original assertion (denied without the right, applied with it) are
   exercised against `ChangeLegalEntityAsync`, so no coverage was lost.
2. **`TransportationService.Api/Modules/Packages/Entities/Package.cs`** — comment only. It said
   "stops are wholesale-replaced on order edits (FK SetNull)", which was both stale and factually
   wrong (soft delete never fires SetNull — that misconception is what made C-01 survive review).
   Left uncorrected it would mislead the next reader into re-introducing the bug.
3. `ITransportOrderService.cs` / `TransportOrderDtos.cs` are inside `Modules/Orders/**` so within
   scope, listed above for completeness.

Per the coordinator's rulings: `orderFormPayload.ts` was **not** touched (it already sends
`id: stop.id` per stop, which is what makes the identity model work end to end), and nothing
under `Modules/Invoicing` was touched.

---

## 4. Tests added

`OrderInvoicedStatusTests` (C-04):
- `Transitions_CoverEveryTransportOrderStatusMember` — walks `Enum.GetValues<TransportOrderStatus>()`,
  forces an order into each status and asserts `GetByIdAsync` returns a detail. Guards the map's
  totality against future enum members.
- `GetById_InvoicedOrder_Returns_Detail_WithEmptyTransitions`
- `ChangeStatus_OnInvoicedOrder_ReturnsInvalidState_NotException`

`OrderUpdateIntegrityTests` (C-02 + C-01):
- `Update_WithDifferentCustomer_IsRefused_AndLeavesTheOrderUntouched` — 400 **and** the DB is
  unchanged (asserts `Notes` was not partially applied).
- `Update_WithDifferentLegalEntity_IsRefused`
- `Update_EchoingTheSameCustomerAndEntity_Succeeds`
- `Update_EchoingStopIds_PreservesStopIdentity_AndEveryReference` — confirms the order, generates
  real packages via `PackageGenerationService`, seeds a `StopExecution`, edits the stops'
  instructions, then asserts the stop ids are unchanged, exactly two live stop rows exist (no
  soft-deleted twins), the package pins still resolve inside the filtered stop set and the
  execution still resolves.
- `Update_AddingAStop_GivesANewIdOnlyToTheAddedStop` (also asserts sequences 1,2,3)
- `Update_RemovingAnUnreferencedStop_SoftDeletesOnlyThatStop`
- `Update_RemovingAStopWithPackagesOnAConfirmedOrder_IsRefused` (400, both stops still live)
- `Update_RemovingAStopWithAStopExecution_IsRefused_EvenInDraft` (the stricter hard-reference rule)
- `Update_ChangingTheTypeOfAReferencedStop_IsRefused`
- `Update_WithAStopIdOfAnotherOrder_TreatsItAsNew_AndNeverAdoptsIt` (adversarial; also asserts the
  other order's stop is untouched)
- `Update_WithoutStopIds_ReplacesStops_AndRelinksPreservedCargo` (legacy path still works)
- `Update_EchoingStopIds_LeavesPreservedCargoLinksUntouched` (relink is a no-op)

`generalSectionCommercialLock.test.tsx` (C-02 UI, 3 cases): selects editable while creating,
disabled + hinted while editing, current values still selected.

### Existing tests updated to the new invariant

- `TransportOrderServiceTests.BuildUpdateFrom` now echoes `Id: s.Id` — this is what a real client
  does (`orderFormPayload.ts` sends it), so the shared helper now exercises the identity path.
- `Update_NullCargoItems_RelinksCargoToReplacedStops` → renamed
  `Update_NullCargoItems_PreservesStopIdentityAndCargoLinks`. The old `Assert.NotEqual` on the
  stop ids (brief's "lines 239-240") is replaced by `Assert.Equal`, plus a new assertion that no
  soft-deleted twin rows were left behind. Carries a comment explaining the invariant change and
  pointing at the test that still covers the legacy replacement + relink path.
- `OrderAndInvoiceEntityGuardTests.OrderUpdate_ToNonDefaultEntity_RequiresTheOverrideRight` — see
  §3 above; carries the same style of explanatory comment.

---

## 5. Test command output (totals)

```
$ dotnet build TransportationService.slnx
Build succeeded.   (0 errors; the 4 pre-existing warnings in the test project are unchanged)

$ dotnet test TransportationService.Api.Tests --no-build --filter FullyQualifiedName~Orders
Passed!  - Failed: 0, Passed: 255, Skipped: 0, Total: 255, Duration: 1 m 3 s

$ dotnet test TransportationService.Api.Tests --no-build
Passed!  - Failed: 0, Passed: 2296, Skipped: 0, Total: 2296, Duration: 9 m 1 s

$ cd TransportationService.Web && npx tsc -b
(exit 0, no output)

$ npx vitest run --testTimeout=30000 src/features/transport-orders src/i18n
Test Files  17 passed (17)
      Tests  196 passed (196)

$ npx vitest run --testTimeout=30000 src/features/transport-orders/components/__tests__/transportOrderSectionedForm.test.tsx
Test Files  1 passed (1)
      Tests  37 passed (37)
```

---

## 6. Concerns

1. **Vitest's default 5 s per-test timeout is flaky on this machine.** `transportOrderSectionedForm.test.tsx`
   failed 0 / 1 / 4 tests across three consecutive default-timeout runs, always with
   `Test timed out in 5000ms`, and passed 37/37 with `--testTimeout=30000`. It is a pre-existing
   environment/perf issue (the run reports 60–120 s of transform time alone), not a regression
   from this task, but CI on a slow agent will be red intermittently. Raising `testTimeout` in
   `vitest.config.ts` would fix it — out of scope here, flagging for the wave.
2. **One `dotnet test` full run aborted mid-suite** ("Test Run Aborted" after 1451 passed, no
   assertion failure, xunit runner stack in the output). The immediately following identical run
   completed 2296/2296. Looks like a test-host crash under memory pressure rather than a real
   defect, but I could not reproduce it to be certain.
3. **Legacy id-less clients get no protection.** A caller that does not echo stop ids still
   replaces the whole stop set, and will now hit the new refusal if any of those stops is
   referenced — i.e. an EDI/portal integration that edits a confirmed order with packages will
   start getting 400s where it previously silently corrupted the references. That is the correct
   trade (silent corruption → loud refusal), but it is a behaviour change for integrations. The
   in-repo web client is unaffected: it already sends stop ids.
4. **Existing production data is not repaired.** Every order edited before this fix still has
   rows pointing at soft-deleted stops. This change stops the bleeding; it does not backfill. A
   data-repair migration (re-point orphaned pins to the surviving stop of the same type and
   sequence, report the ambiguous ones) is a separate piece of work and I did not attempt it — no
   schema change was needed for this task, and a repair would need a product decision about the
   ambiguous cases.
5. **`SyncStopsAsync` issues up to eight small reference queries**, but only when the edit
   actually removes or retypes a stop — the common "edit an instruction" path adds zero queries.
   If that ever shows up in profiling it can be collapsed into one `UNION ALL`, at the cost of
   readability.
6. **The customer-intake gate is now provably dead on the update path.** I passed
   `enforceCustomerIntake: false` explicitly rather than leaving
   `request.CustomerId != order.CustomerId` (which the guard above makes constantly false), so
   the reader is not misled into thinking a customer switch is still possible here.
7. **C-02's UI half is cosmetic by design** — the guard is server-side and adversarially tested.
   The `GeneralSection` change disables the two selects but the existing
   "Klant wijzigen" / "Entiteit wijzigen" affordances live on the detail page, not in the form, so
   a user in the *form* sees the hint text but must leave the form to act on it. Wiring a shortcut
   button into the form would have meant touching files outside my scope; flagging it as a UX
   follow-up for the wave.
