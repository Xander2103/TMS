# UX-sprint 2026-09-09 — ontwerp en bindende contracten

Lead-beslissingen na de architectuurinventaris (zes domeinrapporten). Dit document is de
enige bron van waarheid voor alle werkstromen in deze sprint. Geen commit, geen deploy.

## 1. Wat de inventaris opleverde

### 1.1 Meldingsabonnementen (contactpersonen)
- Geen aparte abonnemententiteit: een abonnement = koppelrij `CustomerCommunicationRuleContact`
  op een `CustomerCommunicationRule` (type-gedreven regelmotor). Catalogus
  `CustomerNotificationCatalog` met 10 stabiele machine-keys (`order-confirmation`, `planning`,
  `eta`, `delivery-pod`, `delivery-problem`, `redelivery`, `invoice`, `credit-note`,
  `invoice-reminder`, `general`), labels vertaald in de client.
- Twee schrijfpaden: `PUT /api/customers/{id}/contacts/{cid}` (velden, geen abonnementen) en
  `PUT /api/customers/{id}/contacts/{cid}/notifications` (`{ optionKeys }`, diff-gebaseerde
  vervanging: toevoegen én verwijderen, geavanceerde regelinstellingen onaangeroerd).
- Leespad: `GET …/contacts/{cid}/notifications`. Backend is correct (17 tests).

**Exacte oorzaken van niet-persisteren en valse dirty-state**
1. In bewerkmodus rendert `CustomerForm` de zelf-opslaande panelen (contacten, adressen,
   communicatie, facturatie) én hun modals BINNEN `<form onSubmit={handleSubmit} onChange={touch}>`.
   `Modal` is geen portal → geneste `<form id="contact-form">`. React laat `change` en `submit`
   doorbubbelen: elk vinkje zet de pagina dirty (→ `beforeunload`), en de Opslaan van de modal
   voert óók `CustomerForm.handleSubmit` uit (ongevraagde `PUT /api/customers/{id}`, verlaten
   van bewerkmodus, of bij validatiefout een sectiesprong die de modal mid-save unmount).
2. Elke contactmutatie roept `reload()` aan vóór de notificatie-PUT; `CustomerDetailPage`
   toont dan `LoadingState` en unmount de hele boom. De modal verdwijnt terwijl de PUT nog
   moet vertrekken; direct heropenen leest de oude staat.
