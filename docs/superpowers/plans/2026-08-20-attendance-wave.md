# Time & Attendance Wave — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Volwaardige Time & Attendance-module: in-/uitpunten + pauzes met server-side state machine, kiosk/prikklokmodus met device-auth + PIN, HR live-overzicht, correcties met audit, planning-vs-werkelijk, rapportering/export, driver activity card voorbereid op tachograaf.

**Architecture:** Nieuwe module `Modules/Attendance` (event-gebaseerde, auditbare lifecycle: `AttendanceSession` + immutable `AttendanceEvent`-timeline + `AttendanceBreak` + `AttendanceCorrection`), kiosk als apart securitydomein (device-secret + PIN-credential + interaction token, nooit een ERP-sessie), read-model `DriverDaySummary` dat attendance/planning combineert en tachograaf expliciet als "niet gekoppeld" toont. Frontend `src/features/time-attendance` volgens huisconventies.

**Tech Stack:** .NET 10 / EF Core 10 / Npgsql / PostgreSQL 16, React 19 + Vite + react-router 7, xUnit + SQLite-harness, Vitest + RTL, ClosedXML.

**Spec:** De wave-opdracht in de sessieprompt (94 secties). Dit plan verwijst per taak naar specsecties (§n).

## Global Constraints

- Opslag UTC via `TimeProvider` (`_timeProvider.GetUtcNow().UtcDateTime`); weergave via tenant `DateFormat`/`Timezone`. Geen `DateTime.Now`.
- Alle entiteiten `ITenantOwned` (Phase3-test); indexen TenantId-first; tabelnamen snake_case; enums `HasConversion<string>()`.
- Nooit audit-/soft-delete-velden in services zetten (interceptor).
- Geen plaintext PIN; PBKDF2 (bestaande `PasswordHasher`), keyed lookup-hash voor identificatie; PIN nooit in logs/API-responses.
- Max één actieve sessie per employee en max één open pauze per sessie: **gefilterde unieke indexen** in PostgreSQL + nette conflictafhandeling.
- Nieuwe permissions: const + `PermissionCodes.All`-tuple + `DefaultRoleUpgrades` v30 + `CurrentVersion = 30` + seeder-test; securityregisters Phase1/8/10 bewust bijwerken.
- Alle user-facing teksten Nederlands; frontendlabels via const-maps; datums via `src/utils/dates.ts`.
- Attendance bevat géén rijtijd-/tachograafvelden en géén loonberekening (§31, §82, §83).
- Migraties additief; geen destructieve operaties.

---

## Fase A — Backend domein

### Task A1: Entities + configurations + DbSets + migratie `TimeAndAttendanceFoundation`

**Files:**
- Create: `TransportationService.Api/Modules/Attendance/Entities/AttendanceSession.cs` (incl. `AttendanceSessionStatus { Working, OnBreak, Completed, AutoClosed, Cancelled }`, `AttendanceSource { Web, Kiosk, Mobile, Api, Import }`)
- Create: `.../Entities/AttendanceEvent.cs` (`AttendanceEventType { ClockIn, BreakStarted, BreakEnded, ClockOut, ManualCorrection, AutoClosed, SessionCancelled, ManualSessionCreated }`; immutable: `ITenantOwned, IHasId, IAuditableEntity`, géén soft delete)
- Create: `.../Entities/AttendanceBreak.cs`, `.../Entities/AttendanceCorrection.cs` (`AttendanceCorrectionKind { ClockIn, ClockOut, BreakStart, BreakEnd, SessionCancelled, ManualSession }`)
- Create: `.../Entities/AttendanceCredential.cs` (`AttendanceCredentialType { Pin }`, `SecretHash`, `LookupHash`, `FailedAttemptCount`, `LockedUntilUtc`, `LastUsedAt`, `IsActive`)
- Create: `.../Entities/KioskDevice.cs` (`Name`, `Guid? LocationId`, `IsActive`, `SecretHash`, `LastSeenAt`, `LastPunchAt`)
- Create: `.../Entities/AttendanceSettings.cs` (per-tenant éénrij: `SelfPunchEnabled`, `KioskEnabled`, `PinLength` (4–8, default 4), `ForgottenClockOutAfterHours` (default 16), `AutoCloseEnabled` (default false), `AutoCloseAfterHours` (default 18), `PlannedNotClockedInGraceMinutes` (default 30))
- Create: `.../Configurations/AttendanceConfigurations.cs` — alle `IEntityTypeConfiguration` in één bestand (huispatroon)
- Modify: `TransportationService.Api/Data/TransportationDbContext.cs` (DbSets)
- Migratie: `dotnet ef migrations add TimeAndAttendanceFoundation`

