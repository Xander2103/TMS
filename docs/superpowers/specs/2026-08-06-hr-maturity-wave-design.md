# HR Module Maturity Wave — design

**Datum:** 2026-08-06
**Branch:** nav-redesign
**Aanleiding:** praktijkfeedback van een HR-verantwoordelijke (Thalie) uit een transportbedrijf. Doel: de HR-module laten aanvoelen als een professionele ERP-module — schaalbaar, auditbaar, uitbreidbaar, zonder dubbel werk.

## 1. Analyse: wat bestaat al (uitbreiden, niet herbouwen)

| Feedbackpunt | Huidige stand | Conclusie |
|---|---|---|
| Enkel voor-/achternaam verplicht | Backend (`EmployeeService.ValidateRequired`) én frontend (`EmployeeForm.validate`) blokkeren al uitsluitend op voornaam + achternaam (migratie `EmployeeOptionalFields`, 2026-08-05). | ✅ Klaar; alleen bewaking van dossierkwaliteit ontbreekt. |
| Meerdere bedrijfsmiddelen + lifecycle | `EmployeeIssuedItem` ondersteunt al n items per medewerker met statusmodel `NotIssued/Issued/Returned/Missing/Damaged`, uitgiftedatum, uitgegeven-door, staat bij uitgifte/retour, retourdispositie (`good/damaged/lost/disposed`), voorraadledger, PDF-ontvangstbewijs, soft delete + audit-historiek. | ✅ Model is er; het gat is **UX**: uitgifte gaat per stuk via één kale dropdown-modal. |
| Historiek (wie/wanneer/oud/nieuw) | `EmployeeHistoryService` projecteert de append-only audit log (M15-patroon). | ✅ Klaar. |
| Belgische datumnotatie | Alle ~106 formatteer-callsites gebruiken al expliciet `nl-BE`; er is géén Amerikaanse notatie. Wel: geen centrale formatter (20+ gedupliceerde helpers) en 4 plekken met rauwe ISO-datums in de UI. | 🔶 Centraliseren + lekken dichten. |
| Inactief i.p.v. verwijderen | `IsActive` + `EmploymentStatus (Active/OnLeave/Suspended/Terminated)` + deactivate/reactivate endpoints + tri-state filter bestaan. Verwijderen bestaat bewust niet (enkel GDPR-anonymisatie). | 🔶 Duidelijke knop op detailpagina + duidelijke statusweergave. |
| Actief/Inactief/Alles-filter | `FilterBar` tri-state bestaat op de lijst. | ✅ Klaar. |
| Documentbeheer | `EmployeeDocument` met categorieën, vervaldatum, sensitieve gating, expiry-reminderbeleid. | ✅ Klaar. |
| Contracttypes | `ContractType` is een tenant-lookup (VAST/BEP/UITZ/ZELF), geen enum. | 🔶 Uitbreiden: extra types, einddatum-verplichting, presets. |
| Tankkaarten | `TankCard` (Fleet) heet in de UI al "Kaartnummer"; heeft `VehicleId`/`DriverId`, geen employee-koppeling, geen limieten, geen interne naam, geen vervalmeldingen. | 🔨 Grootste bouwstuk. |
| Sorteeropties + filters onthouden | `SearchAsync` sorteert hard op naam; niets wordt onthouden. | 🔨 Bouwen. |
| Automatische opvolging onvolledig dossier | Bestaat niet. | 🔨 Grootste bouwstuk. |

## 2. Architectuurbeslissingen

### 2.1 HR Completeness Engine (nieuw)

Geen verspreide if-statements maar één declaratieve catalogus in `Modules/Employees/Services/EmployeeCompletenessService.cs`:

```csharp
public sealed record CompletenessRequirement(
    string Code,          // "national_register_number"
    string Label,         // "Rijksregisternummer"
    string Section,       // employeeSections-id voor deep-link ("hr")
    Func<EmployeeCompletenessContext, bool> IsSatisfied);
```

