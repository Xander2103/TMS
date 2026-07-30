# Corrections and Expansion Wave 4 Implementation Plan (2026-07-29)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Customer portal, fully configurable notifications/automatic e-mails, Peppol-ready invoicing behind provider-neutral interfaces, EDI screen redesign, compact expandable personnel history, personnel navigation without horizontal scrolling, multiple personnel notes with start-screen alerts, and a real calendar for personnel planning.

**Architecture:** Extend existing subsystems only — the `Modules/CustomerPortal` + `User.CustomerId` portal foundation, the `Modules/Messaging` outbox (idempotency keys, backoff, dev sink provider), the `Modules/Notifications` in-app catalog, the two-layer audit system, the `Modules/Edi` module (stays logistics-only), and the invoice snapshot pattern for immutable Peppol payloads. Peppol becomes a new `Modules/Peppol` with `IPeppolProvider` seam + sandbox adapter; no real network connection is claimed. No parallel systems.

**Tech Stack:** .NET 10 + EF Core 10 + Npgsql (PostgreSQL 16), React 19 + Vite + vitest, xunit + in-memory SQLite harness, QuestPDF (already used for labels/acknowledgements) for the invoice PDF, ClosedXML precedent for exports.

## Global Constraints

- All user-facing labels/messages in Dutch, consistent with existing vocabulary (Historiek, Notities, Meldingen, Sjablonen, Klantafwijkingen, Handelspartners, Boekhouding).
- Tenant filtering explicit per query (`TenantId == _tenantContext.TenantId`); portal endpoints additionally scope by `User.CustomerId` server-side on every request — never trust client-supplied customer ids.
- Migrations additive only; never edit applied migrations; data backfills via guarded SQL inside the migration (runs once by nature).
- New permissions: constant + `All` tuple in `PermissionCodes.cs` + `DefaultRoleDefinitions` + one `DefaultRoleUpgrades` version step per phase + seeder test + `[RequirePermission]` + frontend `hasPermission`. Do not hardcode role names anywhere else.
- Audit via `IAuditService.RecordAsync(entityType, entityId, action, oldObj, newObj, ct)` with purpose-built anonymous objects — never raw entities, never secrets (Peppol provider credentials, tokens).
- Validation via `DomainValidationException(field, message)`, camelCase field paths; RFC 7807 ProblemDetails.
- Outbound e-mail/notification production NEVER inside the main business transaction — enqueue `OutboxMessage` rows (idempotency key) and let `MessageDispatcher` deliver.
- Money `decimal` with explicit `HasPrecision`; enums stored as strings.
- Backend zero new warnings; `dotnet test` green; frontend `npm test`, `npm run lint`, `npm run build` green after every phase.
- Baselines recorded 2026-07-29 (before phase 2): backend 1157 passed / 0 failed; frontend 382 tests (381 passed; `unitTypeMasterEditor.test.tsx` "suggests a code from the name" is flaky under full-suite load only — passes in isolation, pre-existing); lint clean; build OK.
- EDI remains a separate logistics module; Peppol is NOT an EDI trading partner. Shared reuse is limited to patterns (hashing, statuses, retry, audit), not tables.
- Provider-dependent Peppol parts stay behind `IPeppolProvider`; only the sandbox adapter is implemented; a real Access Point adapter is explicitly out of scope until provider choice + credentials exist.

---

## Phase 1 — Repository findings (inventory)

### Auth / tenants / permissions
- JWT bearer only; one `User` entity (`Modules/Identity/Entities/User.cs`) with optional `EmployeeId` **and `CustomerId`** — customer-portal users already exist as `User + CustomerId` + role template `klantportaal` (`customer_portal.view|submit_orders|manage_locations`).
- `Modules/CustomerPortal/Controllers/CustomerPortalController.cs` + `Services/CustomerPortalService.cs` (`/api/customer-portal/*`) derive customer context exclusively from `User.CustomerId` (`PortalOutcomeKind.NoCustomerLink` → 403). `Modules/Portal` (`/api/me`) is the separate **employee** portal.
- Invite/reset flows exist: `UserAccountFlowService` + `UserSecurityToken` (Activation 72h / PasswordReset 2h, SHA-256, single-use, max 3 open reset tokens/hour). No MFA, no framework rate limiting.
- Permissions: `PermissionCodes.cs` (~205 codes, `module.action` snake_case) + `PermissionCatalogSeeder`; role templates in `DefaultRoleDefinitions` (8 templates), upgrades in `DefaultRoleUpgrades`, **CurrentVersion = 16**. Checks via `[RequirePermission(...)]` (any-of, IAsyncActionFilter).
- Audit: interceptor stamping + `AuditService.RecordAsync` (287 call sites). `AuditLog.IpAddress`/`CorrelationId` modelled but never written.
- Tenant resolution: `TenantContextMiddleware`; no global tenant query filters (only `!IsDeleted`); isolation by explicit `.Where` + `TenantIsolation`/`Hardening` test suites; `InvalidTenantReferenceException` → 400.
- Migrations applied manually (`dotnet ef database update`); 106 migrations; tests use in-memory SQLite `EnsureCreated`.

### Messaging / notifications (the outbox already exists)
- `Modules/Messaging`: `OutboxMessage` (Channel Email/Sms, `Kind` from `MessageKinds` (15 consts), Pending/Sent/Failed/Suppressed, AttemptCount, NextAttemptAt, **unique per-tenant IdempotencyKey**, FallbackOfMessageId), `MessagingProfile` (per customer/employee opt-outs, quiet hours, fallback channel), `MessageTemplate` (tenant override per Kind+Channel+Language).
- `MessageOutboxService.QueueAsync` (suppression, quiet hours, template render), `MessageTemplates.cs` (`{{token}}` regex render; Dutch built-ins), `MessageDispatcher` (MaxAttempts 5, backoff 5m×2^n, permanent-failure Critical notification to `orders.manage` holders, fallback channel). Providers: `IEmailProvider`/`ISmsProvider` → `DevelopmentSinkProvider` (writes `App_Data/message-sink`). **No SMTP anywhere.**
- `OutboxDispatcherHostedService` (30s), `ExpiryNotificationHostedService` (6h: `ExpiryNotificationProducer` + `HrReminderProducer`, 7-day dedupe), `CalendarSyncHostedService`.
- In-app: `Modules/Notifications` — `Notification`, `NotificationPreference`, `NotificationTypeCatalog` (24 type codes → Category/Severity; Critical bypasses opt-out), `NotifyAsync/NotifyPermissionHoldersAsync/NotifyRoleAsync/NotifyTenantAsync`; sidebar unread badge (60s poll), `/notifications` page. No dashboard alert feed; no header bell.
- Existing screens: `features/messaging/pages/MessagingPage.tsx` (outbox table + templates + preview), `features/notifications/pages/NotificationsPage.tsx`.

