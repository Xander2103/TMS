# Personeel / HR — gerichte verbeteringswave (2026-09-12)

Scope: Personeel/HR + direct gerelateerde stamgegevens, documenten en bedrijfsmiddelen.
Niet gecommit, niet gedeployed. Bestaande architectuur en data blijven; onderstaande flows
zijn degelijk, begrijpelijk en veilig gemaakt.

## 1. Root cause ontvangstbewijs-bug (§7)

Productiejournaal (`journalctl -u transportationservice-api`, 12-09 16:01):

```
System.TypeInitializationException: The type initializer for
'…IssuedItemAcknowledgementRenderer' threw an exception.
 ---> System.InvalidOperationException: No appropriate font found for family name 'Arial'.
      Implement IFontResolver and assign to 'GlobalFontSettings.FontResolver' to use fonts.
```

- Alle vier PDF-renderers (ontvangstbewijs, factuur, transportdocument, labels) zetten enkel
  `GlobalFontSettings.UseWindowsFontsUnderWindows = true`; nergens was een `IFontResolver`
  geregistreerd (het was al gedocumenteerd als "known limitation"). Op de Linux-server bestaat
  "Arial" niet → de statische constructor van de renderer gooit → HTTP 500 bij elke download.
  Lokaal (Windows) werkt het, vandaar dat de bug enkel in productie zichtbaar was.
- Tweede, kleinere fout in dezelfde renderer: na `document.AddPage()` bleef er getekend worden
  op de `XGraphics` van pagina 1 (overflow-rijen kwamen nooit op pagina 2).
- Frontend: de download deed een kale `fetch` met bearer-token zonder de 401→refresh-retry van
  `apiClient`; na het verlopen van het access-token faalde de download tot een andere call het
  token had ververst.

**Fix**

