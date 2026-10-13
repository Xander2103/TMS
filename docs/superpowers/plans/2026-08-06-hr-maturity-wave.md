# HR Maturity Wave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** De HR-module naar ERP-niveau brengen: completeness-engine met automatische opvolging, tankkaart↔medewerker-koppeling met limieten en vervalmeldingen, contracttype-gedreven einddatums, sorteer-/filterpersistentie en 25 UX-verbeteringen — conform `docs/superpowers/specs/2026-08-06-hr-maturity-wave-design.md`.

**Architecture:** Uitbreiden van bestaande patronen: declaratieve requirement-catalogus in een nieuwe `EmployeeCompletenessService`; reminder-producers op het bestaande `ExpiryNotificationHostedService`-circuit met `ReminderDispatchLog`-dedupe; additieve migraties; geen nieuwe permissies (geen rolversie-bump); frontend op het bestaande ui-kit (SectionedForm, FilterBar, DataTable, Badge).

**Tech Stack:** .NET 10 + EF Core (Npgsql prod / SQLite tests), React 19 + TS + vitest, xunit.

## Global Constraints

- Migraties zijn additief: nooit kolommen droppen/hernoemen, nooit historische migraties bewerken. Commando: `dotnet ef migrations add <Name> --project TransportationService.Api` vanaf repo-root. Migraties worden NIET automatisch toegepast bij startup.
- Alle mutaties auditen via `IAuditService.RecordAsync` met anonieme snapshot-objecten (nooit rauwe entities; confidentieel maskeren `•••34`).
- Tenant-isolatie: nieuwe entities via `AuditableTenantEntity` of `ITenantOwned`; client-supplied FK's door `TenantReferenceGuard`.
- Validatiefouten: `DomainValidationException(field, message)` met Nederlandse boodschappen (geen FluentValidation).
- Backend tests: SQLite-patroon (`SqliteTestDbContext`), zie `TransportationService.Api.Tests\TestSupport`.
- Frontend: Nederlandse labels als literals; ui-kit-componenten uit `src/components/ui`; localStorage-keys `ts.<feature>.<thing>`.
- Notificatietypes registreren in `NotificationTypeCatalog.Map` (`NotificationService.cs:31-120`) én `MessageKinds` + `NotificationEventCatalog` (voor admin-configuratie).
- Geen nieuwe permissiecodes in deze wave.
- Elke taak eindigt groen: relevante testsuite draaien vóór commit. Commit na elke taak.

---

### Task 1: Centrale datumutil (frontend) + ISO-lekken dichten

**Files:**
- Create: `TransportationService.Web/src/utils/dates.ts`
- Create: `TransportationService.Web/src/utils/__tests__/dates.test.ts`
- Modify: `TransportationService.Web/src/features/employees/pages/EmployeeDetailPage.tsx:303` (rauwe `employmentEndDate`)
- Modify: `TransportationService.Web/src/features/issued-items/IssuedItemsTab.tsx:326` (rauwe `issuedDate`)
- Modify: `TransportationService.Web/src/features/fuel/components/FuelPanel.tsx:404` (rauwe `transactionDate`)
- Modify: `TransportationService.Web/src/features/maintenance/components/MaintenancePanel.tsx:229` (rauwe `scheduledDate`)
- Modify: `TransportationService.Web/src/features/employees/components/EmployeeNotesPanel.tsx:17`, `EmployeeHistoryPanel.tsx:36` (lokale helpers vervangen door import)

**Interfaces (Produces):**
```ts
// utils/dates.ts
export function parseIsoDate(value: string | null | undefined): Date | null; // 'yyyy-MM-dd' als LOKALE datum (geen dag-shift); timestamps zonder offset krijgen 'Z'
export function formatDate(value: string | null | undefined): string;       // 'd/m/jjjj' via nl-BE, '' bij null
export function formatDateTime(value: string | null | undefined): string;   // dateStyle short + timeStyle short, nl-BE
export function formatDateLong(value: string | null | undefined): string;   // bv. 'woensdag 6 augustus 2026'
```
Implementatie spiegelt `src/i18n/formatters.ts` (`parseIso`-regex `^\d{4}-\d{2}-\d{2}$` → `new Date(y, m-1, d)`).

