# Dossiers — moduledocumentatie

*Laatst bijgewerkt: 2026-08-12 (Wave 1, dossierfundament).*

## Concept

Het **dossier** is de centrale werkeenheid: één klantcase die meerdere operationele
activiteiten en meerdere commerciële onderdelen kan bevatten. De vier begrippen blijven
strikt gescheiden:

| Begrip | Entiteit | Betekenis |
|---|---|---|
| Dossier | `TransportDossier` | de klantcase (DOS-nummer, klant, datum, entiteit, status Open/Gesloten) |
| Operationele activiteit | `DossierActivity` + `ActivityType` | welk werk (distributie, kraanwerk, plateau, opslag, …) |
| Goederen / handling units | `CargoItem` / `Package` (Orders/Packages-modules) | wat er fysiek beweegt |
| Verkooplijnen | `TransportOrderPricingLine`/`ServiceLine` (Orders-module) | wat de klant betaalt |

Een transportvormige activiteit (`ActivityType.HasStops`) wordt uitgevoerd door een
gekoppelde `TransportOrder` — stops, goederen, prijs, scanning en POD leven dáár en worden
**verwezen, nooit gekopieerd**. Invariant (codereview-regel): een veld dat op
`TransportOrder` bestaat mag niet op `DossierActivity` verschijnen. Zelfstandige
activiteiten (kraanwerk ter plaatse, plateau, opslag) hebben geen opdracht; hun eigen
uitvoeringsmodellen volgen in latere waves.

## Activiteitstypes (tenantconfiguratie)

`ActivityType` is tenant-data (beheer: Parameters → Activiteitstypes,
`activity_types.view/manage`, rollen v26). Gedrag wordt uitsluitend door capability-vlaggen
gestuurd (`HasStops`, `SupportsGoods`, `PlanningRelevant`, `WarehouseRelevant`,
`AllowsDuration`, `IsQuickStart`, `IsSystemDefaultTransport`); **geen domeinlogica matcht op
`Code`** buiten de per-tenant seeder (`ActivityTypeSeeder`, lazy add-if-missing, verwijderde
types herrijzen nooit, tenantkeuze van het standaard-transporttype wordt gerespecteerd).
Een andere vervoerder configureert dus een compleet ander activiteitenmodel (container,
koeltransport, intermodaal, …) zonder broncodewijziging. Exact één actief type per tenant
draagt `IsSystemDefaultTransport` (gefilterde unieke index; wissel = clear-then-set in twee
saves).

## Aanmaak & containment

- **Eén-pagina-intake** (2026-09-02, /dossiers/new): klant + transporttype tonen meteen de
  volledige transportintake (route/planning/goederen, hergebruik van de orderformulier-secties in
  compacte modus). Een INGEVULDE intake gaat via POST /api/transport-orders met het additive veld
  ActivityTypeId: order + stops + cargo + wrapper-dossier + activiteit van het GEKOZEN type in een
  atomische save (tests OrderIntakeActivityTypeTests). Een onaangeroerde intake, Opslag en Blanco
  dossier volgen het klassieke pad hieronder.
- **Snelle aanmaak** (`POST /api/dossiers`): alleen de klant is verplicht. Datum = vandaag,
  titel = "{klant} — {datum}", entiteit stil geërfd (klantdefault → tenantdefault → geen),
  sjabloontegel (= quick-start-activiteitstype) wordt de eerste activiteit. Geblokkeerde/
  inactieve klanten krijgen geen nieuw dossier (zelfde regel als orderintake).
- **Auto-wrap**: elke `TransportOrder` die zonder `DossierId` wordt aangemaakt (EDI, portaal,
  legacy API) krijgt in dezelfde save een eigen wrapper-dossier + transportactiviteit +
  `DossierOrder`-koppeling. `OriginTransportOrderId` (gefilterde unieke index) is de
  idempotentiesleutel. Let op de volgorde in `TransportOrderService.CreateAsync`: het
  activiteitstype wordt VÓÓR enige staging geresolved, omdat de lazy seeder een eigen
  `SaveChanges` doet die anders halve orders zou flushen.
- **Backfill** (`DossierBackfillSeeder`, elke start, alle omgevingen): wikkelt historische
  orders zonder dossierkoppeling; gebruikersdossiers blijven onaangeroerd; afgeronde
  opdrachten worden gesloten wrappers; `DossierDate` draagt de historische datum
  (`CreatedAt` is het backfillmoment — bewust, zie planafwijking).

## Readiness (aandachtspunten)