### Customers / invoicing / accounting / Peppol today
- `Customer` already carries `VatNumber`, `CompanyNumber`, `Iban/Bic`, `VatTreatment` (+`VatTreatmentCatalog` with legal texts and `RequiresVatNumber`), `PeppolId`+`PeppolScheme` (validated: scheme 4 digits, id colon-free; gated by `customers.manage_fiscal`), `InvoiceEmail`, `InvoiceLanguageCode`, `PurchaseOrderPolicy` + effective-dated PO numbers, `CustomerCommunicationRule` (Invoice/InvoiceReminder recipient rules — resolver only, no sender). `PeppolSchemeCatalog` (7 EAS codes, `GET /api/customers/peppol-schemes`); `PeppolFieldGroup.tsx` single-field UI (customer only; LegalEntity form still has 2 raw inputs).
- `LegalEntity`: `CompanyNumber`, `VatNumber`, `PeppolId/Scheme`, `Iban/Bic/BankName`, invoice numbering (`InvoiceNumberService`, retry-safe `ClaimAsync`), footer, logo.
- `Invoice`: Draft→Sent→Paid/Cancelled only (no credit notes, no PDF, no sending — "Sent" is a status flip; `Invoice.cs` documents Peppol as clean extension point). Seller snapshots frozen at Send; ledger/sales-category snapshots frozen at Send (`FreezeLedgerSnapshotsAsync`), remediation endpoint + `accounting.manage`. `InvoiceLine` has `Quantity/UnitPrice/VatRatePercent` + ledger snapshot block but **no unit code and no VAT category code**. `InvoiceAttachment` exists (`IncludeWhenSending`).
- Accounting module: `LedgerAccount`, `SalesCategory` (SystemRole Transport/Surcharge/Diesel), health endpoint, XLSX export reading only snapshots.
- QuestPDF precedent: `Modules/Packages/Labels/*`, `IssuedItemAcknowledgementRenderer` → use for invoice PDF.
- Orders: full status model + `TransportOrderStatusHistory` (append-only, interceptor) + timeline service; `TransportOrderDocument` (CMR/delivery notes); `ProofOfDelivery` (versioned, `CustomerVisible`, signature path, photos); `ExecutionException` (`CustomerVisible` flag). Status `Submitted` exists for portal-submitted orders.

### EDI today
- 4 backend files (`Modules/Edi`): `TradingPartner` (Code/Name/CustomerId?/ExternalCustomerIdentifier/MappingProfile "generic-json-v1"), `EdiPartnerLocation`, `EdiMessage` (SHA-256 dedupe, Received/Processed/Failed/DeadLettered/Duplicate, MaxAttempts 3, sync in-request processing, manual replay resets dead-letter budget). All endpoints gated by single `edi.manage`. Partner upsert-by-code only; **no update-by-id/deactivate/customer-link/location-mapping UI**; no delete for location mappings.
- `EdiPage.tsx` (265 lines, flat): bare status select, borrowed `to-stops-table` class, partner `<ul>` with creation form directly beneath, raw-JSON detail modal, "geen klant gekoppeld" badge with API-only remedy. No frontend tests.

### Personnel
- `Employee.Notes` single scalar (max 2000) — the only notes mechanism. Multi-note precedent: `PersonalCalendarNote` (palette-validated colours).
- History: read-time projection over `audit_logs` (`EmployeeHistoryService`, full-snapshot writes, `MaskConfidential`, Dutch `FieldLabels/ActionLabels/ValueLabels`, changed-fields-only, category chips, 8 backend tests). Remaining defects: unmapped keys fall back to raw English; `QualificationTypeId`, `LeaveTypeId`, `VerifiedByUserId`, `DepartmentId` (adjustments), `BalanceTypeId` render raw UUIDs; enums missing labels (`IssuedItemStatus`, `EmployeeDocumentCategory`, `ShiftType`, `DriverAvailabilityStatus` partial); "year" untranslated; **no collapsed/expandable UI** (every card shows the full table); generic raw `AuditHistoryPanel` still used on vehicles/trailers/exceptions (out of wave scope — flag only).
- Detail nav overflow: `Tabs.css .ui-tabs { overflow-x:auto }` (10 page tabs incl. permission-gated `profiel|planning|kwalificaties|documenten|verlofsaldo|afwezigheden|ritten|chauffeursprofiel|bedrijfsmiddelen|historiek`) stacked over `SectionedForm.css .ui-section-nav` (9 section tabs; 5 ids duplicated at both levels). Mobile `<select>` fallback only ≤640px for sections; `.ui-tabs` has none.
- Planning: `/employee-planning` is an employee×day matrix board (keep); the **vertical list per date** is `EmployeePlanningTab` on the employee detail (4-week `<ul>`). Backend `GET /api/employee-planning?from&to` (max 62 days) returns `ScheduleEntryDto` (`sourceType Shift|Absence|Trip|Note`, 13 states, colour, conflict). Month-grid prior art: inline in `PortalPlanningPage.tsx` (week|month|list switch, `.portal-month-cell`, +N overflow) — **not extracted**; `ScheduleChip` + `ScheduleLegend` are shared; date helpers `mondayOf`/`toIsoDate` in `features/employee-planning/types.ts`.
- Leave: `LeaveType`/`LeaveBalanceType`/`Absence` (Requested→UnderReview→Approved/Rejected/Cancelled) + live balance computation.

