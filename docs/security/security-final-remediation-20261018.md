# Security / roles / tenant isolation — final remediation (2026-10-18)

Branch `remediation/security-final` on `integration/core-warehouse-exceptions` (d07335d4). Role
templates stay at **v40** (v39 Warehouse, v40 Exceptions): no default-role permission changed.
Evidence and scripts: `~/Documents/TMS-acceptance-evidence/security-final-20261018`.

| Finding | Result |
|---|---|
| SEC-H1 chauffeur ETA/delay of another driver's trip | **Fixed**: one server-side `DriverTripScope` |
| SEC-H2 offboarding leaves login/session active | **Already fixed by Core, re-verified** (no code change) |
| SEC-M1 Dispatcher receives order prices | **Fixed**: permission-based projection (`CommercialVisibility`) |
| SEC-M2 own password change keeps other sessions | **Fixed**: shared `AccountSessionRevocation` |
| SEC-M3 access JWT survives logout | **Fixed**: access token bound to its refresh-token session |
| SEC-M4 Management described as read-only | **Fixed (wording)**: permissions intentional, description corrected |
| SEC-M5 raw error / endless retry on restricted routes | **Fixed**: route guards + status-aware error states |

## Driver ownership (SEC-H1 + same root cause)

`DriverIdentity.ResolveAsync` is the single answer to "which driver is the signed-in user" (the
ACTIVE driver profile linked to the user's employee; it replaced seven private copies).
`DriverTripScope` answers: office permission → any trip of the tenant; otherwise only a trip whose
`DriverId` is the caller's driver. Another driver's trip is indistinguishable from an unknown id (404).
There is one driver per trip (no co-driver model).

| Endpoint | Driver (own) | Driver (other trip) | Office scope |
|---|---|---|---|
| `GET /api/my/trips` | own only | — | — |
| `GET /api/trips/{id}/eta` | 200 | **404** (was 200) | planning.view |
| `POST /api/trips/{id}/delay` | 200 | **404**, before body validation, no mutation (was 200 + write) | planning.edit |
| `GET /api/trips/{id}/execution`, stop history | 200 | 404 (was 403) | planning.edit |
| stop transition / arrive / complete / skip | 200 | 404 (was 403) | planning.edit |
| POD finalize / read / photos / signature | 200 | 404 (was 403/404); scope before body/version checks | pod.correct / pod.view |
| trip scans list / summary / submit | 200 | 404 (was 403); replay only within the same trip | planning.edit / warehouse.view |
| trip packages, readiness, missing | 200 | 404 (was 403) | planning.edit / warehouse.view |
| `GET /api/packages/{id}/events` | own trips' packages | **404** (was 200 for any package) | packages.view / warehouse.view |
| trip exceptions report / list / photos | 200 | 404 (was 403); scope before body validation | exceptions.resolve / exceptions.view |
| `POST /api/my/incidents` | own trip | 400 "niet aan jou toegewezen"; a replay key of someone else's incident reads as absent (was: echoed the other incident) | — |
| `/api/my/activities`, `/api/my/documents` | own | 404 | — |
| driver documents | own active-trip vehicles/trailers, **without leasing contracts** | 404 | fleet_documents.view |
| warehouse station: `POST /api/warehouse/scans`, trace, overview | — | **403** (was reachable through scanning.*) | warehouse.view / warehouse.manage |

Office users who also hold the Chauffeur role keep their office scope (verified with
test.core.h6e = Dispatcher + Chauffeur). A deactivated driver profile resolves to no driver, so
driver-only deactivation ends driver access while the account (other roles) stays.

## Session policy (SEC-M2, SEC-M3) and matrix

Access tokens (60 min) carry `security_stamp` and `session_id` (= refresh-token rotation family).
`AccountStateAuthorizationFilter` (per request, one query) requires the user's current stamp AND a
live refresh token in that family. No token blacklist.

| Event | Access token | Refresh | Other sessions | Verified |
|---|---|---|---|---|
| login | new session | cookie (HttpOnly, SameSite=Strict, `/api/auth`) | — | API |
| refresh | rotated token valid; the pre-rotation token stays valid (same session) until expiry | rotated | — | API |
| reuse of an old refresh token | 401 for every session of the user (stamp rotated) | family revoked | **all end** (pre-existing H4 reuse policy) | API |
| logout | **401 at once** | family revoked (cookie or, without cookie, the access token's session) | stay signed in | API + browser |
| own password change | 401 (sign in again) | all revoked | **all end** | API + browser |
| admin password reset / reset link | 401 | all revoked | all end | existing |
| user deactivated / blocked | 401 | refused | all end; login refused | API |
| employee offboarded (any path) | 401 | refused | all end; login refused | API |
| role removed / added | session stays; rights change on the next request (permissions read per request) | unchanged | — | API |
| refresh family expired (14 d) | 401 | refused | — | API |

## Role matrix (v40, effective permissions in the isolated DB)

`price` = holds a commercial right (see SEC-M1). Klant = portal identity (customer-linked; internal
endpoints refuse it, portal endpoints refuse internal identities).

| Role | orders view/edit | planning view/edit | operations | price | invoices | Peppol send | employees / confidential / medical | fleet finance | warehouse / inventory | driver | users |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Administrator | ✓/✓ | ✓/✓ | ✓ | ✓ | ✓ | ✓ | ✓/✓/✓ | ✓ | ✓/✓ | ✓ | ✓ |
| Management | ✓/– | ✓/– | ✓ | ✓ | ✓ | ✓ | ✓/✓/– | ✓ | ✓/✓ | – | ✓ |
| Planner | ✓/✓ | ✓/✓ | ✓ | ✓ | – | – | ✓/–/– | – | ✓/– | – | – |
| Dispatcher | ✓/– | ✓/✓ | ✓ | **–** | – | – | ✓/–/– | – | ✓/– | (view) | – |
| HR (Personeelsdienst) | – | – | – | – | – | – | ✓/✓/✓ | – | –/✓ | – | – |
| Wagenparkbeheerder | – | ✓/– | – | – | – | – | ✓/–/– | ✓ | – | – | – |
| Boekhouding | ✓/– | – | – | ✓ | ✓ | ✓ | – | ✓ | – | – | – |
| Magazijn | – | ✓/– | ✓ | – | – | – | – | – | ✓/✓ | – | – |
| Chauffeur | – | – | – | – | – | – | – | – | – | ✓ (own) | – |
| Klant | portal only | – | – | – | portal add-on | – | – | – | – | – | – |
| no role | – | – | – | – | – | – | – | – | – | – | – |

The API sweep (`scripts/probes.mjs`, 21 reads + 3 representative writes × 11 users) checked every
response against these effective permissions: no mismatch.

### SEC-M4 — Management is not read-only (intentional)

Management holds Peppol send/configure/retry, dossiers manage/price/reopen/override entity, tariffs
manage/import, orders override/lock/confirm-incomplete price, fleet_finance.manage, trip costs
manage/override, problems.approve_charge, absences.approve, planning.override_restriction and the
messaging/escalation/notification rule management — granted deliberately by v13/v14/v21/v24/v27/v28.
It does NOT hold orders.create/edit, invoices.create/edit/change_status, accounting.manage or
problems.create_redelivery (accepted EX policy). The seeded description said "Leest alles, …"; it now
reads "Leest alles en beslist mee: keurt afwezigheden en doorrekeningen goed, overschrijft
planningsbeperkingen en prijzen, beheert tarieven, dossiers, vlootfinanciën en Peppol-verzending."
The seeder corrects the superseded text on existing tenants only while it is untouched; the user
manual's role table follows.

## Sensitive fields

| Field | Who receives it | Status |
|---|---|---|
| Order/dossier prices, price lines, pricing snapshot, invoice links | price viewers only | **fixed (M1)** |
| Dossier list total / invoice status / invoice-status filter | price viewers only | **fixed (M1)** |
| Dossier timeline pricing events, order timeline invoice events | price viewers only | **fixed (M1)** |
| Customer tariffs (`pricing-config` service options) | price viewers only (units stay for customers.view) | **fixed (M1)** |
| Dashboard invoiced revenue / receivables | invoices.view only | **fixed** (was returned to every dashboard.view holder) |
| Employee IBAN / national register number | employees.view_confidential | already correct |
| Sickness reason / medical | absences.view_medical | already correct |
| Fleet costs (history, leasing, KPIs) | fleet_finance.view | already correct |
| Driver documents: leasing contracts | never to the driver | **fixed** |
| Incident costs / charge amount / charge description | incidents.view/manage holders | **intentional**: incidents.manage authors the impact section and charge proposals (accepted EX policy); the financial *history* stays redacted to approve_charge / invoices.view / audit_logs.view |
| Charge policies (default amounts) | approve_charge or incidents.manage | intentional (prefills the charge proposal) |
| Customer (company) IBAN/BIC | customers.view | LOW residual: company bank data, edit gated by customers.manage_fiscal |
| Internal incident notes | incidents.view | internal role only; portal never |

## Customer isolation (portal)

Customer A (KL-0005, test.transport.klant) vs B (KL-0006, test.accounting.klant): order lists,
invoice lists and message threads are disjoint; cross-id order detail, invoice detail and invoice PDF
are 404; a cross-customer document/POD content download is refused; portal identities get 403 on the
internal order and POD APIs. Browser: customer A opening B's order shows "Niet gevonden" and nothing
of B.

## Tenant isolation

Tenant B (`test-sec-b`, own Administrator + the 12 template roles seeded by the startup sync) vs
tenant A, both directions: customers, orders, dossiers, trips, employees, drivers, vehicles,
incidents, damage reports, invoices, issued-item catalogue, order documents → 404 by id; nine list
endpoints contain no foreign rows; writes referencing foreign customer/driver/order ids are refused;
updating a foreign customer is refused; tenant B's audit log has no tenant-A entries.

## Known residuals (not security findings, unchanged)

- ~~Redelivery date and damage report from an incident still use UTC "today"~~ — fixed in `integration/portal-security-final` (tenant-local day via `TenantTimeZone`).
- Replaying a logged-out refresh token is treated as theft and ends every session (pre-existing H4).
- The planning picker lists ended/absent drivers as `Unavailable` rather than hiding them.
- Dossier "Meer (financieel · relaties · incidenten)" heading keeps "financieel" for non-price viewers.
- The free-redelivery option sets a €0 one-off price under problems.create_redelivery (accepted EX policy).