`DossierReadinessService` berekent bij het lezen uitvoerbare aandachtspunten
(`ReadinessIssueDto`: code, ernst Info/Warning/Blocking, Nederlandse boodschap, paginasectie,
fase Planning/Warehouse/Execution/Commercial/Invoice). Nooit persistente statuswaarden, nooit
nieuwe `TransportOrderStatus`-leden. Wave-1-regels: `activity.none`, `route.order_missing`,
`order.confirm.stops` (Blocking — exact de bestaande bevestigingspoort, vóóraf getoond),
`route.date_missing`, `pricing.incomplete` (uit de dekking-snapshot), `pricing.none`.
Latere waves voegen producenten toe zonder schemawijziging. Dashboardteller:
`GET /api/dossiers/attention-count` (structurele benadering; de prijsdimensie wordt
querybaar in Wave 2 via de getypte dekkingskolom).

UX-sprint 2026-09-09: elk aandachtspunt draagt nu een `field` dat de pagina kan focussen:
`activity.none` → `activity.add`; `route.order_missing` → sectie `route`, `stops.loading`
(de routesectie biedt inline invoer die de opdracht bij opslaan aanmaakt);
`order.confirm.stops` → `stops.loading` als laden ontbreekt, anders `stops.unloading`;
`route.date_missing` → `stops.plannedFrom`; `pricing.*` → `price`. Nieuwe regel
`pricing.missing` (Warning, sectie prijs, fase Commercial, "{nr}: verkoopprijs ontbreekt." — sinds 2026-09-21; planningsdatum idem: "{nr}: planningsdatum ontbreekt."):
gekoppelde opdracht in Draft/Submitted/Confirmed die niet geprijsd is volgens de ene
definitie `OrderPricingState.IsPricedExpression` (provenance, hardening 2026-09-10:
`PriceIsManual || (PricingSource == OneOff && OneOffFixedAmount != null) || AgreedPrice > 0`;
€ 0 is een geldige prijs wanneer hij expliciet werd afgesproken; de motor schrijft 0 zonder
tellende regels en dát leest als "niet geprijsd"). Onderdrukt voor een opdracht waarvoor
`pricing.incomplete` al verschijnt (zelfde veld, specifiekere boodschap). `pricing.none` blijft
Info voor een transportactiviteit zonder opdracht; een dossier met enkel zelfstandige
activiteiten (opslag, kraanwerk) krijgt géén prijsaandachtspunt — die hebben geen prijsdrager
(productbeslissing, zie `docs/ux-sprint/2026-09-10-hardening.md`).
De attention-count telt zo'n ongeprijsde open opdracht mee (nog één SQL-statement).
Elke opdrachtregel draagt `transportOrderId`, `route.order_missing` draagt `activityId`, zodat de
dossierpagina bij meerdere transportopdrachten de juiste editor kiest (`DossierOrderSwitcher`).
Lijst-DTO: `customerReference`, `customerNumber`, `pricedOrderCount`, `agreedPriceTotal`
(null zolang niets geprijsd is; bij `pricedOrderCount < orderCount` toont de UI "n/m geprijsd");
zoeken matcht ook referentie, klantnaam en klantnummer.
Detail-financials: `pricedOrderCount` naast `agreedOrderTotal`; `orders[].isPriced` per opdracht.

## Concurrency

`TransportDossier` en `TransportOrder` implementeren `IVersionedEntity`; de
audit-interceptor bumpt het token centraal bij elke wijziging (geen handmatige bumps, geen
vergeten mutatiepad). Clients echoën `version`; een verouderd token levert **409 mét de
actuele staat** (dossier: `DossierVersionConflictException` + filter; order:
`TransportOrderOperationOutcome.VersionConflict`). `null` slaat de check over
(legacy/EDI/portaal). Frontend toont een conflictbanner met [Herladen].

## UX

`/dossiers/new` = een-pagina-intake: klant, transporttype-tegels (echte radio's, accent-tokens,
Blanco dossier als bescheiden link eronder), referentie/datum en - zodra een transporttype is
gekozen - route (LocationSelect met klantadressen + vrije adressen, tijdvensters, tijdseis),
goederenregels en opmerkingen. Typewissel bewaart gedeelde velden; alleen echte dataverlies vraagt
bevestiging. De snelle minimale flow blijft: klant + type volstaat. Dossierdetail = kop (nummer, klant·ref·datum·entiteit,
twee statuschips, [+ Activiteit]), Aandacht-paneel met sectiesprongen, activiteitenkaarten
(één actie), contextuele secties Route/Goederen (alleen bij capabilities), prijssamenvatting
met details één klik dieper, drawers met expliciet opslaan. Route-/goederendrawers hosten de
ontlede orderformuliersecties (`features/transport-orders/components/sections/`).

