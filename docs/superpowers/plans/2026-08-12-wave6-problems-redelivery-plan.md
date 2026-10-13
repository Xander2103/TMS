# Wave 6 — Problems + Responsibility + Redelivery Implementation Note

Scope (gap analysis §8i + wave list; master spec problems part): the existing Incident
(management level, dossier-linked, costs) and ExecutionException (trip level) stay THE two
problem records — Wave 6 adds responsibility, an approval-gated charge decision, linked
redelivery creation and one unified problem view. No new parallel "Problem" entity.

## 1. Responsibility attribution (additive on Incident)

- `Incident.ResponsibleParty` (string enum `Unknown | Customer | Own | Driver | Supplier`,
  default Unknown) + `ResponsibilityNotes`. Editable in the incident form; audited like every
  incident change.

## 2. Charge decision (auto/propose/never + audit)

- `Incident.ChargeDecision` (string `None | Proposed | Approved | Rejected`, default None),
  `ChargeAmount`, `ChargeDescription`, `ChargeDecidedByUserId/At`.
- New permission `problems.approve_charge` (v28: management + boekhouding). Proposing is
  open to incidents.manage; APPROVING/REJECTING needs the new right. Decision rule:
  ResponsibleParty == Customer → propose is the normal path; Own/Driver/Supplier → charge
  stays internal (never invoiced) — the UI says so.
- Approval CREATES a Manual pricing line on the linked order (existing line mechanics:
  Kind=Manual, AdjustReason = "Incident {number}: {description}", amount = ChargeAmount) —
  from there the normal invoice flow picks it up (coverage/readiness untouched semantics).
  Requires the incident to have a linked order whose pricing is not Locked/Invoiced;
  otherwise the approval stays recorded and the workspace (Wave 10) surfaces it as a manual
  invoice line to add. Fully audited (old→new + amount).

## 3. Linked redelivery creation

- `POST /api/incidents/{id}/redelivery` (orders.create): duplicates the linked order as a
  new DRAFT order — same customer/goods/stops (fresh dates), reference "HERLEVERING {orig}",
  linked into the SAME dossier (DossierOrders row; auto-wrap skipped), original packages →
  RedeliveryPlanned where the lifecycle allows. `Incident.LinkedRedeliveryOrderId` recorded;
  the redelivery charge (if the customer caused it) follows §2 on the NEW order.

## 4. Unified problem view

- `GET /api/problems` — one combined list: open incidents + open execution exceptions
  (id, kind, title, severity, status, order/trip/dossier link, occurredAt), permissions
  incidents.view OR the exception views. IncidentsPage gains a "Alles" tab consuming it —
  exceptions link through to their trip, incidents to their detail (unified UX, no new page).

## Phases

1. Schema + responsibility + charge decision + permission v28 + approval flow + tests.
2. Redelivery creation + unified problem list + UI (incident detail: responsibility/charge
   blocks + redelivery button; problems tab) + docs (docs/problems.md) + tests.
