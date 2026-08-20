# Driver Activity — scheiding attendance ↔ tachograaf

## De harde grens

**Attendance registreert werkaanwezigheid; tachograafdata registreert activiteit
(rijden/andere arbeid/beschikbaarheid/rust).** Dat zijn verschillende databronnen met
verschillende juridische waarde en ze blijven gescheiden domeinmodellen:

- `AttendanceSession`/`AttendanceEvent` bevatten **geen** rijtijdvelden (geen
  `DrivingMinutes`, geen activiteitstypes) en krijgen die ook nooit.
- Attendance mag **nooit** gebruikt worden om wettelijke rij- en rusttijden te bepalen
  of te rapporteren: ingepunt zijn ≠ rijden. Rijtijdwaarschuwingen (resterende dagelijkse
  rijtijd, volgende verplichte pauze, week-/tweewekenlimiet) verschijnen pas wanneer een
  betrouwbare tachograafbron gekoppeld is — tot dan toont de UI expliciet
  "Tachograafdata niet gekoppeld" en nooit fake data.

## Wat er vandaag is

- Read-model **`DriverDaySummary`** (`GET /api/me/attendance/driver-day`): het
  attendance-blok (status, gestart, werk/pauze/diensttijd) + de geplande shifts van
  vandaag + `TachographConnected = false`.
- Frontend **`DriverActivityCard`** bovenaan de chauffeursapp (`/driver`): punch-acties,
  dagcijfers, planning en een lege-maar-eerlijke tachograafsectie.

## Hoe de tachograaf later aansluit

Nieuwe module **`Modules/DriverActivity`** (niet in Attendance):

```
DriverActivityRecord   — genormaliseerde activiteit per chauffeur
  DriverId / EmployeeId, Start/End (UTC), ActivityType { Driving, OtherWork, Availability, Rest },
  Source (provider), VehicleRef, ImportBatchId
IDriverActivityProvider — provider-neutraal contract (Webfleet Tachograph Manager, VDO,
  Stoneridge, …): FetchActivitiesAsync(driver, range) → genormaliseerde records
```

- Providers volgen het bestaande Integrations-/Peppol-patroon (provider-neutraal domein,
  vendor-logica in een adapter, dispatcher/hosted service voor sync). Geen vendor-SDK's
  in Attendance.
- Het read-model `DriverDaySummary` krijgt er dan een tachograafblok bij
  (`TachographConnected = true` + de vier activiteitstotalen + huidige activiteit) —
  de UI-plek bestaat al; het attendance-blok verandert niet.
- `AttendanceSource` heeft alvast een `Import`-waarde zodat externe klok-imports een
  eigen, geauditeerd pad krijgen en nooit de employee-selfpunch-flow delen.

## Payroll-grens (herhaald, want verwant)

Attendance = geregistreerde werkelijkheid. 8 uur attendance ≠ 8 betaalde uren:
contracten, overuren, nacht-/feestdagtoeslagen, recup en afwezigheden zijn een aparte
payroll-berekeningslaag die later op de attendance-data (en de export) bouwt. Originele
punchtijden blijven daarom altijd onaangeroerd bewaard.
