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

---
---

# Fix round 1 — response to the task review

Commit `9a0ec73` on `worktree-agent-a6d0ccc14e7d087c5` (on top of `bec68f5`).
Review source: `task-1-review.md` (Approved with required follow-ups; 4 Important, 8 Minor).

Every fix below was verified the same way: the new test was run against the *old* behaviour first
and observed to fail, then against the fix and observed to pass. Where that required temporarily
reverting production code, the revert was undone immediately and the suite re-run.

## I-2 (release-blocking) — the C-02 UI lock also fired on create-from-template

**Confirmed, and the review's diagnosis is exactly right.** `NewTransportOrderPage.tsx:71` renders
`<TransportOrderForm order={template ?? undefined} …>` — on the **create** page, `order` carries the
order the new one is based on ("nieuwe opdracht op basis van deze"). `GeneralSection`'s
`Boolean(order)` therefore greyed out the customer and entity selects on a brand-new order and told
the planner to use "Klant wijzigen", a flow that does not exist for an order that has not been
created yet. Duplicating an order for a different customer — the main reason the template flow
exists — became impossible without re-keying from blank.

**Fix.** An explicit `mode: 'create' | 'edit'` prop, threaded page → `TransportOrderForm` →
`GeneralSection`, and the lock now reads `mode === 'edit'`. I made it **required, not defaulted**,
so TypeScript is the enforcement: `npx tsc -b` immediately failed on all three existing test call
sites, which is precisely the signal a default would have swallowed. `order` keeps its dual meaning
(order under edit / template) and both are documented on the prop.

**Scope.** This necessarily went beyond the brief's FE scope, as the reviewer predicted: the lock
cannot be driven off `GeneralSection`'s own inputs, because `order` is the very signal that is
ambiguous. Files touched: `TransportOrderForm.tsx`, `NewTransportOrderPage.tsx` (`mode="create"`),
`TransportOrderDetailPage.tsx` (`mode="edit"`), `GeneralSection.tsx`, plus `mode` added to the three
pre-existing form tests. No behaviour other than the lock reads `mode`.

**Test.** `generalSectionCommercialLock.test.tsx` now has four cases; the decisive new one is
`leaves customer and entity editable when creating from a template` (`mode="create"` **with** an
`order`). *Verified against the old logic:* temporarily restoring `Boolean(order)` fails exactly
that one test and no other — the old test suite could not have caught this, because it only
distinguished "no `order`" from "`order`", which is the conflation at fault.

## I-1 — the C-04 "reflection test" did not test map totality

**Confirmed.** With `TryGetValue` on both readers, the behavioural loop passed whether or not the
map had an `Invoiced` entry, while its docstring claimed to guard totality.

**Fix.** Split into two tests. `Transitions_HaveAnEntryForEveryTransportOrderStatusMember` reaches
the `private static readonly` map by reflection and asserts `ContainsKey` for every enum member,
with a failure message naming the missing statuses and telling the next author what to do
(`use [] for a status that is terminal in the manual workflow`).
`GetById_InEveryStatus_ReturnsADetail` keeps the behavioural loop, now honestly described as a
read-path guard rather than a totality guard.

*Verified:* deleting `[TransportOrderStatus.Invoiced] = []` makes the new test fail with
`TransportOrderService.Transitions has no entry for: Invoiced` while the other three tests in the
file stay green — demonstrating both that the new test bites and that the old one never did.

The failure the review predicted is the real motivation and is now covered: a future
`TransportOrderStatus.Disputed` without a map entry would leave `MapDetailAsync` safe but
`ChangeStatusAsync` silently refusing **every** transition out of it — a dead-end status that reads
as a permission or data problem.

## I-3 — the Draft/Submitted pin-release branch had no test

**Confirmed** — the only code in this change that writes to `Modules/Packages` rows was unexercised.