### Frontend conventions
- React 19, react-router 7 data router, hand-rolled hooks (`usePagedQuery`, `useXMutations` with `run()`), `apiClient` + ProblemDetails field errors, plain CSS + dark-mode tokens in `styles/global.css`, all-Dutch hardcoded copy (no i18n lib), lazy pages via `lazyPage`, nav in `navConfig.ts` (any-of permissions), inline `useAuth().hasPermission`, vitest + testing-library (`vi.mock` of authContextValue), no shared Drawer (feature-local `<aside>`), `UnsavedChangesGuard` (section switches must stay local state).

### Current shortcomings (spec §1 requirement)
1. Portal exists but is minimal (submit/view orders, locations) — no dashboard, documents, invoices, messages, user management, announcements; no rate limiting; no MFA.
2. Notifications: producers hardcode recipients/templates; no per-event admin config, no customer overrides, no placeholder validation, no HTML output, no central screen.
3. EDI screen unusable at scale (flat, creation inline, admin API-only); processing synchronous.
4. Peppol: identifier columns only; no UBL, no transmissions, no provider seam, no incoming docs; invoice lines lack unit code + VAT category; no credit notes; no invoice PDF.
5. Personnel history: raw UUIDs/enums residue; not compact/expandable.
6. Personnel nav: two stacked horizontal scrollers; duplicated levels.
7. Notes: single field, no pinning, no dashboard alert.
8. Planning tab: vertical list, no calendar.

### Migration & security risks
- 4 wave-3 migrations still pending apply on the dev DB; all wave-4 migrations stack additively after them (SQLite tests unaffected). Data backfill (notes conversion) must be inside its migration, guarded, single-run.
- Portal horizontal-escalation risk: every portal endpoint must resolve customer scope from `User.CustomerId`; new tests must attempt cross-customer access on every resource.
- Peppol secrets: provider config values (API keys later) live in configuration/secrets, never in entities, DTOs, audits, or frontend.
- Duplicate sends: transmissions unique per (InvoiceId, active lifecycle); outbox idempotency keys per event+entity.
- Webhook endpoint must be authenticated (shared secret header) + idempotent by provider message id; never trust payload amounts without validation status.
- `.ui-tabs` CSS change affects every consumer (customers, vehicles, inbox, pricing) — must wrap gracefully everywhere, verify via existing tests + snapshots of tab usage.

---

## Phase 2 — Personnel history: compact & expandable

**Files:** `Modules/Employees/Services/EmployeeHistoryService.cs`, `Modules/Employees/Dtos/EmployeeDtos.cs`, `TransportationService.Web/src/features/employees/components/EmployeeHistoryPanel.tsx` (+css), tests both sides.

- [ ] Backend: batch-resolve remaining id fields at read time (legacy rows included): `QualificationTypeId`→type name, `LeaveTypeId`→leave type name, `BalanceTypeId`→balance type name, `DepartmentId`→department name, `VerifiedByUserId`/`DecidedByUserId`→user display name. Single query per type over the page's ids; unknown ids → "Onbekend (verwijderd)".
- [ ] Backend: complete `ValueLabels`: all `IssuedItemStatus`, all `EmployeeDocumentCategory`, `ShiftType`, all `DriverAvailabilityStatus`, `AbsencePartDay`, booleans → Ja/Nee, "year"→"Jaar" in `FieldLabels`. Unmapped keys: humanize instead of raw key (split camelCase) but keep deterministic.
- [ ] Backend: add `Summary` to `EmployeeHistoryEntryDto` — server-built compact Dutch line: single change → "Veldnaam: voor → na"; multi → "N velden gewijzigd (Veld1, Veld2, …)" (first 3); leave adjustments → "Wettelijk verlof 2026: 15 → 25 dagen" style using category+year.
- [ ] Backend tests: uuid resolution (qualification type name shown, no GUID in any Before/After/Summary), enum labels, summary single vs grouped, deleted-lookup fallback; existing 8 history tests stay green.
- [ ] Frontend: collapsed-by-default cards — header row (datum·tijd, ActionLabel, "Door {user}", category Badge) + `Summary` + "Uitklappen"/"Inklappen" toggle (aria-expanded, button); expanded shows existing Veld/Voor/Na table. Newest first (already). Category filter chips (Alles/Profiel/Kwalificaties/Documenten/Bedrijfsmiddelen/Afwezigheden/Verlofsaldo/Chauffeursprofiel) client-side on the loaded page + passed as query param if simple.
- [ ] Frontend tests: collapsed shows summary not table; expand reveals table; filter hides other categories; no raw UUID appears.
- [ ] Verify: `dotnet test` + `npm test`/lint/build. Commit `feat(hr): compact expandable personnel history with resolved labels`.

## Phase 3 — Personnel detail navigation without horizontal scrolling

**Files:** `components/ui/Tabs.css`, `components/ui/SectionedForm.tsx/.css`, `SectionNav.tsx`, `features/employees/pages/EmployeeDetailPage.tsx`, `features/employees/components/employeeSections.ts`, tests.

- [ ] `Tabs.css`: `.ui-tabs { flex-wrap: wrap }` (drop nowrap overflow for desktop; keep compact height); verify other consumers (customers, vehicles, trailers, inbox, pricing) render fine wrapped.
- [ ] Employee page tabs restructure to 8: `profiel` (Overzicht) · `planning` · `kwalificaties` · `documenten` · `verlof` (merged: Verlof & afwezigheden — subcontent: saldo table above absences tab content, each keeping its own permission gates) · `ritten` · `bedrijfsmiddelen` · `historiek`. `chauffeursprofiel` content moves into profile sections as panel section "Chauffeursgegevens" (view + edit). Old deep links `?tab=verlofsaldo|afwezigheden|chauffeursprofiel` map to new tab/section (keep TAB_IDS aliases → redirect via setSearchParams replace).
- [ ] SectionedForm: add `orientation?: 'top' | 'left'` — `left` renders the section nav as a vertical rail (sticky, aria tablist vertical) ≥900px, falling back to existing top nav/select below. Employee profile uses `left`; other consumers unchanged (`top` default).
- [ ] Keyboard: vertical rail arrow-up/down roving tabindex; active state visible; unsaved-change protection unchanged (local state switching).
- [ ] Frontend tests: no `verlofsaldo` tab id remains but alias redirects; wrapped tabs (no overflow style assertion), vertical rail renders sections, chauffeursgegevens section present for drivers; existing employeeSectionedForm/regression tests updated.
- [ ] Verify + commit `feat(hr): employee detail navigation redesign - merged tabs, vertical section rail`.