- [ ] Test schrijven (dag-shift-case: `formatDate('2026-01-01') === '1/1/2026'` ook in TZ vóór UTC; null → ''; datetime-case), test rood zien, util implementeren, groen draaien (`npm test -- dates`).
- [ ] De vier ISO-lekken + twee lokale helpers vervangen door `formatDate` uit de util; bestaande tests draaien.
- [ ] Commit: `feat(web): centrale Belgische datumformatter + rauwe ISO-datums gedicht`

### Task 2: EmployeeCompletenessService (backend)

**Files:**
- Create: `TransportationService.Api/Modules/Employees/Services/EmployeeCompletenessService.cs` (+ `IEmployeeCompletenessService`)
- Modify: `TransportationService.Api/Modules/Employees/Dtos/EmployeeDtos.cs` (DTO's hieronder; `EmployeeDetailDto` + `EmployeeListItemDto` uitbreiden)
- Modify: `TransportationService.Api/Modules/Employees/Services/EmployeeService.cs` (detail: completeness meegeven; `SearchAsync`: batched percentages)
- Modify: `TransportationService.Api/Program.cs` (DI-registratie scoped, naast `IEmployeeService`)
- Test: `TransportationService.Api.Tests/Employees/EmployeeCompletenessTests.cs`

**Interfaces (Produces):**
```csharp
public sealed record CompletenessItemDto(string Code, string Label, string Section);
public sealed record EmployeeCompletenessDto(int Percentage, bool IsComplete, IReadOnlyList<CompletenessItemDto> MissingItems);

public interface IEmployeeCompletenessService
{
    Task<EmployeeCompletenessDto> GetForEmployeeAsync(Guid employeeId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, int>> GetPercentagesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct); // batched, voor lijst
    Task<IReadOnlyList<Guid>> FindIncompleteEmployeeIdsAsync(Guid tenantId, CancellationToken ct); // voor producer (Task 4) — ZONDER ITenantContext bruikbaar? Nee: tenant via meegegeven context; producer bouwt service met DevTenantContext zoals HrReminderProducer.
}
```
Catalogus als `private static readonly IReadOnlyList<CompletenessRequirement>` met codes/labels/secties exact uit de spec §2.1: `date_of_birth`/Geboortedatum/algemeen, `national_register_number`/Rijksregisternummer/hr, `address`/Adres/algemeen (straat+postcode+plaats alle drie), `contact`/E-mail of telefoon/algemeen, `iban`/IBAN/hr, `employment_start`/Startdatum/dienstverband, `contract_type`/Contracttype/dienstverband, `department`/Afdeling/dienstverband, `job_function`/Functie/dienstverband, `emergency_contact`/Noodcontact/noodcontacten, `identity_document`/Identiteitsdocument/documenten, `contract_document`/Contractdocument/documenten, en (alleen als employee een Driver-profiel heeft) `driving_licence_document`/Rijbewijsdocument/documenten. Documentcategorie-mapping: identity = `IdentityCardFront`, contract = `Contract`, rijbewijs = `DrivingLicenceFront`. Percentage = `100 * satisfied / totalApplicable`, afgerond. Context-query's batched (documents per categorie via `GroupBy`, emergency contacts count, driver-flag) en tenant-scoped.

- [ ] Failing tests: leeg dossier → laag percentage + alle items missing; volledig dossier → 100/IsComplete; chauffeursregel alleen bij driver; `GetPercentagesAsync` batched voor 3 employees in 1 tenant, andere tenant onzichtbaar.
- [ ] Implementeren; `EmployeeDetailDto` krijgt `EmployeeCompletenessDto Completeness`, `EmployeeListItemDto` krijgt `int CompletenessPercentage`; `SearchAsync` vult via batch. `EmployeesController` hoeft niet te wijzigen (DTO-doorvoer).
- [ ] `dotnet test --filter EmployeeCompleteness` groen; volledige Employees-map groen.
- [ ] Commit: `feat(hr): declaratieve dossier-completeness engine + percentages in detail/lijst`

### Task 3: HrReminderSettings-uitbreiding + migratie

**Files:**
- Modify: `TransportationService.Api/Modules/Hr/Entities/HrReminderSettings.cs` (+3 velden)
- Modify: `TransportationService.Api/Modules/Hr/Services/HrReminderConfigService.cs` + bijbehorende DTO/controller (`HrReminderSettingsController`) — velden doorgeven, validatie 1–365 dagen, escalatie > reminder
- Migration: `HrDossierReminderSettings`
- Test: uitbreiden `TransportationService.Api.Tests/Hr/*ReminderSettings*`-tests (bestaand bestand zoeken; anders nieuw `HrDossierReminderSettingsTests.cs`)

**Interfaces (Produces):** entity-velden `bool DossierRemindersEnabled = true`, `int DossierReminderDays = 7`, `int DossierEscalationDays = 30` (zelfde namen in DTO, camelCase in JSON).

- [ ] Failing test: settings round-trip via service (defaults 7/30/true; update valideert `DossierEscalationDays > DossierReminderDays` → anders `DomainValidationException("dossierEscalationDays", ...)`).
- [ ] Implementeren + `dotnet ef migrations add HrDossierReminderSettings --project TransportationService.Api`.
- [ ] Tests groen. Commit: `feat(hr): instelbare dossieropvolging (7d melding / 30d escalatie)`

### Task 4: Dossier-reminderproducer + notificatiecatalogi

**Files:**
- Modify: `TransportationService.Api/Modules/Hr/Services/HrReminderProducer.cs` (nieuwe stap in `ProduceForTenantAsync`)
- Modify: `TransportationService.Api/Modules/Messaging/Entities/OutboxMessage.cs` (`MessageKinds.EmployeeDossierIncomplete = "employee_dossier_incomplete"`, `EmployeeDossierIncompleteEscalated = "employee_dossier_incomplete_escalated"` + `AllKinds`)
- Modify: `TransportationService.Api/Modules/Messaging/Services/NotificationEventCatalog.cs` (groep HR/Personeel: labels "Personeelsdossier onvolledig" / "Personeelsdossier onvolledig — escalatie", tokens `["employeeName","percentage","missing"]`, DefaultInApp true, recipients `InternalRole "hr"` resp. `hr`+`management`, severity Warning resp. Critical)
- Modify: `TransportationService.Api/Modules/Notifications/Services/NotificationService.cs` (`NotificationTypeCatalog.Map`: beide types, categorie `Hr`, severity Warning/Critical)
- Test: `TransportationService.Api.Tests/Hr/EmployeeDossierReminderTests.cs`

**Interfaces (Consumes):** `IEmployeeCompletenessService` uit Task 2 (producer bouwt hem per tenant met `DevTenantContext`, zoals bestaande compositie in `HrReminderProducer`); settings uit Task 3.

Logica: employees met `IsActive && CreatedAt <= now - DossierReminderDays` en completeness < 100 → notificatie type `employee_dossier_incomplete`, linkpath `/employees/{id}`, boodschap "Personeelsdossier {naam} is {pct}% compleet. Nog ontbrekend: {top 3 labels}…". `CreatedAt <= now - DossierEscalationDays` → óók `..._escalated` (Critical). Dedupe: `ReminderDispatchLog` met rolling 7-dagen-bucket per stadium: `dossier_incomplete:{employeeId}:{bucket}` en `dossier_escalated:{employeeId}:{bucket}` (zelfde `today.DayNumber / 7`-formule als `ExpiryNotificationProducer.cs:76`). `DossierRemindersEnabled == false` → hele stap overslaan.

- [ ] Failing tests: (a) onvolledig dossier ouder dan 7 d → 1 notificatie aan hr-gebruiker, tweede run zelfde week → géén tweede (dedupe); (b) compleet dossier → niets; (c) 30 d → escalatie Critical erbij; (d) disabled → niets; (e) inactieve employee → niets.
- [ ] Implementeren; volledige Hr-testmap groen.
- [ ] Commit: `feat(hr): automatische opvolging onvolledige dossiers via reminder-circuit`

### Task 5: ContractType.RequiresEndDate + seeds + validatie + presets-fundament

**Files:**
- Modify: `TransportationService.Api/Modules/Reference/Entities/ContractType.cs` (+ `public bool RequiresEndDate { get; set; }`)
- Modify: `TransportationService.Api/Data/ReferenceDataSeeder.cs:93-95` (BEP→true; + `STUD` "Studentenovereenkomst" (true), `FLEXI` "Flexi-job" (false); UITZ→true)
- Migration: `ContractTypeRequiresEndDate` (kolom + data-update `UPDATE contract_types SET "RequiresEndDate" = true WHERE "Code" IN ('BEP','UITZ','STUD')`)
- Modify: `TransportationService.Api/Modules/Reference/...` ContractTypes-DTO/controller: veld exposen + bewerkbaar via bestaande lookup-CRUD
- Modify: `TransportationService.Api/Modules/Employees/Services/EmployeeService.cs` (create+update: gekozen contracttype `RequiresEndDate && EmploymentEndDate is null` → `DomainValidationException("employmentEndDate", "Einddatum is verplicht voor dit contracttype.")`)
- Test: `TransportationService.Api.Tests/Employees/EmployeeContractTypeTests.cs`

**Interfaces (Produces):** `ContractTypeDto` (of bestaand lookup-DTO) krijgt `bool requiresEndDate`; frontend Task 10 leest dit veld uit `useLookupOptions`/contract-types-API.

- [ ] Failing tests: create met BEP-achtig type zonder einddatum → veldfout; mét einddatum → ok; type zonder vlag → geen eis; update die contracttype naar vereist type zet zonder einddatum → veldfout.
- [ ] Implementeren + migratie; Reference- en Employees-tests groen.
- [ ] Commit: `feat(hr): contracttype bepaalt einddatumplicht (bepaalde duur/interim/student)`

### Task 6: Sorteerparameter personeelslijst (backend)

**Files:**
- Modify: `TransportationService.Api/Modules/Employees/Services/IEmployeeService.cs` + `EmployeeService.cs` (`SearchAsync` + `sort`-param, default `name_asc`)
- Modify: `TransportationService.Api/Modules/Employees/Controllers/EmployeesController.cs:54` (query-param `sort`)
- Test: uitbreiden `TransportationService.Api.Tests/Employees/EmployeeServiceTests.cs`

**Interfaces (Produces):** toegestane waarden `name_asc|name_desc|number|recent|department|function|status` (onbekend → `name_asc`). Mapping: number→`EmployeeNumber`, recent→`CreatedAt desc`, department→`Department.Name` (null last) dan naam, function→eerste functienaam dan naam, status→`IsActive desc` dan `EmploymentStatus` dan naam.

- [ ] Failing test per sortering (min. `name_desc`, `number`, `recent`), implementeren, groen, commit: `feat(employees): server-side sorteeropties personeelslijst`

### Task 7: TankCard: employee-koppeling + limieten + nieuwe velden

**Files:**
- Modify: `TransportationService.Api/Modules/Fleet/Entities/TankCard.cs` (+ `EmployeeId Guid?`, `InternalName string?`, `FuelType string?`, `DailyLimit/WeeklyLimit/MonthlyLimit decimal?`, `CostCenter string?`)
- Modify: `TransportationService.Api/Modules/Fleet/Configurations/TankCardConfiguration.cs` (maxlengths 200/50/100, FK employees SetNull, index `(TenantId, EmployeeId)`)
- Migration: `TankCardEmployeeAndLimits` — kolommen + backfill `UPDATE tank_cards tc SET "EmployeeId" = d."EmployeeId" FROM drivers d WHERE tc."DriverId" = d."Id" AND tc."EmployeeId" IS NULL` (Npgsql-sql; SQLite-tests draaien op EnsureCreated dus geen probleem)
- Modify: `TransportationService.Api/Modules/Fleet/Dtos/TankCardDtos.cs` (DTO + requests: alle nieuwe velden + `EmployeeId`, `EmployeeName`; `DriverId` blijft voor compat maar requests sturen voortaan `EmployeeId`)
- Modify: `TransportationService.Api/Modules/Fleet/Services/TankCardService.cs`: `EmployeeId` canoniek; bij schrijven `DriverId` synchroniseren (employee met driver-profiel → die driverId, anders null; expliciete `DriverId` in request zonder `EmployeeId` → employee via driver afleiden). Validatie: limieten `>= 0` (`DomainValidationException("dailyLimit", "Limiet moet positief zijn.")` per veld), employee via `TenantReferenceGuard`. `SearchAsync`: zoektekst matcht ook `InternalName` en employee-naam; naam-join via Employee i.p.v. Driver-keten. Audit-snapshots uitbreiden.
- Nieuw endpoint: `GET /api/employees/{employeeId}/tank-cards` (in `TankCardsController`, permission `tank_cards.view`) → `IReadOnlyList<TankCardDto>`; plus `available=true` queryoptie op de bestaande lijst voor "vrije kaarten" (EmployeeId == null, niet geblokkeerd/verlopen) t.b.v. koppelen.
- Test: uitbreiden `TransportationService.Api.Tests/Fleet/TankCardServiceTests.cs`

**Interfaces (Produces):**
```csharp
// TankCardDto nieuw: Guid? EmployeeId, string? EmployeeName, string? InternalName, string? FuelType,
// decimal? DailyLimit, WeeklyLimit, MonthlyLimit, string? CostCenter
Task<IReadOnlyList<TankCardDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken ct); // op ITankCardService
```

- [ ] Failing tests: create met EmployeeId (driver-employee) → DriverId auto-sync; employee zonder driverprofiel → DriverId null; cross-tenant employee → `InvalidTenantReferenceException`; negatieve limiet → veldfout; `ListForEmployeeAsync` filtert; search op interne naam.
- [ ] Implementeren + migratie; Fleet-tests groen.
- [ ] Commit: `feat(fleet): tankkaart gekoppeld aan medewerker + limieten/interne naam/kostenplaats`

### Task 8: Tankkaart-vervalmeldingen (3 m / 1 m / 1 w)

**Files:**
- Modify: `TransportationService.Api/Modules/Notifications/Services/ExpiryNotificationProducer.cs` (nieuwe branch)
- Modify: `MessageKinds` (+ `TankCardExpiry = "tank_card_expiry"` + `AllKinds`), `NotificationEventCatalog` (groep Vloot, label "Tankkaart vervalt binnenkort", tokens `["cardLabel","expiryDate","stage"]`, recipient `InternalPermission tank_cards.view`, severity Warning), `NotificationTypeCatalog.Map` (`tank_card_expiry` → Fleet/Warning)
- Test: `TransportationService.Api.Tests/Fleet/TankCardExpiryNotificationTests.cs`

Logica: kaarten met `ValidUntil != null && !IsBlocked`; stadia `[90, 30, 7]` dagen; per stadium vuurt zodra `ValidUntil - today <= stage` met dedupekey `tankcard_expiry:{id}:{stage}` (géén bucket — één keer per stadium), 7-dagen-lookback zoals fleet-documents zodat net-verlopen kaarten nog melden. `cardLabel` = `InternalName ?? maskeer(CardNumber)`; linkpath `/tank-cards`.

- [ ] Failing tests: kaart 89 d voor verval → stadium 90 vuurt, 30/7 niet; herrun → dedupe; kaart op 6 d → alle resterende stadia vuren elk éénmaal (90+30+7 elk eigen key); geblokkeerde kaart → niets.
- [ ] Implementeren; Notifications/Fleet-tests groen.
- [ ] Commit: `feat(fleet): automatische tankkaart-vervalmeldingen 3m/1m/1w`

### Task 9: Frontend personeelslijst: sorteren, onthouden filters, badges

**Files:**
- Modify: `TransportationService.Web/src/features/employees/pages/EmployeesPage.tsx`
- Modify: `TransportationService.Web/src/features/employees/api/employeesApi.ts` (`sort` + `onlyIncomplete` client-side? Nee: incomplete-filter client-side op geladen pagina is misleidend — dus `sort` server-side; incomplete-filter = sorteren op completeness is niet gevraagd; filter "enkel onvolledig" implementeren als client-side markering NIET — zie stap: backend heeft geen filter; voeg `completenessPercentage` kolom-badge en laat filter weg? Spec §4.7 vraagt filter → voeg backend-queryparam `incompleteOnly` toe in `SearchAsync` (percentage < 100 via batched set na paging is fout; dus: `incompleteOnly` filtert op de goedkope row-level subset van vereisten? Nee.) BESLISSING: `incompleteOnly=true` laat `SearchAsync` eerst kandidaat-ids bepalen via `IEmployeeCompletenessService.FindIncompleteEmployeeIdsAsync` (tenant-breed, gecachet per request) en filtert `WHERE Id IN (...)` vóór paging. Kleine tenants (≤ duizenden) — acceptabel, gedocumenteerd in service-comment.)
- Modify: `TransportationService.Api/Modules/Employees/Services/EmployeeService.cs` + controller (queryparam `incompleteOnly`) — hoort bij deze taak zodat de filter end-to-end werkt; test erbij in `EmployeeServiceTests`.
- Test: `TransportationService.Web/src/features/employees/pages/__tests__/employeesPage.filters.test.tsx`

**UI:** sorteer-`<select>` (7 opties, Nederlandse labels: "Naam A–Z", "Naam Z–A", "Personeelsnummer", "Recent toegevoegd", "Afdeling", "Functie", "Actief/Inactief"); checkbox "Enkel onvolledige dossiers"; persistentie `ts.employees.filters` = `{activeFilter, jobFunctionId, departmentId, employmentStatus, sort, incompleteOnly}` volgens `PlanningCenterPage`-patroon (`loadStoredFilters` met try/catch + merge over defaults, `useEffect` → `localStorage.setItem`); alle filterwaarden in het `usePagedQuery`-optionsobject zodat de request-key ze dekt (reload-gotcha op `EmployeesPage.tsx:51-55` opruimen). Completeness-badge per rij (tone: <60 danger, 60–99 warning, 100 success, tooltip "Dossier n% compleet"); rijen `IsActive === false` krijgen class `employees-row-inactive` (opacity/grijstint in `employees-page.css`). Contract-einde-badge: `employmentEndDate` binnen 30 d → Badge warning "Uit dienst over n d" naast status.

- [ ] Failing FE-test: sortering wordt uit localStorage hersteld + doorgegeven aan API-call; incomplete-checkbox stuurt `incompleteOnly`; backend-test voor `incompleteOnly`.
- [ ] Implementeren; `npm test` betrokken suites + backend Employees groen.
- [ ] Commit: `feat(web): personeelslijst met sortering, onthouden filters en dossier-badges`

### Task 10: Dossier-UX: secties, completeness-kaart, headeracties, contractpresets

**Files:**
- Modify: `TransportationService.Web/src/features/employees/components/employeeSections.ts` (herindeling §2.6: burgerlijke staat/kinderen → `algemeen`, DIMONA → `dienstverband`, sectie `hr` label "Identiteit & bank"; `EMPLOYEE_SECTION_FIELD_KEYS` mee)
- Modify: `TransportationService.Web/src/features/employees/components/EmployeeForm.tsx` (velden verplaatsen; contracttype-gedreven einddatum: `required` + presetknoppen "1 m/3 m/6 m/12 m" → `employmentEndDate = addMonths(start ?? vandaag, n) - 1 dag`; validatie spiegelt backend)
- Create: `TransportationService.Web/src/features/employees/components/CompletenessCard.tsx` (+ test)
- Modify: `TransportationService.Web/src/features/employees/pages/EmployeeDetailPage.tsx`: completeness-kaart boven de tabs (missing items klikbaar → juiste tab/sectie via bestaande `?section=`-routing); headerknop "Medewerker inactief zetten"/"Heractiveren" (ConfirmDialog, bestaande `deactivate/reactivate` endpoints via `useEmployeeMutations`); header-subtitel "In dienst sinds {formatDate} · {n} jaar" + leeftijd "({n} j.)" naast geboortedatum in read-only view; personeelsnummer kopieerbaar (klik → `navigator.clipboard.writeText` + toast); `mailto:`/`tel:`-links.
- Modify: `TransportationService.Web/src/features/employees/api/employeesApi.ts` + `types/employee.ts` (completeness-DTO, `contractTypes` met `requiresEndDate`)
- Modify: `TransportationService.Web/src/features/employees/pages/NewEmployeePage.tsx` (duplicaatwaarschuwing: na debounce zelfde voor+achternaam bestaand → niet-blokkerende hint "Er bestaat al een medewerker met deze naam." via bestaande search-API)
- Test: `.../__tests__/completenessCard.test.tsx`, uitbreiden `employeeForm`-tests (presets, verplichte einddatum)

**Interfaces (Consumes):** `EmployeeDetailDto.completeness` (Task 2), `contractType.requiresEndDate` (Task 5), datumutil (Task 1).

- [ ] Failing FE-tests: CompletenessCard toont percentage + missing labels en linkt secties; presetknop zet correcte einddatum (28-2-schrikkeljaar-case: 31-1 + 1 m → 28-2/29-2); einddatum-required rendert alleen bij `requiresEndDate`-type.
- [ ] Implementeren; alle employees-FE-tests groen.
- [ ] Commit: `feat(web): personeelsdossier met completeness-kaart, herziene secties en contractpresets`

### Task 11: Bulk-uitgifte bedrijfsmiddelen

**Files:**
- Create: `TransportationService.Web/src/features/issued-items/components/BulkIssueModal.tsx` (+ test `__tests__/bulkIssueModal.test.tsx`)
- Modify: `TransportationService.Web/src/features/issued-items/IssuedItemsTab.tsx` (knop "Meerdere middelen uitgeven" naast bestaande "Bedrijfsmiddel toevoegen"; tabel: kolommen `formatDate`-datums + "Uitgegeven door" (`issuedByName` zit al in DTO? — check `EmployeeIssuedItemDto`; zo niet: backend `IssuedItemService.ListForEmployeeAsync` verrijken met usernaam, kleine backend-wijziging + test toegestaan binnen deze taak))

**UI:** modal met sjablonen gegroepeerd per categorie, checkbox per sjabloon; bij aangevinkt: variant-select (indien variants) + aantal (default `defaultQuantity ?? 1`) + serienummer-veld; gedeelde velden bovenaan: uitgiftedatum (default vandaag), opmerking. Submit → sequentieel `saveEmployeeIssuedItem` per selectie (status `Issued`); 409-negative-stock per item afgehandeld via bestaande `NegativeStockConfirmModal`-flow (item overslaan + fout tonen als gebruiker weigert); succes-toast "n middelen uitgegeven"; lijst herladen.

- [ ] Failing FE-test: selectie van 2 sjablonen bouwt 2 save-calls met gedeelde datum; variant vereist wanneer variants aanwezig.
- [ ] Implementeren; issued-items-tests groen.
- [ ] Commit: `feat(web): bulk-uitgifte bedrijfsmiddelen met categoriechecklijst`

### Task 12: Tankkaarten-UI: nieuwe velden + medewerkersperspectief

**Files:**
- Modify: `TransportationService.Web/src/features/tank-cards/types.ts` + `api/tankCardsApi.ts` (nieuwe velden; `listEmployeeTankCards(employeeId)`, `available`-param)
- Modify: `TransportationService.Web/src/features/tank-cards/pages/TankCardsPage.tsx`: formulier + kolommen — "Interne naam", "Medewerker" (SearchableSelect op employees i.p.v. chauffeur-select; label kolom "Medewerker"), "Leverancier" (bestaand Provider-label hernoemen), "Brandstoftype", limieten (Dag/Week/Maand, €), "Kostenplaats", "Geldig tot" met `formatDate`.
- Create: `TransportationService.Web/src/features/employees/components/EmployeeTankCardsSection.tsx` (+ test): op tab Bedrijfsmiddelen onder de middelen-tabel (gate `tank_cards.view`): gekoppelde kaarten (masked nummer, interne naam, status-badge, geldig-tot), acties "Bestaande kaart koppelen" (select uit `available`-kaarten → `PUT` met `employeeId`) en "Nieuwe kaart" (mini-modal met kaartnummer/leverancier/geldig-tot → `POST` met `employeeId`), ontkoppelen (PUT `employeeId: null`) — alles gate `tank_cards.edit`/`tank_cards.create`.
- Modify: `TransportationService.Web/src/features/employees/pages/EmployeeDetailPage.tsx` (sectie mounten in bedrijfsmiddelen-tab)
- Test: `__tests__/employeeTankCardsSection.test.tsx`

**Interfaces (Consumes):** Task 7-endpoints/DTO.

- [ ] Failing FE-tests: sectie toont kaarten + koppelen-flow roept juiste API; TankCardsPage submit stuurt nieuwe velden.
- [ ] Implementeren; tank-cards/employees-FE-tests groen.
- [ ] Commit: `feat(web): tankkaartbeheer vanuit medewerker én vloot, zonder dubbele invoer`

### Task 13: HR-settings-UI voor dossieropvolging + docs

**Files:**
- Modify: frontend HR-instellingenpagina (zoek bestaande reminder-settings-UI in `features/settings` of waar `HrReminderSettings` bewerkt wordt; velden: toggle "Opvolging onvolledige dossiers", "Eerste melding na (dagen)", "Escalatie na (dagen)")
- Create: `docs/hr-dossier.md` (korte moduledoc: completeness-catalogus uitbreiden, reminderflow, tankkaartkoppeling)
- Test: bestaande settings-test uitbreiden indien aanwezig; anders render/submit-test.

- [ ] Implementeren; tests groen; commit: `feat(web): instellingen dossieropvolging + moduledocumentatie`

### Task 14: Eindverificatie + push

- [ ] `dotnet test` (volledige backend-suite) — groen.
- [ ] `cd TransportationService.Web && npx tsc -b && npm test && npm run build` — alles groen.
- [ ] `node scripts/secret-scan.mjs` — schoon.
- [ ] Migratielijst controleren (3 nieuwe), spec/plan-checkboxes afvinken, memory-bestand bijwerken.
- [ ] `git push origin nav-redesign`.
- [ ] Implementatierapport opleveren (architectuurbeslissingen, entiteiten/DTO's, migraties, UX-lijst, testresultaten, commit-hashes).

## Self-review

- Spec §2.1–2.8 → Tasks 2,4 / 3,4 / 7,8,12 / 5,10 / 6,9 / 10 / 1 / 11. Extra-UX-lijst §4: 1(T1) 2-5(T10) 6-11(T9) 12-13(T10,5) 14(T10) 15-16(T11) 17-18(T12) 19-20(T8,7) 21-22(T4) 23(T10) 24(T10) 25(T10/2). Gedekt.
- Typeconsistentie: `EmployeeCompletenessDto`/`CompletenessItemDto` (T2) gebruikt in T4/T9/T10; `requiresEndDate` (T5) in T10; tankcard-DTO (T7) in T12. Consistent.
- Volgorde: T1 onafhankelijk; T2→T4/T9/T10; T3→T4; T5→T10; T6→T9; T7→T8/T12.