Kernvelden `AttendanceSession : AuditableTenantEntity, IVersionedEntity`: `EmployeeId`, `ClockInAt` (UTC), `ClockOutAt` (UTC?), `Status`, `ClockInSource`/`ClockOutSource`, `Guid? KioskDeviceId`, `Guid? LocationId`, `Version`, `ForgottenClockOutNotifiedAt` (sweep-dedupe-stamp).

Indexen (alle TenantId-first):
- sessions: uniek gefilterd `(TenantId, EmployeeId)` WHERE `"ClockOutAt" IS NULL AND "IsDeleted" = false` (max één actieve sessie, §6); `(TenantId, ClockInAt)`, `(TenantId, EmployeeId, ClockInAt)`, `(TenantId, Status)`
- breaks: uniek gefilterd `(TenantId, SessionId)` WHERE `"EndedAt" IS NULL AND "IsDeleted" = false`; `(TenantId, SessionId)`, `(TenantId, EmployeeId, StartedAt)`
- events: `(TenantId, SessionId, OccurredAt)`, `(TenantId, EmployeeId, OccurredAt)`
- credentials: uniek gefilterd `(TenantId, LookupHash)` WHERE `"IsDeleted" = false`; uniek gefilterd `(TenantId, EmployeeId)` WHERE actieve pin
- kiosk_devices: uniek `(TenantId, Name)` gefilterd op niet-verwijderd
- attendance_settings: uniek `(TenantId)`

FK's: `HasOne<Employee>().WithMany().OnDelete(Restrict)`; `HasOne<Location>().WithMany().OnDelete(SetNull)`; breaks/events → session Cascade.

- [ ] Entities + configurations schrijven; DbSets toevoegen
- [ ] `dotnet build` groen
- [ ] Migratie genereren en nakijken (geen destructieve ops, snapshot consistent)
- [ ] Commit

### Task A2: Permissions v30 + securityregisters

**Files:**
- Modify: `Modules/Identity/PermissionCodes.cs` — consts + tuples (Dutch descriptions): `attendance.self`, `attendance.view`, `attendance.correct`, `attendance.report`, `attendance.manage_kiosks`, `attendance.manage_credentials`, `attendance.manage_settings`
- Modify: `Data/DefaultRoleUpgrades.cs` — step v30, `CurrentVersion = 30`. Grants: `hr` → self, view, correct, report, manage_credentials, manage_settings; `management` → view, report; `chauffeur` → self; overige medewerker-templates (planner, dispatcher, boekhouding, magazijn — exacte codes bij implementatie verifiëren) → self. Dispatcher géén `attendance.view` (bewust, §76).
- Modify: `Data/DefaultRoleDefinitions.cs` — zelfde grants voor verse tenants
- Modify Tests: `Identity/DefaultRoleSeederTests.cs` (Version30-fact, CurrentVersion-pin verplaatsen), Phase10 `ReviewedAuthenticatedOnlyEndpoints` (n.v.t. mits alles attribuut-gegate), Phase1 allowlist (kioskendpoints in Task B2), Phase8 `ServiceSideEnforcedCodes` (alleen indien service-side gates)

- [ ] Codes + upgrades + defs; seeder-tests bijwerken; commit (na controllers kunnen Phase8-checks pas slagen — volgorde bewaken; permission-consts pas committen samen met eerste `[RequirePermission]`-gebruik of registratie)

### Task A3: State machine + calculator + kernservice (punches)