## Phase 4 — Multiple personnel notes + start-screen alerts

**Backend new:** `Modules/Employees/Entities/EmployeeNote.cs` — `EmployeeNote : AuditableTenantEntity, ISoftDeletable`: `EmployeeId`, `Text` (max 4000), `IsPinnedToDashboard`, nav Employee. Config: table `employee_notes`, index `(TenantId, EmployeeId)`, `(TenantId, IsPinnedToDashboard)` filtered.

- [ ] Entity + configuration + DbSet + migration `EmployeeNotes` **including guarded backfill**: `INSERT INTO employee_notes (...) SELECT ... FROM employees WHERE "Notes" IS NOT NULL AND btrim("Notes") <> ''` (CreatedAt=now, CreatedBy=null, pinned=false). Keep `Employee.Notes` column (legacy, no longer written by UI).
- [ ] Permissions v17: `employee_notes.view`, `employee_notes.manage`, `employee_notes.pin` (constants, catalog, hr/management templates per existing grant style, upgrade step, seeder test).
- [ ] `EmployeeNoteService` + controller `api/employees/{employeeId}/notes` (GET list newest-first, POST, PUT {id}, DELETE {id} soft, POST {id}/pin, POST {id}/unpin). Audit: Created/Updated (before/after text)/Deleted/Pinned/Unpinned. Validation: non-empty text.
- [ ] Dashboard: `DashboardService` gains pinned-notes block (id, employeeId, employee name, text excerpt, pinnedAt/author) returned **only when** the caller has `employee_notes.view` (check via `IPermissionSetService` in controller/service). Frontend `DashboardPage` renders "Aandachtspunten personeel" alert panel above `db-grid` (only when list non-empty); click → `/employees/{id}?tab=profiel`.
- [ ] Frontend profile: replace notes card + form `notities` textarea section with a notes panel (SectionedForm `panel: true` section in edit AND card list on Overzicht): note cards newest-first (author, date, pin badge "Op startscherm"), actions per permission: Bewerken, Verwijderen (ConfirmDialog), Toevoegen aan melding startscherm / Verwijderen van startscherm. Add-note form (textarea + save).
- [ ] Backend tests: CRUD+audit, pin/unpin, migration-equivalent conversion covered via service-level test (existing note text becomes first note — simulate by seeding scalar then running conversion helper used by migration? backfill is SQL-only → assert instead: list endpoint returns converted rows on PG only — cover conversion logic via SQL review + acceptance note), permissions enforced, tenant isolation, dashboard pinned list respects permission.
- [ ] Frontend tests: multiple notes render as cards, delete confirms first, pin toggles badge, dashboard panel shows pinned note and navigates.
- [ ] Verify + commit `feat(hr): multiple employee notes with dashboard pinning (roles v17)`.

## Phase 5 — Personnel planning as a real calendar

**Files:** extract shared calendar from portal: new `src/components/calendar/{MonthGrid.tsx,WeekGrid.tsx,CalendarToolbar.tsx,calendar.css}` (props: `anchor: Date`, `entriesByDate: Map<string, ScheduleEntry[]>`, `renderEntry(entry)`, `onSelectDate`, `onNavigate`, overflow "+N meer"); refactor `PortalPlanningPage` to consume them (no visual regression); rebuild `EmployeePlanningTab` on them.

- [ ] Extract MonthGrid/WeekGrid from `PortalPlanningPage.tsx` (7 cols ma–zo, leading pad cells, today highlight, date headers, `+N meer` overflow, click date → day detail); keep `.portal-*` visuals via new shared classes; `ScheduleChip`/`ScheduleLegend` stay the entry renderer; portal tests stay green.
- [ ] `EmployeePlanningTab`: view switcher Maand | Week | Lijst (default remembered per user via localStorage key `ts.employeePlanning.view`), vandaag/vorige/volgende navigation, fetches only the visible range (month = grid range ≤42 days < 62-day API cap), legend included, requested vs approved leave distinct (existing `LeaveRequested`/`LeaveApproved` states + chip labels), all-day absences render as full-width chips, click entry → detail popover/drawer with authorized actions (open absence → link to absences tab / review dialog when `absences.approve`).
- [ ] Accessibility: grid cells as buttons with aria-labels ("dinsdag 4 augustus, 2 items"), keyboard navigable, current date marked.
- [ ] Frontend tests: month renders correct cells/pad days for a fixed date, entries land on correct dates, requested vs approved distinguishable by label, view preference persisted, range passed to API matches visible grid, week view shows 7 day columns.
- [ ] Verify + commit `feat(hr): month/week calendar for employee planning tab with shared calendar grid`.

## Phase 6 — Notification domain: configurable events, recipients, overrides

**Backend new (extends `Modules/Messaging` + `Modules/Notifications`, no new parallel system):**

- `NotificationEventCatalog` (static, like `NotificationTypeCatalog`): `EventKey`, Dutch label, group (Orders/Facturatie/Personeel/Vloot), allowed placeholder tokens, default channels, supported recipient types, linked `MessageKind`. Events (only where domain hooks exist): order_created, order_submitted_portal, order_accepted, order_rejected, order_info_requested, order_planned, order_pickup_window, order_delivery_window, order_pickup_completed, order_delivery_completed, order_delay_detected, order_failed_delivery, order_damage_registered, order_pod_available, invoice_draft_ready, invoice_sent, invoice_peppol_queued, invoice_peppol_delivered, invoice_peppol_failed, invoice_credit_note, personnel_qualification_expiry, personnel_medical_expiry, personnel_document_expiry, leave_requested, leave_decided, employee_note_pinned, fleet_maintenance_due, fleet_inspection_due, fleet_document_expiry, fleet_damage_created.
- `NotificationRule : AuditableTenantEntity`: `EventKey` (unique per tenant), `Enabled`, `InAppEnabled`, `EmailEnabled` (SMS column reserved, no paid integration), `RecipientsJson` (typed list: CustomerPrimaryContact | CustomerCommunicationRule(type) | InternalPermission(code) | InternalRole(templateCode) | ExplicitEmail(address) | Driver), `AllowCustomerOverride`.
- `CustomerNotificationOverride : AuditableTenantEntity`: `CustomerId`, `EventKey`, `Enabled?` (null = inherit), unique `(TenantId, CustomerId, EventKey)`.
- `NotificationEventService`: single entry `PublishAsync(eventKey, context)` — resolves rule (default from catalog when no row), customer override, recipients, language (recipient preferred → customer default → tenant default), queues `OutboxMessage` per email recipient (idempotency key `{eventKey}:{entityId}:{recipientAddress}`) and `Notification` per in-app recipient. Never called inside a SaveChanges transaction — call sites enqueue after the business save (outbox rows have their own save; acceptable per existing producer pattern).