3. De asynchrone GET van abonnementen overschrijft `notificationKeys` onvoorwaardelijk; vinkjes
   die de gebruiker al zette worden gewist en `sameKeys` slaat de PUT dan over ("soms niet
   opgeslagen"). Een mislukte GET wordt ingeslikt → alle vakjes leeg → één vinkje opslaan
   unsubscribet al het andere.

### 1.2 Locaties en openingsuren
- `Location` (code, naam, type, straat/huisnummer/postcode/plaats/land, `ExternalReference`,
  contact, operationele velden, `OpeningIntervals`, `CustomerId` legacy) + `CustomerLocationLink`
  (alias, klantreferentie, rol Both/Loading/Unloading, defaults). Links zijn bron van waarheid.
- Openingsuren: `LocationOpeningInterval { DayOfWeek 1..7, TimeOnly From/To, Note }`,
  wire-formaat strikt `"HH:mm"` (parse `TimeOnly.TryParseExact("HH:mm")`, uitvoer
  `ToString("HH:mm")`). Regel: zelfde dag, `From < To`, geen overlap. Geen nachtelijke vensters.
- **AM/PM-oorzaak**: uitsluitend de native `<input type="time">`, die Chrome onder een en-US
  UI-taal in 12-uursweergave rendert ongeacht `lang="nl"`. Geen enkele app-code converteert of
  parseert tijden. Semantiek: in het 12u-segment-UI levert "12:00" zonder PM-keuze `"00:00"`
  (middernacht); beide validatoren wijzen dat correct af. Het is dus een invoer-ergonomie-
  probleem van de browsercontrol, niet van parsing in de app; de opslag is al 24u.
- Layout: `.ohe-day-body` is een wrappende flex-rij met alle tijdvakken + "+ Tijdvak" als
  items; de foutmelding staat als extra kind ín het tijdvak → siblings verschuiven; tijdvakken
  lopen horizontaal.

### 1.3 Route/stops
- `TransportOrderStop { Sequence, StopType Loading|Unloading (string op de wire), LocationId?,
  adres-snapshot, contact-snapshot, PlannedFrom/To (UTC-instant), Requested/Confirmed/
  Earliest/Latest, TimeRequirement(+From/To TimeOnly), Reference, Instructions, … }`.
- Geen per-stop endpoint: stops gaan als volledige collectie mee in `PUT /api/transport-orders/{id}`
  (id-behoudende sync: niet-geëchode stops worden soft-deleted; ontbrekend id = nieuwe stop;
  volgorde = requestindex; `Version`-token → 409 met actuele staat).
- Datum-only stop: `plannedFrom = tenantmiddernacht`, `plannedTo = null`. Readiness
  `route.date_missing` = geen enkele stop met `PlannedFrom`.
- Locatiezoeken: `GET /api/locations/options` (alles, geen zoekterm — huidig `LocationSelect`,
  clientfiltering); `GET /api/addresses/picker?customerId&search&take` (naam, code, straat,
  postcode, plaats; groepen Klantadres → Recent → Alle) = de juiste serverzoekbron.
- Quick-create: `LocationQuickCreateDialog` (duplicaatcheck, `customerId`-koppeling) geeft
  de aangemaakte optie terug; `SearchableSelect.onCreate` selecteert die automatisch.

### 1.4 Prijs
- "Dossierprijs" = `SUM(TransportOrder.AgreedPrice ?? 0)` over gekoppelde opdrachten.
  `AgreedPrice` wordt door de backend afgeleid uit `PricingSnapshot.LinesTotal` (som van
  Auto/AutoAdjusted/Manual niet-informatieve prijsregels), tenzij:
  (a) override `PriceIsManual` + reden (`orders.override_price`), (b) legacy afgesproken prijs
  wanneer niets berekend kon worden en geen regels bestaan, (c) `PricingSource = OneOff` +
  `OneOffFixedAmount` (eenmalige prijsafspraak; géén extra permissie; de motor zet het bedrag
  als Auto-regel; overleeft herberekening; lock/confirm van toepassing).
- Verkooplijnen = `TransportOrderPricingLine` (Kind Auto/AutoAdjusted/Manual/Proposed). Vrije
  regel = Kind Manual, `PUT /api/transport-orders/{id}/pricing/lines`, permissie
  `orders.override_price|orders.manage`, verplicht label + (bedrag óf aantal×eenheidsprijs).
- `€ 0,00` betekent vandaag: geen opdracht, of `AgreedPrice = null`, of de motor liep zonder
  tellende regels (→ 0). Geen onderscheid met een echte nulprijs.
- `pricing.none` verschijnt alleen als er activiteiten zijn en géén enkele activiteit een
  opdracht heeft; een gekoppelde conceptopdracht zonder prijs geeft vandaag géén aandachtspunt.
- "Ga naar prijs": `document.getElementById('sectie-prijs').scrollIntoView` — sectie is al in
  beeld (pagina past in viewport), niets krijgt focus, en in de `pricing.none`-staat bevat de
  sectie geen enkele actie ("Prijsdetails"/"+ Verkooplijn" navigeren beide naar de
  opdrachtpagina en zijn verborgen zonder opdracht). Op de opdrachtpagina is "+ Vrije regel"
  bovendien verborgen zolang er nul regels zijn.

### 1.5 Dossierlijst
- Eén SQL-projectie met gecorreleerde subquery's (geen N+1), `Take(500)`, geen paging.
  Zoeken matcht enkel `DossierNumber` en `Title`. Lijst-DTO mist referentie, klantnummer, prijs.
  `TransportDossier.CustomerReference` en `Customer.CustomerNumber` bestaan.

## 2. Beslissingen

### 2.1 Geen migratie
Alle benodigde concepten bestaan. Backend-wijzigingen zijn additief op DTO's/queries.

### 2.2 Contactpersonen
- **Isolatiegrens**: nieuw `components/ui/SelfSavingPanel.tsx` — een wrapper die React-
  `change`/`input`/`submit`/`reset`-propagatie stopt. `CustomerForm` wikkelt elk `editPanels`-
  paneel erin. Daarmee raakt niets in een zelf-opslaand paneel de paginadirty-state.
- `Modal` wordt een portal naar `document.body` (geen geneste `<form>` in de DOM, geen
  clipping van dropdowns in modals). Synthetic events bubbelen nog steeds door de React-boom;
  daarom blijft de isolatiegrens nodig.
- Sectie `contactpersonen` in bewerkmodus krijgt `panel: true` (geen pagina-Opslaan/Annuleren
  boven een zelf-opslaand paneel).
- Volgorde van opslaan: contactvelden PUT → notificaties PUT → pas dán `onChanged()` (reload).
  De modal blijft gemount tot beide klaar zijn; bij mislukking blijft de modal open met de
  ingevoerde waarden en foutmelding; dirty blijft waar.
- `CustomerDetailPage` toont geen `LoadingState` meer als er al klantdata is (stale-while-
  refetch): geen unmount, geen verlies van bewerkingen in andere secties.
- Notificatievakjes zijn uitgeschakeld (met "laden…") tot de GET klaar is; mislukte GET →
  foutregel + vakjes uitgeschakeld en géén optionKeys mee (nooit destructief).
- Visueel: `FormSection` gaat plat binnen `SectionedForm` (zie 2.6); het paneel zelf toont
  géén tweede kop meer maar een toolbar (`PanelHeader`: filter links, "+ Contact toevoegen"
  rechts) + `DataTable` als enige container.

### 2.3 Openingsuren
- Nieuw `components/ui/TimeInput.tsx`: gecontroleerde tekstinvoer `HH:mm` 24u,
  `inputMode="numeric"`, normalisatie op blur (`8`→`08:00`, `830`→`08:30`, `8.30`/`8h30`/
  `8u30`→`08:30`, `24:00`/ongeldig → onveranderd + `aria-invalid`), emit alleen geldige
  `HH:mm` of `''`. Geen AM/PM mogelijk. Wire-formaat blijft `"HH:mm"`.
- Gebruikt in `OpeningHoursEditor`, de vier aankomstvelden van `LocationForm`, en de
  tijdvensters in `RouteSection` (route-inline op het dossier). Andere schermen met native
  time-inputs blijven buiten scope (gerapporteerd).
- `OpeningHoursEditor` op CSS-grid: kolommen `dag | van | – | tot | notitie | ×`; elk tijdvak
  een eigen grid-rij; foutmelding in een eigen rij (`grid-column: 2 / -1`); "+ Tijdvak" als
  laatste rij van de dag; "Gesloten" idem. Eén container-rand (de editor), geen fieldset.
- `LocationDetailPage` leestabel houdt dezelfde kolomlogica.

### 2.4 Dossierprijs — hoe het concept wordt blootgelegd
Geen nieuw prijsveld. De sectie **Verkoop & prijs** op het dossier wordt:
1. **Groot bedrag** = dossiertotaal (`financials.agreedOrderTotal`) maar alléén als
   `financials.pricedOrderCount > 0`; anders "Nog geen prijs" (geen `€ 0,00`).
2. **Afgesproken prijs** (inline, per gekoppelde transportopdracht; standaard de eerste):
   invoer `€` + [Opslaan]. Opslaan zet `pricingSource = 'OneOff'` en `oneOffFixedAmount` via
   de bestaande order-PUT (volledige payload uit `orderValuesFromDetail`, met `version`).
   Dit ís het domeinconcept "de klant en wij spraken € X af voor deze opdracht": geen
   permissie-override, overleeft herberekening, lock/confirm blijft gelden. Hint onder het
   veld legt uit dat een afgesproken prijs de tariefberekening voor deze opdracht vervangt en
   toont de berekende tariefprijs als die bestaat. Wanneer `priceIsManual` (override) of
   status Locked/Invoiced: veld alleen-lezen met verwijzing naar Prijsdetails.
   Permissie: `orders.edit|orders.manage`.
3. **Verkooplijnen**: tabel van `pricingLines` van de opdracht (omschrijving, aantal,
   eenheidsprijs, bedrag, bron) + inline "+ Verkooplijn"-rij (omschrijving, aantal,
   eenheidsprijs óf bedrag) → `saveOrderPriceLines` (`orders.override_price|orders.manage`).
   Manuele regels kunnen verwijderd worden. "Prijsdetails" blijft de link naar de opdracht.
4. **Zonder transportopdracht** (`pricing.none`, bv. alleen Opslag): eerlijke uitleg dat de
   prijs op een transportopdracht wordt bijgehouden + directe actie: "Transportopdracht
   aanmaken" voor een transportactiviteit zonder opdracht, anders "+ Activiteit".
   Zelfstandige activiteiten (opslag/kraan) hebben in deze wave geen eigen prijsdrager;
   dat wordt gerapporteerd.
- Lijst en detail gebruiken exact dezelfde "geprijsd"-definitie (2.5).

### 2.5 Backendcontract (additief)
```
DossierListItemDto  += CustomerReference string?, CustomerNumber string?,
                       AgreedPriceTotal decimal? (null als PricedOrderCount == 0),
                       PricedOrderCount int
DossierFinancialSummaryDto += PricedOrderCount int
```
"Geprijsd" (één definitie, statische helper `OrderPricingState.IsPriced` in Modules/Orders,
inline gespiegeld in EF-projecties): `PriceIsManual || AgreedPrice > 0`.
Lijstzoeken: `DossierNumber`, `Title`, `CustomerReference`, klantnaam, klantnummer (alles
in dezelfde SQL-projectie; geen N+1).
Readiness:
- `Field` wordt gevuld: `order.confirm.stops` → `stops.loading` als laden ontbreekt, anders
  `stops.unloading`; `route.date_missing` → `stops.plannedFrom`; `route.order_missing` →
  sectie `route`, field `stops.loading`; `pricing.*` → `price`.
- Nieuw `pricing.missing` (Warning, sectie prijs, field `price`): gekoppelde opdracht in
  Draft/Submitted/Confirmed die niet geprijsd is. `pricing.none` blijft (Info, geen opdracht).
- Operator-precedentiebug in `route.date_missing` gefixt (`!HasDate && (Draft||Submitted||Confirmed)`).
Picker: `AddressPickerOptionDto += CustomerNames string?` (gekoppelde klantnamen), zoekterm
matcht ook `ExternalReference`; filter en limiet in SQL.

### 2.6 Visuele hiërarchie
- `FormSection` krijgt `variant: 'framed' | 'flat'`; binnen `SectionedFormBodyContext` is
  `flat` de standaard: `<section aria-labelledby>` + `<h3>`-titel, geen rand/afronding;
  opeenvolgende platte secties gescheiden door `border-top` + ruimte. Buiten een
  SectionedForm blijft `framed` (portal, incidenten, entiteiten).
- Nieuw `components/ui/PanelHeader.tsx`: `{ title?, description?, actions?, children }` → één
  rij: links titel/omschrijving/filters, rechts primaire actie.
- Globale tokens toegevoegd: `--text-muted`, `--radius-sm/md/lg`, `--space-1..6`.
- Klantpanelen Adressen/Communicatie/Facturatie: dubbele kop weg, `PanelHeader` toolbar.

### 2.7 Dossierdetail — navigatie en route
- Sectie-registry (`features/dossiers/sectionRegistry.tsx`): context met
  `registerSection(id, api)` / `registerField(fieldKey, ref)` / `goTo(section, field?)`:
  opent `<details>`, scrollt, focust het veld (of de sectiekop met `tabIndex=-1`) en zet een
  tijdelijke `data-highlight`. Geen `document.querySelector`.
- `AttentionPanel.onNavigate(section, field)` gebruikt `issue.field` uit de backend.
- **Route-inline** (`DossierRouteEditor`): vervangt samenvatting + "Route bewerken" voor
  bewerkbare open dossiers. Rendert `RouteSection` (met `LocationSelect`-autocomplete,
  datum, tijdvenster 24u, referentie/instructie) direct op de pagina met per stop een
  laad/los-label en volgnummer, "+ Extra laadstop" / "+ Extra losstop", ↑/↓ volgorde, ×.
  Expliciete sectie-Opslaan/Annuleren met dirty-indicator (collectie-vervangingssemantiek
  maakt autosave onveilig; dit is coherent met de drawers). Opslaan = order-PUT met alle
  stop-id's + `version`; 409 → conflictbanner met Herladen. Na succes:
  `setLoadedOrder(updated)` + `load()` (readiness ververst).
  Transportactiviteit zonder opdracht: editor is meteen invulbaar; bij Opslaan eerst
  `createOrderForActivity`, dan PUT stops.
  Permissie: `dossiers.manage && (orders.edit || orders.manage)`; locatie aanmaken
  `locations.create`. Gesloten dossier / niet-bewerkbare status → leesweergave.
- `LocationSelect` gaat serverzijdig zoeken via `/api/addresses/picker` (debounce 250 ms,
  AbortController + query-key race guard, min 1 teken; leeg = klantadressen/recent),
  rijke opties (naam / straat, postcode plaats / groep · klantnamen), toetsenbord volledig,
  "+ Nieuw adres "…" aanmaken" → `LocationQuickCreateDialog` → automatische selectie.

### 2.8 Presentatie
- Geld: `formatCurrency`/`euro` (`€ 1.250,00`, NBSP). Tijd: 24u. Datum: bestaande
  tenantnotatie. Zoeken/sorteren op getypte waarden.

## 3. Werkstromen en eigenaarschap (bestanden disjunct)
- F1 `TimeInput` (+tests).
- F2 `SearchableSelect` async/rijk + `LocationSelect` picker (+tests).
- W3 backend: lijst-DTO/zoeken/prijs, financials, readiness, picker (+tests).
- Lead: `FormSection` flat, `PanelHeader`, `SelfSavingPanel`, `Modal` portal, tokens.
- W1 contacten: `CustomerForm`, `CustomerDetailPage`, `CustomerContactsPanel`, hook,
  tests (+ backend levenscyclustest).
- W2 openingsuren: `OpeningHoursEditor`, css, `LocationForm` aankomstvelden,
  `LocationDetailPage`, klantpanelen Adressen/Communicatie/Facturatie koppen, tests.
- W4 dossierlijst frontend: `DossiersPage`, types, api, css, tests.
- Lead (W5): dossierdetail: registry, AttentionPanel, prijs-sectie, route-inline,
  `RouteSection` TimeInput, tests.

## 4. Definition of done
Alle regressietests uit de opdracht; `npm test`, `npm run lint`, `npx tsc -b`,
`dotnet test` groen; handmatige validatie in browser waar de omgeving het toelaat.