**Files:**
- Create: `Modules/Attendance/Services/AttendanceStateMachine.cs` — statisch, expliciete geldige transities (§5): `CanClockIn(activeSession is null)`, `CanStartBreak(status == Working)`, `CanEndBreak(status == OnBreak)`, `CanClockOut(status is Working or OnBreak)`; uitpunten tijdens pauze sluit de pauze automatisch op hetzelfde tijdstip.
- Create: `Modules/Attendance/Services/AttendanceCalculator.cs` — statisch: `Gross(session, now)`, `BreakTotal(breaks, now)`, `Net`; kalenderdag-splitsing in tenant-timezone (`TimeZoneInfo.FindSystemTimeZoneById(tenantTz)`, fallback Europe/Amsterdam) voor rapportering (§37); nooit negatieve duraties (§38).
- Create: `Modules/Attendance/Services/AttendanceService.cs` + `IAttendanceService` — `ClockInAsync/ClockOutAsync/StartBreakAsync/EndBreakAsync(employeeId, AttendanceSource, kioskDeviceId?, locationId?)` → `AttendancePunchResult(Outcome { Success, AlreadyClockedIn, NotClockedIn, BreakAlreadyActive, NoActiveBreak, EmployeeInactive, SelfPunchDisabled }, dto)`. Elke punch: sessie muteren + `AttendanceEvent` appenden + `SaveChanges` in één transactie; `DbUpdateException` op de unieke index → `AlreadyClockedIn` (concurrency §6). Inactieve/exited employee → geweigerd (§39). `GetStatusAsync(employeeId)` licht (sessie+open pauze, geen event-history, §85); `GetDaySummaryAsync`; `GetHistoryAsync(employeeId, from, to)` (sessies+breaks+correcties+events).
- Tests: `TransportationService.Api.Tests/Attendance/AttendanceStateMachineTests.cs`, `AttendanceCalculatorTests.cs` (overnight, meerdere pauzes, DST-overgang, middernachtsplitsing), `AttendanceServiceTests.cs` (dubbel inpunten geweigerd, uitpunten zonder sessie, pauze zonder sessie/dubbele pauze, uitpunten met open pauze sluit pauze, tenant-isolatie, inactieve employee, TestClock)

- [ ] Failing tests eerst (TDD per gedrag), implementatie, alles groen, commit

### Task A4: Kiosk-securitydomein

**Files:**
- Create: `Modules/Attendance/Security/AttendancePinHasher.cs` — `Hash` via bestaande `IPasswordHasher` (PBKDF2 210k, §42); `ComputeLookupHash(tenantId, pin)` = HMAC-SHA256 met configsleutel `Attendance:PinPepper` (base64 ≥32 bytes); zonder pepper → kiosk-functies fail-closed uitgeschakeld ("Kiosk niet geconfigureerd"). Geen SHA256-als-opslaghash; lookup-hash is keyed en dient alleen voor O(1)-identificatie, verificatie blijft PBKDF2.
- Create: `Modules/Attendance/Security/KioskDeviceAuthenticator.cs` — header `X-Kiosk-Device: {deviceId}.{secret}`; secret = 32 random bytes (base64url), opgeslagen als SHA-256-hash (high-entropy token, patroonconform); `CryptographicOperations.FixedTimeEquals`; disabled/onbekend device → zelfde generieke weigering.
- Create: `Modules/Attendance/Services/KioskInteractionTokenStore.cs` — `IMemoryCache`: token (GUID) → (tenantId, employeeId, deviceId), TTL 45 s, single-use (§44). Gedocumenteerd: in-memory, single-instance deployment.
- Create: `Modules/Attendance/Services/KioskPunchService.cs` — `IdentifyAsync(deviceAuth, pin)`: device valideren → credential via lookup-hash → lockout check (5 fouten → 5 min, backoff) → PBKDF2-verify → generiek `Code ongeldig` bij elke fout (§41, geen enumeration/timing-verschil tussen "geen credential" en "fout PIN": altijd een dummy-verify) → minimal state (voornaam, status, toegestane acties) + interaction token. `PunchAsync(deviceAuth, token, action)`: token consumeren → `IAttendanceService` met `Source = Kiosk` + device/location.
- Create: `Modules/Attendance/Services/KioskDeviceService.cs` — CRUD + provisioning (secret éénmalig tonen, §14/§66), rotate, disable; audit via `IAuditService` (create/disable/rotate).
- Create: `Modules/Attendance/Services/AttendanceCredentialService.cs` — PIN zetten/genereren/reset/disable per employee (`attendance.manage_credentials`); PIN-lengte uit settings; uniek per tenant (lookup-hash-index); response bevat de PIN éénmalig alleen bij genereren; audit.
- Modify: `Modules/Authentication/RateLimitingServiceCollectionExtensions.cs` — policy `KioskPolicy = "kiosk"` (per IP, 15/min, QueueLimit 0).
- Modify: `Modules/Security/StartupSecurityValidator.cs` — pepper-validatie (indien geconfigureerd: geldig base64 ≥32 bytes; niet verplicht voor boot).
- Tests: `Attendance/KioskSecurityTests.cs` — geldig/ongeldig device, disabled device, disabled credential, verkeerde PIN generiek, lockout/backoff, token single-use + expiry, cross-tenant (device tenant A + PIN tenant B onmogelijk), inactieve employee geweigerd, kiosk uit via settings, pepper ontbreekt → fail closed; `AttendanceCredentialServiceTests.cs`.

