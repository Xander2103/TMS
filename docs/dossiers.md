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

## Concurrency

`TransportDossier` en `TransportOrder` implementeren `IVersionedEntity`; de
audit-interceptor bumpt het token centraal bij elke wijziging (geen handmatige bumps, geen
vergeten mutatiepad). Clients echoën `version`; een verouderd token levert **409 mét de
actuele staat** (dossier: `DossierVersionConflictException` + filter; order:
`TransportOrderOperationOutcome.VersionConflict`). `null` slaat de check over
(legacy/EDI/portaal). Frontend toont een conflictbanner met [Herladen].

## UX

`/dossiers/new` = 4 velden + tegels. Dossierdetail = kop (nummer, klant·ref·datum·entiteit,
twee statuschips, [+ Activiteit]), Aandacht-paneel met sectiesprongen, activiteitenkaarten
(één actie), contextuele secties Route/Goederen (alleen bij capabilities), prijssamenvatting
met details één klik dieper, drawers met expliciet opslaan. Route-/goederendrawers hosten de
ontlede orderformuliersecties (`features/transport-orders/components/sections/`).

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
