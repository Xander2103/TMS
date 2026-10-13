# Personeelsplanning: schaalbaarheid, filtering en Excel (2026-10-08)

## Eén selectiemodel

Grid, lijstweergave, **Week kopiëren** en de Excel-export lezen dezelfde selectie en de backend
lost die op met één gedeelde medewerkerfilter (`ShiftService.SelectedEmployees`). Semantiek:

| Regel | Betekenis |
| --- | --- |
| Binnen één dimensie | OR (Chauffeur + Dispatcher = een van beide) |
| Tussen dimensies | AND (Transport én Chauffeur) |
| Dienstverband | scope van de groep; standaard enkel in dienst (`Employee.IsActive`) |
| Expliciete medewerkers zonder groepsfilters | zij **zijn** de selectie ("deze twee mensen") |
| Expliciete medewerkers mét groepsfilters | gepind bovenop het groepsresultaat; de rij krijgt `matchesFilters=false` → "Buiten huidige filters" |
| Alleen chauffeurs | op basis van het chauffeursprofiel (`drivers`), nooit een functienaam |

Filters zijn nooit een beveiligingsgrens: tenant-scoping en `employee_planning.view/manage`
gelden altijd.

## Globale medewerkerzoekfunctie

`GET /api/employee-planning/employees?search=` doorzoekt **alle** medewerkers van de tenant
(ook uit dienst, gemarkeerd), los van de actieve planningfilters. Elk woord moet matchen op
nummer, voor- of achternaam ("peeters jan"). Dit is bewust een aparte service
(`PlanningEmployeeDirectoryService`), zodat filters nooit per ongeluk de zoekruimte verkleinen.

## API

- `GET /api/employee-planning?from&to&departmentIds&jobFunctionIds&contractTypeIds&statuses&driversOnly&employeeIds`
  (legacy `departmentId`/`employeeId` blijven werken); antwoord bevat per rij functies,
  contracttype, `isActive`, `matchesFilters` en globaal `totalInScope`.
- `GET /api/employee-planning/filter-options` — afdelingen/functies/contracttypes + eigen afdeling.
- `GET /api/employee-planning/export` — xlsx (sheets *Planning* en *Details*), zelfde parameters.
- `POST /api/shifts/copy-week` — aanvaardt dezelfde selectievelden; zonder selectie blijft het
  de volledige tenant (gedrag van vóór deze wave).

Geen databasemigratie.

## Frontend

- URL is de bron van waarheid: `week, weeks, departments, functions, contracts, status,
  drivers, employees, view` (stabiele ids). F5, Back/Forward, bookmarks en delen werken.
- Laatst gebruikte groepsfilters per gebruiker in localStorage
  (`ts.employeePlanning.filters:<userId>`); alleen toegepast op een URL zonder filters.
- `usePlanningSchedule`: AbortController + sequentienummer; vorige data blijft staan tijdens
  het laden; verouderde antwoorden worden genegeerd.
- `useVirtualRows`: rijvirtualisatie in de scrollcontainer (drempel 60 rijen; lijst 100),
  gemeten rijhoogtes, sticky dagkoppen en medewerkerkolom in dezelfde scroller.
- `MultiSelect` (gedeeld, `components/ui`): doorzoekbare multi-select met toetsenbord en
  server-side zoekmodus.

## Gemeten (lokaal, 489 actieve medewerkers, 1 924 shifts/week)

| Selectie | API | Payload | Rijen in DOM | Dagcellen | Chips |
| --- | --- | --- | --- | --- | --- |
| Alles (489) | 13–20 ms | 928 kB | 15 | 105 | 60 |
| Chauffeurs (242) | 10–15 ms | 461 kB | 15 | 105 | 60 |
| Chauffeurs + dispatchers (330) | 11–13 ms | 634 kB | 15 | 105 | 60 |
| Dispatch · Dispatcher (91) | 8–11 ms | 175 kB | 14 | 98 | 56 |
| Export 92 medewerkers | 104–409 ms | 29 kB xlsx | — | — | — |