- [ ] TDD; groen; commit

### Task A5: Correcties + audit

**Files:**
- Create: `Modules/Attendance/Services/AttendanceCorrectionService.cs` + interface — `CorrectSessionAsync(sessionId, request{ClockInAt?, ClockOutAt?, reason}, ...)`, `CorrectBreakAsync`, `CancelSessionAsync(reason)`, `CreateManualSessionAsync(employeeId, clockIn, clockOut, breaks[], reason)`. Elke correctie: verplichte reden (§24), `AttendanceCorrection`-rij (old/new), `ManualCorrection`-event, `IAuditService.RecordAsync("AttendanceSession", …)`; origineel blijft traceerbaar (§10, §67); validaties (chronologie, pauzes binnen sessie, geen overlap met andere sessie van die employee).
- Tests: `Attendance/AttendanceCorrectionServiceTests.cs` — reden verplicht, audit geschreven, old/new bewaard, cancel i.p.v. delete, manual session, validatiefouten, tenant-isolatie.

- [ ] TDD; groen; commit

### Task A6: HR-overzicht, planning-vs-werkelijk, rapporten + export

**Files:**
- Create: `Modules/Attendance/Services/AttendanceOverviewService.cs` — live overzicht (§16/§17): actieve medewerkers + status (Niet ingepunt / Aan het werk / Pauze / Uitgepunt / Mogelijk vergeten uit te punten / Handmatig gecorrigeerd-indicator), sinds, gewerkt, pauze; joins met `Shift` (gepland vandaag) en approved `Absence` (afwezig ≠ anomalie, §50); filters datum/afdeling/status/zoek; anomalie "gepland maar niet ingepunt" na grace (§51).
- Create: `Modules/Attendance/Services/AttendanceReportService.cs` — per employee/dag/week/maand: gross/break/net/gepland/afwijking/ontbrekende punches/correctie-aantal (§34); XML-doccomment met metriekdefinities (huisnorm). Planned minuten uit `Shift` (wall-clock) vs actual (UTC→tenant-tz) — bronnen blijven gescheiden (§19).
- Create: `Modules/Attendance/Services/AttendanceExportService.cs` — ClosedXML, Criteria-sheet, tekstcellen (formule-injectieveilig), `RecordExportAsync(..., Classification.Confidential)` (§35).
- Modify: `Modules/Reporting/ReportCatalog.cs` — attendance-rapport registreren.
- Tests: `Attendance/AttendanceOverviewServiceTests.cs`, `AttendanceReportServiceTests.cs` (incl. overnight-splitsing per kalenderdag), `AttendanceExportServiceTests.cs` (formule-injectie), `Reporting/ReportCatalogTests` blijft groen.

- [ ] TDD; groen; commit

### Task A7: Sweep (vergeten uitpunten + auto-close) + notificaties