## Documenten (D6, master sprint 2026-09-21)

Eén entiteit, één bestand: `TransportOrderDocument` hangt aan het dossier als geheel
(`TransportOrderId = null`, scope `Dossier`) of aan één opdracht (scope `Order`). Voor een
opdrachtdocument is `DossierId` altijd het **eigenaarsdossier** van de opdracht
(`OwningDossierResolver`: wrapper eerst, anders de oudste koppeling) — gezet bij aanmaak en
meegenomen bij `orders` koppelen/ontkoppelen en in de wrapper-backfill. Check-constraint: minstens
één van beide gezet. Lijst én telling delen één definitie (`DossierDocumentQuery.ForDossier`), dus
elk document telt precies één keer; per activiteit tellen alleen de eigen documenten van de opdracht.

- `GET/POST /api/dossiers/{id}/documents` (`dossiers.view` / `dossiers.manage`); de opdrachtlijst
  `GET /api/transport-orders/{id}/documents` blijft alleen de EIGEN documenten geven.
- Vlakke routes `api/order-documents/{id}`: het attribuut is alleen de grove buitenpoort; de scope
  van de rij bepaalt het recht, op één plek (`OrderDocumentAccessResolver`). Dossierdocument:
  downloaden `dossiers.view|manage`, schrijven `dossiers.manage`; opdrachtdocument: zoals voorheen.
  De service draait in exact de geautoriseerde scope — via een opdrachtpad is een dossierdocument
  onvindbaar.
- `POST /api/order-documents/{id}/move` `{ targetTransportOrderId }`: alleen de koppeling wijzigt
  (bestand, naam, type en klantzichtbaarheid blijven), doel moet in hetzelfde dossier zitten,
  schrijfrecht op bron- én doelscope, idempotent, audit `Moved`.
- Gesloten dossier: schrijven op dossierniveau (en aanmaken/verplaatsen via het dossier) wordt
  geweigerd met dezelfde melding als elke andere dossiermutatie; lezen blijft mogelijk.
- Klantportaal: scope impliceert nooit zichtbaarheid. Een dossierdocument verschijnt alleen met
  `CustomerVisible` + bestand + `dossier.CustomerId` = klant van de portaalgebruiker; de download
  controleert hetzelfde predicaat opnieuw. Chauffeurs: geen toegang (ongewijzigd).

**Uitgegeven transportdocumenten** (`IssuedTransportDocument`): uniek eigen nummer per tenant +
soort + jaar — `CMR-2026-00001`, `LB-…` (leveringsbon), `WB-…` (werkbon) — via een sequentierij met
concurrency-token (het `InvoiceNumberService`-patroon; een `TenantSettings`-teller kan geen reeks
per soort en jaar dragen) en unieke indexen op (`TenantId`,`DocumentNumber`) en
(`TenantId`,`RequestId`). Zelfde `requestId` = zelfde record, ook bij twee gelijktijdige identieke
verzoeken. Extern nummer wordt bewaard zoals ingevoerd, nooit overschreven. Kraanwerk ter plaatse
krijgt een werkbon en nooit een CMR/leveringsbon; een CMR/leveringsbon vereist een laad- of losstop.
`GET /api/issued-transport-documents/{id}/pdf` rendert via de bestaande renderer met document-,
dossier- en opdrachtnummer (+ "Extern nr."); de nummerloze stream-endpoints blijven ongewijzigd.

## API-overzicht

CRUD + `close/reopen/orders/relations` (ongewijzigd), plus: `PUT {id}/legal-entity`
(geauditeerd oud→nieuw), `POST {id}/activities`, `PUT/DELETE {id}/activities/{aid}`,
`POST {id}/activities/reorder`, `POST {id}/activities/{aid}/create-order` (conceptopdracht
ín het dossier voor een bestaande transportactiviteit), `GET attention-count`. Alle mutaties
`dossiers.manage`; versie-token optioneel op elke mutatie.

## Tests

`Api.Tests/Dossiers/`: `ActivityTypeSeederTests`, `ActivityTypeServiceTests`,
`DossierBackfillTests`, `DossierFoundationTests` (fastcreate/activiteiten/kraan+plateau/
opslag-only/auto-wrap/409), `DossierServiceTests`. Frontend: `features/dossiers/__tests__/`.