**Fix.** `Update_DraftOrder_RemovingAPinnedStop_ReleasesThePackagePins`: a Draft order, real
packages via `PackageGenerationService`, then the pinned unloading stop is dropped in favour of a
new one. Asserts 200, the dropped stop is soft-deleted, `DeliveryStopId` is now `null` on every
package, **and** — the part that catches a one-sided release bug — the pin to the *preserved*
loading stop is untouched. The docstring cross-references the Confirmed counterpart
(`Update_RemovingAStopWithPackagesOnAConfirmedOrder_IsRefused`) so the two halves of the rule read
as a pair.

## I-4 — refusals returned after the header had already been mutated

**Confirmed, and worth fixing even though it is latent.** The stop guards ran at `:738`, ~20 header
assignments after `:698`. `AuditService.RecordAsync` calls `SaveChangesAsync` unconditionally, so
any audit write later in the same DI scope would have flushed a header edit that was refused and
never version-stamped. Nothing flushes it *today*, but C-01 made the late return far more reachable
(every attempt to drop a referenced stop hits it).

**Fix — split validate from apply.** `SyncStopsAsync` became:

- `PlanStopSyncAsync` — read-only. Matches inputs to existing rows, computes removals/retypes, loads
  the references, applies **all** the refusal rules, and returns either an error or a
  `StopSyncPlan` record (matched array, removals, package pins to release). Called next to the other
  guards, as the last member of the fail-before-mutate block, so error precedence for every
  existing case is unchanged (`ConfirmationError` still wins where it did before).
- `ApplyStopSyncAsync` — mutating, and by construction can no longer refuse anything: releases the
  planned pins, updates preserved stops in place, creates new ones, renumbers, resolves snapshots,
  stages removals.

`UpdateAsync` is now fail-before-mutate end to end, matching what the C-02 guards already did.

**Test.** `Update_RefusedStopRemoval_LeavesHeaderVersionAndStopsUntouched` sends a refused stop plan
*together with* four header edits, then — the important part — calls `SaveChangesAsync()` on the
test context to **force a flush of anything left in the tracker**, and only then reloads and asserts
`Version`, `UpdatedAt`, `CustomerReference`, `Notes`, `GoodsDescription`, `AdrRequired` and the full
stop projection are byte-identical. *Verified against the old ordering:* moving the guard back below
the header assignments makes this test fail on `Assert.Equal() Failure: Values differ`.

## Minor findings

- **M-4 / duplicate echoed id — behaviour CHANGED, not just tested.** The review noted the
  "second occurrence becomes a new stop" rule was untested; the coordinator ruled it should be a
  400. I agree with the coordinator and changed it: a client echoing one id twice cannot mean one
  row twice, so guessing at identity is exactly what this blocker exists to stop. Now refused with
  `"Stop {n} komt meermaals voor in deze aanvraag; elke stop mag maar één keer worden meegestuurd."`
  Test: `Update_WithADuplicateEchoedStopId_IsRefused` (400 + DB unchanged).
- **M-2 / foreign *tenant* id.** `Update_WithAStopIdOfAnotherTenant_TreatsItAsNew_AndNeverTouchesThatTenant`
  seeds a real second tenant with its own customer, order and stop, echoes that stop id, and asserts
  the edit succeeds with a *freshly generated* id while the other tenant's row keeps its tenant,
  parent order, city and `IsDeleted = false`. The order-level case is kept as well.
- **M-3 / reorder.** `Update_ReorderingEchoedStops_KeepsIdsAndRenumbersSequences` swaps two unloading
  stops and asserts ids survive, sequences follow the new request order, and (via
  `IgnoreQueryFilters`) that there are exactly three rows with none soft-deleted — no phantom rows,
  no transient-duplicate-sequence fallout.
- **M-5 / vestigial parameter.** `BuildStopsAsync` lost its `previousStops` parameter (its only
  caller is `CreateAsync`, which always passed null) and its doc comment now states plainly that it
  is the CREATE path. `BuildStops` and `ResolveStopSnapshotsAsync` stay shared.
- **M-6 / warnings.** Both `xUnit2031` warnings fixed with the `Assert.Single(collection, predicate)`
  overload. The build is back to the 4 pre-existing warnings, and this time I verified the count
  rather than asserting it — the earlier report's claim was wrong and the review was right to
  catch it.