**Files:**
- Create: `Modules/Attendance/Services/AttendanceSweepService.cs` — `AttendanceSweepHostedService` (PeriodicTimer 15 min, 45 s delay) + scoped `AttendanceSweepWorker`: per actieve tenant (try/catch per tenant), settings lezen (projectie, null-tolerant); sessies > `ForgottenClockOutAfterHours` → notificatie `attendance_forgotten_clockout` naar employee-user + `NotifyPermissionHoldersAsync("attendance.correct", ...)`, dedupe `attendance_forgotten_clockout:{sessionId}` + one-shot stamp; indien `AutoCloseEnabled` en > `AutoCloseAfterHours`: sessie sluiten met `Status = AutoClosed`, `AutoClosed`-event, audit (§22/§23). Default: alleen waarschuwen.
- Modify: `Modules/Notifications/Services/NotificationService.cs` — `NotificationTypeCatalog.Map` + `attendance_forgotten_clockout` (Hr, Warning).
- Modify: `Program.cs` — DI-registraties hele module + hosted service.
- Tests: `Attendance/AttendanceSweepTests.cs` — detectie, dedupe (geen spam, §48), auto-close alleen indien enabled + audit/event, tenant-isolatie.

- [ ] TDD; groen; commit

### Task A8: Controllers + settings + securitytest-registraties

**Files:**
- Create: `Modules/Attendance/Controllers/MyAttendanceController.cs` — `[Route("api/me/attendance")]`, `[RequirePermission(attendance.self)]`: `GET status`, `POST clock-in|clock-out|break/start|break/end`, `GET history?from&to` (max 92 dagen), `GET day?date=`. Employee-resolutie via `User.EmployeeId` (PortalService-patroon); nooit client-supplied employeeId (§9); geen employee-link → 404 met bestaand bericht.
- Create: `.../Controllers/AttendanceController.cs` — `GET api/attendance/overview?date&departmentId&status&search` (`attendance.view`); `GET api/employees/{employeeId}/attendance?from&to` (`attendance.view`); correcties: `POST api/attendance/sessions/{id}/corrections`, `POST .../cancel`, `POST api/attendance/sessions` (manueel) (`attendance.correct`).
- Create: `.../Controllers/AttendanceReportsController.cs` — `GET api/reports/attendance/{report}` (`attendance.report`).
- Create: `.../Controllers/KioskDevicesController.cs` — `api/attendance/kiosks` CRUD + `POST {id}/rotate-secret` (`attendance.manage_kiosks`).
- Create: `.../Controllers/AttendanceCredentialsController.cs` — `PUT/DELETE api/employees/{employeeId}/attendance-credential` + `POST .../generate` (`attendance.manage_credentials`); response nooit hash/PIN behalve éénmalig gegenereerde PIN.
- Create: `.../Controllers/KioskPunchController.cs` — `[AllowAnonymous]` + `[EnableRateLimiting(KioskPolicy)]`: `POST api/attendance/kiosk/identify`, `POST api/attendance/kiosk/punch`, `GET api/attendance/kiosk/ping`. Device-auth in service; documentatie waarom AllowAnonymous (device-header-auth, §74).
- Create: `.../Controllers/AttendanceSettingsController.cs` — `GET/PUT api/attendance/settings` (`attendance.manage_settings`).
- Create: `.../Dtos/AttendanceDtos.cs` — sealed records.
- Modify Tests: Phase1-allowlist (+3 kioskacties), Phase10 (attribuut-gegate → automatisch geclassificeerd), Phase8 (indien nodig), `docs`-notitie.
- Tests: `Attendance/AttendanceEndpointSecurityTests.cs` — reflectie: elk attendance-endpoint heeft RequirePermission of staat bewust in kiosk-allowlist; kiosk-endpoints geven zonder geldige device-header nooit data.

- [ ] Volledige backend build + testsuite groen; commit

## Fase B — Frontend

### Task B1: Fundament — utils, api, types

**Files:**
- Modify: `src/utils/dates.ts` + tests — `formatTime(iso)` → `"07:54"`, `formatDurationMinutes(min)` → `"7u48"`/`"0u31"` (planning-`formatMinutes`-notatie consolideren)
- Create: `src/features/time-attendance/types.ts` — statussen + `ATTENDANCE_STATUS_LABELS/TONE`, DTO-types
- Create: `src/features/time-attendance/api/timeAttendanceApi.ts` (me-status/punch/history, overview, employee-attendance, corrections, settings, kiosks, credentials) en `api/kioskApi.ts` (rauwe fetch met `X-Kiosk-Device`-header, géén Bearer/apiClient — kiosk is geen ERP-sessie)

### Task B2: Werkstatus-card (dashboard + portal) (§8, §45, §52)