- [ ] Entities + configs + DbSets + migration `NotificationRules`.
- [ ] Catalog + service + wiring at existing hooks: `TransportOrderService` status transitions + portal submit; `AbsenceService` create/decide; `EmployeeNoteService.Pin`; `InvoiceService.ChangeStatus(Sent)` + draft-ready; expiry producers (qualification/document/fleet) re-routed through `PublishAsync` keeping their dedupe windows; damage report create. Peppol events wired in Phase 12.
- [ ] Template layer: extend `MessageTemplate` with `BodyHtml?` (optional; sanitized on save with allowlist: p, br, strong, em, ul, ol, li, a[href http/https], h1-h3 — strip everything else incl. attributes/scripts); placeholder validation on save & preview: tokens outside the event's allowed list → `DomainValidationException` "Onbekende placeholder {{x}}"; template changes audited (old/new subject+body).
- [ ] Per-customer template override: `MessageTemplate` gains `CustomerId?` (null = tenant default); resolution order customer+lang → customer → tenant+lang → tenant → built-in. Inherited/overridden state must be derivable (dto flag).
- [ ] Permissions v18: `notification_rules.view`, `notification_rules.manage` (+ reuse existing `messaging.manage` for retry, `message_templates.manage` for templates).
- [ ] Backend tests: rule resolution default/disabled/override; recipient resolution each type; language fallback; idempotent double-publish (single outbox row); placeholder rejection; HTML sanitization strips script/onclick; customer template precedence; tenant isolation; existing messaging tests green.
- [ ] Verify + commit `feat(messaging): configurable notification events, recipients and customer overrides (roles v18)`.

## Phase 7 — Notification administration UI

**Files:** new `features/notification-admin/` (pages+api+types+css), nav under Communicatie → "Meldingen en e-mails" (`/settings/notifications`), route + navConfig; fold links from MessagingPage.

- [ ] Endpoints: `GET/PUT api/notification-rules` (list = catalog joined with rules: event, group, enabled, channels, recipients, overridable), `GET/PUT api/customers/{id}/notification-overrides`, extend template endpoints for CustomerId + html + placeholders list (`GET api/message-templates/placeholders?eventKey=`).
- [ ] Page with `Tabs`: **Gebeurtenissen** (DataTable grouped by group: Gebeurtenis, Kanalen (checkboxes in-app/e-mail), Ontvangers summary, Actief switch, edit modal for recipients incl. explicit e-mail add), **Sjablonen** (existing template editor moved/extended: kind+language+customer selector, subject/body/bodyHtml, placeholder chips insert, preview with sample data, unknown-placeholder error surface), **Ontvangers** (explanation + communication-rule links per customer → existing customer communication panel), **Klantafwijkingen** (customer SearchableSelect → per-event inherit/aan/uit + customer templates, "Overgenomen van standaard" vs "Afwijkend" badges), **Verzonden berichten** (outbox filter Status=Sent; correlation link to entity), **Mislukte berichten** (Status=Failed + retry button via existing `/outbox/{id}/retry`, failure reason).
- [ ] Frontend tests: events tab toggles rule (PUT payload), template placeholder chip inserts token, unknown placeholder error shown, failed tab retry calls endpoint, permission-gated (notification_rules.view for page).
- [ ] Verify + commit `feat(messaging): notification & email administration screen`.

## Phase 8 — Customer portal foundation & security

Inspect `CustomerPortalService` first; extend, don't rebuild.

