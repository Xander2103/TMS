# Operationele architectuur (operational enterprise wave, 2026-07-21)

Gedeelde fundamenten waarop het planbord, het operationeel centrum, de chauffeursapp en de
dockplanning bouwen. Alles is backend-afgedwongen; React bevat geen gezaghebbende regels.

## Gestructureerde conflicten

- `PlanningConflictDto` (Modules/Planning/Dtos/TripDtos.cs) draagt naast `Code`, `Blocking`,
  `Description` en `Severity` nu ook `Category` (`ConflictCategory`: Resource, Availability,
  Qualification, Capacity, Timing, Equipment, Document, Data), `RelatedEntityType/Id`,
  `OverrideAllowed`, `RequiredPermission` en `SuggestedAction`.
- De verrijking gebeurt centraal in `PlanningConflictService.Enrich` — regelsites blijven
  code + omschrijving. "MissingData" is `Severity=Information` + `Category=Data`;
  "Overrideable" is de `OverrideAllowed`-vlag (elke blokkerende planningsregel is
  overschrijfbaar met `planning.override_restriction`).
- Dockconflicten gebruiken hetzelfde severitymodel via `DockConflictDto`
  (Modules/Warehousing) met `warehouse.conflict_override` als override-permissie.

## Conflictoverschrijvingen

Tabel `conflict_overrides` (`ConflictOverride`, Modules/Planning/Entities): `EntityType`
("Trip" of "DockAppointment"), `EntityId`, `ConflictCodes` (csv), verplichte `Reason`,
actor via `CreatedByUserId`. Geschreven in DEZELFDE SaveChanges als de actie die
overschrijft; zichtbaar in de ritdetail (`TripDetailDto.Overrides`) en geauditeerd.
Een override zonder reden wordt geweigerd (400).

## Optimistische concurrency

`Trip.Version` en `DockAppointment.Version` (Guid, `IsConcurrencyToken`). Twee lagen:
1. expliciete servicecontrole — de client stuurt de geladen versie mee; mismatch geeft
   409 `{ staleVersion: true, current: <actuele staat> }` zodat de client kan rebases;
2. EF-concurrencytoken als race-backstop (werkt op Npgsql én de SQLite-testharnas — `xmin`
   doet dat niet). Elke mutatie bumpt de versie.

## Operationele meldingen (alerts)

`operational_alerts` (`OperationalAlert`, Modules/Operations): severity (Information/
Warning/Critical), categorie, bron-regelcode, gerelateerde entiteit, `LinkPath`,
`DedupeKey` (uniek per tenant), lifecycle Active → Acknowledged → Resolved met actor en
tijdstip, `AssignedUserId`.

`AlertSyncService.SyncAsync` is een DEDUPE-UPSERT-projectie over bestaande data:
vertraagde actieve ritten (StopEta Late), afgeronde losstops zonder POD (3 dagen terug),
open kritieke incidenten, open uitvoeringsafwijkingen, onderhoud/keuring over datum en
verlopende voertuigdocumenten. Herhaald draaien dupliceert nooit (unieke dedupe-sleutel);
verdwenen condities lossen automatisch op (systeem, zonder gebruiker); terugkerende
condities heropenen dezelfde rij. De sync draait inline bij elke overview-refresh.
Onderhoud is hier datum-gebaseerd; de kilometerstand-variant blijft bij het fleetdashboard.

## Realtime-strategie: gecontroleerde polling

Bewust géén SignalR: er bestond geen push-infrastructuur en de SPA polde al unread-counts.
`/operations` pollt elke 30 s het overview-endpoint; dat endpoint is een volledige,
idempotente projectie (incl. alert-sync), dus herstel na verbindingsverlies is gewoon de
volgende poll — geen event-replay die fout kan gaan. Tenant- en permissiescoping zitten in
het endpoint zelf.

## Locatiemodel — geen nep-GPS

`LocationSource`: LiveGps, LastKnownGps, ScanLocation, StopLocation, PlannedLocation,
Unavailable. LiveGps/LastKnownGps zijn gereserveerd voor een toekomstige telematica-
integratie en worden NOOIT gesynthetiseerd. De ladder in `OperationsOverviewService`:
1. laatste custody-event met GPS (scanlocatie, met timestamp);
2. coördinaten van de laatst afgewerkte stop (masterlocatie);
3. coördinaten van de geplande volgende stop (expliciet "gepland");
4. eerlijk `Unavailable`.

## ETA-model

Hergebruik van Modules/Eta: `StopEta` met `EtaSource` (Heuristic, Provider,
DispatcherOverride) en `EtaStatus` (OnTime, AtRisk, Late), append-only historiek en de
`IRouteEstimationProvider`-naad voor bv. PTV. De frontend labelt de bron altijd
("raming" / "handmatig") — een handmatige of heuristische ETA presenteert zich nooit als
live route-intelligentie. Vertraging = CurrentEta t.o.v. het geplande venstereinde.

## Idempotentie voor mobiel/offline

Per aggregaat een client-sleutel (patroon van `ScanEvent.ClientEventId`):
- `StopStatusHistory.ClientRequestId` — stopovergangen (incl. complete/skip);
- `ProofOfDelivery.ClientRequestId` — POD-afronding (replay geeft de bestaande POD terug);
- `Incident.ClientRequestId` — incidentmelding.
Unieke gefilterde indexen per tenant; een herhaalde sleutel geeft de actuele staat als
succes terug in plaats van opnieuw te muteren. De offline wachtrij in de frontend levert
altijd in volgorde af en stopt bij netwerkfouten (zie docs/driver-app.md).

## Favorieten / recent / vastgepind

Eén tabel `user_resource_links` (`UserResourceLink`, Modules/Portal): `Kind`
(Favorite/Recent/Pinned), gesloten `EntityType`-catalogus, alleen de relatie plus een
weergavecache (label/route, ververst bij elke touch). Self-scoped endpoints onder
`/api/me/resource-links` (auth-only, zoals de rest van /api/me); bij het TONEN wordt de
view-permissie per type opnieuw gecontroleerd en vallen verwijderde doelen weg. Recents
zijn per gebruiker begrensd op 25.