- `EmployeeCompletenessContext` bundelt de employee-row plus batched flags (heeft contract-document, heeft identiteitsdocument, heeft noodcontact, heeft rijbewijsdocument indien chauffeur, heeft actieve tankkaart? nee — tankkaart is geen dossiervereiste).
- Catalogus (initieel): geboortedatum, rijksregisternummer, adres (straat+postcode+plaats), e-mail of telefoon, IBAN, startdatum, contracttype, afdeling, functie, noodcontact, identiteitsdocument, contractdocument; chauffeurs extra: rijbewijsdocument. Nieuwe vereisten = één regel toevoegen aan de catalogus.
- Confidentiële velden (NRN/IBAN): de engine rapporteert alléén *of* het veld ingevuld is (boolean), nooit de waarde — geen extra permissielek.
- Output: `EmployeeCompletenessDto { Percentage, MissingItems[{code,label,section}], IsComplete }`.
- Endpoints: opgenomen in `EmployeeDetailDto`; lijst-endpoint krijgt per rij `CompletenessPercentage` via één batched query (documents/contacts gegroepeerd per employee-id).
- Tenant-instelbaar: `HrReminderSettings` (bestaand per-tenant record) krijgt `DossierReminderDays` (default 7), `DossierEscalationDays` (default 30), `DossierRemindersEnabled` (default true). Géén per-vereiste toggletabel (YAGNI — catalogus is code, één plek).

### 2.2 Automatische opvolging (hergebruik reminder-infrastructuur)

Nieuwe producer-branch in het bestaande `ExpiryNotificationHostedService`-circuit (zelfde patroon als `HrReminderProducer`):

- ≥ `DossierReminderDays` na aanmaak én onvolledig → notificatie aan HR-rol (`hr`-template, zelfde resolutie als verjaardagsreminders), type `employee_dossier_incomplete` (Warning, categorie Hr), linkpath `/employees/{id}`.
- ≥ `DossierEscalationDays` → type `employee_dossier_incomplete_escalated` (Critical) aan `hr` + `management`.
- Dedupe via `ReminderDispatchLog` met rolling 7-dagen-bucket (zoals `ExpiryNotificationProducer`) zodat een onvolledig dossier wekelijks blijft opvolgen tot het compleet is; zodra compleet vuurt er niets meer (predicaat in de query).
- Nieuwe `MessageKinds` + `NotificationEventCatalog`-entries (groep HR) — daarmee automatisch beheerbaar in de bestaande notification-admin. Geen taak-creatie in de achtergrond: `EmployeeTaskService.CreateAsync` vereist een request-user en HR-gebruikers zijn niet gegarandeerd employees; de melding linkt rechtstreeks naar het dossier. (Beslissing gedocumenteerd; kan later alsnog.)

### 2.3 Tankkaart: één object, twee perspectieven

- **`TankCard.EmployeeId` (Guid?, SetNull) wordt de canonieke persoonskoppeling.** Migratie backfillt `EmployeeId` vanuit `DriverId → Driver.EmployeeId`. `DriverId` blijft bestaan (additief migreren, niets droppen) en wordt bij schrijven gesynchroniseerd: employee met chauffeursprofiel → `DriverId` mee gezet; anders null. DTO's exposen beide + namen.
- Nieuwe velden: `InternalName` (string?, "kaart Jan – DKV"), `FuelType` (string?, lookup-vrij), `DailyLimit`/`WeeklyLimit`/`MonthlyLimit` (decimal?), `CostCenter` (string?). `Provider` = leverancier (bestaat al). `CardNumber` heet al "Kaartnummer".
- **Vervalmeldingen 3 m / 1 m / 1 w:** nieuwe branch in `ExpiryNotificationProducer` met per-stadium dedupekeys (`tankcard_expiry:{id}:90|30|7`) via `ReminderDispatchLog`; event `tank_card_expiry` gericht op `tank_cards.view`-houders. Elk stadium vuurt exact één keer (geen bucket — stadium zelf is de trap).
- **Vanuit medewerker:** nieuwe kaartensectie op het tabblad Bedrijfsmiddelen: gekoppelde kaarten tonen (masked nummer, status, geldig tot), bestaande vrije kaart koppelen, of nieuwe kaart aanmaken (mini-form, zelfde service). **Vanuit tankkaart:** medewerker-select (vervangt chauffeur-select; chauffeurs zijn employees). Eén servicepad (`TankCardService`) — geen dubbele logica.

### 2.4 Contracten