- [ ] Backend: rate limiting via ASP.NET `AddRateLimiter` — fixed-window policy on `/api/auth/login`, `/api/auth/forgot-password`, `/api/auth/reset-password` (e.g. 10/min/IP) + ProblemDetails 429; register once, test with harness where feasible (unit-level policy config assertion).
- [ ] Permissions v19: `customer_portal.view_documents`, `customer_portal.view_invoices`, `customer_portal.messages`, `customer_portal.manage_users` added to catalog + `klantportaal` template gets view_documents/view_invoices/messages by default (manage_users NOT default).
- [ ] Portal user management endpoints (`api/customer-portal/users`, gated `customer_portal.manage_users` + same-customer scope): list (own customer only), invite (creates User with CustomerId=caller's customer, klantportaal role by template code, activation token via `UserAccountFlowService` → **e-mail through outbox** event `portal_user_invited` (add to catalog), never return raw token in response when mail configured — in dev sink mode return token in response like existing admin flow), deactivate/reactivate, resend invite, per-user portal permission toggle (grant/revoke the three optional portal permission roles — implement as per-user extra role assignment using existing Role/UserRole, no new mechanism; portal-assignable roles are the klantportaal-family templates only — never expose internal roles).
- [ ] Internal staff side: customer detail gains "Portaalgebruikers" panel (list/invite/deactivate) reusing same endpoints via admin equivalents (`users.manage` path already exists — link only).
- [ ] Frontend portal shell: `features/customer-portal/` + `CustomerPortalLayout` (like DriverLayout precedent; minimal top nav: Dashboard, Opdrachten, Documenten, Facturen, Berichten, Gebruikers (gated)) routed under `/klantportaal/*`, entered automatically post-login when `user.customerId` set & `customer_portal.view` (redirect from `/`); internal admin screens never rendered in this shell.
- [ ] Security tests (backend): cross-customer access blocked on every existing portal endpoint (foreign order id → 404/403), user without CustomerId → 403, disabled user blocked at login, portal user cannot call internal endpoints (`employees`, `invoices` admin) without permissions, invite cannot target another customer, deactivated portal user's refresh dies.
- [ ] Verify + commit `feat(portal): customer portal foundation - users, invites, rate limiting, shell (roles v19)`.

## Phase 9 — Customer portal modules: dashboard, orders, documents, invoices, messages

- [ ] Order creation (extend existing submit flow): draft state client-side, validation (locations/contacts/quantities/references per customer defaults: default addresses from customer locations, PO policy enforced), submitted status visible, internal accept/reject/request-info actions on `Submitted` orders (order detail internal: Accepteren → Confirmed, Afwijzen → Cancelled with reason, Info opvragen → info-request note + notification event `order_info_requested`); audit all.
- [ ] Portal orders: list w/ filters (status, date, reference), detail: status timeline (reuse timeline service, filtered to customer-visible statuses), stops with planned windows, quantities, references, linked documents (only `CustomerVisible` POD + order documents of customer-visible types), exceptions where `CustomerVisible`.
- [ ] Documents module: list across own orders (delivery notes, CMR, POD) + invoices attachments; downloads re-checked server-side (`customer_portal.view_documents` + ownership).
- [ ] Invoice PDF: `InvoicePdfRenderer` (QuestPDF, legal entity header/logo/footer, lines, VAT breakdown, payment info IBAN/OGM, Dutch labels) + `GET api/invoices/{id}/pdf` (internal `invoices.view`) and portal variant (own customer + `customer_portal.view_invoices`). PDF is presentation only — not the structured Peppol doc.
- [ ] Portal invoices: list (number, date, due date, status incl. Peppol transmission status placeholder field, amount) + detail + PDF download + attachments marked `IncludeWhenSending`.
- [ ] Messages: new `Modules/CustomerPortal/Entities/CustomerMessage.cs` — `CustomerMessage : AuditableTenantEntity`: `CustomerId`, `TransportOrderId?`, `AuthorUserId`, `AuthorIsStaff`, `Body` (max 4000), `AttachmentStorageKey?/FileName/ContentType`; thread = (CustomerId[, OrderId]); read tracking `CustomerMessageRead` (UserId, LastReadAt per thread scope). Endpoints portal + internal (internal gated new v19 perm `customer_messages.view/send` — internal side surfaces on customer detail "Berichten" tab + order detail panel); unread counts both sides; audit sends; notification events (`portal_message_received` internal in-app, `customer_message_received` → customer e-mail per rules).
- [ ] Portal dashboard: active orders count, upcoming deliveries (next 7 days), delayed/problem orders (delay/exception flagged), unread messages, recent invoices, announcements. `PortalAnnouncement : AuditableTenantEntity` (Title, Body, ActiveFrom/Until, IsActive) + admin CRUD (reuse `customer_portal` admin perm? new `portal_announcements.manage` in v19) + portal read endpoint.
- [ ] Frontend pages for all of the above in portal shell (DataTable/Badge/Tabs reuse, Dutch, responsive); internal additions (customer Berichten tab, order Portaal panel with accept/reject/info actions, announcements settings page under Instellingen → Communicatie).
- [ ] Tests backend: ownership scoping per module (foreign customer 404), submitted-order accept/reject/info transitions + audit + notifications queued, message unread counts, announcement windowing, PDF renderer smoke (bytes non-empty, contains invoice number via text extraction skip — assert no exception + non-trivial length), portal invoice list excludes other customers.
- [ ] Tests frontend: portal nav gating per permission, order submit flow validation errors, messages thread render + unread badge, dashboard cards.
- [ ] Verify + commit `feat(portal): customer portal dashboard, orders, documents, invoices and messages`.

## Phase 10 — EDI redesign (separate from Peppol)

- [ ] Backend: permissions v20 `edi.view`, `edi.test`, `edi.retry` (manage keeps full rights; view=read tabs, test=simulate/validate, retry=replay). Existing endpoints re-annotated any-of (`edi.view` for GETs, `edi.retry` for replay, `edi.test` for simulate).
- [ ] Partner admin API: `PUT api/edi/partners/{id}` (name, customerId, externalCustomerIdentifier, mappingProfile, isActive, notes), `DELETE api/edi/partners/{partnerId}/locations/{locationId}`, `GET api/edi/stats` (counts per status, failed, unresolved-mapping count, last-7-days processed), `POST api/edi/validate` (dry-run: parse+map, return validation errors, never create order/message). Audit partner changes.
- [ ] Unresolved-mapping queue: `GET api/edi/messages?status=Failed&mappingIssues=true` — flag derived from `ValidationErrorsJson` containing location/customer mapping errors; expose `mappingIssue: bool` on rows.
- [ ] Frontend rebuild `EdiPage` with `Tabs`: **Dashboard** (stat tiles: verwerkt (7d), mislukt, wacht op mapping, dead-letter; recent messages mini-table), **Berichten** (DataTable: Ontvangen/verzonden, Richting, Partner, Type, Externe referentie, Status Badge, Fout, acties Detail/Opnieuw (edi.retry); FilterBar: richting, status, partner, zoeken), **Handelspartners** (cards/table: code, naam, gekoppelde klant (name or warning), profiel "Generiek JSON", status actief/inactief + test/productie n.v.t. note, locatiemappings count, acties Bewerken/Deactiveren), **Mappings** (per partner: klantkoppeling SearchableSelect, locatiemappings table + add/delete, unresolved queue list linking to message detail), **Testen** (modal/page: partner select, message type (order), payload preview (sample), "Valideren zonder te versturen" → validation result list, "Versturen naar test" → simulate; results panel).
- [ ] Partner creation → Modal (code, naam, klant, externe klantcode, actief) — removed from inline list; explains profile ("Actief profiel: Generiek JSON — partnerspecifieke formaten volgen zodra er een specificatie is").
- [ ] Message detail modal: structured (kop: partner, richting, status, poging N/3, fout; payload `<pre>`; result link to created order; replay button).
- [ ] Tests backend: partner update/deactivate + audit, location mapping delete, stats counts, validate-dry-run creates nothing, permission split (view cannot replay), tenant isolation. Frontend: tabs render per permission, partner modal submits, validate flow shows Dutch errors, messages table filters.
- [ ] Verify + commit `feat(edi): EDI redesign - dashboard, tables, partner management, dry-run validation (roles v20)`.

## Phase 11 — Peppol provider-neutral domain & configuration

**New module `Modules/Peppol/` (Entities/Services/Controllers/Configurations/Dtos):**

- `IPeppolProvider` (`Services/IPeppolProvider.cs`): `ValidateParticipantAsync(scheme, id)` → `PeppolParticipantResult(Found, SupportedDocumentTypes, ProviderReference)`; `SendDocumentAsync(PeppolOutboundRequest)` → `PeppolSubmissionResult(ProviderMessageId, Accepted, Error)`; `GetTransmissionStatusAsync(providerMessageId)` → status; `ValidateDocumentAsync(xml)` → errors; provider descriptor (`Key`, `DisplayName`, `SupportsRegistration`). Implementations: `SandboxPeppolProvider` (deterministic: configurable in-memory outcomes, id ending "999" = not found, marks Delivered after N status polls; used in dev/tests). DI: `IPeppolProviderFactory` resolving by settings `ProviderKey` — only "sandbox" registered.
- `PeppolSettings : AuditableTenantEntity`: `LegalEntityId` (unique per tenant), `Enabled`, `Environment` (Sandbox/Live enum, string-stored), `ProviderKey` ("sandbox"), `InvoiceEmailFallback?`, `DefaultInvoiceNote?`; identifiers/IBAN/address read from `LegalEntity` (single source; missing → validation issue). Secrets: none stored (sandbox has none; real keys later via configuration section `Peppol:Providers:{key}` — options class pattern like `JwtOptions`; document only).
- Customer additions (migration on `customers`): `PeppolEnabled` (bool default false), `PeppolValidationStatus` (Unknown/Found/NotFound, string), `PeppolValidatedAt?`, `PeppolValidationReference?`, `BuyerReference?`, `PeppolDeliveryPreference` (Peppol/EmailFallback/None? default Peppol when enabled).
- `PeppolTransmission : AuditableTenantEntity`: `InvoiceId`, `DocumentKind` (Invoice/CreditNote), `Status` (Draft/Validated/Queued/SubmittedToProvider/AcceptedByProvider/Delivered/Failed/Rejected/Cancelled), `Environment`, `ProviderKey`, `ProviderMessageId?`, `SellerParticipant`, `BuyerParticipant`, `PayloadStorageKey`, `PayloadHash` (SHA-256), `PayloadVersion` (int), `ErrorDetail?` (sanitized), `ResponseCode?`, `RetryCount`, `CorrelationId`, child `PeppolTransmissionEvent` (Status, Timestamp, Detail) append-only. Unique filtered index: one non-terminal transmission per invoice.
- `PeppolIncomingDocument : AuditableTenantEntity`: `DocumentKind` (SupplierInvoice/SupplierCreditNote/StatusMessage), `SupplierParticipant`, `SupplierName?`, `DocumentNumber`, `DocumentDate?`, `Amount?`, `Currency?`, `PayloadStorageKey`, `PayloadHash`, `Status` (Received/NeedsReview/Linked/Rejected), `ProviderMessageId` (unique per tenant — idempotency), `LinkedSupplierNote?` free text (no supplier model exists — review queue only).

- [ ] Entities/configs/DbSets/migrations (`PeppolFoundation`); permissions v21: `peppol.view`, `peppol.configure`, `peppol.validate`, `peppol.send`, `peppol.retry`, `peppol.view_incoming` (boekhouding template gets view/validate/send/retry/view_incoming; configure admin-only default via management? follow accounting.manage precedent).
- [ ] Config endpoints: `GET/PUT api/peppol/settings` (per legal entity; PUT audited without secrets), `GET api/peppol/overview` (config completeness checklist, env, queued/delivered/failed counts, missing-customer-identifier count, received count), `POST api/peppol/settings/test-connection` (provider Validate on own participant).
- [ ] Customer action `POST api/customers/{id}/peppol/verify` → provider ValidateParticipant, stores status/date/reference; customer accounting/fiscal UI: "Peppol-gegevens controleren" button + result (gevonden/niet gevonden, doc types, laatst gecontroleerd, referentie) + new fields (enabled, delivery preference, buyer reference) on the fiscaal section + `PeppolFieldGroup` reused on LegalEntity form (replace 2 raw inputs).
- [ ] Backend tests: settings CRUD+audit; verify action stores result; sandbox provider deterministic behaviors; permission gates; tenant isolation; one-active-transmission index. 
- [ ] Verify + commit `feat(peppol): provider-neutral Peppol domain, sandbox provider, configuration (roles v21)`.

## Phase 12 — Structured invoice generation & validation (UBL BIS 3.0)

- [ ] Invoice domain additions (migration): `InvoiceLine.UnitCode` (string 10, default "C62", UI select from UnitTypes where mapped → UN/ECE rec 20 mapping table in code; service lines default C62, transport line C62, day-based DAY), `InvoiceLine.VatCategoryCode` derived-not-stored? → store at line for snapshot immutability (S/AE/K/G/E/Z), `Invoice.Kind` (Invoice/CreditNote, default Invoice), `Invoice.CreditedInvoiceId?`, `Invoice.PaymentReference?` (structured OGM optional). Credit-note creation endpoint: `POST api/invoices/{id}/credit-note` (copies lines negated→positive amounts with CreditNote kind, links original, Draft; existing numbering series per legal entity — separate `CreditNoteNumberFormat`? reuse invoice series with `CN` prefix config field on LegalEntity: add `CreditNotePrefix?` fallback to InvoicePrefix+"CN").
- [ ] VAT category mapping from `VatTreatment` (single source `VatTreatmentCatalog` extension): DomesticVat→S (rate>0) or Z (0%), ReverseCharge→AE + exemption reason (vatex code + existing Dutch legal text), IntraCommunitySupply→K + reason, ExportOutsideEu→G + reason, VatExempt→E + reason, Other→S custom. Never guess: missing treatment → validation error.
- [ ] `UblDocumentBuilder` (`Modules/Peppol/Services/`): builds UBL 2.1 Invoice/CreditNote XML per BIS Billing 3.0 (CustomizationID `urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0`, ProfileID billing 3.0): supplier/customer party (endpoint scheme+id, legal name, VAT id, address+country), issue/due date, currency, payment means (IBAN/BIC, payment id), buyer reference (Customer.BuyerReference ?? CustomerReference ?? required-check), order reference (PO), lines (qty+unitCode, net price, line extension, item VAT category+percent), allowances/charges (none modelled → document), tax subtotals grouped by category+rate, exemption reason codes/text, legal monetary totals with rounding (2-decimal, sum checks), attachment of PDF optional later. Deterministic output (sorted, invariant culture).
- [ ] `PeppolValidationService`: Dutch error list per spec §5.4 (company config, customer participant, finalized invoice, currency, legal data, VAT validity, payment details, line quantities/units/amounts, totals reconcile, ledger snapshots complete where required, negative invoice → "maak een creditnota", mandatory references per customer policy). Endpoints: `POST api/invoices/{id}/peppol/validate` → issues list; `GET api/invoices/{id}/peppol/preview` (structured summary), `GET api/invoices/{id}/peppol/xml` (admin/test mode, `peppol.validate`).
- [ ] Backend tests (core of this phase): UBL mapping per treatment (S/AE/K/G/E) incl. exemption text; totals reconcile incl. rounding edge (0.005); credit note negation+link; unit codes; buyer/order reference rules; validation catalog — each missing-field rule fires its Dutch message; deterministic XML (two builds byte-equal); ledger snapshot untouched by generation; XSD-shape sanity (well-formed, expected root/namespaces, CustomizationID). 
- [ ] Verify + commit `feat(peppol): UBL BIS 3.0 generation, credit notes, validation catalog`.

## Phase 13 — Peppol transmission lifecycle, screens, webhook & incoming

- [ ] `PeppolTransmissionService`: `QueueAsync(invoiceId)` (validate→build XML→store payload via IFileStorageService category "peppol" + hash→create transmission Queued + event; duplicate guard: active transmission exists → Dutch error; invoice must be Sent status), `PeppolDispatcherHostedService` (outbox pattern: Queued → provider SendDocument → SubmittedToProvider/AcceptedByProvider or Failed w/ backoff MaxAttempts 5; Submitted/Accepted → poll GetTransmissionStatus → Delivered/Rejected; never mark Delivered on local success), `RetryAsync` (`peppol.retry`, only Failed/Rejected, new transmission version reusing payload unless invoice changed → rebuild + version+1), `CancelAsync` (Queued only). Invoice list/detail expose transmission status; portal invoice status field wired (phase 9 placeholder).
- [ ] Notification events wired: invoice_peppol_queued/delivered/failed via Phase 6 `PublishAsync`.
- [ ] Webhook: `POST api/peppol/webhook/{providerKey}` — anonymous route + shared-secret header check (`Peppol:Webhook:Secret` config; 401 otherwise), payload: provider message id + status/incoming doc; idempotent (dedupe by ProviderMessageId + event kind), maps status updates to transmissions, stores incoming documents (duplicate detection by hash+supplier+number → Duplicate ignored), always 200 after persist, processing errors → review queue not 500 (log). Sandbox provider can generate webhook-equivalent callbacks in tests directly against the handler service.
- [ ] Incoming review queue endpoints: list/detail (`peppol.view_incoming`), mark reviewed/rejected, link note.
- [ ] Screens (`features/peppol/`, nav Facturatie group → "Peppol", route `/peppol`, gated `peppol.view`): Tabs **Overzicht** (config state checklist, sandbox/live badge, counts, missing-customer list link), **Uitgaand** (DataTable: factuur, klant, bedrag, datum, omgeving, status Badge (lifecycle labels NL), provider-referentie, fout, acties details/retry), **Inkomend** (leverancier, documentnr, bedrag, ontvangen, status, gekoppeld/niet + review actions), **Configuratie** (per legal entity: bedrijfsgegevens (read-only from LegalEntity + link), provider select (sandbox), omgeving, e-mailfallback, test connection button), **Validatieproblemen** (missing company fields, customers zonder Peppol-ID (list + link), geblokkeerde facturen met redenen).
- [ ] Invoice detail panel "Peppol": validation status/issues (Dutch list per §5.4 example), buttons Valideren, Voorbeeld (structured preview modal), XML downloaden (test/admin), Versturen via Peppol (queue, `peppol.send`), transmission history timeline (events, timestamps, provider refs).
- [ ] Tests backend: queue duplicate-guard; lifecycle transitions incl. never-delivered-on-submit; retry versioning + payload immutability (original storage row untouched, hash stable); webhook idempotency (double post = single transition), bad secret 401; incoming dedupe; sandbox/live separation (env stamped, settings env change doesn't rewrite old rows); notification events queued; tenant isolation; permissions. Frontend: overview checklist renders, uitgaand table + retry gating, invoice panel validate→issues list→send disabled until valid, config test-connection.
- [ ] Verify + commit `feat(peppol): transmission lifecycle, dispatcher, webhook, incoming queue and screens`.

## Phase 14 — Integration review, permissions audit, docs, final report

- [ ] Cross-cutting review: run independent code review (superpowers:requesting-code-review) over the wave; fix Critical/Important findings.
- [ ] Permission matrix doc update (`docs/permission-matrix-operations.md` or new section) + `docs/` new pages: `customer-portal.md`, `notifications.md`, `peppol.md` (incl. **exact steps to connect a real provider**: implement `IPeppolProvider`, register in factory, add `Peppol:Providers:{key}` options + secrets, flip settings ProviderKey/Environment, provider webhook registration), `edi.md` update.
- [ ] Full verification: `dotnet build` (0 warnings), `dotnet test`, `npm test`, `npm run lint`, `npm run build`; record exact counts.
- [ ] Update memory + final report per spec §14.

## Execution notes

- One commit per phase minimum; subagent-driven with per-phase spec+code review.
- Screenshots referenced in the spec were not attached to the session; UX decisions follow the written requirements + existing design system. Flag in final report.
- SMS: architecture already channel-ready (`MessageChannel.Sms`, dev sink); no paid provider work.
