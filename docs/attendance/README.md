# Urenregistratie (Time & Attendance)

Workforce-attendance-laag van TransportationService: inpunten, uitpunten, pauzes,
prikklok (kiosk), HR-liveoverzicht, correcties, planning-vs-werkelijk en rapportering.

## Functionaliteit

| Wat | Waar | Permissie |
|---|---|---|
| In-/uitpunten + pauzes (self-service) | Dashboard-/portaalcard "Werkstatus", `/portal/time` | `attendance.self` |
| Eigen urenhistoriek (dag/week/maand, timeline, correcties zichtbaar) | `/portal/time` ("Mijn uren") | `attendance.self` |
| Prikklok op tablet/touch-pc | `/kiosk` (fullscreen, geen ERP-shell) | device-key + PIN (geen gebruikerssessie) |
| HR-liveoverzicht (wie is aanwezig/pauze/afwezig/ontbreekt) | `/attendance` (Personeel → Aanwezigheid) | `attendance.view` |
| Urenregistratie per medewerker | Medewerkerfiche → tab "Urenregistratie" | `attendance.view` |
| Correcties (met verplichte reden), annuleren, manuele sessies | zelfde tab | `attendance.correct` |
| PIN-beheer (genereren/zetten/intrekken, nooit uitlezen) | zelfde tab, blok "Prikklokcode" | `attendance.manage_credentials` |
| Rapport + XLSX-export (bruto/pauze/netto/gepland/afwijking) | Rapporten-catalogus "Urenregistratie (XLSX)", `GET /api/reports/attendance` | `attendance.report` |
| Instellingen (selfpunch, kiosk, PIN-lengte, drempels, auto-close) | `/settings/attendance` | `attendance.manage_settings` |
| Prikklokbeheer (provisioning, rotatie, uitschakelen) | `/settings/attendance` | `attendance.manage_kiosks` |
| Driver Activity Card (attendance + planning; tachograaf expliciet niet gekoppeld) | Chauffeursapp `/driver` | `attendance.self` |

## Automatiek

- **Sweep** (elke 15 min): actieve sessies langer dan de tenant-drempel (default 16 u)
  → notificatie "Mogelijk vergeten uit te punten" naar de medewerker én naar houders van
  `attendance.correct` (dedupe per sessie, one-shot).
- **Auto-close** staat standaard UIT. Indien ingeschakeld sluit de sweep sessies na de
  geconfigureerde grens met status `AutoClosed`, een event én een auditrij — nooit stil.

## Documentatie

- [architecture.md](architecture.md) — entiteiten, state machine, services, domeingrenzen
- [security.md](security.md) — PIN-opslag, device-auth, rate limiting, tenant-isolatie
- [kiosk.md](kiosk.md) — installatie, provisioning, rotatie, offline-gedrag
- [driver-activity.md](driver-activity.md) — scheiding attendance/tachograaf + toekomstige integratie
- [operations.md](operations.md) — troubleshooting en beheer
- [user-guide.md](user-guide.md) — stap-voor-stap voor medewerker, HR en kioskbeheerder

## Bewust NIET in deze module

- **Loonberekening** — attendance is geregistreerde werkelijkheid; contracten, overuren,
  feestdagen en recup zijn een aparte toekomstige payroll-laag (zie architecture.md §Payroll).
- **Wettelijke rij-/rusttijden** — attendance bewijst nooit rijtijden; dat vergt een
  tachograafbron (zie driver-activity.md).
- **Correctieverzoek door de medewerker** — v1 laat de medewerker HR aanspreken (of de
  notificatie beantwoorden); de correctieservice + Tasks/Notifications liggen klaar om
  hier later een aanvraag/approve-flow op te bouwen.
- **Offline kiosk-punches** — de kiosk meldt netwerkproblemen eerlijk en doet nooit alsof
  een punch lukte; een offline queue is gedocumenteerd als toekomstpad (kiosk.md).