- `ContractType` krijgt `RequiresEndDate` (bool, default false). Seed-update: `BEP` → true; nieuwe seeds `STUD` "Studentenovereenkomst" (true), `FLEXI` "Flexi-job" (false), `UITZ` bestaat ("Uitzendkracht/interim", true), `ZELF` bestaat. `SeedIfEmptyAsync`-patroon respecteren: bestaande tenants krijgen nieuwe types via additieve sync, bestaande rijen blijven onaangeroerd behalve de nieuwe kolom-default; `RequiresEndDate` wordt voor bekende codes (BEP/UITZ/STUD) eenmalig gezet in de migratie.
- Validatie in `EmployeeService` (create + update): contracttype met `RequiresEndDate` en zonder `EmploymentEndDate` → veldfout `employmentEndDate` ("Einddatum is verplicht voor dit contracttype."). Zachte regel — bestaat het dossier al zonder einddatum, dan blokkeert enkel een bewerking die het contracttype raakt niet; de completeness-engine bewaakt de rest.
- Frontend: bij zo'n contracttype wordt Einddatum `required` + presetknoppen "1 m / 3 m / 6 m / 12 m" die einddatum = startdatum + n maanden − 1 dag zetten (startdatum leeg → vandaag als basis).

### 2.5 Personeelsoverzicht: sorteren + onthouden filters

- Backend: `SearchAsync` krijgt `sort`-parameter (`name_asc` default, `name_desc`, `number`, `recent` (CreatedAt desc), `department`, `function`, `status`).
- Frontend: sorteer-select in de `FilterBar`-rij; álle filters (search uitgezonderd) + sortering persist in `localStorage` onder `ts.employees.filters` (PlanningCenter-patroon, merge over defaults). Filters die niet in de `usePagedQuery`-key zitten gaan mee in het options-object zodat de reload-gotcha verdwijnt.
- Completeness-kolom (percentagebadge) + grijze rijstijl voor inactieven.

### 2.6 Dossierstructuur ("Persoonlijk + HR")

Er zijn géén twee pagina's — er is één dossier met een sectierail. Verbeteringen:
- Sectie `hr` hernoemd naar **"Identiteit & bank"**-lading: burgerlijke staat/kinderen verhuizen naar `algemeen` (persoonlijk), DIMONA naar `dienstverband`. Secties worden: Algemeen · Dienstverband · Identiteit & bank · Noodcontacten · (extra panelen) · Notities. `EMPLOYEE_SECTION_FIELD_KEYS` volgt mee (foutbadges + deep-links blijven correct).
- Bovenaan het dossier: **completeness-kaart** ("Dossier 78% compleet — nog ontbrekend: …") met klikbare items die naar de juiste sectie springen; groene "Dossier compleet"-staat.
- Headeracties: duidelijke knop **"Medewerker inactief zetten"** (bestaande deactivate-endpoint, ConfirmDialog) / "Heractiveren", naast e-mail/tel-quick-links en kopieerbaar personeelsnummer.

### 2.7 Centrale datumformatter

Nieuw `src/utils/dates.ts`: `parseIsoDate` (dag-shift-veilig, zoals `i18n/formatters.parseIso`), `formatDate` (→ `d/m/jjjj`, nl-BE), `formatDateTime`, `formatDateLong`. Alle employees/issued-items/tank-cards/fuel-schermen gaan erop over; de vier rauwe-ISO-lekken worden gedicht. Overige features migreren opportunistisch (geen big-bang rename door 52 files — buiten HR-scope alleen de lekken).

### 2.8 Uitgifte-UX (bedrijfsmiddelen)

- **Bulk-uitgifte:** "Meerdere middelen uitgeven" — checklijst van actieve sjablonen (checkbox per sjabloon, gegroepeerd per categorie, met variant-select en aantal waar relevant), één uitgiftedatum + uitgegeven-door (huidige gebruiker) + opmerking, in één submit n `EmployeeIssuedItem`-rijen via bestaand `UpsertAsync`-pad (sequentieel, bestaande stock-guards blijven werken). Bestaand één-item-pad blijft voor correcties.
- Tabel: geformatteerde datums, kolom "Uitgegeven door", statusbadges met tonen, retourflow ongewijzigd.

