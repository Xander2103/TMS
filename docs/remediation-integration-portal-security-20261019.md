# Integration — Customer Portal final + Security final (2026-10-19)

Branch `integration/portal-security-final`, started from `integration/core-warehouse-exceptions`
(d07335d4: Core/Master Data + Warehouse/Inventory + Incidents/Exceptions). Not merged, not deployed.
Evidence and scripts: `~/Documents/TMS-acceptance-evidence/portal-security-integration-20261019`.

## Commit mapping (cherry-picked one by one, oldest → newest)

| Source | Commit | Integrated as |
|---|---|---|
| Portal | 1a4d596a status projection, info-request round trip, visibility rule, issued-document publication | 63703163 |
| Portal | bbd0f8a7 truthful portal status, answer on the order, intake fixes, publish issued documents, POD after completion | 034b58b9 |
| Portal | b1eca524 test: tenant-local default dates | cd753f36 |
| Portal | aca86f3d docs | 600f252c |
| Security | 39fcd169 status-aware load errors | e356a783 |
| Security | a4116d09 route guards | b1d42fd1 |
| Security | 31305ef1 session binding + password change (M2/M3) | d5d65638 |
| Security | c2cfee23 commercial projection (M1) | 2260c481 |
| Security | 387de3e9 DriverTripScope (H1) | 796dc13d |
| Security | b3235f75 Management description (M4) | 41780e4d |
| Security | e8f20f76 portal scanning tile | fc353471 |
| Security | 3846a22e sign out after portal password change | 73cf8f9b |
| Security | dbc58def logout on a dead session (test) | c0f41a7b |
| Security | b446278b order price surfaces | 866876bc |
| Security | 3271fe05 dossier price surfaces | de322920 |
| Security | d100586d test: local day in two web tests | **skipped — identical fix already in cd753f36** |
| Security | ee54fe60 "Niet gevonden" for foreign trip/order | a703f702 |
| Security | 69eb1cb5 docs | cf11da4c |
| Security | 8a34b4e2 test: return-date default | **skipped — identical fix already in cd753f36** |
| Integration | tenant-local redelivery + damage-report dates | c550f80b |

## Conflicts and semantic resolution

- **Textual conflicts (2):** both branches fixed the same nightly midnight tests
  (`newDossierPage`, `issuedItemsTab`, `IssuedItemReturnTests`) with the same helpers
  (`todayLocalIsoDate`, `TenantTimeZone.TodayAsync`). Portal's version was kept; the two Security
  test-only commits became empty and were skipped. One helper, no duplicates.
- **Auto-merged shared files, reviewed semantically:**
  - `AppRoutes.tsx`: Portal's `InternalOnly` wraps the driver shell, and Security's permission
    guard sits inside it. A portal user is bounced to `/klantportaal`; a staff user without
    `driver_workflow.view` gets "Geen toegang" inside the shell.
  - `LoginPage.tsx` (Portal session notice + return target) × Security session invalidation: the
    return target is the router `location` (internal path, never a URL parameter) and passes the
    route guards again; an explicit sign-out (logout, own password change) carries no return target.
  - `TransportOrderService` / `TransportOrdersController`: Portal's workflow changes (KP-01 silent
    back-to-draft, `/klantportaal` links, UTC-normalised stop windows, portal-review state endpoint)
    × Security's price projection. The new staff endpoints return no money; review responses use the
    same redacted order projection.
  - `CustomerPortalOrderDetailPage` (Portal answer-the-question flow) × Security 404 UX: both kept.
  - `TripExecutionPage` (Portal POD after completion) × Security 404 UX: both kept.
- **Dashboard:** Portal's `PortalDashboardService` (invoice facts only with
  `customer_portal.view_invoices`) and Security's staff `DashboardService` (revenue/receivables only
  with `invoices.view`) are separate services. Both test sets are kept, and the live run checked:
  portal basis user gets no invoice facts, staff dashboard API is 403 for portal users, and Dispatcher
  gets zero totals while Boekhouding keeps them.

## Migrations and role templates

- One new migration: `20261018213037_CustomerPortalFinalRemediation` (+ Designer), last in order,
  no duplicate ids; Security has none.
- Fresh PostgreSQL DB: 191 migrations, startup, role templates v40, restart without duplicate grants.
- Upgrade of a d07335d4 database: only `CustomerPortalFinalRemediation` applied, startup, both tenants
  v40, restart without duplicate grants; no pending migrations; `has-pending-model-changes` clean.
- `DefaultRoleUpgrades.CurrentVersion = 40` (no source commit changed default permissions).

## Date fixes (integration scope)

- Redelivery: next working day after `TenantTimeZone.TodayAsync` (was the UTC date).
- Damage report from an incident or an afwijking: `TenantTimeZone.ToLocalDate` of the problem's
  instant (was `DateOnly.FromDateTime` of the UTC instant).
- Tests: summer (+2) and winter (+1) instants where the UTC and tenant dates differ, plus a
  before-midnight control. Red on the previous code, green now.

## Residuals (unchanged here)

- NU1903: `System.Security.Cryptography.Pkcs` 10.0.0 has a known high-severity vulnerability
  (GHSA-846f-f764-76mx); reported on restore/build of `TransportationService.Api` and the test project.
  The same package also carries NU1902 (moderate, GHSA-2fmv-rv2c-59x7). Production Hardening.
- UX-002 / UX-003: Chauffeur and Magazijn still land on `/inbox`; Magazijn's menu still shows
  Planbord/Ritlijst/Live opvolging (its `planning.view` / `operations.view` rights). UX wave.
- Incident-list "overdue" badges still compare with the UTC day (not one of the two named bugs).
- Replaying a logged-out refresh token ends every session of the user (pre-existing H4 reuse policy).