- Create: `components/WorkStatusCard.tsx` + css — drie toestanden (Niet ingepunt / Aan het werk / Pauze), grote primaire actie(s), vandaag gewerkt/pauze, 60 s-poll + refetch na actie; alleen tonen bij `user.employeeId` + `attendance.self`.
- Modify: `src/features/dashboard/pages/DashboardPage.tsx` (prominente panel-slot vóór KPI-groepen) en `src/features/portal/pages/PortalDashboardPage.tsx` (centrale card) + portal-module-launcher "Mijn uren".
- Tests: drie toestanden, acties, foutafhandeling.

### Task B3: "Mijn uren" self-service (§9, §68)

- Create: `pages/MyTimePage.tsx` (route `/portal/time`): vandaag/deze week/deze maand-totalen, sessielijst + timeline (punches, pauzes, correctie-annotaties "↳ gecorrigeerd vanuit … door … reden: …"), gepland vs werkelijk indien beschikbaar.
- Tests: rendering, periodewissel, correctieweergave.

### Task B4: HR-overzicht + employee-detail-tab (§16–§18, §54)

- Create: `pages/AttendanceOverviewPage.tsx` (route `/attendance`, nav Personeel → "Aanwezigheid"): 30 s-poll (OperationsPage-patroon), filters (datum/afdeling/status/zoek), DataTable met statusbadges, drilldown naar employee-tab.
- Create: `components/AttendanceTab.tsx` (`{ employeeId }`): status, periodes, sessies, correctiedialoog (`attendance.correct`: clock-in/out & pauzes bewerken met verplichte reden; sessie annuleren; manuele sessie), PIN-beheer (`attendance.manage_credentials`: genereren/reset/disable, PIN éénmalig tonen).
- Modify: `EmployeeDetailPage.tsx` (TAB_IDS + 'uren' "Urenregistratie") + `employeeDetailNav.test.tsx`-stub.
- Tests: overview-filters, badges, correctiedialoog-validatie, tab-permissies.

### Task B5: Instellingen + prikklokbeheer (§14, §28, §65)

- Create: `pages/AttendanceSettingsPage.tsx` (`/settings/attendance`, model LeaveSettingsPage): policies + prikklokkenlijst (naam/locatie/status/laatste activiteit), aanmaken → provisioning-secret éénmalig tonen, rotate/disable.
- Modify: navConfig (Parameters → Personeel), AppRoutes, commands.
- Tests: pagina + provisioning-flow.

### Task B6: Kiosk (§10–§13, §15, §53, §79)

- Create: `pages/KioskPage.tsx` + `kiosk.css` — route `/kiosk` **zonder enige layout/shell**, buiten `RequireAuth`: fullscreen, groot uur + datum (live), numeriek keypad (touch + fysiek toetsenbord), PIN-dots; setup-scherm bij ontbrekende devicekey (localStorage `ts.kiosk.device`); na identify → welkomscherm met status + één/twee grote actieknoppen; na actie → bevestiging + auto-reset na 4 s (privacy, §12); netwerkfout → eerlijke foutmelding, nooit doen alsof punch lukte (§15); geen navigatie naar andere modules; a11y (focus, aria-live, geen kleur-only status, §56).
- Tests: PIN-invoer, ongeldige code generiek, actiescherm, auto-reset/privacy-reset, offline-melding.

### Task B7: Driver Activity Card (§30, §70)

- Create: `components/DriverActivityCard.tsx` — attendance (werk/pauze), diensttijd, planning vandaag; tachograafsectie expliciet "Tachograafdata niet gekoppeld" (geen fake data); punch-acties geïntegreerd.
- Modify: `DriverHomePage.tsx` (prominent bovenaan). Backend: `GET api/me/attendance/driver-day` (read-model `DriverDaySummary`, Task A8-endpoint).
- Tests: card met/zonder attendance, tachograaf-niet-gekoppeld-status.

### Task B8: Frontend-gates

- [ ] `npm run typecheck` (of `tsc -b`), volledige `npm test`, `npm run build` groen; navConfig-test bijgewerkt; commit per logisch blok

## Fase C — Afronding

### Task C1: Documentatie (§77–§79)