## 3. Gegevensmodel & migraties (additief)

1. `TankCardEmployeeAndLimits` — `tank_cards`: + `EmployeeId` (FK employees, SetNull, index `(TenantId, EmployeeId)`), + `InternalName` (200), + `FuelType` (50), + `DailyLimit`/`WeeklyLimit`/`MonthlyLimit` (numeric?), + `CostCenter` (100); backfill `EmployeeId` uit drivers.
2. `ContractTypeRequiresEndDate` — `contract_types`: + `RequiresEndDate` bool default false; data-update BEP/UITZ → true.
3. `HrDossierReminderSettings` — `hr_reminder_settings`: + `DossierRemindersEnabled` (true), + `DossierReminderDays` (7), + `DossierEscalationDays` (30).

Geen nieuwe permissies (bestaande codes dekken alles: `employees.*`, `tank_cards.*`, `issued_items.*`, `hr_settings.manage`) → géén rolversie-bump. Nieuwe notificatietypes in `NotificationTypeCatalog` + `MessageKinds` + `NotificationEventCatalog`.

## 4. Overige UX-verbeteringen (selectie ≥20, uitgevoerd in deze wave)

1. Centrale datumutil + rauwe-ISO-lekken gedicht (4×). 2. Leeftijd naast geboortedatum. 3. Anciënniteit ("in dienst sinds … — n jaar") in dossierheader. 4. Kopieerbaar personeelsnummer. 5. `mailto:`/`tel:`-links in header en lijst. 6. Completeness-badge in lijst + 7. filter "enkel onvolledige dossiers". 8. Sorteeropties (7 stuks) server-side. 9. Filters + sortering onthouden. 10. Grijze rijstijl inactieven. 11. Contract-einde-badge ("contract loopt af over n dagen") in dossier + lijst. 12. Contracttype-presets 1/3/6/12 m. 13. Einddatum automatisch verplicht per contracttype. 14. Duidelijke "Medewerker inactief zetten"/"Heractiveren"-knop met bevestiging. 15. Bulk-uitgifte bedrijfsmiddelen met categoriegroepering. 16. "Uitgegeven door" + geformatteerde datums in de middelen-tabel. 17. Tankkaartensectie op medewerkersdossier (koppelen/aanmaken). 18. Medewerker-koppeling op tankkaartpagina (i.p.v. enkel chauffeur). 19. Tankkaart-vervalmeldingen 3 m/1 m/1 w. 20. Tankkaartlimieten + interne naam + kostenplaats. 21. Onvolledig-dossier-notificaties (7 d) + escalatie (30 d), wekelijks herhalend, configureerbaar. 22. Notification-admin-integratie van de nieuwe events. 23. Sectieherindeling (persoonlijk vs. identiteit & bank vs. dienstverband) met behouden foutrouting. 24. Duplicaatwaarschuwing bij aanmaken (zelfde voor- + achternaam, niet-blokkerend). 25. Missing-document-hints in completeness-kaart met deep-link naar Documenten-tab.

## 5. Buiten scope (bewust)

- **Archived/Exited-lifecycle**: `IsActive` + `EmploymentStatus.Terminated` + GDPR-anonymisatie dekken het reële proces; een vierde status introduceert dubbele waarheid. Her te bekijken bij archiveringsvereisten.
- **Automatische taakcreatie** voor onvolledige dossiers (request-user-vereiste; melding linkt naar dossier).
- **App-brede formatter-migratie buiten HR-features** (52 files) — alleen de zichtbare lekken.
- **Per-vereiste tenant-toggles** in de completeness-engine.

## 6. Teststrategie

- Backend: unit/integratietests (SQLite-patroon) voor `EmployeeCompletenessService` (catalogus, percentages, chauffeursregel, batched lijst), dossier-reminderproducer (dedupe, drempels, stopt bij compleet), `TankCardService` (employee-koppeling, driver-sync, limietvalidatie ≥0, expiry-stadia), contracttype-einddatumvalidatie, sort-parameter.
- Frontend: vitest voor datumutil, completeness-kaart, contractpresets, filterpersistentie, bulk-uitgifte-payload.
- Volledige suites + `tsc -b` + `npm run build` groen vóór commit.
