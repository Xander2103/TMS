# Wave 7 — Distribution Planning Implementation Note

Scope (gap analysis wave list; master spec planning part): planning readiness, zone reuse for
grouping, a TRANSPARENT tour-proposal heuristic (explains, never black-box), multi-day
consideration. The planning center, trips, conflicts and costing all stay — Wave 7 feeds the
planner better candidates; accepting a proposal composes the EXISTING create/assign flows.

## 1. Planning readiness + zone grouping

- `PlanningProposalService.GetProposalsAsync(date)`: orders READY TO PLAN = status Confirmed,
  not on any active trip, orderDate ≤ date+lookahead. Each ready order resolves its DELIVERY
  zone via the same postal-range mechanism the pricing zones use (zone reuse — one zone
  concept in the whole system).
- Response: per zone one proposal (zone, orders with weight/ldm/pallet totals — the Wave-3
  inputs — plus per-order readiness facts) + an "Ongezoneerd" group for orders whose delivery
  has no matching zone (transparent: reason "geen zone voor postcode X"), + overdue flag for
  orders whose date already passed (multi-day: yesterday's leftovers surface FIRST).

## 2. Transparent proposal heuristic

- Deliberately simple and explainable: group per zone per date; within a zone order by
  postal code (route proximity proxy); totals shown so the planner sees capacity at a
  glance. Every exclusion carries its reason (no delivery stop, no date, no zone). No
  black-box optimisation — the heuristic explains itself in the response (`Explanation`
  strings per proposal), per the master spec's "transparent, constraint-explaining" demand.

## 3. Accept → existing flows

- `POST /api/planning/proposals/accept` ({ tripDate, orderIds }) → TripService.CreateAsync +
  AssignOrdersAsync (all existing validation/conflict machinery applies). Returns the trip.

## 4. UI

- "Voorstellen" panel on the planning page: date picker (default today), proposals per zone
  with totals + explanations, per proposal "Maak rit" (accept). Overdue orders badge.

Permissions: trips.* / planning existing codes (no new ones — proposals are a read + the
accept composes rights the planner already needs).

## Phase (single, gated)

Service + endpoints + panel + tests (readiness filter, zone grouping incl. unzoned reason,
overdue-first, accept creates trip with orders).
