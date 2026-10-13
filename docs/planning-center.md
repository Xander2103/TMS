# Planbord / Planning Center (`/planning-center`)

Dispatcher-werkruimte met drie zones: ongepland werk, tijdlijnbord en middelen.

## Read models (`PlanningBoardService`, `GET /api/planning-board`)

Alle projecties zijn gebatcht (vast aantal queries, geen N+1 op entiteitsdata) en begrensd
op 31 dagen / 500 ritten / 500 middelen per soort.

- **Board** (`GET /api/planning-board?from&to`): ritten met tijden, chauffeur/voertuig/
  oplegger, ordertelling, stops, routesamenvatting (eerste laad- → laatste losstad),
  ladinggewicht/-volume t.o.v. capaciteit (zelfde bron als de conflictengine: oplegger
  indien toegewezen, anders voertuig), conflicttellingen per severity en de
  concurrency-versie. Conflicttellingen worden live per rit herberekend door de bestaande
  engine (zelfde correctheid-eerst-gedrag als de bestaande riltlijst).
- **Ongepland** (`GET /api/planning-board/unplanned-orders`): Confirmed/Submitted-orders
  die niet op een actieve rit staan; server-side zoeken/filteren (klant, status,
  prioriteit, datum, alleen-aandachtspunten), gepagineerd (max 100), urgentie eerst.
  Aandachtsbadges: SubmittedReview, NoStops, MissingWeight, MissingVolume,
  AppointmentRequired.
- **Middelen** (`GET /api/planning-board/resources?from&to`): chauffeurs (beschikbaarheid,
  afwezigheden, kwalificatieblokkades/-waarschuwingen via de bestaande statuscalculator,
  vast voertuig — vehicle-side SoT), voertuigen/opleggers (operationele status, capaciteit,
  uitrusting, ADR, onderhoud/keuring over datum, toewijzingen in de periode).

## Gerichte commando's (drag-and-drop-mutaties)

Op `api/trips/{id}`: `POST orders` (incrementeel toevoegen), `DELETE orders/{orderId}`,
`POST orders/reorder`, `PUT driver|vehicle|trailer`, `POST reschedule`,
`POST validate-assignment` (dry-run voor drag-over-feedback). Gemeenschappelijke kern
(`TripService.ApplyTargetedAsync`):

1. status-guard: alleen Draft en Planned;
2. versiecontrole (409 + actuele staat bij mismatch);
3. mutatie + referentievalidatie;
4. Planned-ritten worden volledig HER-gevalideerd door de conflictengine; blokkerende
   conflicten vereisen `planning.override_restriction` + verplichte reden → rij in
   `conflict_overrides`;
5. planning-entry-sync + costing-herberekening + versiebump in één SaveChanges;
6. audit (OrdersAssigned, OrderRemoved, OrdersReordered, DriverChanged, VehicleChanged,
   TrailerChanged, Rescheduled) en notificaties.

Ordertoewijzing vereist bovendien `orders.assign` of `orders.manage` (zoals voorheen).
Een Planned rit claimt toegevoegde orders direct (Confirmed → Planned); verwijderen geeft
ze vrij. De laatste order verwijderen creëert het blokkerende `NoOrders`-conflict.

## Notificaties (gat gedicht)

Bij een Planned rit horen chauffeurs het voortaan óók bij herbezetting: de oude chauffeur
krijgt "niet meer toegewezen", de nieuwe "toegewezen", en tijdstip-/voertuig-/oplegger-
wijzigingen sturen `trip_changed` — telkens precies één melding per betrokkene.

## Frontend-flow

Elke drop roept een backend-commando aan; er wordt nooit alleen frontend-state bewaard.
Bij afwijzing: alleen de getroffen zones herladen (request-key per zone), exacte
gestructureerde conflicten in de dialoog, en (permissiebewust) een override-met-reden-
retry. `staleVersion`-409's verversen het bord met een duidelijke melding. Toegankelijk
alternatief voor slepen: Enter/"Plan in…" opent een ritkiezer. Filterstate wordt lokaal
onthouden (`ts.planningCenter.filters`).

Sneltoets: `g p` opent het planbord (zie het sneltoetsenregister).