- Create: `docs/attendance/README.md`, `architecture.md` (entiteiten, state machine, domeingrens attendance↔tachograaf/payroll §31–§33/§36/§82–§83), `security.md` (PIN-hashing + pepper, device-auth, rate limiting, interaction tokens, permissions, tenant-isolatie), `kiosk.md` (provisioning, tablet-kioskmode, rotation, disable, offline-strategie), `driver-activity.md` (toekomstige `Modules/DriverActivity`, `IDriverActivityProvider`, Webfleet/VDO/Stoneridge-aansluitpunt), `operations.md` (troubleshooting, lange shifts, verloren device, credentialreset), `user-guide.md` (HR/employee/kiosk-admin how-tos §78)
- Modify: `docs/delivery/developer-architecture.md` (migratietabel + permissiechecklist v30), `docs/permissions.md`

### Task C2: Kwaliteitsgates + review + commit/push (§88–§90)

- [ ] `dotnet build -c Release` + volledige backendsuite groen
- [ ] `npm ci`-equivalent, typecheck, tests, `npm run build` groen
- [ ] Securityregisters kloppen; geen plaintext credentials; secret scan
- [ ] Migratie toegepast indien DB bereikbaar; geen pending model changes
- [ ] Senior review-pass (§89): state machine, concurrency, kiosk attack surface, PIN storage, tenant-isolatie, tijdsberekeningen, correcties, scheiding tachograaf
- [ ] Logische commits + push naar `nav-redesign`; working tree clean
- [ ] Eindrapport (§93)

## Bewuste beslissingen (decision record)

1. **PIN-identificatie:** één code per medewerker, per tenant uniek; O(1)-lookup via HMAC-SHA256-lookup-hash met serverside pepper (`Attendance:PinPepper`), verificatie met PBKDF2. Zonder pepper is kiosk fail-closed uitgeschakeld. Rationale: PBKDF2-scan over alle credentials per punch is onbruikbaar; keyed HMAC lekt niets zonder de pepper; échte bescherming van een 4-cijferige ruimte komt van rate limiting + lockout + device-auth (§41).
2. **Kiosk-auth:** geen user-JWT; `[AllowAnonymous]`-endpoints met verplichte `X-Kiosk-Device`-header (deviceId + 256-bit secret, SHA-256-hash opgeslagen — high-entropy token, password-grade hashing onnodig), Peppol-webhook-precedent. Interaction token (§44) in-memory, 45 s, single-use.
3. **Sessiestatus opgeslagen** (Working/OnBreak/…): transactioneel consistent gehouden met breaks; DB-invarianten via gefilterde unieke indexen; goedkope statusquery voor dashboard/overzicht (§85).
4. **Events immutable, geen soft delete**; sessies/breaks soft-deletable via basisklasse maar delete wordt nergens aangeboden — correctie/annulering is de weg (§59).
5. **Normale punches niet in de algemene auditlog** — het AttendanceEvent ís de immutable audittrail; wél audit: correcties, manuele sessies, credential-/kiosk-beheer, settings, exports (§27, bewuste keuze).
6. **Auto-close default uit**; v1 primair waarschuwen (§23).
7. **Correctieverzoek door medewerker (§25) → niet in v1**; architectuur (Tasks/Notifications + correctieservice) ligt klaar; gedocumenteerd in README onder "Toekomstig".
8. **Geen offline kiosk-punches in v1** (§15): eerlijke foutmelding; toekomstige queue-strategie (ActionQueue-patroon met `clientRequestId`-idempotency) gedocumenteerd in kiosk.md.
9. **Tenant-timezone**: eerste server-side consumer van `TenantSettings.Timezone` voor kalenderdag-attributie in rapporten; punches blijven puur UTC.
10. **Driver Activity**: read-model `DriverDaySummary` in Attendance-module (aggregatie), tachograafvelden bestaan nergens in attendance-entiteiten; providerintegratie later in `Modules/DriverActivity`.

## Self-review (spec-dekking)

Alle 94 secties gemapt; bewust uitgesteld (en gedocumenteerd): §25 (correctieverzoek-workflow), §15 (echte offline queue), §69 (kalenderweergave — lijst/rapport eerst), §33 (rijtijdwaarschuwingen — pas bij echte tachograafbron). Acceptatiecriteria §92: 1–40 gedekt door bovenstaande taken.