- `Modules/Pdf/PdfFontResolver.cs`: één `IFontResolver` voor het hele proces. Zoekvolgorde per
  face (regular/bold): Arial (Windows-fontmap, bestaande PDF's blijven identiek) → Liberation
  Sans / DejaVu Sans in de OS-fontmappen → DejaVu Sans embedded in de assembly
  (`Modules/Pdf/Fonts`, Bitstream Vera-licentie, van de server gehaald). Geregistreerd in
  `Program.cs` én in `ConfigureFonts()` van elke renderer (tests zonder host).
- Renderer: eigen `XGraphics` per pagina.
- Frontend: `apiClient.downloadFile(path, fallbackName)` (bearer, één refresh-retry bij 401,
  ProblemDetails-boodschap bij fout, bestandsnaam uit `Content-Disposition`); het
  ontvangstbewijs gebruikt dit.
- Regressietests: `Pdf/PdfFontResolverTests`, `Employees/IssuedItemReturnTests`
  (`Receipt_RendersValidPdf_WithEmployeeItemSerialAndIssueDate`, multi-page), frontend
  `apiClientDownload.test.ts`.

## 2. Auto-code voor Afdeling / Contracttype / Functie (§4)

Alle drie zijn `LookupEntity`-tabellen met een gefilterde unieke index `(TenantId, Code)`
(`IsDeleted = false`). Bestaande codes zijn korte hoofdletterafkortingen (PLAN, VAST, CHAUF-CE).

Gekozen strategie (`Common/Lookups/LookupCodeGenerator` + `LookupService.CreateAsync`):

- `CreateLookupRequest.Code` mag leeg/null zijn. Naam blijft verplicht.
- Basis = naam zonder diacritics, alfanumeriek, hoofdletters, max 10 tekens (`Kraanmachinist`
  → `KRAANMACHI`); daarna de eerstvolgende vrije ordinal: `PLAN`, `PLAN-2`, `PLAN-3`.
- Uniek server-side: de kandidaat wordt bepaald uit alle codes van de tenant (inclusief
  soft-deleted, zodat historiek nooit "herrijst"); de unieke index is de scheidsrechter. Verliest
  een create de race (concurrent dezelfde kandidaat) → `DbUpdateException` → de verliezer neemt
  automatisch de volgende ordinal (max. 5 pogingen). Een expliciete dubbele code blijft 409.
- Bestaande codes worden nooit herschreven; frontend max+1 wordt niet vertrouwd.
- Tests: `Lookups/LookupAutoCodeTests` (expliciet uniek, leeg → auto, twee opvolgende creates,
  duplicate expliciet, bestaande codes onveranderd, soft-deleted overgeslagen, race via
  test-interceptor die een concurrente rij invoegt).
- Frontend: Code-veld optioneel in `LookupCreateDialog` (inline vanuit het medewerkerformulier)
  en `LookupFormDialog` (beheerpagina's), met helpertekst "Laat dit veld leeg om automatisch de
  volgende beschikbare unieke code te laten genereren."

## 3. Bedrijfsmiddel-eenheden (§8)

- Backend: `Modules/Employees/IssuedItemUnits.cs` — vaste catalogus `piece, pair, set, box,
  pack, roll, meter, liter, kilogram, other`, default `piece`. `IssuedItemService.Apply`
  accepteert een code of een bekende legacy-naam (Stuks, Paar, KG, …) en weigert al de rest
  (400, veld `unit`). Het endpoint `GET /api/unit-types/inventory-options` is verwijderd:
  bedrijfsmiddelen hebben geen afhankelijkheid meer van de algemene UnitType-masterdata (die
  tabel zelf is niet gewijzigd).
- Migratie `20260912162834_IssuedItemUnitCatalog` (data-only; lokaal toegepast, productie NIET): bekende namen →
  code, NULL → `piece`, onbekend → `other`. Kolomtype ongewijzigd; Down is no-op — een rollback zet
  de data dus niet terug, zie §8.1 voor de controle, het herstelpunt en de gevolgen.
- Frontend: `features/issued-items/issuedItemUnits.ts` spiegelt dezelfde codes (labels via
  i18n `issuedItems.units.*`); het Eenheid-veld in het sjabloonformulier is een vaste dropdown
  met Stuk als default; legacy waarden buiten de lijst tonen als "Overige".
- Tests: `Employees/IssuedItemUnitsTests` (catalogus, parsing, default, legacy, weigering,
  endpoint weg) + frontend `issuedItemUnits`/`templateFormModal`-tests.

## 4. Categorieën + sjablonen onder Personeel (§9)

Nieuwe pagina `/issued-items` (tabs `categories` | `templates`, detail
`/issued-items/templates/:id`) in het Personeel-menu ("Bedrijfsmiddelen (beheer)"). De
Parameters-entries zijn weg; oude routes redirecten (`/settings/issued-item-templates[/:id]`,
`/master-data/issued-item-categories`). Rechten: templates `issued_items.manage_templates`,
categorieën `inventory.manage` — HR en Admin hebben beide, medewerkers geen van beide. Zie §10 voor de
exacte bestanden.

## 5. Retour / heractiveren (§10, §11)

State machine op `EmployeeIssuedItem.Status` (bestaand enum):

```
Issued  --POST …/issued-items/{id}/return-->     Returned   (ReturnedDate, ReturnCondition,
                                                             ReturnDisposition, ReceivedBackByUserId,
                                                             audit "Returned", voorraad terug bij "good")
Returned --POST …/issued-items/{id}/reactivate--> Issued    (IssuedDate = vandaag/opgegeven,
                                                             retourvelden gewist, IssuedByUserId = actor,
                                                             voorraad opnieuw geconsumeerd,
                                                             audit "Reactivated" met reden)
Returned --DELETE-->                              400        (historiek blijft; alleen actieve/afwijkende
                                                             rijen kunnen nog verwijderd worden)
```

Beide transities hergebruiken het bestaande upsert-pad (`SnapshotRequest(item) with {…}`), dus
voorraadbewegingen, negative-stock-guard en audit blijven één implementatie. DTO kreeg
`ReceivedBackByName`. Frontend: rij-acties per status (actief: Bewerken · Teruggebracht ·
Verwijderen; teruggebracht: gedimd, status + datum + "door …", enkel Heractiveren).
Tests: `Employees/IssuedItemReturnTests`.

## 6. Document- en kwalificatiewaarschuwingen op de fiche (§5, §6)

`Modules/Employees/Services/EmployeeAttentionService` levert `EmployeeDetailDto.Attention`
(en `GET /api/employees/{id}/attention` om te verversen zonder herladen):

- documenten: niet-gearchiveerd, met vervaldatum; lead time uit de bestaande
  `ExpiryReminderPolicy` (kind `EmployeeDocumentCategory`, per categorie of `*`), anders 30 dagen
  — dezelfde policy als de reminder-producer;
- kwalificaties: `IQualificationStatusCalculator` met `TenantSettings.QualificationExpiryWarningDays`
  — exact de status die het kwalificatietabblad toont; Suspended/Pending tellen niet mee.
- geen tweede definitie van "verloopt binnenkort"; de waarschuwing verdwijnt zodra het document
  gearchiveerd/verwijderd is of de kwalificatie vernieuwd/geschorst.

Frontend: `EmployeeAttentionStrip` boven de completeness-kaart, klikbaar naar
`?tab=documenten&documentId=…` / `?tab=kwalificaties&qualificationId=…` met highlight + scroll;
overleeft F5. Tests: `Employees/EmployeeAttentionTests` + frontend strip/highlight-tests.

## 7. Rechten (§12)

- Rolversie **v33** (`DefaultRoleUpgrades`): HR krijgt `departments.manage`, `job_functions.manage`,
  `reference_data.view`, `reference_data.manage` (contracttypes zitten onder referentiegegevens;
  een aparte permissie zou de bestaande view-toekenning aan planner/dispatcher breken). Ook in het
  `hr`-sjabloon voor nieuwe tenants. Administrator heeft de volledige catalogus.
- Bedrijfsmiddelcategorieën (`inventory.manage`), sjablonen (`issued_items.manage_templates`),
  uitgifte/retour/heractiveren/verwijderen (`issued_items.manage`), ontvangstbewijs
  (`issued_items.view`) — ongewijzigd, HR + Admin hebben ze; chauffeur/magazijn/planner/… niet.
- Backend is de poort (`RequirePermission`-attributen, `LookupControllerBase`), frontend verbergt
  enkel. Tests: `Hr/HrWavePermissionTests`, `Identity/DefaultRoleSeederTests` (v33).

## 8. Migraties

| Migratie | Waarom | Status |
|---|---|---|
| `20260912162834_IssuedItemUnitCatalog` | Data-only mapping van legacy eenheidsnamen naar catalogus-codes (backward compatible, kolom ongewijzigd) | lokaal toegepast, productie pending |

Geen schemawijzigingen; rolversie v33 wordt door de bestaande seeder toegepast bij de eerste start.

### 8.1 Veiligheid en rollback van `IssuedItemUnitCatalog`

De migratie is één `UPDATE` op `issued_item_templates."Unit"` (`varchar(30)`, nullable); geen
andere tabel of kolom wordt geraakt. Gecontroleerd op 2026-09-20 met de exacte `Up()`-SQL tegen
een tijdelijke tabel op de lokale Postgres 16 (transactie teruggedraaid), met 28 representatieve
waarden (NULL, leeg, enkel spaties, hoofdletters, omringende spaties, accenten zoals `Boîte` en
`Mètre`, bestaande codes, onbekende vrije tekst, een waarde van exact 30 tekens):

- elke rij eindigt op een catalogus-code, geen NULL's meer, langste waarde 8 tekens (past in de kolom);
- de SQL-mapping is gelijk aan `IssuedItemUnits.TryParse`/`Normalize` (zelfde aliassen; onbekend → `other`);
- **idempotent**: een tweede run wijzigt 0 rijen, dus opnieuw toepassen is ongevaarlijk;
- de update is bewust niet tenant-gefilterd: hij geldt voor alle tenants en ook voor gearchiveerde rijen.

**`Down()` is bewust leeg.** De codes zijn ook in het vorige schema geldige waarden, maar de
oorspronkelijke tekst is na `Up()` overschreven en wordt nergens bewaard. Gevolgen:

- een **code-rollback** (vorige release terugzetten) of `dotnet ef database update <vorige migratie>`
  **draait de data niet terug**: de kolom blijft de codes bevatten. De vorige versie krijgt dan
  codes (`piece`) waar ze eenheidsnamen ("Stuks") verwacht; dat gedrag is niet getest, dus controleer
  na zo'n rollback het sjabloonformulier en zet desnoods de back-up hieronder terug;
- het verlies is alleen inhoudelijk voor **onbekende vrije tekst** (bv. "fles"), die `other` wordt.
  Bekende namen zijn omkeerbaar in betekenis, niet in exacte schrijfwijze.

Daarom vóór het toepassen op een omgeving met echte data (productie: nog NIET toegepast):

```sql
-- 1. Wat staat er nu, en wat zou 'other' worden?
SELECT "Unit", count(*) FROM issued_item_templates GROUP BY 1 ORDER BY 2 DESC;

-- 2. Herstelpunt (klein; mag na acceptatie weer weg).
CREATE TABLE issued_item_templates_unit_backup_20260912 AS
SELECT "Id", "Unit" FROM issued_item_templates;

-- Terugzetten indien ooit nodig:
-- UPDATE issued_item_templates t SET "Unit" = b."Unit"
-- FROM issued_item_templates_unit_backup_20260912 b WHERE b."Id" = t."Id";
```

Levert stap 1 waarden op die `other` zouden worden maar een eigen betekenis hebben, breid dan eerst
de alias-lijst uit (migratie-SQL én `IssuedItemUnits.LegacyAliases`) in plaats van ze te laten vervallen.

## 9. Testresultaten en browservalidatie

- Backend: 2578/2578 groen (volledige suite), incl. nieuwe `LookupAutoCodeTests`,
  `IssuedItemUnitsTests`, `IssuedItemReturnTests`, `EmployeeAttentionTests`, `HrWavePermissionTests`,
  `Pdf/PdfFontResolverTests`; `DefaultRoleSeederTests` bijgewerkt naar v33.
- Frontend: 1550/1550 groen (239 bestanden), `tsc -b` en `eslint src` schoon.
- Browser (Chrome, 1920px, lokale API + Vite, als Admin): save-and-new-label + tooltip (hover/focus),
  DIMONA-tooltip met "Meer info", doorzoekbare Afdeling/Contracttype/Functie met "+ Nieuwe …",
  inline aanmaken met lege code → server genereerde `KLANTENDIE`, formulier behield DIMONA-waarde;
  `/issued-items` met tabs Categorieën/Sjablonen, sjabloon met eenheid "Paar" opgeslagen als
  `pair`, oude routes redirecten; Teruggebracht → gedimde rij met datum/"door", enkel
  Heractiveren, overleeft F5; Heractiveren → weer actief; ontvangstbewijs 200 zonder
  console-fouten; kwalificatie (13 dagen) en document (verlopen) verschijnen in de
  aandachtsstrook, klik opent tab met highlight, overleeft F5.
- Personeelslid-rol: met het seed-account `driver@acme.be` via de API gecontroleerd —
  aanmaken afdeling/sjabloon/categorie, retour/heractiveren/verwijderen, ontvangstbewijs en
  attention geven allemaal HTTP 403. HR-rol is via `HrWavePermissionTests` en de seeder-test
  afgedekt (er bestaat geen HR-testaccount in de lokale seed).
- Testdata in de lokale DB: afdeling "Klantendienst", functie "Klantendienstmedewerker",
  sjabloon "Werkhandschoenen", en bij MED-0001 een uitgifte, een Alfapass-kwalificatie en een
  bijkomend document. Migratie `IssuedItemUnitCatalog` is lokaal toegepast (productie niet).

## 10. Gewijzigde bestanden (samenvatting)

Backend: `Modules/Pdf/{PdfFontResolver.cs,Fonts/}`, alle vier PDF-renderers, `Program.cs`, csproj
(embedded fonts); `Common/Lookups/{LookupCodeGenerator.cs,LookupService.cs,LookupDtos.cs}`;
`Modules/Employees/{IssuedItemUnits.cs,Services/IssuedItemService.cs,Services/EmployeeAttentionService.cs,
Services/EmployeeService.cs,Services/IEmployeeService.cs,Dtos/EmployeeDtos.cs,
Controllers/IssuedItemsController.cs,Controllers/EmployeesController.cs}`;
`Modules/Reference/Controllers/UnitTypesController.cs` (inventory-options weg);
`Data/{DefaultRoleDefinitions.cs,DefaultRoleUpgrades.cs}`; migratie `20260912162834_IssuedItemUnitCatalog`.
Tests: zie §9 + `TestSupport/SqliteTestDbContext.cs` (extra interceptors).

Frontend: `api/apiClient.ts` (downloadFile); `components/ui/{InfoTip.tsx,InfoTip.css,FormField.tsx,
SearchableSelect.tsx,DataTable.tsx}`; `features/master-data/{components/LookupSelect.tsx,
components/LookupFormDialog.tsx,hooks/useLookupOptions.ts,types.ts,lookupRegistry.ts}`;
`features/employees/{components/EmployeeForm.tsx,components/EmployeeAttentionStrip.tsx(+css),
components/EmployeeDocumentsTab.tsx,components/QualificationsTab.tsx,pages/EmployeeDetailPage.tsx,
api/employeesApi.ts,types/employee.ts}`; `features/issued-items/{issuedItemUnits.ts,issuedItemsApi.ts,
IssuedItemsTab.tsx,TemplateFormModal.tsx,TemplateVariantsEditor.tsx,components/IssuedItemTemplatesPanel.tsx,
pages/IssuedItemsAdminPage.tsx,pages/IssuedItemTemplateDetailPage.tsx,pages/InventoryOverviewPage.tsx}`
(`pages/IssuedItemTemplatesPage.tsx` verwijderd); `features/notifications/notificationView.ts`
(`/issued-items` → inventory); `routes/AppRoutes.tsx`; `components/layout/nav/navConfig.ts`;
locales nl/en/fr (`employees`, `employeeAttention` (nieuw), `issuedItems`, `masterData`, `navigation`, `ui`).

Afrondingsronde (§11): `components/layout/nav/{navConfig.ts,navState.ts}`, `routes/portalRouting.tsx`
+ hun tests; `Modules/Pdf/Fonts/LICENSE-DejaVu.txt` en de csproj-regel die hem meelevert.

## 11. Afrondingsronde vóór commit (2026-09-20)

### 11.1 Actieve navigatie op Categorieën én Sjablonen

Het menu-item "Bedrijfsmiddelen (beheer)" wees naar één tabblad (`/issued-items/templates`).
`NavLink` en `findActiveModuleId` matchen op prefix, dus op `/issued-items/categories` was noch het
item noch de module Personeel actief. Het item wijst nu naar de **sectiewortel `/issued-items`**;
de bestaande indexroute kiest het eerste toegestane tabblad. Geen dubbel nav-item, geen aparte
match-logica. Regressietests renderen het échte item op de drie routes (beide tabs + sjabloondetail).

### 11.2 HR kreeg een 403 na elke login

Gevonden tijdens de browservalidatie als HR: `RootRedirect` viel voor iedereen zonder
`dossiers.view` terug op `/transport-orders`, zonder `orders.view` te controleren. Nu:
`dossiers.view` → `/dossiers`, anders `orders.view` → `/transport-orders`, anders het eerste
menu-item dat de rol mag openen (`findFirstPermittedRoute` in `navState.ts`; het persoonlijke
portaal wordt overgeslagen). Rollen mét een van beide rechten landen exact zoals voorheen; HR
landt op `/dashboard`.

### 11.3 Browservalidatie als HR (niet als Admin)

Uitgevoerd in een echte (headless) Chromium tegen lokale API + Vite, met een tijdelijk lokaal
account met uitsluitend de rol HR (rolversie v33). Het account is na de test gedeactiveerd en van
een nieuw, niet bewaard wachtwoord voorzien; er zijn bewust **geen inloggegevens** in repo, docs,
tests of logs opgenomen. Doorlopen: medewerker openen → Dienstverband → doorzoekbare
Afdeling/Contracttype/Functie → afdeling inline aangemaakt (201, code auto-gegenereerd, direct
geselecteerd) → opslaan (200) → Categorieën en Sjablonen (nav blijft actief, ook na F5) →
bedrijfsmiddel uitgegeven → ontvangstbewijs (200, geldige PDF van 77,6 kB) → Teruggebracht →
F5 (status blijft, enkel Heractiveren) → Heractiveren. Daarna elk van de 22 menu-items die HR
ziet geopend: geen enkele 4xx/5xx, geen paginafouten, geen exceptions in het API-logboek. De enige
403 van de sessie was §11.2 en is na de fix niet meer reproduceerbaar.

### 11.4 Testresultaten van deze ronde

- Backend: 2578/2578 groen (volledige suite). Frontend: 1558/1558 groen (239 bestanden),
  `tsc -b` en `eslint .` schoon.
- **Eén van de vier frontend-runs faalde op één test** terwijl tegelijk de backend-suite bouwde en
  draaide. De naam van die test is niet vastgelegd; de twee runs erna (één onder dezelfde belasting,
  één onbelast) waren volledig groen. De oorzaak is dus **niet vastgesteld**: een timeout onder
  belasting is aannemelijk maar niet bewezen. Bij herhaling: draai met `--reporter=verbose` en
  leg de testnaam vast.

### 11.5 Meegeleverde fonts en licentie

`Modules/Pdf/Fonts/DejaVuSans.ttf` en `DejaVuSans-Bold.ttf` zijn ongewijzigde DejaVu Sans
**2.37**-bestanden (naamtabel gecontroleerd: copyright Bitstream 2003 + Tavmjong Bah 2006,
DejaVu-wijzigingen publiek domein). De licentie staat het bundelen in (commerciële) software toe,
op voorwaarde dat de copyright- en toestemmingsvermelding **bij elke kopie** zit, dat de fonts niet
los verkocht worden en dat een gewijzigde versie niet onder de namen "Bitstream"/"Vera"/"Arev"
verspreid wordt. Omdat de fonts als embedded resource in de assembly meereizen, levert de csproj de
ongewijzigde licentietekst mee als `licenses/DejaVu-LICENSE.txt` in build- én publish-output (bron:
`Modules/Pdf/Fonts/LICENSE-DejaVu.txt`, overgenomen uit de officiële `version_2_37`-tag). De fonts
niet hernoemen, subsetten of bewerken zonder deze paragraaf opnieuw te toetsen.

## 12. Vervolgpunten uit de afrondingsronde

F-1, F-2 en F-3 zijn **opgelost in de follow-up van 2026-09-21** (§13); F-4 staat nog open. De
tabel beschrijft de problemen zoals ze geregistreerd werden.

| # | Probleem | Waar | Notitie |
|---|---|---|---|
| F-1 | Na de **verplichte wachtwoordwijziging** komt de gebruiker opnieuw op `/change-password` terecht | `features/auth/LoginPage.tsx` (`from`-state) + `RequireAuth.tsx` | Backend is correct (`MustChangePassword` wordt gewist). Na de wijziging volgt logout; `RequireAuth` bewaart `from=/change-password` en `LoginPage` navigeert daar na login weer heen. De pagina is dan optioneel, maar het oogt als een mislukte wijziging. |
| F-2 | De titel op het wachtwoordwijzigingsscherm **overlapt zichzelf** bij regelafbreking | `/change-password` ("Kies je eigen wachtwoord") | De h1 breekt af over twee regels en die regels lopen in elkaar (vermoedelijk de regelhoogte; oorzaak niet onderzocht); zichtbaar in de smalle kaart op desktopbreedte. |
| F-3 | Nederlandse vertaling **"Nieuwe contracttype"** is grammaticaal fout | `locales/nl/masterData.json` (`createOption`: `Nieuwe {singular} "{query}" toevoegen`) | Het sjabloon gaat uit van de-woorden; het-woorden (contracttype) vragen "Nieuw". De variant zonder zoekterm toont wél correct "+ Nieuw contracttype". Vergt een lidwoord/geslacht per lookup of een neutrale formulering; FR/EN nalopen op hetzelfde patroon. |
| F-4 | Niet-geïdentificeerde falende frontendtest onder belasting | zie §11.4 | Geen oorzaak bewezen. |

## 13. Follow-up 2026-09-21: F-1, F-2 en F-3 opgelost

### 13.1 F-1 — na de verplichte wachtwoordwijziging opnieuw op `/change-password`

**Oorzaak.** Na een geslaagde wijziging trekt de backend alle sessies in, dus het scherm roept
`logout()` aan. Daardoor wordt de status `unauthenticated` terwijl de gebruiker nog op
`/change-password` staat. `RequireAuth` kon een **bewuste afmelding** niet onderscheiden van een
anonieme bezoeker of een verlopen sessie en bewaarde dus `from=/change-password`; `LoginPage`
navigeerde daar na het aanmelden weer heen. Hetzelfde mechanisme speelde bij de gewone
afmeldknop: de pagina die je verliet werd de landingspagina van de volgende aanmelding, ook als
dat een ander account was (mogelijk zonder rechten op die pagina).

**Fix (bij de oorzaak, geen hardcoded redirect).**
- `AuthContext` houdt `signedOut` bij: `true` vanaf een expliciete `logout()` tot de volgende
  aanmelding, in dezelfde render gezet als de statuswissel.
- `RequireAuth` geeft `from` alleen nog mee als de sessie NIET door een afmelding eindigde. Na een
  afmelding gaat `LoginPage` dus naar `/`, waar `RootRedirect` de startpagina op basis van rechten
  kiest (§11.2). Een anonieme deeplink en een verlopen sessie keren nog steeds terug naar de
  gevraagde pagina.
- `/change-password` bestaat alleen voor de verplichte wijziging: wie de vlag niet (meer) heeft en
  de pagina toch opent (bladwijzer, onthouden locatie) gaat naar `/` en dus naar zijn startpagina.
- Het wijzigingsscherm navigeert met `replace`, zodat het verlaten scherm niet in de historiek blijft.

**Tests.** `features/auth/__tests__/forcedPasswordChangeFlow.test.tsx` draait de échte
`AuthProvider`, `RequireAuth`, `LoginPage`, `ChangePasswordPage` en `RootRedirect` tegen een kleine
nagebootste backend (wachtwoord + `MustChangePassword` + sessie): volledige flow voor HR zonder
`dossiers.view`/`orders.view` (landt op `/dashboard`), voor een rol mét `dossiers.view` (landt op
`/dossiers`), F5 en opnieuw aanmelden, oud tijdelijk wachtwoord geweigerd, afmelden neemt de
pagina niet mee, anonieme deeplink keert wél terug, `/change-password` zonder vlag. Vóór de fix
faalden 5 van de 8.

### 13.2 F-2 — overlappende titel

**Oorzaak.** `:root` zet `font: 18px/145%`. Een line-height in procenten wordt op de root
uitgerekend en als **vaste lengte** geërfd (26px; 23px onder 1024px). De globale `h1` is 56px
(36px mobiel) en zet zelf geen line-height, dus een `h1` die afbreekt drukt zijn regels over
elkaar. De kaartschermen (vergeten/reset/activeren/verplichte wijziging) gebruikten die kale
`h1` in een kaart van 400px.

**Fix.** `.login-card h1` krijgt in `LoginPage.css` de titelstijl van de kaart (24px, zoals de
loginpagina) met een **eenheidsloze** `line-height: 1.25`, `overflow-wrap` en `text-wrap: balance`;
geen zoom, geen scaling. De introregel van de flowschermen kreeg een ondermarge. Gemeten in de
browser: 1440px en 390px één regel, 320px twee regels zonder overlap, titel binnen de kaart, geen
horizontale scroll. `authCardTitle.test.ts` bewaakt de regel (`LoginPage.css` staat daarvoor in
de `css.include` van `vitest.config.ts`).

**Bewust niet gedaan:** de globale `h1` aanpassen. Dat verschuift de hoogte van elke paginatitel in
de app en hoort niet in een kleine follow-up. Het onderliggende risico blijft: elke kale `h1` die
afbreekt overlapt. Zie restpunt R-1.

### 13.3 F-3 — "Nieuwe contracttype"

**Oorzaak.** Vijf nl-sjablonen plakten "Nieuwe" vóór een willekeurig zelfstandig naamwoord
(`Nieuwe {singular}`), fout voor het-woorden. De vorige wave had enkel de rij zónder zoekterm
gepatcht met een override in het medewerkerformulier. In het Frans gaf hetzelfde patroon
"+ Nouveau une fonction" (`{singular}` bevat daar het lidwoord).

**Fix: expliciete vertalingen, geen geraden grammatica.** `masterData.newLabel.<slug>` bevat per
lookup en per taal de volledige frase ("Nieuw contracttype", "Nouvelle fonction", "New department").
Die wordt gebruikt voor de knop op de beheerpagina, beide dialoogtitels, de "+"-rij en de rij met
zoekterm (`{newLabel} "{query}" toevoegen`). De generieke sleutels `list.new`, `form.newTitle` en
`select.newTitle` zijn vervallen. Aanroepers die een los woord meegaven ("afdeling", "categorie" —
hardgecodeerd Nederlands, dus ook fout in FR/EN) gebruiken nu de registry-sleutel; voor een los
woord bestaat nog een neutrale terugval ("Toevoegen: {singular}"). De vijf overbodige
`employees.form.*`-sleutels zijn verwijderd. `lookupNewLabels.test.ts` eist voor elke lookup in
de registry een label in nl/fr/en en verbiedt elk sjabloon dat "nieuw" aan `{singular}` plakt.

### 13.4 Restpunten

| # | Punt |
|---|---|
| R-1 | Globale `h1` zonder eigen line-height (zie §13.2): latent voor elke kale `h1` die afbreekt. Vraagt een app-brede visuele controle. |
| R-2 | nl-titels `"{singular} bewerken"` / `"{singular} verwijderen"` beginnen met een kleine letter ("contracttype bewerken"). Grammaticaal juist, typografisch slordig; niet aangeraakt. |
| R-3 | Op de kaartschermen staat de link "Terug naar aanmelden" op 320px náást de knop en breekt dan af; de kaartformulieren hebben geen eigen kolomlayout. Cosmetisch, niet aangeraakt. |
| F-4 | Onveranderd open (§11.4): oorzaak van de eenmalig falende frontendtest is niet vastgesteld. In deze ronde waren alle volledige runs groen (1580/1580). |

Bijvangst in §13.2: een lang e-mailadres in de welkomstregel van het activatiescherm liep op 320px
buiten de kaart; de introregel breekt nu af (`overflow-wrap: anywhere`).

### 13.5 Validatie

Frontend 1580/1580 (242 bestanden), `tsc -b` en `eslint .` schoon. Backend niet geraakt (geen
enkel bestand onder `TransportationService.Api*` gewijzigd), dus de backend-suite is niet opnieuw
gedraaid. Browser (headless Chromium, lokale API + Vite, tijdelijk HR-account dat na afloop weer
gedeactiveerd en van een niet bewaard wachtwoord voorzien is): aanmelden met tijdelijk wachtwoord →
`/change-password` → wijzigen → `/login` zonder `from` → aanmelden → `/dashboard` → F5 blijft
`/dashboard` → afmelden vanaf `/employees` en opnieuw aanmelden → `/dashboard` → `/change-password`
rechtstreeks openen → `/dashboard` → anonieme deeplink `/absences` keert na aanmelden terug naar
`/absences`. Geen 4xx/5xx buiten de anonieme sessiecheck. F-3 nagelopen in nl en fr op het
medewerkerformulier (drie lookups: "+"-rij, rij met zoekterm, dialoogtitel) en op de beheerpagina.
