# Personeelsdossier: volledigheid, opvolging, tankkaart & contracteinddatum

> HR-maturity-wave (2026-08-06), taken 2-5, 8, 13. Backend: `TransportationService.Api/Modules/Employees|Hr|Fleet`.
> Frontend instellingen: `TransportationService.Web/src/features/employees/pages/HrReminderSettingsPage.tsx`
> (`/settings/hr-reminders`, permissie `hr_settings.manage`).

## 1. Completeness-systeem

`EmployeeCompletenessService`
(`TransportationService.Api/Modules/Employees/Services/EmployeeCompletenessService.cs`) berekent
per medewerker een percentage en een lijst ontbrekende items via een **declaratieve catalogus** —
geen verspreide if-statements. De catalogus (`Catalogue`, private static, regel ~76) is een lijst
`CompletenessRequirement(Code, Label, Section, IsApplicable, IsSatisfied)`.

**Een vereiste toevoegen is één regel** in `Catalogue`:

```csharp
new("iban", "IBAN", "hr", AlwaysApplicable, c => c.HasIban),
```

- `IsApplicable` bepaalt of de regel meetelt (bv. `driving_licence_document` telt alleen als
  `c.IsDriver`); de meeste regels gebruiken `AlwaysApplicable`.
- `IsSatisfied` leest uitsluitend uit `CompletenessContext` — een record dat **al** batched en
  tenant-gescoped is opgebouwd (`BuildContextsAsync`), zodat een nieuwe vereiste nooit een eigen
  databasequery introduceert. Wil de vereiste een nieuw gegeven controleren dat nog niet in de
  context zit, breid dan eerst `CompletenessContext` en de query in `BuildContextsAsync` uit.
- Vertrouwelijke velden (rijksregisternummer, IBAN) worden **enkel op aanwezigheid** getoetst —
  de engine leest of rapporteert nooit de waarde zelf.

**Percentage/IsComplete-semantiek** (`Evaluate`): van de *toepasselijke* regels wordt het aandeel
voldane regels afgerond (`MidpointRounding.AwayFromZero`) tot een percentage 0-100.
`IsComplete = (aantal ontbrekende items == 0)`. Een medewerker zonder toepasselijke regels (zou in
de praktijk niet voorkomen) is per definitie 100%/compleet.

Consumers: `EmployeeCompletenessDto` op het detail-endpoint, `GetPercentagesAsync` (batched, voor
de personeelslijst-badge), `FindIncompleteEmployeeIdsAsync` (voor de reminder-producer, §2, en het
filter "Enkel onvolledige dossiers" op de personeelslijst).

## 2. Opvolgingsflow (reminders)

`HrReminderProducer.ProduceDossierRemindersAsync`
(`TransportationService.Api/Modules/Hr/Services/HrReminderProducer.cs`, regel ~225) draait als
stap in de bestaande HR-reminder-sweep (naast verjaardagen, dienstjubilea, einde dienstverband).
Volledig overgeslagen wanneer `HrReminderSettings.DossierRemindersEnabled == false`.

Per actieve medewerker met een onvolledig dossier (`FindIncompleteEmployeeIdsAsync`) en
`CreatedAt <= vandaag - DossierReminderDays`:

1. **Melding** (`employee_dossier_incomplete`, ernst Warning) naar alle houders van de `hr`-rol:
   *"Personeelsdossier {naam} is {pct}% compleet. Nog ontbrekend: {top 3 labels}…"*, link naar
   `/employees/{id}`.
2. **Escalatie** (`employee_dossier_incomplete_escalated`, ernst Critical) zodra ook
   `CreatedAt <= vandaag - DossierEscalationDays`: zelfde boodschap naar `hr` + `management`.

**Dedupe**: `ReminderDispatchLog` met een rollende 7-dagenbucket (`vandaag.DayNumber / 7`, zelfde
formule als `ExpiryNotificationProducer.cs:76`) — sleutels `dossier_incomplete:{employeeId}:{bucket}`
en `dossier_escalated:{employeeId}:{bucket}`. Een reeds geclaimde sleutel deze week wordt niet
opnieuw verstuurd; zodra de bucket omslaat (volgende week) kan de melding opnieuw vuren als het
dossier nog steeds onvolledig is.