- **M-7 / stale name.** `Update_EchoedStopId_CarriesSnapshotOverDespiteRebuild` →
  `Update_EchoedStopId_KeepsTheSnapshotOfThePreservedStop`, with a comment recording that the
  invariant changed (no rebuild) while the carry-over rule it guards did not.
- **M-1 / retype-vs-delete asymmetry — NOT changed, deliberately.** On a Draft order a pinned stop
  cannot be retyped but can be deleted and re-added as the other type. I left it: relaxing retype to
  release pins would add a second, subtler pin-release path for a marginal convenience, and the
  strict rule fails safe. Recorded here rather than silently kept.
- **M-8 / execution-plan fields — noted, not fixed (pre-existing).** `ApplyStopInput` writes
  `ConfirmedFrom/To`, `EarliestAllowed/LatestAllowed`, `AppointmentRequired` and
  `AppointmentReference` from the request, and `orderFormPayload.ts` does not send them — so a
  header edit still wipes a window confirmed via `UpdateStopExecutionPlanAsync`. Byte-identical to
  the pre-fix `BuildStops`, so not a regression, but now that stop identity survives an edit this is
  the next-most-visible way an ordinary edit destroys operational data. **Wave 2 candidate.**
- **Data repair — still NOT done, by design.** Orders edited before this fix keep pins, executions
  and PODs pointing at soft-deleted stops; per the review's severity note their scans stay blocked
  (`PackageScanProcessor` refuses a mismatched pin) and their labels stay address-less
  (`PackageLabelService` yields null with no fallback) until a backfill runs. **Task 5 will
  evaluate.**

## Files changed in this round

Backend: `Modules/Orders/Services/TransportOrderService.cs` (I-4 split, M-4 refusal, M-5 parameter).
Frontend: `components/TransportOrderForm.tsx`, `components/sections/GeneralSection.tsx`,
`pages/NewTransportOrderPage.tsx`, `pages/TransportOrderDetailPage.tsx`.
Tests: `Orders/OrderInvoicedStatusTests.cs`, `Orders/OrderUpdateIntegrityTests.cs`,
`Orders/StopSnapshotTests.cs` (rename only), and `mode` added to
`transportOrderFormDisclosure.test.tsx`, `transportOrderSectionedForm.test.tsx`,
`transportOrderStopSnapshot.test.tsx`, `generalSectionCommercialLock.test.tsx`.

No schema change, no new permission, no migration. Tenant isolation, `[RequirePermission]`
attributes, financial snapshots and every `RecordAsync` remain as reviewed.

## Test output (fix round 1)

```
$ dotnet build TransportationService.slnx
Build succeeded.   0 errors, 4 warnings (all pre-existing; my 2 xUnit2031 are gone)

$ dotnet test TransportationService.Api.Tests --no-build --filter FullyQualifiedName~Orders
Passed!  - Failed: 0, Passed: 261, Skipped: 0, Total: 261   (was 255; +6 new)

$ dotnet test TransportationService.Api.Tests --no-build
Passed!  - Failed: 0, Passed: 2302, Skipped: 0, Total: 2302   (was 2296; +6 new)

$ cd TransportationService.Web && npx tsc -b
(exit 0, no output)

$ npx vitest run src/features/transport-orders --testTimeout=30000
Test Files  14 passed (14)
      Tests  114 passed (114)   (was 113; +1 new template case)
```

## Concerns after this round

1. **The `mode` prop touches two pages and the shared form** — beyond the brief's FE scope, but the
   review is right that there is no correct fix inside `GeneralSection` alone. It is compile-enforced
   (required prop), so no call site can silently regress.
2. **M-1's asymmetry is now a documented decision, not an oversight** — if the coordinator prefers
   symmetry, relaxing retype to release pins in Draft/Submitted is a small, contained follow-up.
3. **Vitest's default 5 s timeout is still flaky on this machine** (unchanged from round 1) — all FE
   runs above used `--testTimeout=30000` as instructed. Raising it in `vitest.config.ts` remains a
   wave-level suggestion.
4. **M-8 and the data backfill stay open** and are the two things most likely to be mistaken for
   "C-01 was not fixed" if someone hits them after release.
