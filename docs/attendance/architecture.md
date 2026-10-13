# Attendance — architectuur

Module: `TransportationService.Api/Modules/Attendance` (Entities / Configurations /
Controllers / Dtos / Services / Security). Frontend: `TransportationService.Web/src/features/time-attendance`.

## Entiteiten

| Entiteit | Rol | Bijzonderheden |
|---|---|---|
| `AttendanceSession` | Eén werkperiode (inpunt → uitpunt), ook over middernacht | `AuditableTenantEntity` + `IVersionedEntity` (Version-token); status `Working/OnBreak/Completed/AutoClosed/Cancelled`; bron + kiosk/locatie-stempel; `HasCorrections`; `ForgottenClockOutNotifiedAt` (sweep-one-shot) |
| `AttendanceEvent` | **Immutable** timeline (ClockIn, BreakStarted, BreakEnded, ClockOut, ManualCorrection, AutoClosed, SessionCancelled, ManualSessionCreated) | Geen soft delete, geen update-pad — dit ís de audittrail van gewone punches |
| `AttendanceBreak` | Pauze binnen een sessie (meerdere per sessie) | `EmployeeId` gedenormaliseerd voor rapportage |
| `AttendanceCorrection` | Manuele correctie: Kind, OldValue, NewValue, verplichte reden | Corrector = `CreatedByUserId` (interceptor) |
| `AttendanceCredential` | Kiosk-identificatie per medewerker (v1: PIN) | `SecretHash` (PBKDF2) + `LookupHash` (keyed HMAC); lockoutvelden; extensible `Type` voor badge/NFC/QR |
| `KioskDevice` | Geregistreerde prikklok | `SecretHash` (SHA-256 van 256-bit token), locatiekoppeling, device-lockout, LastSeen/LastPunch |
| `AttendanceSettings` | Eén rij per tenant | selfpunch/kiosk aan-uit, PIN-lengte, drempels, auto-close (default uit), grace-marge |

### Databankinvarianten (PostgreSQL partial unique indexes)

- `UX_attendance_sessions_active_per_employee`: max **één actieve sessie per medewerker**
  (`ClockOutAt IS NULL AND IsDeleted = false`) — dubbelkliks/tweede browser/netwerkretries
  kunnen nooit een tweede actieve sessie opleveren, ongeacht de applicatielaag.
- `UX_attendance_breaks_open_per_session`: max één open pauze per sessie.
- `UX_attendance_credentials_lookup`: PIN-codes per tenant uniek (vereiste voor
  PIN-only-identificatie).

Indexen zijn TenantId-first (o.a. `(TenantId, EmployeeId, ClockInAt)`, `(TenantId, Status)`,
`(TenantId, SessionId, OccurredAt)`) — attendance groeit snel en de status-/rapportquery's
zijn erop gebouwd.

## State machine (`AttendanceStateMachine`)

```
NotClockedIn ──ClockIn──▶ Working ──StartBreak──▶ OnBreak
     ▲                      │  ▲                     │
     │                      │  └────EndBreak─────────┘
     └──(nieuwe dag)  ClockOut│◀──────ClockOut (sluit open pauze mee)
                            ▼
                        Completed          (systeem: AutoClosed; HR: Cancelled)
```

Ongeldig en server-side geweigerd: dubbel inpunten, uitpunten zonder sessie, pauze zonder
sessie, dubbele pauze, pauze stoppen zonder pauze, punchen als inactieve medewerker.
De frontend verbergt knoppen, maar de backend (service + DB-index) blijft authoritative.

## Services

| Service | Verantwoordelijkheid |
|---|---|
| `AttendanceService` | Punches (transactioneel: sessie + event), lichte statusquery, historiek met planning-vergelijking, `DriverDaySummary`-read-model |
| `AttendanceCalculator` | DE enige bruto/pauze/netto-berekening + kalenderdag-splitsing in tenant-tijdzone (DST-/nachtshift-correct, nooit negatief) |
| `AttendanceCorrectionService` | Correcties/annulering/manuele sessies — altijd reden + correctierij + event + auditlog; Version-token-check |
| `AttendanceOverviewService` | HR-liveoverzicht met statusprioriteit en anomalieën (metriekdefinities in de XML-doc) |
| `AttendanceReportService` / `AttendanceExportService` | Dagrapportage + formule-injectieveilige XLSX (Criteria-blad, export-audit `Confidential`) |
| `KioskPunchService` | Device-auth → identify (PIN) → interaction token → punch; anti-enumeratie + lockouts |
| `KioskDeviceService` / `AttendanceCredentialService` / `AttendanceSettingsService` | Beheer, alles geauditeerd |
| `AttendanceSweepWorker` (+ hosted service, 15 min) | Vergeten-uitpunt-meldingen; optionele auto-close |

## Tijd

Opslag altijd UTC via `TimeProvider` (`GetUtcNow().UtcDateTime`). Weergave via de
tenant-datumnotatie (frontend `utils/dates.ts`: `formatTime`, `formatDurationMinutes`).
Kalenderdag-attributie (rapporten, "vandaag gewerkt") gebeurt in `TenantSettings.Timezone`
(IANA; deze module is de eerste server-side consumer). Een nachtshift blijft één sessie —
alleen rapportage splitst per dag; DST-overgangen tellen de werkelijk verstreken tijd.

## Domeingrenzen (bewust)

- **Planning ≠ attendance**: `Shift` (EmployeePlanning) is verwacht, attendance is
  werkelijk. Vergelijken gebeurt read-only in historiek/rapport; attendance schrijft nooit
  planning. Standby-shifts tellen niet als geplande werktijd.
- **Absences**: goedgekeurde afwezigheid ⇒ het overzicht verwacht géén inpunt (status
  "Afwezig", geen anomalie).
- **Tachograaf**: attendance-entiteiten hebben géén rijtijdvelden. Zie
  [driver-activity.md](driver-activity.md).
- **Payroll**: attendance ≠ betaalde uren. Afronding/toeslagen/overuren zijn een latere,
  aparte laag; originele punchtijden blijven altijd bewaard (er is bewust geen
  afrondingsinstelling in v1).
- **Soft delete**: sessies/pauzes erven de soft-delete-basis maar er bestaat géén
  delete-endpoint — annuleren (Cancelled) is de weg; events zijn immutable.
- **Audit**: gewone punches staan alleen in de eventtimeline (dat is hun audittrail);
  de algemene auditlog bevat correcties, manuele sessies, credential-/kiosk-/settings-
  beheer, auto-closes en exports.

## API-oppervlak

```
GET  /api/me/attendance/status|history|driver-day     ┐ self-scoped via User.EmployeeId
POST /api/me/attendance/clock-in|clock-out|break/*    ┘ (attendance.self)

GET  /api/attendance/overview                         attendance.view
GET  /api/employees/{id}/attendance                   attendance.view
POST /api/attendance/sessions[...corrections|cancel]  attendance.correct
GET  /api/attendance/report · GET /api/reports/attendance   attendance.report

GET/POST/PUT /api/attendance/kiosks[...]              attendance.manage_kiosks
GET/PUT/DELETE /api/employees/{id}/attendance-credential    attendance.manage_credentials
GET/PUT /api/attendance/settings                      attendance.manage_settings

GET  /api/attendance/kiosk/ping     ┐ [AllowAnonymous] + X-Kiosk-Device-header
POST /api/attendance/kiosk/identify │ + rate-limitpolicy "kiosk"
POST /api/attendance/kiosk/punch    ┘ (zie security.md)
```

## Migratie

`20260820*_TimeAndAttendanceFoundation` — 7 additieve tabellen, geen destructieve
operaties.