**Instelbaar** via `HrReminderSettings` (`TransportationService.Api/Modules/Hr/Entities/HrReminderSettings.cs`)
en `GET/PUT api/hr/reminder-settings` (`HrReminderSettingsController`, permissie
`hr_settings.manage` voor PUT, `employees.view` voor GET):

| Veld | Default | Validatie |
|---|---|---|
| `DossierRemindersEnabled` | `true` | — |
| `DossierReminderDays` | `7` | 1-365 |
| `DossierEscalationDays` | `30` | 1-365, moet > `DossierReminderDays` (anders veldfout `dossierEscalationDays`: "Escalatie moet later vallen dan de eerste melding.") |

UI: **Instellingen → HR-herinneringen** (`/settings/hr-reminders`), sectie "Opvolging onvolledige
dossiers" — toggle + twee dagvelden, uitgeschakeld zolang de toggle uit staat.

## 3. Tankkaartkoppeling

`TankCard` (`TransportationService.Api/Modules/Fleet/Entities/TankCard.cs`) heeft twee
medewerker-gerelateerde velden:

- `EmployeeId` — **canoniek**. Elke create/update geeft ofwel `employeeId` ofwel `driverId` op;
  `TankCardService.ResolveEmployeeAndDriverAsync` (regel ~388) zoekt bij een `employeeId` het
  bijhorende `Driver`-profiel op en zet `DriverId` automatisch mee (auto-sync, niet omgekeerd
  instelbaar).
- `DriverId` — legacy pointer, altijd afgeleid van `EmployeeId`. Geeft de aanroeper in plaats
  daarvan een `driverId` op, dan wordt daaruit de `EmployeeId` afgeleid. Een onbekende `driverId`
  levert `TankCardOperationResult.InvalidReference`.

**Vervalmeldingen**: `ExpiryNotificationProducer` (`TransportationService.Api/Modules/Notifications/Services/ExpiryNotificationProducer.cs`,
regel ~198) bewaakt `TankCard.ValidUntil` met drie vaste stages: **90 / 30 / 7 dagen** vóór
verval (`TankCardExpiryStages`). Elke stage heeft een eigen, niet-rollende dedupe-sleutel
(`tankcard_expiry:{cardId}:{stage}`) — vuurt dus precies één keer per stage, ooit. Wanneer een
kaart bij eerste observatie al binnen meerdere stages tegelijk valt (bv. laat gezien, 6 dagen
vóór verval: 90/30/7 allemaal al voorbij hun drempel), worden alle vervallen stage-sleutels wél
geclaimd (zodat ze niet later nog aparte meldingen geven) maar wordt er maar **één** melding
gepubliceerd: de strengste (kleinste) stage — *"tightest-stage"*-gedrag. Labels: 90 → "3 maanden",
30 → "1 maand", 7 → "1 week".

## 4. Contracttype-einddatumplicht

`ContractType.RequiresEndDate` (`TransportationService.Api/Modules/Reference/Entities/ContractType.cs`)
is een tenant-beheerbaar vlag op het contracttype-lookup (bewerkbaar via de gewone
lookup-endpoints `PUT/POST api/lookups/contract-types`, veld `requiresEndDate`; standaard `true`
voor bepaalde-duur/uitzendtypes, `false` voor onbepaalde duur — zie seeds in
`ReferenceDataSeeder.cs`).

- **Server-validatie**: `EmployeeService` (regel ~625) weigert opslaan wanneer
  `contractType.RequiresEndDate == true` en `employmentEndDate` leeg is.
- **UI-presets**: `EmployeeForm.tsx` toont het einddatumveld als verplicht
  (`required={contractTypeRequiresEndDate}`) zodra het geselecteerde contracttype dit vereist, met
  vier snelkeuzeknoppen (`CONTRACT_END_DATE_PRESETS` in
  `TransportationService.Web/src/features/employees/utils/contractPresets.ts`: 1, 3, 6, 12
  maanden vanaf startdatum (fallback: vandaag) die de einddatum in één klik invullen.
